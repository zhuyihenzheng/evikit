import { Check, Plus, Search, Settings, Folder, X } from "lucide-react";
import { resolveVerdict } from "../../core/verdict";
import type { TestCase, Project } from "../../core/types";
import { Verdict } from "./primitives";
export default function Sidebar({
  project,
  cases,
  selected,
  query,
  ngOnly,
  setQuery,
  setNgOnly,
  onSelect,
  onNew,
  onSettings,
  mobileOpen,
  onClose,
}: {
  project: Project;
  cases: TestCase[];
  selected?: string;
  query: string;
  ngOnly: boolean;
  setQuery: (v: string) => void;
  setNgOnly: (v: boolean) => void;
  onSelect: (id: string) => void;
  onNew: () => void;
  onSettings: () => void;
  mobileOpen: boolean;
  onClose: () => void;
}) {
  const filtered = cases.filter(
    (c) =>
      (!ngOnly || resolveVerdict(c) === "NG") &&
      `${c.id} ${c.title} ${c.note}`
        .toLowerCase()
        .includes(query.toLowerCase()),
  );
  return (
    <>
      <div
        className={`sidebar-shade ${mobileOpen ? "visible" : ""}`}
        onClick={onClose}
      />
      <aside
        className={`sidebar ${mobileOpen ? "mobile-open" : ""}`}
        aria-label="ケースナビゲーション"
      >
        <div className="brand">
          <span className="brand-mark">
            <Check size={26} strokeWidth={3} />
          </span>
          <div>
            <strong>evikit</strong>
            <small>テストエビデンス</small>
          </div>
          <button
            className="icon-button mobile-only"
            aria-label="ナビゲーションを閉じる"
            onClick={onClose}
          >
            <X size={20} />
          </button>
        </div>
        <button className="project-switch" onClick={onSettings}>
          <Folder size={17} />
          <span>
            <strong>{project.name}</strong>
            <small>{project.env || "環境未設定"}</small>
          </span>
        </button>
        <div className="sidebar-heading">
          <h2>テストケース</h2>
          <button
            className="icon-button bordered"
            aria-label="新規ケース"
            title="新規ケース"
            onClick={onNew}
          >
            <Plus size={20} />
          </button>
        </div>
        <label className="search">
          <Search size={17} />
          <input
            aria-label="ケースを検索"
            placeholder="ケースを検索"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {query ? (
            <button aria-label="検索をクリア" onClick={() => setQuery("")}>
              <X size={14} />
            </button>
          ) : null}
        </label>
        <div className="case-filters">
          <button
            className={!ngOnly ? "active" : ""}
            onClick={() => setNgOnly(false)}
          >
            すべて <span>{cases.length}</span>
          </button>
          <button
            className={ngOnly ? "active ng-filter" : "ng-filter"}
            onClick={() => setNgOnly(true)}
          >
            NG のみ{" "}
            <span>
              {cases.filter((c) => resolveVerdict(c) === "NG").length}
            </span>
          </button>
        </div>
        <nav className="case-list">
          {filtered.map((c) => (
            <button
              key={c.id}
              className={`case-item ${selected === c.id ? "selected" : ""}`}
              onClick={() => onSelect(c.id)}
              aria-current={selected === c.id ? "page" : undefined}
            >
              <span className="case-id">
                {c.id}
                <Verdict value={resolveVerdict(c)} />
              </span>
              <span className="case-title">{c.title || "名称未設定"}</span>
              <span className="case-counts">
                {c.steps.length} ステップ<span>·</span>
                {c.evidence.length} エビデンス
              </span>
            </button>
          ))}
          {!filtered.length ? (
            <p className="muted sidebar-empty">
              {cases.length
                ? "一致するケースがありません"
                : "ケースを作成して始めましょう"}
            </p>
          ) : null}
        </nav>
        <div className="sidebar-bottom">
          <p className="local-status">
            <i />
            ローカルで動作中
          </p>
          <button className="button settings-button" onClick={onSettings}>
            <Settings size={17} />
            プロジェクト設定
          </button>
        </div>
      </aside>
    </>
  );
}
