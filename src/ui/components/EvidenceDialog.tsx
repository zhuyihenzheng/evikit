import { useEffect, useState } from "react";
import {
  FileUp,
  Image as ImageIcon,
  Table2,
  FileText,
  WandSparkles,
} from "lucide-react";
import { CATEGORIES, type Category, type Step } from "../../core/types";
import { detectFile, detectText } from "../../core/detect";
import { prepareText } from "../../core/import";
import { request, json } from "../api";
import { ErrorMessage, Field, Modal } from "./primitives";
export interface Incoming {
  file?: File;
  text?: string;
}
export interface EvidenceInput {
  file?: File;
  text?: string;
  metadata: {
    caption: string;
    category: Category;
    step: number | null;
    source: string;
    note: string;
  };
}
export default function EvidenceDialog({
  incoming,
  steps,
  step,
  onSubmit,
  onClose,
}: {
  incoming: Incoming;
  steps: Step[];
  step: number | null;
  onSubmit: (input: EvidenceInput) => Promise<void>;
  onClose: () => void;
}) {
  const [file, setFile] = useState(incoming.file);
  const [text, setText] = useState(incoming.text ?? "");
  const detection = file ? detectFile(file.name, file.size) : detectText(text);
  let kind = detection.kind;
  try {
    if (!file && text.trim()) kind = prepareText(text).kind;
  } catch {}
  const [category, setCategory] = useState<Category>(
    kind === "image"
      ? "画面"
      : kind === "table"
        ? "DB"
        : detection.lang === "log"
          ? "ログ"
          : detection.lang === "json" || detection.lang === "xml"
            ? "API"
            : "その他",
  );
  const [caption, setCaption] = useState(
    file?.name.replace(/\.[^.]+$/, "") ?? "",
  );
  const [source, setSource] = useState(file?.name ?? "");
  const [note, setNote] = useState("");
  const [assigned, setAssigned] = useState(step);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [preview, setPreview] = useState("");
  const [formatLang, setFormatLang] = useState(
    detection.lang === "json" || detection.lang === "xml"
      ? detection.lang
      : "sql",
  );
  useEffect(() => {
    if (file && kind === "image") {
      const url = URL.createObjectURL(file);
      setPreview(url);
      return () => URL.revokeObjectURL(url);
    }
    setPreview("");
  }, [file, kind]);
  const labels = {
    image: "画像",
    table: "表データ",
    text: "テキスト",
    file: "添付ファイル",
  };
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setBusy(true);
    try {
      await onSubmit({
        file,
        text: file ? undefined : text,
        metadata: {
          caption: caption.trim() || file?.name || labels[kind],
          category,
          step: assigned,
          source,
          note,
        },
      });
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      title="エビデンスを追加"
      onClose={() => !busy && onClose()}
      footer={
        <>
          <span className="modal-foot-hint">
            {assigned === null ? "共通" : `Step ${assigned}`} に追加
          </span>
          <button className="button" disabled={busy} onClick={onClose}>
            キャンセル
          </button>
          <button
            className="button primary"
            form="evidence-form"
            type="submit"
            disabled={busy || (!file && !text.trim())}
          >
            {busy ? "追加中…" : "エビデンスを追加"}
          </button>
        </>
      }
    >
      <form id="evidence-form" onSubmit={submit} className="form-grid">
        <div className="import-summary full">
          <span className="import-icon">
            {kind === "image" ? (
              <ImageIcon />
            ) : kind === "table" ? (
              <Table2 />
            ) : (
              <FileText />
            )}
          </span>
          <div>
            <strong>{labels[kind]}</strong>
            <p>
              {file
                ? `${file.name} · ${(file.size / 1024).toFixed(1)} KB`
                : kind === "table"
                  ? "行・列を持つ CSV として保存します"
                  : "貼り付けた内容をテキストとして保存します"}
            </p>
          </div>
          <label className="text-button file-label">
            ファイルを選択
            <input
              type="file"
              aria-label="追加するファイル"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) {
                  setFile(f);
                  setCaption(f.name.replace(/\.[^.]+$/, ""));
                  setSource(f.name);
                  const d = detectFile(f.name, f.size);
                  setCategory(
                    d.kind === "image"
                      ? "画面"
                      : d.kind === "table"
                        ? "DB"
                        : d.lang === "log"
                          ? "ログ"
                          : "その他",
                  );
                }
              }}
            />
          </label>
        </div>
        {preview ? (
          <div className="import-preview full">
            <img src={preview} alt="追加する画像のプレビュー" />
          </div>
        ) : null}
        {!file ? (
          <Field label="貼り付け内容" full>
            <textarea
              className="code-input"
              rows={7}
              value={text}
              autoFocus={!text}
              onChange={(e) => setText(e.target.value)}
              placeholder="ログ、JSON、SQL 結果などを貼り付けてください"
            />
          </Field>
        ) : null}
        {!file && kind === "text" && text ? (
          <div className="format-controls full">
            <select
              aria-label="整形形式"
              value={formatLang}
              onChange={(e) => setFormatLang(e.target.value)}
            >
              <option value="json">JSON</option>
              <option value="xml">XML</option>
              <option value="sql">SQL</option>
            </select>
            <button
              type="button"
              className="text-button"
              disabled={busy}
              onClick={async () => {
                setBusy(true);
                setError("");
                try {
                  const result = await request<{ text: string }>(
                    "/format",
                    json("POST", { text, lang: formatLang }),
                  );
                  setText(result.text);
                } catch (e) {
                  setError((e as Error).message);
                } finally {
                  setBusy(false);
                }
              }}
            >
              <WandSparkles size={15} />
              内容を整形
            </button>
          </div>
        ) : null}
        <Field label="タイトル" full>
          <input
            autoFocus={Boolean(file || text)}
            value={caption}
            maxLength={200}
            onChange={(e) => setCaption(e.target.value)}
            placeholder="例：最終ログイン日時の更新"
          />
        </Field>
        <Field label="カテゴリ">
          <select
            value={category}
            onChange={(e) => setCategory(e.target.value as Category)}
          >
            {CATEGORIES.map((v) => (
              <option key={v}>{v}</option>
            ))}
          </select>
        </Field>
        <Field label="所属ステップ">
          <select
            value={assigned ?? "common"}
            onChange={(e) =>
              setAssigned(
                e.target.value === "common" ? null : Number(e.target.value),
              )
            }
          >
            <option value="common">共通</option>
            {steps.map((s) => (
              <option key={s.no} value={s.no}>
                Step {s.no} — {s.action.slice(0, 25) || "操作未入力"}
              </option>
            ))}
          </select>
        </Field>
        <details className="full metadata-details">
          <summary>出典・確認ポイントを追加</summary>
          <Field label="出典 / SQL / コマンド">
            <textarea
              rows={3}
              value={source}
              onChange={(e) => setSource(e.target.value)}
              placeholder="SQL 文、ログファイル名、取得元など"
            />
          </Field>
          <Field label="確認ポイント">
            <textarea
              rows={2}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="この証拠で確認したこと"
            />
          </Field>
        </details>
        <div className="full">
          <ErrorMessage error={error} />
        </div>
      </form>
    </Modal>
  );
}
