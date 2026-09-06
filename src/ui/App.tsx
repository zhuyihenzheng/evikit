import { lazy, Suspense, useEffect, useRef, useState } from "react";
import {
  CheckCircle2,
  Download,
  Save,
  Plus,
  Upload,
  Clipboard,
  FileSpreadsheet,
  FileCode2,
  Package,
  X,
  Menu,
  Trash2,
  ArrowUpRight,
} from "lucide-react";
import {
  TestCaseSchema,
  type TestCase,
  type Evidence,
  type Project,
  type Annotations,
} from "../core/types";
import { resolveVerdict } from "../core/verdict";
import { orderEvidence } from "../export/format";
import {
  connect,
  request,
  json,
  form,
  type CaseDocument,
  type ProjectDocument,
  type ExportDocument,
} from "./api";
import Sidebar from "./components/Sidebar";
import StepTable from "./components/StepTable";
import EvidenceCard from "./components/EvidenceCard";
import EvidenceDialog, {
  type Incoming,
  type EvidenceInput,
} from "./components/EvidenceDialog";
import { CaseDialog, SettingsDialog } from "./components/CaseDialogs";
import { Modal, Verdict, Empty } from "./components/primitives";
const AnnotationEditor = lazy(() => import("./components/AnnotationEditor"));

export default function App() {
  const [project, setProject] = useState<ProjectDocument | null>(null);
  const [cases, setCases] = useState<TestCase[]>([]);
  const [document, setDocument] = useState<CaseDocument | null>(null);
  const [draft, setDraft] = useState<TestCase | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState("");
  const busyRef = useRef(false);
  const [query, setQuery] = useState("");
  const [ngOnly, setNgOnly] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [dialog, setDialog] = useState<"new" | "case" | "settings" | null>(
    null,
  );
  const [incoming, setIncoming] = useState<Incoming | null>(null);
  const [annotation, setAnnotation] = useState<Evidence | null>(null);
  const [exported, setExported] = useState<ExportDocument | null>(null);
  const [selectedStep, setSelectedStep] = useState<number | null>(1);
  const [filter, setFilter] = useState<string>("all");
  const [tab, setTab] = useState("steps");
  const [dragging, setDragging] = useState(false);
  const dragCount = useRef(0);
  const input = useRef<HTMLInputElement>(null);
  const [toast, setToast] = useState<{
    message: string;
    archiveId?: string;
  } | null>(null);
  const [pending, setPending] = useState<(() => Promise<void>) | null>(null);
  const dirty = Boolean(
    document &&
    draft &&
    JSON.stringify(document.data) !== JSON.stringify(draft),
  );
  const activeStep = draft?.steps.some((s) => s.no === selectedStep)
    ? selectedStep
    : null;
  const hasModal = Boolean(
    dialog || incoming || annotation || exported || pending,
  );

  function accept(value: CaseDocument) {
    if (document?.data.id !== value.data.id) {
      setSelectedStep(value.data.steps[0]?.no ?? null);
      setFilter("all");
      setTab("steps");
    }
    history.replaceState(null, "", `/#case=${value.data.id}`);
    setDocument(value);
    setDraft(value.data);
    setCases((items) =>
      items.some((c) => c.id === value.data.id)
        ? items.map((c) => (c.id === value.data.id ? value.data : c))
        : [...items, value.data].sort((a, b) => a.id.localeCompare(b.id)),
    );
  }
  async function loadCase(id: string) {
    const value = await request<CaseDocument>(`/cases/${id}`);
    accept(value);
    setSelectedStep(value.data.steps[0]?.no ?? null);
    setFilter("all");
    setTab("steps");
    setMobileOpen(false);
    history.replaceState(null, "", `/#case=${id}`);
  }
  async function act<T>(label: string, work: () => Promise<T>): Promise<T> {
    if (busyRef.current)
      throw new Error("前の操作を完了するまでお待ちください");
    busyRef.current = true;
    setBusy(label);
    setError("");
    try {
      return await work();
    } catch (e) {
      setError((e as Error).message);
      throw e;
    } finally {
      busyRef.current = false;
      setBusy("");
    }
  }
  const fire = (label: string, work: () => Promise<unknown>) => {
    void act(label, work).catch(() => {});
  };
  async function save(): Promise<CaseDocument | null> {
    if (!draft || !document) return null;
    if (!dirty) return document;
    const result = await request<CaseDocument>(
      `/cases/${draft.id}`,
      json("PUT", draft, document.revision),
    );
    accept(result);
    return result;
  }
  function guard(work: () => Promise<void>) {
    if (busyRef.current) return;
    if (dirty) setPending(() => work);
    else fire("読み込み中", work);
  }
  function chooseFile(files: FileList | File[]) {
    if (files.length > 1) {
      setError("ファイルは 1 つずつ追加してください。");
      return;
    }
    if (files[0]) setIncoming({ file: files[0] });
  }
  useEffect(() => {
    void act("プロジェクトを読み込み中", async () => {
      await connect();
      const [p, cs] = await Promise.all([
        request<ProjectDocument>("/project"),
        request<TestCase[]>("/cases"),
      ]);
      setProject(p);
      setCases(cs);
      const selected = new URLSearchParams(location.hash.slice(1)).get("case");
      if (cs.length)
        await loadCase(
          cs.some((c) => c.id === selected) ? selected! : cs[0]!.id,
        );
    }).catch(() => {});
  }, []);
  useEffect(() => {
    const unload = (e: BeforeUnloadEvent) => {
      if (dirty) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", unload);
    return () => window.removeEventListener("beforeunload", unload);
  }, [dirty]);
  useEffect(() => {
    const paste = (e: ClipboardEvent) => {
      if (
        !draft ||
        hasModal ||
        busyRef.current ||
        (e.target as HTMLElement)?.closest(
          "input,textarea,[contenteditable=true]",
        )
      )
        return;
      const files = e.clipboardData?.files;
      if (files?.length) {
        e.preventDefault();
        chooseFile(files);
      } else {
        const text = e.clipboardData?.getData("text/plain");
        if (text?.trim()) {
          e.preventDefault();
          setIncoming({ text });
        }
      }
    };
    const drop = (e: DragEvent) => {
      e.preventDefault();
      setDragging(false);
      dragCount.current = 0;
      if (
        !hasModal &&
        draft &&
        !busyRef.current &&
        e.dataTransfer?.files.length
      )
        chooseFile(e.dataTransfer.files);
    };
    const over = (e: DragEvent) => {
      if (e.dataTransfer?.types.includes("Files")) e.preventDefault();
    };
    const enter = (e: DragEvent) => {
      if (draft && !hasModal && e.dataTransfer?.types.includes("Files")) {
        dragCount.current++;
        setDragging(true);
      }
    };
    const leave = () => {
      dragCount.current = Math.max(0, dragCount.current - 1);
      if (!dragCount.current) setDragging(false);
    };
    const key = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        if (!hasModal && !busyRef.current)
          fire("保存中", async () => {
            await save();
            setToast({ message: "変更を保存しました" });
          });
      }
    };
    window.addEventListener("paste", paste);
    window.addEventListener("drop", drop);
    window.addEventListener("dragover", over);
    window.addEventListener("dragenter", enter);
    window.addEventListener("dragleave", leave);
    window.addEventListener("keydown", key);
    return () => {
      window.removeEventListener("paste", paste);
      window.removeEventListener("drop", drop);
      window.removeEventListener("dragover", over);
      window.removeEventListener("dragenter", enter);
      window.removeEventListener("dragleave", leave);
      window.removeEventListener("keydown", key);
    };
  }, [draft, hasModal, document, dirty]);
  useEffect(() => {
    if (!toast || toast.archiveId) return;
    const timer = setTimeout(() => setToast(null), 4500);
    return () => clearTimeout(timer);
  }, [toast]);
  async function upload(value: EvidenceInput) {
    await act("エビデンスを追加中", async () => {
      const current = await save();
      if (!current) return;
      const body = new FormData();
      body.set("metadata", JSON.stringify(value.metadata));
      if (value.file) body.set("file", value.file);
      else body.set("text", value.text ?? "");
      accept(
        await request<CaseDocument>(
          `/cases/${current.data.id}/evidence`,
          form(body, current.revision),
        ),
      );
      setFilter("all");
      setToast({ message: "エビデンスを保存しました" });
    });
  }
  function archive(eid?: string) {
    fire("削除中", async () => {
      const current = await save();
      if (!current) return;
      const result = await request<CaseDocument & { archiveId: string }>(
        `/cases/${current.data.id}${eid ? `/evidence/${eid}` : ""}`,
        { method: "DELETE", headers: { "If-Match": current.revision } },
      );
      if (eid) accept(result);
      else {
        const remaining = cases.filter((c) => c.id !== current.data.id);
        setCases(remaining);
        if (remaining.length) await loadCase(remaining[0]!.id);
        else {
          setDocument(null);
          setDraft(null);
        }
      }
      setToast({
        message: eid ? `${eid} を削除しました` : "ケースを削除しました",
        archiveId: result.archiveId,
      });
    });
  }
  const changeEvidence = (e: Evidence) =>
    setDraft((c) =>
      c
        ? {
            ...c,
            evidence: c.evidence.map((old) => (old.id === e.id ? e : old)),
          }
        : c,
    );
  const visibleCases = draft
    ? cases.map((c) => (c.id === draft.id ? draft : c))
    : cases;
  const evidence = draft
    ? orderEvidence(draft.evidence).filter(
        (e) =>
          filter === "all" ||
          (filter === "common" ? e.step === null : e.step === Number(filter)),
      )
    : [];
  const makeNew = () => {
    let n = 1;
    while (
      cases.some(
        (c) => c.id.toLowerCase() === `tc-${String(n).padStart(3, "0")}`,
      )
    )
      n++;
    return TestCaseSchema.parse({
      id: `TC-${String(n).padStart(3, "0")}`,
      title: "",
      tester: project?.data.tester,
      env: project?.data.env,
      date: new Date().toLocaleDateString("sv-SE"),
      steps: [{ no: 1, verdict: "未実施" }],
    });
  };
  if (!project)
    return (
      <div className="startup">
        <div className="brand">
          <span className="brand-mark">
            <CheckCircle2 />
          </span>
          <strong>evikit</strong>
        </div>
        <h1>
          {error
            ? "プロジェクトを開けませんでした"
            : "プロジェクトを読み込み中…"}
        </h1>
        {error ? (
          <>
            <p role="alert">{error}</p>
            <button className="button" onClick={() => location.reload()}>
              再読み込み
            </button>
          </>
        ) : null}
      </div>
    );
  return (
    <div className="app-shell">
      <Sidebar
        project={project.data}
        cases={visibleCases}
        selected={draft?.id}
        query={query}
        ngOnly={ngOnly}
        setQuery={setQuery}
        setNgOnly={setNgOnly}
        onSelect={(id) => id !== draft?.id && guard(() => loadCase(id))}
        onNew={() =>
          guard(async () => {
            setDialog("new");
          })
        }
        onSettings={() => setDialog("settings")}
        mobileOpen={mobileOpen}
        onClose={() => setMobileOpen(false)}
      />
      <main className="workspace">
        <header className="topbar">
          <button
            className="icon-button mobile-only"
            aria-label="ケース一覧を開く"
            onClick={() => setMobileOpen(true)}
          >
            <Menu size={21} />
          </button>
          <div className="breadcrumb">
            テストケース <span>/</span>{" "}
            <strong>{draft?.id ?? "新規プロジェクト"}</strong>
          </div>
          <div className="top-actions">
            <span
              className={`save-status ${dirty ? "unsaved" : ""}`}
              role="status"
            >
              {busy ? (
                <>
                  <span className="spinner" />
                  {busy}
                </>
              ) : dirty ? (
                <>
                  <i />
                  未保存の変更
                </>
              ) : (
                <>
                  <CheckCircle2 size={17} />
                  保存済み
                </>
              )}
            </span>
            <button
              className="button save-button"
              disabled={Boolean(busy) || !dirty}
              onClick={() =>
                fire("保存中", async () => {
                  await save();
                  setToast({ message: "変更を保存しました" });
                })
              }
            >
              <Save size={16} />
              保存
            </button>
            <button
              className="button primary"
              disabled={Boolean(busy) || !cases.length}
              onClick={() =>
                fire("成果物を出力中", async () => {
                  await save();
                  setExported(
                    await request<ExportDocument>("/export", {
                      method: "POST",
                    }),
                  );
                })
              }
            >
              <Download size={17} />
              成果物を出力
            </button>
          </div>
        </header>
        {error && !hasModal ? (
          <div className="error-banner" role="alert">
            <span>{error}</span>
            <button
              className="text-button"
              onClick={() =>
                guard(async () => {
                  if (draft) await loadCase(draft.id);
                })
              }
            >
              再読み込み
            </button>
            <button
              className="icon-button"
              aria-label="エラーを閉じる"
              onClick={() => setError("")}
            >
              <X size={16} />
            </button>
          </div>
        ) : null}
        {draft ? (
          <>
            <div className="case-heading">
              <div className="case-title-row">
                <h1>{draft.title || "名称未設定"}</h1>
                <Verdict value={resolveVerdict(draft)} />
                <button
                  className="button edit-case"
                  disabled={Boolean(busy)}
                  onClick={() => setDialog("case")}
                >
                  ケース情報を編集
                </button>
              </div>
              <p className="case-meta">
                <span>{draft.id}</span>
                <span>実施者 {draft.tester || "未設定"}</span>
                <span>{draft.date || "日付未設定"}</span>
                <span>{draft.env || "環境未設定"}</span>
                {draft.verdict ? <span>判定：手動指定</span> : null}
              </p>
              <div className="precondition">
                <strong>前提条件</strong>
                <span>
                  {draft.precondition || "ケース情報から前提条件を設定できます"}
                </span>
              </div>
              <div className="tabs">
                <button
                  className={tab === "steps" ? "active" : ""}
                  onClick={() => setTab("steps")}
                >
                  ステップ & エビデンス
                </button>
                <button
                  className={tab === "overview" ? "active" : ""}
                  onClick={() => setTab("overview")}
                >
                  ケース概要
                </button>
              </div>
            </div>
            <fieldset className="editor-fields" disabled={Boolean(busy)}>
              <div className="case-content">
                {tab === "steps" ? (
                  <>
                    <StepTable
                      value={draft}
                      selected={activeStep}
                      verdicts={project.data.verdicts}
                      onSelect={setSelectedStep}
                      onChange={setDraft}
                    />
                    <section
                      className="evidence-section"
                      aria-labelledby="evidence-title"
                    >
                      <div className="section-heading evidence-section-heading">
                        <h2 id="evidence-title">
                          エビデンス <span>{draft.evidence.length}</span>
                        </h2>
                        <div className="evidence-filters">
                          <button
                            className={filter === "all" ? "active" : ""}
                            onClick={() => setFilter("all")}
                          >
                            すべて
                          </button>
                          {draft.steps.map((s) => (
                            <button
                              key={s.no}
                              className={
                                filter === String(s.no) ? "active" : ""
                              }
                              onClick={() => {
                                setFilter(String(s.no));
                                setSelectedStep(s.no);
                              }}
                            >
                              Step {s.no}
                            </button>
                          ))}
                          <button
                            className={filter === "common" ? "active" : ""}
                            onClick={() => {
                              setFilter("common");
                              setSelectedStep(null);
                            }}
                          >
                            共通
                          </button>
                        </div>
                      </div>
                      <div className={`dropzone ${dragging ? "dragging" : ""}`}>
                        <Upload size={27} strokeWidth={1.6} />
                        <div>
                          <strong>
                            ここにファイルをドロップ、または貼り付け
                          </strong>
                          <p>
                            画像・表・ログ・添付ファイル
                            <span className="drop-target">
                              {activeStep === null
                                ? "共通"
                                : `Step ${activeStep}`}{" "}
                              に追加
                            </span>
                          </p>
                        </div>
                        <div className="drop-actions">
                          <button
                            className="button"
                            onClick={() => input.current?.click()}
                          >
                            ファイルを選択
                          </button>
                          <button
                            className="text-button"
                            onClick={() => setIncoming({ text: "" })}
                          >
                            <Clipboard size={13} />
                            テキストを追加
                          </button>
                        </div>
                        <input
                          ref={input}
                          type="file"
                          className="sr-only"
                          aria-label="証拠ファイルを選択"
                          onChange={(e) => {
                            if (e.target.files) chooseFile(e.target.files);
                            e.target.value = "";
                          }}
                        />
                      </div>
                      <div className="evidence-list">
                        {evidence.map((e) => (
                          <EvidenceCard
                            key={e.id}
                            caseId={draft.id}
                            evidence={e}
                            steps={draft.steps}
                            onChange={changeEvidence}
                            onDelete={() => archive(e.id)}
                            onAnnotate={() =>
                              fire("画像を準備中", async () => {
                                const current = await save();
                                setAnnotation(
                                  current!.data.evidence.find(
                                    (v) => v.id === e.id,
                                  )!,
                                );
                              })
                            }
                          />
                        ))}
                        {!evidence.length ? (
                          <Empty
                            title={
                              draft.evidence.length
                                ? "このステップのエビデンスはまだありません"
                                : "最初のエビデンスを追加しましょう"
                            }
                          >
                            <p>
                              ステップを選んで、スクリーンショットや確認結果を貼り付けてください。
                            </p>
                          </Empty>
                        ) : null}
                      </div>
                    </section>
                  </>
                ) : (
                  <section className="overview">
                    <h2>ケース概要</h2>
                    <dl>
                      <dt>件名</dt>
                      <dd>{draft.title}</dd>
                      <dt>実施者 / 実施日</dt>
                      <dd>
                        {draft.tester || "—"} / {draft.date || "—"}
                      </dd>
                      <dt>環境</dt>
                      <dd>{draft.env || "—"}</dd>
                      <dt>判定</dt>
                      <dd>
                        <Verdict value={resolveVerdict(draft)} />
                        <span>
                          {draft.verdict
                            ? "ケースで手動指定"
                            : "ステップから自動判定"}
                        </span>
                      </dd>
                      <dt>前提条件</dt>
                      <dd>{draft.precondition || "—"}</dd>
                      <dt>備考</dt>
                      <dd>{draft.note || "—"}</dd>
                      <dt>記録</dt>
                      <dd>
                        {draft.steps.length} ステップ / {draft.evidence.length}{" "}
                        エビデンス
                      </dd>
                    </dl>
                    <p className="form-hint">
                      未入力のステップ判定は「未実施」として集計します。
                    </p>
                    <button
                      className="text-button danger"
                      onClick={() => archive()}
                    >
                      <Trash2 size={15} />
                      このケースを削除
                    </button>
                    <p className="form-hint">
                      削除後の通知から元に戻せます。データは .trash
                      に保管されます。
                    </p>
                  </section>
                )}
              </div>
            </fieldset>
          </>
        ) : (
          <Empty title="最初のテストケースを作りましょう">
            <p>操作・期待結果・証拠をまとめて、成果物を作成します。</p>
            <button className="button primary" onClick={() => setDialog("new")}>
              <Plus size={17} />
              新規ケース
            </button>
          </Empty>
        )}
        <footer className="workspace-footer">
          <span>
            evikit <span className="version">0.2</span>
          </span>
          <span>すべてのデータはこのコンピューターに保存されます</span>
        </footer>
      </main>
      {dragging && draft && !hasModal ? (
        <div className="drop-overlay">
          <Upload size={44} />
          <h2>
            {activeStep === null ? "共通" : `Step ${activeStep}`}{" "}
            にエビデンスを追加
          </h2>
          <p>ファイルをここにドロップ</p>
        </div>
      ) : null}
      {dialog === "new" || dialog === "case" ? (
        <CaseDialog
          value={dialog === "new" ? makeNew() : draft!}
          isNew={dialog === "new"}
          verdicts={project.data.verdicts}
          onClose={() => setDialog(null)}
          onSubmit={async (v) => {
            if (dialog === "new")
              await act("ケースを作成中", async () => {
                accept(await request<CaseDocument>("/cases", json("POST", v)));
                setSelectedStep(1);
                setFilter("all");
                setQuery("");
                setNgOnly(false);
                setMobileOpen(false);
              });
            else setDraft(v);
          }}
        />
      ) : null}
      {dialog === "settings" ? (
        <SettingsDialog
          value={project.data}
          path={project.path}
          onClose={() => setDialog(null)}
          onSubmit={(p) =>
            act("設定を保存中", async () =>
              setProject(
                await request<ProjectDocument>(
                  "/project",
                  json("PUT", p, project.revision),
                ),
              ),
            )
          }
        />
      ) : null}
      {incoming && draft ? (
        <EvidenceDialog
          incoming={incoming}
          steps={draft.steps}
          step={activeStep}
          onClose={() => setIncoming(null)}
          onSubmit={upload}
        />
      ) : null}
      {annotation && draft ? (
        <Suspense
          fallback={
            <div className="busy-overlay">注釈エディターを読み込み中…</div>
          }
        >
          <AnnotationEditor
            evidence={annotation}
            caseId={draft.id}
            onClose={() => setAnnotation(null)}
            onSave={(blob, annotations) =>
              act("注釈を保存中", async () => {
                const body = new FormData();
                body.set("annotations", JSON.stringify(annotations));
                if (blob) body.set("file", blob, "annotated.png");
                else body.set("reset", "true");
                accept(
                  await request<CaseDocument>(
                    `/cases/${draft.id}/evidence/${annotation.id}/image`,
                    form(body, document!.revision, "PUT"),
                  ),
                );
                setToast({
                  message: blob ? "注釈を保存しました" : "原図に戻しました",
                });
              })
            }
          />
        </Suspense>
      ) : null}
      {exported ? (
        <Modal
          title="成果物を出力しました"
          onClose={() => setExported(null)}
          footer={
            <button
              className="button primary"
              onClick={() => setExported(null)}
            >
              完了
            </button>
          }
        >
          <div className="export-success">
            <CheckCircle2 size={32} />
            <p>保存済みのテスト結果から、成果物を生成しました。</p>
          </div>
          <div className="export-files">
            <a
              href={exported.urls.zip}
              className="export-file primary-download"
              download
            >
              <Package size={25} />
              <span>
                <strong>納品パッケージ</strong>
                <small>Excel + HTML + 添付ファイル + チェックサム一覧</small>
              </span>
              <Download size={18} />
            </a>
            <a href={exported.urls.xlsx} className="export-file" download>
              <FileSpreadsheet size={25} />
              <span>
                <strong>Excel</strong>
                <small>顧客提出用の固定レイアウト</small>
              </span>
              <Download size={18} />
            </a>
            <a
              href={exported.urls.html}
              className="export-file"
              target="_blank"
              rel="noreferrer"
            >
              <FileCode2 size={25} />
              <span>
                <strong>HTML レビュー</strong>
                <small>画像・添付を含む単一ファイル</small>
              </span>
              <ArrowUpRight size={18} />
            </a>
          </div>
          <div className="folder-info">
            <strong>出力先</strong>
            <code>{exported.outDir}</code>
          </div>
        </Modal>
      ) : null}
      {pending ? (
        <Modal
          title="未保存の変更があります"
          onClose={() => setPending(null)}
          footer={
            <>
              <button
                className="button"
                disabled={Boolean(busy)}
                onClick={() => setPending(null)}
              >
                キャンセル
              </button>
              <button
                className="button"
                disabled={Boolean(busy)}
                onClick={() =>
                  fire("読み込み中", async () => {
                    const next = pending;
                    setPending(null);
                    setDraft(document?.data ?? null);
                    await next();
                  })
                }
              >
                変更を破棄
              </button>
              <button
                className="button primary"
                disabled={Boolean(busy)}
                onClick={() =>
                  fire("保存中", async () => {
                    await save();
                    const next = pending;
                    setPending(null);
                    await next();
                  })
                }
              >
                保存して続ける
              </button>
            </>
          }
        >
          <p>現在のケースの変更を保存してから続けますか？</p>
          {error ? (
            <p className="form-error" role="alert">
              {error}
            </p>
          ) : null}
        </Modal>
      ) : null}
      {toast ? (
        <div className="toast" role="status">
          <CheckCircle2 size={18} />
          <span>{toast.message}</span>
          {toast.archiveId ? (
            <button
              disabled={Boolean(busy)}
              onClick={() =>
                fire("復元中", async () => {
                  await save();
                  const result = await request<CaseDocument>(
                    `/restore/${toast.archiveId}`,
                    { method: "POST" },
                  );
                  accept(result);
                  setToast({ message: "元に戻しました" });
                })
              }
            >
              元に戻す
            </button>
          ) : null}
          <button aria-label="通知を閉じる" onClick={() => setToast(null)}>
            <X size={15} />
          </button>
        </div>
      ) : null}
    </div>
  );
}
