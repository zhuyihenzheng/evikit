using System.Text;

namespace Evikit.Core;

public static class Inputs
{
    public static string Utf8(byte[] data)
    {
        if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("テキスト・表は 2 MiB 以下にしてください。");
        try { return new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new InvalidDataException("UTF-8 で保存されたファイルを選択してください。Shift-JIS は事前に変換してください。"); }
    }
    public static (string Kind, string Category, string Lang) Detect(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => ("image", "画面", ""),
        ".csv" or ".tsv" => ("table", "DB", ""),
        ".log" => ("text", "ログ", "log"),
        ".json" => ("text", "API", "json"),
        ".xml" => ("text", "API", "xml"),
        ".sql" => ("text", "DB", "sql"),
        ".txt" or ".yaml" or ".yml" or ".ini" or ".conf" => ("text", "設定", "plain"),
        _ => ("file", Media.IsVideo(file) ? "画面" : "その他", "")
    };
}
public static class Tables
{
    public static List<List<string>> Parse(string input)
    {
        input = input.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        if (Encoding.UTF8.GetByteCount(input) > 2 * 1024 * 1024) throw new InvalidDataException("表は 2 MiB 以下にしてください。");
        // Inspect only the first logical record; quoted tabs/commas do not select the delimiter.
        bool quoted = false; int tabs = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '"') { if (quoted && i + 1 < input.Length && input[i + 1] == '"') i++; else quoted = !quoted; }
            else if (!quoted && input[i] == '\n') break;
            else if (!quoted && input[i] == '\t') tabs++;
        }
        char delimiter = tabs > 0 ? '\t' : ',';
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); quoted = false; bool closed = false;
        void Cell() { row.Add(cell.ToString()); cell.Clear(); closed = false; if (row.Count > 256) throw new InvalidDataException("表は 256 列以内にしてください。"); }
        void Row() { Cell(); rows.Add(row); row = []; if (rows.Count > 10001) throw new InvalidDataException("表はヘッダー + 10,000 行以内にしてください。"); }
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < input.Length && input[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                else cell.Append(ch);
            }
            else if (ch == delimiter) Cell();
            else if (ch == '\n') Row();
            else if (ch == '"' && cell.Length == 0 && !closed) quoted = true;
            else if (closed || ch == '"') throw new InvalidDataException("CSV の引用符が不正です。");
            else cell.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV の引用符が閉じていません。");
        if (cell.Length > 0 || row.Count > 0 || closed) Row();
        if (rows.Count == 0 || rows.Any(r => r.Count != rows[0].Count)) throw new InvalidDataException("表の列数が一致していません。");
        return rows;
    }
    public static string ToCsv(List<List<string>> rows) => string.Join("\n", rows.Select(r => string.Join(",", r.Select(s => "\"" + s.Replace("\"", "\"\"") + "\"")))) + "\n";
    public static string[] Lines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return lines.Length > 1 && lines[^1] == "" ? lines[..^1] : lines;
    }
}
