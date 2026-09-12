using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Evikit.Windows;

internal static class CaptureNative
{
    internal const int HotKeyMessage = 0x0312;
    internal const uint ControlAlt = 0x0002 | 0x0001, ControlShift = 0x0002 | 0x0004, NoRepeat = 0x4000;
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    internal static Bitmap ReadScreen(Rectangle bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1 || (long)bounds.Width * bounds.Height > 80_000_000)
            throw new InvalidOperationException("撮影範囲は 80 メガピクセル以内にしてください。");
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}

// The screenshot is frozen before the selection overlay appears. Coordinates are
// device pixels relative to the entire virtual desktop (which may start negative).
internal sealed class CaptureRegionForm : Form
{
    private readonly Bitmap desktop;
    private Point start;
    private bool dragging;
    public Rectangle Selection { get; private set; }

    public CaptureRegionForm(Bitmap desktop, Rectangle bounds)
    {
        this.desktop = desktop;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = bounds; TopMost = true;
        ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross; KeyPreview = true;
        Text = "範囲をドラッグ / Esc でキャンセル";
        Shown += (_, _) => { Bounds = bounds; Activate(); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, desktop.Width), Math.Clamp(point.Y, 0, desktop.Height));
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
        if (e.Button != MouseButtons.Left) return;
        start = Clamp(e.Location); dragging = true; Capture = true; Selection = Rectangle.Empty;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!dragging) return;
        var end = Clamp(e.Location);
        Selection = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (!dragging || e.Button != MouseButtons.Left) return;
        OnMouseMove(e); dragging = false; Capture = false;
        if (Selection.Width >= 3 && Selection.Height >= 3) DialogResult = DialogResult.OK;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(110, Color.Black)); e.Graphics.FillRectangle(shade, ClientRectangle);
        if (!Selection.IsEmpty)
        {
            e.Graphics.DrawImage(desktop, Selection, Selection, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.LimeGreen, 2); e.Graphics.DrawRectangle(pen, Selection);
        }
        // Place the hint near the cursor so it remains visible on any monitor.
        var point = PointToClient(Cursor.Position);
        var hint = new Rectangle(Math.Clamp(point.X + 16, 0, Math.Max(0, Width - 360)), Math.Clamp(point.Y + 24, 0, Math.Max(0, Height - 36)), 360, 32);
        e.Graphics.FillRectangle(Brushes.Black, hint);
        TextRenderer.DrawText(e.Graphics, "範囲をドラッグ → 自動保存 / Esc：取消", Font, hint, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
