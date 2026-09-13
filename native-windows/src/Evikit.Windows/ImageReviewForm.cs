using System.ComponentModel;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class ImageReviewForm : Form
{
    private readonly Workspace workspace;
    private readonly string evidenceId;
    private readonly DataGridView grid = Ui.Grid();
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Background };
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new(8), ForeColor = Ui.Green };
    private readonly Label heading = new() { Dock = DockStyle.Top, Height = 76, Padding = new(8), AutoEllipsis = true };
    private readonly Stack<string> deleted = new();
    private readonly List<Bitmap> thumbnails = [];
    private BindingList<ReviewRow> rows = [];
    private bool loading, dirty, busy;
    public CaseDocument Document { get; private set; }
    private Evidence Evidence => Document.Data.Evidence.Single(e => e.Id == evidenceId);
    private sealed class ReviewRow
    {
        public string Key { get; init; } = "";
        public int Number { get; set; }
        public Bitmap? Thumbnail { get; init; }
        public string Caption { get; set; } = "";
        public string Note { get; set; } = "";
        public string Status { get; init; } = "";
    }
    public ImageReviewForm(Workspace workspace, CaseDocument document, string evidenceId)
    {
        this.workspace = workspace; Document = document; this.evidenceId = evidenceId;
        if (Evidence.Kind != "image") throw new InvalidOperationException("画像のエビデンスを選択してください。");
        Text = $"{document.Data.Id} / {evidenceId} — エビデンス内の画像"; Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new(1180, 730); MinimumSize = new(980, 650); StartPosition = FormStartPosition.CenterParent;
        grid.MultiSelect = true; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; grid.RowTemplate.Height = 104;
        grid.Columns.Add(new DataGridViewImageColumn { Name = "Thumbnail", HeaderText = "画像", DataPropertyName = "Thumbnail", ImageLayout = DataGridViewImageCellLayout.Zoom, FillWeight = 95, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        Ui.Column(grid, "順番", "Number", 35, true); Ui.Column(grid, "画像の見出し（任意）", "Caption", 130); Ui.Column(grid, "画像の説明（任意）", "Note", 190);
        Ui.Column(grid, "状態", "Status", 65, true);
        grid.SelectionChanged += (_, _) => { if (!loading) Ui.Guard(Preview); };
        grid.CellValueChanged += (_, _) => { if (!loading) MarkDirty(); };
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.DataError += (_, e) => { e.ThrowException = false; state.Text = "入力内容を確認してください。"; };
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1200, 650), SplitterDistance = 780, Panel1MinSize = 600, Panel2MinSize = 240 };
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(preview);
        var bar = Ui.Bar(Ui.Button("＋ 画像を追加", PickImages, true), Ui.Button("画像を貼り付け", Paste), Ui.Button("共通情報を編集", EditCommon),
            Ui.Button("↑", () => MoveImage(-1)), Ui.Button("↓", () => MoveImage(1)), Ui.Button("説明を一括追記", AppendNote),
            Ui.Button("画像に注釈", Annotate), Ui.Button("原図に戻す", ResetImage), Ui.Button("画像を削除", Delete), Ui.Button("削除を復元", Restore),
            Ui.Button("画像を保存…", SaveImage), Ui.Button("保存  Ctrl+S", Save, true), Ui.Button("閉じる", Close));
        Controls.Add(split); Controls.Add(heading); Controls.Add(bar); Controls.Add(state);
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = !busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) _ = ImportAsync(files); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (busy) return;
            if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; Ui.Guard(Save); }
            else if (e.Control && e.Shift && e.KeyCode == Keys.V) { e.SuppressKeyPress = true; Ui.Guard(Paste); }
        };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            try
            {
                EndEdit(); if (!dirty) return;
                var result = MessageBox.Show(this, "説明・並び順の変更を保存しますか？追加済みの画像自体は保存されています。", "画像の整理", MessageBoxButtons.YesNoCancel);
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
    private void MarkDirty() { dirty = true; state.Text = "● 画像の説明・並び順に未保存の変更があります。"; }
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
            foreach (var image in ImageGroups.Items(Evidence))
            {
                var thumb = new Bitmap(144, 90); thumbnails.Add(thumb); string error = "";
                using (var graphics = Graphics.FromImage(thumb))
                {
                    graphics.Clear(Ui.Background);
                    try
                    {
                        using var decoded = Ui.Decode(workspace.ReadEvidence(Document.Data.Id, image));
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        double scale = Math.Min(144d / decoded.Width, 90d / decoded.Height);
                        int w = Math.Max(1, (int)(decoded.Width * scale)), h = Math.Max(1, (int)(decoded.Height * scale));
                        graphics.DrawImage(decoded, new Rectangle((144 - w) / 2, (90 - h) / 2, w, h));
                    }
                    catch (Exception ex) { error = ex.Message; graphics.DrawString("読込エラー", Font, Brushes.Firebrick, 10, 30); }
                }
                rows.Add(new() { Key = ImageGroups.Key(image), Number = rows.Count + 1, Thumbnail = thumb,
                    Caption = Evidence.Images == null ? "" : image.Caption, Note = Evidence.Images == null ? "" : image.Note, Status = error });
            }
            grid.DataSource = rows;
            if (rows.Count > 0) SelectRow(Math.Max(0, rows.ToList().FindIndex(r => r.Key == select)));
            dirty = false; state.Text = $"1 件のエビデンス / {rows.Count} 枚 / 保存済み";
            heading.Text = $"{Evidence.Id}  {Evidence.Caption}  /  {(Evidence.Step == null ? "共通" : "Step " + Evidence.Step)}\n共通の説明：{Evidence.Note}\nファイルの複数選択・ドラッグ＆ドロップ・貼り付けで、このエビデンスに追加できます。";
        }
        finally { loading = false; }
        Preview();
    }
    private ReviewRow Selected() => grid.CurrentRow?.DataBoundItem as ReviewRow ?? throw new InvalidOperationException("画像を選択してください。");
    private List<ReviewRow> SelectedRows() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ReviewRow>().OrderBy(r => r.Number).ToList();
    private void SelectRow(int index) { grid.ClearSelection(); grid.CurrentCell = grid.Rows[index].Cells[1]; grid.Rows[index].Selected = true; }
    private void Preview()
    {
        preview.Image?.Dispose(); preview.Image = null;
        if (grid.CurrentRow?.DataBoundItem is ReviewRow row)
        {
            try { preview.Image = Ui.Decode(workspace.ReadEvidence(Document.Data.Id, ImageGroups.Select(Evidence, row.Key))); }
            catch (Exception ex) { state.Text = "画像を表示できません：" + ex.Message; }
        }
    }
    private void MoveImage(int offset)
    {
        EndEdit(); if (SelectedRows().Count != 1) throw new InvalidOperationException("移動する画像を 1 枚選択してください。");
        int index = rows.IndexOf(Selected()), next = index + offset; if (next < 0 || next >= rows.Count) return;
        loading = true;
        try { (rows[index], rows[next]) = (rows[next], rows[index]); for (int i = 0; i < rows.Count; i++) rows[i].Number = i + 1; rows.ResetBindings(); SelectRow(next); }
        finally { loading = false; }
        MarkDirty(); Preview();
    }
    private void AppendNote()
    {
        EndEdit(); var selected = SelectedRows(); if (selected.Count == 0) return;
        string? text = Ui.Prompt(this, "画像の説明を一括追記", $"{selected.Count} 枚に追記"); if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var row in selected) row.Note = row.Note.Length == 0 ? text : row.Note + "\n" + text;
        rows.ResetBindings(); MarkDirty();
    }
    private void Save()
    {
        EndEdit(); if (!dirty) return;
        Document = workspace.SaveGallery(Document, evidenceId, rows.Select(r => new GalleryEdit(r.Key, r.Caption, r.Note)).ToArray());
        dirty = false; state.Text = $"{rows.Count} 枚 / 説明・並び順を保存しました。";
    }
    private void EditCommon()
    {
        Save(); using var dialog = new EvidenceDialog(Document.Data, Evidence);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var c = Contract.Clone(Document.Data); c.Evidence[c.Evidence.FindIndex(e => e.Id == evidenceId)] = dialog.Metadata!;
        Document = workspace.SaveCase(new(c, Document.Revision)); Rebuild();
    }
    private void PickImages()
    {
        using var dialog = new OpenFileDialog { Title = "同じエビデンスに画像を追加", Multiselect = true, Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _ = ImportAsync(dialog.FileNames);
    }
    private async Task ImportAsync(string[] paths)
    {
        if (busy) return;
        int added = 0;
        try
        {
            Save(); busy = true; Enabled = false; UseWaitCursor = true;
            foreach (string path in paths)
            {
                state.Text = $"画像を追加しています… {added}/{paths.Length}";
                var doc = Document; int? step = Evidence.Step;
                Document = await Task.Run(() =>
                {
                    if (new FileInfo(path).Length > Media.MaxInlineBytes) throw new InvalidDataException("画像は 25 MiB 以下にしてください。");
                    byte[] bytes = File.ReadAllBytes(path);
                    using var decoded = Ui.Decode(bytes);
                    return workspace.AppendImage(doc, evidenceId, new("image", "画面", Path.GetFileNameWithoutExtension(path), step, "", "", "", bytes, "png", Path.GetFileName(path)));
                });
                added++;
            }
        }
        catch (Exception ex) { MessageBox.Show(this, $"{added} 枚は保存済みです。追加を停止しました。\n{ex.Message}"); }
        finally { busy = false; Enabled = true; UseWaitCursor = false; Ui.Guard(() => Rebuild()); }
    }
    private void Paste()
    {
        if (!Clipboard.ContainsImage()) throw new InvalidOperationException("クリップボードに画像がありません。");
        using var image = Clipboard.GetImage(); if (image == null) return;
        if ((long)image.Width * image.Height > 80_000_000) throw new InvalidDataException("画像が大きすぎます。");
        Save(); Document = workspace.AppendImage(Document, evidenceId, new("image", "画面", "貼り付け画像", Evidence.Step, "クリップボード", "", "", Ui.Png(image), "png"));
        Rebuild(ImageGroups.Key(ImageGroups.Items(Evidence)[^1]));
    }
    private void Annotate()
    {
        string key = Selected().Key; Save(); var image = ImageGroups.Select(Evidence, key);
        using var dialog = new AnnotationForm(workspace.ReadEvidence(Document.Data.Id, image, true), image.Annotations);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Document = workspace.SaveAnnotation(Document, evidenceId, dialog.Result!, dialog.Annotation, key); Rebuild(key);
    }
    private void ResetImage()
    {
        string key = Selected().Key; Save(); Document = workspace.ResetAnnotation(Document, evidenceId, key); Rebuild(key);
    }
    private void Delete()
    {
        EndEdit(); var selected = SelectedRows(); if (selected.Count == 0) return;
        if (selected.Count == rows.Count) throw new InvalidOperationException("1 枚以上残してください。エビデンス全体の削除はメイン画面から行えます。");
        if (MessageBox.Show(this, $"{selected.Count} 枚を削除しますか？原図は保持し「削除を復元」で戻せます。", "画像を削除", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        Save();
        try { foreach (var row in selected) { var result = workspace.DeleteImage(Document, evidenceId, row.Key); Document = result.Document; deleted.Push(result.ArchiveId); } }
        finally { Rebuild(); }
    }
    private void Restore()
    {
        if (deleted.Count == 0) throw new InvalidOperationException("この画面で削除した画像はありません。");
        Save(); Document = workspace.RestoreImage(deleted.Peek()); deleted.Pop(); Rebuild();
    }
    private void SaveImage()
    {
        var image = ImageGroups.Select(Evidence, Selected().Key);
        using var dialog = new SaveFileDialog { FileName = image.File, Title = "選択した画像を保存" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = Path.GetFullPath(dialog.FileName);
        if (destination.StartsWith(workspace.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("プロジェクト外を選択してください。");
        workspace.CopyEvidence(Document.Data.Id, image, destination, false);
    }
    protected override void Dispose(bool disposing) { if (disposing) DisposeImages(); base.Dispose(disposing); }
}
