// §5.4 判定の導出 / §7.1 判定色テーブル
import type { Step, TestCase } from "./types";

export const DEFAULT_VERDICTS = [
  "OK",
  "NG",
  "保留",
  "対象外",
  "未実施",
] as const;

export interface VerdictStyle {
  /** ARGB なしの 6 桁。無地の場合は null。 */
  fill: string | null;
  font: string | null;
}

/** §7.1 判定色。未実施は塗りなし。 */
export const VERDICT_STYLES: Record<string, VerdictStyle> = {
  OK: { fill: "C6EFCE", font: "006100" },
  NG: { fill: "FFC7CE", font: "9C0006" },
  保留: { fill: "FFEB9C", font: "9C5700" },
  対象外: { fill: "E7E6E6", font: "595959" },
  未実施: { fill: null, font: null },
};

export function verdictStyle(verdict: string): VerdictStyle {
  return VERDICT_STYLES[verdict] ?? { fill: null, font: null };
}

/**
 * §5.4: いずれかのステップが NG → NG。次に 保留、次に 未実施。
 * 全て 対象外 なら 対象外。それ以外は OK。ステップが無ければ 未実施。
 */
export function deriveVerdict(steps: Step[]): string {
  if (steps.length === 0) return "未実施";
  const vs = steps.map((s) => s.verdict.trim() || "未実施");
  if (vs.includes("NG")) return "NG";
  if (vs.includes("保留")) return "保留";
  if (vs.includes("未実施")) return "未実施";
  if (vs.every((v) => v === "対象外")) return "対象外";
  return "OK";
}

/** case.verdict が空のときだけ導出する。 */
export function resolveVerdict(testCase: TestCase): string {
  return testCase.verdict !== ""
    ? testCase.verdict
    : deriveVerdict(testCase.steps);
}

/** project.verdicts の順で件数を数える。未知の判定は集計しない。 */
export function countVerdicts(
  cases: TestCase[],
  verdicts: string[],
): { verdict: string; count: number }[] {
  return verdicts.map((v) => ({
    verdict: v,
    count: cases.filter((c) => resolveVerdict(c) === v).length,
  }));
}
