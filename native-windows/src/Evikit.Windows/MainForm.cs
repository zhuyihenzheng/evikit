using System.ComponentModel;
using System.Diagnostics;
using Evikit.Core;

namespace Evikit.Windows;

internal sealed class MainForm : Form
{
    private Workspace? workspace;
    private CaseDocument? current;
    private bool loading, dirty, exporting;
    private readonly ListBox cases = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, DisplayMember = "Label" };
    private readonly TextBox search = Ui.Text();
    private readonly CheckBox ngOnly = new() { Text = "NG のみ", AutoSize = true, Padding = new(4) };
    private readonly Label projectLabel = new() { Text = "プロジェクト未選択", AutoSize = true, MaximumSize = new(245, 0), Padding = new(10), ForeColor = Ui.Green };
    private readonly Label caseHeading = new() { Text = "プロジェクトを開いてください", Dock = DockStyle.Top, Height = 54, Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, 16, FontStyle.Bold), Padding = new(12), ForeColor = Ui.Green };
    private readonly TextBox title = Ui.Text(), tester = Ui.Text(), date = Ui.Text(), env = Ui.Text(), precondition = Ui.Text("", true), note = Ui.Text("", true);
    private readonly ComboBox verdict = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DataGridView steps = Ui.Grid(), evidence = Ui.Grid(true);
    private readonly Panel preview = new() { Dock = DockStyle.Fill, Padding = new(8), BackColor = Color.White };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel status = new("プロジェクトを開く、または新規作成してください。"), dirtyLabel = new();
    private string? archive;
    private string? videoPlayer;
    private List<CaseDocument> caseList = [];
    private sealed record CaseItem(string Id, string Label);

    public MainForm(string? initialPath)
    {
        Text = "evikit Desktop — テスト証拠ワークスペース"; Font = SystemFonts.MessageBoxFont; BackColor = Ui.Background; StartPosition = FormStartPosition.CenterScreen; ClientSize = new(1280, 850); MinimumSize = new(1000, 700); AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
        var bar = Ui.Bar(Ui.Button("プロジェクトを開く…", OpenProject), Ui.Button("新規プロジェクト…", CreateProject), Ui.Button("プロジェクト設定", ProjectSettings), Ui.Button("保存  Ctrl+S", Save, true), Ui.Button("再読込", Reload), Ui.Button("連続スクリーンショット", StartCapture, true), Ui.Button("成果物を出力", () => _ = ExportAsync(), true));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, ColumnStyles = { new(SizeType.Absolute, 265), new(SizeType.Percent, 100) }, Padding = new(8) };
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(8) };
        var filter = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 }; filter.Controls.Add(projectLabel); search.PlaceholderText = "ID・タイトルで検索"; filter.Controls.Add(search); filter.Controls.Add(ngOnly);
        var newCase = Ui.Bar(Ui.Button("＋ 用例を追加", AddCase)); newCase.Dock = DockStyle.Bottom;
        sidebar.Controls.Add(cases); sidebar.Controls.Add(filter); sidebar.Controls.Add(newCase);
        var editor = new Panel { Dock = DockStyle.Fill, Padding = new(8, 0, 0, 0) }; editor.Controls.Add(tabs); editor.Controls.Add(caseHeading);
        layout.Controls.Add(sidebar, 0, 0); layout.Controls.Add(editor, 1, 0);
        var basic = new TabPage("用例の情報") { BackColor = Color.White }; var fields = Ui.Fields();
        Ui.Field(fields, "タイトル", title); Ui.Field(fields, "担当者", tester); Ui.Field(fields, "日付", date); Ui.Field(fields, "環境", env); Ui.Field(fields, "判定（空欄＝自動）", verdict, 48); Ui.Field(fields, "前提条件", precondition, 120); Ui.Field(fields, "備考", note, 150); basic.Controls.Add(fields);
        var stepPage = new TabPage("テストステップ"); Ui.Column(steps, "No.", "No", 28, true); Ui.Column(steps, "操作", "Action", 170); Ui.Column(steps, "テスト条件", "Condition", 150); Ui.Column(steps, "期待結果", "Expected", 150); Ui.Column(steps, "実際結果", "Actual", 150); Ui.Column(steps, "判定", "Verdict", 60);
        stepPage.Controls.Add(steps); stepPage.Controls.Add(Ui.Bar(Ui.Button("＋ ステップ", AddStep), Ui.Button("テスト条件…", EditStepCondition), Ui.Button("削除", DeleteStep), Ui.Button("↑", () => MoveStep(-1)), Ui.Button("↓", () => MoveStep(1))));
        var evidencePage = new TabPage("証拠・プレビュー");
        Ui.Column(evidence, "ID", "Id", 35); Ui.Column(evidence, "種類", "Kind", 45); Ui.Column(evidence, "分類", "Category", 45); Ui.Column(evidence, "見出し", "Caption", 150); Ui.Column(evidence, "Step", "Step", 35);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(800, 600), Orientation = Orientation.Horizontal, SplitterDistance = 200, Panel1MinSize = 100, Panel2MinSize = 140 }; split.Panel1.Controls.Add(evidence); split.Panel2.Controls.Add(preview);
        var evidenceBar = Ui.Bar(Ui.Button("＋ 証拠", () => AddEvidence()), Ui.Button("＋ 動画", AddVideo), Ui.Button("動画を開く", () => _ = OpenVideoAsync()), Ui.Button("動画の確認画像", AddVideoFrame), Ui.Button("貼り付け", PasteEvidence), Ui.Button("画像をまとめて整理", ReviewImages), Ui.Button("情報編集", EditEvidence), Ui.Button("画像に注釈", Annotate), Ui.Button("原図に戻す", ResetImage), Ui.Button("削除", DeleteEvidence), Ui.Button("削除を復元", () => _ = RestoreEvidenceAsync()), Ui.Button("証拠を保存…", () => _ = SaveEvidenceAsync()));
        evidencePage.Controls.Add(split); evidencePage.Controls.Add(evidenceBar);
        tabs.TabPages.AddRange([basic, stepPage, evidencePage]); tabs.Enabled = false;
        var statusBar = new StatusStrip(); status.Spring = true; status.TextAlign = ContentAlignment.MiddleLeft; statusBar.Items.AddRange([status, dirtyLabel]);
        Controls.Add(layout); Controls.Add(bar); Controls.Add(statusBar);
        cases.SelectedIndexChanged += (_, _) => SwitchCase(); search.TextChanged += (_, _) => FilterCases(); ngOnly.CheckedChanged += (_, _) => FilterCases();
        foreach (var control in new Control[] { title, tester, date, env, precondition, note, verdict }) control.TextChanged += (_, _) => MarkDirty();
        steps.CellValueChanged += (_, _) => MarkDirty(); steps.CurrentCellDirtyStateChanged += (_, _) => { if (steps.IsCurrentCellDirty) steps.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        steps.DataError += (_, e) => { e.ThrowException = false; status.Text = "ステップの入力内容を確認してください。"; };
        evidence.SelectionChanged += (_, _) => { if (!loading) Ui.Guard(PreviewEvidence); };
        AllowDrop = true; DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files) { if (Directory.Exists(file)) continue; await AddEvidenceAsync(file); } };
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; Ui.Guard(Save); } else if (e.Control && e.Shift && e.KeyCode == Keys.V) { e.SuppressKeyPress = true; Ui.Guard(PasteEvidence); } };
        FormClosing += (_, e) => { if (exporting) { e.Cancel = true; status.Text = "保存・検証・出力が完了するまでお待ちください。"; return; } try { if (!ResolveDraft()) e.Cancel = true; } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } if (!e.Cancel) workspace?.Dispose(); };
        Shown += (_, _) => { if (initialPath != null) Ui.Guard(() => LoadProject(initialPath)); };
    }
    private void NeedCase() { if (current == null || workspace == null) throw new InvalidOperationException("用例を選択してください。"); }
    private void MarkDirty() { if (!loading && current != null) { dirty = true; dirtyLabel.Text = "● 未保存"; } }
    private void OpenProject()
    {
        using var d = new FolderBrowserDialog { Description = "project.yaml のあるフォルダーを選択", UseDescriptionForTitle = true };
        if (d.ShowDialog(this) == DialogResult.OK) LoadProject(d.SelectedPath);
    }
    private void CreateProject()
    {
        var name = Ui.Prompt(this, "新規プロジェクト", "プロジェクト名"); if (name == null) return;
        using var d = new FolderBrowserDialog { Description = "新規プロジェクトの保存先フォルダーを選択", UseDescriptionForTitle = true };
        if (d.ShowDialog(this) != DialogResult.OK || !ResolveDraft()) return;
        Workspace.Create(d.SelectedPath, name); LoadProject(d.SelectedPath);
    }
    private void LoadProject(string path)
    {
        if (!ResolveDraft()) return;
        if (workspace != null && Path.GetFullPath(path).Equals(workspace.Root, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }
        var opened = new Workspace(path); workspace?.Dispose(); workspace = opened; current = null; archive = null;
        var p = workspace.LoadProject().Data; projectLabel.Text = p.Name; Text = p.Name + " — evikit Desktop";
        verdict.Items.Clear(); verdict.Items.Add(""); verdict.Items.AddRange(p.Verdicts.ToArray());
        caseList = workspace.ListCases(); FilterCases(); if (cases.Items.Count > 0) cases.SelectedIndex = 0; else ClearCase(); status.Text = path;
    }
    private void FilterCases()
    {
        loading = true;
        try
        {
            string? selected = current?.Data.Id; cases.Items.Clear();
            foreach (var doc in caseList.Where(c => (!ngOnly.Checked || Contract.Verdict(c.Data) == "NG") && (c.Data.Id + " " + c.Data.Title).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var item = new CaseItem(doc.Data.Id, $"{doc.Data.Id}  [{Contract.Verdict(doc.Data)}]  {doc.Data.Title}"); cases.Items.Add(item); if (item.Id == selected) cases.SelectedItem = item;
            }
        }
        finally { loading = false; }
    }
    private void SwitchCase()
    {
        if (loading || cases.SelectedItem is not CaseItem item || item.Id == current?.Data.Id) return;
        Ui.Guard(() => { try { if (!ResolveDraft()) { FilterCases(); return; } LoadCase(workspace!.LoadCase(item.Id)); } catch { FilterCases(); throw; } });
    }
    private bool ResolveDraft()
    {
        if (!dirty) return true;
        var result = MessageBox.Show(this, "変更を保存しますか？\n「いいえ」は現在の草稿を破棄します。", "未保存の変更", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false; if (result == DialogResult.Yes) Save();
        if (result == DialogResult.No && current != null && workspace != null) LoadCase(workspace.LoadCase(current.Data.Id));
        return true;
    }
    private void ClearCase() { current = null; dirty = false; dirtyLabel.Text = ""; tabs.Enabled = false; caseHeading.Text = "「＋ 用例を追加」から開始してください"; }
    private void LoadCase(CaseDocument doc)
    {
        loading = true;
        try
        {
            current = doc; var c = doc.Data; title.Text = c.Title; tester.Text = c.Tester; date.Text = c.Date; env.Text = c.Env; verdict.Text = c.Verdict; precondition.Text = c.Precondition; note.Text = c.Note;
            steps.DataSource = new BindingList<Step>(c.Steps); evidence.DataSource = new BindingList<Evidence>(c.Evidence);
            caseHeading.Text = c.Id + "  /  " + c.Title; tabs.Enabled = true; dirty = false; dirtyLabel.Text = "保存済み";
        }
        finally { loading = false; }
        PreviewEvidence();
    }
    private void CaptureDraft()
    {
        NeedCase(); Validate(); steps.EndEdit(); if (steps.DataSource != null) BindingContext?[steps.DataSource]?.EndCurrentEdit();
        var c = current!.Data; c.Title = title.Text; c.Tester = tester.Text; c.Date = date.Text; c.Env = env.Text; c.Verdict = verdict.Text; c.Precondition = precondition.Text; c.Note = note.Text;
    }
    private void Save()
    {
        NeedCase(); if (!dirty) return; CaptureDraft(); var saved = workspace!.SaveCase(current!); int tab = tabs.SelectedIndex;
        LoadCase(saved); tabs.SelectedIndex = tab; caseList = workspace.ListCases(); FilterCases(); status.Text = "保存しました。";
    }
    private void Reload()
    {
        if (workspace == null || !ResolveDraft()) return;
        string? id = current?.Data.Id; caseList = workspace.ListCases();
        if (id != null && caseList.Any(c => c.Data.Id == id)) LoadCase(workspace.LoadCase(id)); else ClearCase();
        FilterCases(); status.Text = "ディスクから再読込しました。";
    }
    private void AddCase()
    {
        if (workspace == null) throw new InvalidOperationException("プロジェクトを開いてください。"); if (!ResolveDraft()) return;
        int n = 1; while (caseList.Any(c => c.Data.Id.Equals($"TC-{n:000}", StringComparison.OrdinalIgnoreCase))) n++;
        var id = Ui.Prompt(this, "用例を追加", "用例 ID", $"TC-{n:000}"); if (id == null) return;
        var name = Ui.Prompt(this, "用例を追加", "タイトル"); if (name == null) return;
        LoadCase(workspace.CreateCase(id, name)); caseList = workspace.ListCases(); search.Text = ""; ngOnly.Checked = false; FilterCases(); tabs.SelectedIndex = 0;
    }
    private void ProjectSettings()
    {
        if (workspace == null) throw new InvalidOperationException("プロジェクトを開いてください。");
        var doc = workspace.LoadProject(); using var f = new Form { Text = "プロジェクト設定", Font = Font, StartPosition = FormStartPosition.CenterParent, ClientSize = new(550, 310) };
        var name = Ui.Text(doc.Data.Name); var person = Ui.Text(doc.Data.Tester); var environment = Ui.Text(doc.Data.Env);
        var lines = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = doc.Data.ExcerptLines, Dock = DockStyle.Fill };
        var width = new NumericUpDown { Minimum = 100, Maximum = 2000, Value = doc.Data.ImageMaxWidth, Dock = DockStyle.Fill };
        var fields = Ui.Fields(); Ui.Field(fields, "名称", name); Ui.Field(fields, "既定担当者", person); Ui.Field(fields, "既定環境", environment); Ui.Field(fields, "ログ抜粋行", lines); Ui.Field(fields, "画像最大幅", width);
        var bar = Ui.Bar(Ui.Button("保存", () => { doc.Data.Name = name.Text; doc.Data.Tester = person.Text; doc.Data.Env = environment.Text; doc.Data.ExcerptLines = (int)lines.Value; doc.Data.ImageMaxWidth = (int)width.Value; workspace.SaveProject(doc); projectLabel.Text = name.Text; f.DialogResult = DialogResult.OK; }, true)); bar.Dock = DockStyle.Bottom; f.Controls.Add(fields); f.Controls.Add(bar); f.ShowDialog(this);
    }
    private void AddStep()
    {
        NeedCase(); CaptureDraft(); current!.Data.Steps.Add(new Step { No = current.Data.Steps.Select(s => s.No).DefaultIfEmpty(0).Max() + 1 });
        ((BindingList<Step>)steps.DataSource!).ResetBindings(); MarkDirty(); steps.CurrentCell = steps.Rows[^1].Cells[1];
    }
    private void EditStepCondition()
    {
        NeedCase(); if (steps.CurrentRow?.DataBoundItem is not Step selected) throw new InvalidOperationException("ステップを選択してください。");
        CaptureDraft();
        using var dialog = new Form { Text = $"Step {selected.No} — テスト条件", Font = Font, ClientSize = new(600, 300), StartPosition = FormStartPosition.CenterParent };
        var input = Ui.Text(selected.Condition ?? "", true); input.PlaceholderText = "例：権限＝管理者\r\n顧客 ID＝00012\r\n登録前の状態＝未登録";
        var fields = Ui.Fields(); Ui.Field(fields, "テスト条件", input, 225);
        var bar = Ui.Bar(Ui.Button("反映", () => dialog.DialogResult = DialogResult.OK, true), Ui.Button("キャンセル", () => dialog.DialogResult = DialogResult.Cancel));
        bar.Dock = DockStyle.Bottom; dialog.Controls.Add(fields); dialog.Controls.Add(bar);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        selected.Condition = string.IsNullOrEmpty(input.Text) ? null : input.Text;
        ((BindingList<Step>)steps.DataSource!).ResetBindings(); MarkDirty();
    }
    private void DeleteStep()
    {
        NeedCase(); if (steps.CurrentRow?.DataBoundItem is not Step s) return;
        if (MessageBox.Show(this, "ステップを削除しますか？関連する証拠は「共通」に移動します。", "ステップ削除", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        CaptureDraft(); current!.Data.Steps.Remove(s); foreach (var e in current.Data.Evidence.Where(e => e.Step == s.No)) e.Step = null; Renumber();
    }
    private void MoveStep(int offset)
    {
        NeedCase(); if (steps.CurrentRow?.DataBoundItem is not Step s) return; CaptureDraft();
        var list = current!.Data.Steps; int old = list.IndexOf(s), next = old + offset; if (next < 0 || next >= list.Count) return;
        (list[old], list[next]) = (list[next], list[old]); Renumber(); steps.CurrentCell = steps.Rows[next].Cells[1];
    }
    private void Renumber()
    {
        var c = current!.Data; var mapping = c.Steps.Select((s, i) => (s.No, New: i + 1)).ToDictionary(x => x.No, x => x.New);
        foreach (var e in c.Evidence) if (e.Step != null) e.Step = mapping[e.Step.Value];
        for (int i = 0; i < c.Steps.Count; i++) c.Steps[i].No = i + 1;
        ((BindingList<Step>)steps.DataSource!).ResetBindings(); ((BindingList<Evidence>)evidence.DataSource!).ResetBindings(); MarkDirty();
    }
    private Evidence SelectedEvidence() { NeedCase(); return evidence.CurrentRow?.DataBoundItem as Evidence ?? throw new InvalidOperationException("証拠を選択してください。"); }
    private void AddEvidence(string? file = null, byte[]? image = null, string? text = null)
        => _ = AddEvidenceAsync(file, image, text);
    private async Task AddEvidenceAsync(string? file = null, byte[]? image = null, string? text = null, Evidence? video = null, string timestamp = "")
    {
        if (exporting) return;
        try
        {
            NeedCase(); Save(); using var d = new EvidenceDialog(current!.Data, file: file, clipboardImage: image, clipboardText: text);
            if (video != null) d.SetVideoFrame(video, timestamp);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var doc = current; var input = d.Result!;
            if (video != null && input.Kind != "image") throw new InvalidDataException("動画の確認画像には image を選択してください。");
            var saved = await RunStorageAsync(() => workspace!.AddEvidence(doc!, input), "証拠をコピー・検証しています…");
            LoadCase(saved); tabs.SelectedIndex = 2; evidence.CurrentCell = evidence.Rows[^1].Cells[0];
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "証拠を追加できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private async Task<T> RunStorageAsync<T>(Func<T> action, string message)
    {
        if (exporting) throw new InvalidOperationException("現在の処理が完了するまでお待ちください。");
        exporting = true; Enabled = false; UseWaitCursor = true; status.Text = message;
        try { var result = await Task.Run(action); status.Text = "完了しました。"; return result; }
        finally { exporting = false; Enabled = true; UseWaitCursor = false; }
    }
    private void AddVideo()
    {
        NeedCase(); using var d = new OpenFileDialog { Title = "動画を追加（最大 2 GiB）", Filter = "動画|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.m4v" };
        if (d.ShowDialog(this) == DialogResult.OK) AddEvidence(d.FileName);
    }
    private Evidence SelectedVideo()
    {
        var video = SelectedEvidence();
        if (video.Kind != "file" || !Media.IsVideo(video.File)) throw new InvalidOperationException("動画の証拠を選択してください。");
        return video;
    }
    private async Task OpenVideoAsync()
    {
        if (exporting) return;
        try
        {
            var video = SelectedVideo(); Save(); string caseId = current!.Data.Id;
            if (videoPlayer == null || !File.Exists(videoPlayer))
            {
                using var picker = new OpenFileDialog { Title = "会社で許可されたローカル動画プレーヤーを選択（実行ファイル）", Filter = "プレーヤー|*.exe" };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (!Path.GetExtension(picker.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("プレーヤーの .exe を選択してください。");
                videoPlayer = Path.GetFullPath(picker.FileName);
            }
            string path = await RunStorageAsync(() => { workspace!.VerifyEvidence(caseId, video); return workspace.EvidencePath(caseId, video); }, "動画の SHA-256 を検証しています…");
            // Launch the explicitly chosen player with a separate argument, never a browser association or shell command.
            Process.Start(new ProcessStartInfo(videoPlayer) { UseShellExecute = false, ArgumentList = { path } });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message + "\n対応するローカルプレーヤーが必要です。", "動画を開けませんでした"); }
    }
    private void AddVideoFrame()
    {
        var video = Contract.Clone(SelectedVideo());
        using var d = new OpenFileDialog { Title = "動画の確認画像を選択（再生画面のスクリーンショット）", Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        string? time = Ui.Prompt(this, "動画の確認位置", "時間（例 00:35）", "00:00"); if (time == null) return;
        _ = AddEvidenceAsync(d.FileName, video: video, timestamp: time);
    }
    private void PasteEvidence()
    {
        NeedCase(); if (Clipboard.ContainsImage()) { using var image = Clipboard.GetImage(); if (image != null) { if ((long)image.Width * image.Height > 80_000_000) throw new InvalidDataException("画像が大きすぎます。"); AddEvidence(image: Ui.Png(image)); } }
        else if (Clipboard.ContainsText()) AddEvidence(text: Clipboard.GetText());
        else throw new InvalidOperationException("クリップボードに画像・テキストがありません。");
    }
    private void StartCapture()
    {
        NeedCase();
        int? selectedStep = tabs.SelectedIndex == 1 && steps.CurrentRow?.DataBoundItem is Step s ? s.No : null;
        Save();
        using var session = new CaptureSessionForm(workspace!, current!, selectedStep);
        // A modal session fixes the project/case; hide the main window before any screenshot.
        Hide();
        try { session.ShowDialog(); }
        finally
        {
            Show(); Activate(); LoadCase(workspace!.LoadCase(current!.Data.Id));
            caseList = workspace.ListCases(); FilterCases(); tabs.SelectedIndex = 2;
        }
        if (current!.Data.Evidence.Any(e => e.Kind == "image")) ReviewImages();
    }
    private void ReviewImages()
    {
        NeedCase(); Save();
        using var review = new ImageReviewForm(workspace!, current!);
        try { review.ShowDialog(this); }
        finally { LoadCase(workspace!.LoadCase(current!.Data.Id)); tabs.SelectedIndex = 2; }
    }
    private void EditEvidence()
    {
        string id = SelectedEvidence().Id; Save(); var e = current!.Data.Evidence.Single(e => e.Id == id);
        using var d = new EvidenceDialog(current.Data, e); if (d.ShowDialog(this) != DialogResult.OK) return;
        current.Data.Evidence[current.Data.Evidence.IndexOf(e)] = d.Metadata!; LoadCase(workspace!.SaveCase(current));
    }
    private void Annotate()
    {
        string id = SelectedEvidence().Id; Save(); var e = current!.Data.Evidence.Single(e => e.Id == id); if (e.Kind != "image") throw new InvalidOperationException("画像を選択してください。");
        using var d = new AnnotationForm(workspace!.ReadEvidence(current.Data.Id, e, true), e.Annotations);
        if (d.ShowDialog(this) == DialogResult.OK) LoadCase(workspace.SaveAnnotation(current, id, d.Result!, d.Annotation));
    }
    private void ResetImage()
    {
        string id = SelectedEvidence().Id; Save(); var e = current!.Data.Evidence.Single(e => e.Id == id); if (e.Kind != "image") throw new InvalidOperationException("画像を選択してください。");
        if (MessageBox.Show(this, "現在の注釈を外して原図に戻しますか？画像ファイルは保持されます。", "原図に戻す", MessageBoxButtons.OKCancel) == DialogResult.OK) LoadCase(workspace!.ResetAnnotation(current, id));
    }
    private void DeleteEvidence()
    {
        string id = SelectedEvidence().Id;
        if (MessageBox.Show(this, id + " を削除しますか？ファイルは保持され、復元できます。", "証拠を削除", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        Save(); var result = workspace!.DeleteEvidence(current!, id); archive = result.ArchiveId; LoadCase(result.Document); status.Text = "削除しました。「削除を復元」で元に戻せます。";
    }
    private async Task RestoreEvidenceAsync()
    {
        if (exporting) return;
        try
        {
            NeedCase(); if (archive == null) throw new InvalidOperationException("このセッションで削除した証拠はありません。"); Save(); string id = archive;
            var restored = await RunStorageAsync(() => workspace!.RestoreEvidence(id), "証拠を検証・復元しています…");
            archive = null; LoadCase(restored); caseList = workspace!.ListCases(); FilterCases();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "復元できませんでした"); }
    }
    private void DisposePreview()
    {
        foreach (Control c in preview.Controls.Cast<Control>().ToArray()) { if (c is PictureBox p) p.Image?.Dispose(); c.Dispose(); } preview.Controls.Clear();
    }
    private void PreviewEvidence()
    {
        DisposePreview(); if (current == null || workspace == null || evidence.CurrentRow?.DataBoundItem is not Evidence e) return;
        try
        {
            if (e.Kind == "file")
            {
                string path = workspace.EvidencePath(current.Data.Id, e); long size = new FileInfo(path).Length;
                bool video = Media.IsVideo(e.File);
                if (video)
                {
                    var options = Ui.Bar(Ui.Button("プレーヤーを変更", () => { videoPlayer = null; status.Text = "次回「動画を開く」でプレーヤーを選択してください。"; }));
                    options.Dock = DockStyle.Bottom; preview.Controls.Add(options);
                }
                preview.Controls.Add(new Label { Text = $"{(video ? "動画" : "添付ファイル")}  {e.Id}\n\n{e.Caption}\n{e.OriginalName}\n{size / 1048576d:N1} MiB\n\n確認事項：{e.Note}\n\n" + (video ? "「動画を開く」でローカルプレーヤーを起動します。\n「動画の確認画像」で時間点付きのスクリーンショットを追加できます。\nExcel には動画リンク・説明・確認画像を出力します。" : "「証拠を保存…」で取り出せます。") + "\n\n内容の SHA-256 は再生・取り出し・出力時に検証します。", Dock = DockStyle.Fill, Padding = new(18), AutoEllipsis = true });
                return;
            }
            byte[] bytes = workspace.ReadEvidence(current.Data.Id, e);
            if (e.Kind == "image") preview.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = Ui.Decode(bytes), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(241, 244, 242) });
            else if (e.Kind == "table")
            {
                var grid = Ui.Grid(true); var rows = Tables.Parse(Inputs.Utf8(bytes)); for (int i = 0; i < rows[0].Count; i++) grid.Columns.Add("c" + i, rows[0][i]);
                foreach (var row in rows.Skip(1).Take(500)) grid.Rows.Add(row.Cast<object>().ToArray()); grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells; preview.Controls.Add(grid);
                if (rows.Count > 501) preview.Controls.Add(new Label { Text = "先頭 500 行を表示（出力は全行）", Dock = DockStyle.Bottom });
            }
            else if (e.Kind == "text") { var box = Ui.Text(string.Join(Environment.NewLine, Tables.Lines(Inputs.Utf8(bytes)).Take(1000).Select((line, i) => $"{i + 1,4}  {line}")), true); box.ReadOnly = true; box.Font = new Font("Consolas", 10); preview.Controls.Add(box); }
            else preview.Controls.Add(new Label { Text = $"添付ファイル\n\n{e.OriginalName}\n{bytes.Length:N0} bytes\n\n「証拠を保存…」で取り出せます。", Dock = DockStyle.Fill, Padding = new(18) });
            preview.Controls.Add(new Label { Text = $"{e.Id}  {e.Caption}    {e.CapturedAt}\n出典：{e.Source}\n確認事項：{e.Note}", Dock = DockStyle.Top, Height = 74, AutoEllipsis = true, Padding = new(4) });
        }
        catch (Exception ex) { preview.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = ex.Message }); }
    }
    private async Task SaveEvidenceAsync()
    {
        if (exporting) return;
        try
        {
        var e = SelectedEvidence(); using var d = new SaveFileDialog { FileName = e.OriginalName == "" ? e.File : e.OriginalName, Title = "証拠ファイルを保存" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        string destination = Path.GetFullPath(d.FileName), root = workspace!.Root + Path.DirectorySeparatorChar;
        if (destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("証拠の取り出し先はプロジェクト外を選択してください。");
        string caseId = current!.Data.Id;
        await RunStorageAsync(() => { workspace.CopyEvidence(caseId, e, destination, false); return true; }, "証拠をコピー・検証しています…");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "証拠を保存できませんでした"); }
    }
    private async Task ExportAsync()
    {
        if (exporting) return;
        try
        {
            if (workspace == null) throw new InvalidOperationException("プロジェクトを開いてください。"); if (current != null) Save();
            using var d = new FolderBrowserDialog { Description = "成果物の出力先（新しい日時フォルダーを作成します）", UseDescriptionForTitle = true, SelectedPath = workspace.Root };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string result = await RunStorageAsync(() => Export.Deliver(workspace, d.SelectedPath), "Excel・添付・ZIP を出力しています…");
            status.Text = "出力完了：" + result;
            // Open only Explorer, never an HTML file or browser.
            if (MessageBox.Show(this, "Excel・添付・ZIP を出力しました。\n\n" + result + "\n\n保存先フォルダーを開きますか？", "出力完了", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { result }, UseShellExecute = false });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "出力できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { exporting = false; Enabled = true; UseWaitCursor = false; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { DisposePreview(); workspace?.Dispose(); } base.Dispose(disposing); }
}
