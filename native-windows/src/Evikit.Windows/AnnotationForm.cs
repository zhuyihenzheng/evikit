using System.Drawing.Drawing2D;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class AnnotationForm : Form
{
    private readonly Bitmap original;
    private readonly Canvas canvas = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(227, 234, 229) };
    private readonly ComboBox tool = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ComboBox color = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly TextBox text = new() { Width = 180, PlaceholderText = "文字ツールの内容" };
    private readonly Stack<Annotations> history = new();
    private PointF? start, end;
    private float scale = 1, offsetX, offsetY;
    public Annotations Annotation { get; private set; }
    public byte[]? Result { get; private set; }
    public AnnotationForm(byte[] image, Annotations? saved)
    {
        original = Ui.Decode(image); Annotation = saved == null ? new() : Contract.Clone(saved);
        Text = "画像に注釈 — 原図は保持されます"; Font = SystemFonts.MessageBoxFont; StartPosition = FormStartPosition.CenterParent; ClientSize = new(1100, 760); MinimumSize = new(750, 500); KeyPreview = true;
        tool.Items.AddRange(["枠", "矢印", "番号", "文字", "切り抜き"]); tool.SelectedIndex = 0;
        color.Items.AddRange(["赤", "青", "黄"]); color.SelectedIndex = 0;
        var toolbar = Ui.Bar(tool, color, text, Ui.Button("元に戻す Ctrl+Z", Undo), Ui.Button("切り抜き解除", () => { Remember(); Annotation.Crop = null; canvas.Invalidate(); }), Ui.Button("注釈をクリア", () => { Remember(); Annotation = new(); canvas.Invalidate(); }), Ui.Button("保存", Save, true), Ui.Button("キャンセル", () => DialogResult = DialogResult.Cancel));
        var info = new Label { Text = "原図座標で保存。枠・矢印・切り抜き：ドラッグ ／ 番号・文字：クリック。切り抜き範囲は緑で表示。", Dock = DockStyle.Bottom, Height = 34, Padding = new(6) };
        Controls.Add(canvas); Controls.Add(toolbar); Controls.Add(info);
        canvas.Paint += (_, e) => PaintCanvas(e.Graphics);
        canvas.Resize += (_, _) => canvas.Invalidate();
        canvas.MouseDown += (_, e) => { if (e.Button != MouseButtons.Left) return; var p = Point(e.Location); if (p == null) return; start = end = p; canvas.Capture = true; };
        canvas.MouseMove += (_, e) => { if (start == null) return; end = Point(e.Location, true); canvas.Invalidate(); };
        canvas.MouseUp += (_, e) => Ui.Guard(() => { if (e.Button != MouseButtons.Left || start == null) return; end = Point(e.Location, true); Finish(); canvas.Capture = false; });
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Z) { e.SuppressKeyPress = true; Undo(); } };
    }
    private void Remember() => history.Push(Contract.Clone(Annotation));
    private void Undo() { if (history.TryPop(out var previous)) { Annotation = previous; canvas.Invalidate(); } }
    private PointF? Point(System.Drawing.Point point, bool clamp = false)
    {
        float x = (point.X - offsetX) / scale, y = (point.Y - offsetY) / scale;
        if (!clamp && (x < 0 || y < 0 || x > original.Width || y > original.Height)) return null;
        return new(Math.Clamp(x, 0, original.Width), Math.Clamp(y, 0, original.Height));
    }
    private void Finish()
    {
        var a = start!.Value; var b = end ?? a; start = end = null;
        string ink = new[] { "red", "blue", "yellow" }[color.SelectedIndex];
        var rect = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        if (tool.SelectedIndex is 0 or 4 && (rect.Width < 2 || rect.Height < 2)) { canvas.Invalidate(); return; }
        if (tool.SelectedIndex == 3 && string.IsNullOrWhiteSpace(text.Text)) throw new InvalidOperationException("文字を入力してください。");
        if (Annotation.Shapes.Count >= 1000) throw new InvalidOperationException("注釈は 1000 件以内です。");
        Remember();
        switch (tool.SelectedIndex)
        {
            case 0: Annotation.Shapes.Add(new() { Type = "rect", X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height, Color = ink }); break;
            case 1: Annotation.Shapes.Add(new() { Type = "arrow", X = a.X, Y = a.Y, X2 = b.X, Y2 = b.Y, Color = ink }); break;
            case 2:
                int n = Annotation.Shapes.Where(s => s.Type == "number").Select(s => s.N ?? 0).DefaultIfEmpty(0).Max() + 1;
                if (n > 999) throw new InvalidOperationException("番号は 999 までです。");
                Annotation.Shapes.Add(new() { Type = "number", X = a.X, Y = a.Y, N = n, Color = ink }); break;
            case 3: Annotation.Shapes.Add(new() { Type = "text", X = a.X, Y = a.Y, Text = text.Text, Color = ink }); break;
            case 4:
                int left = (int)Math.Floor(rect.Left), top = (int)Math.Floor(rect.Top), right = Math.Min(original.Width, (int)Math.Ceiling(rect.Right)), bottom = Math.Min(original.Height, (int)Math.Ceiling(rect.Bottom));
                Annotation.Crop = new() { X = left, Y = top, W = right - left, H = bottom - top }; break;
        }
        canvas.Invalidate();
    }
    private void PaintCanvas(Graphics g)
    {
        scale = Math.Max(.001f, Math.Min((canvas.Width - 32f) / original.Width, (canvas.Height - 32f) / original.Height));
        offsetX = (canvas.Width - original.Width * scale) / 2; offsetY = (canvas.Height - original.Height * scale) / 2;
        g.TranslateTransform(offsetX, offsetY); g.ScaleTransform(scale, scale); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(original, 0, 0, original.Width, original.Height); DrawShapes(g);
        if (Annotation.Crop is { } crop)
        {
            using var pen = new Pen(Color.FromArgb(0, 130, 85), 2 / scale) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, (float)crop.X, (float)crop.Y, (float)crop.W, (float)crop.H);
        }
        if (start is { } a && end is { } b)
        {
            using var pen = new Pen(Color.DimGray, 2 / scale) { DashStyle = DashStyle.Dash };
            if (tool.SelectedIndex == 1) g.DrawLine(pen, a, b); else g.DrawRectangle(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
        g.ResetTransform();
    }
    private void DrawShapes(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var s in Annotation.Shapes)
        {
            var ink = s.Color switch { "blue" => Color.FromArgb(37, 99, 235), "yellow" => Color.FromArgb(234, 179, 8), _ => Color.FromArgb(220, 38, 38) };
            using var pen = new Pen(ink, 4); using var brush = new SolidBrush(ink); using var font = new Font("Yu Gothic", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            float x = (float)s.X, y = (float)s.Y;
            switch (s.Type)
            {
                case "rect": g.DrawRectangle(pen, x, y, (float)(s.W ?? 0), (float)(s.H ?? 0)); break;
                case "arrow":
                    using (var cap = new AdjustableArrowCap(5, 6)) { pen.CustomEndCap = cap; g.DrawLine(pen, x, y, (float)(s.X2 ?? x), (float)(s.Y2 ?? y)); } break;
                case "number":
                    g.FillEllipse(brush, x - 16, y - 16, 32, 32);
                    using (var white = new SolidBrush(Color.White)) using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(s.N.ToString(), font, white, new RectangleF(x - 16, y - 16, 32, 32), format); break;
                case "text": g.DrawString(s.Text, font, brush, x, y); break;
            }
        }
    }
    private void Save()
    {
        var crop = Annotation.Crop;
        int x = crop == null ? 0 : (int)Math.Floor(crop.X), y = crop == null ? 0 : (int)Math.Floor(crop.Y);
        int w = crop == null ? original.Width : (int)Math.Round(crop.W), h = crop == null ? original.Height : (int)Math.Round(crop.H);
        if (w < 1 || h < 1 || x < 0 || y < 0 || x + w > original.Width || y + h > original.Height) throw new InvalidDataException("切り抜き範囲が原図の外側です。");
        // Normalize fractional browser-edition crops to the exact PNG pixel dimensions.
        if (crop != null) Annotation.Crop = new() { X = x, Y = y, W = w, H = h };
        using var output = new Bitmap(w, h); using (var g = Graphics.FromImage(output)) { g.Clear(Color.White); g.TranslateTransform(-x, -y); g.DrawImage(original, 0, 0, original.Width, original.Height); DrawShapes(g); }
        Result = Ui.Png(output); DialogResult = DialogResult.OK;
    }
    protected override void Dispose(bool disposing) { if (disposing) original.Dispose(); base.Dispose(disposing); }
    private sealed class Canvas : Panel { public Canvas() { DoubleBuffered = true; ResizeRedraw = true; } }
}
