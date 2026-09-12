using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class ConditionChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Assert(bool value) { if (!value) throw new Exception("Step condition assertion failed"); }
        string path = Path.Combine(root, "condition-project"); Workspace.Create(path, "テスト条件");
        using var w = new Workspace(path); var doc = w.CreateCase("TC-COND", "ステップごとの条件");
        check("step conditions are optional, multiline, and preserved with the step when reordered", () =>
        {
            var old = Yaml.Read<TestCase>(Encoding.UTF8.GetBytes("id: TC-OLD\nsteps:\n- no: 1\n  action: old\nevidence: []\n"));
            Assert(old.Steps[0].Condition == null);
            Assert(!Encoding.UTF8.GetString(Yaml.Write(old.Steps[0])).Contains("condition:"));
            doc.Data.Steps = [new() { No = 1, Action = "登録", Condition = "権限：管理者\nID：00012", Verdict = "OK" }, new() { No = 2, Action = "表示" }];
            doc = w.SaveCase(doc);
            (doc.Data.Steps[0], doc.Data.Steps[1]) = (doc.Data.Steps[1], doc.Data.Steps[0]);
            for (int i = 0; i < doc.Data.Steps.Count; i++) doc.Data.Steps[i].No = i + 1;
            doc = w.SaveCase(doc); var loaded = w.LoadCase(doc.Data.Id);
            Assert(loaded.Data.Steps[1].Action == "登録" && loaded.Data.Steps[1].Condition == "権限：管理者\nID：00012");
            Assert(loaded.Data.Steps[0].Condition == null);
        });
        check("Excel exports conditions directly under the matching step and omits absent conditions", () =>
        {
            var output = Export.Deliver(w, Path.Combine(root, "condition-output"));
            using var workbook = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
            if (new OpenXmlValidator().Validate(workbook).Any()) throw new Exception("Invalid condition workbook");
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = xml.Descendants(ns + "row").ToArray();
            int index = Array.FindIndex(rows, row => row.Descendants(ns + "t").Any(t => t.Value == "ステップ 2 / テスト条件：\n権限：管理者\nID：00012"));
            Assert(index > 0 && rows[index - 1].Descendants(ns + "t").Any(t => t.Value == "登録"));
            Assert(!xml.ToString().Contains("ステップ 1 / テスト条件"));
            Console.WriteLine("CONDITION_OUTPUT=" + output);
        });
    }
}
