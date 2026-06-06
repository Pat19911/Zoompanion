using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZoombiniHelper.UI;

/// <summary>
/// Win32 plumbing to keep an always-on-top overlay actually on top, even
/// when a fullscreen-style game grabs focus and tries to clobber the
/// Z-order. Call <see cref="Reassert"/> from a tick timer.
///
/// Companion to <see cref="ToolWindowExStyles"/>: the constructor extended
/// styles set the initial flags, this enforcer renews them after the game
/// strips them.
/// </summary>
public static class TopmostEnforcer
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOWNA = 8; // show without activation

    public static void Reassert(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        if (!IsWindowVisible(handle)) ShowWindow(handle, SW_SHOWNA);
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

/// <summary>Extended window style flags for a tool-window-style overlay that
/// stays topmost without taking focus or appearing in the taskbar.</summary>
public static class ToolWindowExStyles
{
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST    = 0x00000008;

    public const int All = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
}
