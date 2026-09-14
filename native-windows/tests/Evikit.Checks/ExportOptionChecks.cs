using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class ExportOptionChecks
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static void Assert(bool value) { if (!value) throw new Exception("Export option assertion failed"); }
    private static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected export option rejection"); }
    private static XDocument Xml(ZipArchive zip, string path) { using var stream = zip.GetEntry(path)!.Open(); return XDocument.Load(stream); }
    private static string Text(ZipArchive zip) => string.Join("\n", zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")).SelectMany(e => Xml(zip, e.FullName).Descendants(S + "t")).Select(e => e.Value));
    private static void Validate(string file)
    {
        using var document = SpreadsheetDocument.Open(file, false);
        var errors = new OpenXmlValidator().Validate(document).ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(e => e.Description)));
    }
    private static string Workbook(string output) => Path.Combine(output, "report.xlsx");

    internal static void Run(string root, Action<string, Action> check)
    {
        string path = Path.Combine(root, "export-options-project"); Workspace.Create(path, "選択出力");
        var w = new Workspace(path);
        try
        {
            var project = w.LoadProject(); project.Data.Tester = "PROJECT-TESTER"; project.Data.Env = "PROJECT-ENV"; w.SaveProject(project);
            var doc = w.CreateCase("TC-OPTIONS", "選択出力の用例");
            doc.Data.Tester = "CASE-TESTER"; doc.Data.Env = "CASE-ENV"; doc.Data.Date = "2026-01-02";
            doc.Data.Precondition = "CASE-CONDITION"; doc.Data.Note = "CASE-NOTE";
            doc.Data.Steps = [new() { No = 1, Action = "登録", Expected = "成功", Actual = "表示済み", Verdict = "OK", Condition = "STEP-CONDITION" }];
            doc = w.SaveCase(doc);
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            doc = w.AddEvidence(doc, new("image", "画面", "GROUP-CAPTION", 1, "GROUP-SOURCE", "GROUP-NOTE", "", png, "png"));
            doc = w.AppendImage(doc, "E01", new("image", "画面", "SECOND-CAPTION", 1, "SECOND-SOURCE", "SECOND-NOTE", "", png, "png"));
            doc = w.AddEvidence(doc, new("image", "画面", "SINGLE-CAPTION", 1, "SINGLE-SOURCE", "SINGLE-NOTE", "", png, "png"));
            doc = w.AddEvidence(doc, new("table", "DB", "CSV-CAPTION", 1, "SELECT 'DB-SOURCE'", "DB-NOTE", "", Encoding.UTF8.GetBytes("id,name\n00012,顧客\n"), "csv"));
            doc = w.AddEvidence(doc, new("text", "ログ", "LOG-CAPTION", 1, "LOG-SOURCE", "LOG-NOTE", "log", Encoding.UTF8.GetBytes("2026-01-02 本文の日時は保持\n"), "log"));
            doc = w.AddEvidence(doc, new("file", "画面", "VIDEO-CAPTION", 1, "VIDEO-SOURCE", "VIDEO-NOTE", "", new byte[1024], "mp4"));
            doc = w.AddEvidence(doc, new("file", "その他", "FILE-CAPTION", 1, "FILE-SOURCE", "FILE-NOTE", "", Encoding.UTF8.GetBytes("attachment"), "bin"));
            foreach (var e in doc.Data.Evidence)
            {
                e.CapturedAt = "2026-02-03T04:05:06+09:00";
                if (e.Images != null) foreach (var image in e.Images) image.CapturedAt = "2026-03-04T05:06:07+09:00";
            }
            doc = w.SaveCase(doc);
            var snapshot = w.Snapshot();
            string originalSnapshot = JsonSerializer.Serialize(snapshot, Contract.Json);
            var originalFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToDictionary(f => f, Files.HashFile);
            string outputRoot = Path.Combine(root, "export-options-output");
            string Deliver(ExportOptions options)
            {
                string output = Export.Deliver(w, outputRoot, options); Validate(Workbook(output));
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
                Assert(manifest.RootElement.GetProperty("exportOptions").Deserialize<ExportOptions>(Contract.Json) == options);
                using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip"));
                foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
                {
                    string relative = item.GetProperty("path").GetString()!;
                    Assert(delivery.GetEntry(relative) != null && Files.HashFile(Path.Combine(output, relative)) == item.GetProperty("sha256").GetString());
                }
                Assert(!delivery.Entries.Any(e => e.FullName.Contains(".evikit") || e.FullName.Contains(".trash")));
                return output;
            }

            check("export defaults retain all evidence, dates, metadata and legacy layout", () =>
            {
                Assert(w.LoadExportOptions() == new ExportOptions());
                string output = Deliver(new()); using var zip = ZipFile.OpenRead(Workbook(output));
                Assert(zip.Entries.Count(e => e.FullName.StartsWith("xl/media/")) == 3);
                string text = Text(zip); Assert(text.Contains("2026-03-04T05:06:07+09:00") && text.Contains("VIDEO-CAPTION") && text.Contains("DB-SOURCE"));
                Assert(Xml(zip, "xl/worksheets/sheet1.xml").Descendants(S + "col").Select(c => (int)c.Attribute("width")!).SequenceEqual(new[] { 6, 36, 32, 32, 8, 14 }));
            });
            check("image exclusion removes single and grouped blocks, drawing bytes, links and attachments", () =>
            {
                string output = Deliver(new() { Images = false }); using var zip = ZipFile.OpenRead(Workbook(output));
                Assert(!zip.Entries.Any(e => e.FullName.StartsWith("xl/media/") || e.FullName.StartsWith("xl/drawings/")));
                string text = Text(zip); Assert(!text.Contains("GROUP-CAPTION") && !text.Contains("SINGLE-CAPTION") && text.Contains("CSV-CAPTION"));
                var sheet = Xml(zip, "xl/worksheets/sheet2.xml");
                var step = sheet.Descendants(S + "row").Single(r => r.Descendants(S + "t").Any(t => t.Value == "登録"));
                Assert(step.Elements(S + "c").Last().Value == "E03, E04, E05, E06");
                using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip")); Assert(!delivery.Entries.Any(e => e.FullName.EndsWith(".png")));
                Assert(!Directory.EnumerateFiles(output, "*.png", SearchOption.AllDirectories).Any());
            });
            check("date exclusion removes summary date and every capture timestamp while preserving log content", () =>
            {
                string output = Deliver(new() { Dates = false }); using var zip = ZipFile.OpenRead(Workbook(output)); string text = Text(zip);
                Assert(!text.Contains("取得：") && !text.Contains("日付") && !text.Contains("2026-02-03") && !text.Contains("2026-03-04"));
                Assert(text.Contains("2026-01-02 本文の日時は保持") && zip.Entries.Count(e => e.FullName.StartsWith("xl/media/")) == 3);
                Assert(Xml(zip, "xl/worksheets/sheet1.xml").Descendants(S + "col").Count() == 5);
            });
            check("tester and environment exclusion adjusts summary columns, styles and frozen row positions", () =>
            {
                string output = Deliver(new() { Tester = false, Environment = false, Dates = false, Conditions = false }); using var zip = ZipFile.OpenRead(Workbook(output));
                string text = Text(zip); Assert(!text.Contains("TESTER") && !text.Contains("ENV") && !text.Contains("CONDITION"));
                var summary = Xml(zip, "xl/worksheets/sheet1.xml"); Assert(summary.Descendants(S + "col").Count() == 4);
                Assert((string?)summary.Descendants(S + "pane").Single().Attribute("topLeftCell") == "A3");
                Assert((string?)summary.Descendants(S + "c").Single(c => (string?)c.Attribute("r") == "D3").Attribute("s") == "3");
                Assert(summary.Descendants(S + "mergeCell").Any(c => (string?)c.Attribute("ref") == "A1:D1"));
                Assert((string?)Xml(zip, "xl/worksheets/sheet2.xml").Descendants(S + "pane").Single().Attribute("topLeftCell") == "A5");
            });
            check("conditions notes and sources can be omitted independently of pictures and CSV data", () =>
            {
                string output = Deliver(new() { Conditions = false, Notes = false, Sources = false }); using var zip = ZipFile.OpenRead(Workbook(output));
                string text = Text(zip); Assert(!text.Contains("CONDITION") && !text.Contains("NOTE") && !text.Contains("SOURCE"));
                Assert(text.Contains("00012") && text.Contains("GROUP-CAPTION") && zip.Entries.Count(e => e.FullName.StartsWith("xl/media/")) == 3);
            });
            check("video and other-file switches filter independently and preserve relative links", () =>
            {
                foreach (bool video in new[] { false, true })
                {
                    string output = Deliver(new() { Videos = video, Files = !video }); using var zip = ZipFile.OpenRead(Workbook(output)); string text = Text(zip);
                    Assert(text.Contains("VIDEO-CAPTION") == video && text.Contains("FILE-CAPTION") != video);
                    using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip")); Assert((delivery.GetEntry("files/TC-OPTIONS/E05.mp4") != null) == video);
                    Assert((delivery.GetEntry("files/TC-OPTIONS/E06.bin") != null) != video);
                    var rels = Xml(zip, "xl/worksheets/_rels/sheet2.xml.rels");
                    foreach (var link in rels.Root!.Elements().Where(r => (string?)r.Attribute("TargetMode") == "External"))
                        Assert(File.Exists(Path.Combine(output, Uri.UnescapeDataString((string)link.Attribute("Target")!))));
                }
            });
            var minimal = new ExportOptions { Images = false, Videos = false, Files = false, Dates = false, Tester = false, Environment = false, Conditions = false, Notes = false, Sources = false };
            check("minimal output keeps cases steps verdicts tables and logs with only their attachments", () =>
            {
                string output = Deliver(minimal); using var zip = ZipFile.OpenRead(Workbook(output)); string text = Text(zip);
                foreach (string retained in new[] { "TC-OPTIONS", "登録", "成功", "表示済み", "OK", "00012", "本文の日時は保持" }) Assert(text.Contains(retained));
                using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip")); Assert(delivery.Entries.Count(e => e.FullName.StartsWith("files/")) == 2);
                Console.WriteLine("MINIMAL_OUTPUT=" + output);
            });
            check("all 64 metadata option combinations produce valid workbooks with the expected labels", () =>
            {
                for (int flags = 0; flags < 64; flags++)
                {
                    var options = new ExportOptions { Dates = (flags & 1) != 0, Tester = (flags & 2) != 0, Environment = (flags & 4) != 0, Conditions = (flags & 8) != 0, Notes = (flags & 16) != 0, Sources = (flags & 32) != 0 };
                    string file = Path.Combine(root, $"option-matrix-{flags}.xlsx"); Xlsx.Write(snapshot, file, options); Validate(file);
                    using var zip = ZipFile.OpenRead(file); string text = Text(zip);
                    Assert(text.Contains("取得：") == options.Dates && text.Contains("担当：") == options.Tester && text.Contains("環境：") == options.Environment);
                    Assert(text.Contains("前提条件：") == options.Conditions && text.Contains("備考：") == options.Notes && text.Contains("出典 / SQL：") == options.Sources);
                }
            });
            check("snapshot delivery and direct workbook APIs apply the same filters without mutating input", () =>
            {
                string output = Export.Deliver(snapshot, outputRoot, minimal); Validate(Workbook(output));
                string direct = Path.Combine(root, "direct-options.xlsx"); Xlsx.Write(snapshot, direct, minimal); Validate(direct);
                using var first = ZipFile.OpenRead(Workbook(output)); using var second = ZipFile.OpenRead(direct); Assert(Text(first) == Text(second));
                Assert(JsonSerializer.Serialize(snapshot, Contract.Json) == originalSnapshot);
                foreach (var file in originalFiles) Assert(Files.HashFile(file.Key) == file.Value);
            });
            check("project output preferences survive restart and do not affect other projects", () =>
            {
                w.SaveExportOptions(minimal); w.Dispose(); w = new Workspace(path); Assert(w.LoadExportOptions() == minimal);
                Assert(File.Exists(Path.Combine(path, ".evikit", "export-options.json")));
                string otherPath = Path.Combine(root, "other-export-options"); Workspace.Create(otherPath, "別プロジェクト"); using var other = new Workspace(otherPath); Assert(other.LoadExportOptions() == new ExportOptions());
                foreach (var file in originalFiles.Where(f => !f.Key.EndsWith(".evikit.lock"))) Assert(Files.HashFile(file.Key) == file.Value);
            });
            check("malformed or unknown output preferences stop instead of silently exporting hidden fields", () =>
            {
                string settings = Path.Combine(path, ".evikit", "export-options.json");
                foreach (string invalid in new[] { "null", "{", "{\"dates\":null}", "{\"images\":\"false\"}", "{\"futureOption\":false}" })
                { File.WriteAllText(settings, invalid); Reject(() => w.LoadExportOptions()); }
                File.WriteAllText(settings, "{\"images\":false}"); Assert(w.LoadExportOptions() == new ExportOptions { Images = false });
                w.SaveExportOptions(minimal);
            });
            check("excluded broken images are not read while selected corrupted evidence blocks publishing", () =>
            {
                string image = w.EvidencePath(doc.Data.Id, doc.Data.Evidence[0].Images![1]); byte[] original = File.ReadAllBytes(image); File.Delete(image);
                _ = Deliver(new() { Images = false }); Reject(() => Export.Deliver(w, outputRoot)); File.WriteAllBytes(image, original);
                string csv = w.EvidencePath(doc.Data.Id, doc.Data.Evidence[2]); original = File.ReadAllBytes(csv); File.AppendAllText(csv, "tampered");
                Reject(() => Export.Deliver(w, outputRoot, minimal)); File.WriteAllBytes(csv, original);
                Assert(!Directory.EnumerateFileSystemEntries(outputRoot).Any(p => Path.GetFileName(p).StartsWith('.')));
            });
        }
        finally { w.Dispose(); }
    }
}
