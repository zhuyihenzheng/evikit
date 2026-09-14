using System.Text.Json;
using System.Text.Json.Serialization;

namespace Evikit.Core;

// Output preferences never change the stored case/evidence data.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExportOptions
{
    public bool Images { get; init; } = true;
    public bool Videos { get; init; } = true;
    public bool Files { get; init; } = true;
    public bool Dates { get; init; } = true;
    public bool Tester { get; init; } = true;
    public bool Environment { get; init; } = true;
    public bool Conditions { get; init; } = true;
    public bool Notes { get; init; } = true;
    public bool Sources { get; init; } = true;

    internal bool Includes(Evidence evidence) => evidence.Kind switch
    {
        "image" => Images,
        "file" => Media.IsVideo(evidence.File) ? Videos : Files,
        _ => true
    };

    internal ProjectSnapshot Select(ProjectSnapshot snapshot, string? caseId = null)
    {
        var cases = snapshot.Cases;
        if (caseId != null)
        {
            Contract.Id(caseId);
            cases = cases.Where(c => c.Data.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (cases.Count != 1 || cases[0].Data.Id != caseId)
                throw new InvalidDataException("出力する用例を特定できません。用例を選び直してください。");
        }
        return new(snapshot.Project, cases.Select(c =>
        {
            var data = Contract.Clone(c.Data);
            data.Evidence = data.Evidence.Where(Includes).ToList();
            return new CaseSnapshot(data, c.Evidence.Where(e => Includes(e.Metadata)).ToList());
        }).ToList());
    }
}

public sealed partial class Workspace
{
    public ExportOptions LoadExportOptions()
    {
        lock (gate)
        {
            string path = Files.Safe(Root, ".evikit", "export-options.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ExportOptions>(File.ReadAllBytes(path), Contract.Json)
                    ?? throw new InvalidDataException("出力設定が空です。設定ファイルを確認してください。")
                : new();
        }
    }

    public void SaveExportOptions(ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (gate) Files.Atomic(Files.Safe(Root, ".evikit", "export-options.json"), JsonSerializer.SerializeToUtf8Bytes(options, Contract.Json));
    }
}
