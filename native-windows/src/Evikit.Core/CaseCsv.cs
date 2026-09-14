using System.Globalization;

namespace Evikit.Core;

public static class CaseCsv
{
    private static string Header(string text) => text.Trim().Replace(" ", "").Replace("　", "").Replace("_", "");
    private static readonly Dictionary<string, string> Headers = MakeHeaders();
    private static Dictionary<string, string> MakeHeaders()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string field, params string[] aliases) { result[Header(field)] = field; foreach (string alias in aliases) result[Header(alias)] = field; }
        Add("caseId", "用例ID", "ケースID", "用例编号");
        Add("title", "用例名", "ケース名", "用例名称", "用例标题");
        Add("stepNo", "step", "no", "ステップ番号", "ステップNo", "步骤编号", "步骤");
        Add("action", "操作", "操作内容");
        Add("condition", "テスト条件", "条件", "测试条件");
        Add("expected", "期待結果", "期待値", "期待值", "预期结果");
        Add("actual", "実際結果", "实际结果");
        Add("verdict", "判定");
        Add("precondition", "前提条件");
        Add("tester", "担当者", "担当");
        Add("date", "実施日", "日付", "执行日期");
        Add("env", "環境", "环境");
        Add("caseNote", "用例備考", "用例备注", "備考", "备注");
        return result;
    }

    // One logical CSV/TSV record is one step. All values remain strings except step No.
    public static List<TestCase> Parse(string csv, Project defaults)
    {
        var project = Contract.Clone(defaults); Contract.Validate(project);
        var rows = Tables.Parse(csv);
        var columns = new Dictionary<string, int>();
        for (int i = 0; i < rows[0].Count; i++)
        {
            if (!Headers.TryGetValue(Header(rows[0][i]), out string? field))
                throw new InvalidDataException($"未対応の列名「{rows[0][i]}」です。用例 CSV テンプレートの列名を使用してください。");
            if (!columns.TryAdd(field, i)) throw new InvalidDataException($"列「{rows[0][i]}」が重複しています。");
        }
        foreach (string field in new[] { "caseId", "title", "action", "expected" })
            if (!columns.ContainsKey(field)) throw new InvalidDataException("用例ID・用例名・操作・期待結果 の列が必要です。");
        var cases = new List<TestCase>();
        var byId = new Dictionary<string, TestCase>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<(string Id, string Field), string>();
        var stepNumbers = new HashSet<(string Id, int No)>();
        var maxStep = new Dictionary<string, int>();
        for (int index = 1; index < rows.Count; index++)
        {
            var row = rows[index]; if (row.All(string.IsNullOrWhiteSpace)) continue;
            string Value(string field) => columns.TryGetValue(field, out int column) ? row[column] : "";
            try
            {
                string id = Value("caseId").Trim(); Contract.Id(id);
                if (!byId.TryGetValue(id, out var c))
                {
                    c = new TestCase { Id = id, Tester = project.Tester, Env = project.Env, Date = DateTime.Now.ToString("yyyy-MM-dd"), NextEvidenceNumber = 1 };
                    byId.Add(id, c); cases.Add(c);
                }
                else if (c.Id != id) throw new InvalidDataException("同じ用例 ID の大文字・小文字を統一してください。");
                void Field(string field, Action<string> assign)
                {
                    string value = Value(field); if (string.IsNullOrWhiteSpace(value)) return;
                    if (field == "title") value = value.Trim();
                    var key = (id, field);
                    if (metadata.TryGetValue(key, out string? old) && old != value)
                        throw new InvalidDataException($"{id} の {field} が別のレコードと一致しません。用例の共通情報は同じ値か空欄にしてください。");
                    metadata[key] = value; assign(value);
                }
                Field("title", v => c.Title = v); Field("tester", v => c.Tester = v); Field("env", v => c.Env = v);
                Field("date", v => c.Date = v); Field("precondition", v => c.Precondition = v); Field("caseNote", v => c.Note = v);
                string number = Value("stepNo").Trim(); int step;
                if (number == "") step = checked(maxStep.GetValueOrDefault(id) + 1);
                else if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                    throw new InvalidDataException("ステップ番号は 1 以上の整数にしてください。空欄なら自動採番します。");
                if (!stepNumbers.Add((id, step))) throw new InvalidDataException($"{id} のステップ番号 {step} が重複しています。");
                maxStep[id] = Math.Max(maxStep.GetValueOrDefault(id), step);
                string action = Value("action"); if (string.IsNullOrWhiteSpace(action)) throw new InvalidDataException("操作を入力してください。");
                string verdict = Value("verdict").Trim();
                if (verdict != "" && !project.Verdicts.Contains(verdict)) throw new InvalidDataException($"未定義の判定「{verdict}」です。空欄またはプロジェクトの判定を使用してください。");
                c.Steps.Add(new Step { No = step, Action = action, Condition = Value("condition") == "" ? null : Value("condition"), Expected = Value("expected"), Actual = Value("actual"), Verdict = verdict });
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException)
            { throw new InvalidDataException($"CSV レコード {index + 1}：{ex.Message}", ex); }
        }
        if (cases.Count == 0) throw new InvalidDataException("用例のデータ行がありません。");
        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Title)) throw new InvalidDataException($"{c.Id} の用例名を入力してください。");
            Contract.Validate(c);
        }
        return cases;
    }

    public static string Template() => Tables.ToCsv([
        ["用例ID", "用例名", "ステップ番号", "操作", "テスト条件", "期待結果"],
        ["TC-CSV001", "ログイン", "1", "ログイン画面を開く", "利用者：未ログイン", "ユーザー ID とパスワードの入力欄が表示される"],
        ["TC-CSV001", "", "2", "正しい ID とパスワードでログイン", "ユーザー ID：test_user\nパスワード：テスト用の値", "ホーム画面へ遷移する"],
        ["TC-CSV002", "顧客検索", "1", "顧客 ID で検索する", "顧客 ID：00012", "対象の顧客情報が表示される"]
    ]);
}

public sealed record CaseImportResult(IReadOnlyList<string> CreatedIds, string? FailedId = null, string? Error = null);

public sealed partial class Workspace
{
    private List<(string Id, string Path, byte[] Bytes)> PrepareCaseImport(IReadOnlyList<TestCase> cases)
    {
        if (cases.Count == 0) throw new InvalidDataException("インポートする用例がありません。");
        var used = UnavailableCaseIds(); var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Id, string Path, byte[] Bytes)>();
        foreach (var source in cases)
        {
            var c = Contract.Clone(source); Contract.Validate(c);
            if (string.IsNullOrWhiteSpace(c.Title) || c.Steps.Count == 0 || c.Evidence.Count != 0)
                throw new InvalidDataException($"{c.Id}：用例名とステップが必要です。CSV で証拠ファイルは取り込みません。");
            if (!ids.Add(c.Id)) throw new InvalidDataException($"用例 ID {c.Id} が重複しています。");
            if (used.Contains(c.Id)) throw new InvalidDataException($"{c.Id} は使用済みです。既存・削除済みの用例を上書きしません。CSV の ID を変更してください。");
            string path = Files.Safe(Root, "cases", c.Id + ".yaml");
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(Path.ChangeExtension(path, ".yml")) || Directory.Exists(Path.ChangeExtension(path, ".yml")))
                throw new IOException($"{c.Id} の保存先が既に存在します。");
            result.Add((c.Id, path, Yaml.Write(c)));
        }
        return result;
    }

    public void ValidateCaseImport(IReadOnlyList<TestCase> cases)
    {
        lock (gate) _ = PrepareCaseImport(cases);
    }

    public CaseImportResult ImportCases(IReadOnlyList<TestCase> cases)
    {
        lock (gate)
        {
            // Validate and serialize the entire batch before creating anything. Each
            // case is then committed with a create-only rename; existing YAML is never replaced.
            var prepared = PrepareCaseImport(cases); var created = new List<string>();
            foreach (var item in prepared)
            {
                try { Files.Atomic(item.Path, item.Bytes, true); created.Add(item.Id); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { return new(created.ToArray(), item.Id, ex.Message); }
            }
            return new(created.ToArray());
        }
    }
}
