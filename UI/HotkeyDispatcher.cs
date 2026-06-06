using System.Runtime.InteropServices;

namespace ZoombiniHelper.UI;

/// <summary>
/// Polled global hotkey dispatcher. Each registered key has its own debounce
/// timer so a held key only fires once per <see cref="DefaultDebounce"/> window.
/// Designed to be called every UI tick (e.g. 200 ms).
///
/// Uses <c>GetAsyncKeyState</c> instead of the form's KeyDown event because
/// the game window has focus while the user plays — our form never sees the
/// keystrokes otherwise.
/// </summary>
public sealed class HotkeyDispatcher
{
    public const int VK_F1  = 0x70;
    public const int VK_F12 = 0x7B;
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(1);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private record Entry(int VirtualKey, TimeSpan Debounce, Action Handler)
    {
        public DateTime LastFired = DateTime.MinValue;
    }

    private readonly List<Entry> _entries = new();

    /// <param name="virtualKey">Win32 VK_* constant.</param>
    /// <param name="handler">Called on key-down edge after debounce.</param>
    /// <param name="debounce">Min interval between consecutive fires; default 1 s.</param>
    public void Register(int virtualKey, Action handler, TimeSpan? debounce = null)
        => _entries.Add(new Entry(virtualKey, debounce ?? DefaultDebounce, handler));

    /// <summary>Call once per tick. Walks all registered hotkeys and fires
    /// their handler if the key is currently pressed and the debounce has expired.</summary>
    public void Poll()
    {
        var now = DateTime.UtcNow;
        foreach (var e in _entries)
        {
            bool pressed = (GetAsyncKeyState(e.VirtualKey) & 0x0001) != 0;
            if (!pressed) continue;
            if (now - e.LastFired < e.Debounce) continue;
            e.LastFired = now;
            e.Handler();
        }
    }
}
