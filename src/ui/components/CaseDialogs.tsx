import { useState } from "react";
import type { Project, TestCase } from "../../core/types";
import { Field, Modal, ErrorMessage } from "./primitives";
export function CaseDialog({
  value,
  isNew,
  verdicts,
  onSubmit,
  onClose,
}: {
  value: TestCase;
  isNew?: boolean;
  verdicts: string[];
  onSubmit: (v: TestCase) => Promise<void>;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(value);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const update = (key: keyof TestCase, value: string) =>
    setDraft((d) => ({ ...d, [key]: value }));
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await onSubmit(draft);
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      title={isNew ? "新規テストケース" : "ケース情報を編集"}
      onClose={() => !busy && onClose()}
      footer={
        <>
          <button className="button" disabled={busy} onClick={onClose}>
            キャンセル
          </button>
          <button
            className="button primary"
            form="case-form"
            type="submit"
            disabled={busy}
          >
            {busy ? "保存中…" : isNew ? "ケースを作成" : "変更を適用"}
          </button>
        </>
      }
    >
      <form id="case-form" className="form-grid" onSubmit={submit}>
        <Field label="テスト ID">
          <input
            required
            pattern="[A-Za-z0-9_-]+"
            maxLength={80}
            value={draft.id}
            disabled={!isNew}
            onChange={(e) => update("id", e.target.value)}
          />
        </Field>
        <Field label="判定">
          <select
            value={draft.verdict}
            onChange={(e) => update("verdict", e.target.value)}
          >
            <option value="">ステップから自動判定</option>
            {verdicts.map((v) => (
              <option key={v}>{v}</option>
            ))}
          </select>
        </Field>
        <Field label="件名" full>
          <input
            required
            autoFocus
            maxLength={200}
            value={draft.title}
            onChange={(e) => update("title", e.target.value)}
            placeholder="例：ログイン正常系"
          />
        </Field>
        <Field label="実施者">
          <input
            value={draft.tester}
            onChange={(e) => update("tester", e.target.value)}
          />
        </Field>
        <Field label="実施日">
          <input
            type="date"
            value={draft.date}
            onChange={(e) => update("date", e.target.value)}
          />
        </Field>
        <Field label="環境" full>
          <input
            value={draft.env}
            placeholder="例：STG / Windows 11 / Chrome"
            onChange={(e) => update("env", e.target.value)}
          />
        </Field>
        <Field label="前提条件" full>
          <textarea
            rows={3}
            value={draft.precondition}
            onChange={(e) => update("precondition", e.target.value)}
          />
        </Field>
        <Field label="備考" full>
          <textarea
            rows={2}
            value={draft.note}
            onChange={(e) => update("note", e.target.value)}
            placeholder="不具合票番号、補足事項など"
          />
        </Field>
        <div className="full">
          <ErrorMessage error={error} />
        </div>
      </form>
    </Modal>
  );
}
export function SettingsDialog({
  value,
  path,
  onSubmit,
  onClose,
}: {
  value: Project;
  path: string;
  onSubmit: (p: Project) => Promise<void>;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(value);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <Modal
      title="プロジェクト設定"
      onClose={() => !busy && onClose()}
      footer={
        <>
          <button className="button" onClick={onClose} disabled={busy}>
            キャンセル
          </button>
          <button
            className="button primary"
            type="submit"
            form="project-form"
            disabled={busy}
          >
            {busy ? "保存中…" : "設定を保存"}
          </button>
        </>
      }
    >
      <form
        id="project-form"
        className="form-grid"
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          try {
            await onSubmit(draft);
            onClose();
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        <Field label="プロジェクト名" full>
          <input
            autoFocus
            required
            value={draft.name}
            onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          />
        </Field>
        <Field label="既定の実施者">
          <input
            value={draft.tester}
            onChange={(e) => setDraft({ ...draft, tester: e.target.value })}
          />
        </Field>
        <Field label="既定の環境">
          <input
            value={draft.env}
            onChange={(e) => setDraft({ ...draft, env: e.target.value })}
          />
        </Field>
        <p className="form-hint full">
          実施者・環境は新しいケースに適用されます。
        </p>
        <Field label="Excel のテキスト抜粋（行）">
          <input
            type="number"
            min={1}
            max={1000}
            required
            value={draft.excerptLines}
            onChange={(e) =>
              setDraft({ ...draft, excerptLines: Number(e.target.value) })
            }
          />
        </Field>
        <Field label="Excel の画像最大幅（px）">
          <input
            type="number"
            min={100}
            max={2000}
            required
            value={draft.imageMaxWidth}
            onChange={(e) =>
              setDraft({ ...draft, imageMaxWidth: Number(e.target.value) })
            }
          />
        </Field>
        <div className="folder-info full">
          <strong>保存先</strong>
          <code>{path}</code>
          <p>設定・ケース・証拠は、このフォルダに保存されます。</p>
        </div>
        <div className="full">
          <ErrorMessage error={error} />
        </div>
      </form>
    </Modal>
  );
}
