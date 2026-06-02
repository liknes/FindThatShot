using System.IO;
using System.Runtime.InteropServices;

namespace VideoArchiveManager.App.Helpers.Player;

/// <summary>
/// Minimal P/Invoke surface for libmpv's client API (the subset we need to
/// embed mpv in a child window and drive basic transport). This is the
/// experimental GPU-rendered player path that replaces FFME's CPU
/// WriteableBitmap rendering for full-res 4K60 playback.
///
/// <para>
/// The native dependency is <c>libmpv-2.dll</c> (a self-contained build that
/// statically links FFmpeg). It is NOT bundled in the repo; drop it into
/// <c>tools/mpv/</c> next to the existing <c>tools/ffmpeg/</c> folder. A
/// shinchiro "mpv-dev" Windows build provides it. <see cref="Register"/>
/// wires a DllImportResolver so the loader finds it there.
/// </para>
///
/// <para>
/// All strings cross the boundary as UTF-8 (libmpv's encoding) via
/// <see cref="UnmanagedType.LPUTF8Str"/> — important because clip paths can
/// contain non-ASCII characters.
/// </para>
/// </summary>
internal static class MpvInterop
{
    private const string Lib = "libmpv-2.dll";

    // mpv_format values we use (from client.h).
    public const int MPV_FORMAT_FLAG = 3;
    public const int MPV_FORMAT_INT64 = 4;
    public const int MPV_FORMAT_DOUBLE = 5;

    // mpv_event_id values we care about (from client.h).
    public const int MPV_EVENT_NONE = 0;
    public const int MPV_EVENT_SHUTDOWN = 1;

    private static bool _resolverRegistered;

    /// <summary>
    /// Point the native loader at <c>tools/mpv/libmpv-2.dll</c>. Mirrors how
    /// App.xaml.cs points FFME at <c>tools/ffmpeg/</c>. Safe to call repeatedly.
    /// </summary>
    public static void Register(string mpvDirectory)
    {
        if (_resolverRegistered) return;
        _resolverRegistered = true;

        NativeLibrary.SetDllImportResolver(typeof(MpvInterop).Assembly, (name, _, _) =>
        {
            if (!name.StartsWith("libmpv", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var full = Path.Combine(mpvDirectory, Lib);
            return File.Exists(full) && NativeLibrary.TryLoad(full, out var handle)
                ? handle
                : IntPtr.Zero;
        });
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option(
        IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref long data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(
        IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    // Flag-typed read (MPV_FORMAT_FLAG marshals as a C int, 0/1). Needed for
    // boolean properties like "pause": mpv refuses to convert a flag to a
    // double, so the double overload above returns an error for them and the
    // value can never be read. Same native entry point, different out type.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_flag(
        IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out int data);

    // char** args, NULL-terminated. Marshalled by hand in MpvPlayer.Command.
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
}
