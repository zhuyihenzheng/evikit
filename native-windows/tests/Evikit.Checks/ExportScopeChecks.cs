using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class ExportScopeChecks
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static void Assert(bool value) { if (!value) throw new Exception("Export scope assertion failed"); }
    private static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected export scope rejection"); }
    private static XDocument Xml(ZipArchive zip, string file) { using var stream = zip.GetEntry(file)!.Open(); return XDocument.Load(stream); }
    private static string Text(ZipArchive zip) => string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")).SelectMany(e => Xml(zip, e.FullName).Descendants(S + "t")).Select(t => t.Value));
    private static void Validate(string output, params string[] ids)
    {
        using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(e => e.Description)));
        using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
        Assert(Xml(zip, "xl/workbook.xml").Descendants(S + "sheet").Select(e => (string)e.Attribute("name")!).SequenceEqual(new[] { "サマリ" }.Concat(ids)));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("scope").GetProperty("caseIds").EnumerateArray().Select(e => e.GetString()).SequenceEqual(ids));
        using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip"));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string relative = item.GetProperty("path").GetString()!;
            Assert(delivery.GetEntry(relative) != null && Files.HashFile(Path.Combine(output, relative)) == item.GetProperty("sha256").GetString());
            if (relative.StartsWith("files/")) Assert(ids.Contains(relative.Split('/')[1]));
        }
        Assert(delivery.Entries.Where(e => e.FullName.StartsWith("files/")).All(e => ids.Contains(e.FullName.Split('/')[1])));
    }

    internal static void Run(string root, Action<string, Action> check)
    {
        string path = Path.Combine(root, "export-scope-project"), outputRoot = Path.Combine(root, "export-scope-output");
        Workspace.Create(path, "出力範囲"); using var w = new Workspace(path);
        var a = w.CreateCase("TC-A", "今回の対象"); var b = w.CreateCase("TC-B", "OTHER-CASE-TITLE");
        a.Data.Steps = [new() { No = 1, Action = "選択した操作", Expected = "00012", Actual = "確認済み", Verdict = "OK", Condition = "A-CONDITION" }]; a = w.SaveCase(a);
        b.Data.Steps = [new() { No = 1, Action = "OTHER-ACTION", Verdict = "NG" }]; b = w.SaveCase(b);
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        a = w.AddEvidence(a, new("image", "画面", "A-GALLERY", 1, "", "A-NOTE", "", png, "png"));
        a = w.AppendImage(a, "E01", new("image", "画面", "2 枚目", 1, "", "", "", png, "png"));
        a = w.AddEvidence(a, new("table", "DB", "A-CSV", 1, "SELECT 'A-SOURCE'", "", "", Encoding.UTF8.GetBytes("id\n00012\n"), "csv"));
        a = w.AddEvidence(a, new("file", "画面", "A-VIDEO", 1, "", "", "", new byte[1024], "mp4"));
        b = w.AddEvidence(b, new("table", "DB", "OTHER-CSV", 1, "", "", "", Encoding.UTF8.GetBytes("id\nOTHER-ROW\n"), "csv"));
        var snapshot = w.Snapshot(); string original = JsonSerializer.Serialize(snapshot, Contract.Json);
        var dataFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != ".evikit.lock").ToDictionary(f => f, Files.HashFile);

        check("one-case export includes only its summary row, sheet, grouped images, video and attachments", () =>
        {
            string output = Export.Deliver(w, outputRoot, caseId: a.Data.Id); Validate(output, a.Data.Id);
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
            Assert(!Text(zip).Contains("OTHER-") && !Text(zip).Contains(b.Data.Id));
            Assert(zip.Entries.Count(e => e.FullName.StartsWith("xl/media/")) == 2);
            var summary = Xml(zip, "xl/worksheets/sheet1.xml"); Assert(summary.Descendants(S + "row").Count() == 4);
            Assert(summary.Descendants(S + "c").Single(e => (string?)e.Attribute("r") == "E4").Value == "OK");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert(manifest.RootElement.GetProperty("scope").GetProperty("mode").GetString() == "case");
            Assert(Directory.EnumerateFiles(Path.Combine(output, "files"), "*", SearchOption.AllDirectories).Count() == 4);
            Assert(!Directory.Exists(Path.Combine(output, "files", b.Data.Id)));
            Console.WriteLine("SINGLE_CASE_OUTPUT=" + output);
        });
        check("single-case scope composes with image video and metadata exclusions", () =>
        {
            string output = Export.Deliver(w, outputRoot, new() { Images = false, Videos = false, Dates = false, Tester = false, Conditions = false, Notes = false, Sources = false }, a.Data.Id); Validate(output, a.Data.Id);
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); string text = Text(zip);
            Assert(text.Contains("00012") && !text.Contains("OTHER-") && !text.Contains("A-GALLERY") && !text.Contains("A-VIDEO") && !text.Contains("A-CONDITION") && !text.Contains("取得："));
            Assert(!zip.Entries.Any(e => e.FullName.StartsWith("xl/media/")));
            Assert(Directory.EnumerateFiles(Path.Combine(output, "files"), "*", SearchOption.AllDirectories).Single().EndsWith("E02.csv"));
            Console.WriteLine("SINGLE_CASE_MINIMAL_OUTPUT=" + output);
        });
        check("default API export still includes all cases and scope is explicit in manifest", () =>
        {
            string output = Export.Deliver(w, outputRoot); Validate(output, a.Data.Id, b.Data.Id);
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); Assert(Text(zip).Contains("OTHER-ROW"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert(manifest.RootElement.GetProperty("scope").GetProperty("mode").GetString() == "all");
        });
        check("memory snapshot delivery and direct XLSX writer share single-case scope without changing input", () =>
        {
            string output = Export.Deliver(snapshot, outputRoot, caseId: b.Data.Id); Validate(output, b.Data.Id);
            string file = Path.Combine(root, "direct-single-case.xlsx"); Xlsx.Write(snapshot, file, caseId: b.Data.Id);
            using var first = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); using var second = ZipFile.OpenRead(file);
            Assert(Text(first) == Text(second) && !Text(first).Contains("A-GALLERY"));
            Assert(JsonSerializer.Serialize(snapshot, Contract.Json) == original);
        });
        check("missing or deleted single-case target never falls back to a full-project export", () =>
        {
            int outputs = Directory.EnumerateDirectories(outputRoot).Count();
            Reject(() => Export.Deliver(w, outputRoot, caseId: "TC-MISSING"));
            Reject(() => Export.Deliver(snapshot, outputRoot, caseId: "TC-MISSING"));
            string absent = Path.Combine(root, "missing-case.xlsx"); Reject(() => Xlsx.Write(snapshot, absent, caseId: "TC-MISSING")); Assert(!File.Exists(absent));
            string archive = w.DeleteCase(a); Reject(() => Export.Deliver(w, outputRoot, caseId: a.Data.Id)); a = w.RestoreCase(archive);
            Assert(Directory.EnumerateDirectories(outputRoot).Count() == outputs);
        });
        check("unsafe and ambiguous selected IDs reject while a single yml file is supported", () =>
        {
            foreach (string id in new[] { "", "../TC-A", "CON", "tc-a" })
            { Reject(() => Export.Deliver(w, outputRoot, caseId: id)); Reject(() => Export.Deliver(snapshot, outputRoot, caseId: id)); }
            string yaml = Path.Combine(path, "cases", "TC-A.yaml"), yml = Path.ChangeExtension(yaml, ".yml");
            File.Copy(yaml, yml); Reject(() => Export.Deliver(w, outputRoot, caseId: a.Data.Id)); File.Delete(yml);
            Reject(() => Export.Deliver(snapshot with { Cases = [snapshot.Cases[0], snapshot.Cases[0]] }, outputRoot, caseId: a.Data.Id));
            File.Move(yaml, yml); string output = Export.Deliver(w, outputRoot, caseId: a.Data.Id); Validate(output, a.Data.Id); File.Move(yml, yaml);
        });
        check("unselected case YAML and evidence are not read or copied by single-case delivery", () =>
        {
            string otherYaml = Path.Combine(path, "cases", "TC-B.yaml"), otherCsv = w.EvidencePath(b.Data.Id, b.Data.Evidence[0]);
            byte[] yaml = File.ReadAllBytes(otherYaml), csv = File.ReadAllBytes(otherCsv);
            File.WriteAllText(otherYaml, "invalid: ["); File.Delete(otherCsv);
            string output = Export.Deliver(w, outputRoot, caseId: a.Data.Id); Validate(output, a.Data.Id);
            Reject(() => Export.Deliver(w, outputRoot));
            File.WriteAllBytes(otherYaml, yaml); File.WriteAllBytes(otherCsv, csv);
        });
        check("selected corruption blocks publishing and changing scope preserves source data and saved options", () =>
        {
            string image = w.EvidencePath(a.Data.Id, a.Data.Evidence[0].Images![1]); byte[] bytes = File.ReadAllBytes(image); File.AppendAllText(image, "tampered");
            Reject(() => Export.Deliver(w, outputRoot, caseId: a.Data.Id)); File.WriteAllBytes(image, bytes);
            Assert(!Directory.EnumerateFileSystemEntries(outputRoot).Any(p => Path.GetFileName(p).StartsWith('.')));
            var options = new ExportOptions { Dates = false }; w.SaveExportOptions(options);
            Validate(Export.Deliver(w, outputRoot, options, a.Data.Id), a.Data.Id);
            Validate(Export.Deliver(w, outputRoot, options, b.Data.Id), b.Data.Id);
            Assert(w.LoadExportOptions() == options && !File.ReadAllText(Path.Combine(path, ".evikit", "export-options.json")).Contains("caseId"));
            foreach (var file in dataFiles) Assert(Files.HashFile(file.Key) == file.Value);
        });
    }
}
