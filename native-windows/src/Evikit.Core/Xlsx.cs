using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Evikit.Core;

// A small, explicit OOXML writer: all values are inline strings (never formulas).
// Uses only .NET ZIP/XML APIs; Excel is not required on the editing machine.
public static class Xlsx
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace D = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private sealed record Pic(int Row, byte[] Data, string Extension, int Width, int Height);
    private sealed class Sheet(string name)
    {
        public string Name { get; } = name;
        public List<XElement> Rows { get; } = [];
        public List<string> Merges { get; } = [];
        public List<(string Cell, string Target, bool Internal)> Links { get; } = [];
        public List<Pic> Pictures { get; } = [];
        public int Row(params string[] cells) => Add(30, 0, cells);
        public int Add(double height, int style, params string[] cells)
        {
            int n = Rows.Count + 1;
            if (n > 1048576 || cells.Length > 16384) throw new InvalidDataException("Excel の最大行列数を超えました。");
            var row = new XElement(S + "row", new XAttribute("r", n), new XAttribute("ht", Math.Min(409, height).ToString(CultureInfo.InvariantCulture)), new XAttribute("customHeight", 1));
            for (int i = 0; i < cells.Length; i++)
            {
                string value = cells[i] ?? "";
                if (value.Length > 32767) throw new InvalidDataException($"{Name}: Excel のセル上限（32,767 文字）を超えました。証拠を分割してください。");
                // XML 1.0 cannot represent these control characters; stop rather than silently change evidence.
                if (value.Any(ch => ch < 32 && ch is not '\n' and not '\r' and not '\t')) throw new InvalidDataException("Excel に保存できない制御文字が含まれています。");
                row.Add(new XElement(S + "c", new XAttribute("r", Col(i + 1) + n), new XAttribute("s", style), new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))));
            }
            Rows.Add(row); return n;
        }
        public void Full(string text, int style = 0)
        {
            int n = Add(Math.Max(28, Math.Ceiling((text.Length / 110.0) + text.Count(c => c == '\n')) * 16 + 12), style, text);
            Merges.Add($"A{n}:F{n}");
        }
    }
    private static string Col(int n) { string s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }
    private static int VerdictStyle(string v) => v switch { "OK" => 3, "NG" => 4, "保留" => 5, _ => 0 };
    public static void Write(ProjectSnapshot snapshot, string output)
    {
        var summary = new Sheet("サマリ"); summary.Full(snapshot.Project.Name, 1); summary.Full($"担当：{snapshot.Project.Tester}　環境：{snapshot.Project.Env}");
        summary.Add(28, 2, "No.", "用例", "タイトル", "担当", "判定", "日付");
        var sheets = new List<Sheet> { summary }; var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { summary.Name };
        foreach (var item in snapshot.Cases)
        {
            var c = item.Data;
            string baseName = c.Id[..Math.Min(c.Id.Length, 31)], name = baseName; int suffix = 1;
            while (!used.Add(name)) { string tail = "-" + suffix++; name = baseName[..Math.Min(baseName.Length, 31 - tail.Length)] + tail; }
            var sheet = new Sheet(name); sheets.Add(sheet);
            int sumRow = summary.Row((sheets.Count - 1).ToString(), c.Id, c.Title, c.Tester, Contract.Verdict(c), c.Date);
            summary.Rows[^1].Elements(S + "c").ElementAt(4).SetAttributeValue("s", VerdictStyle(Contract.Verdict(c)));
            summary.Links.Add(($"B{sumRow}", $"'{name}'!A1", true));
            sheet.Full($"{c.Id}　{c.Title}", 1);
            sheet.Full($"判定：{Contract.Verdict(c)}　担当：{c.Tester}　日付：{c.Date}　環境：{c.Env}");
            sheet.Full("前提条件：" + c.Precondition);
            int back = sheet.Row("サマリへ"); sheet.Links.Add(($"A{back}", "'サマリ'!A1", true));
            sheet.Add(28, 2, "No.", "操作", "期待結果", "実際結果", "判定", "証拠");
            foreach (var step in c.Steps)
            {
                double height = Math.Max(42, new[] { step.Action, step.Expected, step.Actual }.Max(t => Math.Ceiling(t.Length / 20.0) + t.Count(ch => ch == '\n')) * 15 + 12);
                sheet.Add(height, 0, step.No.ToString(), step.Action, step.Expected, step.Actual, step.Verdict, string.Join(", ", c.Evidence.Where(e => e.Step == step.No).Select(e => e.Id)));
                sheet.Rows[^1].Elements(S + "c").ElementAt(4).SetAttributeValue("s", VerdictStyle(step.Verdict));
            }
            foreach (var evidence in item.Evidence)
            {
                var e = evidence.Metadata;
                sheet.Full($"{e.Id}  [{e.Category}] {e.Caption}　{(e.Step == null ? "共通" : "ステップ " + e.Step)}", 2);
                sheet.Full("取得：" + e.CapturedAt); if (e.Source != "") sheet.Full("出典 / SQL：" + e.Source);
                if (e.Kind == "image")
                {
                    var (width, height, extension) = ImageSize(evidence.Bytes);
                    double scale = Math.Min(1, Math.Min(snapshot.Project.ImageMaxWidth / (double)width, 1600d / height));
                    int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
                    sheet.Pictures.Add(new(sheet.Rows.Count, evidence.Bytes, extension, w, h));
                    int remaining = h + 12;
                    while (remaining > 0) { int pixels = Math.Min(remaining, 400); sheet.Add(pixels * .75, 0, ""); remaining -= pixels; }
                }
                else if (e.Kind == "table")
                {
                    var table = Tables.Parse(Inputs.Utf8(evidence.Bytes));
                    int index = 0;
                    foreach (var row in table) sheet.Add(Math.Max(28, row.Max(t => Math.Ceiling(t.Length / 24.0) + t.Count(ch => ch == '\n')) * 15 + 12), index++ == 0 ? 2 : 0, row.ToArray());
                }
                else if (e.Kind == "text")
                {
                    var lines = Tables.Lines(Inputs.Utf8(evidence.Bytes));
                    for (int i = 0; i < Math.Min(lines.Length, snapshot.Project.ExcerptLines); i++) sheet.Full($"{i + 1,4}  {lines[i]}");
                    if (lines.Length > snapshot.Project.ExcerptLines) sheet.Full($"… 全 {lines.Length} 行。全文は添付ファイルを参照。");
                }
                if (e.Note != "") sheet.Full("確認事項：" + e.Note);
                int linkRow = sheet.Row("", "証拠ファイル：" + (e.OriginalName == "" ? e.File : e.OriginalName));
                sheet.Links.Add(($"B{linkRow}", "files/" + Uri.EscapeDataString(c.Id) + "/" + Uri.EscapeDataString(e.File), false));
            }
            sheet.Full("備考：" + c.Note);
        }
        using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
        void Xml(string path, XElement root) { var entry = archive.CreateEntry(path); using var stream = entry.Open(); new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(stream); }
        void Bytes(string path, byte[] data) { var entry = archive.CreateEntry(path); using var stream = entry.Open(); stream.Write(data); }
        var contentTypes = new XElement(XName.Get("Types", "http://schemas.openxmlformats.org/package/2006/content-types"));
        void Type(string path, string type) => contentTypes.Add(new XElement(contentTypes.Name.Namespace + "Override", new XAttribute("PartName", "/" + path), new XAttribute("ContentType", type)));
        contentTypes.Add(new XElement(contentTypes.Name.Namespace + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")));
        Type("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Type("xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        Xml("_rels/.rels", new XElement(P + "Relationships", Rel("rId1", "officeDocument", "xl/workbook.xml")));
        Xml("xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets", sheets.Select((s, i) => new XElement(S + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(R + "id", "rId" + (i + 1)))))));
        Xml("xl/_rels/workbook.xml.rels", new XElement(P + "Relationships", sheets.Select((_, i) => Rel("rId" + (i + 1), "worksheet", $"worksheets/sheet{i + 1}.xml")), Rel("styles", "styles", "styles.xml")));
        Xml("xl/styles.xml", Styles());
        for (int i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i]; string sheetPath = $"xl/worksheets/sheet{i + 1}.xml";
            Type(sheetPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            var rels = new XElement(P + "Relationships");
            var root = new XElement(S + "worksheet", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheetPr", new XElement(S + "pageSetUpPr", new XAttribute("fitToPage", 1))),
                new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XElement(S + "pane", new XAttribute("ySplit", i == 0 ? 3 : 5), new XAttribute("topLeftCell", i == 0 ? "A4" : "A6"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(S + "cols", new[] { 6, 36, 32, 32, 8, 14 }.Select((w, n) => new XElement(S + "col", new XAttribute("min", n + 1), new XAttribute("max", n + 1), new XAttribute("width", w), new XAttribute("customWidth", 1)))), new XElement(S + "sheetData", sheet.Rows));
            if (sheet.Merges.Count > 0) root.Add(new XElement(S + "mergeCells", new XAttribute("count", sheet.Merges.Count), sheet.Merges.Select(m => new XElement(S + "mergeCell", new XAttribute("ref", m)))));
            if (sheet.Links.Count > 0)
            {
                var links = new XElement(S + "hyperlinks"); int n = 0;
                foreach (var link in sheet.Links)
                {
                    string id = "link" + ++n;
                    links.Add(new XElement(S + "hyperlink", new XAttribute("ref", link.Cell), link.Internal ? new XAttribute("location", link.Target) : new XAttribute(R + "id", id)));
                    if (!link.Internal) { var rel = Rel(id, "hyperlink", link.Target); rel.Add(new XAttribute("TargetMode", "External")); rels.Add(rel); }
                }
                root.Add(links);
            }
            root.Add(new XElement(S + "pageMargins", new XAttribute("left", .25), new XAttribute("right", .25), new XAttribute("top", .4), new XAttribute("bottom", .4), new XAttribute("header", .2), new XAttribute("footer", .2)), new XElement(S + "pageSetup", new XAttribute("paperSize", 9), new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0)));
            if (sheet.Pictures.Count > 0)
            {
                string drawingPath = $"xl/drawings/drawing{i + 1}.xml";
                Type(drawingPath, "application/vnd.openxmlformats-officedocument.drawing+xml");
                rels.Add(Rel("drawing", "drawing", $"../drawings/drawing{i + 1}.xml")); root.Add(new XElement(S + "drawing", new XAttribute(R + "id", "drawing")));
                var drawing = new XElement(D + "wsDr", new XAttribute(XNamespace.Xmlns + "xdr", D), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R)); var drawingRels = new XElement(P + "Relationships");
                int n = 0;
                foreach (var pic in sheet.Pictures)
                {
                    n++; string imagePath = $"xl/media/image{i + 1}p{n}.{pic.Extension}";
                    Bytes(imagePath, pic.Data); Type(imagePath, pic.Extension == "jpg" ? "image/jpeg" : "image/" + pic.Extension);
                    drawingRels.Add(Rel("img" + n, "image", $"../media/image{i + 1}p{n}.{pic.Extension}"));
                    drawing.Add(new XElement(D + "oneCellAnchor", new XElement(D + "from", new XElement(D + "col", 1), new XElement(D + "colOff", 0), new XElement(D + "row", pic.Row), new XElement(D + "rowOff", 0)), new XElement(D + "ext", new XAttribute("cx", pic.Width * 9525L), new XAttribute("cy", pic.Height * 9525L)),
                        new XElement(D + "pic", new XElement(D + "nvPicPr", new XElement(D + "cNvPr", new XAttribute("id", n), new XAttribute("name", "Evidence " + n)), new XElement(D + "cNvPicPr", new XElement(A + "picLocks", new XAttribute("noChangeAspect", 1)))),
                            new XElement(D + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", "img" + n)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                            new XElement(D + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", pic.Width * 9525L), new XAttribute("cy", pic.Height * 9525L))), new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")))), new XElement(D + "clientData")));
                }
                Xml(drawingPath, drawing); Xml($"xl/drawings/_rels/drawing{i + 1}.xml.rels", drawingRels);
            }
            Xml(sheetPath, root); if (rels.HasElements) Xml($"xl/worksheets/_rels/sheet{i + 1}.xml.rels", rels);
        }
        Xml("[Content_Types].xml", contentTypes);
    }
    private static XElement Rel(string id, string type, string target) => new(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target));
    private static XElement Styles()
    {
        var fills = new[] { "FFFFFF", "FFFFFF", "164C40", "E6F1EC", "C6EFCE", "FFC7CE", "FFEB9C" };
        var root = new XElement(S + "styleSheet",
            new XElement(S + "fonts", new XAttribute("count", 2), new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 10)), new XElement(S + "name", new XAttribute("val", "Yu Gothic"))), new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 14)), new XElement(S + "color", new XAttribute("rgb", "FFFFFFFF")), new XElement(S + "name", new XAttribute("val", "Yu Gothic")))),
            new XElement(S + "fills", new XAttribute("count", fills.Length), fills.Select((f, i) => new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", i == 0 ? "none" : i == 1 ? "gray125" : "solid"), i < 2 ? null : new XElement(S + "fgColor", new XAttribute("rgb", "FF" + f)), i < 2 ? null : new XElement(S + "bgColor", new XAttribute("indexed", 64)))))),
            new XElement(S + "borders", new XAttribute("count", 1), new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom"), new XElement(S + "diagonal"))),
            new XElement(S + "cellStyleXfs", new XAttribute("count", 1), new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            new XElement(S + "cellXfs", new XAttribute("count", 6), Enumerable.Range(0, 6).Select(i => new XElement(S + "xf", new XAttribute("numFmtId", 49), new XAttribute("fontId", i == 1 ? 1 : 0), new XAttribute("fillId", i == 0 ? 0 : i + 1), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XAttribute("applyNumberFormat", 1), new XElement(S + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", 1))))),
            new XElement(S + "cellStyles", new XAttribute("count", 1), new XElement(S + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
        return root;
    }
    public static (int Width, int Height, string Extension) ImageSize(byte[] data)
    {
        string type = ImageType(data); int w = 0, h = 0; string ext;
        if (type == "image/png" && data.Length >= 24) { w = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)); h = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20)); ext = "png"; }
        else if (type == "image/gif" && data.Length >= 10) { w = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)); h = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)); ext = "gif"; }
        else if (type == "image/bmp" && data.Length >= 26) { w = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(18)); h = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(22))); ext = "bmp"; }
        else if (type == "image/jpeg")
        {
            ext = "jpg"; int p = 2;
            while (p + 4 < data.Length)
            {
                if (data[p++] != 255) continue; byte marker = data[p++];
                if (marker is 0xD8 or 0xD9 or 0x01 or >= 0xD0 and <= 0xD7) continue;
                if (marker == 0xFF) { p--; continue; }
                int len = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p)); if (len < 2 || p + len > data.Length) break;
                if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC && len >= 7) { h = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 3)); w = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 5)); break; }
                p += len;
            }
        }
        else throw new InvalidDataException("Excel の画像は PNG / JPEG / GIF / BMP が必要です。WebP は PNG に変換して取り込んでください。");
        if (w < 1 || h < 1 || (long)w * h > 80_000_000) throw new InvalidDataException("画像が破損しているか、80 メガピクセルを超えています。");
        return (w, h, ext);
    }
    public static string ImageType(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })) return "image/png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 216 })) return "image/jpeg";
        if (bytes.AsSpan().StartsWith("GIF8"u8)) return "image/gif";
        if (bytes.AsSpan().StartsWith("BM"u8)) return "image/bmp";
        if (bytes.Length > 12 && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new InvalidDataException("対応していない画像形式です。");
    }
}
