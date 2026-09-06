using System.Text;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class EvidenceDialog : Form
{
    private readonly ComboBox kind = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox step = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox lang = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox caption = Ui.Text(), source = Ui.Text("", true), note = Ui.Text("", true), content = Ui.Text("", true);
    private readonly Label filename = new() { AutoSize = true, Padding = new(6) };
    private byte[]? bytes;
    private string extension = "txt", originalName = "";
    private readonly List<int?> stepNumbers = [null];
    private readonly bool metadataOnly;
    private readonly Evidence? metadata;
    public NewEvidence? Result { get; private set; }
    public Evidence? Metadata { get; private set; }
    public EvidenceDialog(TestCase c, Evidence? existing = null, string? file = null, byte[]? clipboardImage = null, string? clipboardText = null)
    {
        metadataOnly = existing != null; metadata = existing;
        Text = metadataOnly ? "証拠の情報を編集" : "証拠を追加"; Font = SystemFonts.MessageBoxFont; StartPosition = FormStartPosition.CenterParent; ClientSize = new(740, 700); MinimumSize = new(620, 550);
        kind.Items.AddRange(["image", "table", "text", "file"]); kind.SelectedItem = existing?.Kind ?? "text";
        category.Items.AddRange(Contract.Categories); category.SelectedItem = existing?.Category ?? "その他";
        lang.Items.AddRange(["", "plain", "log", "json", "xml", "sql"]); lang.SelectedItem = existing?.Lang ?? "plain";
        step.Items.Add("共通"); foreach (var s in c.Steps) { step.Items.Add($"{s.No} — {s.Action}"); stepNumbers.Add(s.No); } step.SelectedIndex = Math.Max(0, stepNumbers.IndexOf(existing?.Step));
        caption.Text = existing?.Caption ?? ""; source.Text = existing?.Source ?? ""; note.Text = existing?.Note ?? "";
        var fields = Ui.Fields(); Ui.Field(fields, "種類", kind); Ui.Field(fields, "分類", category); Ui.Field(fields, "ステップ", step); Ui.Field(fields, "見出し", caption); Ui.Field(fields, "出典 / SQL", source, 85); Ui.Field(fields, "確認事項", note, 75); Ui.Field(fields, "言語", lang);
        if (!metadataOnly)
        {
            Ui.Field(fields, "ファイル", Ui.Bar(Ui.Button("ファイル選択…", Choose), Ui.Button("画像を貼り付け", PasteImage), filename), 60);
            Ui.Field(fields, "テキスト / 表", content, 180);
            content.PlaceholderText = "ログ・JSON・CSV・Excel からコピーした TSV を貼り付け。表は種類を table に設定。";
        }
        kind.Enabled = !metadataOnly;
        var save = Ui.Button(metadataOnly ? "保存" : "追加", Save, true); var cancel = Ui.Button("キャンセル", () => DialogResult = DialogResult.Cancel);
        var buttons = Ui.Bar(save, cancel); buttons.Dock = DockStyle.Bottom; Controls.Add(fields); Controls.Add(buttons); CancelButton = cancel;
        if (file != null) LoadFile(file);
        if (clipboardImage != null) { bytes = clipboardImage; extension = "png"; kind.SelectedItem = "image"; category.SelectedItem = "画面"; caption.Text = "クリップボード画像"; filename.Text = "clipboard.png"; }
        if (clipboardText != null) { content.Text = clipboardText; if (clipboardText.Contains('\t')) { kind.SelectedItem = "table"; category.SelectedItem = "DB"; } }
    }
    private void Choose() { using var d = new OpenFileDialog { Title = "証拠ファイルを選択" }; if (d.ShowDialog(this) == DialogResult.OK) LoadFile(d.FileName); }
    private void LoadFile(string path)
    {
        if (new FileInfo(path).Length > 25 * 1024 * 1024) throw new InvalidDataException("証拠は 25 MiB 以下にしてください。");
        var detected = Inputs.Detect(path); kind.SelectedItem = detected.Kind; category.SelectedItem = detected.Category; lang.SelectedItem = detected.Lang;
        bytes = File.ReadAllBytes(path); originalName = Path.GetFileName(path); extension = Path.GetExtension(path).TrimStart('.'); if (extension == "") extension = "bin";
        caption.Text = Path.GetFileNameWithoutExtension(path); filename.Text = originalName;
        if (detected.Kind is "table" or "text") content.Text = Inputs.Utf8(bytes);
        if (detected.Kind == "image") { using var image = Ui.Decode(bytes); }
    }
    private void PasteImage()
    {
        using var image = Clipboard.GetImage(); if (image == null) throw new InvalidDataException("クリップボードに画像がありません。");
        if ((long)image.Width * image.Height > 80_000_000) throw new InvalidDataException("画像が大きすぎます。");
        bytes = Ui.Png(image); extension = "png"; kind.SelectedItem = "image"; category.SelectedItem = "画面"; filename.Text = "clipboard.png";
    }
    private void Save()
    {
        string k = (string)kind.SelectedItem!, cat = (string)category.SelectedItem!;
        if (metadataOnly)
        {
            var e = Contract.Clone(metadata!); e.Caption = caption.Text; e.Source = source.Text; e.Note = note.Text; e.Category = cat; e.Step = stepNumbers[step.SelectedIndex]; e.Lang = (string)lang.SelectedItem!;
            Metadata = e; DialogResult = DialogResult.OK; return;
        }
        byte[] data;
        if (k is "text" or "table") { data = Encoding.UTF8.GetBytes(content.Text); extension = k == "table" ? "csv" : "txt"; if (k == "table") _ = Tables.Parse(content.Text); }
        else { data = bytes ?? throw new InvalidDataException("ファイルまたは画像を選択してください。"); if (k == "image") { using var image = Ui.Decode(data); extension = Xlsx.ImageSize(data).Extension; } }
        Result = new(k, cat, caption.Text, stepNumbers[step.SelectedIndex], source.Text, note.Text, (string)lang.SelectedItem!, data, extension, originalName); DialogResult = DialogResult.OK;
    }
}
