using System.Text.Json;

namespace Evikit.Core;

public sealed record PendingCapture(string Token, string CaseId, int? Step, string Caption, string CapturedAt, byte[] Png)
{
    public string FileName => $"capture-{Token}.png";
}

// A durable inbox bridges the PNG + YAML commit. A retry uses the same token,
// so a crash after the YAML rename cannot append the same capture twice.
public sealed class CaptureInbox(Workspace workspace)
{
    private string DirectoryPath => Files.Safe(workspace.Root, ".capture-inbox");
    private string PathFor(string token) => Files.Safe(workspace.Root, ".capture-inbox", token + ".json");

    public static void Validate(PendingCapture capture)
    {
        Contract.Id(capture.CaseId);
        if (!Guid.TryParseExact(capture.Token, "N", out _) || capture.Step is < 1 || capture.Caption == null ||
            !DateTimeOffset.TryParse(capture.CapturedAt, out _)) throw new InvalidDataException("撮影情報が不正です。");
        if (capture.Png == null || capture.Png.Length > 25 * 1024 * 1024 ||
            !capture.Png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("スクリーンショットは 25 MiB 以下の PNG が必要です。");
        _ = Xlsx.ImageSize(capture.Png);
    }

    public void Stage(PendingCapture capture)
    {
        Validate(capture);
        string path = PathFor(capture.Token);
        if (File.Exists(path))
        {
            var existing = Read(path);
            if (existing.CaseId != capture.CaseId || existing.Step != capture.Step || existing.Caption != capture.Caption ||
                existing.CapturedAt != capture.CapturedAt || Files.Hash(existing.Png) != Files.Hash(capture.Png))
                throw new IOException("撮影の回収記録と内容が一致しません。");
            return;
        }
        Files.Atomic(path, JsonSerializer.SerializeToUtf8Bytes(capture, Contract.Json), true);
    }

    private static PendingCapture Read(string path)
    {
        if (new FileInfo(path).Length > 36 * 1024 * 1024) throw new InvalidDataException("撮影の回収記録が大きすぎます。");
        var capture = JsonSerializer.Deserialize<PendingCapture>(File.ReadAllBytes(path), Contract.Json)
            ?? throw new InvalidDataException("撮影の回収記録が空です。");
        Validate(capture);
        if (Path.GetFileNameWithoutExtension(path) != capture.Token) throw new InvalidDataException("撮影の回収 ID が一致しません。");
        return capture;
    }

    public IReadOnlyList<string> PendingTokens(string caseId)
    {
        Contract.Id(caseId);
        if (!Directory.Exists(DirectoryPath)) return [];
        // Return tokens rather than retaining every full-resolution image in memory.
        return Directory.EnumerateFiles(DirectoryPath, "*.json").Order(StringComparer.Ordinal)
            .Select(path => Read(Files.Safe(workspace.Root, ".capture-inbox", Path.GetFileName(path))))
            .Where(c => c.CaseId == caseId).Select(c => (c.Token, c.CapturedAt))
            .OrderBy(c => c.CapturedAt).Select(c => c.Token).ToArray();
    }

    public PendingCapture Load(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) throw new InvalidDataException("不正な撮影 ID です。");
        return Read(PathFor(token));
    }

    public CaseDocument Commit(CaseDocument document, PendingCapture capture)
    {
        Stage(capture);
        var saved = workspace.AddCapturedImage(document, capture);
        // Failed cleanup is harmless: the next recovery recognizes the committed file.
        try { File.Delete(PathFor(capture.Token)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return saved;
    }
}

public sealed record ImageReviewEdit(string Id, string Caption, int? Step, string Note);
