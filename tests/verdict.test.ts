// §5.4 判定の導出 / §7.1 判定色
import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { loadCase } from "../src/core/store";
import { StepSchema, type Step } from "../src/core/types";
import {
  countVerdicts,
  deriveVerdict,
  resolveVerdict,
  verdictStyle,
} from "../src/core/verdict";

const SAMPLE = join(import.meta.dir, "..", "examples", "reference");
const steps = (...verdicts: string[]): Step[] =>
  verdicts.map((verdict, i) => StepSchema.parse({ no: i + 1, verdict }));

describe("verdict 導出 (§5.4)", () => {
  test("いずれか NG -> NG", () => {
    expect(deriveVerdict(steps("OK", "NG", "保留"))).toBe("NG");
  });

  test("NG が無く 保留 があれば 保留", () => {
    expect(deriveVerdict(steps("OK", "保留", "未実施"))).toBe("保留");
  });

  test("NG / 保留 が無く 未実施 があれば 未実施", () => {
    expect(deriveVerdict(steps("OK", "未実施", "対象外"))).toBe("未実施");
  });

  test("すべて 対象外 -> 対象外", () => {
    expect(deriveVerdict(steps("対象外", "対象外"))).toBe("対象外");
  });

  test("それ以外は OK（対象外 が混ざっても OK）", () => {
    expect(deriveVerdict(steps("OK", "OK"))).toBe("OK");
    expect(deriveVerdict(steps("OK", "対象外"))).toBe("OK");
  });

  test("ステップ無し -> 未実施", () => {
    expect(deriveVerdict([])).toBe("未実施");
  });

  test("case.verdict が入っていればそれを優先", () => {
    expect(
      resolveVerdict({ verdict: "保留", steps: steps("NG") } as never),
    ).toBe("保留");
    expect(resolveVerdict({ verdict: "", steps: steps("NG") } as never)).toBe(
      "NG",
    );
  });
});

describe("サンプルの判定", () => {
  test("TC-001 OK / TC-002 NG (導出) / TC-003 OK (step3 対象外)", () => {
    expect(resolveVerdict(loadCase(SAMPLE, "TC-001"))).toBe("OK");
    expect(resolveVerdict(loadCase(SAMPLE, "TC-002"))).toBe("NG");
    expect(resolveVerdict(loadCase(SAMPLE, "TC-003"))).toBe("OK");
  });

  test("件数集計", () => {
    const cases = ["TC-001", "TC-002", "TC-003"].map((id) =>
      loadCase(SAMPLE, id),
    );
    expect(
      countVerdicts(cases, ["OK", "NG", "保留", "対象外", "未実施"]),
    ).toEqual([
      { verdict: "OK", count: 2 },
      { verdict: "NG", count: 1 },
      { verdict: "保留", count: 0 },
      { verdict: "対象外", count: 0 },
      { verdict: "未実施", count: 0 },
    ]);
  });
});

describe("判定色 (§7.1)", () => {
  test("色テーブル", () => {
    expect(verdictStyle("OK")).toEqual({ fill: "C6EFCE", font: "006100" });
    expect(verdictStyle("NG")).toEqual({ fill: "FFC7CE", font: "9C0006" });
    expect(verdictStyle("保留")).toEqual({ fill: "FFEB9C", font: "9C5700" });
    expect(verdictStyle("対象外")).toEqual({ fill: "E7E6E6", font: "595959" });
    expect(verdictStyle("未実施")).toEqual({ fill: null, font: null });
    expect(verdictStyle("なにか")).toEqual({ fill: null, font: null });
  });
});
