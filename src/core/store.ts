// project.yaml / cases/*.yaml の読み書き、エビデンスファイルの追加と ID 採番
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readdirSync, readFileSync } from "node:fs";
import { basename } from "node:path";
import { atomicWrite, safePath } from "./fs";
import YAML from "yaml";
import {
  ProjectSchema,
  TestCaseSchema,
  type Category,
  type Evidence,
  type Kind,
  type Lang,
  type Project,
  type TestCase,
} from "./types";

export const projectFile = (dir: string) => safePath(dir, "project.yaml");
export const casesDir = (dir: string) => safePath(dir, "cases");
function checkedId(id: string): string {
  if (!isValidCaseId(id)) throw new Error("case id は [A-Za-z0-9_-] のみ");
  return id;
}
export const caseFile = (dir: string, caseId: string) =>
  safePath(dir, "cases", `${checkedId(caseId)}.yaml`);
export const evidenceDir = (dir: string, caseId: string) =>
  safePath(dir, "evidence", checkedId(caseId));
export const evidencePath = (dir: string, caseId: string, file: string) =>
  safePath(dir, "evidence", checkedId(caseId), file);
export const existingCaseFile = (dir: string, caseId: string) =>
  existsSync(caseFile(dir, caseId))
    ? caseFile(dir, caseId)
    : safePath(dir, "cases", `${checkedId(caseId)}.yml`);

/** §5.2: case ID はファイル名 / sheet 名になるので [A-Za-z0-9_-] のみ。 */
export function isValidCaseId(id: string): boolean {
  return /^[A-Za-z0-9_-]+$/.test(id);
}

export function sha256(data: Buffer | string): string {
  return createHash("sha256").update(data).digest("hex");
}

/** §5.2: date / capturedAt は必ず引用符付き文字列で書き出す。 */
function toYaml(value: unknown): string {
  const doc = new YAML.Document(value);
  YAML.visit(doc, {
    Pair(_key, pair) {
      const key = (pair.key as { value?: unknown } | null)?.value;
      if (
        (key === "date" || key === "capturedAt") &&
        YAML.isScalar(pair.value)
      ) {
        pair.value.type = "QUOTE_DOUBLE";
      }
    },
  });
  return doc.toString();
}

export function loadProject(dir: string): Project {
  const raw = YAML.parse(readFileSync(projectFile(dir), "utf8"));
  return ProjectSchema.parse(raw ?? {});
}

export function saveProject(dir: string, project: Project): void {
  atomicWrite(projectFile(dir), toYaml(ProjectSchema.parse(project)));
}

export function listCaseIds(dir: string): string[] {
  const d = casesDir(dir);
  if (!existsSync(d)) return [];
  const ids = readdirSync(d)
    .filter((f) => f.endsWith(".yaml") || f.endsWith(".yml"))
    .map((f) => f.replace(/\.(yaml|yml)$/, ""))
    .sort();
  if (new Set(ids).size !== ids.length)
    throw new Error("同じ ID の .yaml と .yml が存在します");
  return ids;
}

export function loadCase(dir: string, caseId: string): TestCase {
  const file = existingCaseFile(dir, caseId);
  const raw = YAML.parse(readFileSync(file, "utf8"));
  const result = TestCaseSchema.parse(raw ?? {});
  if (result.id !== caseId)
    throw new Error(`${caseId}: ファイル名と case id が一致しません`);
  return result;
}

export function loadCases(dir: string): TestCase[] {
  return listCaseIds(dir).map((id) => loadCase(dir, id));
}

/** Preserve existing hashes so metadata edits cannot silently bless modified evidence. */
export function saveCase(dir: string, testCase: TestCase): TestCase {
  const parsed = TestCaseSchema.parse(testCase);
  const withHash: TestCase = {
    ...parsed,
    evidence: parsed.evidence.map((e) => {
      const path = evidencePath(dir, parsed.id, e.file);
      if (!existsSync(path)) return e;
      return { ...e, sha256: e.sha256 || sha256(readFileSync(path)) };
    }),
  };
  mkdirSync(casesDir(dir), { recursive: true });
  const path = existingCaseFile(dir, parsed.id);
  atomicWrite(
    existsSync(path) ? path : caseFile(dir, parsed.id),
    toYaml(withHash),
  );
  return withHash;
}

/**
 * §5.2: 採番は「現存する最大番号 + 1」。欠番は詰めない、既存 ID は振り直さない。
 * 2 桁ゼロ埋め、99 を超えたら 3 桁。
 */
export function nextEvidenceId(evidence: Evidence[]): string {
  let max = 0;
  for (const e of evidence) {
    const m = /^E(\d+)$/.exec(e.id);
    if (m) max = Math.max(max, Number(m[1]));
  }
  const n = max + 1;
  return `E${String(n).padStart(n > 99 ? 3 : 2, "0")}`;
}

export interface NewEvidence {
  kind: Kind;
  category: Category;
  caption?: string;
  step?: number | null;
  source?: string;
  note?: string;
  lang?: Lang;
  capturedAt?: string;
  /** kind=file のとき保持する元のファイル名。 */
  originalName?: string;
  /** kind=image / file はバイナリ、table / text は UTF-8 文字列。 */
  data: Buffer | string;
  /** ファイル名の拡張子 (省略時は kind から決める)。 */
  extension?: string;
}

const DEFAULT_EXT: Record<Kind, string> = {
  image: "png",
  table: "csv",
  text: "txt",
  file: "bin",
};

/**
 * エビデンスファイルを evidence/<caseId>/ に書き、case に追記して保存する。
 * ファイル名は <id>.<ext>、kind=file だけ <id>-<originalName> (§5.2)。
 */
export function addEvidenceFile(
  dir: string,
  testCase: TestCase,
  input: NewEvidence,
): TestCase {
  const next = Math.max(
    Number(nextEvidenceId(testCase.evidence).slice(1)),
    testCase.nextEvidenceNumber ?? 1,
  );
  const id = `E${String(next).padStart(2, "0")}`;
  const ext = (input.extension ?? DEFAULT_EXT[input.kind]).replace(/^\./, "");
  if (!/^[a-zA-Z0-9]+$/.test(ext)) throw new Error("不正な拡張子です");
  const originalName = input.originalName
    ? basename(input.originalName.replace(/\\/g, "/"))
        .replace(/[\x00-\x1f<>:"|?*]/g, "_")
        .replace(/[. ]+$/, "")
        .slice(0, 180)
    : "";
  const file =
    input.kind === "file" && originalName
      ? `${id}-${originalName}`
      : `${id}.${ext}`;

  const target = evidenceDir(dir, testCase.id);
  mkdirSync(target, { recursive: true });
  // §5.2: text は改行を \n に統一して UTF-8 のまま保存する。
  const body =
    typeof input.data === "string"
      ? Buffer.from(input.data.replace(/\r\n?/g, "\n"), "utf8")
      : input.data;
  const path = evidencePath(dir, testCase.id, file);
  if (existsSync(path))
    throw new Error(`${file} が既に存在します。証拠 ID を再利用できません`);

  const evidence: Evidence = {
    id,
    kind: input.kind,
    category: input.category,
    caption: input.caption ?? "",
    step: input.step ?? null,
    file,
    capturedAt: input.capturedAt ?? "",
    source: input.source ?? "",
    note: input.note ?? "",
    sha256: sha256(body),
    lang: input.lang ?? "",
    originalName,
    size: body.byteLength,
  };
  const updated = TestCaseSchema.parse({
    ...testCase,
    nextEvidenceNumber: next + 1,
    evidence: [...testCase.evidence, evidence],
  });
  atomicWrite(path, body);
  return saveCase(dir, updated);
}
