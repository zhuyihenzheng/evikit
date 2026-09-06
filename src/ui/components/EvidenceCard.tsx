import { useEffect, useState } from "react";
import {
  Image as ImageIcon,
  Table2,
  FileText,
  Paperclip,
  Pencil,
  Trash2,
  Download,
  Search,
} from "lucide-react";
import {
  CATEGORIES,
  type Category,
  type Evidence,
  type Step,
} from "../../core/types";
import { rawUrl, request } from "../api";
interface Preview {
  rows?: string[][];
  text?: string;
  total: number;
}
const ICONS = {
  image: ImageIcon,
  table: Table2,
  text: FileText,
  file: Paperclip,
};
export default function EvidenceCard({
  evidence: e,
  caseId,
  steps,
  onChange,
  onAnnotate,
  onDelete,
}: {
  evidence: Evidence;
  caseId: string;
  steps: Step[];
  onChange: (e: Evidence) => void;
  onAnnotate: () => void;
  onDelete: () => void;
}) {
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState("");
  const [keyword, setKeyword] = useState("");
  const [expanded, setExpanded] = useState(false);
  useEffect(() => {
    setPreview(null);
    setError("");
    let active = true;
    if (e.kind === "text" || e.kind === "table")
      request<Preview>(`/cases/${caseId}/evidence/${e.id}/preview`)
        .then((data) => {
          if (active) setPreview(data);
        })
        .catch((error) => {
          if (active) setError(error.message);
        });
    return () => {
      active = false;
    };
  }, [caseId, e.id, e.kind, e.sha256]);
  const Icon = ICONS[e.kind];
  const url = rawUrl(caseId, e.id, e.sha256);
  const lines = preview?.text?.split("\n") ?? [];
  function highlight(value: string) {
    if (!keyword) return value;
    const index = value.toLowerCase().indexOf(keyword.toLowerCase());
    return index < 0 ? (
      value
    ) : (
      <>
        {value.slice(0, index)}
        <mark>{value.slice(index, index + keyword.length)}</mark>
        {value.slice(index + keyword.length)}
      </>
    );
  }
  return (
    <article
      className={`evidence-card evidence-${e.kind}`}
      id={`evidence-${e.id}`}
      aria-label={`${e.id} ${e.caption}`}
    >
      <div className="evidence-head">
        <Icon size={18} />
        <span className="evidence-id">{e.id}</span>
        <select
          className="inline-select category-select"
          aria-label={`${e.id} カテゴリ`}
          value={e.category}
          onChange={(event) =>
            onChange({ ...e, category: event.target.value as Category })
          }
        >
          {CATEGORIES.map((v) => (
            <option key={v}>{v}</option>
          ))}
        </select>
        <input
          className="caption-input"
          aria-label={`${e.id} タイトル`}
          value={e.caption}
          placeholder="タイトルを入力"
          onChange={(event) => onChange({ ...e, caption: event.target.value })}
        />
        <select
          className="inline-select assignment-select"
          aria-label={`${e.id} 所属ステップ`}
          value={e.step ?? "common"}
          onChange={(event) =>
            onChange({
              ...e,
              step:
                event.target.value === "common"
                  ? null
                  : Number(event.target.value),
            })
          }
        >
          <option value="common">共通</option>
          {steps.map((s) => (
            <option value={s.no} key={s.no}>
              Step {s.no}
            </option>
          ))}
        </select>
        <time title={e.capturedAt}>{e.capturedAt.slice(11, 16)}</time>
        {e.kind === "image" ? (
          <button className="text-button annotate-button" onClick={onAnnotate}>
            <Pencil size={14} />
            注釈を編集
          </button>
        ) : null}
        <button
          className="icon-button"
          aria-label={`${e.id} を削除`}
          title="削除（元に戻せます）"
          onClick={onDelete}
        >
          <Trash2 size={15} />
        </button>
      </div>
      <div className="evidence-body">
        <div className="evidence-preview">
          {e.kind === "image" ? (
            <button
              className="image-preview"
              onClick={onAnnotate}
              aria-label={`${e.id} の画像を注釈編集`}
            >
              <img
                loading="lazy"
                src={url}
                alt={e.caption || e.id}
                onError={() =>
                  setError("画像を読み取れません。原本を確認してください。")
                }
              />
              <span className="image-edit-hint">
                <Pencil size={14} />
                クリックして注釈を編集
              </span>
            </button>
          ) : null}
          {e.kind === "table" && preview?.rows ? (
            <>
              <div className="data-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      {preview.rows[0]?.map((cell, i) => (
                        <th key={i}>{cell}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows
                      .slice(1, expanded ? undefined : 7)
                      .map((row, i) => (
                        <tr key={i}>
                          {row.map((cell, j) => (
                            <td key={j}>{cell}</td>
                          ))}
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
              <div className="preview-caption">
                <span>
                  {preview.total} 件
                  {preview.total > 100 ? " · 先頭 100 件をプレビュー" : ""}
                </span>
                {preview.total > 6 ? (
                  <button
                    className="text-button"
                    onClick={() => setExpanded(!expanded)}
                  >
                    {expanded ? "折りたたむ" : "続きを表示"}
                  </button>
                ) : null}
              </div>
            </>
          ) : null}
          {e.kind === "text" && preview ? (
            <>
              <div className="log-toolbar">
                <span>{e.lang || "text"}</span>
                <label>
                  <Search size={13} />
                  <input
                    placeholder="キーワードを強調"
                    aria-label={`${e.id} キーワードを強調`}
                    value={keyword}
                    onChange={(event) => setKeyword(event.target.value)}
                  />
                </label>
              </div>
              <pre className="log-preview">
                {lines.slice(0, expanded ? undefined : 8).map((line, i) => (
                  <span className="log-line" key={i}>
                    <span className="line-no">{i + 1}</span>
                    <code>{highlight(line) || " "}</code>
                  </span>
                ))}
              </pre>
              <div className="preview-caption">
                <span>
                  {preview.total} 行
                  {preview.total > 200 ? " · 先頭 200 行をプレビュー" : ""}
                </span>
                {lines.length > 8 ? (
                  <button
                    className="text-button"
                    onClick={() => setExpanded(!expanded)}
                  >
                    {expanded ? "折りたたむ" : "続きを表示"}
                  </button>
                ) : null}
              </div>
            </>
          ) : null}
          {e.kind === "file" ? (
            <div className="attachment-preview">
              <Paperclip size={30} />
              <strong>{e.originalName || e.file}</strong>
              <span>
                {e.size < 1024
                  ? `${e.size} B`
                  : `${(e.size / 1024).toFixed(1)} KB`}
              </span>
              <a className="button" href={`${url}&download=1`} download>
                <Download size={16} />
                添付をダウンロード
              </a>
            </div>
          ) : null}
          {(e.kind === "text" || e.kind === "table") && !preview && !error ? (
            <p className="muted preview-loading">読み込み中…</p>
          ) : null}
          {error ? (
            <p className="form-error" role="alert">
              {error}
            </p>
          ) : null}
        </div>
        <div className="evidence-notes">
          <label>
            <span>確認ポイント</span>
            <textarea
              rows={3}
              value={e.note}
              placeholder="この証拠で確認したこと"
              aria-label={`${e.id} 確認ポイント`}
              onChange={(event) => onChange({ ...e, note: event.target.value })}
            />
          </label>
          <label>
            <span>{e.category === "DB" ? "SQL / 出典" : "出典"}</span>
            <textarea
              className={e.category === "DB" ? "code-input" : ""}
              rows={3}
              value={e.source}
              placeholder="取得元、ファイル名など"
              aria-label={`${e.id} 出典`}
              onChange={(event) =>
                onChange({ ...e, source: event.target.value })
              }
            />
          </label>
          <a
            className="text-button original-link"
            href={`${url}&download=1`}
            download
          >
            <Download size={13} />
            {e.annotations ? "出力用画像を保存" : "原本を保存"}
          </a>
        </div>
      </div>
    </article>
  );
}
