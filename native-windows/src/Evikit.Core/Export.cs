using System.IO.Compression;
using System.Text.Json;

namespace Evikit.Core;

public static class Export
{
    public static string Deliver(ProjectSnapshot snapshot, string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string stage = Path.Combine(outputRoot, ".staging-" + stamp), target = Path.Combine(outputRoot, stamp);
        Directory.CreateDirectory(stage);
        try
        {
            // All output consumes this in-memory snapshot, never live evidence files.
            Xlsx.Write(snapshot, Path.Combine(stage, "report.xlsx"));
            foreach (var c in snapshot.Cases)
                foreach (var e in c.Evidence)
                {
                    var path = Files.Safe(stage, "files", c.Data.Id, e.Metadata.File);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, e.Bytes);
                }
            var entries = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order().Select(path => new { path = Path.GetRelativePath(stage, path).Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = Files.Hash(File.ReadAllBytes(path)) }).ToArray();
            File.WriteAllText(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(new { generator = "evikit-native/0.1.0-alpha", generatedAt = DateTimeOffset.Now, project = snapshot.Project.Name, files = entries }, Contract.Json));
            // ZIP created outside the staged directory avoids accidentally including itself.
            string zip = Path.Combine(outputRoot, ".package-" + stamp + ".zip");
            try { ZipFile.CreateFromDirectory(stage, zip, CompressionLevel.Optimal, false); File.Move(zip, Path.Combine(stage, "delivery.zip")); }
            finally { if (File.Exists(zip)) File.Delete(zip); }
            Directory.Move(stage, target); return target;
        }
        catch { if (Directory.Exists(stage)) Directory.Delete(stage, true); throw; }
    }
}
