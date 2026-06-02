using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace VideoArchiveManager.App.Helpers.Player;

/// <summary>
/// Thin managed wrapper around an mpv instance embedded into a child window.
/// Exposes only the transport surface MainWindow needs (open / play / pause /
/// stop / seek / position / duration), mirroring the shape of the FFME API it
/// stands in for so the code-behind wiring stays parallel.
///
/// <para>
/// mpv renders with <c>vo=gpu</c> directly into the supplied window handle and
/// clears its framebuffer to black, so letterbox bars are black and there are
/// no white flashes — the whole reason we moved off the LibVLCSharp VideoView.
/// </para>
///
/// <para>
/// libmpv's client API is thread-safe, so the UI thread can poll properties
/// while a background pump drains the event queue. Draining matters: if events
/// are never read the queue backs up and playback can stall.
/// </para>
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private IntPtr _ctx;
    private Thread? _eventPump;
    private volatile bool _running;

    public bool IsInitialized => _ctx != IntPtr.Zero;

    /// <summary>
    /// Create the mpv context, apply options, bind it to <paramref name="hwnd"/>
    /// and initialize. Must be called once, before any playback command.
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        if (_ctx != IntPtr.Zero) return;

        _ctx = MpvInterop.mpv_create();
        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create failed (is libmpv-2.dll present in tools/mpv/?).");

        // Options that must be set BEFORE mpv_initialize.
        // Apply mpv's built-in "fast" profile FIRST: bilinear scaling, no
        // debanding/dither/sigmoid, cheap color handling. The default vo=gpu
        // render path runs expensive per-frame shaders (HQ polyphase scaler +
        // dither + D-Log tone curve) that a weak GPU (e.g. GeForce GT 1030)
        // can't sustain at 4K60 — playback then runs ~2x slow with no audio
        // clock to drop against. "fast" slashes that render cost.
        MpvInterop.mpv_set_option_string(_ctx, "profile", "fast");

        MpvInterop.mpv_set_option_string(_ctx, "vo", "gpu");
        // Force a Direct3D11 GPU context + GPU decode. Under a WPF HwndHost
        // child window mpv's default context can land on an OpenGL/ANGLE path
        // that presents badly through DWM redirection (jitter + slowness);
        // d3d11 is the reliable Windows path and pairs with d3d11va for
        // zero-copy hardware decode.
        MpvInterop.mpv_set_option_string(_ctx, "gpu-api", "d3d11");
        MpvInterop.mpv_set_option_string(_ctx, "gpu-context", "d3d11");
        MpvInterop.mpv_set_option_string(_ctx, "hwdec", "auto");
        MpvInterop.mpv_set_option_string(_ctx, "keep-open", "yes");   // hold last frame at EOF instead of going idle
        MpvInterop.mpv_set_option_string(_ctx, "osc", "no");          // no mpv on-screen controller
        MpvInterop.mpv_set_option_string(_ctx, "osd-level", "0");

        // Do NOT auto-load the sibling DJI ".SRT" as an on-screen subtitle. That
        // file is drone telemetry (FrameCnt / iso / shutter / GPS …) the app
        // consumes for the flight-path map — burning it over the whole frame is
        // not what we want in the review player.
        MpvInterop.mpv_set_option_string(_ctx, "sub-auto", "no");
        MpvInterop.mpv_set_option_string(_ctx, "sid", "no");

        // Pace presentation to the display refresh. The source is ~59.94 fps and
        // typical displays are ~59.95 Hz, so this is essentially a 1:1 lock that
        // removes the judder you get from presenting frames off-cadence (the
        // reason mpv looked jerkier than the Media Foundation based Windows
        // players, which vsync-pace their presentation). interpolation stays OFF
        // so we don't add per-frame GPU cost on a weak card. NOTE: this only
        // works because profile=fast made the render real-time — an earlier
        // attempt at display-resample on the slow HQ render path starved the
        // frame queue and played in slow motion.
        MpvInterop.mpv_set_option_string(_ctx, "video-sync", "display-resample");
        MpvInterop.mpv_set_option_string(_ctx, "interpolation", "no");

        // DIAGNOSTIC: keep a quiet mpv log (warnings/errors only) so real
        // problems still surface, without the per-frame shader/format flood
        // that the earlier all=v level produced (tens of thousands of lines
        // per play, which itself added overhead). Remove once dialed in.
        try
        {
            var logDir = VideoArchiveManager.Core.Configuration.AppSettings.DefaultBaseDirectory;
            System.IO.Directory.CreateDirectory(logDir);
            var logPath = System.IO.Path.Combine(logDir, "mpv.log");
            MpvInterop.mpv_set_option_string(_ctx, "log-file", logPath);
            MpvInterop.mpv_set_option_string(_ctx, "msg-level", "all=warn");
        }
        catch
        {
            // Logging is best-effort; never block player init on it.
        }

        MpvInterop.mpv_set_option_string(_ctx, "input-default-bindings", "no");
        MpvInterop.mpv_set_option_string(_ctx, "input-vo-keyboard", "no");
        MpvInterop.mpv_set_option_string(_ctx, "background", "#000000"); // letterbox / idle = black
        MpvInterop.mpv_set_option_string(_ctx, "force-window", "no");

        // Bind mpv to our child window (wid takes an int64 of the HWND).
        var wid = hwnd.ToInt64();
        MpvInterop.mpv_set_option(_ctx, "wid", MpvInterop.MPV_FORMAT_INT64, ref wid);

        if (MpvInterop.mpv_initialize(_ctx) < 0)
        {
            MpvInterop.mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
            throw new InvalidOperationException("mpv_initialize failed.");
        }

        _running = true;
        _eventPump = new Thread(EventPump) { IsBackground = true, Name = "mpv-events" };
        _eventPump.Start();
    }

    public void Load(string path)
    {
        if (_ctx == IntPtr.Zero) return;
        Command("loadfile", path);
        SetPaused(false);
    }

    public void Play() => SetPaused(false);

    public void Pause() => SetPaused(true);

    public void SetPaused(bool paused)
    {
        if (_ctx == IntPtr.Zero) return;
        MpvInterop.mpv_set_property_string(_ctx, "pause", paused ? "yes" : "no");
    }

    public void Stop()
    {
        if (_ctx == IntPtr.Zero) return;
        Command("stop");
    }

    public void SeekAbsolute(double seconds)
    {
        if (_ctx == IntPtr.Zero) return;
        Command("seek", seconds.ToString("F3", CultureInfo.InvariantCulture), "absolute");
    }

    public double GetTimePosition() => GetDouble("time-pos");

    public double GetDuration() => GetDouble("duration");

    public bool GetPaused()
    {
        if (_ctx == IntPtr.Zero) return true;
        return MpvInterop.mpv_get_property(_ctx, "pause", MpvInterop.MPV_FORMAT_DOUBLE, out var d) >= 0 && d != 0;
    }

    private double GetDouble(string name)
    {
        if (_ctx == IntPtr.Zero) return 0;
        return MpvInterop.mpv_get_property(_ctx, name, MpvInterop.MPV_FORMAT_DOUBLE, out var value) >= 0
            ? value
            : 0;
    }

    // Marshal a NULL-terminated char** of UTF-8 strings for mpv_command.
    private void Command(params string[] args)
    {
        if (_ctx == IntPtr.Zero) return;

        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
                ptrs[i] = Utf8ToHGlobal(args[i]);
            ptrs[args.Length] = IntPtr.Zero;

            var pinned = GCHandle.Alloc(ptrs, GCHandleType.Pinned);
            try
            {
                MpvInterop.mpv_command(_ctx, pinned.AddrOfPinnedObject());
            }
            finally
            {
                pinned.Free();
            }
        }
        finally
        {
            foreach (var p in ptrs)
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }
    }

    private static IntPtr Utf8ToHGlobal(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    private void EventPump()
    {
        while (_running)
        {
            var evPtr = MpvInterop.mpv_wait_event(_ctx, 0.1);
            if (evPtr == IntPtr.Zero) continue;

            var eventId = Marshal.ReadInt32(evPtr);
            if (eventId == MpvInterop.MPV_EVENT_SHUTDOWN)
                break;
            // Other events are intentionally discarded for the spike — draining
            // the queue is the point, not reacting to individual events.
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _eventPump?.Join(500); } catch { /* shutting down */ }
        _eventPump = null;

        if (_ctx != IntPtr.Zero)
        {
            MpvInterop.mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
    }
}
