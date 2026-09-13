using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class CaseArchiveChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Assert(bool condition) { if (!condition) throw new Exception("Case archive assertion failed"); }
        static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected case archive rejection"); }
        string path = Path.Combine(root, "case-archive-project"); Workspace.Create(path, "用例の削除と復元");
        var w = new Workspace(path);
        try
        {
            var doc = w.CreateCase("TC-001", "削除・復元の対象");
            doc.Data.Steps = [new() { No = 1, Action = "登録", Condition = "ID＝00012\n権限＝管理者", Verdict = "OK" }]; doc = w.SaveCase(doc);
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            doc = w.AddEvidence(doc, new("image", "画面", "操作前後", 1, "", "共通説明", "", png, "png"));
            doc = w.AppendImage(doc, "E01", new("image", "画面", "操作後", 1, "", "画像の説明", "", png, "png"));
            string second = ImageGroups.Key(doc.Data.Evidence[0].Images![1]);
            doc = w.SaveAnnotation(doc, "E01", png, new() { Shapes = [new() { Type = "number", X = 0, Y = 0, N = 1 }] }, second);
            string video = Path.Combine(root, "case-archive-video.mp4"); File.WriteAllBytes(video, new byte[1024 * 1024]);
            doc = w.AddEvidence(doc, new("file", "画面", "動画", 1, "", "00:35 確認", "", [], "mp4", "recording.mp4", video));
            var empty = w.CreateCase("TC-002", "空の用例");
            string yaml = Path.Combine(path, "cases", "TC-001.yaml"), yml = Path.ChangeExtension(yaml, ".yml");
            File.Move(yaml, yml); File.WriteAllText(yml, "# exact YAML retained\n" + File.ReadAllText(yml)); doc = w.LoadCase(doc.Data.Id);
            var pending = new PendingCapture(Guid.NewGuid().ToString("N"), doc.Data.Id, 1, "未完了の撮影", "2026-09-13T12:00:00+09:00", png, "E01", true);
            new CaptureInbox(w).Stage(pending);
            string archive = "", emptyArchive = ""; byte[] expected = [];
            var files = Directory.EnumerateFiles(Path.Combine(path, "evidence", doc.Data.Id)).ToDictionary(p => p, Files.HashFile);

            check("case deletion rejects stale revisions and invalid IDs without removing data", () =>
            {
                var stale = Contract.Clone(doc); doc.Data.Note = "最新の説明"; doc = w.SaveCase(doc);
                Reject(() => w.DeleteCase(stale)); Assert(w.LoadCase(doc.Data.Id).Revision == doc.Revision && w.ListDeletedCases().Count == 0);
                var invalid = Contract.Clone(doc); invalid.Data.Id = "../outside"; Reject(() => w.DeleteCase(invalid));
            });
            check("whole case deletion retains exact YAML, grouped images, originals, videos and capture inbox", () =>
            {
                expected = File.ReadAllBytes(yml); archive = w.DeleteCase(doc);
                Assert(!File.Exists(yml) && !File.Exists(yaml));
                Assert(File.ReadAllBytes(Path.Combine(path, ".trash", "native-case-" + archive, "case.yaml")).SequenceEqual(expected));
                var item = w.ListDeletedCases().Single(); Assert(item.CaseId == doc.Data.Id && item.Title == doc.Data.Title && item.StepCount == 1 && item.EvidenceCount == 2 && item.ImageCount == 2);
                foreach (var file in files) Assert(Files.HashFile(file.Key) == file.Value);
                Assert(new CaptureInbox(w).PendingTokens(doc.Data.Id).Single() == pending.Token);
                Reject(() => new CaptureInbox(w).Commit(doc, pending));
            });
            check("deleted cases are excluded from report sheets, summary and delivery attachments", () =>
            {
                Assert(w.ListCases().Single().Data.Id == empty.Data.Id);
                string output = Export.Deliver(w, Path.Combine(root, "deleted-case-output"));
                using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false); Assert(!new OpenXmlValidator().Validate(document).Any());
                using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); using var stream = zip.GetEntry("xl/workbook.xml")!.Open();
                XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var sheets = XDocument.Load(stream).Descendants(s + "sheet").Select(e => (string?)e.Attribute("name")).ToArray();
                Assert(sheets.SequenceEqual(new[] { "サマリ", empty.Data.Id }));
                using var summary = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); Assert(!XDocument.Load(summary).ToString().Contains(doc.Data.Id));
                using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip")); Assert(!delivery.Entries.Any(e => e.FullName.Contains(doc.Data.Id) || e.FullName.Contains(".trash")));
            });
            check("deleted IDs including empty cases are reserved case-insensitively", () =>
            {
                emptyArchive = w.DeleteCase(empty); Assert(w.ListCases().Count == 0 && w.ListDeletedCases().Count == 2);
                foreach (string id in new[] { "TC-001", "tc-001", "TC-002", "tc-002" }) Reject(() => w.CreateCase(id, "別の用例"));
                Assert(w.UnavailableCaseIds().Contains("tc-002"));
                var next = w.CreateCase("TC-003", "新しい用例"); Assert(next.Data.Id == "TC-003");
            });
            check("trash listing and original yml restoration survive workspace restart", () =>
            {
                w.Dispose(); w = new Workspace(path);
                Assert(w.ListDeletedCases().Count == 2); doc = w.RestoreCase(archive);
                Assert(File.Exists(yml) && !File.Exists(yaml) && File.ReadAllBytes(yml).SequenceEqual(expected));
                Assert(doc.Data.Steps[0].Condition == "ID＝00012\n権限＝管理者" && doc.Data.Evidence[0].Images!.Count == 2);
                Assert(doc.Data.Evidence[0].Images![1].Annotations!.Shapes.Count == 1 && doc.Data.Evidence[1].Note == "00:35 確認");
                Reject(() => w.RestoreCase(archive)); Assert(w.ListDeletedCases().Single().CaseId == empty.Data.Id);
            });
            check("pending screenshots can resume in their original group after case restoration", () =>
            {
                var inbox = new CaptureInbox(w); doc = inbox.Commit(doc, inbox.Load(pending.Token));
                Assert(doc.Data.Evidence.Count == 2 && doc.Data.Evidence[0].Images!.Count == 3 && inbox.PendingTokens(doc.Data.Id).Count == 0);
                doc = inbox.Commit(doc, pending); Assert(doc.Data.Evidence[0].Images!.Count == 3);
            });
            check("same-ID restore collision never overwrites a newly created external case", () =>
            {
                archive = w.DeleteCase(doc);
                string collision = Path.Combine(path, "cases", "tc-001.yaml"); byte[] other = Yaml.Write(new TestCase { Id = "tc-001", Title = "外部で作成" }); Files.Atomic(collision, other);
                Reject(() => w.RestoreCase(archive)); Assert(File.ReadAllBytes(collision).SequenceEqual(other));
                Assert(w.ListDeletedCases().Any(c => c.ArchiveId == archive)); File.Delete(collision); doc = w.RestoreCase(archive);
            });
            check("missing or changed non-cover images and originals block restore while preserving trash", () =>
            {
                archive = w.DeleteCase(doc); var image = doc.Data.Evidence[0].Images![1];
                foreach (string file in new[] { image.File, image.OriginalFile! })
                {
                    string imagePath = Path.Combine(path, "evidence", doc.Data.Id, file); byte[] bytes = File.ReadAllBytes(imagePath);
                    File.AppendAllText(imagePath, "changed"); Reject(() => w.RestoreCase(archive));
                    Assert(!File.Exists(yml) && w.ListDeletedCases().Any(c => c.ArchiveId == archive));
                    File.Delete(imagePath); Reject(() => w.RestoreCase(archive)); File.WriteAllBytes(imagePath, bytes);
                }
                doc = w.RestoreCase(archive);
            });
            check("modified archive YAML and unsafe archive paths are rejected", () =>
            {
                archive = w.DeleteCase(doc); string archived = Path.Combine(path, ".trash", "native-case-" + archive, "case.yaml"); byte[] bytes = File.ReadAllBytes(archived);
                File.AppendAllText(archived, "\n# externally changed\n"); Reject(() => w.RestoreCase(archive)); Assert(!File.Exists(yml));
                foreach (string id in new[] { "../case", "", "native-case-" + archive }) Reject(() => w.RestoreCase(id));
                File.WriteAllBytes(archived, bytes); doc = w.RestoreCase(archive);
            });
            check("record-only interrupted deletes and completed restores are not shown as deleted", () =>
            {
                string unfinished = Guid.NewGuid().ToString("N");
                Files.Atomic(Path.Combine(path, ".trash", "native-case-" + unfinished, "record.json"), File.ReadAllBytes(Path.Combine(path, ".trash", "native-case-" + archive, "record.json")));
                Assert(w.ListDeletedCases().Single().ArchiveId == emptyArchive && w.LoadCase(doc.Data.Id).Revision == doc.Revision);
                empty = w.RestoreCase(emptyArchive); Assert(w.ListDeletedCases().Count == 0);
                Assert(w.ListCases().Count == 3);
            });
            check("archive-write failure leaves the active case and evidence untouched", () =>
            {
                string blocked = Path.Combine(root, "case-archive-blocked"); Workspace.Create(blocked, "blocked");
                using var other = new Workspace(blocked); var c = other.CreateCase("TC-BLOCK", "保持");
                File.WriteAllText(Path.Combine(blocked, ".trash"), "occupied");
                Reject(() => other.DeleteCase(c)); Assert(other.LoadCase(c.Data.Id).Revision == c.Revision);
            });
            check("restored cases return to the Excel summary and all evidence is delivered", () =>
            {
                string output = Export.Deliver(w, Path.Combine(root, "restored-case-output"));
                using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false); Assert(!new OpenXmlValidator().Validate(document).Any());
                using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx")); using var stream = zip.GetEntry("xl/workbook.xml")!.Open();
                Assert(XDocument.Load(stream).ToString().Contains(doc.Data.Id));
                foreach (var item in doc.Data.Evidence.SelectMany(ImageGroups.Items)) Assert(Files.HashFile(Path.Combine(output, "files", doc.Data.Id, item.File)) == item.Sha256);
                Console.WriteLine("RESTORED_CASE_OUTPUT=" + output);
            });
        }
        finally { w.Dispose(); }
    }
}
