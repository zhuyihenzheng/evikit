// §5 データ形式: zod スキーマ (Project / TestCase / Step / Evidence)
import { z } from "zod";

/** YAML の空欄は null になるので、文字列項目は null を "" に寄せる。 */
const optionalText = z
  .string()
  .nullish()
  .transform((v) => v ?? "");

export const KINDS = ["image", "table", "text", "file"] as const;
export const CATEGORIES = [
  "画面",
  "DB",
  "ログ",
  "API",
  "コマンド",
  "設定",
  "エラー",
  "その他",
] as const;
export const LANGS = ["", "log", "json", "xml", "sql", "plain"] as const;

export const KindSchema = z.enum(KINDS);
export const CategorySchema = z.enum(CATEGORIES);
export const LangSchema = z.enum(LANGS);

export const LocalFileSchema = z
  .string()
  .min(1)
  .max(240)
  .refine(
    (v) =>
      !/[\\/\x00-\x1f<>:"|?*]/.test(v) &&
      v !== "." &&
      v !== ".." &&
      !/[. ]$/.test(v),
    "ファイル名にパスや使用できない文字が含まれています",
  );
const coordinate = z.number().finite().min(0).max(100000);
const color = z.enum(["red", "blue", "yellow"]);
const position = { x: coordinate, y: coordinate, color };
export const AnnotationSchema = z.object({
  crop: z
    .object({
      x: coordinate,
      y: coordinate,
      w: coordinate.positive(),
      h: coordinate.positive(),
    })
    .optional(),
  shapes: z
    .array(
      z.discriminatedUnion("type", [
        z.object({
          type: z.literal("rect"),
          ...position,
          w: coordinate,
          h: coordinate,
        }),
        z.object({
          type: z.literal("arrow"),
          ...position,
          x2: coordinate,
          y2: coordinate,
        }),
        z.object({
          type: z.literal("number"),
          ...position,
          n: z.number().int().min(1).max(999),
        }),
        z.object({
          type: z.literal("text"),
          ...position,
          text: z.string().max(1000),
        }),
      ]),
    )
    .max(1000)
    .default([]),
});
export type Annotations = z.infer<typeof AnnotationSchema>;
export type AnnotationShape = Annotations["shapes"][number];

export const EvidenceSchema = z.object({
  id: z.string().regex(/^E\d+$/),
  kind: KindSchema,
  category: CategorySchema,
  caption: optionalText,
  /** 所属ステップ。無い場合は null (§5.3 の「共通」)。 */
  step: z.number().int().positive().nullable().default(null),
  file: LocalFileSchema,
  originalFile: LocalFileSchema.optional(),
  originalSha256: z
    .string()
    .regex(/^[a-f0-9]{64}$/)
    .optional(),
  annotations: AnnotationSchema.optional(),
  /** 文字列のまま扱う (Date に変換しない)。 */
  capturedAt: optionalText,
  source: optionalText,
  note: optionalText,
  sha256: optionalText,
  lang: LangSchema.nullish().transform((v) => v ?? ""),
  originalName: optionalText,
  size: z
    .number()
    .int()
    .nonnegative()
    .nullish()
    .transform((v) => v ?? 0),
});

export const StepSchema = z.object({
  no: z.number().int().positive(),
  action: optionalText,
  /** Native editor field; preserve on shared-project reads and saves. */
  condition: optionalText.optional(),
  expected: optionalText,
  actual: optionalText,
  verdict: optionalText,
});

export const TestCaseSchema = z
  .object({
    /** ファイル名 / sheet 名に使うので [A-Za-z0-9_-] のみ (§5.2)。 */
    id: z
      .string()
      .max(80)
      .regex(/^[A-Za-z0-9_-]+$/, "case id は [A-Za-z0-9_-] のみ")
      .refine(
        (v) => !/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i.test(v),
        "Windows の予約名は使用できません",
      ),
    title: optionalText,
    precondition: optionalText,
    tester: optionalText,
    /** 日付は必ず文字列 (§5.2)。 */
    date: optionalText,
    env: optionalText,
    /** 空なら §5.4 でステップから導出する。 */
    verdict: optionalText,
    note: optionalText,
    steps: z.array(StepSchema).default([]),
    evidence: z.array(EvidenceSchema).default([]),
    /** 採番済み ID は削除後も再利用しない。旧 YAML では省略可。 */
    nextEvidenceNumber: z.number().int().positive().optional(),
  })
  .superRefine((c, ctx) => {
    if (new Set(c.steps.map((s) => s.no)).size !== c.steps.length)
      ctx.addIssue({
        code: "custom",
        message: "ステップ番号が重複しています",
        path: ["steps"],
      });
    if (new Set(c.evidence.map((e) => e.id)).size !== c.evidence.length)
      ctx.addIssue({
        code: "custom",
        message: "エビデンス ID が重複しています",
        path: ["evidence"],
      });
    for (const e of c.evidence)
      if (e.step !== null && !c.steps.some((s) => s.no === e.step))
        ctx.addIssue({
          code: "custom",
          message: `${e.id}: 所属ステップが存在しません`,
          path: ["evidence"],
        });
  });

export const ProjectSchema = z.object({
  name: z.string().min(1),
  tester: optionalText,
  env: optionalText,
  verdicts: z
    .array(z.string())
    .nullish()
    .transform((v) => v ?? ["OK", "NG", "保留", "対象外", "未実施"]),
  excerptLines: z
    .number()
    .int()
    .min(1)
    .max(1000)
    .nullish()
    .transform((v) => v ?? 30),
  imageMaxWidth: z
    .number()
    .int()
    .min(100)
    .max(2000)
    .nullish()
    .transform((v) => v ?? 640),
});

export type Kind = (typeof KINDS)[number];
export type Category = (typeof CATEGORIES)[number];
export type Lang = (typeof LANGS)[number];
export type Evidence = z.infer<typeof EvidenceSchema>;
export type Step = z.infer<typeof StepSchema>;
export type TestCase = z.infer<typeof TestCaseSchema>;
export type Project = z.infer<typeof ProjectSchema>;
