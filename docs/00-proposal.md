# 通用测试成果物（エビデンス）生成工具 — 需求分析与整体方案（v0.1 提案）

> 状态：**已按反馈裁剪**（2026-09-04）：脱敏、Word/PDF、模板、多 Run 等移出 MVP；MVP 契约见 `01-mvp.md`。本文件保留为长期方向参考。
> 日期：2026-09-04　工作名：`evikit`（CLI：`evi`），可随时改名。

---

## 0. 结论先行

| 项目 | 结论 |
|---|---|
| 做什么 | 一个"本地优先、证据结构化、一套数据多种输出"的测试成果物工具。测试人员执行测试时把截图 / SQL 结果 / 日志 / 文件 / API / 命令等证据挂到对应步骤上，工具负责编号、排版、脱敏、生成 Excel / HTML / PDF / Word 等成果物。 |
| 形态 | 核心库（TypeScript）+ CLI + 本地 Web UI。项目数据 = 一个纯文件夹（可 git / 共享盘 / 压缩交付）。桌面壳（全局快捷键截图、托盘）作为后期打包层。 |
| 关键设计 | 所有证据类型都渲染到 7 种"原语"（图片 / 表格 / 代码块 / 文本 / 键值 / 差分 / 附件）；每种导出格式只实现这 7 种原语 → 新增证据类型与新增输出格式互不牵连。 |
| 安全 | 零联网、凭据不入项目、脱敏是非破坏性覆盖层并按导出プロファイル应用、导出前预检扫描、每条证据带哈希与时间戳。 |
| 第一步 | Phase 0：先用假数据做出一份 Excel + HTML 样板成果物，确认"长相"再写工具。 |

---

## 1. 使用场景与工作流分析

### 1.1 使用者与环境（假设，请在 §5 确认）

- **执行者**：手工测试为主（単体 / 結合 / 総合 / 受入 / リグレッション），部分自动化。日本 SI 现场为主 → Windows 客户端、Excel 文化、客户指定样式、外部软件安装受限、数据不能出网。
- **复核者**（レビュー者 / リーダー）：要快速判断"证据能否支撑判定"，找 NG、找漏。
- **交付对象**（客户 / PM）：要求格式统一、可归档、敏感信息已处理、必要时能证明证据未被篡改。
- **开发环境**：macOS；**目标环境**：Windows 为主，需同时支持 macOS / Linux。

### 1.2 工作流与痛点

| 阶段 | 现状做法 | 痛点 |
|---|---|---|
| 准备 | 測試仕様書（Excel）里已有 テストID / 手順 / 期待結果 | 要把 case 再抄一遍到エビデンス文件；格式各项目不同 |
| 执行 | 操作 → 截图 → 贴 Excel → 手写说明；DB 查询结果、日志、JSON 也**截图** | 截图是"文字的照片"：不能搜、不能脱敏、不能比对、体积大。步骤 ↔ 证据的对应靠人工排版，多了就乱 |
| 整理 | 调图大小、编号、加红框、写実際結果 / 判定 | 大量机械劳动；返工（NG → 修正 → 再測）时历史丢失或重复贴 |
| 复核 | 打开 Excel 逐条看 | 看不出"该看哪里"；证据缺失 / 错位难以发现；无法按 NG 过滤 |
| 交付 | 手工马赛克 / 删列脱敏，改成客户格式，打包 | 脱敏遗漏是事故；换格式就返工；无法证明证据未被改动 |

**核心矛盾**：证据以"图片 + 手工排版"的形式产生，而真正需要的是"结构化、可复用、可脱敏、可多格式输出"的数据。

### 1.3 从场景推出的硬性要求

1. 步骤 ↔ 证据的对应关系由**工具**维护（ID、引用、排版），不靠人。
2. 文本类证据（SQL 结果、日志、CSV/JSON、API、命令）**以文本 / 表格保存**，图片仅作为一种呈现方式（客户坚持要"截图"时可渲染成图）。
3. 输出格式**可替换**：同一份数据 → 客户 A 的 Excel 样式 / 客户 B 的 Word / 内部复核用 HTML。
4. **不依赖网络**，数据不进入任何服务器；工具本身零遥测。
5. 现场可能**不能安装软件** → 需要单文件可执行，以及"系统自带截图 + 粘贴"的零依赖兜底流程。
6. 不同项目的字段、判定值、语言、样式都不同 → 必须可配置、可模板化。

---

## 2. 对需求的补充与调整（我的判断）

| # | 补充 / 调整 | 理由 |
|---|---|---|
| 1 | **证据以结构化数据保存，图片只是渲染结果之一** | 可搜索、可脱敏（按列 / 按正则）、可比对（前后差分）、体积小。"截图 DB 结果"的做法在工具内用"渲染成图"替代。 |
| 2 | **从既有測試仕様書（Excel / CSV / Markdown）导入用例** | 测试人员不应重复录入 case；列映射按模板保存，同项目复用。 |
| 3 | **区分"用例（仕様）"与"执行记录（実施 / Run）"** | 支持再測（NG → OK）与历史保留；成果物导出指定 Run（默认最新）。UI 上默认单 Run，不增加复杂度。 |
| 4 | **两类输出角色：查看用（自包含 HTML）与交付用（Excel / PDF / Word）**，同源生成 | HTML 适合复核（搜索、折叠、NG 过滤、打印）；Excel 是日本 SI 的事实交付标准。 |
| 5 | **模板驱动的 Excel 输出** | 客户样式是固定的；没有模板模式，工具就不会被现场采用。内置版式用于无指定格式的场景。 |
| 6 | **脱敏 = 非破坏性覆盖层 + 导出プロファイル + 导出前预检** | 原始证据保留（内部复核需要真实数据），对客户导出时应用规则；预检扫描防漏。 |
| 7 | **每条证据自带完整性元数据**（SHA-256、采集时刻、采集者、来源） | 复核与审计可信；导出包含清单（manifest）。 |
| 8 | **提供自动化 SDK / CLI** | Playwright / pytest 等脚本可直接写入同一格式，手工与自动化产出同一种成果物。 |
| 9 | **本地优先、凭据不入项目** | DB / API 凭据以"连接别名"引用，实体存 OS 钥匙串或项目外的本地文件。 |
| 10 | **明确边界（不做）** | 不做测试管理系统（不替代 TestRail / Jira / Backlog，只做链接与推送）、不做自动化测试框架、不做在线协作服务。 |

> 说明：您贴的"客户端更新"文本是一种"操作记录型成果物"（操作 → 版本 → 验证 → 备注），本方案将其归为 `command` / `note` / `keyvalue` 类证据，可以直接覆盖，不作为特例设计。

---

## 3. 整体方案

### 3.1 工具形态与取舍

**推荐：核心库 + CLI + 本地 Web UI，后期加桌面壳。**

```
┌──────────────────────────────────────────────────────────┐
│  Shells（入口）                                             │
│   CLI (evi)   │  本地 Web UI（浏览器）  │  桌面壳(Tauri, 后期) │
│               │                        │  快捷键/托盘/原生截图 │
├──────────────────────────────────────────────────────────┤
│  Core（TypeScript 库，Shell 无关）                          │
│   数据模型 & schema │ 文件存储 │ 解析器(CSV/JSON/XML/log/HAR) │
│   脱敏引擎 │ 差分引擎 │ 渲染原语 │ 导出流水线 │ 插件加载        │
├──────────────────────────────────────────────────────────┤
│  Providers（可插拔）                                        │
│   采集：截图 / 剪贴板 / DB / HTTP / 命令 / 文件               │
│   导出：HTML / XLSX / DOCX / PDF / Markdown / JSON bundle    │
│   导入：Excel / CSV / Markdown 仕様書                         │
│   集成：Backlog / Jira / Redmine / TestRail（后期）           │
├──────────────────────────────────────────────────────────┤
│  Storage：项目 = 纯文件夹（YAML + CSV/JSON + 附件）            │
└──────────────────────────────────────────────────────────┘
```

为什么不是其他形态：

| 备选 | 不选为主的原因 |
|---|---|
| Electron 桌面应用优先 | 包大、签名 / 分发成本高；核心价值（数据模型 + 导出）与壳无关，壳应最后做 |
| Excel 插件 / VBA | 证据类型受限、无法结构化保存、难扩展、难做脱敏与差分 |
| VS Code 扩展 | 测试人员未必用 VS Code |
| 纯浏览器扩展 | 只适合 Web 应用测试，覆盖不了 DB / 日志 / 命令 |
| 纯 CLI + 手写 Markdown | 贴截图、标注、复核体验差 |

**打包**：`bun build --compile`（或 Node SEA）产出单文件可执行，面向不能安装软件的现场；后期 Tauri 产出安装包 / 便携 exe。

### 3.2 数据模型

```
Project
 ├─ Suite (可选分组：機能 / 画面 / 章节)
 │   └─ TestCase (仕様)
 │        ├─ id, title, precondition, tags, customFields
 │        └─ Step[] : no, action, expected
 └─ Run (実施：env, build, tester, date)
      └─ CaseResult (caseId, verdict, actualSummary, ticketLinks)
           ├─ StepResult[] : stepNo, actual, verdict, evidenceRefs[]
           └─ Evidence[]
```

**Evidence（证据）通用字段**：

| 字段 | 说明 |
|---|---|
| `id` | 内部稳定 ID（ULID）；展示 ID（如 `TC-001-S02-E01`）在导出时按版面规则生成，避免增删导致重编号 |
| `type` | screenshot / table / text / file / http / command / config / error / diff / note |
| `caption` | 标题（一句话说明这条证据证明什么） |
| `capturedAt` / `capturedBy` / `host` | 采集时刻、采集者、机器 |
| `source` | 来源元数据：窗口标题 / URL / 连接别名 / 文件路径 / 命令 |
| `content` | 结构化内容（见 §3.3） |
| `attachments[]` | 原文件引用（内容寻址，带 SHA-256、大小、MIME） |
| `annotations[]` | 红框 / 箭头 / 编号 / 文字（图片）；高亮行 / 高亮单元格（表格 / 文本） |
| `masks[]` | 脱敏覆盖层（区域 / 列 / 正则命中 / 路径） |
| `checkpoints[]` | 確認ポイント：复核者该看哪里 |
| `hash` | 内容 SHA-256（采集时计算） |
| `links` | 所属 step（可多对多：一条证据支撑多个步骤） |

**判定值**：默认 `OK / NG / 保留 / 対象外 / 未実施`，项目级可改（○/×、PASS/FAIL 等）。
**步骤可选**：只有 case 级期待 / 実際的项目，证据直接挂在 case 上。

### 3.3 证据类型 → 渲染原语

| 类型 | 采集方式 | 保存内容 | 渲染原语 |
|---|---|---|---|
| `screenshot` | OS 截图 / 剪贴板粘贴 / 拖入图片 / 后期快捷键 | PNG + 元数据（窗口标题、分辨率、URL、时刻） | Image |
| `table` | 执行 SQL / 粘贴 TSV·CSV（A5:SQL、DBeaver、SSMS 复制）/ 导入 CSV | 列定义 + 行数据 + 来源（SQL 文本、连接别名、执行时刻、件数、耗时） | Code(SQL) + Table |
| `text` / 日志 | 粘贴 / 文件 / 命令输出 | 全文附件 + 摘录范围（行区间 / 时间区间）+ 高亮规则 | Code + Attachment |
| `file` (CSV/TXT/JSON/XML/其他) | 拖拽 / 路径 | 原文件（内容寻址）+ 预览（CSV 前 N 行成表、JSON/XML 格式化） | Table / Code + Attachment |
| `http` | 内置执行 / 导入 curl / HAR / Postman | method, url, headers, body, status, respHeaders, respBody, duration | KeyValue + Code |
| `command` | 内置执行 / 粘贴 | cmd, cwd, exitCode, stdout, stderr, duration | KeyValue + Code |
| `config` | 文件 / 粘贴 | 路径 + 内容 + 关注键 | Code |
| `error` | 粘贴 / 截图 | 文本或图片 + 严重度 | Code / Image |
| `diff` | 选两条 table / text / file 证据 | 差分结果（行级 / 单元格级）；典型用法：DB 操作前后、期待 CSV vs 实际 CSV | Diff |
| `note` | 手写 | Markdown | Text |

**7 种原语**：`Image`、`Table`、`Code`、`Text`、`KeyValue`、`Diff`、`Attachment`。
每个导出器只需实现这 7 种；每个证据类型只需实现 `parse()` 与 `toPrimitives()`。

### 3.4 项目存储结构（项目 = 文件夹）

```
my-project/
├─ project.yaml            # 项目信息、字段定义、判定值、脱敏规则、导出プロファイル、标签
├─ cases/                  # 仕様：一 case 一文件，避免多人冲突
│   ├─ TC-001.yaml
│   └─ TC-002.yaml
├─ runs/
│   └─ 2026-09-04-round1/
│       ├─ run.yaml        # env, build, tester, date
│       ├─ results/
│       │   └─ TC-001.yaml # 各 step 的 actual / verdict / evidenceRefs
│       └─ evidence/
│           └─ TC-001/
│               ├─ 01JX...-screenshot.png
│               ├─ 01JX...-screenshot.meta.yaml
│               ├─ 01JX...-table.yaml     # sql, conn alias, executedAt, rows -> rows.csv
│               ├─ 01JX...-table.rows.csv
│               └─ 01JX...-text.meta.yaml # 摘录范围、高亮、附件哈希
├─ attachments/            # 大文件，按 sha256 内容寻址，去重
│   └─ sha256/ab/abcd....log
├─ templates/              # 该项目的导出模板（xlsx / docx / html）
├─ exports/                # 生成的成果物（默认 gitignore）
└─ .evikit/                # 索引 / 缓存（可重建，gitignore）
```

选择理由：透明（人能读）、可备份、git 友好、无需数据库服务、单 case 单文件天然避免合并冲突、凭据不在此目录。可选 SQLite 索引仅用于搜索加速，可随时重建。

### 3.5 采集层

| 来源 | 方案 | 零依赖兜底 |
|---|---|---|
| 截图 | macOS `screencapture` / Windows PowerShell（System.Drawing）/ Linux `grim`·`gnome-screenshot`；后期 Tauri 全局快捷键"截图并挂到当前步骤" | `Win+Shift+S` / `Cmd+Shift+4` → 在 UI 里 `Ctrl+V` |
| 剪贴板智能粘贴 | 自动识别：图片 / TSV / CSV / JSON / XML / 纯文本 → 推荐类型，可改 | — |
| DB | 连接别名（`~/.evikit/connections.yaml` 或 OS 钥匙串）；驱动按需加载：pg / mysql2 / mssql / sqlite（oracledb 需客户端，作为可选）；**只读保护**（默认仅允许 SELECT，行数上限）；**前后快照 + 差分** | 从 DB 工具复制结果 → 粘贴成 `table` |
| HTTP | 内置执行；导入 curl 命令 / HAR / Postman 集合 | 粘贴请求 / 响应文本 |
| 命令 | 内置执行（记录 cwd / 环境 / 退出码 / 耗时） | 粘贴命令与输出 |
| 文件 | 拖拽 / 路径；内容寻址存储；生成预览 | — |
| 自动化 | SDK（TS 先行，Python 后续）：`evi.attach(caseId, stepNo, evidence)`；CLI 同等能力 | — |

### 3.6 输出层与导出プロファイル

| 格式 | 用途 | 实现 |
|---|---|---|
| HTML（单文件自包含） | 复核、查看、搜索、NG 过滤、打印 | 模板引擎 + 内联 CSS/JS + base64 图片 |
| Excel (.xlsx) | 交付（日本 SI 标准） | ExcelJS；**内置版式** + **模板模式**（占位符 / 重复区域） |
| Word (.docx) | 部分客户要求 | `docx` 库；模板模式后期 |
| PDF | 归档 | HTML → 无头 Chromium（可选组件）；或用 Excel / 浏览器打印 |
| Markdown + assets | 放 wiki / 仓库 | 直接生成 |
| JSON bundle + manifest | 机器可读、归档、再导入、审计 | 含全部哈希与脱敏报告 |

**导出プロファイル**（存在 `project.yaml`，可命名多个）：
`格式 + 模板 + 脱敏プロファイル + 范围（cases / run / 仅 NG）+ 选项（图片最大宽、摘录行数、是否附原文件、语言标签）`。
例：`customer-xlsx`（客户模板、全脱敏、附清单）、`internal-html`（无脱敏、含全部日志）。

### 3.7 成果物版面设计（样例）

**ID 规则**：展示 ID = `{caseId}-S{stepNo}-E{seq}`；case 级证据为 `{caseId}-E{seq}`。步骤表里引用 `E01, E02`，证据块标题里回指步骤。

**Excel（每 case 一 sheet，或按 suite 连续）示意**：

```
┌────────────┬────────────────────────┬──────────┬──────────────┬──────┬────────────┐
│ テストID    │ TC-001                  │ 件名      │ ログイン正常系  │ 判定  │ OK         │
│ 実施者      │ 王                      │ 実施日    │ 2026-09-04    │ 環境  │ STG        │
│ 前提条件    │ ユーザー U001 が存在すること                                               │
├────┬───────────────────────┬──────────────────────┬──────────────────┬──────┬─────────┤
│ No │ 操作                   │ 期待結果              │ 実際結果          │ 判定  │ エビデンス │
├────┼───────────────────────┼──────────────────────┼──────────────────┼──────┼─────────┤
│ 1  │ ログイン画面を開く       │ ログイン画面が表示される │ 表示された        │ OK   │ E01     │
│ 2  │ ID/PW を入力し「ログイン」│ ホーム画面へ遷移       │ 遷移した          │ OK   │ E02,E03 │
├────┴───────────────────────┴──────────────────────┴──────────────────┴──────┴─────────┤
│ [E01] 画面 │ ログイン画面表示 │ 2026-09-04 10:12:03 │ Chrome – ログイン │ Step 1        │
│ ┌───────────────────────────────┐                                                   │
│ │           (画像, 幅≤ 640px)    │  確認ポイント: タイトルが「ログイン」であること           │
│ └───────────────────────────────┘                                                   │
├──────────────────────────────────────────────────────────────────────────────────────┤
│ [E03] DB │ 最終ログイン日時が更新されること │ 2026-09-04 10:12:40 │ stg-db (PostgreSQL) │ Step 2 │
│ SQL: SELECT user_id, last_login_at FROM users WHERE user_id = 'U001';                 │
│ ┌─────────┬─────────────────────┐                                                    │
│ │ user_id │ last_login_at       │  1 件 / 12 ms                                       │
│ │ U001    │ 2026-09-04 10:12:35 │  確認ポイント: last_login_at が操作時刻以降であること     │
│ └─────────┴─────────────────────┘                                                    │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

**版面规则**：

- 每条证据块固定头部：`[ID] 类型 │ 标题 │ 时刻 │ 来源 │ 所属步骤`，正文，`確認ポイント`。
- 表格类证据用**真实单元格**（表头加底色），不是图片；客户坚持要图时切换"渲染成图"选项。
- 长文本：正文放**摘录**（默认 ≤ 40 行，带行号、高亮行加底色），全文放"付録"sheet 或随包附件并在正文给链接；避免 Excel 单元格 32,767 字符上限与排版崩坏。
- 图片：等比缩放到最大宽度（可配），保留原图为附件。
- 判定着色：OK 绿 / NG 红 / 保留 黄 / 対象外 灰。
- **サマリ sheet**：件数统计（按判定 / 按 suite）、NG 一览（含 ticket 链接）、环境信息、实施者、脱敏プロファイル、生成时刻、工具版本、清单哈希。

**HTML**：左侧 case 导航（带判定色标 + NG 过滤），右侧 case 头部 → 步骤表（可点击跳到证据）→ 证据块（图片灯箱、表格可排序、代码块带行号与高亮、差分双栏）；全局搜索；打印样式直出 PDF。

### 3.8 安全与敏感信息

| 方面 | 方案 |
|---|---|
| 数据位置 | 全部本地；工具不联网、无遥测。仅用户主动触发的 DB / HTTP 采集会访问目标系统 |
| 凭据 | 项目内只存**连接别名**；实体默认存 `~/.evikit/connections.yaml`（600 权限，项目外）；OS 钥匙串接入作为可选组件（原生依赖，与 DB 驱动同样按需加载）；导出包永不包含凭据 |
| 脱敏规则类型 | ① 正则预设：邮箱、电话（日本格式）、マイナンバー、信用卡、JWT / `Bearer`、AWS 密钥、`password=`；② 项目词典（客户名、特定 ID）；③ 表格按列（`email`、`tel`）；④ JSON path / HTTP header 名；⑤ 图片区域模糊 / 涂黑（手动框选；后期 OCR 辅助定位）；⑥ 整条证据排除 |
| 非破坏性 | 原始内容保留；`masks[]` 作为覆盖层；导出按脱敏プロファイル应用；导出清单记录规则与命中数 |
| 导出前预检 | 扫描导出内容中的高熵字符串与未覆盖的敏感模式 → 列出并要求明确确认后才导出 |
| 完整性 | 每条证据 / 附件 SHA-256；导出 manifest 记录原始哈希与脱敏后哈希；可选对 manifest 签名 |
| 传输 | 可选：导出为 AES 加密 zip（客户交付）；项目文件夹可选 `age` 加密备份 |
| 审计 | `exports/log.jsonl` 记录每次导出（谁 / 何时 / 哪个プロファイル / 哈希） |

### 3.9 扩展点

| 扩展点 | 接口（TS） | 说明 |
|---|---|---|
| 证据类型 | `EvidenceType { id, detect?, parse, toPrimitives, schema }` | 只要映射到 7 种原语即可获得全部导出格式 |
| 采集源 | `CaptureProvider { id, capture(options) → Evidence }` | 截图 / DB / HTTP / 命令 / 自定义 |
| 导出器 | `Exporter { id, render(primitives, layout, profile) }` | 新格式只实现 7 种原语 |
| 导入器 | `Importer { id, import(file, mapping) → TestCase[] }` | 各种仕様書格式 |
| 脱敏规则 | `MaskRule { id, applyTo(primitive) }` | 正则 / 词典 / 列 / 路径 / 图片区域 |
| 集成 | `Integration { linkTicket, pushResult }` | Backlog / Jira / Redmine / TestRail / Xray |
| 自定义字段 | `project.yaml` 声明式 | 出现在 UI 与所有导出 |
| 模板 | HTML/MD（Nunjucks）；xlsx 模板（占位符 + 重复区域）；docx 模板 | 每项目独立 |
| 语言 | UI：ja / zh / en；成果物标签按项目覆盖 | 期待結果 / 実際結果 / 判定 等标签可改 |

插件从 `project/plugins/` 或 npm 包加载；核心 API 语义化版本控制。

### 3.10 技术选型

| 层 | 选择 | 备注 |
|---|---|---|
| 语言 / 运行时 | TypeScript；Bun（开发 + 单文件打包），Node 20+ 兼容 | UI、core、CLI 共享 schema |
| 仓库 | monorepo：`core` / `capture` / `exporters` / `server` / `ui` / `cli` / `sdk`（后期 `desktop`） | |
| schema | zod（运行时校验 + 类型） | YAML/JSON 文件 = 契约 |
| 解析 | papaparse（CSV）、fast-xml-parser、yaml、jsdiff | 纯 JS，便于单文件打包 |
| 图片 | 浏览器 canvas（标注 / 模糊）+ jimp（导出时合成，纯 JS） | 避免 sharp 原生依赖 |
| Excel | ExcelJS（生成 + 模板读写、图片嵌入） | 模板保真需早期验证 |
| Word | `docx`；模板 `docx-templates`（后期） | |
| HTML | Nunjucks + 内联资源 | |
| PDF | Playwright/Chromium（可选组件） | 默认走打印 |
| 服务 | Hono + WebSocket（本地 127.0.0.1，随机端口 + token） | |
| UI | React + Vite；TanStack Table；Konva（标注画布）；Monaco 或轻量代码块 | |
| CLI | citty / commander | |
| DB 驱动 | pg / mysql2 / mssql / better-sqlite3 / oracledb（可选，动态加载） | |

Python 也可行（openpyxl / python-docx / FastAPI），但富 UI（标注、表格、差分）与"UI—core 共享类型"用 TS 更顺；磁盘格式与语言无关，后续可加 Python SDK 给 pytest 用。

### 3.11 风险与需要早期验证的点

| 风险 | 对策 |
|---|---|
| Excel 模板保真（ExcelJS 读写复杂模板可能丢条件格式 / 图表） | Phase 0–1 用一份真实客户模板做 spike；不行则退到"内置版式 + 客户模板仅取样式参数" |
| PDF 依赖体积（Chromium ~150MB） | PDF 作为可选组件；默认 HTML / Excel 打印 |
| Windows 现场不能装软件 | 单文件可执行；截图走系统自带 + 粘贴；DB 走粘贴结果 |
| 大日志（数百 MB） | 只存摘录 + 外部路径引用 + 哈希；附件大小上限可配 |
| Oracle 驱动安装麻烦 | 允许粘贴查询结果替代直连 |
| 团队并发编辑 | 一 case 一文件，冲突交给 git；不做实时协作 |
| 未签名 exe 被 SmartScreen 拦 | 后期考虑代码签名；先以便携方式 + 说明文档过渡 |

---

## 4. 分阶段计划

| 阶段 | 目标 | 交付 |
|---|---|---|
| **Phase 0 · 样板** | 确认"长相"和数据契约 | ① 用假数据手工生成的 Excel + HTML 样板成果物；② `project.yaml` / case / run / evidence 的 schema 与示例项目；③ Excel 模板 spike 结论 |
| **Phase 1 · MVP** | 能在真实测试中用起来 | 项目初始化；仕様書导入（Excel/CSV 列映射）；case / step 编辑；剪贴板智能粘贴 + 拖拽（图片 / 表格 / 文本 / 文件）；标题 / 確認ポイント / 判定；HTML + Excel（内置版式）导出；基础脱敏（正则 + 列 + 图片手动区域）；CLI 基本命令；单文件打包 |
| **Phase 2 · 采集与交付强化** | 覆盖全部证据类型与交付要求 | DB 直连（只读保护、前后差分）；HTTP / 命令执行器；文件差分；日志摘录工具（时间区间、高亮）；图片标注编辑器；导出プロファイル + 预检扫描 + manifest；Excel 模板模式；Word / PDF；多 Run 与再測 |
| **Phase 3 · 桌面壳与生态** | 顺手与集成 | Tauri 壳（全局快捷键截图、托盘、原生截图）；TS / Python SDK；Backlog / Jira / Redmine 链接与 TestRail 推送；插件机制开放；i18n 完整化 |

MVP 刻意保持小：目的是证明数据模型与"原语 → 多格式"设计成立，并让真实项目先用起来提意见。

---

## 5. 需要您确认的决策点（附我的默认）

| # | 问题 | 我的默认 |
|---|---|---|
| 1 | 终端用户的 OS 与安装限制？现场有没有 Node？ | Windows 为主且可能不能安装 → 单文件可执行；开发在 macOS，三平台都支持 |
| 2 | Excel 是否为必须的交付格式？能否提供一份**真实客户エビデンス模板**和一份**真实測試仕様書**样本（脱敏后即可）？ | Excel 必须；Phase 0 先用内置版式，拿到样本后做模板 spike 并据此设计导入列映射 |
| 3 | 单人使用还是团队共用？ | 先单人；文件夹设计已兼容 git / 共享盘团队用 |
| 4 | 常用数据库？ | 优先 PostgreSQL / MySQL / SQL Server；Oracle 先走粘贴结果 |
| 5 | 技术栈：TypeScript（推荐）还是 Python？ | TypeScript |
| 6 | UI 与成果物语言？ | 成果物默认日文；UI 日 / 中 / 英可切换 |
| 7 | 是否需要多 Run（再測历史）在 MVP 就可见？ | MVP 隐藏（单 Run），Phase 2 开放 |
| 8 | 工具名 | `evikit` / `evi`，可改 |

---

## 附录 A · CLI 示例（设想）

```bash
evi init my-project                                   # 建项目（交互选语言 / 判定值 / 字段）
evi import spec.xlsx --map templates/spec-map.yaml    # 从仕様書导入 case
evi serve                                             # 打开本地 Web UI
evi capture screen --case TC-001 --step 2 -c "ホーム画面"   # 截图挂到步骤
evi sql --conn stg-db --case TC-001 --step 2 "select ..."   # 执行 SQL 存为 table
evi http --case TC-002 --curl 'curl -X POST ...'      # 记录 API 请求 / 响应
evi run --case TC-003 -- ./deploy.sh                  # 记录命令与输出
evi add file ./out/result.csv --case TC-004 --diff expected.csv  # 文件 + 差分
evi check --profile customer                          # 脱敏预检
evi export --profile customer-xlsx                    # 生成交付物
evi export --profile internal-html --open             # 生成复核用 HTML 并打开
```

## 附录 B · `project.yaml` 示例（设想）

```yaml
name: 顧客管理システム 結合テスト
locale: ja
verdicts: [OK, NG, 保留, 対象外, 未実施]
fields:                      # 自定义字段
  case: [{ key: reviewer, label: レビュー者 }, { key: priority, label: 優先度 }]
  run:  [{ key: build, label: ビルド番号 }]
labels:                      # 成果物标签覆盖
  expected: 期待結果
  actual: 実際結果
masking:
  profiles:
    internal: { rules: [] }
    customer:
      rules:
        - { type: regex, preset: email }
        - { type: regex, preset: jp-phone }
        - { type: column, columns: [email, tel, address] }
        - { type: dictionary, file: masks/customer-names.txt }
        - { type: header, names: [Authorization, Cookie] }
exports:
  customer-xlsx: { format: xlsx, template: templates/customer.xlsx, masking: customer, imageMaxWidth: 640, excerptLines: 40, attachOriginals: false }
  internal-html: { format: html, masking: internal, attachOriginals: true }
```

## 附录 C · 一条 `table` 证据的元数据示例（设想）

```yaml
id: 01JX6K3H2M9QZ8V4F1N7R5T2WY
type: table
caption: 最終ログイン日時が更新されること
capturedAt: 2026-09-04T10:12:40+09:00
capturedBy: wang
source: { kind: sql, connection: stg-db, dialect: postgresql, durationMs: 12 }
content:
  sql: "SELECT user_id, last_login_at FROM users WHERE user_id = 'U001';"
  columns: [user_id, last_login_at]
  rowsFile: 01JX6K3H2M9QZ8V4F1N7R5T2WY.rows.csv
  rowCount: 1
checkpoints:
  - last_login_at が操作時刻（10:12:35 前後）以降であること
masks:
  - { type: column, columns: [email] }   # 若结果含 email 列则覆盖
links: [{ step: 2 }]
hash: sha256:3b1f...c9e2
```
