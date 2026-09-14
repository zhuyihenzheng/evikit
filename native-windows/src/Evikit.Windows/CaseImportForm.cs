using System.Text;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class CaseImportForm : Form
{
    private readonly Workspace workspace;
    private readonly DataGridView cases = Ui.Grid(true), steps = Ui.Grid(true);
    private readonly TextBox details = Ui.Text("", true);
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 68, Padding = new(8) };
    private readonly Button import;
    private List<TestCase> pending = [];
    private bool busy;
    public List<string> CreatedIds { get; } = [];
    private sealed record Preview(string Id, string Title, int Steps, string Tester, string Env);

    public CaseImportForm(Workspace workspace)
    {
        this.workspace = workspace;
        Text = "CSV から用例を作成"; Font = SystemFonts.MessageBoxFont; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(1000, 680); MinimumSize = new(760, 520); StartPosition = FormStartPosition.CenterParent;
        Ui.Column(cases, "用例 ID", "Id", 80); Ui.Column(cases, "用例名", "Title", 180); Ui.Column(cases, "Step 数", "Steps", 45); Ui.Column(cases, "担当者", "Tester", 70); Ui.Column(cases, "環境", "Env", 70);
        Ui.Column(steps, "No.", "No", 30); Ui.Column(steps, "操作", "Action", 160); Ui.Column(steps, "テスト条件", "Condition", 160); Ui.Column(steps, "期待結果", "Expected", 160); Ui.Column(steps, "実際結果", "Actual", 100); Ui.Column(steps, "判定", "Verdict", 50);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new(1000, 540), SplitterDistance = 210, Panel1MinSize = 100, Panel2MinSize = 120 };
        details.ReadOnly = true;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var stepPage = new TabPage("ステップのプレビュー"); stepPage.Controls.Add(steps);
        var casePage = new TabPage("用例の情報"); casePage.Controls.Add(details);
        tabs.TabPages.AddRange([stepPage, casePage]);
        split.Panel1.Controls.Add(cases); split.Panel2.Controls.Add(tabs);
        import = Ui.Button("プレビューの用例を作成", () => _ = ImportAsync(), true); import.Enabled = false;
        var bar = Ui.Bar(Ui.Button("CSV / TSV を選択…", Choose), Ui.Button("テンプレートを保存…", SaveTemplate), import, Ui.Button("閉じる", Close));
        var hint = new Label { Dock = DockStyle.Top, Height = 65, Padding = new(8), Text = "1 行＝1 ステップ。同じ用例 ID の行は 1 件の用例にまとめます。UTF-8 CSV / TSV、列名付き。\nテンプレートを Excel で編集し「CSV UTF-8」で保存してください。操作・条件・期待結果を下の表で確認してから作成します。既存の用例は上書きしません。" };
        Controls.Add(split); Controls.Add(hint); Controls.Add(bar); Controls.Add(state);
        cases.SelectionChanged += (_, _) => ShowSteps();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        state.Text = "テンプレートを保存するか、作成済みの CSV を選んでください。まだ用例は作成されません。";
    }
    private void ShowSteps()
    {
        var c = cases.CurrentRow?.DataBoundItem is Preview row ? pending.FirstOrDefault(c => c.Id == row.Id) : null;
        steps.DataSource = c?.Steps;
        details.Text = c == null ? "" : $"用例 ID：{c.Id}\r\n用例名：{c.Title}\r\n担当者：{c.Tester}\r\n日付：{c.Date}\r\n環境：{c.Env}\r\n\r\n前提条件：\r\n{c.Precondition}\r\n\r\n備考：\r\n{c.Note}";
    }
    private void PreviewCases()
    {
        cases.DataSource = pending.Select(c => new Preview(c.Id, c.Title, c.Steps.Count, c.Tester, c.Env)).ToList(); ShowSteps();
    }
    private void Choose()
    {
        using var dialog = new OpenFileDialog { Title = "用例を作成する CSV / TSV", Filter = "CSV / TSV|*.csv;*.tsv|すべてのファイル|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        pending = []; cases.DataSource = null; steps.DataSource = null; import.Enabled = false;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidDataException("CSV は 2 MiB 以下にしてください。");
            pending = CaseCsv.Parse(Inputs.Utf8(File.ReadAllBytes(dialog.FileName)), workspace.LoadProject().Data);
            PreviewCases(); workspace.ValidateCaseImport(pending);
            import.Enabled = true;
            state.Text = $"{Path.GetFileName(dialog.FileName)}：{pending.Count} 用例 / {pending.Sum(c => c.Steps.Count)} ステップ。確認後に「プレビューの用例を作成」を押してください。";
        }
        catch (Exception ex) { state.Text = "作成できません：" + ex.Message; MessageBox.Show(this, ex.Message, "CSV を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void SaveTemplate()
    {
        using var dialog = new SaveFileDialog { Title = "用例 CSV テンプレートを保存", Filter = "CSV|*.csv", FileName = "evikit-case-template.csv" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, CaseCsv.Template(), new UTF8Encoding(true));
    }
    private async Task ImportAsync()
    {
        if (busy || pending.Count == 0) return;
        bool complete = false;
        busy = true; Enabled = false; UseWaitCursor = true; state.Text = "全件の内容と ID を検証して、用例を作成しています…";
        try
        {
            var result = await Task.Run(() => workspace.ImportCases(pending));
            CreatedIds.AddRange(result.CreatedIds);
            pending = pending.Skip(result.CreatedIds.Count).ToList(); PreviewCases();
            if (result.Error == null) complete = true;
            else
            {
                state.Text = $"{CreatedIds.Count} 件は作成済み、残り {pending.Count} 件。{result.FailedId} で停止しました。";
                MessageBox.Show(this, state.Text + "\n" + result.Error + "\n\n作成済みの用例は保持されています。残りはプレビューに残し、保存先の問題を解決後に再試行できます。", "一部の用例を作成できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex) { state.Text = "作成を開始できません：" + ex.Message; MessageBox.Show(this, ex.Message, "CSV を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; Enabled = true; UseWaitCursor = false; import.Enabled = pending.Count > 0; }
        if (complete) DialogResult = DialogResult.OK;
    }
}
