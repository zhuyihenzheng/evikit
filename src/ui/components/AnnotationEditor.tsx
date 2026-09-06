import { useEffect, useRef, useState } from "react";
import {
  Square,
  MoveUpRight,
  Circle,
  Type,
  Crop,
  Undo2,
  RotateCcw,
  Check,
} from "lucide-react";
import type { Annotations, AnnotationShape, Evidence } from "../../core/types";
import { rawUrl } from "../api";
import { Modal, ErrorMessage } from "./primitives";
const colors = { red: "#dc3545", blue: "#2563eb", yellow: "#e5ad10" };
type Tool = "rect" | "arrow" | "number" | "text" | "crop";
const tools = [
  { id: "rect", label: "赤枠", Icon: Square },
  { id: "arrow", label: "矢印", Icon: MoveUpRight },
  { id: "number", label: "番号", Icon: Circle },
  { id: "text", label: "テキスト", Icon: Type },
  { id: "crop", label: "トリミング", Icon: Crop },
] as const;

export function paint(
  canvas: HTMLCanvasElement,
  image: HTMLImageElement,
  annotations: Annotations,
  draft?: AnnotationShape | null,
) {
  const crop = annotations.crop ?? {
    x: 0,
    y: 0,
    w: image.naturalWidth,
    h: image.naturalHeight,
  };
  canvas.width = Math.round(crop.w);
  canvas.height = Math.round(crop.h);
  const ctx = canvas.getContext("2d")!;
  ctx.translate(-crop.x, -crop.y);
  ctx.drawImage(image, 0, 0);
  ctx.lineWidth = 3;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  for (const shape of [...annotations.shapes, ...(draft ? [draft] : [])]) {
    ctx.strokeStyle = colors[shape.color];
    ctx.fillStyle = colors[shape.color];
    if (shape.type === "rect")
      ctx.strokeRect(shape.x, shape.y, shape.w, shape.h);
    if (shape.type === "arrow") {
      const angle = Math.atan2(shape.y2 - shape.y, shape.x2 - shape.x);
      ctx.beginPath();
      ctx.moveTo(shape.x, shape.y);
      ctx.lineTo(shape.x2, shape.y2);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(shape.x2, shape.y2);
      ctx.lineTo(
        shape.x2 - 14 * Math.cos(angle - 0.45),
        shape.y2 - 14 * Math.sin(angle - 0.45),
      );
      ctx.lineTo(
        shape.x2 - 14 * Math.cos(angle + 0.45),
        shape.y2 - 14 * Math.sin(angle + 0.45),
      );
      ctx.closePath();
      ctx.fill();
    }
    if (shape.type === "number") {
      ctx.beginPath();
      ctx.arc(shape.x, shape.y, 14, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = "white";
      ctx.font = "bold 17px sans-serif";
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(String(shape.n), shape.x, shape.y + 1);
    }
    if (shape.type === "text") {
      ctx.font = "bold 16px sans-serif";
      ctx.textAlign = "left";
      ctx.textBaseline = "top";
      ctx.strokeStyle = "white";
      ctx.lineWidth = 4;
      ctx.strokeText(shape.text, shape.x, shape.y);
      ctx.fillText(shape.text, shape.x, shape.y);
      ctx.lineWidth = 3;
    }
  }
}

export default function AnnotationEditor({
  evidence,
  caseId,
  onSave,
  onClose,
}: {
  evidence: Evidence;
  caseId: string;
  onSave: (blob: Blob | null, annotations: Annotations) => Promise<void>;
  onClose: () => void;
}) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const image = useRef<HTMLImageElement | null>(null);
  const [history, setHistory] = useState<Annotations[]>([
    evidence.annotations ?? { shapes: [] },
  ]);
  const current = history[history.length - 1]!;
  const [tool, setTool] = useState<Tool>("rect");
  const [color, setColor] = useState<keyof typeof colors>("red");
  const [text, setText] = useState("ここを確認");
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [draft, setDraft] = useState<AnnotationShape | null>(null);
  const start = useRef<{ x: number; y: number } | null>(null);
  useEffect(() => {
    const img = new Image();
    let active = true;
    img.onload = () => {
      if (active) {
        image.current = img;
        setReady(true);
      }
    };
    img.onerror = () => {
      if (active) setError("原画像を読み取れません");
    };
    img.src = `${rawUrl(caseId, evidence.id, evidence.sha256)}&original=1`;
    return () => {
      active = false;
    };
  }, [caseId, evidence.id]);
  useEffect(() => {
    if (ready && canvas.current && image.current)
      paint(canvas.current, image.current, current, draft);
  }, [ready, current, draft]);
  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const bounds = e.currentTarget.getBoundingClientRect();
    const crop = current.crop ?? {
      x: 0,
      y: 0,
      w: image.current!.naturalWidth,
      h: image.current!.naturalHeight,
    };
    return {
      x: Math.round(
        crop.x +
          Math.max(
            0,
            Math.min(
              crop.w,
              ((e.clientX - bounds.left) / bounds.width) * crop.w,
            ),
          ),
      ),
      y: Math.round(
        crop.y +
          Math.max(
            0,
            Math.min(
              crop.h,
              ((e.clientY - bounds.top) / bounds.height) * crop.h,
            ),
          ),
      ),
    };
  };
  const shape = (
    from: { x: number; y: number },
    to: { x: number; y: number },
  ): AnnotationShape =>
    tool === "arrow"
      ? { type: "arrow", ...from, x2: to.x, y2: to.y, color }
      : {
          type: "rect",
          x: Math.min(from.x, to.x),
          y: Math.min(from.y, to.y),
          w: Math.abs(to.x - from.x),
          h: Math.abs(to.y - from.y),
          color,
        };
  const push = (a: Annotations) => setHistory((h) => [...h, a]);
  const save = async (reset = false) => {
    setBusy(true);
    setError("");
    try {
      let blob: Blob | null = null;
      if (!reset) {
        const output = document.createElement("canvas");
        paint(output, image.current!, current);
        blob = await new Promise<Blob>((resolve, reject) =>
          output.toBlob(
            (b) => (b ? resolve(b) : reject(new Error("PNG を作成できません"))),
            "image/png",
          ),
        );
      }
      await onSave(blob, current);
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      title={`${evidence.id} · スクリーンショット注釈`}
      wide
      onClose={() => !busy && onClose()}
      footer={
        <>
          <span className="modal-foot-hint">
            {ready
              ? `${current.crop?.w ?? image.current?.naturalWidth} × ${current.crop?.h ?? image.current?.naturalHeight} px · ${current.shapes.length} 個の注釈`
              : "画像を読み込み中…"}
          </span>
          <button className="button" onClick={onClose} disabled={busy}>
            キャンセル
          </button>
          <button
            className="button primary"
            disabled={busy || !ready}
            onClick={() => save()}
          >
            <Check size={16} />
            {busy ? "保存中…" : "注釈を保存"}
          </button>
        </>
      }
    >
      <div className="annotation-tools">
        {tools.map(({ id, label, Icon }) => (
          <button
            className={`tool-button ${tool === id ? "active" : ""}`}
            key={id}
            onClick={() => setTool(id)}
            title={label}
            aria-pressed={tool === id}
          >
            <Icon size={17} />
            <span>{label}</span>
          </button>
        ))}
        <span className="toolbar-separator" />
        {(Object.keys(colors) as (keyof typeof colors)[]).map((c) => (
          <button
            key={c}
            aria-label={`${c === "red" ? "赤" : c === "blue" ? "青" : "黄"}色`}
            aria-pressed={color === c}
            className={`color-button ${color === c ? "selected" : ""}`}
            style={{ background: colors[c] }}
            onClick={() => setColor(c)}
          />
        ))}
        <button
          className="icon-button"
          title="元に戻す"
          aria-label="元に戻す"
          disabled={history.length < 2 || busy}
          onClick={() => setHistory((h) => h.slice(0, -1))}
        >
          <Undo2 size={18} />
        </button>
        <button
          className="text-button restore-image"
          disabled={!ready || busy}
          onClick={() => save(true)}
        >
          <RotateCcw size={15} />
          原図に戻す
        </button>
      </div>
      {tool === "text" ? (
        <input
          className="annotation-text"
          aria-label="注釈テキスト"
          value={text}
          maxLength={1000}
          onChange={(e) => setText(e.target.value)}
          placeholder="配置するテキスト"
        />
      ) : null}
      <p className="annotation-instruction">
        {tool === "number"
          ? "画像をクリックして番号を配置します。"
          : tool === "text"
            ? "テキストを入力して、配置する位置をクリックします。"
            : tool === "crop"
              ? "残す範囲をドラッグして選択します。"
              : "画像上をドラッグして描画します。"}{" "}
        原図はそのまま保管されます。
      </p>
      <div className="annotation-stage">
        {!ready && !error ? <p>原画像を読み込み中…</p> : null}
        <canvas
          ref={canvas}
          aria-label="画像注釈キャンバス"
          style={{ display: ready ? "block" : "none" }}
          onPointerDown={(e) => {
            if (!ready || busy) return;
            e.preventDefault();
            const p = point(e);
            if (tool === "number") {
              push({
                ...current,
                shapes: [
                  ...current.shapes,
                  {
                    type: "number",
                    ...p,
                    color,
                    n:
                      Math.max(
                        0,
                        ...current.shapes
                          .filter((s) => s.type === "number")
                          .map((s) => s.n),
                      ) + 1,
                  },
                ],
              });
              return;
            }
            if (tool === "text") {
              if (text.trim())
                push({
                  ...current,
                  shapes: [
                    ...current.shapes,
                    { type: "text", ...p, text, color },
                  ],
                });
              return;
            }
            start.current = p;
            e.currentTarget.setPointerCapture(e.pointerId);
          }}
          onPointerMove={(e) => {
            if (start.current) setDraft(shape(start.current, point(e)));
          }}
          onPointerUp={(e) => {
            if (!start.current) return;
            const item = shape(start.current, point(e));
            start.current = null;
            setDraft(null);
            e.currentTarget.releasePointerCapture(e.pointerId);
            if (item.type === "rect" && item.w >= 3 && item.h >= 3)
              push(
                tool === "crop"
                  ? {
                      ...current,
                      crop: { x: item.x, y: item.y, w: item.w, h: item.h },
                    }
                  : { ...current, shapes: [...current.shapes, item] },
              );
            if (
              item.type === "arrow" &&
              Math.hypot(item.x2 - item.x, item.y2 - item.y) >= 3
            )
              push({ ...current, shapes: [...current.shapes, item] });
          }}
          onPointerCancel={() => {
            start.current = null;
            setDraft(null);
          }}
        />
      </div>
      <ErrorMessage error={error} />
    </Modal>
  );
}
