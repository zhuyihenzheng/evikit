using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Evikit.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

if (args.Length == 3 && args[0] == "--export-fixture")
{
    using var fixture = new Workspace(args[1]);
    var output = Export.Deliver(fixture.Snapshot(), args[2]);
    using var file = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
    var errors = new OpenXmlValidator().Validate(file).ToArray();
    if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(e => e.Description)));
    Console.WriteLine(output);
    return;
}

string sample = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("../examples/reference");
string root = Path.Combine(Path.GetTempPath(), "evikit-native-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
void Equal<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected {b}, got {a}"); }
void True(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws(Action action) { bool threw = false; try { action(); } catch { threw = true; } True(threw, "Expected rejection"); }
byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
string testProject = Path.Combine(root, "project");
Workspace.Create(testProject, "デスクトップ版 検証");
try
{
    using (var w = new Workspace(testProject))
    {
        Check("shared lock prevents second editor", () => Throws(() => { using var second = new Workspace(testProject); }));
        Check("reserved IDs / traversal rejected", () => { foreach (var id in new[] { "../case", "NUL", "con", "COM1", "A/B", "A:B" }) Throws(() => w.CreateCase(id, "invalid")); Throws(() => Files.Safe(testProject, "..", "escape")); });
        var doc = w.CreateCase("TC-001", "正常系");
        Check("case IDs collide case-insensitively", () => Throws(() => w.CreateCase("tc-001", "duplicate")));
        Check("defaults and Japanese YAML round trip", () => { Equal(doc.Data.Title, "正常系"); Equal(w.LoadProject().Data.Name, "デスクトップ版 検証"); Equal(Contract.Verdict(doc.Data), "未実施"); });
        Check("quoted dates, leading zeros and null text fields", () =>
        {
            var c = Yaml.Read<TestCase>(Text("id: TC-100\ntitle: null\ndate: \"2026-09-05\"\nsteps: []\nevidence: []\n")); Contract.Validate(c); Equal(c.Title, "");
            c.Title = "00012"; string written = Encoding.UTF8.GetString(Yaml.Write(c)); True(written.Contains("\"2026-09-05\"")); True(written.Contains("\"00012\"")); Equal(Yaml.Read<TestCase>(Yaml.Write(c)).Title, "00012");
        });
        Check("unknown YAML fields and duplicate keys fail instead of data loss", () => { Throws(() => Yaml.Read<TestCase>(Text("id: TC-100\nfutureField: 1\n"))); Throws(() => Yaml.Read<TestCase>(Text("id: A\nid: B\n"))); });
        Check("browser nullish numeric defaults and multi-document rejection", () =>
        {
            var p = Yaml.Read<Project>(Text("name: example\nexcerptLines: null\nimageMaxWidth: ~\n")); Contract.Validate(p); Equal(p.ExcerptLines, 30); Equal(p.ImageMaxWidth, 640);
            var invalid = Yaml.Read<Project>(Text("name: example\nexcerptLines: 0\n")); Throws(() => Contract.Validate(invalid));
            Throws(() => Yaml.Read<Project>(Text("name: first\n---\nname: second\n")));
        });
        Check("verdict precedence and empty steps", () =>
        {
            var c = new TestCase { Steps = [new() { No = 1, Verdict = "OK" }, new() { No = 2, Verdict = "" }] }; Equal(Contract.Verdict(c), "未実施"); c.Steps[1].Verdict = "NG"; Equal(Contract.Verdict(c), "NG"); c.Verdict = "保留"; Equal(Contract.Verdict(c), "保留");
        });
        Check("step/evidence references validated", () => { var c = Contract.Clone(doc.Data); c.Steps = [new() { No = 1 }, new() { No = 1 }]; Throws(() => Contract.Validate(c)); });
        doc.Data.Steps.Add(new() { No = 1, Action = "ログイン", Expected = "成功", Actual = "成功", Verdict = "OK" }); doc = w.SaveCase(doc);
        Check("optimistic revision rejects stale metadata", () => { var stale = Contract.Clone(doc); doc.Data.Note = "saved"; doc = w.SaveCase(doc); stale.Data.Title = "overwrite"; Throws(() => w.SaveCase(stale)); Equal(w.LoadCase("TC-001").Data.Title, "正常系"); });
        Check("TSV / quoted multiline CSV preserve all strings", () =>
        {
            var t = Tables.Parse("id\tvalue\n00012\t\"a,b\nnext\"\n"); Equal(t[1][0], "00012"); Equal(t[1][1], "a,b\nnext"); Equal(Tables.Parse(Tables.ToCsv(t))[1][1], "a,b\nnext");
            var csv = Tables.Parse("\"a\tb\",c\n\"x\ty\",z\n"); Equal(csv[0].Count, 2); Equal(csv[1][0], "x\ty"); Throws(() => Tables.Parse("a,b\n1\n")); Throws(() => Tables.Parse("a,b\n\"bad\n"));
        });
        Check("UTF-8 rejects binary / legacy text", () => Throws(() => Inputs.Utf8([0xff, 0xfe, 0x82, 0xa0])));
        doc = w.AddEvidence(doc, new("table", "DB", "顧客データ", 1, "SELECT * FROM users;", "先頭ゼロ", "", Text("id\tname\n00012\t=HYPERLINK(\"\"bad\"\")\n".Replace("=HYPERLINK(\"\"bad\"\")", "=1+1")), "csv"));
        Check("structured table stored as CSV and source SQL", () => { var e = doc.Data.Evidence.Single(); Equal(Tables.Parse(Inputs.Utf8(w.ReadEvidence("TC-001", e)))[1][0], "00012"); Equal(e.Source, "SELECT * FROM users;"); Equal(e.Step, 1); });
        string csvPath = Path.Combine(testProject, "evidence", "TC-001", doc.Data.Evidence[0].File); var originalCsv = File.ReadAllBytes(csvPath);
        Check("metadata save cannot bless tampered evidence", () => { File.AppendAllText(csvPath, "tamper"); doc.Data.Note = "metadata"; doc = w.SaveCase(doc); Throws(() => w.Snapshot()); File.WriteAllBytes(csvPath, originalCsv); });
        Check("delete, monotonic ID and restore", () =>
        {
            var deleted = w.DeleteEvidence(doc, "E01"); doc = w.AddEvidence(deleted.Document, new("text", "ログ", "ログ", 1, "app.log", "", "log", Text("success\n"), "log"));
            Equal(doc.Data.Evidence.Single().Id, "E02"); doc = w.RestoreEvidence(deleted.ArchiveId); Equal(doc.Data.Evidence.Count, 2); Throws(() => w.RestoreEvidence(deleted.ArchiveId));
        });
        Check("restore unlinks deleted step", () =>
        {
            var deleted = w.DeleteEvidence(doc, "E01"); doc = deleted.Document; doc.Data.Steps.Clear(); foreach (var e in doc.Data.Evidence) e.Step = null; doc = w.SaveCase(doc); doc = w.RestoreEvidence(deleted.ArchiveId); Equal(doc.Data.Evidence.Single(e => e.Id == "E01").Step, null);
        });
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        doc = w.AddEvidence(doc, new("image", "画面", "小さい画像", null, "", "", "", png, "png")); var imageId = doc.Data.Evidence[^1].Id;
        Check("annotations preserve original bytes and reset", () =>
        {
            doc = w.SaveAnnotation(doc, imageId, png, new() { Shapes = [new() { Type = "rect", X = 0, Y = 0, W = 1, H = 1, Color = "red" }] });
            var e = doc.Data.Evidence.Single(e => e.Id == imageId); True(e.File != e.OriginalFile); Equal(Files.Hash(w.ReadEvidence(doc.Data.Id, e, true)), Files.Hash(png));
            doc = w.ResetAnnotation(doc, imageId); True(doc.Data.Evidence.Single(e => e.Id == imageId).OriginalFile == null);
        });
        doc = w.AddEvidence(doc, new("file", "その他", "添付<script>", null, "", "", "", [0, 1, 2, 3, 255], "bin", "attachment.bin"));
        var snapshot = w.Snapshot();
        Check("snapshot isolated from later edits", () => { doc.Data.Title = "changed later"; doc = w.SaveCase(doc); Equal(snapshot.Cases[0].Data.Title, "正常系"); });
        string output = "";
        Check("complete export and ZIP manifest hashes", () =>
        {
            output = Export.Deliver(snapshot, Path.Combine(root, "outputs"));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            using var zip = ZipFile.OpenRead(Path.Combine(output, "delivery.zip"));
            foreach (var f in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                string path = f.GetProperty("path").GetString()!; string hash = f.GetProperty("sha256").GetString()!;
                Equal(Files.Hash(File.ReadAllBytes(Path.Combine(output, path))), hash); using var stream = zip.GetEntry(path)!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); Equal(Files.Hash(memory.ToArray()), hash);
            }
            True(zip.GetEntry("manifest.json") != null); True(zip.GetEntry("delivery.zip") == null);
        });
        Check("OOXML values remain strings and formulas never execute", () =>
        {
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml = XDocument.Load(stream);
            Equal(xml.Descendants(s + "f").Count(), 0); True(xml.Descendants(s + "t").Any(t => t.Value == "00012")); True(xml.Descendants(s + "t").Any(t => t.Value == "=1+1"));
            True(xml.Descendants(s + "c").All(c => (string?)c.Attribute("t") == "inlineStr"));
            True(xml.Descendants(s + "row").All(r => (string?)r.Attribute("customHeight") == "1")); True(zip.GetEntry("xl/drawings/drawing2.xml") != null);
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels"))) { using var xmlStream = entry.Open(); _ = XDocument.Load(xmlStream); }
        });
        Check("native delivery contains no HTML", () => { True(!Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories).Any()); using var z = ZipFile.OpenRead(Path.Combine(output, "delivery.zip")); True(!z.Entries.Any(e => e.FullName.EndsWith(".html"))); });
        Check("Microsoft Open XML validator accepts workbook", () =>
        {
            using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
            var errors = new OpenXmlValidator().Validate(document).ToList();
            True(errors.Count == 0, string.Join("\n", errors.Take(10).Select(e => e.Description + " " + e.Path?.XPath)));
        });
        Check("failed export publishes nothing", () =>
        {
            var bad = Contract.Clone(snapshot); var e = bad.Cases[0].Evidence.First(e => e.Metadata.Kind == "image"); e.Bytes[0] = 0;
            string failRoot = Path.Combine(root, "failed-output"); Throws(() => Export.Deliver(bad, failRoot)); Equal(Directory.EnumerateFileSystemEntries(failRoot).Count(), 0);
        });
        Check("export snapshot validates originals too", () =>
        {
            doc = w.SaveAnnotation(doc, imageId, png, new()); var e = doc.Data.Evidence.Single(e => e.Id == imageId); string path = Path.Combine(testProject, "evidence", doc.Data.Id, e.OriginalFile!); File.AppendAllText(path, "tamper"); Throws(() => w.Snapshot()); File.WriteAllBytes(path, png);
        });
    }
    Check("lock removed on close and project opens again", () => { True(!File.Exists(Path.Combine(testProject, ".evikit.lock"))); using var reopened = new Workspace(testProject); });
    Check("yml extension is preserved", () =>
    {
        File.Move(Path.Combine(testProject, "cases", "TC-001.yaml"), Path.Combine(testProject, "cases", "TC-001.yml")); using var w = new Workspace(testProject); var c = w.LoadCase("TC-001"); c.Data.Note = "yml"; w.SaveCase(c); True(!File.Exists(Path.Combine(testProject, "cases", "TC-001.yaml")));
    });
    Check("internal symlink rejected", () =>
    {
        string outside = Path.Combine(root, "outside.txt"); File.WriteAllText(outside, "outside"); string link = Path.Combine(testProject, "link.txt");
        try { File.CreateSymbolicLink(link, outside); } catch (UnauthorizedAccessException) { Console.WriteLine("SKIP symlink creation requires Windows developer mode"); return; }
        Throws(() => Files.Safe(testProject, "link.txt"));
    });
    if (Directory.Exists(sample))
    {
        string copied = Path.Combine(root, "sample"); Directory.CreateDirectory(copied);
        foreach (var file in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sample, file); if (rel.Split(Path.DirectorySeparatorChar).Any(s => s is "exports" or ".trash") || rel == ".evikit.lock") continue;
            string dest = Path.Combine(copied, rel); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest);
        }
        Check("current browser project opens and exports without migration", () =>
        {
            using var w = new Workspace(copied); var snapshot = w.Snapshot(); True(snapshot.Cases.Count >= 3);
            foreach (var c in w.ListCases()) w.SaveCase(c);
            string target = Export.Deliver(w.Snapshot(), Path.Combine(root, "sample-output"));
            using var document = SpreadsheetDocument.Open(Path.Combine(target, "report.xlsx"), false);
            var errors = new OpenXmlValidator().Validate(document).ToList();
            True(errors.Count == 0, string.Join("\n", errors.Take(10).Select(e => e.Description + " " + e.Path?.XPath)));
            Console.WriteLine("SAMPLE_OUTPUT=" + target); Console.WriteLine("ROUNDTRIP_PROJECT=" + copied);
        });
    }
    Console.WriteLine($"{passed} checks passed. Artifacts: {root}");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Failure artifacts: " + root); Environment.ExitCode = 1; }
