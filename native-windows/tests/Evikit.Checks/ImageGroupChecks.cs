using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class ImageGroupChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Assert(bool value) { if (!value) throw new Exception("Image group assertion failed"); }
        static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected image group rejection"); }
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        string path = Path.Combine(root, "image-group-project"); Workspace.Create(path, "複数画像のエビデンス");
        using var w = new Workspace(path); var inbox = new CaptureInbox(w);
        var doc = w.CreateCase("TC-GALLERY", "ログインから登録まで");
        doc.Data.Steps = [new() { No = 1, Action = "ログイン", Condition = "顧客 ID＝00012" }, new() { No = 2, Action = "登録" }]; doc = w.SaveCase(doc);
        NewEvidence Input(string caption, int step = 1) => new("image", "画面", caption, step, "manual", "元の説明 " + caption, "", png, "png");
        PendingCapture Capture(string? id, int step = 1) => new(Guid.NewGuid().ToString("N"), doc.Data.Id, step, "撮影", "2026-09-13T10:00:00+09:00", png, id, true, "連続撮影の共通見出し");
        string group = "", second = "";
        check("legacy image remains a single file until explicitly appended", () =>
        {
            doc = w.AddEvidence(doc, Input("ログイン画面")); group = doc.Data.Evidence.Single().Id;
            Assert(doc.Data.Evidence[0].Images == null && !Encoding.UTF8.GetString(Yaml.Write(doc.Data)).Contains("images:"));
            var original = Contract.Clone(doc.Data.Evidence[0]);
            doc = w.AppendImage(doc, group, Input("ログイン後"));
            var e = doc.Data.Evidence.Single();
            Assert(e.Images!.Count == 2 && e.File == "" && e.Caption == original.Caption && e.Note == original.Note && e.Step == 1);
            Assert(e.Images[0].File == original.File && e.Images[0].Sha256 == original.Sha256 && e.Images[0].Source == original.Source);
            second = ImageGroups.Key(e.Images[1]);
            var reloaded = w.LoadCase(doc.Data.Id); Assert(reloaded.Data.Evidence.Count == 1 && reloaded.Data.Evidence[0].Images!.Count == 2);
        });
        check("gallery metadata and ordering preserve common fields and image identity", () =>
        {
            var e = doc.Data.Evidence.Single(); string first = ImageGroups.Key(e.Images![0]);
            doc = w.SaveGallery(doc, group, [new(second, "画像 00012", "個別\n説明"), new(first, "ログイン前", "")]);
            e = doc.Data.Evidence.Single(); Assert(e.Images![0].File == second && e.Caption == "ログイン画面" && e.Note == "元の説明 ログイン画面");
            Assert(e.Images[0].Note == "個別\n説明" && e.Images[1].Source == "manual");
            var edits = e.Images.Select(i => new GalleryEdit(ImageGroups.Key(i), i.Caption, i.Note)).ToArray();
            Reject(() => w.SaveGallery(doc, group, [edits[0]])); Reject(() => w.SaveGallery(doc, group, [edits[0], edits[0]]));
        });
        check("annotation changes only the selected image and reset keeps its stable identity", () =>
        {
            var e = doc.Data.Evidence.Single(); string untouched = JsonSerializer.Serialize(e.Images![1], Contract.Json);
            doc = w.SaveAnnotation(doc, group, png, new() { Shapes = [new() { Type = "number", N = 1, X = 0, Y = 0 }] }, second);
            e = doc.Data.Evidence.Single(); Assert(ImageGroups.Key(e.Images![0]) == second && e.Images[0].OriginalFile == second && e.Images[0].File != second);
            Assert(JsonSerializer.Serialize(e.Images[1], Contract.Json) == untouched);
            Assert(w.ReadEvidence(doc.Data.Id, e.Images[0], true).SequenceEqual(png));
            Reject(() => w.SaveAnnotation(doc, group, png, new()));
            doc = w.ResetAnnotation(doc, group, second); Assert(doc.Data.Evidence.Single().Images![0].File == second);
        });
        check("one continuous capture destination accumulates images without extra evidence IDs", () =>
        {
            int next = doc.Data.NextEvidenceNumber!.Value;
            for (int i = 0; i < 30; i++) doc = inbox.Commit(doc, Capture(group));
            Assert(doc.Data.Evidence.Count == 1 && doc.Data.Evidence.Single().Images!.Count == 32 && doc.Data.NextEvidenceNumber == next);
            var fresh = Capture(null, 2); doc = inbox.Commit(doc, fresh);
            Assert(doc.Data.Evidence.Count == 2 && doc.Data.Evidence[^1].Caption == fresh.GroupCaption && doc.Data.Evidence[^1].Images!.Count == 1);
        });
        check("group capture recovery is idempotent after YAML commit and annotation", () =>
        {
            var capture = Capture(group); inbox.Stage(capture); doc = w.AddCapturedImage(doc, capture);
            int count = doc.Data.Evidence[0].Images!.Count;
            doc = w.SaveAnnotation(doc, group, png, new(), capture.FileName);
            doc = inbox.Commit(doc, capture); Assert(doc.Data.Evidence[0].Images!.Count == count && inbox.PendingTokens(doc.Data.Id).Count == 0);
            var failed = Capture(group); inbox.Stage(failed); string blocked = Path.Combine(path, "evidence", doc.Data.Id, failed.FileName); Directory.CreateDirectory(blocked);
            Reject(() => inbox.Commit(doc, failed)); Assert(w.LoadCase(doc.Data.Id).Data.Evidence[0].Images!.Count == count);
            Directory.Delete(blocked); doc = inbox.Commit(doc, inbox.Load(failed.Token)); Assert(doc.Data.Evidence[0].Images!.Count == count + 1);
        });
        check("deleted individual image is recoverable but not resurrected by a leftover journal", () =>
        {
            var capture = Capture(group); doc = inbox.Commit(doc, capture); inbox.Stage(capture);
            int count = doc.Data.Evidence[0].Images!.Count;
            var removed = w.DeleteImage(doc, group, capture.FileName); doc = removed.Document;
            doc = inbox.Commit(doc, capture); Assert(doc.Data.Evidence[0].Images!.Count == count - 1);
            doc = w.RestoreImage(removed.ArchiveId); Assert(doc.Data.Evidence[0].Images!.Count == count);
            Reject(() => w.RestoreImage(removed.ArchiveId));
            var one = doc.Data.Evidence[1]; Reject(() => w.DeleteImage(doc, one.Id, ImageGroups.Key(one.Images![0])));
        });
        check("invalid or stale destination cannot write or redirect a screenshot", () =>
        {
            foreach (var capture in new[] { Capture("E999"), Capture(group, 2) })
            {
                Reject(() => w.AddCapturedImage(doc, capture)); Assert(!File.Exists(Path.Combine(path, "evidence", doc.Data.Id, capture.FileName)));
            }
            var stale = Contract.Clone(doc); doc.Data.Note = "new revision"; doc = w.SaveCase(doc);
            Reject(() => w.AppendImage(stale, group, Input("stale")));
            Reject(() => w.AppendImage(doc, group, Input("invalid") with { Data = [1, 2, 3] }));
            Assert(w.LoadCase(doc.Data.Id).Revision == doc.Revision);
        });
        check("merge retains one record, ordered images, old descriptions and retired IDs", () =>
        {
            doc = w.AddEvidence(doc, Input("旧エビデンス")); string old = doc.Data.Evidence[^1].Id;
            doc = w.AppendImage(doc, old, Input("旧画像 2")); doc.Data.Evidence[^1].Note = "旧グループの共通説明"; doc = w.SaveCase(doc);
            int before = doc.Data.Evidence[0].Images!.Count, next = doc.Data.NextEvidenceNumber!.Value;
            doc = w.MergeImages(doc, [old, group]);
            var e = doc.Data.Evidence[0]; Assert(e.Id == group && e.Images!.Count == before + 2 && e.Caption == "ログイン画面");
            Assert(e.Images![^1].Note.Contains("旧グループの共通説明") && doc.Data.Evidence.All(e => e.Id != old));
            Assert(doc.Data.NextEvidenceNumber == next);
            Reject(() => w.MergeImages(doc, [group, doc.Data.Evidence[1].Id]));
        });
        check("empty, mixed-kind, duplicate and unsafe image collections are rejected", () =>
        {
            foreach (Action<Evidence> corrupt in new Action<Evidence>[] {
                e => e.Images = [], e => e.Kind = "text", e => e.Images!.Add(Contract.Clone(e.Images[0])),
                e => e.Images![1].File = "../escape.png", e => e.Images![1].Sha256 = "invalid", e => e.File = "ambiguous.png" })
            { var c = Contract.Clone(doc.Data); corrupt(c.Evidence[0]); Reject(() => w.SaveCase(new(c, doc.Revision))); }
        });
        check("group deletion and restoration retain all images, originals and metadata", () =>
        {
            string serialized = JsonSerializer.Serialize(doc.Data.Evidence[0], Contract.Json);
            var removed = w.DeleteEvidence(doc, group); doc = removed.Document;
            doc = w.RestoreEvidence(removed.ArchiveId);
            Assert(JsonSerializer.Serialize(doc.Data.Evidence.Single(e => e.Id == group), Contract.Json) == serialized);
        });
        check("Excel groups all images under one heading and packages each link in reviewed order", () =>
        {
            foreach (string output in new[] { Export.Deliver(w, Path.Combine(root, "gallery-output")), Export.Deliver(w.Snapshot(), Path.Combine(root, "gallery-snapshot-output")) })
            {
                using var file = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false); Assert(!new OpenXmlValidator().Validate(file).Any());
                using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
                using var sheet = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var values = XDocument.Load(sheet).Descendants(s + "t").Select(t => t.Value).ToArray();
                Assert(values.Count(t => t.Contains("[画面]")) == doc.Data.Evidence.Count);
                Assert(values.Any(t => t.Contains("画像 00012")) && values.Any(t => t == "画像の確認事項：個別\n説明"));
                var images = doc.Data.Evidence.SelectMany(ImageGroups.Items).ToArray();
                using var drawing = zip.GetEntry("xl/drawings/drawing2.xml")!.Open(); var anchors = XDocument.Load(drawing).Root!.Elements().ToArray();
                Assert(anchors.Length == images.Length);
                XNamespace d = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
                var rows = anchors.Select(a => int.Parse(a.Element(d + "from")!.Element(d + "row")!.Value)).ToArray(); Assert(rows.Zip(rows.Skip(1)).All(p => p.First < p.Second));
                var labels = values.Where(v => v.StartsWith("画像ファイル：")).ToArray();
                Assert(labels.SequenceEqual(images.Select(i => "画像ファイル：" + (i.OriginalName == "" ? i.File : i.OriginalName))));
                using var delivery = ZipFile.OpenRead(Path.Combine(output, "delivery.zip"));
                foreach (var image in images)
                {
                    string rel = $"files/{doc.Data.Id}/{image.File}";
                    Assert(delivery.GetEntry(rel) != null && Files.HashFile(Path.Combine(output, rel)) == image.Sha256);
                }
                Assert(!delivery.Entries.Any(e => e.FullName.EndsWith(".html")));
                Console.WriteLine("GALLERY_OUTPUT=" + output);
            }
        });
        check("tampering with a non-cover image or its original blocks export and restoration", () =>
        {
            var e = doc.Data.Evidence.Single(e => e.Id == group); var annotated = e.Images!.First(i => i.OriginalFile != null);
            foreach (string file in new[] { e.Images![1].File, annotated.OriginalFile! })
            {
                string imagePath = Path.Combine(path, "evidence", doc.Data.Id, file); byte[] original = File.ReadAllBytes(imagePath); File.AppendAllText(imagePath, "changed");
                string output = Path.Combine(root, "gallery-invalid-output"); Reject(() => Export.Deliver(w, output)); Assert(!Directory.EnumerateFileSystemEntries(output).Any());
                var removed = w.DeleteEvidence(doc, group); doc = removed.Document; Reject(() => w.RestoreEvidence(removed.ArchiveId));
                File.WriteAllBytes(imagePath, original); doc = w.RestoreEvidence(removed.ArchiveId);
            }
        });
    }
}
