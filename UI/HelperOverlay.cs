using System.Drawing;
using System.Windows.Forms;
using ZoombiniHelper.Bubblewonder;
using ZoombiniHelper.Diagnostics;
using ZoombiniHelper.Drag;
using ZoombiniHelper.Localization;
using ZoombiniHelper.Puzzles;
using ZoombiniHelper.UI.Rendering;
using ZoombiniHelper.Win32;

namespace ZoombiniHelper.UI;

/// <summary>
/// Always-on-top helper overlay for the v2 (PE32) Zoombinis game. Each
/// concern lives in its own class — the overlay just wires them together
/// and orchestrates the per-tick update.
/// </summary>
public sealed class HelperOverlay : Form
{
    // Per-tick orchestration state
    private readonly ProcessAttacher  _attacher      = new();
    private readonly PuzzleManager    _puzzleManager = PuzzleRegistry.CreateManager();
    private readonly OverlayRenderer  _renderer;
    private readonly HotkeyDispatcher _hotkeys       = new();
    private readonly WindowDragHandler _windowDrag;
    private readonly DragHistory _dragHistory = new();
    private readonly BubblewonderTracker _bubbleTracker = new();

    // UI controls
    private readonly Label _title    = new();
    private readonly Label _body     = new();
    private readonly Panel _bodyScroll = new();
    private readonly GridPanel _grid = new();
    private readonly Label _footer   = new();
    private readonly Label _closeBtn = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private System.Windows.Forms.Timer? _footerResetTimer;
    private bool _closing;
    private PuzzleId _currentPuzzle = PuzzleId.None;

    // Localized — resolved on each access so a runtime language switch shows.
    private static string DefaultFooter => Loc.T("overlay.footer");
    private static readonly string ErrorLogPath = Path.Combine(Path.GetTempPath(), "zoombini-helper.log");

    public HelperOverlay()
    {
        _renderer = new OverlayRenderer(
            fallback: new PuzzleDisplayRenderer(),
            specific: new IPuzzleRenderer[]
            {
                new CliffRenderer(),
                new CavesRenderer(),
                new PizzaRenderer(),
                new FleensRenderer(),
                new MudballRenderer(),
                new HotelRenderer(),
                new StoneRiseRenderer(),
                new CaptainCajunRenderer(),
                new BubblewonderRenderer(_bubbleTracker),
            });
        _windowDrag = new WindowDragHandler(this);

        BuildForm();
        BuildLabels();
        BuildCloseButton();
        WireMouseAndKeys();
        WireHotkeys();

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // --- form / label setup ---

    private void BuildForm()
    {
        Text = "Zoombini Helper";
        ClientSize = new Size(500, 800);
        TopMost = true;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(18, 22, 36);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);
        ShowInTaskbar = false;
        Padding = new Padding(8);
        DoubleBuffered = true;
        KeyPreview = true;
    }

    private void BuildLabels()
    {
        _title.Dock = DockStyle.Top;
        _title.Height = 28;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(140, 220, 255);
        _title.Text = Loc.T("overlay.title.start");

        // Wrap the body label in a scroll panel so long text stays reachable
        // even when the helper window is shorter than the content (Mudball at
        // Diff 4 with many active targets routinely overruns 500 px height).
        _body.AutoSize = true;
        // Breite auf die Panel-Breite kappen → lange Zeilen brechen UM (wachsen nach
        // unten) statt nach rechts zu laufen. Ohne Kappung musste man horizontal
        // scrollen, um die ganze Anweisung zu sehen (User-Wunsch 2026-06-05). Der
        // konkrete Wert wird in UpdateBodyWrapWidth() aus der Panel-Breite gesetzt
        // (minus vertikaler Scrollbar, damit kein horizontaler Scroll entsteht).
        _body.MaximumSize = new Size(1, 0);
        _body.TextAlign = ContentAlignment.TopLeft;
        _body.Font = new Font("Consolas", 10.5f);
        _body.Location = new Point(0, 0);

        _bodyScroll.Dock = DockStyle.Fill;
        _bodyScroll.AutoScroll = true;
        _bodyScroll.Padding = new Padding(0);
        _bodyScroll.Controls.Add(_body);
        // Wrap-Breite bei jeder Panel-Größenänderung (Fenster resize / Dock-Layout)
        // nachziehen. SizeChanged feuert NICHT beim Ein-/Ausblenden der vertikalen
        // Scrollbar (das ändert nur ClientSize, nicht Size) → kein Oszillieren.
        _bodyScroll.SizeChanged += (_, _) => UpdateBodyWrapWidth();
        UpdateBodyWrapWidth();

        // Grid panel: starts hidden (height 0). Each renderer can set its
        // own height via OverlayLabels.GridHeight; we apply that on tick.
        _grid.Dock = DockStyle.Bottom;
        _grid.Height = 0;
        _grid.Visible = false;

        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 18;
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.Font = new Font("Segoe UI", 8.0f);
        _footer.ForeColor = Color.FromArgb(120, 140, 170);
        _footer.Text = DefaultFooter;

        Controls.Add(_bodyScroll);
        Controls.Add(_grid);
        Controls.Add(_footer);
        Controls.Add(_title);
    }

    /// <summary>Setzt die maximale Body-Breite auf die Panel-Breite minus vertikaler
    /// Scrollbar → der Text bricht innerhalb des Fensters um, statt nach rechts zu
    /// laufen (kein horizontaler Scroll mehr). Basiert auf <c>Width</c> (stabil), NICHT
    /// auf <c>ClientSize.Width</c> (das schrumpft, wenn die Scrollbar erscheint →
    /// würde oszillieren). Die Scrollbar-Breite wird IMMER reserviert, damit auch bei
    /// langem Text (vertikale Scrollbar sichtbar) nichts horizontal überläuft.</summary>
    private void UpdateBodyWrapWidth()
    {
        int w = _bodyScroll.Width - SystemInformation.VerticalScrollBarWidth - 4;
        _body.MaximumSize = new Size(Math.Max(50, w), 0);
    }

    private void BuildCloseButton()
    {
        _closeBtn.Text = "×";
        _closeBtn.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
        _closeBtn.ForeColor = Color.FromArgb(220, 100, 100);
        _closeBtn.TextAlign = ContentAlignment.MiddleCenter;
        _closeBtn.Size = new Size(28, 28);
        _closeBtn.Cursor = Cursors.Hand;
        _closeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _closeBtn.Location = new Point(ClientSize.Width - 36, 4);
        _closeBtn.Click += (_, _) => Close();
        _closeBtn.MouseEnter += (_, _) => _closeBtn.ForeColor = Color.FromArgb(255, 80, 80);
        _closeBtn.MouseLeave += (_, _) => _closeBtn.ForeColor = Color.FromArgb(220, 100, 100);
        Controls.Add(_closeBtn);
        _closeBtn.BringToFront();
    }

    private void WireMouseAndKeys()
    {
        _windowDrag.AttachTo(this, _title, _body, _footer, _bodyScroll);
        foreach (Control ctl in new Control[] { this, _title, _body, _footer, _bodyScroll })
            ctl.DoubleClick += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private void WireHotkeys()
    {
        _hotkeys.Register(HotkeyDispatcher.VK_F12, DumpDiagnostic);
        _hotkeys.Register(HotkeyDispatcher.VK_F1,  CycleActiveHints,
                          debounce: TimeSpan.FromMilliseconds(300));
    }

    /// <summary>F1 dispatches to whichever puzzle is currently detected.
    /// Renderers opt into hint cycling by implementing
    /// <see cref="IHintCyclingRenderer"/> — no per-puzzle wiring needed here.</summary>
    private void CycleActiveHints() => _renderer.CycleHintsFor(_currentPuzzle);

    // --- main tick ---

    private void OnTick()
    {
        if (_closing || IsDisposed) return;
        try
        {
            // Re-assert topmost first — Windows likes to demote tool windows
            // when a fullscreen game grabs activation; this fixes that quickly.
            TopmostEnforcer.Reassert(Handle);

            var status = _attacher.Tick();
            if (status == ProcessAttacher.Status.WaitingForGame || _attacher.State is null)
            {
                _title.ForeColor = Color.FromArgb(180, 180, 180);
                _title.Text = Loc.T("overlay.waiting.title");
                _body.Text  = Loc.T("overlay.waiting.body");
                return;
            }
            if (status == ProcessAttacher.Status.JustAttached)
                _title.Text = Loc.T("overlay.connected", _attacher.Memory.AttachedProcessId);

            _hotkeys.Poll();

            var state    = _attacher.State!;
            var detected = _puzzleManager.Detect(state);
            _currentPuzzle = detected.IsActive ? detected.Id : PuzzleId.None;
            var pool     = detected.IsActive ? PoolScanner.Scan(state) : new List<PoolMember>();

            var labels = new OverlayLabels();
            _renderer.Render(detected, state, pool, labels);
            _title.ForeColor = labels.TitleColor;
            _title.Text      = labels.Title;
            _body.Text       = labels.Body;

            // Sync grid panel: visible only when a renderer asks for it.
            _grid.PaintAction = labels.PaintGrid;
            int newHeight = labels.PaintGrid != null ? Math.Max(50, labels.GridHeight) : 0;
            if (_grid.Height != newHeight) _grid.Height = newHeight;
            bool shouldShow = labels.PaintGrid != null;
            if (_grid.Visible != shouldShow) _grid.Visible = shouldShow;
            _grid.Invalidate();

            _body.Text = labels.Body;

            // Track every pickup so a post-mortem F12 dump shows the recent
            // history — useful when a misfire is rare.
            var held = HeldZoombini.Find(state);
            int? cave = (detected.Id == PuzzleId.StoneColdCaves && held is { } hh)
                        ? CavesState.Read(state).FindAcceptingCave(hh)
                        : null;
            _dragHistory.OnTick(held, cave, pool.Count, detected.Id.ToString());

            // Bubblewonder-Event-Tracker: pollt jeden Tick (200ms) das Grid und
            // erkennt Schalter-Wechsel, Position-Aktivierungen, Pop/Score-Events.
            // Das löst das "F12 ist zu langsam"-Problem — wir sehen den
            // ZB-Pfad als Event-Timeline statt nur Snapshot.
            if (detected.Id == PuzzleId.BubblewonderAbyss)
                _bubbleTracker.Tick(state, pool);
        }
        catch (ObjectDisposedException) { /* form is closing; benign */ }
        catch (Exception ex) { ShowFatalError(ex); }
    }

    private void ShowFatalError(Exception ex)
    {
        if (_closing) return;
        _title.Text = Loc.T("overlay.error.title");
        _body.Text  = Loc.T("overlay.error.body", ex.GetType().Name, ex.Message, ErrorLogPath);
        try { File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:HH:mm:ss}] {ex}\n\n"); }
        catch { /* logging is best-effort */ }
    }


    // --- diagnostics ---

    private void DumpDiagnostic()
    {
        if (_attacher.State is null) return;
        // AppContext.BaseDirectory points to the bundle extraction temp dir
        // when running as a self-contained single-file exe with extraction —
        // the user would never find dumps there. Environment.ProcessPath gives
        // the actual on-disk exe path, whose directory is what they expect.
        string baseDir = Path.GetDirectoryName(Environment.ProcessPath ?? "")
                         ?? AppContext.BaseDirectory;
        string path = Path.Combine(baseDir, $"memdump-{DateTime.Now:HHmmss}.txt");
        using (var sw = new StreamWriter(path, append: false) { AutoFlush = true })
        {
            MemoryDumpFile.Write(sw, _attacher.State, _puzzleManager,
                _attacher.Memory.AttachedProcessName ?? "?",
                _attacher.Memory.AttachedProcessId,
                _attacher.Memory.ModuleBase,
                helperTitle: _title.Text,
                helperBody:  _body.Text,
                history:     _dragHistory,
                bubbleTracker: _bubbleTracker);
        }
        FlashFooter(Loc.T("overlay.dump.flash", Path.GetFileName(path)));
    }

    private void FlashFooter(string message)
    {
        _footer.Text = message;
        _footerResetTimer?.Dispose();
        _footerResetTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _footerResetTimer.Tick += (_, _) =>
        {
            if (_closing || IsDisposed) return;
            _footer.Text = DefaultFooter;
            _footerResetTimer?.Stop();
        };
        _footerResetTimer.Start();
    }

    // --- form lifecycle ---

    /// <summary>Don't fade ourselves when the game window takes activation. The
    /// base implementation triggers visual changes that some games abuse to hide us.</summary>
    protected override void OnDeactivate(EventArgs e) { /* intentional no-op */ }
    protected override bool ShowWithoutActivation => true;

    /// <summary>Tool-window flags: no taskbar entry, never steals focus, always topmost.
    /// WS_EX_TOPMOST in addition to TopMost=true: the managed property gets clobbered
    /// when an exclusive-mode game starts.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= ToolWindowExStyles.All;
            return cp;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Stop the tick BEFORE tearing down — prevents the timer from firing
        // during dispose and accessing already-disposed state.
        _closing = true;
        try { _timer.Stop(); } catch { }
        try { _footerResetTimer?.Stop(); _footerResetTimer?.Dispose(); } catch { }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _timer.Dispose(); } catch { }
        try { _attacher.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
