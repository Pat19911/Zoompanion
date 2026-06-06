using System.Runtime.InteropServices;

namespace ZoombiniHelper.Win32;

/// <summary>
/// Locates the Zoombinis game window. Currently unused — kept around in
/// case a future renderer needs to know where the game window sits on
/// screen. The Stone Rise minimap doesn't need this anymore: it uses the
/// engine's own internal coord space (read directly from process memory).
/// </summary>
public static class GameWindowFinder
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    public readonly record struct WindowCandidate(IntPtr HWnd, string Title, int Width, int Height);

    /// <summary>Enumerate every visible top-level window owned by
    /// <paramref name="targetPid"/>. Useful both for picking the game
    /// window and for showing diagnostic info when the wrong one was chosen.</summary>
    public static List<WindowCandidate> EnumerateCandidates(int targetPid)
    {
        var list = new List<WindowCandidate>();
        var sb = new System.Text.StringBuilder(256);
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != (uint)targetPid) return true;
            if (!IsWindowVisible(hWnd)) return true;
            if (!GetClientRect(hWnd, out var r)) return true;
            sb.Clear();
            GetWindowText(hWnd, sb, sb.Capacity);
            list.Add(new WindowCandidate(hWnd, sb.ToString(), r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Find the most likely game window: prefer one whose title
    /// contains "zoombini" (case-insensitive), then fall back to the
    /// largest visible window. The title-based pick avoids selecting a
    /// launcher background or Wine container that happens to be owned
    /// by the same process and may have a larger client area than the
    /// actual game window.</summary>
    public static IntPtr? FindMainWindow(int targetPid)
    {
        var candidates = EnumerateCandidates(targetPid);
        if (candidates.Count == 0) return null;

        var byTitle = candidates
            .Where(c => c.Title.Contains("zoombini", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => (long)c.Width * c.Height)
            .FirstOrDefault();
        if (byTitle.HWnd != IntPtr.Zero) return byTitle.HWnd;

        return candidates
            .OrderByDescending(c => (long)c.Width * c.Height)
            .First().HWnd;
    }

    /// <summary>Returns the client-area rectangle in SCREEN coordinates.</summary>
    public static System.Drawing.Rectangle? GetClientScreenRect(IntPtr hWnd)
    {
        if (!GetClientRect(hWnd, out var r)) return null;
        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref origin)) return null;
        return new System.Drawing.Rectangle(origin.X, origin.Y, r.Right - r.Left, r.Bottom - r.Top);
    }

}
