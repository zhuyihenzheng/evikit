// §11: 生成した xlsx / html を読み直して検証する
import { afterAll, beforeAll, describe, expect, test } from "bun:test";
import ExcelJS from "exceljs";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { exportProject, type ExportResult } from "../src/export";
import { buildHtml } from "../src/export/html";
import { ProjectSchema, TestCaseSchema } from "../src/core/types";

const SAMPLE = join(import.meta.dir, "..", "examples", "reference");

let out: ExportResult;
let wb: ExcelJS.Workbook;
let html: string;

beforeAll(async () => {
  const dir = mkdtempSync(join(tmpdir(), "evikit-"));
  out = await exportProject(SAMPLE, dir);
  wb = new ExcelJS.Workbook();
  await wb.xlsx.readFile(out.xlsxPath);
  html = readFileSync(out.htmlPath, "utf8");
}, 60000);

afterAll(() => {
  if (out?.outDir) rmSync(out.outDir, { recursive: true, force: true });
});

describe("export", () => {
  test("xlsx / html / 添付ファイルが出力される", () => {
    const files = readdirSync(out.outDir);
    expect(files.some((f) => f.endsWith(".xlsx"))).toBe(true);
    expect(files.some((f) => f.endsWith(".html"))).toBe(true);
    expect(
      existsSync(join(out.outDir, "files", "TC-003", "E04-customers.zip")),
    ).toBe(true);
    expect(out.files).toContain(join("files", "TC-003", "E04-customers.zip"));
  });

  test("sheet 数 = 4、最初の sheet が サマリ", () => {
    expect(wb.worksheets.length).toBe(4);
    expect(wb.worksheets[0]!.name).toBe("サマリ");
    expect(wb.worksheets.map((w) => w.name)).toEqual([
      "サマリ",
      "TC-001",
      "TC-002",
      "TC-003",
    ]);
  });

  test("TC-001 の画像は 2 枚", () => {
    expect(wb.getWorksheet("TC-001")!.getImages().length).toBe(2);
  });

  test("TC-001!B1 === 'TC-001'", () => {
    expect(wb.getWorksheet("TC-001")!.getCell("B1").value).toBe("TC-001");
  });

  test("TC-003 の表で '00012' が文字列のまま残る", () => {
    const ws = wb.getWorksheet("TC-003")!;
    const hits: string[] = [];
    ws.eachRow({ includeEmpty: false }, (row) => {
      row.eachCell({ includeEmpty: false }, (cell) => {
        if (cell.value === "00012") hits.push(cell.address);
      });
    });
    expect(hits.length).toBe(1);
  });

  test("サマリの見出しと件数（文字列で書かれている）", () => {
    const ws = wb.worksheets[0]!;
    expect(ws.getCell("A1").value).toBe("プロジェクト");
    expect(ws.getCell("B1").value).toBe("顧客管理システム 結合テスト");
    expect(ws.getCell("A5").value).toBe("判定");
    expect(ws.getCell("A6").value).toBe("OK");
    expect(ws.getCell("B6").value).toBe("2");
    expect(ws.getCell("A11").value).toBe("合計");
    expect(ws.getCell("B11").value).toBe("3");
    expect(ws.getCell("A13").value).toBe("テストID");
  });

  test("サマリの ID セルは case sheet への内部リンク", () => {
    const cell = wb.worksheets[0]!.getCell("A14").value as {
      text: string;
      hyperlink: string;
    };
    expect(cell.text).toBe("TC-001");
    expect(cell.hyperlink).toBe("#'TC-001'!A1");
  });

  test("判定セルに §7.1 の色が付く", () => {
    const ok = wb.getWorksheet("TC-001")!.getCell("F1");
    expect(ok.value).toBe("OK");
    expect((ok.fill as ExcelJS.FillPattern).fgColor?.argb).toBe("FFC6EFCE");
    const ng = wb.getWorksheet("TC-002")!.getCell("F1");
    expect(ng.value).toBe("NG");
    expect((ng.fill as ExcelJS.FillPattern).fgColor?.argb).toBe("FFFFC7CE");
  });

  test("合并 + 折返しの行には明示的な行高がある (§7.1)", () => {
    const ws = wb.getWorksheet("TC-001")!;
    // 3 行目 = 前提条件。A 列のラベル「前提条件」は幅 6 に対して 2 行 -> 28pt。
    expect(ws.getRow(3).height).toBe(28);
    // 11 行目 = E01 の標題行。F 列の日時 19 文字は幅 14 に対して 2 行 -> 28pt。
    expect(ws.getCell("A11").value).toBe("E01");
    expect(ws.getRow(11).height).toBe(28);
    // 出典行は B..F 合并のみで 1 行に収まる -> 14pt。
    expect(ws.getRow(12).height).toBe(14);
  });

  test("ステップ行には行高を設定しない (§7.3)", () => {
    // TC-001: 1..3 ヘッダー / 4 空 / 5 ステップ表頭 / 6..8 ステップ行
    const ws = wb.getWorksheet("TC-001")!;
    expect(ws.getCell("A5").value).toBe("No");
    expect(ws.getCell("F5").value).toBe("エビデンス");
    expect(ws.getCell("F6").value).toBe("E01");
    expect(ws.getCell("F7").value).toBe("E02, E04");
    expect(ws.getRow(6).height).toBeUndefined();
  });

  test("A4 横 / 横 1 ページに収める (§7.1)", () => {
    const setup = wb.getWorksheet("TC-001")!.pageSetup;
    expect(setup.orientation).toBe("landscape");
    expect(setup.fitToWidth).toBe(1);
    expect(setup.fitToHeight).toBe(0);
  });
});

describe("html", () => {
  test("http:// / https:// を含まない (§8.1)", () => {
    expect(/https?:\/\//.test(html)).toBe(false);
  });

  test("単一ファイル・画像は data: URI", () => {
    expect(html).toContain('<meta charset="utf-8">');
    expect(html).toContain("data:image/png;base64,");
    expect(html).not.toContain("<link ");
    expect(html).not.toContain("<script src");
  });

  test("case セクションとエビデンスのアンカー", () => {
    expect(html).toContain('id="case-TC-001"');
    expect(html).toContain('id="ev-TC-001-E01"');
    expect(html).toContain('href="#ev-TC-001-E01"');
    expect(html).toContain('href="data:application/octet-stream;base64,');
    expect(html).toContain('download="customers.zip"');
  });

  test("検索 / NG のみ / 判定バッジ", () => {
    expect(html).toContain('id="q"');
    expect(html).toContain('id="ngonly"');
    expect(html).toContain("NG のみ");
    expect(html).toContain('class="v v-ng"');
  });

  test("'00012' がそのまま出る", () => {
    expect(html).toContain("<td>00012</td>");
  });

  test("ユーザーデータは HTML エスケープされる (§8.7)", () => {
    const project = ProjectSchema.parse({
      name: "<script>x</script>",
      env: "STG",
    });
    const testCase = TestCaseSchema.parse({
      id: "TC-9",
      title: "a & b",
      steps: [{ no: 1, action: "<b>押下</b>", verdict: "OK" }],
    });
    const out = buildHtml(SAMPLE, project, [testCase]);
    expect(out).toContain("&lt;script&gt;x&lt;/script&gt;");
    expect(out).toContain("&lt;b&gt;押下&lt;/b&gt;");
    expect(out).toContain("a &amp; b");
    expect(out).not.toContain("<script>x</script>");
  });
});

describe("excerptLines の打ち切り (§7.3 / §8.5)", () => {
  test("Excel は残り行数を注記し、HTML は details に畳む", async () => {
    const dir = mkdtempSync(join(tmpdir(), "evikit-src-"));
    mkdirSync(join(dir, "cases"), { recursive: true });
    mkdirSync(join(dir, "evidence", "TC-1"), { recursive: true });
    writeFileSync(
      join(dir, "project.yaml"),
      "name: excerpt\nenv: STG\nexcerptLines: 5\n",
    );
    const lines = Array.from({ length: 12 }, (_, i) => `line ${i + 1}`).join(
      "\n",
    );
    writeFileSync(join(dir, "evidence", "TC-1", "E01.log"), `${lines}\n`);
    writeFileSync(
      join(dir, "cases", "TC-1.yaml"),
      [
        "id: TC-1",
        "title: 長いログ",
        "steps: []",
        "evidence:",
        "  - id: E01",
        "    kind: text",
        "    category: ログ",
        "    caption: 長いログ",
        "    step: null",
        "    file: E01.log",
        "    lang: log",
        "",
      ].join("\n"),
    );

    const result = await exportProject(
      dir,
      mkdtempSync(join(tmpdir(), "evikit-out-")),
    );
    const book = new ExcelJS.Workbook();
    await book.xlsx.readFile(result.xlsxPath);
    const ws = book.getWorksheet("TC-1")!;
    const texts: string[] = [];
    ws.eachRow({ includeEmpty: false }, (row) => {
      row.eachCell({ includeEmpty: false }, (cell) => {
        if (typeof cell.value === "string") texts.push(cell.value);
      });
    });
    expect(texts).toContain("line 5");
    expect(texts).not.toContain("line 6");
    expect(texts).toContain("…（全 12 行、残りは HTML 版を参照）");

    const page = readFileSync(result.htmlPath, "utf8");
    expect(page).toContain("残り 7 行を表示");
    expect(page).toContain("<details>");

    rmSync(dir, { recursive: true, force: true });
    rmSync(result.outDir, { recursive: true, force: true });
  }, 30000);
});
