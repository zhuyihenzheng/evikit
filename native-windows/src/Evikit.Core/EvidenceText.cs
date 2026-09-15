using System.Text;
using System.Text.Json;

namespace Evikit.Core;

public sealed record EvidenceTextResult(string Kind, List<List<string>>? Rows, string Message);

// Inference is only for new evidence. Stored tables and case CSV imports keep the
// strict Tables.Parse contract; ambiguous text always has a manual type override.
public static class EvidenceText
{
    public static EvidenceTextResult Analyze(string input, string mode = "auto")
    {
        if (Encoding.UTF8.GetByteCount(input) > 2 * 1024 * 1024)
            throw new InvalidDataException("テキスト・表は 2 MiB 以下にしてください。");
        if (mode == "text") return new("text", null, "テキストとして保存します（手動指定）。");
        if (mode == "table") return Table(Tables.Parse(input));
        if (mode != "auto") throw new ArgumentException("Unknown evidence text mode.", nameof(mode));
        if (string.IsNullOrWhiteSpace(input)) return new("text", null, "列名付きの CSV / TSV を貼り付けると、下に表を表示します。");
        // Pretty-printed API responses with commas and indentation are not DB tables.
        try { using var json = JsonDocument.Parse(input.TrimStart('\uFEFF')); return Text(); }
        catch (JsonException) { }
        if (input.Contains('\t') || input.Contains(','))
        {
            try
            {
                var rows = Tables.Parse(input);
                if (rows.Count >= 2 && rows[0].Count >= 2) return Table(rows);
            }
            catch (InvalidDataException ex)
            { return new("text", null, "表として認識できません：" + ex.Message + " 原文は保持しています。種類が自動のままならテキストとして保存します。"); }
        }
        return Text();
    }
    private static EvidenceTextResult Text() => new("text", null, "テキストとして保存します。表の自動認識には列名 + データ行、2 列以上の CSV / TSV が必要です。1 列の表などは種類を table に指定してください。");
    private static EvidenceTextResult Table(List<List<string>> rows) => new("table", rows,
        $"表として保存します：{rows[0].Count} 列 / {rows.Count - 1:N0} 行。先頭行は列名。プレビューは先頭 500 行、保存・Excel 出力は全行です。");
}
