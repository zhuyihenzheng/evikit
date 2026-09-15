using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class EvidenceTextChecks
{
    private static void Assert(bool value) { if (!value) throw new Exception("Evidence text assertion failed"); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid table rejection"); }
    internal static void Run(string root, Action<string, Action> check)
    {
        check("pasted DB TSV and CSV automatically become tables with identical string cells", () =>
        {
            foreach (string separator in new[] { "\t", "," })
            {
                var result = EvidenceText.Analyze($"\uFEFFid{separator}name{separator}amount\r\n00012{separator}日本語{separator}001.00\r\n00013{separator}{separator}NULL\r\n");
                Assert(result.Kind == "table" && result.Rows!.Count == 3 && result.Rows[1].SequenceEqual(new[] { "00012", "日本語", "001.00" }) && result.Rows[2][1] == "");
            }
        });
        check("quoted commas tabs quotes and multiline DB cells survive normalization", () =>
        {
            List<List<string>> expected = [["id", "備考"], ["00012", "A, B\tC\n\"確認済み\""], ["00013", ""]];
            string csv = Tables.ToCsv(expected); var result = EvidenceText.Analyze(csv);
            Assert(result.Kind == "table" && Tables.ToCsv(result.Rows!) == csv);
        });
        check("plain logs SQL JSON and one-column text stay text by default", () =>
        {
            foreach (string value in new[] { "", "   ", "first\nsecond", "one, sentence", "id\n00012\n", "SELECT id, name FROM customers;", "{\n\t\"id\": \"00012\",\n\t\"name\": \"顧客\"\n}", "[\n[1,2],\n[3,4]\n]" })
                Assert(EvidenceText.Analyze(value).Kind == "text");
        });
        check("explicit text overrides inference and explicit table supports one column and header-only", () =>
        {
            Assert(EvidenceText.Analyze("id,name\n00012,A", "text").Rows == null);
            Assert(EvidenceText.Analyze("id\n00012\n", "table").Rows![1][0] == "00012");
            Assert(EvidenceText.Analyze("id,name\n", "table").Rows!.Count == 1);
        });
        check("malformed DB data reports its error and explicit table blocks saving", () =>
        {
            foreach (string bad in new[] { "id,name\n00012,A,extra", "id,name\n00012,\"unclosed", "id\tname\n00012" })
            {
                var result = EvidenceText.Analyze(bad);
                Assert(result.Kind == "text" && result.Rows == null && result.Message.Contains("表として認識できません"));
                Reject(() => EvidenceText.Analyze(bad, "table"));
            }
        });
        check("table inference enforces byte row and column limits without silently truncating", () =>
        {
            Reject(() => EvidenceText.Analyze(new string('あ', 700000)));
            Reject(() => EvidenceText.Analyze(new string('a', 2 * 1024 * 1024 + 1), "text"));
            Reject(() => EvidenceText.Analyze("id,name\n" + string.Concat(Enumerable.Repeat("00012,A\n", 10001)), "table"));
            Reject(() => EvidenceText.Analyze(string.Join(',', Enumerable.Repeat("x", 257)), "table"));
        });
        check("pasted tables retain all 1000 rows after save reload and Excel export", () =>
        {
            string path = Path.Combine(root, "pasted-db"); Workspace.Create(path, "DB 貼り付け"); using var workspace = new Workspace(path);
            var doc = workspace.CreateCase("TC-DB", "DB 確認");
            List<List<string>> rows = [["id", "value"]];
            for (int i = 0; i < 1000; i++) rows.Add([i.ToString("D5"), i == 12 ? "=1+1" : $"日本語 {i}"]);
            string input = Tables.ToCsv(rows); var parsed = EvidenceText.Analyze(input);
            doc = workspace.AddEvidence(doc, new(parsed.Kind, "DB", "検索結果", null, "SELECT id, value FROM example", "", "", Encoding.UTF8.GetBytes(input), "csv"));
            doc = workspace.LoadCase(doc.Data.Id);
            var evidence = doc.Data.Evidence.Single();
            Assert(evidence.Kind == "table" && File.ReadAllText(workspace.EvidencePath(doc.Data.Id, evidence)) == input);
            string output = Export.Deliver(workspace, Path.Combine(root, "pasted-db-output"), caseId: doc.Data.Id);
            using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
            Assert(!new OpenXmlValidator().Validate(document).Any());
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml = XDocument.Load(stream);
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var texts = xml.Descendants(s + "t").Select(t => t.Value).ToHashSet();
            Assert(texts.Contains("00012") && texts.Contains("00999") && texts.Contains("=1+1") && !xml.Descendants(s + "f").Any());
            Assert(xml.Descendants(s + "c").All(c => (string?)c.Attribute("t") == "inlineStr"));
            Console.WriteLine("PASTED_DB_OUTPUT=" + output);
        });
    }
}
