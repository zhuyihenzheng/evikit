using System.Text;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class EvidenceDialog : Form
{
    private const string AutoKind = "自動（表 / テキスト）";
    private readonly ComboBox kind = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox step = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox lang = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox caption = Ui.Text(), source = Ui.Text("", true), note = Ui.Text("", true), content = Ui.Text("", true);
    private readonly Label filename = new() { AutoSize = true, Padding = new(6) };
    private readonly DataGridView tablePreview = Ui.Grid(true);
    private readonly Label formatStatus = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new(3) };
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 250 };
    private Button save = null!;
    private byte[]? bytes;
    private string extension = "txt", originalName = "";
    private string? sourcePath;
    private readonly List<int?> stepNumbers = [null];
    private readonly bool metadataOnly;
    private readonly Evidence? metadata;
    public NewEvidence? Result { get; private set; }
    public Evidence? Metadata { get; private set; }
    public EvidenceDialog(TestCase c, Evidence? existing = null, string? file = null, byte[]? clipboardImage = null, string? clipboardText = null)
    {
        metadataOnly = existing != null; metadata = existing;
        Text = metadataOnly ? "証拠の情報を編集" : "証拠を追加"; Font = SystemFonts.MessageBoxFont; StartPosition = FormStartPosition.CenterParent; ClientSize = new(860, 840); MinimumSize = new(620, 550); AutoScaleMode = AutoScaleMode.Dpi;
        kind.Name = "EvidenceKind"; content.Name = "EvidenceContent"; tablePreview.Name = "EvidencePreview"; formatStatus.Name = "EvidenceFormatStatus";
        kind.Items.AddRange([AutoKind, "image", "table", "text", "file"]); kind.SelectedItem = existing?.Kind ?? AutoKind;
        category.Items.AddRange(Contract.Categories); category.SelectedItem = existing?.Category ?? "その他";
        lang.Items.AddRange(["", "plain", "log", "json", "xml", "sql"]); lang.SelectedItem = existing?.Lang ?? "plain";
        step.Items.Add("共通"); foreach (var s in c.Steps) { step.Items.Add($"{s.No} — {s.Action}"); stepNumbers.Add(s.No); } step.SelectedIndex = Math.Max(0, stepNumbers.IndexOf(existing?.Step));
        caption.Text = existing?.Caption ?? ""; source.Text = existing?.Source ?? ""; note.Text = existing?.Note ?? "";
        var fields = Ui.Fields(); Ui.Field(fields, "種類", kind); Ui.Field(fields, "分類", category); Ui.Field(fields, "ステップ", step); Ui.Field(fields, "見出し", caption); Ui.Field(fields, "出典 / SQL", source, 85); Ui.Field(fields, "確認事項", note, 75); Ui.Field(fields, "言語", lang);
        if (!metadataOnly)
        {
            Ui.Field(fields, "取込", Ui.Bar(Ui.Button("ファイル選択…", Choose), Ui.Button("データを貼り付け", PasteText), Ui.Button("画像を貼り付け", PasteImage), filename), 84);
            content.AcceptsTab = true; content.MaxLength = 2 * 1024 * 1024 + 1;
            content.PlaceholderText = "DB / Excel の列名付きデータを Ctrl+V。CSV / TSV は自動で表にします。SQL は上の「出典 / SQL」へ。";
            tablePreview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            tablePreview.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            var dataPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                ColumnStyles = { new(SizeType.Percent, 50), new(SizeType.Percent, 50) }, RowStyles = { new(SizeType.Absolute, 26), new(SizeType.Percent, 100) } };
            dataPanel.Controls.Add(new Label { Text = "原文 / 編集（Ctrl+V）", AutoSize = true }, 0, 0);
            dataPanel.Controls.Add(new Label { Text = "表プレビュー", AutoSize = true }, 1, 0);
            dataPanel.Controls.Add(content, 0, 1); dataPanel.Controls.Add(tablePreview, 1, 1);
            Ui.Field(fields, "データ", dataPanel, 240); Ui.Field(fields, "認識結果", formatStatus, 84);
        }
        kind.Enabled = !metadataOnly;
        save = Ui.Button(metadataOnly ? "保存" : "追加", Save, true); save.Name = "SaveEvidence";
        var cancel = Ui.Button("キャンセル", () => DialogResult = DialogResult.Cancel);
        var buttons = Ui.Bar(save, cancel); buttons.Dock = DockStyle.Bottom; Controls.Add(fields); Controls.Add(buttons); CancelButton = cancel;
        if (!metadataOnly)
        {
            content.TextChanged += (_, _) => { previewTimer.Stop(); save.Enabled = false; tablePreview.Rows.Clear(); tablePreview.Columns.Clear(); formatStatus.Text = "データを確認しています…"; previewTimer.Start(); };
            kind.SelectedIndexChanged += (_, _) => RefreshTextPreview();
            previewTimer.Tick += (_, _) => RefreshTextPreview();
        }
        if (file != null) LoadFile(file);
        if (clipboardImage != null) { bytes = clipboardImage; extension = "png"; kind.SelectedItem = "image"; category.SelectedItem = "画面"; caption.Text = "クリップボード画像"; filename.Text = "clipboard.png"; }
        if (clipboardText != null) content.Text = clipboardText;
        if (!metadataOnly) RefreshTextPreview();
    }
    protected override void Dispose(bool disposing) { if (disposing) previewTimer.Dispose(); base.Dispose(disposing); }
    private EvidenceTextResult AnalyzeText() => EvidenceText.Analyze(content.Text, (string)kind.SelectedItem! == AutoKind ? "auto" : (string)kind.SelectedItem!);
    private void RefreshTextPreview()
    {
        previewTimer.Stop(); tablePreview.Rows.Clear(); tablePreview.Columns.Clear();
        bool isText = (string)kind.SelectedItem! is AutoKind or "text" or "table";
        content.Enabled = isText; tablePreview.Enabled = isText; save.Enabled = true;
        formatStatus.ForeColor = SystemColors.ControlText;
        if (!isText) { formatStatus.Text = "画像 / ファイルを追加します。テキストの内容は使用しません。"; return; }
        try
        {
            var result = AnalyzeText(); formatStatus.Text = result.Message;
            if (result.Rows is not { } rows) return;
            if ((string)category.SelectedItem! == "その他") category.SelectedItem = "DB";
            for (int i = 0; i < rows[0].Count; i++) tablePreview.Columns.Add(new DataGridViewTextBoxColumn { Name = "c" + i, HeaderText = rows[0][i], Width = 140, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (var row in rows.Skip(1).Take(500)) tablePreview.Rows.Add(row.Cast<object>().ToArray());
        }
        catch (InvalidDataException ex) { formatStatus.Text = ex.Message; formatStatus.ForeColor = Color.Firebrick; save.Enabled = false; }
    }
    private void PasteText()
    {
        if (!Clipboard.ContainsText()) throw new InvalidDataException("クリップボードにテキストがありません。DB の結果を列名付きでコピーしてください。");
        bytes = null; sourcePath = null; originalName = ""; extension = "txt"; filename.Text = "クリップボードのデータ";
        kind.SelectedItem = AutoKind; content.Text = Clipboard.GetText(); RefreshTextPreview();
    }
    private void Choose() { using var d = new OpenFileDialog { Title = "証拠ファイルを選択" }; if (d.ShowDialog(this) == DialogResult.OK) LoadFile(d.FileName); }
    private void LoadFile(string path)
    {
        var detected = Inputs.Detect(path);
        long size = new FileInfo(path).Length, maximum = detected.Kind == "file" ? Media.MaxFileBytes : Media.MaxInlineBytes;
        if (size > maximum) throw new InvalidDataException($"この種類の証拠は {maximum / 1048576:N0} MiB 以下にしてください。");
        kind.SelectedItem = detected.Kind; category.SelectedItem = detected.Category; lang.SelectedItem = detected.Lang;
        sourcePath = detected.Kind == "file" ? Path.GetFullPath(path) : null;
        bytes = detected.Kind == "file" ? null : File.ReadAllBytes(path);
        originalName = Path.GetFileName(path); extension = Path.GetExtension(path).TrimStart('.'); if (extension == "") extension = "bin";
        caption.Text = Path.GetFileNameWithoutExtension(path); filename.Text = $"{originalName} ({size / 1048576d:N1} MiB)"; content.Clear();
        if (Media.IsVideo(path)) note.PlaceholderText = "例：00:35 エラー表示 / 01:12 再試行で成功";
        if (detected.Kind is "table" or "text") content.Text = Inputs.Utf8(bytes!);
        if (detected.Kind == "image") { using var image = Ui.Decode(bytes!); }
        RefreshTextPreview();
    }
    internal void SetVideoFrame(Evidence video, string timestamp)
    {
        step.SelectedIndex = Math.Max(0, stepNumbers.IndexOf(video.Step));
        source.Text = $"動画 {video.Id} ({video.OriginalName}) / {timestamp}";
        caption.Text = $"動画 {video.Id}：{timestamp}";
    }
    private void PasteImage()
    {
        using var image = Clipboard.GetImage(); if (image == null) throw new InvalidDataException("クリップボードに画像がありません。");
        if ((long)image.Width * image.Height > 80_000_000) throw new InvalidDataException("画像が大きすぎます。");
        bytes = Ui.Png(image); sourcePath = null; originalName = "clipboard.png"; extension = "png"; kind.SelectedItem = "image"; category.SelectedItem = "画面"; filename.Text = "clipboard.png";
    }
    private void Save()
    {
        string k = (string)kind.SelectedItem!, cat = (string)category.SelectedItem!;
        if (metadataOnly)
        {
            var e = Contract.Clone(metadata!); e.Caption = caption.Text; e.Source = source.Text; e.Note = note.Text; e.Category = cat; e.Step = stepNumbers[step.SelectedIndex]; e.Lang = (string)lang.SelectedItem!;
            Metadata = e; DialogResult = DialogResult.OK; return;
        }
        // Parse the current content again, never save a stale debounced preview.
        if (k == AutoKind) k = AnalyzeText().Kind;
        if (k == "table" && cat == "その他") cat = "DB";
        byte[] data;
        if (k == "file" && sourcePath != null)
        {
            Result = new(k, cat, caption.Text, stepNumbers[step.SelectedIndex], source.Text, note.Text, "", [], extension, originalName, sourcePath);
            DialogResult = DialogResult.OK; return;
        }
        if (k is "text" or "table") { data = Encoding.UTF8.GetBytes(content.Text); extension = k == "table" ? "csv" : "txt"; if (k == "table") _ = Tables.Parse(content.Text); }
        else { data = bytes ?? throw new InvalidDataException("ファイルまたは画像を選択してください。"); if (k == "image") { using var image = Ui.Decode(data); extension = Xlsx.ImageSize(data).Extension; } }
        Result = new(k, cat, caption.Text, stepNumbers[step.SelectedIndex], source.Text, note.Text, k == "table" ? "" : (string)lang.SelectedItem!, data, extension, originalName); DialogResult = DialogResult.OK;
    }
}
