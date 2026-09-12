using Evikit.Core;

namespace Evikit.Windows;

internal sealed class CaptureSessionForm : Form
{
    private readonly Workspace workspace;
    private readonly CaptureInbox inbox;
    private readonly ComboBox step = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox shortcuts = new() { Width = 275, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label state = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new(10), ForeColor = Ui.Green };
    private readonly Button capture, repeat, pause, retry, recover, rescue;
    private readonly FlowLayoutPanel targetBar, captureBar;
    private PendingCapture? pending;
    private Rectangle? lastRegion, lastDesktop;
    private bool busy, paused, ready;
    private int savedCount;
    public CaseDocument Document { get; private set; }
    private sealed record StepChoice(int? No, string Label);
    private sealed record HotKeys(string Label, uint Modifiers, Keys Region, Keys Repeat);

    public CaptureSessionForm(Workspace workspace, CaseDocument document, int? initialStep)
    {
        this.workspace = workspace; inbox = new(workspace); Document = document;
        Text = $"{document.Data.Id} — 連続スクリーンショット";
        Font = SystemFonts.MessageBoxFont; AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(770, 300); MinimumSize = new(720, 330); StartPosition = FormStartPosition.CenterScreen;
        TopMost = true; MaximizeBox = false; BackColor = Ui.Background;
        step.DisplayMember = "Label"; step.Items.Add(new StepChoice(null, "共通（後でステップを指定）"));
        foreach (var s in document.Data.Steps) step.Items.Add(new StepChoice(s.No, $"Step {s.No}  {s.Action}"));
        step.SelectedIndex = 0;
        for (int i = 0; i < step.Items.Count; i++) if (step.Items[i] is StepChoice choice && choice.No == initialStep) step.SelectedIndex = i;
        shortcuts.DisplayMember = "Label";
        shortcuts.Items.AddRange([new HotKeys("Ctrl+Alt+S：範囲 / Ctrl+Alt+R：再撮影", CaptureNative.ControlAlt, Keys.S, Keys.R),
            new HotKeys("Ctrl+Shift+F8 / Ctrl+Shift+F9", CaptureNative.ControlShift, Keys.F8, Keys.F9)]);
        shortcuts.SelectedIndex = 0;
        targetBar = Ui.Bar(new Label { Text = "保存先：" + document.Data.Id, AutoSize = true, Padding = new(4, 7, 0, 0) }, step,
            Ui.Button("次の Step →", () => { if (step.SelectedIndex + 1 < step.Items.Count) step.SelectedIndex++; }));
        capture = Ui.Button("範囲を撮影", () => _ = TakeAsync(false), true);
        repeat = Ui.Button("同じ範囲を撮影", () => _ = TakeAsync(true));
        pause = Ui.Button("一時停止", TogglePause);
        captureBar = Ui.Bar(capture, repeat, pause, Ui.Button("終了して画像を整理", Close, true));
        var shortcutBar = Ui.Bar(new Label { Text = "ショートカット", AutoSize = true, Padding = new(4, 7, 0, 0) }, shortcuts);
        retry = Ui.Button("保存を再試行", () => _ = RetryAsync());
        recover = Ui.Button("未完了の撮影を回収", () => _ = RecoverAsync());
        rescue = Ui.Button("未保存画像を書き出す…", Rescue);
        var recoveryBar = Ui.Bar(retry, recover, rescue); recoveryBar.Dock = DockStyle.Bottom;
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        header.Controls.Add(targetBar); header.Controls.Add(captureBar); header.Controls.Add(shortcutBar);
        Controls.Add(state); Controls.Add(header); Controls.Add(recoveryBar);
        shortcuts.SelectedIndexChanged += (_, _) => { if (ready) RegisterKeys(); };
        Shown += (_, _) =>
        {
            ready = true; RegisterKeys();
            try { var n = inbox.PendingTokens(Document.Data.Id).Count; SetState(n == 0 ? "撮影待機中。画像は 1 枚ずつ自動保存されます。" : $"未完了の撮影が {n} 件あります。「回収」で保存を再開できます。"); }
            catch (Exception ex) { SetState(ex.Message, true); }
            UpdateButtons();
        };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; return; }
            if (pending != null)
            {
                e.Cancel = true;
                MessageBox.Show(this, "未保存の画像があります。「保存を再試行」または「未保存画像を書き出す…」で保存してから終了してください。", "画像を保持しています");
            }
        };
        FormClosed += (_, _) => ReleaseKeys();
        UpdateButtons();
    }

    private void SetState(string message, bool error = false)
    {
        state.ForeColor = error ? Color.Firebrick : Ui.Green;
        state.Text = $"このセッション：{savedCount} 枚保存済み\n{message}";
    }
    protected override bool ShowWithoutActivation => true;
    private void UpdateButtons()
    {
        capture.Enabled = repeat.Enabled = !busy && !paused && pending == null;
        repeat.Enabled &= lastRegion.HasValue;
        targetBar.Enabled = shortcuts.Enabled = pause.Enabled = !busy && pending == null;
        retry.Enabled = rescue.Enabled = !busy && pending != null;
        recover.Enabled = !busy && pending == null;
        pause.Text = paused ? "撮影を再開" : "一時停止";
    }
    private void TogglePause()
    {
        paused = !paused; RegisterKeys(); UpdateButtons();
        SetState(paused ? "一時停止中。ショートカットを解除しました。" : "撮影を再開しました。");
    }
    private void ReleaseKeys()
    {
        if (!IsHandleCreated) return;
        CaptureNative.UnregisterHotKey(Handle, 1); CaptureNative.UnregisterHotKey(Handle, 2);
    }
    private void RegisterKeys()
    {
        ReleaseKeys(); if (paused) return;
        var keys = (HotKeys)shortcuts.SelectedItem!;
        bool regionOk = CaptureNative.RegisterHotKey(Handle, 1, keys.Modifiers | CaptureNative.NoRepeat, (uint)keys.Region);
        bool repeatOk = CaptureNative.RegisterHotKey(Handle, 2, keys.Modifiers | CaptureNative.NoRepeat, (uint)keys.Repeat);
        if (!regionOk || !repeatOk)
        {
            ReleaseKeys();
            MessageBox.Show(this, "ショートカットが使用中です。別の組み合わせを選ぶか、撮影ボタンを使用してください。", "ショートカットを登録できません");
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == CaptureNative.HotKeyMessage)
        {
            if (!busy && !paused && pending == null) _ = TakeAsync(m.WParam.ToInt32() == 2);
            return;
        }
        base.WndProc(ref m);
    }

    private async Task TakeAsync(bool sameRegion)
    {
        if (busy || paused || pending != null) return;
        busy = true; UpdateButtons();
        // Freeze the destination before hiding the toolbar or awaiting anything.
        int? target = ((StepChoice)step.SelectedItem!).No;
        try
        {
            var desktopBounds = SystemInformation.VirtualScreen;
            if (sameRegion && (lastRegion == null || lastDesktop != desktopBounds || !desktopBounds.Contains(lastRegion.Value)))
                throw new InvalidOperationException("画面構成が変わりました。「範囲を撮影」で範囲を選び直してください。");
            Hide(); await Task.Delay(200);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
            byte[] png;
            if (sameRegion)
            {
                using var bitmap = CaptureNative.ReadScreen(lastRegion!.Value); png = Ui.Png(bitmap);
            }
            else
            {
                using var desktop = CaptureNative.ReadScreen(desktopBounds);
                using var selection = new CaptureRegionForm(desktop, desktopBounds);
                if (selection.ShowDialog() != DialogResult.OK) { SetState("撮影をキャンセルしました。"); return; }
                lastRegion = new Rectangle(selection.Selection.X + desktopBounds.X, selection.Selection.Y + desktopBounds.Y, selection.Selection.Width, selection.Selection.Height);
                lastDesktop = desktopBounds;
                using var cropped = desktop.Clone(selection.Selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                png = Ui.Png(cropped);
            }
            pending = new(Guid.NewGuid().ToString("N"), Document.Data.Id, target, $"スクリーンショット {savedCount + 1:000}", timestamp, png);
            Show(); SetState("画像を保存しています…");
            await CommitPendingAsync();
        }
        catch (Exception ex) { SetState("撮影・保存できませんでした：" + ex.Message, true); }
        finally { Show(); busy = false; UpdateButtons(); }
    }

    private async Task CommitPendingAsync()
    {
        var image = pending ?? throw new InvalidOperationException("未保存の画像はありません。");
        var saved = await Task.Run(() => inbox.Commit(workspace.LoadCase(Document.Data.Id), image));
        Document = saved; pending = null;
        var evidence = saved.Data.Evidence.SingleOrDefault(e => (e.OriginalFile ?? e.File) == image.FileName);
        if (evidence == null) { SetState("削除済みの撮影の回収記録を整理しました。画像は再追加していません。"); return; }
        savedCount++;
        SetState($"{evidence.Id} を保存しました。{(evidence.Step == null ? "共通" : "Step " + evidence.Step)} / 説明は終了後にまとめて入力できます。");
    }
    private async Task RetryAsync()
    {
        if (busy || pending == null) return;
        busy = true; UpdateButtons();
        try { await CommitPendingAsync(); }
        catch (Exception ex) { SetState("未保存画像を保持しています：" + ex.Message, true); }
        finally { busy = false; UpdateButtons(); }
    }
    private async Task RecoverAsync()
    {
        if (busy || pending != null) return;
        busy = true; UpdateButtons();
        try
        {
            var tokens = await Task.Run(() => inbox.PendingTokens(Document.Data.Id));
            foreach (var token in tokens)
            {
                pending = await Task.Run(() => inbox.Load(token));
                await CommitPendingAsync();
            }
            if (tokens.Count == 0) SetState("未完了の撮影はありません。");
        }
        catch (Exception ex) { SetState("回収を停止しました。画像は保持しています：" + ex.Message, true); }
        finally { busy = false; UpdateButtons(); }
    }
    private void Rescue()
    {
        if (pending == null) return;
        using var dialog = new SaveFileDialog { FileName = pending.FileName, Filter = "PNG 画像|*.png", Title = "未保存画像を別の場所に保存" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = Path.GetFullPath(dialog.FileName);
        if (destination.StartsWith(workspace.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("画像の退避先はプロジェクト外を選択してください。");
        Files.Atomic(destination, pending.Png);
        pending = null; paused = true; ReleaseKeys(); UpdateButtons();
        SetState("PNG を退避しました。証拠への追加は未完了です。回収記録がある場合は後で「回収」できます。");
    }
}
