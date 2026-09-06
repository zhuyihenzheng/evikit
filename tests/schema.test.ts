// §11: 示例项目の 3 case が zod 校验を通ること、date が文字列であること
import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import {
  listCaseIds,
  loadCase,
  loadProject,
  nextEvidenceId,
} from "../src/core/store";
import { EvidenceSchema, TestCaseSchema } from "../src/core/types";

const SAMPLE = join(import.meta.dir, "..", "examples", "reference");

describe("schema", () => {
  test("サンプルの project.yaml が読める", () => {
    const project = loadProject(SAMPLE);
    expect(project.name).toBe("顧客管理システム 結合テスト");
    expect(project.tester).toBe("王");
    expect(project.env).toBe("STG");
    expect(project.verdicts).toEqual(["OK", "NG", "保留", "対象外", "未実施"]);
    expect(project.excerptLines).toBe(30);
    expect(project.imageMaxWidth).toBe(640);
  });

  test("サンプルの 3 case がすべて zod 校验を通る", () => {
    const ids = listCaseIds(SAMPLE);
    expect(ids).toEqual(["TC-001", "TC-002", "TC-003"]);
    for (const id of ids) {
      const testCase = loadCase(SAMPLE, id);
      expect(TestCaseSchema.safeParse(testCase).success).toBe(true);
    }
  });

  test("date と capturedAt は文字列", () => {
    const testCase = loadCase(SAMPLE, "TC-001");
    expect(typeof testCase.date).toBe("string");
    expect(testCase.date).toBe("2026-09-04");
    for (const e of testCase.evidence)
      expect(typeof e.capturedAt).toBe("string");
  });

  test("4 つの kind がサンプルに揃っている", () => {
    const kinds = new Set(
      listCaseIds(SAMPLE).flatMap((id) =>
        loadCase(SAMPLE, id).evidence.map((e) => e.kind),
      ),
    );
    expect([...kinds].sort()).toEqual(["file", "image", "table", "text"]);
  });

  test("sha256 が保存済み、kind=file は size を持つ", () => {
    const testCase = loadCase(SAMPLE, "TC-003");
    for (const e of testCase.evidence)
      expect(e.sha256).toMatch(/^[0-9a-f]{64}$/);
    const attachment = testCase.evidence.find((e) => e.kind === "file")!;
    expect(attachment.originalName).toBe("customers.zip");
    expect(attachment.size).toBeGreaterThan(0);
  });

  test("空欄 (null) は空文字に寄せる / step は null 可", () => {
    const parsed = EvidenceSchema.parse({
      id: "E01",
      kind: "text",
      category: "その他",
      file: "E01.txt",
      caption: null,
      note: null,
      source: null,
      step: null,
    });
    expect(parsed.note).toBe("");
    expect(parsed.step).toBeNull();
  });

  test("case id は [A-Za-z0-9_-] のみ", () => {
    expect(TestCaseSchema.safeParse({ id: "TC/001" }).success).toBe(false);
    expect(TestCaseSchema.safeParse({ id: "TC-001" }).success).toBe(true);
  });

  test("§5.2 ID 採番は最大 + 1、欠番は詰めない", () => {
    const ev = (id: string) =>
      EvidenceSchema.parse({
        id,
        kind: "text",
        category: "その他",
        file: `${id}.txt`,
      });
    expect(nextEvidenceId([])).toBe("E01");
    expect(nextEvidenceId([ev("E01"), ev("E03")])).toBe("E04");
    expect(nextEvidenceId([ev("E99")])).toBe("E100");
  });
});
