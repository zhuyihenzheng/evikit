import { Plus, ArrowUp, ArrowDown, Trash2, Paperclip } from "lucide-react";
import type { TestCase } from "../../core/types";
export default function StepTable({
  value,
  selected,
  verdicts,
  onSelect,
  onChange,
}: {
  value: TestCase;
  selected: number | null;
  verdicts: string[];
  onSelect: (n: number) => void;
  onChange: (v: TestCase) => void;
}) {
  const update = (index: number, field: string, text: string) =>
    onChange({
      ...value,
      steps: value.steps.map((s, i) =>
        i === index ? { ...s, [field]: text } : s,
      ),
    });
  const move = (index: number, delta: number) => {
    const steps = [...value.steps];
    [steps[index], steps[index + delta]] = [
      steps[index + delta]!,
      steps[index]!,
    ];
    const numbering = new Map(steps.map((s, i) => [s.no, i + 1]));
    onChange({
      ...value,
      steps: steps.map((s, i) => ({ ...s, no: i + 1 })),
      evidence: value.evidence.map((e) => ({
        ...e,
        step: e.step === null ? null : numbering.get(e.step)!,
      })),
    });
    if (selected !== null) onSelect(numbering.get(selected)!);
  };
  const remove = (no: number) => {
    onChange({
      ...value,
      steps: value.steps.filter((s) => s.no !== no),
      evidence: value.evidence.map((e) =>
        e.step === no ? { ...e, step: null } : e,
      ),
    });
  };
  return (
    <section aria-labelledby="steps-title" className="steps-section">
      <div className="section-heading">
        <h2 id="steps-title">
          テストステップ <span>{value.steps.length} ステップ</span>
        </h2>
        <button
          className="text-button"
          onClick={() => {
            const no = Math.max(0, ...value.steps.map((s) => s.no)) + 1;
            onChange({
              ...value,
              steps: [
                ...value.steps,
                { no, action: "", expected: "", actual: "", verdict: "未実施" },
              ],
            });
            onSelect(no);
          }}
        >
          <Plus size={17} />
          ステップを追加
        </button>
      </div>
      <div className="table-scroll">
        <table className="steps-table">
          <thead>
            <tr>
              <th>No.</th>
              <th>操作</th>
              <th>期待結果</th>
              <th>実際結果</th>
              <th>判定</th>
              <th>
                <span className="sr-only">並べ替え・削除</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {value.steps.map((step, index) => (
              <tr
                key={step.no}
                className={selected === step.no ? "selected" : ""}
                onFocus={() => onSelect(step.no)}
                onClick={() => onSelect(step.no)}
              >
                <td>
                  <button
                    className="step-number"
                    aria-label={`Step ${step.no} を選択`}
                    onClick={() => onSelect(step.no)}
                  >
                    {step.no}
                  </button>
                  {value.evidence.some((e) => e.step === step.no) ? (
                    <span
                      className="step-evidence-count"
                      title="関連エビデンス"
                    >
                      <Paperclip size={11} />
                      {value.evidence.filter((e) => e.step === step.no).length}
                    </span>
                  ) : null}
                </td>
                {(["action", "expected", "actual"] as const).map((key) => (
                  <td key={key}>
                    <textarea
                      aria-label={`Step ${step.no} ${key === "action" ? "操作" : key === "expected" ? "期待結果" : "実際結果"}`}
                      rows={3}
                      value={step[key]}
                      placeholder={
                        key === "action"
                          ? "操作を入力"
                          : key === "expected"
                            ? "期待する結果"
                            : "確認した結果"
                      }
                      onChange={(e) => update(index, key, e.target.value)}
                    />
                  </td>
                ))}
                <td>
                  <select
                    className={`verdict-select ${step.verdict === "OK" ? "ok" : step.verdict === "NG" ? "ng" : ""}`}
                    aria-label={`Step ${step.no} 判定`}
                    value={step.verdict || "未実施"}
                    onChange={(e) => update(index, "verdict", e.target.value)}
                  >
                    {[...new Set([...verdicts, step.verdict || "未実施"])].map(
                      (v) => (
                        <option key={v}>{v}</option>
                      ),
                    )}
                  </select>
                </td>
                <td className="step-actions">
                  <button
                    className="icon-button"
                    disabled={!index}
                    aria-label={`Step ${step.no} を上へ`}
                    onClick={(e) => {
                      e.stopPropagation();
                      move(index, -1);
                    }}
                  >
                    <ArrowUp size={13} />
                  </button>
                  <button
                    className="icon-button"
                    disabled={index === value.steps.length - 1}
                    aria-label={`Step ${step.no} を下へ`}
                    onClick={(e) => {
                      e.stopPropagation();
                      move(index, 1);
                    }}
                  >
                    <ArrowDown size={13} />
                  </button>
                  <button
                    className="icon-button"
                    aria-label={`Step ${step.no} を削除`}
                    title="ステップを削除（証拠は共通へ移動）"
                    onClick={(e) => {
                      e.stopPropagation();
                      remove(step.no);
                    }}
                  >
                    <Trash2 size={13} />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!value.steps.length ? (
          <p className="table-empty">
            ステップを追加して、操作・期待結果・実際結果を記録しましょう。
          </p>
        ) : null}
      </div>
    </section>
  );
}
