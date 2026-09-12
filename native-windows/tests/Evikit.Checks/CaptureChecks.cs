using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class CaptureChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Assert(bool value) { if (!value) throw new Exception("Capture assertion failed"); }
        static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected capture rejection"); }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        PendingCapture Image(int number, int? step = 1) => new(Guid.NewGuid().ToString("N"), "TC-CAPTURE", step,
            $"画面 {number:000}", "2026-09-11T12:34:56.789+09:00", png.ToArray());
        string path = Path.Combine(root, "capture-project"); Workspace.Create(path, "連続スクリーンショット");
        using var w = new Workspace(path); var inbox = new CaptureInbox(w);
        var doc = w.CreateCase("TC-CAPTURE", "画面操作");
        doc.Data.Steps = [new() { No = 1, Action = "ログイン" }, new() { No = 4, Action = "登録" }]; doc = w.SaveCase(doc);
        var first = Image(1); var second = Image(2);
        check("continuous capture stores identical deliberate shots with separate IDs and exact times", () =>
        {
            doc = inbox.Commit(doc, first); doc = inbox.Commit(doc, second);
            Assert(doc.Data.Evidence.Count == 2 && doc.Data.Evidence.Select(e => e.Id).Distinct().Count() == 2);
            Assert(doc.Data.Evidence.All(e => e.Step == 1 && e.CapturedAt == first.CapturedAt));
            Assert(inbox.PendingTokens(doc.Data.Id).Count == 0);
            Assert(w.ReadEvidence(doc.Data.Id, doc.Data.Evidence[0]).SequenceEqual(png));
        });
        check("capture journal survives a failed save and can retry without loss", () =>
        {
            var failed = Image(3); string blocked = Path.Combine(path, "evidence", doc.Data.Id, failed.FileName);
            Directory.CreateDirectory(blocked); Reject(() => inbox.Commit(doc, failed));
            Assert(w.LoadCase(doc.Data.Id).Data.Evidence.Count == 2);
            var reloadedInbox = new CaptureInbox(w); Assert(reloadedInbox.PendingTokens(doc.Data.Id).Single() == failed.Token);
            Directory.Delete(blocked); doc = reloadedInbox.Commit(doc, reloadedInbox.Load(failed.Token));
            Assert(doc.Data.Evidence.Count == 3 && inbox.PendingTokens(doc.Data.Id).Count == 0);
        });
        check("recovery reuses an image written before interrupted YAML commit", () =>
        {
            var image = Image(4, 4); inbox.Stage(image);
            Files.Atomic(Path.Combine(path, "evidence", doc.Data.Id, image.FileName), png, true);
            doc = inbox.Commit(doc, inbox.Load(image.Token));
            Assert(doc.Data.Evidence.Count == 4 && doc.Data.Evidence[^1].Step == 4);
        });
        check("recovery after YAML commit is idempotent even after annotation and editing", () =>
        {
            var image = Image(5); inbox.Stage(image); doc = w.AddCapturedImage(doc, image);
            string id = doc.Data.Evidence[^1].Id;
            doc = w.SaveAnnotation(doc, id, png, new() { Shapes = [new() { Type = "number", X = 0, Y = 0, N = 1 }] });
            doc.Data.Evidence[^1].Note = "後から追加した説明"; doc = w.SaveCase(doc);
            doc = inbox.Commit(doc, inbox.Load(image.Token));
            Assert(doc.Data.Evidence.Count == 5 && doc.Data.Evidence[^1].Note == "後から追加した説明");
            Assert(doc.Data.Evidence[^1].Annotations != null && inbox.PendingTokens(doc.Data.Id).Count == 0);
        });
        check("capture rejects stale destinations, invalid step and reused token contents", () =>
        {
            var stale = Contract.Clone(doc); doc.Data.Note = "external edit"; doc = w.SaveCase(doc);
            var image = Image(6); Reject(() => inbox.Commit(stale, image));
            Assert(inbox.PendingTokens(doc.Data.Id).Count == 1); doc = inbox.Commit(doc, image);
            var invalid = Image(7, 99); Reject(() => w.AddCapturedImage(doc, invalid));
            Reject(() => w.AddCapturedImage(w.CreateCase("TC-OTHER", "other"), image));
            inbox.Stage(image); Reject(() => inbox.Stage(image with { Caption = "different" }));
            doc = inbox.Commit(doc, image);
        });
        check("recovery refuses a changed PNG without overwriting it", () =>
        {
            var image = Image(7); inbox.Stage(image);
            string file = Path.Combine(path, "evidence", doc.Data.Id, image.FileName);
            Files.Atomic(file, Encoding.UTF8.GetBytes("different bytes"));
            Reject(() => inbox.Commit(doc, image)); Assert(File.ReadAllText(file) == "different bytes");
            File.Delete(file); doc = inbox.Commit(doc, image);
        });
        // Interleave a table between image slots to ensure review cannot remove or edit it.
        doc = w.AddEvidence(doc, new("table", "DB", "00012", 1, "SELECT id", "table note", "", Encoding.UTF8.GetBytes("id\n00012\n"), "csv"));
        var table = doc.Data.Evidence[^1]; doc.Data.Evidence.RemoveAt(doc.Data.Evidence.Count - 1); doc.Data.Evidence.Insert(1, table); doc = w.SaveCase(doc);
        var before = Contract.Clone(doc);
        check("batch review reorders images and changes captions, steps and notes while preserving sources", () =>
        {
            var edits = doc.Data.Evidence.Where(e => e.Kind == "image").Reverse().Select((e, i) => new ImageReviewEdit(e.Id, $"確認 {i:000}", 4, "一括説明\n個別確認")).ToArray();
            doc = w.SaveImageReview(doc, edits);
            Assert(doc.Data.Evidence[1].Id == table.Id && doc.Data.Evidence[1].Source == "SELECT id");
            Assert(doc.Data.Evidence.Where(e => e.Kind == "image").Select(e => e.Id).SequenceEqual(edits.Select(e => e.Id)));
            foreach (var e in doc.Data.Evidence.Where(e => e.Kind == "image"))
            {
                var original = before.Data.Evidence.Single(o => o.Id == e.Id);
                Assert(e.File == original.File && e.Sha256 == original.Sha256 && e.CapturedAt == original.CapturedAt);
                Assert(e.OriginalFile == original.OriginalFile && e.Step == 4 && e.Note == "一括説明\n個別確認");
            }
        });
        check("batch review rejects stale, incomplete, duplicate and invalid-step edits", () =>
        {
            var edits = doc.Data.Evidence.Where(e => e.Kind == "image").Select(e => new ImageReviewEdit(e.Id, e.Caption, e.Step, e.Note)).ToArray();
            Reject(() => w.SaveImageReview(before, edits)); Reject(() => w.SaveImageReview(doc, edits.Skip(1).ToArray()));
            var duplicate = edits.ToArray(); duplicate[1] = duplicate[0]; Reject(() => w.SaveImageReview(doc, duplicate));
            var invalid = edits.ToArray(); invalid[0] = invalid[0] with { Step = 999 }; Reject(() => w.SaveImageReview(doc, invalid));
            Assert(w.LoadCase(doc.Data.Id).Revision == doc.Revision);
        });
        check("many captures persist incrementally and export all images in reviewed order", () =>
        {
            for (int i = 8; i <= 64; i++) doc = inbox.Commit(doc, Image(i));
            var images = doc.Data.Evidence.Where(e => e.Kind == "image").ToArray(); Assert(images.Length == 64);
            Assert(w.LoadCase(doc.Data.Id).Data.Evidence.Count == 65);
            string output = Export.Deliver(w.Snapshot(), Path.Combine(root, "capture-output"));
            using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
            Assert(!new OpenXmlValidator().Validate(document).Any());
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
            using var drawing = zip.GetEntry("xl/drawings/drawing2.xml")!.Open();
            var xml = XDocument.Load(drawing); Assert(xml.Root!.Elements().Count() == 64);
            using var sheet = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
            var contents = XDocument.Load(sheet); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var titles = contents.Descendants(ns + "t").Select(e => e.Value).Where(t => t.Contains("[画面]")).ToArray();
            Assert(titles.Length == images.Length && titles.Select((t, i) => t.StartsWith(images[i].Id + " ")).All(x => x));
            Console.WriteLine("CAPTURE_OUTPUT=" + output);
        });
        check("leftover recovery record never resurrects a deliberately deleted screenshot", () =>
        {
            inbox.Stage(first);
            string id = doc.Data.Evidence.Single(e => (e.OriginalFile ?? e.File) == first.FileName).Id;
            var deletion = w.DeleteEvidence(doc, id); doc = deletion.Document;
            doc = inbox.Commit(doc, first);
            Assert(doc.Data.Evidence.All(e => e.Id != id) && inbox.PendingTokens(doc.Data.Id).Count == 0);
            doc = w.RestoreEvidence(deletion.ArchiveId);
            Assert(doc.Data.Evidence.Any(e => e.Id == id));
        });
    }
}
