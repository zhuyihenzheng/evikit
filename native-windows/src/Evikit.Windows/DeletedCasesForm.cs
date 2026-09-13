using Evikit.Core;

namespace Evikit.Windows;

internal sealed class DeletedCasesForm : Form
{
    private readonly Workspace workspace;
    private readonly DataGridView grid = Ui.Grid(true);
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new(8) };
    private readonly Button restore;
    private bool busy;
    public CaseDocument? Restored { get; private set; }

    public DeletedCasesForm(Workspace workspace)
    {
        this.workspace = workspace;
        Text = "削除した用例"; Font = SystemFonts.MessageBoxFont; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(880, 470); MinimumSize = new(700, 360); StartPosition = FormStartPosition.CenterParent;
        Ui.Column(grid, "用例 ID", "CaseId", 85); Ui.Column(grid, "用例名", "Title", 200);
        Ui.Column(grid, "削除日時", "DeletedAt", 160); Ui.Column(grid, "Step 数", "StepCount", 45);
        Ui.Column(grid, "証拠数", "EvidenceCount", 45); Ui.Column(grid, "画像数", "ImageCount", 45);
        restore = Ui.Button("選択した用例を復元", () => _ = RestoreAsync(), true);
        var bar = Ui.Bar(restore, Ui.Button("再読込", Reload), Ui.Button("閉じる", Close));
        var hint = new Label { Dock = DockStyle.Top, Height = 46, Padding = new(8), Text = "削除した用例は出力に含まれません。復元すると元の ID・ステップ・エビデンスが戻ります。\n画像・動画などのファイルは保持されています。" };
        Controls.Add(grid); Controls.Add(hint); Controls.Add(bar); Controls.Add(state);
        grid.SelectionChanged += (_, _) => restore.Enabled = !busy && grid.CurrentRow?.DataBoundItem is DeletedCase;
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Reload();
    }
    private void Reload()
    {
        grid.DataSource = workspace.ListDeletedCases().ToList();
        restore.Enabled = grid.CurrentRow?.DataBoundItem is DeletedCase;
        state.Text = grid.Rows.Count == 0 ? "削除した用例はありません。" : $"{grid.Rows.Count} 件。復元時に証拠ファイルを検証します。";
    }
    private async Task RestoreAsync()
    {
        if (busy || grid.CurrentRow?.DataBoundItem is not DeletedCase selected) return;
        CaseDocument? restored = null;
        busy = true; Enabled = false; UseWaitCursor = true; state.Text = "証拠を検証して復元しています… 動画が大きい場合は時間がかかります。";
        try { restored = await Task.Run(() => workspace.RestoreCase(selected.ArchiveId)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "用例を復元できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; Enabled = true; UseWaitCursor = false; }
        if (restored != null) { Restored = restored; DialogResult = DialogResult.OK; }
        else state.Text = "復元を完了できませんでした。用例は削除一覧に保持されています。";
    }
}
