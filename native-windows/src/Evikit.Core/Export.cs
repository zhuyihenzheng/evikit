using System.IO.Compression;
using System.Text.Json;

namespace Evikit.Core;

public static class Export
{
    public static string Deliver(ProjectSnapshot snapshot, string outputRoot, ExportOptions? options = null, string? caseId = null)
    {
        options ??= new();
        var selected = options.Select(snapshot, caseId);
        return DeliverCore(outputRoot, options, caseId, stage =>
        {
            foreach (var c in selected.Cases)
                foreach (var e in c.Evidence)
                    foreach (var image in e.Attachments())
                        Files.Atomic(Files.Safe(stage, "files", c.Data.Id, image.Metadata.File), image.Bytes, true);
            return selected;
        });
    }

    public static string Deliver(Workspace workspace, string outputRoot, ExportOptions? options = null, string? caseId = null)
    {
        options ??= new();
        return DeliverCore(outputRoot, options, caseId, stage => workspace.StageSnapshot(Path.Combine(stage, "files"), options, caseId));
    }

    private static string DeliverCore(string outputRoot, ExportOptions options, string? caseId, Func<string, ProjectSnapshot> prepare)
    {
        Directory.CreateDirectory(outputRoot);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string stage = Path.Combine(outputRoot, ".staging-" + stamp), target = Path.Combine(outputRoot, stamp);
        Directory.CreateDirectory(stage);
        try
        {
            // Copy and verify attachments before generating Excel. Video bytes stay on disk.
            var snapshot = prepare(stage);
            Xlsx.Write(snapshot, Path.Combine(stage, "report.xlsx"), options);
            var entries = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order().Select(path => new { path = Path.GetRelativePath(stage, path).Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = Files.HashFile(path) }).ToArray();
            var scope = new { mode = caseId == null ? "all" : "case", caseIds = snapshot.Cases.Select(c => c.Data.Id).ToArray() };
            File.WriteAllText(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(new { generator = "evikit-native/0.6.0-alpha", generatedAt = DateTimeOffset.Now, project = snapshot.Project.Name, scope, exportOptions = options, files = entries }, Contract.Json));
            // ZIP created outside the staged directory avoids accidentally including itself.
            string zip = Path.Combine(outputRoot, ".package-" + stamp + ".zip");
            try
            {
                using (var package = ZipFile.Open(zip, ZipArchiveMode.Create))
                    foreach (var path in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                        package.CreateEntryFromFile(path, Path.GetRelativePath(stage, path).Replace('\\', '/'), Media.IsVideo(path) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                File.Move(zip, Path.Combine(stage, "delivery.zip"));
            }
            finally { if (File.Exists(zip)) File.Delete(zip); }
            Directory.Move(stage, target); return target;
        }
        catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
    }
}
