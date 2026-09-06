// xlsx.ts / html.ts が共有する並び順・表示整形のヘルパ
import type { Evidence } from "../core/types";

/** E01 -> 1。数字が無ければ 0。 */
function evidenceNo(id: string): number {
  const m = /(\d+)/.exec(id);
  return m ? Number(m[1]) : 0;
}

/**
 * §5.3: step 昇順でグループ化し、step: null は最後 (ラベル「共通」)。
 * グループ内は id 番号の昇順。
 */
export function orderEvidence(evidence: Evidence[]): Evidence[] {
  return [...evidence].sort((a, b) => {
    const as = a.step ?? Number.MAX_SAFE_INTEGER;
    const bs = b.step ?? Number.MAX_SAFE_INTEGER;
    if (as !== bs) return as - bs;
    const an = evidenceNo(a.id);
    const bn = evidenceNo(b.id);
    return an !== bn ? an - bn : a.id.localeCompare(b.id);
  });
}

/** 証拠ブロックのグループ表示: `Step 1` または `共通`。 */
export function stepLabel(step: number | null): string {
  return step === null ? "共通" : `Step ${step}`;
}

/**
 * `2026-09-04T10:12:03+09:00` -> `2026-09-04 10:12:03`。
 * Date を経由するとタイムゾーンでずれるので文字列のまま整形する。
 */
export function formatCapturedAt(value: string): string {
  if (!value) return "";
  return value
    .replace("T", " ")
    .replace(/(Z|[+-]\d{2}:?\d{2})$/, "")
    .trim()
    .slice(0, 19);
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** `yyyy-mm-dd HH:mm` (ローカル時刻)。 */
export function formatStamp(d: Date): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

/** 出力ディレクトリ名用の `yyyyMMdd-HHmmss`。 */
export function formatDirStamp(d: Date): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

/** 末尾の空行を落として行配列にする。 */
export function toLines(text: string): string[] {
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  while (lines.length > 0 && lines[lines.length - 1] === "") lines.pop();
  return lines;
}

/** §7.1: CJK は 2 文字幅として数える。 */
export function displayWidth(text: string): number {
  let w = 0;
  for (const ch of text) w += (ch.codePointAt(0) ?? 0) > 0xff ? 2 : 1;
  return w;
}

/** category が DB で source が SQL 文らしいか (§7.3 出典行)。 */
export function looksLikeSql(source: string): boolean {
  return /^\s*(SELECT|INSERT|UPDATE|DELETE|WITH|CREATE|ALTER|DROP)\b/i.test(
    source,
  );
}

/** 出力ファイル名に使えない文字を `_` に置き換える。 */
export function safeFileName(name: string): string {
  const cleaned = name
    .replace(/[\\/:*?"<>|]/g, "_")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/^\.+/, "");
  return cleaned === "" ? "project" : cleaned;
}
