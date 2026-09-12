using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Evikit.Core;

internal static class VideoChecks
{
    internal static void Run(string root, Action<string, Action> check)
    {
        static void Assert(bool value, string message = "Video assertion failed") { if (!value) throw new Exception(message); }
        static void Reject(Action action) { try { action(); } catch { return; } throw new Exception("Expected video rejection"); }
        string project = Path.Combine(root, "video-project"); Workspace.Create(project, "動画の証拠");
        using var w = new Workspace(project); var doc = w.CreateCase("TC-VIDEO", "動画と画面の混在");
        doc.Data.Steps.Add(new() { No = 1, Action = "操作する", Verdict = "OK" }); doc = w.SaveCase(doc);
        // Synthetic payload tests file handling, not MP4 decoding or actual recording.
        string source = Path.Combine(root, "操作記録.MP4");
        using (var file = File.Create(source))
        {
            var buffer = new byte[1024 * 1024]; new Random(42).NextBytes(buffer);
            for (int i = 0; i < 64; i++) file.Write(buffer);
        }
        NewEvidence Input(string path) => new("file", "画面", "操作記録", 1, "録画ファイル", "00:35 エラー\n01:12 成功", "", [], "mp4", Path.GetFileName(path), path);
        check("video formats stay file evidence with screen category", () =>
        {
            foreach (var extension in new[] { "MP4", "mov", "avi", "wmv", "mkv", "webm", "m4v" })
            {
                Assert(Media.IsVideo("sample." + extension)); Assert(Inputs.Detect("sample." + extension) == ("file", "画面", ""));
            }
            Assert(!Media.IsVideo("sample.mp4.exe"));
        });
        check("64 MiB video import uses bounded buffers and preserves original bytes", () =>
        {
            long start = GC.GetTotalAllocatedBytes(true); doc = w.AddEvidence(doc, Input(source));
            long allocation = GC.GetTotalAllocatedBytes(true) - start;
            var e = doc.Data.Evidence.Single();
            Assert(allocation < 32 * 1024 * 1024, $"Video import allocated {allocation:N0} bytes");
            Assert(e.Size == 64 * 1024 * 1024 && e.Sha256 == Files.HashFile(source));
            Assert(e.Kind == "file" && e.Step == 1 && e.OriginalName == "操作記録.MP4");
            Assert(!File.ReadAllText(Path.Combine(project, "cases", "TC-VIDEO.yaml")).Contains(root));
            Console.WriteLine($"VIDEO_IMPORT_ALLOCATED={allocation}");
        });
        check("missing and oversized video imports do not publish metadata or partial files", () =>
        {
            int count = Directory.GetFiles(Path.Combine(project, "evidence", doc.Data.Id)).Length;
            Reject(() => w.AddEvidence(doc, Input(Path.Combine(root, "missing.mp4"))));
            string large = Path.Combine(root, "too-large.mp4");
            using (var file = File.Create(large)) file.SetLength(Media.MaxFileBytes + 1);
            try { Reject(() => w.AddEvidence(doc, Input(large))); } finally { File.Delete(large); }
            Assert(w.LoadCase(doc.Data.Id).Revision == doc.Revision);
            Assert(Directory.GetFiles(Path.Combine(project, "evidence", doc.Data.Id)).Length == count);
        });
        check("stale or non-file path imports are rejected", () =>
        {
            var stale = Contract.Clone(doc); doc.Data.Note = "saved"; doc = w.SaveCase(doc);
            Reject(() => w.AddEvidence(stale, Input(source)));
            Reject(() => w.AddEvidence(doc, Input(source) with { Kind = "image" }));
            Reject(() => w.AddEvidence(doc, Input(source) with { Extension = "../mp4" }));
        });
        var video = doc.Data.Evidence[0];
        check("large videos can be copied, deleted and restored without byte-array reads", () =>
        {
            Reject(() => w.ReadEvidence(doc.Data.Id, video));
            string copied = Path.Combine(root, "取り出した動画.mp4"); w.CopyEvidence(doc.Data.Id, video, copied);
            Assert(Files.HashFile(copied) == video.Sha256);
            var deleted = w.DeleteEvidence(doc, video.Id); doc = w.RestoreEvidence(deleted.ArchiveId);
            Assert(doc.Data.Evidence.Single().Id == video.Id && doc.Data.Evidence.Single().Size == 64 * 1024 * 1024);
        });
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        doc = w.AddEvidence(doc, new("image", "画面", "動画の確認画像 00:35", 1, $"動画 {video.Id} / 00:35", "エラーを確認", "", png, "png"));
        string output = "";
        check("video delivery streams attachments, preserves hashes and includes screenshot and notes", () =>
        {
            long start = GC.GetTotalAllocatedBytes(true); output = Export.Deliver(w, Path.Combine(root, "video-output"));
            long allocation = GC.GetTotalAllocatedBytes(true) - start;
            Assert(allocation < 32 * 1024 * 1024, $"Video export allocated {allocation:N0} bytes");
            Console.WriteLine($"VIDEO_EXPORT_ALLOCATED={allocation}");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
                Assert(Files.HashFile(Path.Combine(output, entry.GetProperty("path").GetString()!)) == entry.GetProperty("sha256").GetString());
            using var package = ZipFile.OpenRead(Path.Combine(output, "delivery.zip"));
            var attachment = package.GetEntry($"files/{doc.Data.Id}/{video.File}")!;
            Assert(attachment.Length == video.Size);
            using var stream = attachment.Open();
            Assert(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)) == video.Sha256);
            Assert(!package.Entries.Any(e => e.FullName.EndsWith(".html")));
            Console.WriteLine("VIDEO_OUTPUT=" + output);
        });
        check("Excel contains relative video links, time notes, and image, without embedded media or macros", () =>
        {
            using var document = SpreadsheetDocument.Open(Path.Combine(output, "report.xlsx"), false);
            Assert(!new OpenXmlValidator().Validate(document).Any());
            using var zip = ZipFile.OpenRead(Path.Combine(output, "report.xlsx"));
            using var sheet = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml = XDocument.Load(sheet);
            Assert(xml.ToString().Contains("動画を開く：操作記録.MP4") && xml.ToString().Contains("00:35 エラー"));
            Assert(zip.GetEntry("xl/drawings/drawing2.xml") != null);
            using var rels = zip.GetEntry("xl/worksheets/_rels/sheet2.xml.rels")!.Open();
            Assert(XDocument.Load(rels).Root!.Elements().Any(e => (string?)e.Attribute("Target") == $"files/{doc.Data.Id}/{video.File}" && (string?)e.Attribute("TargetMode") == "External"));
            Assert(!zip.Entries.Any(e => e.FullName.Contains("embeddings/") || e.FullName.EndsWith(".mp4") || e.FullName.EndsWith("vbaProject.bin")));
        });
        check("tampered video fails export and file extraction without publishing partial delivery", () =>
        {
            string path = w.EvidencePath(doc.Data.Id, video); byte original;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) { original = (byte)file.ReadByte(); file.Position = 0; file.WriteByte((byte)(original ^ 1)); }
            try
            {
                string failed = Path.Combine(root, "tampered-video-output"); Reject(() => Export.Deliver(w, failed));
                Assert(!Directory.EnumerateFileSystemEntries(failed).Any());
                string destination = Path.Combine(root, "do-not-overwrite.mp4"); File.WriteAllText(destination, "keep existing");
                Reject(() => w.CopyEvidence(doc.Data.Id, video, destination, false)); Assert(File.ReadAllText(destination) == "keep existing");
                Reject(() => w.VerifyEvidence(doc.Data.Id, video));
            }
            finally { using var file = new FileStream(path, FileMode.Open, FileAccess.Write); file.WriteByte(original); }
        });
    }
}
