import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import {
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";
import sharp from "sharp";
import ExcelJS from "exceljs";
import JSZip from "jszip";
import { Workspace } from "../src/core/workspace";
import { ProjectSchema, TestCaseSchema, StepSchema } from "../src/core/types";
import {
  caseFile,
  evidencePath,
  loadCase,
  saveProject,
  sha256,
} from "../src/core/store";
import { deriveVerdict } from "../src/core/verdict";
import { normalizeTable, prepareText, terminalTable } from "../src/core/import";
import { exportProject } from "../src/export";
let dir: string;
let workspace: Workspace;
beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), "evikit-workspace-"));
  saveProject(
    dir,
    ProjectSchema.parse({ name: "テスト成果物", tester: "担当 A", env: "STG" }),
  );
  workspace = new Workspace(dir);
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));
const newCase = () =>
  workspace.createCase({
    id: "TC-001",
    title: "保存の確認",
    steps: [{ no: 1 }, { no: 2, verdict: "OK" }],
  });

describe("workspace persistence", () => {
  test("native step conditions survive browser metadata edits and YAML reload", () => {
    const initial = workspace.createCase({ id: "TC-COND", title: "条件を保持", steps: [{ no: 1, condition: "ID：00012\n残高：0" }] });
    const saved = workspace.updateCase(initial.data.id, { ...initial.data, title: "編集後" }, initial.revision);
    expect(saved.data.steps[0]!.condition).toBe("ID：00012\n残高：0");
    expect(loadCase(dir, initial.data.id).steps[0]!.condition).toBe("ID：00012\n残高：0");
  });
  test("new cases inherit project defaults, and blank verdict stays 未実施", () => {
    const c = newCase();
    expect(c.data.tester).toBe("担当 A");
    expect(c.data.env).toBe("STG");
    expect(deriveVerdict(c.data.steps)).toBe("未実施");
    expect(deriveVerdict([StepSchema.parse({ no: 1, verdict: "  " })])).toBe(
      "未実施",
    );
  });
  test("metadata save and restart roundtrip with optimistic conflict rejection", () => {
    const first = newCase();
    const second = workspace.updateCase(
      first.data.id,
      { ...first.data, note: "BUG-9", date: "2026-09-05" },
      first.revision,
    );
    expect(second.revision).not.toBe(first.revision);
    expect(() =>
      workspace.updateCase(first.data.id, first.data, first.revision),
    ).toThrow("更新されています");
    expect(new Workspace(dir).case(first.data.id).data.note).toBe("BUG-9");
    expect(readFileSync(caseFile(dir, first.data.id), "utf8")).toContain(
      'date: "2026-09-05"',
    );
    expect(
      readdirSync(join(dir, "cases")).filter((f) => f.endsWith(".tmp")),
    ).toEqual([]);
  });
  test("external YAML edits are detected, missing revisions are rejected", () => {
    const c = newCase();
    writeFileSync(
      caseFile(dir, c.data.id),
      readFileSync(caseFile(dir, c.data.id), "utf8") + "\n# external\n",
    );
    expect(() => workspace.updateCase(c.data.id, c.data, c.revision)).toThrow(
      "更新されています",
    );
    expect(() => workspace.updateCase(c.data.id, c.data)).toThrow("リビジョン");
  });
  test("case IDs cannot collide by Windows casing", () => {
    newCase();
    expect(() =>
      workspace.createCase({ id: "tc-001", title: "duplicate" }),
    ).toThrow("同じ ID");
  });
  test("YML files retain their extension, without duplicate cases", () => {
    mkdirSync(join(dir, "cases"));
    writeFileSync(join(dir, "cases", "TC-9.yml"), "id: TC-9\ntitle: yml\n");
    const c = workspace.case("TC-9");
    workspace.updateCase("TC-9", { ...c.data, title: "更新" }, c.revision);
    expect(workspace.cases().length).toBe(1);
    expect(readdirSync(join(dir, "cases"))).toEqual(["TC-9.yml"]);
  });
  test("cannot attach evidence to a missing step or duplicate step numbers", () => {
    const c = newCase();
    expect(() =>
      workspace.addEvidence(
        c.data.id,
        { kind: "text", category: "ログ", step: 5, data: "log" },
        c.revision,
      ),
    ).toThrow("所属ステップ");
    expect(() =>
      TestCaseSchema.parse({ id: "TC", steps: [{ no: 1 }, { no: 1 }] }),
    ).toThrow("重複");
  });
  test("archives survive restart, IDs are never reused, undo preserves later edits", () => {
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "text", category: "ログ", step: 2, data: "first\r\nsecond" },
      c.revision,
    );
    const hash = c.data.evidence[0]!.sha256;
    const deleted = workspace.archive(c.data.id, "E01", c.revision);
    c = workspace.case(c.data.id);
    c = workspace.addEvidence(
      c.data.id,
      { kind: "text", category: "ログ", data: "new" },
      c.revision,
    );
    expect(c.data.evidence[0]!.id).toBe("E02");
    c = workspace.updateCase(
      c.data.id,
      { ...c.data, note: "keep later edits", steps: c.data.steps.slice(0, 1) },
      c.revision,
    );
    c = new Workspace(dir).restore(deleted.archiveId);
    expect(c.data.note).toBe("keep later edits");
    expect(c.data.evidence.find((e) => e.id === "E01")?.step).toBeNull();
    expect(c.data.evidence.find((e) => e.id === "E01")?.sha256).toBe(hash);
    expect(readFileSync(evidencePath(dir, c.data.id, "E01.txt"), "utf8")).toBe(
      "first\nsecond",
    );
  });
  test("case archive restores metadata and evidence together", () => {
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      {
        kind: "file",
        category: "その他",
        originalName: "sample.bin",
        data: Buffer.from([0, 1, 2]),
      },
      c.revision,
    );
    const archived = workspace.archive(c.data.id, null, c.revision);
    expect(workspace.cases()).toEqual([]);
    const restored = new Workspace(dir).restore(archived.archiveId);
    expect(restored.data).toEqual(c.data);
    expect(
      readFileSync(evidencePath(dir, c.data.id, "E01-sample.bin")),
    ).toEqual(Buffer.from([0, 1, 2]));
  });
  test("protects evidence identity, hashes, annotation fields, and filenames from generic PUT", () => {
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "text", category: "ログ", data: "log" },
      c.revision,
    );
    expect(() =>
      workspace.updateCase(
        c.data.id,
        { ...c.data, evidence: [{ ...c.data.evidence[0], file: "other.txt" }] },
        c.revision,
      ),
    ).toThrow("証拠ファイル");
    expect(() =>
      workspace.updateCase(c.data.id, { ...c.data, evidence: [] }, c.revision),
    ).toThrow("証拠ファイル");
    expect(() =>
      TestCaseSchema.parse({
        ...c.data,
        evidence: [{ ...c.data.evidence[0], file: "../project.yaml" }],
      }),
    ).toThrow("ファイル名");
  });
  test("rejects both file and directory symlink escapes", () => {
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "text", category: "ログ", data: "log" },
      c.revision,
    );
    rmSync(evidencePath(dir, c.data.id, "E01.txt"));
    symlinkSync(
      join(dir, "project.yaml"),
      join(dir, "evidence", c.data.id, "E01.txt"),
    );
    expect(() => evidencePath(dir, c.data.id, "E01.txt")).toThrow(
      "シンボリックリンク",
    );
    rmSync(join(dir, "evidence", c.data.id), { recursive: true });
    symlinkSync(join(dir, "cases"), join(dir, "evidence", c.data.id));
    expect(() => evidencePath(dir, c.data.id, "anything")).toThrow(
      "シンボリックリンク",
    );
  });
  test("concurrent operations serialize and stale updates lose safely", async () => {
    const c = newCase();
    const results = await Promise.allSettled([
      workspace.serial(() =>
        workspace.updateCase(
          c.data.id,
          { ...c.data, note: "winner" },
          c.revision,
        ),
      ),
      workspace.serial(() =>
        workspace.updateCase(
          c.data.id,
          { ...c.data, note: "loser" },
          c.revision,
        ),
      ),
    ]);
    expect(results[0]!.status).toBe("fulfilled");
    expect(results[1]!.status).toBe("rejected");
    expect(workspace.case(c.data.id).data.note).toBe("winner");
  });
});

describe("structured imports", () => {
  test("CSV quoting, leading zeros, BOM, empty cells and CRLF roundtrip", () => {
    expect(normalizeTable('\uFEFFid,name\r\n00012,"a,b"\r\n00013,')).toBe(
      'id,name\n00012,"a,b"\n00013,',
    );
    expect(prepareText("id\tcount\r\n00012\t0001").text).toBe(
      "id,count\n00012,0001",
    );
    expect(prepareText("id;name\n00012;顧客", "data.csv").text).toBe(
      "id,name\n00012,顧客",
    );
  });
  test("terminal psql/mysql tables become structured data but ordinary comma logs do not", () => {
    const mysql =
      "+-------+------+\n| id    | name |\n+-------+------+\n| 00012 | Test |\n+-------+------+\n1 row in set";
    expect(prepareText(mysql).text).toBe("id,name\n00012,Test");
    expect(prepareText(mysql).kind).toBe("table");
    expect(
      terminalTable(" id | name\n----+-----\n 00012 | Test\n(1 row)"),
    ).toEqual([
      ["id", "name"],
      ["00012", "Test"],
    ]);
    expect(prepareText("INFO,started\nWARN,slow").kind).toBe("text");
  });
  test("malformed and overlarge structured input fails explicitly", () => {
    expect(() => normalizeTable("a,b\n1,2,3")).toThrow("列数");
    expect(() => prepareText("x".repeat(2 * 1024 * 1024 + 1))).toThrow("2 MB");
  });
});

describe("image and delivery integrity", () => {
  test("annotations preserve original bytes, survive restart, and reset exactly", async () => {
    const original = await sharp({
      create: { width: 120, height: 80, channels: 3, background: "#efefef" },
    })
      .jpeg()
      .toBuffer();
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "image", category: "画面", data: original, extension: "jpg" },
      c.revision,
    );
    const png = await sharp(original)
      .extract({ left: 10, top: 10, width: 60, height: 40 })
      .png()
      .toBuffer();
    c = workspace.image(
      c.data.id,
      "E01",
      png,
      {
        crop: { x: 10, y: 10, w: 60, h: 40 },
        shapes: [{ type: "number", x: 30, y: 20, n: 1, color: "red" }],
      },
      c.revision,
    );
    expect(c.data.evidence[0]!.originalFile).toBe("E01.jpg");
    expect(readFileSync(evidencePath(dir, c.data.id, "E01.jpg"))).toEqual(
      original,
    );
    expect(c.data.evidence[0]!.sha256).toBe(sha256(png));
    expect(
      new Workspace(dir).case(c.data.id).data.evidence[0]!.annotations?.crop?.w,
    ).toBe(60);
    c = workspace.image(c.data.id, "E01", null, {}, c.revision);
    expect(c.data.evidence[0]!.file).toBe("E01.jpg");
    expect(c.data.evidence[0]!.annotations).toBeUndefined();
  });
  test("missing or modified evidence blocks export without publishing a partial directory", async () => {
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "text", category: "ログ", data: "original" },
      c.revision,
    );
    writeFileSync(evidencePath(dir, c.data.id, "E01.txt"), "modified");
    workspace.updateCase(
      c.data.id,
      { ...c.data, note: "metadata cannot bless tampering" },
      c.revision,
    );
    expect(workspace.case(c.data.id).data.evidence[0]!.sha256).toBe(
      sha256("original"),
    );
    await expect(exportProject(dir)).rejects.toThrow("ハッシュ");
    expect(readdirSync(join(dir, "exports"))).toEqual([]);
    rmSync(evidencePath(dir, c.data.id, "E01.txt"));
    await expect(exportProject(dir)).rejects.toThrow("見つかりません");
  });
  test("WebP exports as PNG without changing the original, ZIP manifest verifies every file", async () => {
    const webp = await sharp({
      create: { width: 160, height: 100, channels: 3, background: "#abcdef" },
    })
      .webp()
      .toBuffer();
    let c = newCase();
    c = workspace.addEvidence(
      c.data.id,
      { kind: "image", category: "画面", data: webp, extension: "webp" },
      c.revision,
    );
    c = workspace.addEvidence(
      c.data.id,
      { kind: "table", category: "DB", data: "id,value\n00012,=1+1", step: 1 },
      c.revision,
    );
    const result = await exportProject(dir);
    const zip = await JSZip.loadAsync(readFileSync(result.zipPath));
    const manifest = JSON.parse(
      await zip.file("manifest.json")!.async("string"),
    );
    for (const entry of manifest.files)
      expect(sha256(await zip.file(entry.file)!.async("nodebuffer"))).toBe(
        entry.sha256,
      );
    expect(loadCase(dir, c.data.id).evidence[0]!.file).toBe("E01.webp");
    expect(readFileSync(evidencePath(dir, c.data.id, "E01.webp"))).toEqual(
      webp,
    );
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.readFile(result.xlsxPath);
    expect(wb.getWorksheet(c.data.id)!.getImages().length).toBe(1);
    const values: unknown[] = [];
    wb.getWorksheet(c.data.id)!.eachRow((r) =>
      r.eachCell((cell) => values.push(cell.value)),
    );
    expect(values).toContain("00012");
    expect(values).toContain("=1+1");
    expect(readFileSync(result.htmlPath, "utf8")).toContain(
      "data:image/png;base64,",
    );
  });
});
