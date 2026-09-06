using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Evikit.Core;

public sealed record CaseDocument(TestCase Data, string Revision);
public sealed record ProjectDocument(Project Data, string Revision);
public sealed record NewEvidence(string Kind, string Category, string Caption, int? Step, string Source, string Note, string Lang, byte[] Data, string Extension, string OriginalName = "");
public sealed record ArchivedEvidence(string CaseId, Evidence Evidence);
public sealed record EvidenceSnapshot(Evidence Metadata, byte[] Bytes);
public sealed record CaseSnapshot(TestCase Data, List<EvidenceSnapshot> Evidence);
public sealed record ProjectSnapshot(Project Project, List<CaseSnapshot> Cases);

public static class Files
{
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Safe(string root, params string[] parts)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("プロジェクト外へのアクセスはできません。");
        var check = path;
        while (check != root)
        {
            if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("プロジェクト内のリンク・ジャンクションは使用できません。");
            check = Path.GetDirectoryName(check) ?? throw new IOException("不正なパスです。");
        }
        return path;
    }
    public static void Atomic(string path, byte[] bytes, bool createOnly = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, path, !createOnly);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class Yaml
{
    private static readonly IDeserializer Reader = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking().Build();
    public static T Read<T>(byte[] bytes)
    {
        var input = new YamlStream(); input.Load(new StringReader(Encoding.UTF8.GetString(bytes)));
        if (input.Documents.Count != 1 || input.Documents[0].RootNode is not YamlMappingNode root) throw new InvalidDataException("単一の YAML オブジェクトが必要です。");
        static void DefaultNull(YamlMappingNode mapping, string key, string value)
        {
            var node = new YamlScalarNode(key);
            if (mapping.Children.TryGetValue(node, out var item) && item is YamlScalarNode scalar && scalar.Style == ScalarStyle.Plain && (string.IsNullOrEmpty(scalar.Value) || scalar.Value == "~" || scalar.Value.Equals("null", StringComparison.OrdinalIgnoreCase)))
                mapping.Children[node] = new YamlScalarNode(value);
        }
        // Match the browser contract's nullish defaults, without accepting invalid zero values.
        if (typeof(T) == typeof(Project)) { DefaultNull(root, "excerptLines", "30"); DefaultNull(root, "imageMaxWidth", "640"); }
        if (typeof(T) == typeof(TestCase) && root.Children.TryGetValue(new YamlScalarNode("evidence"), out var evidence) && evidence is YamlSequenceNode entries)
            foreach (var entry in entries.Children.OfType<YamlMappingNode>()) DefaultNull(entry, "size", "0");
        using var writer = new StringWriter(); input.Save(writer, false);
        return Reader.Deserialize<T>(writer.ToString()) ?? throw new InvalidDataException("YAML が空です。");
    }
    // Quote every string, including dates, numeric-looking IDs and the empty string.
    // JSON is used only as an in-memory typed tree, never as a replacement for project YAML.
    public static byte[] Write<T>(T value)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, Contract.Json));
        static YamlNode Node(JsonElement item) => item.ValueKind switch
        {
            JsonValueKind.Object => new YamlMappingNode(item.EnumerateObject().Select(p => new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(p.Name), Node(p.Value)))),
            JsonValueKind.Array => new YamlSequenceNode(item.EnumerateArray().Select(Node)),
            JsonValueKind.String => new YamlScalarNode(item.GetString()) { Style = ScalarStyle.DoubleQuoted },
            _ => new YamlScalarNode(item.GetRawText()) { Style = ScalarStyle.Plain }
        };
        var stream = new YamlStream(new YamlDocument(Node(json.RootElement)));
        using var writer = new StringWriter(); stream.Save(writer, false);
        return Encoding.UTF8.GetBytes(writer.ToString());
    }
}

public sealed class Workspace : IDisposable
{
    public string Root { get; }
    private readonly string lockPath;
    private readonly string owner = Environment.ProcessId.ToString();
    private FileStream? heldLock;
    private readonly object gate = new();
    public Workspace(string root)
    {
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException(Root);
        lockPath = Files.Safe(Root, ".evikit.lock");
        if (File.Exists(lockPath))
        {
            // Shared lock format with browser edition. Never guess that a live PID is stale.
            var pidText = File.ReadAllText(lockPath);
            if (!int.TryParse(pidText, out var pid) || pid <= 0) throw new IOException("ロック情報が不正です。プロジェクトの使用状況を確認してください。");
            bool alive;
            try { using var process = Process.GetProcessById(pid); alive = !process.HasExited; }
            catch (ArgumentException) { alive = false; }
            if (alive) throw new IOException("このプロジェクトは使用中です。ブラウザ版または別のデスクトップ版を終了してください。");
            File.Delete(lockPath);
        }
        try
        {
            heldLock = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            heldLock.Write(Encoding.UTF8.GetBytes(owner)); heldLock.Flush(true);
            _ = LoadProject(); _ = ListCases();
        }
        catch { Dispose(); throw; }
    }
    public static void Create(string root, string name)
    {
        var project = new Project { Name = name }; Contract.Validate(project);
        Directory.CreateDirectory(root);
        Files.Atomic(Files.Safe(root, "project.yaml"), Yaml.Write(project), true);
        Directory.CreateDirectory(Files.Safe(root, "cases")); Directory.CreateDirectory(Files.Safe(root, "evidence"));
        var ignore = Files.Safe(root, ".gitignore");
        var old = File.Exists(ignore) ? File.ReadAllText(ignore) : "";
        Files.Atomic(ignore, Encoding.UTF8.GetBytes(old.TrimEnd() + "\n.evikit.lock\nexports/\n*.tmp-*\n"));
    }
    public ProjectDocument LoadProject()
    {
        var bytes = File.ReadAllBytes(Files.Safe(Root, "project.yaml"));
        var project = Yaml.Read<Project>(bytes); Contract.Validate(project);
        return new(project, Files.Hash(bytes));
    }
    public void SaveProject(ProjectDocument doc)
    {
        lock (gate) { Contract.Validate(doc.Data); var path = Files.Safe(Root, "project.yaml"); CheckRevision(path, doc.Revision); Files.Atomic(path, Yaml.Write(doc.Data)); }
    }
    public List<CaseDocument> ListCases()
    {
        var dir = Files.Safe(Root, "cases");
        if (!Directory.Exists(dir)) return [];
        var ids = Directory.EnumerateFiles(dir).Where(p => Path.GetExtension(p) is ".yaml" or ".yml").Select(Path.GetFileNameWithoutExtension).Cast<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length) throw new InvalidDataException("用例 ID / .yaml / .yml が重複しています。");
        return ids.Select(LoadCase).ToList();
    }
    private string CasePath(string id) { Contract.Id(id); var yaml = Files.Safe(Root, "cases", id + ".yaml"); return File.Exists(yaml) ? yaml : Files.Safe(Root, "cases", id + ".yml"); }
    public CaseDocument LoadCase(string id)
    {
        var bytes = File.ReadAllBytes(CasePath(id)); var c = Yaml.Read<TestCase>(bytes); Contract.Validate(c);
        if (c.Id != id) throw new InvalidDataException("ファイル名と用例 ID が一致しません。");
        return new(c, Files.Hash(bytes));
    }
    public CaseDocument CreateCase(string id, string title)
    {
        lock (gate)
        {
            Contract.Id(id);
            if (ListCases().Any(c => string.Equals(c.Data.Id, id, StringComparison.OrdinalIgnoreCase))) throw new IOException("用例 ID が既に存在します。");
            var p = LoadProject().Data;
            var c = new TestCase { Id = id, Title = title, Tester = p.Tester, Env = p.Env, Date = DateTime.Now.ToString("yyyy-MM-dd") };
            Files.Atomic(Files.Safe(Root, "cases", id + ".yaml"), Yaml.Write(c), true); return LoadCase(id);
        }
    }
    private static void CheckRevision(string path, string revision)
    {
        if (!File.Exists(path) || Files.Hash(File.ReadAllBytes(path)) != revision) throw new IOException("外部でファイルが更新されました。草稿を控えてから再読込してください（上書きしていません）。");
    }
    public CaseDocument SaveCase(CaseDocument doc)
    {
        lock (gate)
        {
            Contract.Validate(doc.Data); var path = CasePath(doc.Data.Id); CheckRevision(path, doc.Revision);
            Files.Atomic(path, Yaml.Write(doc.Data)); return LoadCase(doc.Data.Id);
        }
    }
    public byte[] ReadEvidence(string caseId, Evidence e, bool original = false)
    {
        Contract.Id(caseId); var file = original ? e.OriginalFile ?? e.File : e.File; Contract.FileName(file);
        var bytes = File.ReadAllBytes(Files.Safe(Root, "evidence", caseId, file));
        var expected = original ? e.OriginalSha256 ?? e.Sha256 : e.Sha256;
        if (!string.IsNullOrEmpty(expected) && Files.Hash(bytes) != expected) throw new InvalidDataException($"{caseId}/{file}: SHA-256 が一致しません。証拠が変更されています。");
        return bytes;
    }
    public CaseDocument AddEvidence(CaseDocument doc, NewEvidence input)
    {
        lock (gate)
        {
            CheckRevision(CasePath(doc.Data.Id), doc.Revision);
            if (input.Data.Length > 25 * 1024 * 1024) throw new InvalidDataException("証拠は 25 MiB 以下にしてください。");
            var c = Contract.Clone(doc.Data);
            var next = Math.Max(c.NextEvidenceNumber ?? 1, c.Evidence.Select(e => int.Parse(e.Id[1..])).DefaultIfEmpty(0).Max() + 1);
            var id = $"E{next:00}";
            if (!System.Text.RegularExpressions.Regex.IsMatch(input.Extension, "^[A-Za-z0-9]{1,10}$")) throw new InvalidDataException("不正な拡張子です。");
            var file = id + "." + input.Extension.ToLowerInvariant();
            var bytes = input.Kind == "table" ? Encoding.UTF8.GetBytes(Tables.ToCsv(Tables.Parse(Inputs.Utf8(input.Data)))) : input.Kind == "text" ? Encoding.UTF8.GetBytes(Inputs.Utf8(input.Data).Replace("\r\n", "\n").Replace('\r', '\n')) : input.Data;
            c.Evidence.Add(new Evidence { Id = id, Kind = input.Kind, Category = input.Category, Caption = input.Caption, Step = input.Step, Source = input.Source, Note = input.Note, Lang = input.Lang, File = file, CapturedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"), OriginalName = Path.GetFileName(input.OriginalName), Size = bytes.Length, Sha256 = Files.Hash(bytes) });
            c.NextEvidenceNumber = next + 1; Contract.Validate(c);
            Files.Atomic(Files.Safe(Root, "evidence", c.Id, file), bytes, true);
            return SaveCase(new(c, doc.Revision));
        }
    }
    public (CaseDocument Document, string ArchiveId) DeleteEvidence(CaseDocument doc, string id)
    {
        lock (gate)
        {
            CheckRevision(CasePath(doc.Data.Id), doc.Revision);
            var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
            c.NextEvidenceNumber = Math.Max(c.NextEvidenceNumber ?? 1, c.Evidence.Max(e => int.Parse(e.Id[1..])) + 1);
            var archive = Guid.NewGuid().ToString("N");
            Files.Atomic(Files.Safe(Root, ".trash", "native-" + archive, "evidence.json"), JsonSerializer.SerializeToUtf8Bytes(new ArchivedEvidence(c.Id, e), Contract.Json), true);
            c.Evidence.Remove(e); return (SaveCase(new(c, doc.Revision)), archive);
        }
    }
    public CaseDocument RestoreEvidence(string archive)
    {
        lock (gate)
        {
            if (!Guid.TryParseExact(archive, "N", out _)) throw new InvalidDataException("不正な復元 ID です。");
            var saved = JsonSerializer.Deserialize<ArchivedEvidence>(File.ReadAllBytes(Files.Safe(Root, ".trash", "native-" + archive, "evidence.json")), Contract.Json)!;
            var doc = LoadCase(saved.CaseId); var e = saved.Evidence;
            if (doc.Data.Evidence.Any(item => item.Id == e.Id)) throw new IOException("同じ証拠 ID が既に存在します。");
            _ = ReadEvidence(saved.CaseId, e);
            if (e.Step != null && !doc.Data.Steps.Any(s => s.No == e.Step)) e.Step = null;
            doc.Data.Evidence.Add(e); return SaveCase(doc);
        }
    }
    public CaseDocument SaveAnnotation(CaseDocument doc, string id, byte[] png, Annotations annotation)
    {
        lock (gate)
        {
            CheckRevision(CasePath(doc.Data.Id), doc.Revision);
            if (png.Length > 25 * 1024 * 1024 || !png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("注釈画像は 25 MiB 以下の PNG が必要です。");
            var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
            if (e.Kind != "image") throw new InvalidDataException("画像以外には注釈を付けられません。");
            var original = ReadEvidence(c.Id, e, true);
            e.OriginalFile ??= e.File; e.OriginalSha256 ??= Files.Hash(original);
            e.File = $"{id}.annotated-{Guid.NewGuid():N}.png"; e.Sha256 = Files.Hash(png); e.Size = png.Length; e.Annotations = annotation;
            Contract.Validate(c); Files.Atomic(Files.Safe(Root, "evidence", c.Id, e.File), png, true);
            return SaveCase(new(c, doc.Revision));
        }
    }
    public CaseDocument ResetAnnotation(CaseDocument doc, string id)
    {
        var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
        var original = ReadEvidence(c.Id, e, true); e.File = e.OriginalFile ?? e.File; e.Sha256 = Files.Hash(original); e.Size = original.Length;
        e.OriginalFile = null; e.OriginalSha256 = null; e.Annotations = null; return SaveCase(new(c, doc.Revision));
    }
    public ProjectSnapshot Snapshot()
    {
        lock (gate)
        {
            var project = LoadProject(); var cases = ListCases();
            var result = new ProjectSnapshot(project.Data, cases.Select(c => new CaseSnapshot(c.Data, c.Data.Evidence.Select(e => { if (e.OriginalFile != null) _ = ReadEvidence(c.Data.Id, e, true); return new EvidenceSnapshot(e, ReadEvidence(c.Data.Id, e)); }).ToList())).ToList());
            CheckRevision(Files.Safe(Root, "project.yaml"), project.Revision);
            foreach (var c in cases) CheckRevision(CasePath(c.Data.Id), c.Revision);
            return result;
        }
    }
    public void Dispose()
    {
        if (heldLock == null) return;
        heldLock.Dispose(); heldLock = null;
        if (File.Exists(lockPath) && File.ReadAllText(lockPath) == owner) File.Delete(lockPath);
    }
}
