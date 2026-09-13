using System.Text.Json;
using System.Text.RegularExpressions;

namespace Evikit.Core;

public sealed record DeletedCase(string ArchiveId, string CaseId, string Title, string DeletedAt, int StepCount, int EvidenceCount, int ImageCount);

public sealed partial class Workspace
{
    private sealed record CaseArchiveRecord(string CaseId, string Extension, string DeletedAt, string Revision);
    private string CaseArchivePath(string archive, string file)
    {
        if (!Guid.TryParseExact(archive, "N", out _)) throw new InvalidDataException("不正な用例の復元 ID です。");
        return Files.Safe(Root, ".trash", "native-case-" + archive, file);
    }
    private CaseArchiveRecord ReadCaseArchiveRecord(string archive)
    {
        var record = JsonSerializer.Deserialize<CaseArchiveRecord>(File.ReadAllBytes(CaseArchivePath(archive, "record.json")), Contract.Json)
            ?? throw new InvalidDataException("用例の復元情報が空です。");
        Contract.Id(record.CaseId);
        if (record.Extension is not ".yaml" and not ".yml" || !DateTimeOffset.TryParse(record.DeletedAt, out _) || !Regex.IsMatch(record.Revision, "^[a-f0-9]{64}$"))
            throw new InvalidDataException("用例の復元情報が不正です。");
        return record;
    }
    private IEnumerable<string> CaseArchiveIds()
    {
        string trash = Files.Safe(Root, ".trash");
        if (!Directory.Exists(trash)) return [];
        return Directory.EnumerateDirectories(trash, "native-case-*").Select(Path.GetFileName).Cast<string>()
            .Select(name => name["native-case-".Length..]).ToArray();
    }
    private (CaseArchiveRecord Record, TestCase Data) ReadArchivedCase(string archive)
    {
        var record = ReadCaseArchiveRecord(archive);
        var bytes = File.ReadAllBytes(CaseArchivePath(archive, "case.yaml"));
        if (Files.Hash(bytes) != record.Revision) throw new InvalidDataException("削除した用例の内容が変更されています。元のデータを確認してください。");
        var data = Yaml.Read<TestCase>(bytes); Contract.Validate(data);
        if (data.Id != record.CaseId) throw new InvalidDataException("復元する用例 ID が一致しません。");
        return (record, data);
    }

    // Reserve IDs even for empty deleted cases; their pending captures and evidence
    // deletion journals must never become associated with a newly created case.
    public IReadOnlySet<string> UnavailableCaseIds()
    {
        lock (gate)
        {
            var ids = ListCases().Select(c => c.Data.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string evidence = Files.Safe(Root, "evidence");
            if (Directory.Exists(evidence))
                foreach (string path in Directory.EnumerateDirectories(evidence)) ids.Add(Path.GetFileName(path));
            foreach (string archive in CaseArchiveIds())
                if (File.Exists(CaseArchivePath(archive, "record.json"))) ids.Add(ReadCaseArchiveRecord(archive).CaseId);
            return ids;
        }
    }

    public string DeleteCase(CaseDocument doc)
    {
        lock (gate)
        {
            string source = CasePath(doc.Data.Id); CheckRevision(source, doc.Revision);
            string archive = Guid.NewGuid().ToString("N");
            var record = new CaseArchiveRecord(doc.Data.Id, Path.GetExtension(source), DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"), doc.Revision);
            Files.Atomic(CaseArchivePath(archive, "record.json"), JsonSerializer.SerializeToUtf8Bytes(record, Contract.Json), true);
            CheckRevision(source, doc.Revision);
            // A single same-filesystem rename is the commit point. Before it the case
            // is active; after it the durable trash list can restore the exact YAML.
            // Large attachments and recovery journals remain in place, with the ID reserved.
            File.Move(source, CaseArchivePath(archive, "case.yaml"));
            return archive;
        }
    }

    public IReadOnlyList<DeletedCase> ListDeletedCases()
    {
        lock (gate)
        {
            var result = new List<DeletedCase>();
            foreach (string archive in CaseArchiveIds())
            {
                // A record without case.yaml is an interrupted delete or a completed restore.
                if (!File.Exists(CaseArchivePath(archive, "case.yaml"))) continue;
                var (record, data) = ReadArchivedCase(archive);
                result.Add(new(archive, data.Id, data.Title, record.DeletedAt, data.Steps.Count, data.Evidence.Count, data.Evidence.Sum(e => e.ImageCount)));
            }
            return result.OrderByDescending(c => DateTimeOffset.Parse(c.DeletedAt)).ToArray();
        }
    }

    public CaseDocument RestoreCase(string archive)
    {
        lock (gate)
        {
            var (record, data) = ReadArchivedCase(archive);
            if (ListCases().Any(c => c.Data.Id.Equals(data.Id, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("同じ ID の用例があるため復元できません。現在の用例は上書きしていません。");
            foreach (var item in data.Evidence.SelectMany(ImageGroups.Items))
            {
                VerifyEvidence(data.Id, item);
                if (item.OriginalFile != null) VerifyEvidence(data.Id, item, true);
            }
            string source = CaseArchivePath(archive, "case.yaml"); CheckRevision(source, record.Revision);
            File.Move(source, Files.Safe(Root, "cases", data.Id + record.Extension));
            return LoadCase(data.Id);
        }
    }
}
