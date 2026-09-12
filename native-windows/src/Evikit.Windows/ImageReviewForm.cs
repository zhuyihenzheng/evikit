using System.ComponentModel;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class ImageReviewForm : Form
{
    private readonly Workspace workspace;
    private readonly DataGridView grid = Ui.Grid();
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Background };
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 36, Padding = new(8), ForeColor = Ui.Green };
    private readonly Stack<string> deleted = new();
    private readonly List<Bitmap> thumbnails = [];
    private BindingList<ReviewRow> rows = [];
    private bool loading, dirty;
    public CaseDocument Document { get; private set; }

    private sealed class ReviewRow
    {
        public string Id { get; init; } = "";
        public Bitmap? Thumbnail { get; init; }
        public string Caption { get; set; } = "";
        public int Step { get; set; }
        public string Note { get; set; } = "";
    }
    private sealed record StepChoice(int No, string Label);

    public ImageReviewForm(Workspace workspace, CaseDocument document)
    {
        this.workspace = workspace; Document = document;
        Text = document.Data.Id + " — 画像をまとめて整理"; Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new(1180, 700); MinimumSize = new(980, 620); StartPosition = FormStartPosition.CenterParent;
        grid.MultiSelect = true; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 104;
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Thumbnail", HeaderText = "画像", DataPropertyName = "Thumbnail", ImageLayout = DataGridViewImageCellLayout.Zoom, FillWeight = 95, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        Ui.Column(grid, "ID", "Id", 40, true); Ui.Column(grid, "見出し", "Caption", 120);
        var choices = new List<StepChoice> { new(0, "共通") };
        choices.AddRange(document.Data.Steps.Select(s => new StepChoice(s.No, "Step " + s.No)));
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Step", HeaderText = "Step", DataPropertyName = "Step", DataSource = choices, DisplayMember = "Label", ValueMember = "No", FillWeight = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
        Ui.Column(grid, "確認事項・説明", "Note", 180);
        grid.SelectionChanged += (_, _) => { if (!loading) Ui.Guard(Preview); };
        grid.CellValueChanged += (_, _) => { if (!loading) MarkDirty(); };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.DataError += (_, e) => { e.ThrowException = false; state.Text = "入力内容を確認してください。"; };
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1200, 650), SplitterDistance = 780, Panel1MinSize = 600, Panel2MinSize = 240 };
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(preview);
        var bar = Ui.Bar(Ui.Button("↑", () => MoveImage(-1)), Ui.Button("↓", () => MoveImage(1)),
            Ui.Button("説明を一括追記", AppendNote), Ui.Button("Step を一括設定", AssignStep),
            Ui.Button("画像に注釈", Annotate), Ui.Button("削除", Delete), Ui.Button("削除を復元", Restore),
            Ui.Button("保存  Ctrl+S", Save, true), Ui.Button("閉じる", Close));
        var hint = new Label { Dock = DockStyle.Top, Height = 50, Padding = new(8), Text = "撮影した画像は保存済みです。見出し・説明を表で編集できます。Ctrl / Shift + クリックで複数選択。\n上下移動は 1 枚ずつ。説明の一括追記は既存の説明を残します。並び順が Excel の画像順になります。" };
        Controls.Add(split); Controls.Add(hint); Controls.Add(bar); Controls.Add(state);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; Ui.Guard(Save); } };
        FormClosing += (_, e) =>
        {
            try
            {
                EndEdit(); if (!dirty) return;
                var result = MessageBox.Show(this, "説明・並び順の変更を保存しますか？\n撮影済みの画像自体は削除されません。", "画像の整理", MessageBoxButtons.YesNoCancel);
                if (result == DialogResult.Cancel) e.Cancel = true;
                else if (result == DialogResult.Yes) Save();
            }
            catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); }
        };
        Rebuild();
    }

    private void EndEdit()
    {
        Validate(); if (!grid.EndEdit()) throw new InvalidOperationException("画像情報の入力を確認してください。");
        BindingContext?[rows]?.EndCurrentEdit();
    }
    private void MarkDirty() { dirty = true; state.Text = "● 説明・並び順に未保存の変更があります。"; }
    private void DisposeImages()
    {
        preview.Image?.Dispose(); preview.Image = null;
        foreach (var thumbnail in thumbnails) thumbnail.Dispose(); thumbnails.Clear();
    }
    private void Rebuild(string? select = null)
    {
        loading = true;
        try
        {
            grid.DataSource = null; DisposeImages(); rows = [];
            foreach (var e in Document.Data.Evidence.Where(e => e.Kind == "image"))
            {
                // Only small thumbnails stay resident; full images are decoded one at a time.
                using var image = Ui.Decode(workspace.ReadEvidence(Document.Data.Id, e));
                var thumb = new Bitmap(144, 90); thumbnails.Add(thumb);
                using (var graphics = Graphics.FromImage(thumb))
                {
                    graphics.Clear(Ui.Background); graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    double scale = Math.Min(144d / image.Width, 90d / image.Height);
                    int width = Math.Max(1, (int)(image.Width * scale)), height = Math.Max(1, (int)(image.Height * scale));
                    graphics.DrawImage(image, new Rectangle((144 - width) / 2, (90 - height) / 2, width, height));
                }
                rows.Add(new() { Id = e.Id, Thumbnail = thumb, Caption = e.Caption, Step = e.Step ?? 0, Note = e.Note });
            }
            grid.DataSource = rows;
            if (rows.Count > 0) SelectRow(Math.Max(0, rows.ToList().FindIndex(r => r.Id == select)));
            dirty = false; state.Text = $"{rows.Count} 枚 / 保存済み";
        }
        finally { loading = false; }
        Preview();
    }
    private ReviewRow Selected() => grid.CurrentRow?.DataBoundItem as ReviewRow ?? throw new InvalidOperationException("画像を選択してください。");
    private List<ReviewRow> SelectedRows() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ReviewRow>().ToList();
    private void SelectRow(int index) { grid.ClearSelection(); grid.CurrentCell = grid.Rows[index].Cells[1]; grid.Rows[index].Selected = true; }
    private void Preview()
    {
        preview.Image?.Dispose(); preview.Image = null;
        if (grid.CurrentRow?.DataBoundItem is not ReviewRow row) return;
        var e = Document.Data.Evidence.Single(e => e.Id == row.Id);
        preview.Image = Ui.Decode(workspace.ReadEvidence(Document.Data.Id, e));
    }
    private void MoveImage(int offset)
    {
        EndEdit(); if (SelectedRows().Count != 1) throw new InvalidOperationException("移動する画像を 1 枚選択してください。");
        int index = rows.IndexOf(Selected()), next = index + offset;
        if (next < 0 || next >= rows.Count) return;
        loading = true;
        try { (rows[index], rows[next]) = (rows[next], rows[index]); rows.ResetBindings(); SelectRow(next); }
        finally { loading = false; }
        MarkDirty(); Preview();
    }
    private void AppendNote()
    {
        EndEdit(); var selected = SelectedRows(); if (selected.Count == 0) return;
        string? text = Ui.Prompt(this, "説明を一括追記", $"{selected.Count} 枚に追記"); if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var row in selected) row.Note = row.Note.Length == 0 ? text : row.Note + "\n" + text;
        rows.ResetBindings(); MarkDirty();
    }
    private void AssignStep()
    {
        EndEdit(); var selected = SelectedRows(); if (selected.Count == 0) return;
        using var dialog = new Form { Text = $"{selected.Count} 枚の Step を設定", Font = Font, ClientSize = new(480, 145), StartPosition = FormStartPosition.CenterParent };
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Label" };
        combo.Items.Add(new StepChoice(0, "共通"));
        foreach (var s in Document.Data.Steps) combo.Items.Add(new StepChoice(s.No, $"Step {s.No}  {s.Action}"));
        combo.SelectedIndex = 0; var fields = Ui.Fields(); Ui.Field(fields, "移動先", combo);
        var bar = Ui.Bar(Ui.Button("設定", () => dialog.DialogResult = DialogResult.OK, true)); bar.Dock = DockStyle.Bottom;
        dialog.Controls.Add(fields); dialog.Controls.Add(bar);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var row in selected) row.Step = ((StepChoice)combo.SelectedItem!).No;
        rows.ResetBindings(); MarkDirty();
    }
    private void Save()
    {
        EndEdit(); if (!dirty) return;
        var saved = workspace.SaveImageReview(Document, rows.Select(r => new ImageReviewEdit(r.Id, r.Caption, r.Step == 0 ? null : r.Step, r.Note)).ToArray());
        Document = saved; dirty = false; state.Text = $"{rows.Count} 枚 / 説明・並び順を保存しました。";
    }
    private void Annotate()
    {
        string id = Selected().Id; Save(); var e = Document.Data.Evidence.Single(e => e.Id == id);
        using var dialog = new AnnotationForm(workspace.ReadEvidence(Document.Data.Id, e, true), e.Annotations);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Document = workspace.SaveAnnotation(Document, id, dialog.Result!, dialog.Annotation); Rebuild(id);
    }
    private void Delete()
    {
        EndEdit(); var selected = SelectedRows(); if (selected.Count == 0) return;
        if (MessageBox.Show(this, $"選択した {selected.Count} 枚を削除しますか？原図は保持し、この画面の「削除を復元」で戻せます。", "画像を削除", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        Save();
        try
        {
            foreach (var row in selected)
            {
                var result = workspace.DeleteEvidence(Document, row.Id); Document = result.Document; deleted.Push(result.ArchiveId);
            }
        }
        finally { Rebuild(); }
    }
    private void Restore()
    {
        if (deleted.Count == 0) throw new InvalidOperationException("この整理画面で削除した画像はありません。");
        Save(); Document = workspace.RestoreEvidence(deleted.Peek()); deleted.Pop(); Rebuild(Document.Data.Evidence[^1].Id);
    }
    protected override void Dispose(bool disposing) { if (disposing) DisposeImages(); base.Dispose(disposing); }
}
