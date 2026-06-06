using System.Drawing;
using System.Windows.Forms;

namespace ZoombiniHelper.UI;

/// <summary>
/// Lets the user drag a borderless form by clicking anywhere inside it.
/// Wire it via <see cref="AttachTo"/> — pass the form plus any child controls
/// that should also act as drag handles (typically labels, since the form
/// itself doesn't see clicks on its children).
/// </summary>
public sealed class WindowDragHandler
{
    private readonly Form _form;
    private Point _dragStart;
    private bool _dragging;

    public WindowDragHandler(Form form) => _form = form;

    public void AttachTo(params Control[] controls)
    {
        foreach (var ctl in controls)
        {
            ctl.MouseDown += OnMouseDown;
            ctl.MouseMove += OnMouseMove;
            ctl.MouseUp   += OnMouseUp;
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging) _form.Location = new Point(
            Cursor.Position.X - _dragStart.X,
            Cursor.Position.Y - _dragStart.Y);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e) => _dragging = false;
}
