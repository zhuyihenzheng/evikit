using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Evikit.Core;

public sealed class Project
{
    public string Name { get; set; } = "";
    public string Tester { get; set; } = "";
    public string Env { get; set; } = "";
    public List<string> Verdicts { get; set; } = ["OK", "NG", "保留", "対象外", "未実施"];
    public int ExcerptLines { get; set; } = 30;
    public int ImageMaxWidth { get; set; } = 640;
}
public sealed class TestCase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Precondition { get; set; } = "";
    public string Tester { get; set; } = "";
    public string Date { get; set; } = "";
    public string Env { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string Note { get; set; } = "";
    public List<Step> Steps { get; set; } = [];
    public List<Evidence> Evidence { get; set; } = [];
    public int? NextEvidenceNumber { get; set; }
}
public sealed class Step
{
    public int No { get; set; }
    public string Action { get; set; } = "";
    public string? Condition { get; set; }
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Verdict { get; set; } = "";
}
public sealed class Evidence
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "text";
    public string Category { get; set; } = "その他";
    public string Caption { get; set; } = "";
    public int? Step { get; set; }
    public string File { get; set; } = "";
    public string? OriginalFile { get; set; }
    public string? OriginalSha256 { get; set; }
    public Annotations? Annotations { get; set; }
    public string CapturedAt { get; set; } = "";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Lang { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long Size { get; set; }
}
public sealed class Annotations
{
    public Crop? Crop { get; set; }
    public List<Shape> Shapes { get; set; } = [];
}
public sealed class Crop
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}
public sealed class Shape
{
    public string Type { get; set; } = "rect";
    public double X { get; set; }
    public double Y { get; set; }
    public string Color { get; set; } = "red";
    public double? W { get; set; }
    public double? H { get; set; }
    public double? X2 { get; set; }
    public double? Y2 { get; set; }
    public int? N { get; set; }
    public string? Text { get; set; }
}
public static class Contract
{
    public static readonly string[] Categories = ["画面", "DB", "ログ", "API", "コマンド", "設定", "エラー", "その他"];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    public static void Id(string id)
    {
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$") || Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("ID は英数字・ハイフン・アンダースコア（80 文字以内）。Windows 予約名は使用できません。");
    }
    public static void FileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || Regex.IsMatch(name, "[\\\\/\\x00-\\x1f<>:\"|?*]") || name.EndsWith('.') || name.EndsWith(' '))
            throw new InvalidDataException("不正な証拠ファイル名です。");
    }
    public static void Validate(Project p)
    {
        p.Tester ??= ""; p.Env ??= ""; p.Verdicts ??= ["OK", "NG", "保留", "対象外", "未実施"];
        if (string.IsNullOrWhiteSpace(p.Name) || p.ExcerptLines is < 1 or > 1000 || p.ImageMaxWidth is < 100 or > 2000)
            throw new InvalidDataException("プロジェクト名・抜粋行数・画像幅を確認してください。");
    }
    public static void Validate(TestCase c)
    {
        Id(c.Id);
        c.Title ??= ""; c.Precondition ??= ""; c.Tester ??= ""; c.Date ??= ""; c.Env ??= ""; c.Verdict ??= ""; c.Note ??= "";
        if (c.Steps is null || c.Evidence is null) throw new InvalidDataException("steps / evidence は配列が必要です。");
        if (c.Steps.Any(s => s.No < 1) || c.Steps.Select(s => s.No).Distinct().Count() != c.Steps.Count || c.Evidence.Select(e => e.Id).Distinct().Count() != c.Evidence.Count)
            throw new InvalidDataException("ステップ番号・証拠 ID が重複または不正です。");
        foreach (var s in c.Steps) { s.Action ??= ""; s.Expected ??= ""; s.Actual ??= ""; s.Verdict ??= ""; }
        foreach (var e in c.Evidence)
        {
            FileName(e.File); if (e.OriginalFile != null) FileName(e.OriginalFile);
            e.Caption ??= ""; e.Source ??= ""; e.Note ??= ""; e.CapturedAt ??= ""; e.Sha256 ??= ""; e.Lang ??= ""; e.OriginalName ??= "";
            if (!Regex.IsMatch(e.Id, "^E[0-9]{1,8}$") || !new[] { "image", "table", "text", "file" }.Contains(e.Kind) || !Categories.Contains(e.Category) || (e.Step != null && !c.Steps.Any(s => s.No == e.Step)))
                throw new InvalidDataException($"{e.Id}: 証拠種別・分類・ステップ参照が不正です。");
            if (!new[] { "", "log", "json", "xml", "sql", "plain" }.Contains(e.Lang) || e.Size < 0 || (e.Sha256 != "" && !Regex.IsMatch(e.Sha256, "^[a-f0-9]{64}$")))
                throw new InvalidDataException($"{e.Id}: 証拠メタデータが不正です。");
            if (e.OriginalSha256 != null && !Regex.IsMatch(e.OriginalSha256, "^[a-f0-9]{64}$")) throw new InvalidDataException("原図ハッシュが不正です。");
            if (e.Annotations is { } a)
            {
                static bool Coord(double n) => double.IsFinite(n) && n is >= 0 and <= 100000;
                if (a.Crop is { } r && (!Coord(r.X) || !Coord(r.Y) || !Coord(r.W) || !Coord(r.H) || r.W == 0 || r.H == 0)) throw new InvalidDataException("切り抜き範囲が不正です。");
                if (a.Shapes.Count > 1000) throw new InvalidDataException("注釈は 1000 件以内です。");
                foreach (var s in a.Shapes)
                    if (!Coord(s.X) || !Coord(s.Y) || !new[] { "red", "blue", "yellow" }.Contains(s.Color) || !(s.Type switch { "rect" => s.W is { } w && Coord(w) && s.H is { } h && Coord(h), "arrow" => s.X2 is { } x && Coord(x) && s.Y2 is { } y && Coord(y), "number" => s.N is >= 1 and <= 999, "text" => s.Text != null && s.Text.Length <= 1000, _ => false })) throw new InvalidDataException("画像注釈が不正です。");
            }
        }
        if (c.NextEvidenceNumber is < 1) throw new InvalidDataException("証拠採番が不正です。");
    }
    public static string Verdict(TestCase c)
    {
        if (c.Verdict != "") return c.Verdict;
        var values = c.Steps.Select(s => string.IsNullOrWhiteSpace(s.Verdict) ? "未実施" : s.Verdict.Trim()).ToArray();
        foreach (var v in new[] { "NG", "保留", "未実施" }) if (values.Contains(v)) return v;
        return values.Length == 0 ? "未実施" : values.All(v => v == "対象外") ? "対象外" : "OK";
    }
}
