import { randomUUID } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  renameSync,
  rmSync,
} from "node:fs";
import { imageSize } from "image-size";
import { z } from "zod";
import { atomicWrite, safePath } from "./fs";
import {
  AnnotationSchema,
  EvidenceSchema,
  ProjectSchema,
  TestCaseSchema,
  type Evidence,
  type TestCase,
} from "./types";
import {
  addEvidenceFile,
  caseFile,
  evidenceDir,
  evidencePath,
  existingCaseFile,
  listCaseIds,
  loadCase,
  loadCases,
  loadProject,
  nextEvidenceId,
  projectFile,
  saveCase,
  saveProject,
  sha256,
  type NewEvidence,
} from "./store";
import { resolveVerdict } from "./verdict";

export class WorkspaceError extends Error {
  constructor(
    message: string,
    public status = 400,
  ) {
    super(message);
  }
}
export interface CaseDocument {
  data: TestCase;
  revision: string;
}
export const EvidenceMetaSchema = EvidenceSchema.pick({
  caption: true,
  category: true,
  step: true,
  source: true,
  note: true,
});

/** One project per process. The queue also keeps export and mutations from interleaving. */
export class Workspace {
  private tail: Promise<unknown> = Promise.resolve();
  constructor(public readonly dir: string) {
    loadProject(dir);
  }
  serial<T>(work: () => T | Promise<T>): Promise<T> {
    const result = this.tail.then(work);
    this.tail = result.catch(() => {});
    return result;
  }
  project() {
    return {
      data: loadProject(this.dir),
      revision: sha256(readFileSync(projectFile(this.dir))),
      path: this.dir,
    };
  }
  cases() {
    return loadCases(this.dir).map((c) => ({
      ...c,
      resolvedVerdict: resolveVerdict(c),
    }));
  }
  case(id: string): CaseDocument {
    if (!listCaseIds(this.dir).includes(id))
      throw new WorkspaceError("ケースが見つかりません", 404);
    return {
      data: loadCase(this.dir, id),
      revision: sha256(readFileSync(existingCaseFile(this.dir, id))),
    };
  }
  private checked(id: string, revision?: string): TestCase {
    const current = this.case(id);
    this.checkRevision(current.revision, revision);
    return current.data;
  }
  private checkRevision(actual: string, expected?: string) {
    if (!expected)
      throw new WorkspaceError("保存前のリビジョンが必要です", 428);
    if (expected !== actual)
      throw new WorkspaceError(
        "別の画面またはファイルで更新されています。入力を控えてから再読み込みしてください。",
        409,
      );
  }
  updateProject(input: unknown, revision?: string) {
    this.checkRevision(this.project().revision, revision);
    saveProject(this.dir, ProjectSchema.parse(input));
    return this.project();
  }
  createCase(input: unknown) {
    const project = loadProject(this.dir);
    const c = TestCaseSchema.parse({
      tester: project.tester,
      env: project.env,
      ...z.object({}).passthrough().parse(input),
    });
    if (
      listCaseIds(this.dir).some(
        (id) => id.toLowerCase() === c.id.toLowerCase(),
      )
    )
      throw new WorkspaceError("同じ ID のケースが存在します", 409);
    if (c.evidence.length)
      throw new WorkspaceError("新規ケースにファイル参照を指定できません");
    if (!c.title.trim()) throw new WorkspaceError("件名を入力してください");
    saveCase(this.dir, c);
    return this.case(c.id);
  }
  updateCase(id: string, input: unknown, revision?: string) {
    const previous = this.checked(id, revision);
    const c = TestCaseSchema.parse(input);
    if (c.id !== id) throw new WorkspaceError("ケース ID は変更できません");
    if (!c.title.trim()) throw new WorkspaceError("件名を入力してください");
    // File identity and image annotations can only be changed by their dedicated operations.
    const identity = (e: Evidence) =>
      JSON.stringify({
        ...e,
        caption: "",
        category: "その他",
        step: null,
        source: "",
        note: "",
      });
    if (
      c.evidence.length !== previous.evidence.length ||
      c.evidence.some(
        (e) =>
          identity(e) !==
          identity(previous.evidence.find((old) => old.id === e.id)!),
      )
    )
      throw new WorkspaceError(
        "証拠ファイルの変更は追加・削除・注釈操作を使用してください",
      );
    saveCase(this.dir, {
      ...c,
      nextEvidenceNumber: previous.nextEvidenceNumber,
    });
    return this.case(id);
  }
  addEvidence(id: string, input: NewEvidence, revision?: string) {
    const c = this.checked(id, revision);
    if (input.kind === "image") validateImage(Buffer.from(input.data));
    addEvidenceFile(this.dir, c, input);
    return this.case(id);
  }
  updateEvidence(id: string, eid: string, input: unknown, revision?: string) {
    const c = this.checked(id, revision);
    this.findEvidence(c, eid);
    const metadata = EvidenceMetaSchema.parse(input);
    saveCase(this.dir, {
      ...c,
      evidence: c.evidence.map((e) =>
        e.id === eid ? { ...e, ...metadata } : e,
      ),
    });
    return this.case(id);
  }
  findEvidence(c: TestCase, eid: string): Evidence {
    const e = c.evidence.find((e) => e.id === eid);
    if (!e) throw new WorkspaceError("エビデンスが見つかりません", 404);
    return e;
  }
  image(
    id: string,
    eid: string,
    png: Buffer | null,
    annotations: unknown,
    revision?: string,
  ) {
    const c = this.checked(id, revision);
    const e = this.findEvidence(c, eid);
    if (e.kind !== "image") throw new WorkspaceError("画像ではありません");
    const originalFile = e.originalFile ?? e.file;
    const original = readFileSync(evidencePath(this.dir, id, originalFile));
    const originalHash = e.originalSha256 ?? e.sha256;
    if (originalHash && originalHash !== sha256(original))
      throw new WorkspaceError(
        "原画像のハッシュが一致しません。外部での変更を確認してください",
        409,
      );
    let updated: Evidence;
    if (png === null) {
      updated = {
        ...e,
        file: originalFile,
        sha256: sha256(original),
        size: original.length,
      };
      delete updated.originalFile;
      delete updated.originalSha256;
      delete updated.annotations;
    } else {
      const a = AnnotationSchema.parse(annotations);
      const size = validateImage(png);
      if (size.type !== "png")
        throw new WorkspaceError("注釈画像は PNG で保存してください");
      const source = validateImage(original);
      const crop = a.crop ?? { x: 0, y: 0, w: source.width, h: source.height };
      if (
        crop.x + crop.w > source.width ||
        crop.y + crop.h > source.height ||
        size.width !== Math.round(crop.w) ||
        size.height !== Math.round(crop.h)
      )
        throw new WorkspaceError("トリミング範囲と画像サイズが一致しません");
      // Immutable image first, then atomic YAML pointer change. Original bytes never change.
      const file = `${eid}.annotated-${randomUUID().slice(0, 8)}.png`;
      atomicWrite(evidencePath(this.dir, id, file), png);
      updated = {
        ...e,
        file,
        originalFile,
        originalSha256: sha256(original),
        annotations: a,
        sha256: sha256(png),
        size: png.length,
      };
    }
    saveCase(this.dir, {
      ...c,
      evidence: c.evidence.map((item) => (item.id === eid ? updated : item)),
    });
    return this.case(id);
  }
  archive(id: string, eid: string | null, revision?: string) {
    const c = this.checked(id, revision);
    const entry = eid ? this.findEvidence(c, eid) : c;
    const archiveId = randomUUID();
    const path = safePath(this.dir, ".trash", archiveId);
    mkdirSync(path, { recursive: true });
    atomicWrite(
      safePath(this.dir, ".trash", archiveId, "entry.json"),
      JSON.stringify(
        { caseId: id, kind: eid ? "evidence" : "case", entry },
        null,
        2,
      ),
    );
    if (eid) {
      // The bytes stay in evidence/. Only the reference is archived, and IDs are never reused.
      saveCase(this.dir, {
        ...c,
        nextEvidenceNumber: Math.max(
          c.nextEvidenceNumber ?? 1,
          Number(nextEvidenceId(c.evidence).slice(1)),
        ),
        evidence: c.evidence.filter((e) => e.id !== eid),
      });
    } else {
      const source = existingCaseFile(this.dir, id);
      renameSync(source, safePath(this.dir, ".trash", archiveId, "case.yaml"));
      try {
        if (existsSync(evidenceDir(this.dir, id)))
          renameSync(
            evidenceDir(this.dir, id),
            safePath(this.dir, ".trash", archiveId, "evidence"),
          );
      } catch (error) {
        renameSync(
          safePath(this.dir, ".trash", archiveId, "case.yaml"),
          source,
        );
        throw error;
      }
    }
    return { archiveId, ...(eid ? this.case(id) : {}) };
  }
  restore(archiveId: string) {
    if (!/^[a-f0-9-]{36}$/.test(archiveId))
      throw new WorkspaceError("不正な復元 ID です");
    const path = safePath(this.dir, ".trash", archiveId);
    const record = JSON.parse(
      readFileSync(
        safePath(this.dir, ".trash", archiveId, "entry.json"),
        "utf8",
      ),
    );
    const id = z
      .string()
      .regex(/^[A-Za-z0-9_-]+$/)
      .parse(record.caseId);
    if (record.kind === "case") {
      if (
        listCaseIds(this.dir).some(
          (v) => v.toLowerCase() === id.toLowerCase(),
        ) ||
        existsSync(evidenceDir(this.dir, id))
      )
        throw new WorkspaceError("同じ ID があるため復元できません", 409);
      const c = TestCaseSchema.parse(record.entry);
      if (c.id !== id)
        throw new WorkspaceError("復元データの ID が一致しません");
      const files = safePath(this.dir, ".trash", archiveId, "evidence");
      if (existsSync(files)) renameSync(files, evidenceDir(this.dir, id));
      try {
        saveCase(this.dir, c);
      } catch (error) {
        if (existsSync(evidenceDir(this.dir, id)))
          renameSync(evidenceDir(this.dir, id), files);
        throw error;
      }
    } else {
      const c = this.case(id).data;
      const e = EvidenceSchema.parse(record.entry);
      if (c.evidence.some((v) => v.id === e.id))
        throw new WorkspaceError("同じ証拠 ID があるため復元できません", 409);
      if (!c.steps.some((s) => s.no === e.step)) e.step = null;
      saveCase(this.dir, { ...c, evidence: [...c.evidence, e] });
    }
    rmSync(path, { recursive: true });
    return this.case(id);
  }
}

export function validateImage(buffer: Buffer) {
  let result;
  try {
    result = imageSize(buffer);
  } catch {
    throw new WorkspaceError(
      "画像を読み取れません。PNG / JPEG / GIF / WebP を選択してください",
    );
  }
  if (
    !["png", "jpg", "jpeg", "gif", "webp"].includes(result.type ?? "") ||
    result.width * result.height > 40_000_000
  )
    throw new WorkspaceError(
      "画像形式またはサイズに対応していません（最大 4,000 万画素）",
    );
  return result;
}
