# evikit MVP 仕様（确定版 v1）

> **2026-09-05 更新：** Phase 0 / Phase 1 的核心功能已实现。本文保留原始需求与固定导出版式；0.2 的架构调整、数据扩展、实现边界和验收结果以 [05-architecture.md](05-architecture.md) 为准。

> 本文件是实现契约：范围、数据格式、版式、验收标准都以此为准。未写明的细节按"最简单、最可靠"的做法处理，不要自行加功能。
> 背景与长期方向见 `00-proposal.md`（仅供参考，凡与本文件冲突的以本文件为准）。

## 1. MVP 是什么

最小可用版本：只做核心功能，先在真实测试里用起来，再按反馈加。
判断标准：一个测试人员能记录一个 case 的步骤 + 证据，并导出一份能直接交付的 Excel 和一份便于复核的 HTML。

## 2. 范围

**做**
1. 项目 = 文件夹（`project.yaml` + `cases/*.yaml` + `evidence/`）
2. 本地 Web UI：用例列表 / 用例与步骤编辑 / 证据粘贴与拖拽（Phase 1）
3. 导出：Excel（固定版式）+ HTML（单文件）
4. CLI：`init` / `export` / `serve`（serve 为 Phase 1）

**不做（MVP 明确排除，不要实现）**
脱敏、Word / PDF、DB 直连、HTTP / 命令执行器、导出模板、多 Run（再測历史）、插件机制、外部集成（Jira 等）、桌面壳、自动化 SDK、全文搜索索引。
（截图标注：Phase 1 做，见 §12；**Phase 0 不涉及**）

**分块**
- Phase 0：`src/core` + `src/export` + CLI `init`/`export` + 示例项目 + 生成的样品 + 测试
- Phase 1：本地 server + Web UI + CLI `serve`

## 3. 技术栈

- Bun 1.3（运行 / 测试），TypeScript，`bun run src/cli.ts ...`；不引入构建步骤
- 依赖：`exceljs`、`yaml`、`zod`、`papaparse`、`image-size`、`commander`；Phase 1 再加 `hono`、`react`、`vite`
- **先做 10 行 smoke test 验证 ExcelJS 的 `addImage` 在 Bun 下可用**；不行则整体改用 Node 24 + `tsx`，不要硬扛
- 测试：`bun test`
- 不 `git commit`；不改动 `test_tool/` 之外的文件

## 4. 目录结构

```
test_tool/
├─ package.json  tsconfig.json  .gitignore  README.md
├─ src/
│  ├─ core/
│  │  ├─ types.ts      # zod schema：Project / TestCase / Step / Evidence
│  │  ├─ store.ts      # 读写 project.yaml / cases/*.yaml；添加证据文件；分配证据 ID
│  │  ├─ detect.ts     # 粘贴 / 拖拽内容 → kind 判定（§6）
│  │  └─ verdict.ts    # 判定集计、颜色表
│  ├─ export/
│  │  ├─ xlsx.ts
│  │  ├─ html.ts
│  │  └─ index.ts      # exportProject(dir, outDir)
│  └─ cli.ts
├─ examples/
│  ├─ sample/          # 示例项目（§9）
│  └─ gen-sample-images.py
├─ tests/
└─ docs/
```

## 5. 数据格式

### 5.1 `project.yaml`

```yaml
name: 顧客管理システム 結合テスト
tester: 王              # 新規 case のデフォルト
env: STG                # 同上
verdicts: [OK, NG, 保留, 対象外, 未実施]
excerptLines: 30        # Excel に載せるテキスト行数上限
imageMaxWidth: 640      # Excel 画像の最大幅 px
```

### 5.2 `cases/<id>.yaml`（一 case 一文件，含步骤、结果、证据清单）

```yaml
id: TC-001
title: ログイン正常系
precondition: ユーザー U001 が有効状態で存在すること
tester: 王
date: "2026-09-04"        # 日期一律用引号字符串
env: STG
verdict: OK               # 空则按 §5.4 从步骤导出
note: ""                  # 備考（案件番号など）
steps:
  - no: 1
    action: ログイン画面を開く
    expected: ログイン画面が表示される
    actual: 表示された
    verdict: OK
evidence:
  - id: E01               # 分配后不变；允许有空号；绝不重编号
    kind: image           # image | table | text | file
    category: 画面        # 画面 | DB | ログ | API | コマンド | 設定 | エラー | その他
    caption: ログイン画面表示
    step: 1               # 无所属步骤时为 null
    file: E01.png         # 相对 evidence/<caseId>/
    capturedAt: "2026-09-04T10:12:03+09:00"
    source: "Chrome - ログイン"     # 出典：窗口标题 / SQL 文 / URL / 命令 / 文件名
    note: タイトルが「ログイン」であること   # 確認ポイント
    sha256: "..."         # 文件内容哈希（保存时计算，输出物不显示）
    lang: ""              # kind=text 时：log | json | xml | sql | plain（按扩展名/内容判定）
    originalName: ""      # kind=file 时保留原文件名
    size: 0               # kind=file 时字节数
```

- 证据文件放 `evidence/<caseId>/`，文件名 = `<id>.<ext>`；`file` kind 用 `<id>-<originalName>`
- `table` 存为 UTF-8 CSV（无 BOM，`\n` 换行，首行为表头，全部当字符串）
- `text` 原样存 UTF-8，换行统一为 `\n`
- 案件 ID 用作文件名与 sheet 名：只允许 `[A-Za-z0-9_-]`；UI/CLI 建案时校验
- `store.ts` 在保存 case 时同步计算 `sha256`；ID 分配 = 现有最大编号 + 1（两位补零，超过 99 则三位）

### 5.3 输出中的证据顺序

按 `step` 升序分组（1, 2, …），`step: null` 的排在最后，组标签 `共通`；组内按 `id` 编号升序。

### 5.4 判定的导出规则（case.verdict 为空时）

任一步 NG → NG；否则任一步 保留 → 保留；否则任一步 未実施 → 未実施；否则全部 対象外 → 対象外；否则 OK。
无步骤时 → 未実施。

## 6. 粘贴 / 拖拽 → kind 判定表（`detect.ts`，Phase 1 的 UI 也用它）

| 输入 | kind | 备注 |
|---|---|---|
| 剪贴板图片 / `.png .jpg .jpeg .gif .webp` | `image` | 原样保存，不转码 |
| `.csv .tsv` 文件 | `table` | papaparse 解析；tsv 用 `\t` |
| 粘贴文本：≥2 行，且每行 tab 数相同且 ≥1 | `table` | 从 DB 工具复制的结果 |
| 粘贴文本：其余 | `text` | **含逗号的文本不当作表格**（日志里有逗号）；`lang`：以 `{` `[` 开头且 JSON.parse 成功 → json；以 `<` 开头 → xml；否则 plain |
| `.txt .log .json .xml .md .sql .yaml .yml .ini .conf .properties .sh .bat .ps1`，且 ≤ 2 MB | `text` | `lang` 按扩展名：log/json/xml/sql，其余 plain |
| 其他扩展名，或文本类 > 2 MB | `file` | 仅作附件 |

## 7. Excel 版式（固定，不做模板）

### 7.1 通用

| 项目 | 规定 |
|---|---|
| 列宽 | A 6 / B 36 / C 32 / D 32 / E 8 / F 14 |
| 字体 | 全部单元格 `Meiryo UI` 10；代码 / SQL / 日志用 `MS Gothic` 10。ExcelJS 无工作簿默认字体，写每个单元格时统一套用 |
| 对齐 | 文本：上对齐 + 折返；`No` / `判定` 列：居中 |
| 填充色 | 标签格 `F2F2F2`；表头 `D9D9D9`；证据标题行 `DDEBF7` |
| 边框 | 头部块、步骤表、数据表：细线 `BFBFBF` |
| 判定色 | OK 填 `C6EFCE` 字 `006100`；NG 填 `FFC7CE` 字 `9C0006`；保留 填 `FFEB9C` 字 `9C5700`；対象外 填 `E7E6E6` 字 `595959`；未実施 无填充 |
| 页面 | A4 横向，横向缩放到 1 页宽（`fitToWidth: 1, fitToHeight: 0`） |
| sheet 名 | = case id；去掉 `[ ] : * ? / \`，截到 31 字，重复则加 `_2` |
| **合并单元格不会自动调高**：所有"合并 + 折返"的行必须显式设 `height`：行数 = ceil(显示宽度 / 合并区宽度)，CJK 算 2 个字符宽，每行 14pt，上限 20 行 |
| **表格数据全部写成字符串**，防止 ID 前导零被吃掉 |
| **图片后必须按像素高推进行号**：`ceil(height / 20) + 1` 行（默认行高 20px），这些行不要设高度 |

### 7.2 「サマリ」sheet（第一个 sheet）

列宽 A 16 / B 40 / C 10 / D 10 / E 12 / F 12 / G 10

| 行 | 内容 |
|---|---|
| 1 | A `プロジェクト` ｜ B name |
| 2 | A `出力日時` ｜ B `yyyy-mm-dd HH:mm` |
| 3 | A `環境` ｜ B project.env |
| 5 | 表头：A `判定` ｜ B `件数` |
| 6.. | 每个 verdict 一行（按 project.verdicts 顺序，判定格着色），最后一行 `合計` |
| 空一行 | |
| n | 表头：A `テストID` ｜ B `件名` ｜ C `判定` ｜ D `ステップ数` ｜ E `エビデンス数` ｜ F `実施日` ｜ G `実施者` |
| n+1.. | 每 case 一行；A 为超链接到对应 sheet（`#'<sheet>'!A1`）；判定着色 |

### 7.3 case sheet（每 case 一个）

| 行 | 内容 |
|---|---|
| 1 | A `テストID` ｜ B id ｜ C `件名` ｜ D title ｜ E `判定` ｜ F verdict（着色） |
| 2 | A `実施者` ｜ B tester ｜ C `実施日` ｜ D date ｜ E `環境` ｜ F env |
| 3 | A `前提条件` ｜ B..F 合并 precondition（折返，显式行高） |
| 4 | 仅当 note 非空：A `備考` ｜ B..F 合并 note；否则本行不输出 |
| 空一行 | |
| 步骤表头 | A `No` ｜ B `操作` ｜ C `期待結果` ｜ D `実際結果` ｜ E `判定` ｜ F `エビデンス`（表头填充、粗体、边框） |
| 步骤行 | 每 step 一行；F = 该步骤的证据 ID 用 `, ` 连接（如 `E01, E02`）；不合并、折返、**不设行高**（让 Excel 自动调高） |
| 空一行 | |
| 节标题 | A `エビデンス`（粗体） |
| 每条证据 | 见下 |

**每条证据的块**（按 §5.3 顺序）：

| 行 | 内容 |
|---|---|
| 标题行 | A id（粗体）｜ B..D 合并 `{category}｜{caption}`（粗体）｜ E `Step {n}` 或 `共通` ｜ F capturedAt（`yyyy-mm-dd HH:mm:ss`）；整行填充 `DDEBF7` |
| 出典行（source 非空时） | A 空 ｜ B..F 合并 `出典: {source}`；category 为 DB 且 source 像 SQL 时前缀改为 `SQL: ` 并用等宽字体；折返 + 显式行高 |
| 内容 | 按 kind：见下 |
| 確認ポイント行（note 非空时） | A 空 ｜ B..F 合并 `確認ポイント: {note}`；折返 + 显式行高 |
| 空一行 | |

**内容渲染**

| kind | 渲染 |
|---|---|
| image | 等比缩放到 `imageMaxWidth`（小图不放大），左上角放在 B 列当前行（`tl: {col: 1, row: r-1}`，`editAs: 'oneCell'`），然后按像素高推进行号 |
| table | 从 B 列起，表头行（填充 `D9D9D9`、粗体、边框），数据行（边框、字符串），列数超过 5 列时继续向 G、H… 写；最后一行 B `{n} 件` |
| text | 前 `excerptLines` 行，每行一个 Excel 行：A 行号（灰字、右对齐）｜ B..F 合并该行文本（等宽、折返、显式行高）；若超出：追加一行 B `…（全 {total} 行、残りは HTML 版を参照）` |
| file | B..F 合并 `添付ファイル: {originalName}（{size}）`，加超链接到 `files/{caseId}/{file}`（相对导出目录） |

## 8. HTML（单文件）

1. 一个 `.html` 文件，`<meta charset="utf-8">`，全部 CSS / JS 内联，图片用 `data:` base64；**不得出现任何 `http://` / `https://` 引用**
2. 结构：顶栏（项目名、出力日時、各判定件数）｜ 左栏导航（case 列表：id + 件名 + 判定 badge；搜索框；`NG のみ` 复选框）｜ 主区（每 case 一个 `<section id="case-{id}">`）
3. case 区：头部表（テストID / 件名 / 判定 / 実施者 / 実施日 / 環境 / 前提条件 / 備考）→ 步骤表（`エビデンス` 列为跳到 `#ev-{caseId}-{evId}` 的链接）→ 证据块
4. 证据块 `<article id="ev-{caseId}-{evId}">`：标题行（id badge、category、caption、`Step n` / `共通`、时刻）→ 出典（多行时用 `<pre>`）→ 内容 → `確認ポイント`
5. 内容：image = `<img>`（`max-width:100%`，点击用 JS 在新标签打开原图）；table = `<table>`（表头、件数）；text = 带行号的 `<pre>`，前 `excerptLines` 行直接显示，其余放 `<details>`（`残り N 行を表示`）；file = 指向 `files/{caseId}/{file}` 的链接 + 大小
6. 判定颜色同 Excel；打印样式：隐藏左栏，每 case 分页；JS 总量 ≤ 40 行（搜索过滤、NG のみ、图片点击）
7. 所有用户数据经 HTML 转义

## 9. 示例项目 `examples/sample/`（全部假数据，日文）

`project.yaml`：name `顧客管理システム 結合テスト`，tester `王`，env `STG`。

**TC-001 ログイン正常系（OK）**
- precondition：ユーザー U001（一般権限）が有効状態で存在すること
- steps：
  1. ブラウザで `https://stg.example.local/login` を開く ｜ ログイン画面が表示される ｜ 表示された ｜ OK
  2. ユーザーID「U001」とパスワードを入力し「ログイン」を押下 ｜ ホーム画面へ遷移し、ヘッダーに氏名「山田 太郎」が表示される ｜ 遷移・氏名表示を確認 ｜ OK
  3. DB の `users.last_login_at` を確認 ｜ 操作時刻で更新されている ｜ 10:12:35 に更新されていることを確認 ｜ OK
- evidence：E01 image 画面「ログイン画面表示」step1（source `Chrome - ログイン - 顧客管理システム`，note `タイトルが「ログイン」であること`）；E02 image 画面「ホーム画面表示」step2；E03 table DB「最終ログイン日時の更新」step3（source = `SELECT user_id, user_name, last_login_at FROM users WHERE user_id = 'U001';`，1 行）；E04 text ログ「アプリケーションログ」step2（`app.log` 12 行，含 `INFO  login success user=U001`）

**TC-002 ログイン異常系（パスワード誤り）（NG）**
- note：`不具合票 BUG-0123 起票済み`
- steps：
  1. ログイン画面で誤ったパスワードを入力し「ログイン」を押下 ｜ 「ユーザーIDまたはパスワードが正しくありません」と赤字で表示される ｜ 英語メッセージ「Invalid credentials」が表示された ｜ NG
  2. `login_failures` テーブルを確認 ｜ 1 件記録される ｜ 1 件 ｜ OK
- evidence：E01 image 画面「エラーメッセージ表示」step1；E02 text エラー「ブラウザコンソール」step1（lang plain，8 行程度，含 `i18n key not found: auth.invalid_credentials`）；E03 table DB「login_failures 件数」step2

**TC-003 顧客情報 CSV 出力（OK、step3 対象外）**
- steps：
  1. `GET /api/customers/export?from=2026-09-01` を実行 ｜ 200 とダウンロード URL が返る ｜ 200 を確認 ｜ OK
  2. ダウンロードした `customers.csv` を確認 ｜ 3 件、ヘッダー行あり、UTF-8 ｜ 3 件確認 ｜ OK
  3. 圧縮（zip）オプション ｜ 本リリース対象外 ｜ - ｜ 対象外
- evidence：E01 text API「エクスポート API 応答」step1（lang json，含请求行与 JSON 响应）；E02 table その他「customers.csv の内容」step2（3 行，含前导零的 `customer_id` 如 `00012`）；E03 text コマンド「文字コード・行数確認」step2（`file customers.csv` / `wc -l` 的命令与输出）；E04 file その他「ダウンロードした zip」step2（`E04-customers.zip`，用 `zip` 生成的小文件）；E05 text 設定「エクスポート設定」step null（lang plain，yaml 片段）

**示例截图**：用 PIL 画 1280×800 的假画面（ログイン画面 / ホーム画面 / エラー表示），日文字体从 `ls /System/Library/Fonts/ | grep ヒラギノ` 中选；PIL 通过 `python3 examples/gen-sample-images.py` 运行。生成的 PNG 提交到示例项目里。

## 10. CLI（Phase 0 部分）

```
bun run src/cli.ts init <dir>                 # 生成 project.yaml（name = 目录名）、cases/、evidence/、exports/、.gitignore(exports/)
bun run src/cli.ts export <dir> [--out <dir>] # 默认输出 <dir>/exports/<yyyyMMdd-HHmmss>/{name}.xlsx, {name}.html, files/
```

`package.json` 的 `bin` 注册 `evi` → `src/cli.ts`。

## 11. 验收（Phase 0）

1. `bun run src/cli.ts export examples/sample` 成功，产生 `.xlsx`、`.html`、`files/TC-003/E04-customers.zip`
2. 测试（`bun test`）通过，至少包含：
   - schema：示例项目 3 个 case 全部通过 zod 校验；`date` 为字符串
   - detect：TSV 粘贴 → table；含逗号的日志 → text；JSON → text/json；`.csv` → table；`.bin` → file
   - verdict：§5.4 五种情况
   - export：用 ExcelJS 重新打开生成的 xlsx，断言 sheet 数 = 4、第一个 sheet 名 `サマリ`、`TC-001` sheet 的图片数 = 2、`TC-001!B1` = `TC-001`、`TC-003` 表格中 `00012` 原样保留
   - html：`grep -E 'https?://'` 对 HTML 文件无命中（示例数据中的 URL 文本除外——请把示例 URL 写成 `stg.example.local/login` 不带协议，以便这条检查保持简单）
3. `README.md`：安装、命令、目录说明（简短）

## 12. 截图标注（Phase 1，UI 部分；Phase 0 不实现）

用户要求：「截图标注如果有最好」。按最简单可靠的方式做：**在浏览器里用 canvas 画，保存时把标注烧进 PNG**，导出器不需要知道标注的存在。

### 12.1 工具（只做这些）

| 工具 | 说明 |
|---|---|
| 赤枠（矩形） | 拖拽画矩形框，默认红色、线宽 3px |
| 矢印 | 拖拽画箭头（起点 → 终点，终点带箭头头部） |
| 番号 | 点击放一个带圈数字（①②③…自动递增），红底白字，直径 28px |
| テキスト | 点击后输入文字，红色、16px、白色描边以保证可读 |
| トリミング | 拖拽选区后裁剪；裁剪后其他标注坐标随之换算 |
| 元に戻す | 撤销上一步（不做选中 / 移动 / 缩放已画图形，简单为上） |
| 色 | 红 / 蓝 / 黄 三选一，默认红 |

### 12.2 数据与文件

```yaml
# evidence 项新增字段（仅 kind=image）
annotations:            # 坐标以「原图」像素为准
  crop: { x: 0, y: 0, w: 1280, h: 800 }        # 无裁剪时省略
  shapes:
    - { type: rect,   x: 100, y: 200, w: 300, h: 60,  color: red }
    - { type: arrow,  x: 50,  y: 50,  x2: 120, y2: 190, color: red }
    - { type: number, x: 110, y: 190, n: 1, color: red }
    - { type: text,   x: 420, y: 210, text: ここを確認, color: red }
```

- 首次标注时把原图另存为 `E01.orig.png`；`E01.png` 始终是**导出用的成品图**（烧入标注、已裁剪）
- 再次编辑：从 `E01.orig.png` + `annotations` 重建画布，改完重新烧入并覆盖 `E01.png`
- 「標注を削除」：删除 `annotations`，用 `E01.orig.png` 覆盖 `E01.png`
- `sha256` 按 `E01.png`（成品）计算
- 导出器（xlsx / html）不改：照常读 `file`

### 12.3 UI 行为

- 证据卡片上的图片点击 → 打开标注对话框（画布按容器缩放显示，内部坐标始终用原图像素）
- 「保存」= 用 canvas 烧入 → `PUT /api/cases/:id/evidence/:evId/image`（multipart：png + annotations JSON）
- 不引入第三方画布库；纯 `<canvas>` + 指针事件，代码控制在一个组件内
