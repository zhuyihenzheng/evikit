# evikit 引き継ぎ資料（Handoff）— 2026-09-05

> **更新：0.2 のローカル Web UI は実装済みです。** この資料は Phase 0 完了時点の記録として残しています。
> 現在の設計判断・追加データ項目・実装済み範囲・制限は [05-architecture.md](05-architecture.md)、起動方法は [README](../README.md) を参照してください。
> 以下の「Phase 1 未着手」や委託文は履歴であり、現在の作業指示ではありません。

> 目的：この 1 ファイルだけを読めば、別の開発者 / AI が Phase 1 以降を実装できる状態にする。
> リポジトリ：`<repository-root>`
> 関連：`docs/00-proposal.md`（長期構想）/ `docs/01-mvp.md`（**実装契約・正**）/ `docs/03-ai-assist.md`（AI 支援方針）

---

# 第 1 部：これは何か

## 1.1 ツールの目的

ソフトウェアテストの**成果物（エビデンス / 証跡）**を作る道具。テスト実施者が、テストケースの手順ごとに証拠（画面キャプチャ、SQL 結果、ログ、CSV、API 応答、コマンド出力、設定ファイル、エラー）を貼り付けていくと、**顧客にそのまま提出できる Excel** と、**レビュー用の単一 HTML** を自動生成する。

解決する痛み：
- 現状は「スクショを撮って Excel に手で貼る」→ 大量の機械作業、レイアウト崩れ、再テスト時のやり直し
- DB 結果やログまで**画像**で貼るため、検索も比較も差分もできない
- 顧客ごとにフォーマットが違うので毎回作り直し

## 1.2 中核の設計思想（これだけは崩さない）

1. **証拠は構造化データとして保存する。画像は表示形式の 1 つにすぎない。**
   SQL 結果は「SQL 文 + 行データ（CSV）」として保存する。ログは「テキスト + 抜粋範囲」。だから検索・比較・再出力ができる。
2. **プロジェクト = ただのフォルダ。** YAML + 証拠ファイル。DB サーバ不要、git で管理でき、zip で納品できる。
3. **1 つのデータから複数の出力。** Excel（納品用）と HTML（レビュー用）は同じ case データから生成される。将来 Word / PDF を足しても、データ側は変わらない。
4. **ローカル完結。** ネットワーク送信なし、テレメトリなし。顧客データを扱うため。
5. **証拠の種類は 4 つだけ**（`image` / `table` / `text` / `file`）**+ 表示用カテゴリ**（画面 / DB / ログ / API / コマンド / 設定 / エラー / その他）。
   「DB 直結」「HTTP 実行」などの取得手段を増やしても、この 4 種に落ちるので出力側は変えなくていい。

## 1.3 スコープ（ユーザー確定済み）

**やる（MVP）**
- プロジェクトフォルダ（`project.yaml` + `cases/*.yaml` + `evidence/`）
- ローカル Web UI：ケース / ステップ編集、証拠の貼り付け・ドラッグ&ドロップ
- 出力：Excel（固定レイアウト）+ HTML（単一ファイル）
- CLI：`init` / `export` / `serve`
- 画面キャプチャへの注釈（赤枠・矢印・番号・テキスト・トリミング）— Phase 1

**やらない（明示的に除外。勝手に足さないこと）**
マスキング（脱敏）、Word / PDF 出力、DB 直接接続、HTTP / コマンド実行機能、出力テンプレート機能、複数 Run（再テスト履歴）、プラグイン機構、外部連携（Jira 等）、デスクトップアプリ化、自動テスト用 SDK、全文検索インデックス。

**ユーザーの指示（原文）**：「脱敏先不做, WORD PDF不做先」「没有模版,你来设定就好不要再花样,简单明了可靠清楚就好」
→ レイアウトは実装者が決めてよい。**シンプル・明快・確実**が最優先。凝った機能は不要。

## 1.4 想定環境

- 開発機：macOS（Bun 1.3.12 / Node 24 利用可）
- **納品先の利用者：日本の SI 現場、Windows、Excel 文化、ソフトのインストール不可の可能性あり**
- 成果物の言語：**日本語**（期待結果 / 実際結果 / 判定 = OK / NG / 保留 / 対象外 / 未実施）
- フォント指定が `Meiryo UI` / `MS Gothic` なのはこのため（macOS では代替フォントになるが正）

---

# 第 2 部：現在の進捗

## 2.1 完了：Phase 0（コア + 出力 + サンプル + テスト）

**状態：完成・全テスト green・生成物を実機確認済み。**

```
$ bun test
 48 pass / 0 fail / 133 expect() calls   (4 files, 202ms)

$ bunx tsc --noEmit
 (エラーなし)

$ bun run src/cli.ts export examples/sample
出力先: examples/sample/exports/20260905-002842
  顧客管理システム 結合テスト.xlsx
  顧客管理システム 結合テスト.html
  files/TC-003/E04-customers.zip
```

検証済みの内容：
- xlsx を ExcelJS で読み戻し：シート 4 枚（`サマリ` + TC-001/002/003）、列幅 6/36/32/32/8/14、画像 2 枚が `TC-001` に 640×400 で `tl=(1,12)` / `(1,37)` に配置、`TC-001!B1 === "TC-001"`、`00012` が文字列のまま保持、結合セルに明示行高
- HTML を Chrome で表示：ナビ / 検索 / `NG のみ` フィルタ / 画像 / 表 / 行番号つきログ / SQL 表示 / 添付リンク すべて動作、外部リソース参照ゼロ（`grep -cE 'https?://'` → 0）
- **未検証**：実 Excel（Microsoft Excel.app はインストール済みだが目視確認していない）でのレンダリング、印刷スケーリング、Excel 内のハイパーリンククリック挙動

## 2.2 未着手：Phase 1（Web UI）以降

第 4 部に詳細仕様。

## 2.3 ファイル構成（現状）

```
test_tool/
├─ package.json  tsconfig.json  bun.lock  .gitignore  README.md
├─ docs/
│  ├─ 00-proposal.md      全体構想（長期・参考）
│  ├─ 01-mvp.md           ★実装契約（正）。レイアウト規則が全部ここ
│  ├─ 03-ai-assist.md     AI 支援の方針（Phase 2、任意）
│  └─ 04-handoff.md       このファイル
├─ src/
│  ├─ cli.ts              52 行  init / export（commander、shebang 付き）
│  ├─ core/
│  │  ├─ types.ts          87 行  zod スキーマ（データ契約の実体）
│  │  ├─ store.ts         163 行  YAML 読み書き、sha256、証拠 ID 採番
│  │  ├─ detect.ts         82 行  貼り付け/DnD → kind 判定
│  │  └─ verdict.ts        53 行  判定導出・色定義
│  └─ export/
│     ├─ format.ts         88 行  並び順・日時/バイト整形・表示幅・SQL 判定
│     ├─ xlsx.ts          508 行  Excel 出力
│     ├─ html.ts          324 行  HTML 出力
│     └─ index.ts          52 行  exportProject(dir, outDir)
├─ examples/
│  ├─ gen-sample-images.py  PIL でモック画面 1280×800 を生成
│  └─ sample/               サンプルプロジェクト（TC-001/002/003）
└─ tests/                   schema / detect / verdict / export（48 テスト）
```

## 2.4 開発コマンド

```bash
cd <repository-root>
bun install
bun test                                  # 48 テスト
bunx tsc --noEmit                         # 型チェック
bun run src/cli.ts export examples/sample # サンプル出力
bun run src/cli.ts init /path/to/new-project
# サンプル画像の再生成（PIL は AI_works の uv 環境にある）
python3 examples/gen-sample-images.py
```

依存：`exceljs@4.4` / `yaml@2.9` / `zod@4.5` / `papaparse@5.7` / `image-size@2` / `commander@15`。
ランタイムは **Bun**（ExcelJS の `addImage` が Bun で問題なく動くことを確認済み）。ただし `Bun.*` 固有 API は意図的に使っていない（`node:fs` を使用）ので Node へ移せる。

---

# 第 3 部：データ契約（これを壊すと全部壊れる）

## 3.1 `project.yaml`

```yaml
name: 顧客管理システム 結合テスト   # 必須。出力ファイル名にもなる
tester: 王                        # 新規 case の既定値
env: STG                          # 同上
verdicts: [OK, NG, 保留, 対象外, 未実施]
excerptLines: 30                  # Excel に載せるテキスト行数の上限
imageMaxWidth: 640                # Excel 画像の最大幅 px
```

## 3.2 `cases/<id>.yaml`（1 ケース 1 ファイル）

```yaml
id: TC-001                  # [A-Za-z0-9_-] のみ。ファイル名 / sheet 名になる
title: ログイン正常系
precondition: ユーザー U001 が有効状態で存在すること
tester: 王
date: "2026-09-04"          # ★必ず引用符付き文字列（Date にしない）
env: STG
verdict: OK                 # 空ならステップから導出（§3.5）
note: ""                    # 備考（不具合票番号など）
steps:
  - no: 1
    action: ログイン画面を開く
    expected: ログイン画面が表示される
    actual: 表示された
    verdict: OK
evidence:
  - id: E01                 # 採番後は不変。欠番可。絶対に振り直さない
    kind: image             # image | table | text | file
    category: 画面           # 画面|DB|ログ|API|コマンド|設定|エラー|その他
    caption: ログイン画面表示
    step: 1                 # 所属ステップ。無所属は null（表示は「共通」）
    file: E01.png           # evidence/<caseId>/ からの相対
    capturedAt: "2026-09-04T10:12:03+09:00"
    source: "Chrome - ログイン"   # 出典：ウィンドウ名 / SQL 文 / URL / コマンド / ファイル名
    note: タイトルが「ログイン」であること   # 確認ポイント
    sha256: "..."           # 保存時に計算。出力物には出さない
    lang: ""                # kind=text のとき log|json|xml|sql|plain
    originalName: ""        # kind=file のとき元ファイル名
    size: 0                 # kind=file のときバイト数
```

## 3.3 証拠ファイルの置き方

- `evidence/<caseId>/<id>.<ext>`、`kind=file` のみ `<id>-<originalName>`
- `table` → UTF-8 CSV（BOM なし、`\n`、1 行目ヘッダ、全部文字列扱い）
- `text` → UTF-8、改行は `\n` に統一
- `image` → 元のバイナリのまま（再エンコードしない）

## 3.4 証拠の並び順（出力共通）

`step` 昇順でグループ化 → `step: null` は最後（ラベル「共通」）→ グループ内は `id` 昇順。

## 3.5 判定の導出（`case.verdict` が空のとき）

いずれかが NG → **NG** ／ 次に 保留 → **保留** ／ 次に 未実施 → **未実施** ／ 全部 対象外 → **対象外** ／ それ以外 → **OK**。ステップ 0 件 → **未実施**。

## 3.6 貼り付け / ドラッグ&ドロップ → kind 判定表（`src/core/detect.ts`）

| 入力 | kind | 備考 |
|---|---|---|
| クリップボード画像 / `.png .jpg .jpeg .gif .webp` | `image` | 再エンコードしない |
| `.csv .tsv` ファイル | `table` | papaparse。tsv は `\t` |
| 貼り付けテキスト：2 行以上 かつ 各行のタブ数が同じで 1 以上 | `table` | DB ツールからのコピー |
| 貼り付けテキスト：その他 | `text` | **カンマ区切りは表とみなさない**（ログにカンマは普通にある）。`lang` は `{`/`[` 開始で JSON.parse 成功→json、`<` 開始→xml、他 plain |
| `.txt .log .json .xml .md .sql .yaml .yml .ini .conf .properties .sh .bat .ps1` かつ 2MB 以下 | `text` | `lang` は拡張子から |
| その他の拡張子、またはテキスト系 2MB 超 | `file` | 添付のみ |

---

# 第 4 部：既存コードの API（Phase 1 はこれを呼ぶだけ）

すべて `src/` 配下。**新規に別の保存ロジックを書かないこと。**

## 4.1 `core/types.ts`

zod スキーマ：`ProjectSchema` / `TestCaseSchema` / `StepSchema` / `EvidenceSchema`
型：`Project` / `TestCase` / `Step` / `Evidence` / `Kind` / `Category` / `Lang`
定数：`KINDS` / `CATEGORIES` / `LANGS`
※ YAML の空欄（null）は `optionalText` で `""` に寄せている。zod 4 なので `.nullish().transform()` を使う。

## 4.2 `core/store.ts`

```ts
projectFile(dir): string            casesDir(dir): string
caseFile(dir, caseId): string       evidenceDir(dir, caseId): string
isValidCaseId(id): boolean          sha256(data: Buffer|string): string
loadProject(dir): Project           saveProject(dir, project): void
listCaseIds(dir): string[]          loadCase(dir, caseId): TestCase
loadCases(dir): TestCase[]          saveCase(dir, testCase): TestCase
nextEvidenceId(evidence): string    // 既存最大 + 1、2 桁ゼロ埋め（100 以上は 3 桁）

interface NewEvidence {
  kind: Kind; category: Category;
  caption?: string; step?: number|null; source?: string; note?: string;
  lang?: Lang; capturedAt?: string; originalName?: string;
  data: Buffer | string;    // image/file はバイナリ、table/text は UTF-8 文字列
  extension?: string;       // 省略時は kind から（image→png, table→csv, text→txt, file→bin）
}
addEvidenceFile(dir, testCase, input: NewEvidence): TestCase
// → ファイルを書き、ID を採番し、case に追記して保存し、更新後の case を返す
```

## 4.3 `core/detect.ts`

```ts
type DetectInput =
  | { type: "clipboardImage" }
  | { type: "file"; fileName: string; size?: number }
  | { type: "text"; text: string };
detect(input): { kind: Kind; lang: Lang }
detectFile(fileName, size?) / detectText(text) / detectLang(text) / extensionOf(fileName)
TEXT_MAX_BYTES = 2 * 1024 * 1024
```

## 4.4 `core/verdict.ts`

```ts
DEFAULT_VERDICTS = ["OK","NG","保留","対象外","未実施"]
VERDICT_STYLES: Record<string, {fill: string; font: string; ...}>   // §5.2 の色
verdictStyle(verdict) / deriveVerdict(steps) / resolveVerdict(testCase) / countVerdicts(...)
```

## 4.5 `export/index.ts`

```ts
exportProject(projectDir: string, outDir?: string): Promise<{
  outDir: string; xlsxPath: string; htmlPath: string; files: string[];
}>
// 既定の出力先は <projectDir>/exports/<yyyyMMdd-HHmmss>/
```

## 4.6 `export/format.ts`（両出力の共通ヘルパ）

`orderEvidence` / `stepLabel` / `formatCapturedAt` / `formatBytes` / `formatStamp` / `formatDirStamp` / `toLines` / `displayWidth`（CJK を 2 幅で数える）/ `looksLikeSql` / `safeFileName`

---

# 第 5 部：出力レイアウト（実装済み。変更時は必ず守る）

## 5.1 Excel 共通ルール

| 項目 | 規定 |
|---|---|
| 列幅 | A 6 / B 36 / C 32 / D 32 / E 8 / F 14 |
| フォント | 全体 `Meiryo UI` 10、コード/SQL/ログは `MS Gothic` 10（ExcelJS にブック既定フォントが無いのでセルごとに指定） |
| 塗り | ラベル `F2F2F2` / ヘッダ `D9D9D9` / 証拠タイトル行 `DDEBF7` |
| 罫線 | 細線 `BFBFBF` |
| 判定色 | OK `C6EFCE`/`006100`、NG `FFC7CE`/`9C0006`、保留 `FFEB9C`/`9C5700`、対象外 `E7E6E6`/`595959`、未実施 塗りなし |
| ページ | A4 横、横 1 ページに収める（`fitToWidth:1, fitToHeight:0`） |
| シート名 | case id。`[ ] : * ? / \` を除去、31 文字まで、重複は `_2` |

## 5.2 シート構成

**`サマリ`（先頭）**：プロジェクト名 / 出力日時 / 環境 → 判定別件数表（+ 合計）→ ケース一覧（テストID はシートへのハイパーリンク、判定は着色）。

**各 case シート**：
```
r1  テストID | TC-001 | 件名 | ログイン正常系 | 判定 | OK
r2  実施者   | 王      | 実施日 | 2026-09-04  | 環境 | STG
r3  前提条件 | B..F 結合（折返し・明示行高）
r4  備考     | （note が空なら行ごと出さない）
    （空行）
    ステップ表ヘッダ： No | 操作 | 期待結果 | 実際結果 | 判定 | エビデンス
    ステップ行（結合なし・折返しあり・行高は設定しない＝Excel の自動調整に任せる）
    （空行）
    「エビデンス」見出し
    証拠ブロック×N
```

**証拠ブロック**：
```
タイトル行： A=E01(太字) | B..D 結合「画面｜ログイン画面表示」 | E=Step 1 / 共通 | F=2026-09-04 10:12:03   ← 行全体 DDEBF7
出典行（source がある時）： B..F 結合「出典: ...」（DB かつ SQL らしければ「SQL: ...」+ 等幅）
本文（kind 別）
確認ポイント行（note がある時）： B..F 結合「確認ポイント: ...」
（空行）
```

**kind 別の本文**
| kind | 描画 |
|---|---|
| image | `imageMaxWidth` に等比縮小（拡大はしない）、B 列に `tl:{col:1,row:r-1}` / `editAs:'oneCell'` で配置、その後 `ceil(高さpx/20)+1` 行進める |
| table | B 列からヘッダ行（`D9D9D9`・太字・罫線）+ データ行（**全部文字列**）、最後に「N 件」 |
| text | 先頭 `excerptLines` 行を 1 行 1 Excel 行：A=行番号（灰・右寄せ）、B..F 結合（等幅・折返し・明示行高）。超過時は「…（全 N 行、残りは HTML 版を参照）」 |
| file | B..F 結合「添付ファイル: name（328 B）」+ `files/<caseId>/<file>` へのリンク |

## 5.3 HTML

単一ファイル、CSS/JS インライン、画像は `data:` base64、**外部参照ゼロ**。
上部バー（プロジェクト名・出力日時・環境・判定別件数）／左ナビ（ケース一覧 + 検索 + `NG のみ`）／本文（ケースごとに 見出し → 情報表 → ステップ表（エビデンス列は証拠へのアンカー）→ 証拠ブロック）。
証拠ブロックは `id="ev-<caseId>-<evId>"`。text は行番号つき `<pre>`、`excerptLines` 超は `<details>` に格納。画像クリックで別タブ表示。印刷時は左ナビ非表示・ケースごとに改ページ。JS は 40 行以内（検索・NG フィルタ・画像クリックのみ）。

---

# 第 6 部：実装で踏んだ罠（同じ穴に落ちないこと）

1. **ExcelJS の結合セルは行高を自動調整しない。** 折返しテキストを結合したら、必ず `row.height` を明示する。
   計算：表示幅 ÷ 結合範囲の幅（CJK は 2 文字幅で数える）→ 行数 → 1 行 14pt、上限 20 行。
2. **行高はその行の「全セル」の最大で決める。** 結合セルだけ見て決めると、A 列（幅 6）の「前提条件」や F 列（幅 14）の 19 文字の日時が切れる。→ 該当行は 28pt になる。回帰テストで固定済み。
3. **表のセル値は必ず文字列で書く。** 数値変換されると `00012` のような前ゼロ ID が壊れる。
4. **画像の後は必ずピクセル高から行を進める**（`ceil(高さ/20)+1`、既定行高 20px）。忘れると次のブロックが画像に重なる。
5. **`capturedAt` は文字列操作で整形する**（`T`→空白、`+09:00` を除去）。`new Date()` を通すとタイムゾーンでずれる。
6. **ExcelJS は結合セルのマスターのスタイルを従セルへ伝播する**（XML の `s=` が同一）ので、結合範囲への罫線・塗りは安全。
7. **zod 4**：YAML の null を扱うには `.nullish().transform(v => v ?? "")`。
8. **image-size 2** はパスではなく**バイト列**を受け取る。
9. **ESM なので `__dirname` は使えない**（この構成では未使用）。
10. `Meiryo UI` / `MS Gothic` は Windows フォント。**指定は正しい**（納品先が Windows）が、macOS では代替表示になる。

---

# 第 7 部：残作業

## 7.1 Phase 1 — ローカル Web UI（次にやること）

**目的**：テスト実施者が実際にエビデンスを溜められるようにする。ここまでで「使える道具」になる。

### サーバ

- `src/server.ts`：Hono（推奨）で `127.0.0.1` にバインド、ランダムポート、起動時にトークンを発行して URL に含める（外部から叩かれないように）
- `src/cli.ts` に `serve <dir> [--port N]` を追加。起動したらブラウザを開く
- API（全部 `core/store.ts` を呼ぶだけにする）

| メソッド | パス | 内容 |
|---|---|---|
| GET | `/api/project` | `project.yaml` |
| PUT | `/api/project` | 保存 |
| GET | `/api/cases` | 一覧（id / title / verdict / ステップ数 / 証拠数） |
| GET | `/api/cases/:id` | 1 件 |
| PUT | `/api/cases/:id` | 保存（`TestCaseSchema.parse` を通す） |
| POST | `/api/cases` | 新規（id 重複・不正文字を弾く） |
| DELETE | `/api/cases/:id` | 削除（証拠フォルダごと） |
| POST | `/api/cases/:id/evidence` | multipart：ファイル or テキスト + メタ → `detect()` → `addEvidenceFile()` |
| PUT | `/api/cases/:id/evidence/:evId` | メタ（caption / step / note / category / source）更新 |
| DELETE | `/api/cases/:id/evidence/:evId` | 削除（ファイルも） |
| PUT | `/api/cases/:id/evidence/:evId/image` | 注釈焼き込み後の PNG + `annotations` JSON（§7.2） |
| GET | `/api/cases/:id/evidence/:evId/raw` | 証拠ファイルの実体（プレビュー用） |
| POST | `/api/export` | `exportProject()` を呼び、出力パスを返す |

### 画面（React + Vite、`src/ui/`）

1. **ケース一覧**：id / 件名 / 判定バッジ / ステップ数 / 証拠数、新規作成、`NG のみ` フィルタ、右上に「出力」ボタン
2. **ケース編集**：
   - ヘッダ（テストID / 件名 / 前提条件 / 実施者 / 実施日 / 環境 / 備考 / 判定）
   - ステップ表（行追加・削除・並べ替え、`操作 / 期待結果 / 実際結果 / 判定`）
   - 証拠エリア（ステップごとにグループ表示、末尾に「共通」）
3. **証拠の追加**：
   - ページ全体で `paste`（`Ctrl+V`）と drop を受ける → `detect()` で kind 判定 → 追加ダイアログ（種別・カテゴリ・タイトル・所属ステップ・出典・確認ポイント）を出す
   - **どのステップに付けるか**は、直前に選択したステップを既定にする（毎回聞かない）
   - 貼り付け後すぐ一覧に出る。カード上でタイトル / 確認ポイントを直接編集できる
4. **証拠カード**：kind ごとのプレビュー（画像サムネ / 表 / 行番号つきテキスト / 添付情報）、削除、所属ステップ変更

**UX 原則**：スクショを撮って `Ctrl+V` → タイトルを 1 行書く、で終わる。それ以上の操作を要求しない。

### 7.2 スクリーンショット注釈（Phase 1 に含む）

ユーザー要望：「截图标注如果有最好」。

- ツール：**赤枠（矩形）／矢印／番号（①②③ 自動連番）／テキスト／トリミング／元に戻す**、色は赤・青・黄（既定 赤）。図形の選択・移動・リサイズは**作らない**（複雑になるだけ）
- 実装：素の `<canvas>` + ポインタイベント、1 コンポーネントに収める。外部ライブラリを入れない
- 保存時に**注釈を PNG に焼き込む**：
  - 初回注釈時に元画像を `E01.orig.png` として退避
  - `E01.png` は常に「出力用の完成画像」（注釈込み・トリミング済み）
  - `annotations`（座標は**元画像ピクセル基準**）を case YAML に保存 → 再編集は `orig` + `annotations` から再構成
  - 「注釈を削除」= `annotations` を消して `orig` を `E01.png` に戻す
  - `sha256` は完成画像で計算
- **出力側（xlsx / html）は一切変更不要**（今まで通り `file` を読むだけ）

```yaml
# evidence 項目に追加（kind=image のときのみ）
annotations:
  crop: { x: 0, y: 0, w: 1280, h: 800 }      # トリミングなしなら省略
  shapes:
    - { type: rect,   x: 100, y: 200, w: 300, h: 60,  color: red }
    - { type: arrow,  x: 50,  y: 50,  x2: 120, y2: 190, color: red }
    - { type: number, x: 110, y: 190, n: 1, color: red }
    - { type: text,   x: 420, y: 210, text: ここを確認, color: red }
```

### 7.3 Phase 1 に入れる「AI 不要の整形」

貼り付けた内容をきれいにする機能。**AI は使わない**（決定論的で確実・無料・オフライン）：
- JSON / XML の整形、SQL の整形（`sql-formatter`）
- `psql` / `mysql` の枠線つき表出力 → `table` への変換
- ログの行番号表示・キーワードハイライト
- CSV / TSV の区切り推定

### 7.4 Phase 2 以降（今は作らない）

DB 直接接続（読み取り専用・実行前後の差分）、HTTP / コマンド実行の記録、ファイル差分、マスキング（非破壊レイヤ + 出力プロファイル + 出力前スキャン）、Excel テンプレート、Word / PDF、複数 Run、単一実行ファイル化（`bun build --compile`）、デスクトップ化（Tauri：グローバルホットキーでキャプチャ）、自動テスト用 SDK。
AI 支援（OCR / 日本語整形 / ログ抽出）の方針は `docs/03-ai-assist.md`。既定は無効、クラウド送信は明示的に許可した場合のみ。

---

# 第 8 部：次の実装者への指示（そのまま渡せる依頼文）

```
evikit というテストエビデンス生成ツールの Phase 1（ローカル Web UI）を実装してください。
リポジトリ：<repository-root>

前提：
- docs/04-handoff.md（この資料）と docs/01-mvp.md を読むこと。01-mvp.md が仕様の正。
- Phase 0（コア + Excel/HTML 出力 + サンプル + 48 テスト）は完成済み。src/core と src/export は
  原則変更しない。保存処理は必ず core/store.ts の関数を使い、独自の書き込みを増やさない。
- スコープ外（作らない）：マスキング、Word/PDF、DB 直結、HTTP/コマンド実行、テンプレート、
  複数 Run、プラグイン、外部連携、デスクトップ化。
- 方針は「シンプル・明快・確実」。凝った機能や抽象化は不要。

やること：
1. src/server.ts（Hono、127.0.0.1 + ランダムポート + トークン）と CLI の serve コマンド
2. src/ui/（React + Vite）：ケース一覧 / ケース編集（ステップ表）/ 証拠の貼り付け・DnD 追加 /
   証拠カード編集 / 出力ボタン
3. スクリーンショット注釈（canvas、赤枠・矢印・番号・テキスト・トリミング・元に戻す、
   保存時に PNG へ焼き込み、annotations を YAML に保存、元画像は E01.orig.png に退避）
4. AI を使わない整形（JSON/XML/SQL 整形、DB CLI の枠線表 → table 変換）
5. 既存の 48 テストを壊さないこと。新機能にもテストを足すこと。

完了条件：
- bun test が green、bunx tsc --noEmit がクリーン
- bun run src/cli.ts serve examples/sample でブラウザから
  ケース作成 → 画面キャプチャ貼り付け → 注釈 → 出力 まで通しでできる
- 出力した Excel / HTML が Phase 0 と同じレイアウトで壊れていない
```
