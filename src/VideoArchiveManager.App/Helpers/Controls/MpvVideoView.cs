// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VideoArchiveManager.App.Helpers.Player;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Hosts an mpv instance inside a Win32 child window via <see cref="HwndHost"/>.
/// mpv renders GPU video straight into this window (vo=gpu) and clears to
/// black, so we get full-res 4K60 playback with black letterbox bars and no
/// white flashes — unlike FFME's CPU WriteableBitmap path (too slow for 4K60)
/// and the old LibVLCSharp VideoView (white-bar bleed).
///
/// <para>
/// Trade-off of the child-window approach: the video surface is a Win32 child,
/// so WPF can't composite over it (no overlay controls, no opacity animation on
/// the picture itself). That's fine here — the transport bar sits in a separate
/// row below the video, never on top of it.
/// </para>
/// </summary>
public sealed class MpvVideoView : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    private IntPtr _hwndChild;

    /// <summary>The mpv player bound to this window, available after the
    /// handle is built. Null before the control is loaded / after unload.</summary>
    public MpvPlayer? Player { get; private set; }

    /// <summary>Raised once the child window exists and mpv is initialized,
    /// so the host can start driving playback.</summary>
    public event EventHandler? PlayerReady;

    /// <summary>Surfaces an initialization failure (e.g. libmpv missing or
    /// mpv_initialize failed) so the host can degrade gracefully.</summary>
    public event EventHandler<string>? PlayerFailed;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // A blank "static" child window — predefined class, no registration,
        // and mpv paints over it entirely (clearing to black) so its own
        // background never shows.
        _hwndChild = CreateWindowEx(
            0, "static", string.Empty,
            WS_CHILD | WS_VISIBLE,
            0, 0, 0, 0,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        try
        {
            Player = new MpvPlayer();
            Player.Initialize(_hwndChild);
            PlayerReady?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Player?.Dispose();
            Player = null;
            PlayerFailed?.Invoke(this, ex.Message);
        }

        return new HandleRef(this, _hwndChild);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Player?.Dispose();
        Player = null;

        if (_hwndChild != IntPtr.Zero)
        {
            DestroyWindow(_hwndChild);
            _hwndChild = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
