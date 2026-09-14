using Evikit.Core;

namespace Evikit.Windows;

internal sealed class ExportOptionsForm : Form
{
    private readonly CheckBox images, videos, files, dates, tester, environment, conditions, notes, sources;
    public ExportOptions Options => new()
    {
        Images = images.Checked, Videos = videos.Checked, Files = files.Checked,
        Dates = dates.Checked, Tester = tester.Checked, Environment = environment.Checked,
        Conditions = conditions.Checked, Notes = notes.Checked, Sources = sources.Checked
    };

    public ExportOptionsForm(ExportOptions options)
    {
        Text = "成果物の出力設定"; Font = SystemFonts.MessageBoxFont; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(660, 610); MinimumSize = new(580, 490); StartPosition = FormStartPosition.CenterParent;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(18), BackColor = Color.White };
        void Label(string text) => content.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new(575, 0), Margin = new(4, 8, 4, 10) });
        CheckBox Choice(string text, bool value)
        {
            var box = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new(8, 5, 8, 5) };
            content.Controls.Add(box); return box;
        }
        Label("出力する項目を選んでください。元の用例・証拠ファイルは変更しません。");
        Label("Excel と添付 ZIP に含めるエビデンス");
        images = Choice("画像（証拠ブロック・説明・画像ファイル）", options.Images);
        videos = Choice("動画（証拠ブロック・説明・動画ファイル）", options.Videos);
        files = Choice("その他のファイル添付（表・ログは常に出力）", options.Files);
        Label("Excel に表示する情報");
        dates = Choice("実施日・取得日時（用例の日付、各画像の撮影日時など）", options.Dates);
        tester = Choice("担当者", options.Tester);
        environment = Choice("環境", options.Environment);
        conditions = Choice("前提条件・ステップのテスト条件", options.Conditions);
        notes = Choice("備考・確認事項（用例・エビデンス・各画像）", options.Notes);
        sources = Choice("出典 / SQL（各画像の出典を含む）", options.Sources);
        Label("この選択はプロジェクトごとに記憶します。用例名・操作・期待結果・実際結果・判定、表とログの本文は常に出力します。本文・画像内の日時や文字の自動削除は行いません。");
        var next = Ui.Button("出力先を選ぶ…", () => DialogResult = DialogResult.OK, true);
        var cancel = Ui.Button("キャンセル", () => DialogResult = DialogResult.Cancel);
        var reset = Ui.Button("すべて出力", () => { foreach (var box in content.Controls.OfType<CheckBox>()) box.Checked = true; });
        var bar = Ui.Bar(next, reset, cancel); bar.Dock = DockStyle.Bottom;
        Controls.Add(content); Controls.Add(bar); AcceptButton = next; CancelButton = cancel;
    }
}
