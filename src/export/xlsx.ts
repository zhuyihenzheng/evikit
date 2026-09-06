// §7 Excel 版式（固定レイアウト）
import ExcelJS from "exceljs";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import Papa from "papaparse";
import { imageSize } from "image-size";
import { evidencePath } from "../core/store";
import type { Evidence, Project, TestCase } from "../core/types";
import { resolveVerdict, verdictStyle } from "../core/verdict";
import {
  displayWidth,
  formatBytes,
  formatCapturedAt,
  formatStamp,
  looksLikeSql,
  orderEvidence,
  stepLabel,
  toLines,
} from "./format";

// §7.1 共通の書式
const FONT = { name: "Meiryo UI", size: 10 };
const MONO = { name: "MS Gothic", size: 10 };
const FILL_LABEL = "F2F2F2";
const FILL_HEADER = "D9D9D9";
const FILL_EVIDENCE = "DDEBF7";
const BORDER_COLOR = "BFBFBF";
const GRAY_TEXT = "808080";

/** §7.1 列幅 A..F */
const CASE_COL_WIDTHS = [6, 36, 32, 32, 8, 14];
/** §7.2 サマリの列幅 A..G */
const SUMMARY_COL_WIDTHS = [16, 40, 10, 10, 12, 12, 10];

/** §7.1: 合并行の高さ計算。1 行 14pt、上限 20 行。 */
const LINE_PT = 14;
const MAX_LINES = 20;
/** §7.1: 既定の行高 20px。画像の後はこの単位で行を進める。 */
const ROW_PX = 20;

const COL_B = 2;
const COL_F = 6;

interface CellOpts {
  mono?: boolean;
  bold?: boolean;
  fill?: string | null;
  fontColor?: string | null;
  align?: "left" | "center" | "right";
  border?: boolean;
  wrap?: boolean;
  hyperlink?: string;
}

const argb = (hex: string) => `FF${hex}`;

function thinBorder(): Partial<ExcelJS.Borders> {
  const side = { style: "thin" as const, color: { argb: argb(BORDER_COLOR) } };
  return { top: side, left: side, bottom: side, right: side };
}

/**
 * すべてのセルはこの関数を通す。§7.1 のフォント (Meiryo UI 10 / コードは MS Gothic 10)、
 * 上揃え + 折返しをここで必ず適用する。
 */
function put(
  ws: ExcelJS.Worksheet,
  row: number,
  col: number,
  value: string,
  o: CellOpts = {},
): ExcelJS.Cell {
  const cell = ws.getCell(row, col);
  // §7.1: 表の値はすべて文字列で書く（ID の前ゼロ対策）。
  cell.value = o.hyperlink ? { text: value, hyperlink: o.hyperlink } : value;
  cell.font = {
    ...(o.mono ? MONO : FONT),
    bold: o.bold ?? false,
    ...(o.fontColor ? { color: { argb: argb(o.fontColor) } } : {}),
  };
  cell.alignment = {
    vertical: "top",
    wrapText: o.wrap ?? true,
    horizontal: o.align ?? "left",
  };
  if (o.fill)
    cell.fill = {
      type: "pattern",
      pattern: "solid",
      fgColor: { argb: argb(o.fill) },
    };
  if (o.border) cell.border = thinBorder();
  return cell;
}

/** 判定セル（§7.1 判定色、中央揃え）。 */
function putVerdict(
  ws: ExcelJS.Worksheet,
  row: number,
  col: number,
  verdict: string,
  border = true,
) {
  const style = verdictStyle(verdict);
  return put(ws, row, col, verdict, {
    align: "center",
    fill: style.fill,
    fontColor: style.font,
    border,
  });
}

function widthOf(widths: number[], from: number, to: number): number {
  let sum = 0;
  for (let c = from; c <= to; c++) sum += widths[c - 1] ?? 0;
  return sum;
}

/** §7.1: 行数 = ceil(表示幅 / セル幅)、CJK は 2 文字幅。改行ごとに数える。 */
function lineCount(text: string, cellWidth: number): number {
  let rows = 0;
  for (const line of text.split("\n")) {
    rows += Math.max(1, Math.ceil(displayWidth(line) / cellWidth));
  }
  return Math.max(rows, 1);
}

/** 1 行を c1..c2 で結合して値を書く。行高は pinRowHeight で最後にまとめて決める。 */
function mergeCells(
  ws: ExcelJS.Worksheet,
  row: number,
  c1: number,
  c2: number,
  value: string,
  o: CellOpts = {},
): ExcelJS.Cell {
  ws.mergeCells(row, c1, row, c2);
  return put(ws, row, c1, value, o);
}

/**
 * §7.1: 合并单元格不会自动调高 -> 「合并 + 折返」の行は必ず height を明示する。
 * 合并セルだけでなく同じ行の他のセル（A 列のラベル、F 列の日時など）も数えて
 * 最大の行数を採る。1 行 14pt、上限 20 行。
 */
function pinRowHeight(
  ws: ExcelJS.Worksheet,
  widths: number[],
  row: number,
  merges: [number, number][],
): void {
  const mergedWidth = new Map<number, number>();
  const covered = new Set<number>();
  for (const [c1, c2] of merges) {
    mergedWidth.set(c1, widthOf(widths, c1, c2));
    for (let c = c1 + 1; c <= c2; c++) covered.add(c);
  }

  let lines = 1;
  for (let c = 1; c <= widths.length; c++) {
    if (covered.has(c)) continue;
    const cell = ws.getCell(row, c);
    if (cell.alignment?.wrapText === false) continue;
    const value = cell.value;
    const text =
      typeof value === "string"
        ? value
        : typeof value === "object" && value !== null && "text" in value
          ? String((value as { text: unknown }).text)
          : "";
    if (text === "") continue;
    lines = Math.max(
      lines,
      lineCount(text, mergedWidth.get(c) ?? widths[c - 1] ?? 1),
    );
  }
  ws.getRow(row).height = Math.min(lines, MAX_LINES) * LINE_PT;
}

/** B..F を結合した 1 行を書いて行高を固定する（証拠ブロックで多用するパターン）。 */
function mergedLine(
  ws: ExcelJS.Worksheet,
  row: number,
  value: string,
  o: CellOpts = {},
): void {
  mergeCells(ws, row, COL_B, COL_F, value, o);
  pinRowHeight(ws, CASE_COL_WIDTHS, row, [[COL_B, COL_F]]);
}

/** §7.1 sheet 名: `[ ] : * ? / \` を除去し 31 文字に切り、重複は `_2`。 */
export function sheetNameFor(caseId: string, used: Set<string>): string {
  let base = caseId.replace(/[\[\]:*?/\\]/g, "").slice(0, 31);
  if (base === "") base = "sheet";
  let name = base;
  let n = 2;
  while (used.has(name)) {
    const suffix = `_${n}`;
    name = base.slice(0, 31 - suffix.length) + suffix;
    n += 1;
  }
  used.add(name);
  return name;
}

function applyPageSetup(ws: ExcelJS.Worksheet): void {
  // §7.1: A4 横、横 1 ページに収める。fitToPage が無いと fitToWidth は無視される。
  ws.pageSetup = {
    paperSize: 9,
    orientation: "landscape",
    fitToPage: true,
    fitToWidth: 1,
    fitToHeight: 0,
  };
}

function setWidths(ws: ExcelJS.Worksheet, widths: number[]): void {
  widths.forEach((w, i) => {
    ws.getColumn(i + 1).width = w;
  });
}

const IMAGE_EXT: Record<string, "png" | "jpeg" | "gif"> = {
  png: "png",
  jpg: "jpeg",
  jpeg: "jpeg",
  gif: "gif",
};

// ---------------------------------------------------------------- サマリ sheet

function buildSummarySheet(
  wb: ExcelJS.Workbook,
  project: Project,
  cases: TestCase[],
  sheetNames: Map<string, string>,
  generatedAt: Date,
): void {
  const ws = wb.addWorksheet("サマリ");
  applyPageSetup(ws);
  setWidths(ws, SUMMARY_COL_WIDTHS);

  const head: [string, string][] = [
    ["プロジェクト", project.name],
    ["出力日時", formatStamp(generatedAt)],
    ["環境", project.env],
  ];
  head.forEach(([label, value], i) => {
    const row = i + 1;
    put(ws, row, 1, label, { fill: FILL_LABEL, border: true });
    put(ws, row, 2, value, { border: true });
  });

  // 行 5: 判定件数の表頭
  let r = 5;
  put(ws, r, 1, "判定", {
    fill: FILL_HEADER,
    bold: true,
    border: true,
    align: "center",
  });
  put(ws, r, 2, "件数", {
    fill: FILL_HEADER,
    bold: true,
    border: true,
    align: "center",
  });
  r += 1;

  let total = 0;
  for (const verdict of project.verdicts) {
    const count = cases.filter((c) => resolveVerdict(c) === verdict).length;
    total += count;
    putVerdict(ws, r, 1, verdict);
    put(ws, r, 2, String(count), { align: "center", border: true });
    r += 1;
  }
  put(ws, r, 1, "合計", { bold: true, border: true, align: "center" });
  put(ws, r, 2, String(total), { bold: true, border: true, align: "center" });
  r += 2; // 1 行空ける

  const headers = [
    "テストID",
    "件名",
    "判定",
    "ステップ数",
    "エビデンス数",
    "実施日",
    "実施者",
  ];
  headers.forEach((h, i) => {
    put(ws, r, i + 1, h, {
      fill: FILL_HEADER,
      bold: true,
      border: true,
      align: "center",
    });
  });
  r += 1;

  for (const c of cases) {
    const sheet = sheetNames.get(c.id)!;
    // §7.2: A 列は対応 sheet への内部リンク。
    put(ws, r, 1, c.id, { border: true, hyperlink: `#'${sheet}'!A1` });
    put(ws, r, 2, c.title, { border: true });
    putVerdict(ws, r, 3, resolveVerdict(c));
    put(ws, r, 4, String(c.steps.length), { align: "center", border: true });
    put(ws, r, 5, String(c.evidence.length), { align: "center", border: true });
    put(ws, r, 6, c.date, { align: "center", border: true });
    put(ws, r, 7, c.tester, { align: "center", border: true });
    r += 1;
  }
}

// ------------------------------------------------------------- エビデンス内容

function writeImage(
  wb: ExcelJS.Workbook,
  ws: ExcelJS.Worksheet,
  project: Project,
  path: string,
  row: number,
): number {
  const buffer = readFileSync(path);
  const size = imageSize(buffer);
  let width = size.width;
  let height = size.height;
  // §7.3: imageMaxWidth まで等比縮小。小さい画像は拡大しない。
  if (width > project.imageMaxWidth) {
    height = Math.round((height * project.imageMaxWidth) / width);
    width = project.imageMaxWidth;
  }
  const ext = IMAGE_EXT[(size.type ?? "png").toLowerCase()];
  if (!ext)
    throw new Error(
      "Excel で表示できない画像形式です。画像の注釈画面で PNG として保存してから出力してください。",
    );
  const imageId = wb.addImage({
    buffer: buffer as unknown as ExcelJS.Buffer,
    extension: ext,
  });
  ws.addImage(imageId, {
    tl: { col: COL_B - 1, row: row - 1 } as ExcelJS.Anchor,
    ext: { width, height },
    editAs: "oneCell",
  });
  // §7.1: 画像の後はピクセル高で行を進める（既定行高 20px）。この行には高さを設定しない。
  return row + Math.ceil(height / ROW_PX) + 1;
}

function writeTable(ws: ExcelJS.Worksheet, path: string, row: number): number {
  const parsed = Papa.parse<string[]>(readFileSync(path, "utf8"), {
    skipEmptyLines: true,
  });
  const rows = parsed.data;
  if (rows.length === 0) return row;
  const [header, ...body] = rows;

  header!.forEach((value, i) => {
    put(ws, row, COL_B + i, String(value), {
      fill: FILL_HEADER,
      bold: true,
      border: true,
      align: "center",
    });
  });
  row += 1;

  for (const line of body) {
    line.forEach((value, i) => {
      // §7.1: 表の値はすべて文字列（`00012` の前ゼロを保つ）。
      put(ws, row, COL_B + i, String(value), { border: true });
    });
    row += 1;
  }
  put(ws, row, COL_B, `${body.length} 件`);
  return row + 1;
}

function writeText(
  ws: ExcelJS.Worksheet,
  project: Project,
  path: string,
  row: number,
): number {
  const lines = toLines(readFileSync(path, "utf8"));
  const shown = lines.slice(0, project.excerptLines);
  for (let i = 0; i < shown.length; i++) {
    put(ws, row, 1, String(i + 1), {
      mono: true,
      align: "right",
      fontColor: GRAY_TEXT,
      wrap: false,
    });
    mergedLine(ws, row, shown[i]!, { mono: true });
    row += 1;
  }
  if (lines.length > shown.length) {
    mergedLine(ws, row, `…（全 ${lines.length} 行、残りは HTML 版を参照）`);
    row += 1;
  }
  return row;
}

function writeFileLink(
  ws: ExcelJS.Worksheet,
  caseId: string,
  evidence: Evidence,
  path: string,
  row: number,
): number {
  const bytes =
    evidence.size > 0
      ? evidence.size
      : existsSync(path)
        ? statSync(path).size
        : 0;
  const name = evidence.originalName || evidence.file;
  // §7.3: 出力ディレクトリからの相対リンク。
  mergedLine(ws, row, `添付ファイル: ${name}（${formatBytes(bytes)}）`, {
    hyperlink: `files/${caseId}/${evidence.file}`,
  });
  return row + 1;
}

function writeEvidenceBlock(
  wb: ExcelJS.Workbook,
  ws: ExcelJS.Worksheet,
  project: Project,
  projectDir: string,
  testCase: TestCase,
  evidence: Evidence,
  row: number,
): number {
  // 標題行: A id / B..D `{category}｜{caption}` / E Step / F capturedAt。行全体を DDEBF7 で塗る。
  put(ws, row, 1, evidence.id, { bold: true, fill: FILL_EVIDENCE });
  mergeCells(ws, row, COL_B, 4, `${evidence.category}｜${evidence.caption}`, {
    bold: true,
    fill: FILL_EVIDENCE,
  });
  put(ws, row, 5, stepLabel(evidence.step), {
    fill: FILL_EVIDENCE,
    align: "center",
  });
  put(ws, row, 6, formatCapturedAt(evidence.capturedAt), {
    fill: FILL_EVIDENCE,
  });
  // A / E / F にも文字があるので、行全体から行高を決める。
  pinRowHeight(ws, CASE_COL_WIDTHS, row, [[COL_B, 4]]);
  row += 1;

  if (evidence.source !== "") {
    // §7.3: category が DB で SQL らしい source は `SQL: ` を付けて等幅にする。
    const isSql = evidence.category === "DB" && looksLikeSql(evidence.source);
    const label = isSql ? "SQL: " : "出典: ";
    mergedLine(ws, row, `${label}${evidence.source}`, { mono: isSql });
    row += 1;
  }

  const path = evidencePath(projectDir, testCase.id, evidence.file);
  if (existsSync(path)) {
    if (evidence.kind === "image") row = writeImage(wb, ws, project, path, row);
    else if (evidence.kind === "table") row = writeTable(ws, path, row);
    else if (evidence.kind === "text") row = writeText(ws, project, path, row);
    else row = writeFileLink(ws, testCase.id, evidence, path, row);
  }

  if (evidence.note !== "") {
    mergedLine(ws, row, `確認ポイント: ${evidence.note}`);
    row += 1;
  }
  return row + 1; // 1 行空ける
}

// ------------------------------------------------------------------ case sheet

function buildCaseSheet(
  wb: ExcelJS.Workbook,
  project: Project,
  projectDir: string,
  testCase: TestCase,
  sheetName: string,
): void {
  const ws = wb.addWorksheet(sheetName);
  applyPageSetup(ws);
  setWidths(ws, CASE_COL_WIDTHS);

  put(ws, 1, 1, "テストID", { fill: FILL_LABEL, border: true });
  put(ws, 1, 2, testCase.id, { border: true });
  put(ws, 1, 3, "件名", { fill: FILL_LABEL, border: true });
  put(ws, 1, 4, testCase.title, { border: true });
  put(ws, 1, 5, "判定", { fill: FILL_LABEL, border: true });
  putVerdict(ws, 1, 6, resolveVerdict(testCase));

  put(ws, 2, 1, "実施者", { fill: FILL_LABEL, border: true });
  put(ws, 2, 2, testCase.tester, { border: true });
  put(ws, 2, 3, "実施日", { fill: FILL_LABEL, border: true });
  put(ws, 2, 4, testCase.date, { border: true });
  put(ws, 2, 5, "環境", { fill: FILL_LABEL, border: true });
  put(ws, 2, 6, testCase.env, { border: true });

  put(ws, 3, 1, "前提条件", { fill: FILL_LABEL, border: true });
  mergeCells(ws, 3, COL_B, COL_F, testCase.precondition, { border: true });
  pinRowHeight(ws, CASE_COL_WIDTHS, 3, [[COL_B, COL_F]]);

  let r = 4;
  // §7.3: 備考は note が非空のときだけ行を出す。
  if (testCase.note !== "") {
    put(ws, r, 1, "備考", { fill: FILL_LABEL, border: true });
    mergeCells(ws, r, COL_B, COL_F, testCase.note, { border: true });
    pinRowHeight(ws, CASE_COL_WIDTHS, r, [[COL_B, COL_F]]);
    r += 1;
  }
  r += 1; // 1 行空ける

  const stepHeaders = [
    "No",
    "操作",
    "期待結果",
    "実際結果",
    "判定",
    "エビデンス",
  ];
  stepHeaders.forEach((h, i) => {
    put(ws, r, i + 1, h, {
      fill: FILL_HEADER,
      bold: true,
      border: true,
      align: "center",
    });
  });
  r += 1;

  const ordered = orderEvidence(testCase.evidence);
  for (const step of testCase.steps) {
    const ids = ordered
      .filter((e) => e.step === step.no)
      .map((e) => e.id)
      .join(", ");
    put(ws, r, 1, String(step.no), { align: "center", border: true });
    put(ws, r, 2, step.action, { border: true });
    put(ws, r, 3, step.expected, { border: true });
    put(ws, r, 4, step.actual, { border: true });
    putVerdict(ws, r, 5, step.verdict);
    put(ws, r, 6, ids, { border: true });
    // §7.3: ステップ行は結合しない・行高も設定しない（Excel の自動調整に任せる）。
    r += 1;
  }
  r += 1; // 1 行空ける

  put(ws, r, 1, "エビデンス", { bold: true });
  r += 1;

  for (const evidence of ordered) {
    r = writeEvidenceBlock(wb, ws, project, projectDir, testCase, evidence, r);
  }
}

/** プロジェクト全体を 1 つのワークブックに書き出す。 */
export async function writeXlsx(
  projectDir: string,
  project: Project,
  cases: TestCase[],
  outFile: string,
  generatedAt = new Date(),
): Promise<void> {
  const wb = new ExcelJS.Workbook();
  wb.created = generatedAt;

  const used = new Set<string>(["サマリ"]);
  const sheetNames = new Map<string, string>();
  for (const c of cases) sheetNames.set(c.id, sheetNameFor(c.id, used));

  // §7.2: サマリが最初の sheet。
  buildSummarySheet(wb, project, cases, sheetNames, generatedAt);
  for (const c of cases)
    buildCaseSheet(wb, project, projectDir, c, sheetNames.get(c.id)!);

  await wb.xlsx.writeFile(outFile);
}
