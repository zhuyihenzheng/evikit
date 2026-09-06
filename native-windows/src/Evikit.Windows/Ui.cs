using Evikit.Core;

namespace Evikit.Windows;

internal static class Ui
{
    internal static readonly Color Green = Color.FromArgb(24, 92, 73);
    internal static readonly Color Background = Color.FromArgb(244, 247, 245);
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new(80, 34), Padding = new(10, 3, 10, 3), FlatStyle = FlatStyle.Flat, BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Green, Margin = new(4) };
        b.FlatAppearance.BorderColor = Color.FromArgb(205, 220, 213); b.Click += (_, _) => Guard(action); return b;
    }
    internal static void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "evikit — 操作を完了できません", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    internal static TextBox Text(string value = "", bool multiline = false) => new() { Text = value, Dock = DockStyle.Fill, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, AcceptsReturn = multiline, MinimumSize = new(80, multiline ? 58 : 26) };
    internal static FlowLayoutPanel Bar(params Control[] controls)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new(4), BackColor = Background }; p.Controls.AddRange(controls); return p;
    }
    internal static TableLayoutPanel Fields() => new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new(12), ColumnStyles = { new(SizeType.Absolute, 110), new(SizeType.Percent, 100) } };
    internal static void Field(TableLayoutPanel panel, string label, Control input, int height = 42)
    {
        int row = panel.RowCount++; panel.RowStyles.Add(new(SizeType.Absolute, height));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new(4, 8, 4, 4) }, 0, row); panel.Controls.Add(input, 1, row);
    }
    internal static string? Prompt(IWin32Window owner, string title, string label, string value = "")
    {
        using var f = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new(480, 148), MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, Font = SystemFonts.MessageBoxFont };
        var input = Text(value); var fields = Fields(); Field(fields, label, input);
        var ok = Button("決定", () => f.DialogResult = DialogResult.OK, true); var cancel = Button("キャンセル", () => f.DialogResult = DialogResult.Cancel);
        var bar = Bar(ok, cancel); bar.Dock = DockStyle.Bottom; f.Controls.Add(fields); f.Controls.Add(bar); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
    internal static DataGridView Grid(bool readOnly = false) => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = readOnly, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        DefaultCellStyle = new() { WrapMode = DataGridViewTriState.True, Padding = new Padding(3), SelectionBackColor = Color.FromArgb(218, 236, 226), SelectionForeColor = Color.Black },
        EnableHeadersVisualStyles = false, ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(232, 240, 235), ForeColor = Green, Padding = new(4) }
    };
    internal static void Column(DataGridView grid, string title, string property, float weight = 100, bool readOnly = false) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, DataPropertyName = property, Name = property, FillWeight = weight, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable });
    internal static Bitmap Decode(byte[] data)
    {
        _ = Xlsx.ImageSize(data); // dimensions checked before GDI+ allocation
        using var stream = new MemoryStream(data); using var image = Image.FromStream(stream, true, true); return new Bitmap(image);
    }
    internal static byte[] Png(Image image) { using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray(); }
}
