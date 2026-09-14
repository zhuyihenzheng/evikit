using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class CaseCsvChecks
{
    private static void Assert(bool value) { if (!value) throw new Exception("Case CSV assertion failed"); }
    private static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected case CSV rejection"); }
    internal static void Run(string root, Action<string, Action> check)
    {
        var defaults = new Project { Name = "CSV 用例", Tester = "既定担当者", Env = "検証環境", Verdicts = ["OK", "NG", "未実施", "CUSTOM"] };
        string path = Path.Combine(root, "case-csv-project"); Workspace.Create(path, defaults.Name); using var w = new Workspace(path);
        string Minimal(string id, string title = "用例") => $"用例ID,用例名,操作,期待結果\n{id},{title},確認する,表示される\n";
        List<TestCase> Parse(string text) => CaseCsv.Parse(text, defaults);
        string SnapshotFiles() => JsonSerializer.Serialize(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != ".evikit.lock").Order().ToDictionary(f => f, Files.HashFile));

        check("shipped UTF-8 BOM case template matches the UI template and creates two cases with three steps", () =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "case-template.csv"));
            Assert(bytes.Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
            var cases = Parse(Inputs.Utf8(bytes)); var ui = Parse(CaseCsv.Template());
            Assert(JsonSerializer.Serialize(cases, Contract.Json) == JsonSerializer.Serialize(ui, Contract.Json));
            Assert(cases.Count == 2 && cases[0].Steps.Count == 2 && cases[1].Steps.Count == 1);
            Assert(cases[0].Steps[1].Condition!.Contains('\n') && cases[1].Steps[0].Condition!.Contains("00012"));
            Assert(cases.All(c => c.Tester == defaults.Tester && c.Env == defaults.Env && c.Evidence.Count == 0));
        });
        check("English and Chinese column aliases preserve quoted commas multiline text and string values", () =>
        {
            var cases = Parse("case_id,用例名称,操作,条件,期待值\nTC-ALIAS,用例,\"操作,その1\",\"ID：00012\n状態：未登録\",\"\"\"成功\"\" と表示\"\n");
            var step = cases.Single().Steps.Single();
            Assert(step.Action == "操作,その1" && step.Condition == "ID：00012\n状態：未登録" && step.Expected == "\"成功\" と表示");
        });
        check("nonadjacent repeated IDs group in first appearance order and blank numbers auto-increment", () =>
        {
            var cases = Parse("caseId\ttitle\tstepNo\taction\texpected\nTC-A\t用例A\t2\t操作A1\t期待A1\nTC-B\t用例B\t\t操作B1\t期待B1\nTC-A\t\t\t操作A2\t期待A2\n");
            Assert(cases.Select(c => c.Id).SequenceEqual(new[] { "TC-A", "TC-B" }));
            Assert(cases[0].Title == "用例A" && cases[0].Steps.Select(s => s.No).SequenceEqual(new[] { 2, 3 }) && cases[1].Steps.Single().No == 1);
            Assert(cases[0].Steps.All(s => s.Condition == null && s.Verdict == ""));
        });
        check("optional common metadata and actual result columns override defaults and preserve custom verdicts", () =>
        {
            var cases = Parse("caseId,title,action,expected,actual,verdict,precondition,tester,date,env,caseNote\nTC-META,用例,操作,=1+1,確認済み,CUSTOM,前提,担当A,2026-01-02,環境A,用例備考\nTC-META,,次の操作,期待,,,,,,,\n");
            var c = cases.Single(); Assert(c.Tester == "担当A" && c.Env == "環境A" && c.Date == "2026-01-02" && c.Note == "用例備考" && c.Precondition == "前提");
            Assert(c.Steps[0].Expected == "=1+1" && c.Steps[0].Actual == "確認済み" && c.Steps[0].Verdict == "CUSTOM");
        });
        check("bad headers IDs conflicting metadata duplicate steps and invalid verdicts cause no writes", () =>
        {
            string before = SnapshotFiles();
            foreach (string invalid in new[]
            {
                "caseId,title,action\nTC-X,用例,操作\n", "caseId,title,action,expected,unknown\nTC-X,用例,操作,期待,値\n",
                "caseId,用例ID,title,action,expected\nTC-X,TC-X,用例,操作,期待\n", Minimal("../outside"), Minimal("CON"), Minimal(""), Minimal("TC-X", ""),
                "caseId,title,action,expected\nTC-X,用例,,期待\n", "caseId,title,action,expected\nTC-X,名前1,操作,期待\nTC-X,名前2,操作,期待\n",
                "caseId,title,action,expected\nTC-X,用例,操作,期待\ntc-x,用例,操作,期待\n",
                "caseId,title,stepNo,action,expected\nTC-X,用例,1,操作,期待\nTC-X,,1,操作,期待\n",
                "caseId,title,stepNo,action,expected\nTC-X,用例,0,操作,期待\n", "caseId,title,stepNo,action,expected\nTC-X,用例,1.5,操作,期待\n",
                "caseId,title,action,expected,verdict\nTC-X,用例,操作,期待,INVALID\n", "caseId,title,action,expected\n", "caseId,title,action,expected\nTC-X,用例,\"操作,期待\n"
            }) Reject(() => w.ImportCases(Parse(invalid)));
            Assert(SnapshotFiles() == before);
        });
        check("case CSV limits reject oversized or excessive-row input without touching the project", () =>
        {
            string before = SnapshotFiles();
            Reject(() => Parse("caseId,title,action,expected\nTC-LONG,用例,操作," + new string('a', 2 * 1024 * 1024)));
            Reject(() => Parse("caseId,title,action,expected\n" + string.Concat(Enumerable.Range(1, 10001).Select(n => $"TC-LONG,用例,操作{n},期待\n"))));
            Reject(() => w.ImportCases([])); Assert(SnapshotFiles() == before);
        });
        var plan = Parse(CaseCsv.Template());
        check("preview does not write and confirmed batch creates editable cases with conditions and empty evidence", () =>
        {
            string before = SnapshotFiles(); w.ValidateCaseImport(plan); Assert(SnapshotFiles() == before);
            var result = w.ImportCases(plan); Assert(result.Error == null && result.CreatedIds.SequenceEqual(plan.Select(c => c.Id)));
            foreach (var c in plan)
            {
                var loaded = w.LoadCase(c.Id); Assert(JsonSerializer.Serialize(loaded.Data, Contract.Json) == JsonSerializer.Serialize(c, Contract.Json));
                Assert(loaded.Data.Evidence.Count == 0 && loaded.Data.NextEvidenceNumber == 1);
            }
            var edited = w.LoadCase(plan[0].Id); edited.Data.Steps[0].Actual = "画面表示を確認"; edited.Data.Steps[0].Verdict = "OK"; w.SaveCase(edited);
            Assert(w.LoadCase(plan[0].Id).Data.Steps[0].Actual == "画面表示を確認");
        });
        check("repeat import and a conflict in the final case reject the whole batch without overwriting edits", () =>
        {
            string before = SnapshotFiles(); Reject(() => w.ImportCases(plan));
            var incoming = Parse(Minimal("TC-FIRST")); incoming.Add(Contract.Clone(plan[0])); Reject(() => w.ImportCases(incoming));
            Assert(SnapshotFiles() == before && !File.Exists(Path.Combine(path, "cases", "TC-FIRST.yaml")));
            incoming[^1].Id = "TC-LAST"; incoming[^1].Title = ""; Reject(() => w.ImportCases(incoming)); Assert(SnapshotFiles() == before);
        });
        check("deleted case IDs and retained evidence directories cannot be reused by CSV import", () =>
        {
            var deleted = w.CreateCase("TC-DELETED", "削除済み"); w.DeleteCase(deleted);
            Directory.CreateDirectory(Path.Combine(path, "evidence", "TC-RESERVED")); string before = SnapshotFiles();
            foreach (string id in new[] { "TC-DELETED", "tc-deleted", "TC-RESERVED", "tc-reserved" }) Reject(() => w.ImportCases(Parse(Minimal(id))));
            Assert(SnapshotFiles() == before);
        });
        check("commit rechecks IDs after preview and rejects occupied target directories before any creation", () =>
        {
            var incoming = Parse("caseId,title,action,expected\nTC-PREVIEW-A,A,操作,期待\nTC-PREVIEW-B,B,操作,期待\n");
            w.ValidateCaseImport(incoming); var existing = w.CreateCase("TC-PREVIEW-B", "後から作成"); string before = SnapshotFiles();
            Reject(() => w.ImportCases(incoming)); Assert(SnapshotFiles() == before && w.LoadCase(existing.Data.Id).Data.Title == "後から作成");
            incoming[1].Id = "TC-OCCUPIED"; Directory.CreateDirectory(Path.Combine(path, "cases", "TC-OCCUPIED.yaml"));
            Reject(() => w.ImportCases(incoming)); Assert(!File.Exists(Path.Combine(path, "cases", "TC-PREVIEW-A.yaml")));
        });
        check("bulk import handles 200 cases and 1000 steps without altering source plan or existing data", () =>
        {
            string bulkRoot = Path.Combine(root, "case-csv-bulk"); Workspace.Create(bulkRoot, "大量 CSV"); using var bulk = new Workspace(bulkRoot);
            var existing = bulk.CreateCase("TC-EXISTING", "既存用例");
            var rows = new List<List<string>> { new() { "caseId", "title", "action", "condition", "expected" } };
            for (int c = 1; c <= 200; c++) for (int step = 1; step <= 5; step++) rows.Add([$"TC-BULK-{c:000}", "用例" + c, "操作" + step, "ID＝00012", "期待" + step]);
            string csv = Tables.ToCsv(rows); var cases = Parse(csv); string original = JsonSerializer.Serialize(cases, Contract.Json);
            var result = bulk.ImportCases(cases); Assert(result.Error == null && result.CreatedIds.Count == 200);
            Assert(bulk.ListCases().Count == 201 && bulk.ListCases().Sum(c => c.Data.Steps.Count) == 1000);
            Assert(bulk.LoadCase(existing.Data.Id).Revision == existing.Revision && JsonSerializer.Serialize(cases, Contract.Json) == original);
        });
        check("CSV-created cases export singly and together with conditions string expectations and valid Excel", () =>
        {
            string output = Export.Deliver(w, Path.Combine(root, "case-csv-output"), caseId: plan[1].Id);
            using var workbook = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false); Assert(!new OpenXmlValidator().Validate(workbook).Any());
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheet = XDocument.Load(stream); Assert(sheet.Descendants(ns + "t").Any(t => t.Value.Contains("顧客 ID：00012")));
            Assert(sheet.Descendants(ns + "c").All(c => (string?)c.Attribute("t") == "inlineStr"));
            Console.WriteLine("CSV_CASE_OUTPUT=" + output);
            var createdOnly = new ProjectSnapshot(defaults, plan.Select(c => new CaseSnapshot(w.LoadCase(c.Id).Data, [])).ToList());
            output = Export.Deliver(createdOnly, Path.Combine(root, "case-csv-output"));
            using var all = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false); Assert(!new OpenXmlValidator().Validate(all).Any());
            Assert(all.WorkbookPart!.Workbook.Sheets!.Count() == 3);
        });
    }
}
