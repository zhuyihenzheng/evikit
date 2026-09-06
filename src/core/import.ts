import Papa from "papaparse";
import { detectFile, detectText, extensionOf, TEXT_MAX_BYTES } from "./detect";
import type { Category, Kind, Lang } from "./types";

export const MAX_UPLOAD_BYTES = 25 * 1024 * 1024;
export interface ImportResult {
  kind: Kind;
  lang: Lang;
  category: Category;
  text: string;
  extension: string;
}

/** Only recognize a terminal table when there is a structural separator row. */
export function terminalTable(text: string): string[][] | null {
  const lines = text.trim().split(/\r?\n/);
  if (
    !lines.some(
      (l) => /^\s*\+[-+]+\+\s*$/.test(l) || /^\s*[-]+(?:\+[-]+)+\s*$/.test(l),
    )
  )
    return null;
  const rows = lines
    .filter((l) => l.includes("|") && !/^\s*\+[-+]+\+\s*$/.test(l))
    .map((l) =>
      l
        .trim()
        .replace(/^\|/, "")
        .replace(/\|$/, "")
        .split("|")
        .map((v) => v.trim()),
    );
  if (
    rows.length < 2 ||
    rows[0]!.length < 2 ||
    !rows.every((r) => r.length === rows[0]!.length)
  )
    return null;
  return rows;
}

export function normalizeTable(text: string, delimiter?: string): string {
  const parsed = Papa.parse<string[]>(text.replace(/^\uFEFF/, ""), {
    delimiter,
    skipEmptyLines: true,
    dynamicTyping: false,
  });
  const errors = parsed.errors.filter(
    (e) => e.code !== "UndetectableDelimiter",
  );
  if (errors.length)
    throw new Error(`表を読み取れません: ${errors[0]!.message}`);
  const rows = parsed.data;
  if (!rows.length || !rows[0]!.length) throw new Error("表が空です");
  if (rows.length > 10001 || rows[0]!.length > 256)
    throw new Error("表は 10,000 行・256 列以内にしてください");
  if (!rows.every((row) => row.length === rows[0]!.length))
    throw new Error("表の列数が揃っていません。CSV / TSV を確認してください");
  return Papa.unparse(rows, { newline: "\n" });
}

export function prepareText(text: string, fileName?: string): ImportResult {
  const value = text.replace(/^\uFEFF/, "").replace(/\r\n?/g, "\n");
  if (new TextEncoder().encode(value).length > TEXT_MAX_BYTES)
    throw new Error("テキスト・表は 2 MB 以内にしてください");
  if (!value.trim()) throw new Error("内容を入力してください");
  const terminal = !fileName ? terminalTable(value) : null;
  const detection = terminal
    ? { kind: "table" as const, lang: "" as const }
    : fileName
      ? detectFile(fileName)
      : detectText(value);
  const ext = fileName ? extensionOf(fileName) : "";
  return {
    ...detection,
    text:
      detection.kind === "table"
        ? normalizeTable(
            terminal ? Papa.unparse(terminal) : value,
            terminal ? "," : ext === "tsv" || !fileName ? "\t" : undefined,
          )
        : value,
    category:
      detection.kind === "table"
        ? "DB"
        : detection.lang === "log"
          ? "ログ"
          : detection.lang === "json" || detection.lang === "xml"
            ? "API"
            : ext === "sql"
              ? "DB"
              : "その他",
    extension: detection.kind === "table" ? "csv" : ext || "txt",
  };
}
