using System.Text.Json;

namespace Evikit.Core;

public static class ImageGroups
{
    public static string Key(ImageItem image) => image.OriginalFile ?? image.File;
    public static IReadOnlyList<ImageItem> Items(Evidence evidence) => evidence.Images ?? (IReadOnlyList<ImageItem>)[evidence];
    public static List<ImageItem> Promote(Evidence evidence)
    {
        if (evidence.Kind != "image") throw new InvalidDataException("画像のエビデンスを選択してください。");
        if (evidence.Images != null) return evidence.Images;
        evidence.Images = [Contract.Clone<ImageItem>(evidence)];
        evidence.File = ""; evidence.Sha256 = ""; evidence.Size = 0; evidence.OriginalName = "";
        evidence.OriginalFile = null; evidence.OriginalSha256 = null; evidence.Annotations = null;
        return evidence.Images;
    }
    public static ImageItem Select(Evidence evidence, string? key)
    {
        if (evidence.Kind != "image") throw new InvalidDataException("画像のエビデンスを選択してください。");
        if (evidence.Images == null && key == null) return evidence;
        return Items(evidence).SingleOrDefault(i => Key(i) == key) ?? throw new InvalidDataException("エビデンス内の画像を選択してください。");
    }
}

public sealed record GalleryEdit(string Key, string Caption, string Note);
public sealed record ArchivedImage(string CaseId, string EvidenceId, ImageItem Image, int Index);
public sealed record ImageSnapshot(ImageItem Metadata, byte[] Bytes);

public sealed partial class Workspace
{
    public CaseDocument AppendImage(CaseDocument doc, string id, NewEvidence input)
    {
        lock (gate)
        {
            CheckRevision(CasePath(doc.Data.Id), doc.Revision);
            if (input.Kind != "image" || input.SourcePath != null || input.Data.Length > Media.MaxInlineBytes)
                throw new InvalidDataException("追加する画像は 25 MiB 以下にしてください。");
            var (_, _, extension) = Xlsx.ImageSize(input.Data);
            var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
            var images = ImageGroups.Promote(e);
            var image = new ImageItem { File = $"image-{Guid.NewGuid():N}.{extension}", Caption = input.Caption,
                Note = input.Note, Source = input.Source, CapturedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                OriginalName = Path.GetFileName(input.OriginalName), Sha256 = Files.Hash(input.Data), Size = input.Data.Length };
            images.Add(image); Contract.Validate(c);
            Files.Atomic(Files.Safe(Root, "evidence", c.Id, image.File), input.Data, true);
            return SaveCase(new(c, doc.Revision));
        }
    }

    public CaseDocument MergeImages(CaseDocument doc, IReadOnlyList<string> ids)
    {
        var c = Contract.Clone(doc.Data);
        var selected = c.Evidence.Where(e => ids.Contains(e.Id)).ToArray();
        if (ids.Count < 2 || ids.Distinct().Count() != ids.Count || selected.Length != ids.Count || selected.Any(e => e.Kind != "image"))
            throw new InvalidDataException("まとめる画像エビデンスを 2 件以上選択してください。");
        if (selected.Select(e => e.Step).Distinct().Count() != 1)
            throw new InvalidDataException("同じ Step のエビデンスを選択してください。必要なら先に「情報編集」で Step を揃えてください。");
        var target = selected[0];
        var images = selected.SelectMany(e => ImageGroups.Items(e).Select(i =>
        {
            var image = Contract.Clone<ImageItem>(i);
            // Preserve the common description when absorbing an existing collection.
            if (e != target && e.Images != null)
            {
                image.Note = string.Join("\n", new[] { e.Caption, e.Note, image.Note }.Where(t => t != ""));
                image.Source = string.Join("\n", new[] { e.Source, image.Source }.Where(t => t != "").Distinct());
            }
            return image;
        })).ToList();
        ImageGroups.Promote(target); target.Images = images;
        c.NextEvidenceNumber = Math.Max(c.NextEvidenceNumber ?? 1, c.Evidence.Max(e => int.Parse(e.Id[1..])) + 1);
        c.Evidence.RemoveAll(e => e != target && ids.Contains(e.Id));
        return SaveCase(new(c, doc.Revision));
    }

    public CaseDocument SaveGallery(CaseDocument doc, string id, IReadOnlyList<GalleryEdit> edits)
    {
        var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
        var images = ImageGroups.Promote(e).ToDictionary(ImageGroups.Key);
        if (edits.Count != images.Count || edits.Select(i => i.Key).Distinct().Count() != edits.Count || edits.Any(i => !images.ContainsKey(i.Key)))
            throw new InvalidDataException("画像一覧が変わりました。再読込してください。");
        e.Images = edits.Select(edit => { var image = images[edit.Key]; image.Caption = edit.Caption; image.Note = edit.Note; return image; }).ToList();
        return SaveCase(new(c, doc.Revision));
    }

    public (CaseDocument Document, string ArchiveId) DeleteImage(CaseDocument doc, string id, string key)
    {
        lock (gate)
        {
            CheckRevision(CasePath(doc.Data.Id), doc.Revision);
            var c = Contract.Clone(doc.Data); var e = c.Evidence.Single(e => e.Id == id);
            var images = ImageGroups.Promote(e);
            if (images.Count <= 1) throw new InvalidOperationException("最後の 1 枚です。エビデンス全体を削除する場合はメイン画面の「削除」を使用してください。");
            var image = ImageGroups.Select(e, key); int index = images.IndexOf(image);
            string archive = Guid.NewGuid().ToString("N");
            Files.Atomic(Files.Safe(Root, ".trash", "native-" + archive, "image.json"),
                JsonSerializer.SerializeToUtf8Bytes(new ArchivedImage(c.Id, id, image, index), Contract.Json), true);
            images.RemoveAt(index);
            return (SaveCase(new(c, doc.Revision)), archive);
        }
    }

    public CaseDocument RestoreImage(string archive)
    {
        lock (gate)
        {
            if (!Guid.TryParseExact(archive, "N", out _)) throw new InvalidDataException("不正な復元 ID です。");
            var record = JsonSerializer.Deserialize<ArchivedImage>(File.ReadAllBytes(Files.Safe(Root, ".trash", "native-" + archive, "image.json")), Contract.Json)!;
            var doc = LoadCase(record.CaseId);
            var e = doc.Data.Evidence.SingleOrDefault(e => e.Id == record.EvidenceId) ?? throw new InvalidOperationException("元のエビデンスを先に復元してください。");
            var images = ImageGroups.Promote(e);
            if (doc.Data.Evidence.SelectMany(ImageGroups.Items).Any(i => ImageGroups.Key(i) == ImageGroups.Key(record.Image)))
                throw new InvalidOperationException("同じ画像が既に存在します。");
            VerifyEvidence(record.CaseId, record.Image);
            if (record.Image.OriginalFile != null) VerifyEvidence(record.CaseId, record.Image, true);
            images.Insert(Math.Clamp(record.Index, 0, images.Count), record.Image);
            return SaveCase(doc);
        }
    }
}
