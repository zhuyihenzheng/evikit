// §6 貼り付け / ドラッグ&ドロップ → kind 判定表
import type { Kind, Lang } from "./types";

export interface Detection {
  kind: Kind;
  lang: Lang;
}

/** §6: テキスト系ファイルの上限。超えたら file 扱い。 */
export const TEXT_MAX_BYTES = 2 * 1024 * 1024;

const IMAGE_EXTS = ["png", "jpg", "jpeg", "gif", "webp"];
const TABLE_EXTS = ["csv", "tsv"];
const TEXT_EXTS = [
  "txt",
  "log",
  "json",
  "xml",
  "md",
  "sql",
  "yaml",
  "yml",
  "ini",
  "conf",
  "properties",
  "sh",
  "bat",
  "ps1",
];
/** §6: lang は拡張子から。log/json/xml/sql 以外は plain。 */
const EXT_LANG: Record<string, Lang> = {
  log: "log",
  json: "json",
  xml: "xml",
  sql: "sql",
};

export function extensionOf(fileName: string): string {
  const m = /\.([A-Za-z0-9]+)$/.exec(fileName);
  return m ? m[1]!.toLowerCase() : "";
}

/** ファイル名 + サイズから判定する。 */
export function detectFile(fileName: string, size = 0): Detection {
  const ext = extensionOf(fileName);
  if (IMAGE_EXTS.includes(ext)) return { kind: "image", lang: "" };
  if (TABLE_EXTS.includes(ext)) return { kind: "table", lang: "" };
  if (TEXT_EXTS.includes(ext)) {
    // §6: テキスト系でも 2 MB 超は添付ファイル扱い。
    if (size > TEXT_MAX_BYTES) return { kind: "file", lang: "" };
    return { kind: "text", lang: EXT_LANG[ext] ?? "plain" };
  }
  return { kind: "file", lang: "" };
}

/** 貼り付けテキストの lang 判定 (§6)。 */
export function detectLang(text: string): Lang {
  const head = text.trimStart();
  if (head.startsWith("{") || head.startsWith("[")) {
    try {
      JSON.parse(head);
      return "json";
    } catch {
      return "plain";
    }
  }
  if (head.startsWith("<")) return "xml";
  return "plain";
}

/**
 * §6: 2 行以上で、各行のタブ数が同じかつ 1 以上なら table。
 * カンマ区切りは表として扱わない (ログにカンマが入るため)。
 */
export function detectText(text: string): Detection {
  const lines = text
    .replace(/\r\n?/g, "\n")
    .split("\n")
    .filter((l) => l !== "");
  if (lines.length >= 2) {
    const tabs = lines.map((l) => (l.match(/\t/g) ?? []).length);
    if (tabs[0]! >= 1 && tabs.every((t) => t === tabs[0]))
      return { kind: "table", lang: "" };
  }
  return { kind: "text", lang: detectLang(text) };
}

export type DetectInput =
  | { type: "clipboardImage" }
  | { type: "file"; fileName: string; size?: number }
  | { type: "text"; text: string };

export function detect(input: DetectInput): Detection {
  switch (input.type) {
    case "clipboardImage":
      // §6: クリップボード画像はそのまま保存 (再エンコードしない)。
      return { kind: "image", lang: "" };
    case "file":
      return detectFile(input.fileName, input.size ?? 0);
    case "text":
      return detectText(input.text);
  }
}
