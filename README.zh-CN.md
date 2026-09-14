# evikit

把测试步骤与证据整理成 **Excel 和完整附件 ZIP** 的本地工具。支持截图、SQL 与表格数据、日志、API 响应、命令结果和配置文件。

[日本語](README.md) · [源码中的 Windows 发行版](release/) · [GitHub Releases](https://github.com/zhuyihenzheng/evikit/releases) · [原生版架构](native-windows/ARCHITECTURE.md) · [验证记录](native-windows/QA.md)

目前是 **alpha**，包含两种独立界面。项目正本都是文件夹里的 YAML 和证据文件，不依赖数据库、云账号或在线服务。

## 选择版本

| | Windows 原生桌面版 | 浏览器版 |
| --- | --- | --- |
| 界面 | C# / Windows Forms 原生控件 | React 本地网页 |
| 运行依赖 | ZIP 自带 .NET 运行时 | Bun 1.3+ 与浏览器 |
| 浏览器内核 | 不使用 Chromium、WebView2 或 Electron | 使用现有浏览器 |
| 导出 | Excel、附件、校验清单、ZIP；不生成 HTML | Excel、单文件 HTML、附件、校验清单、ZIP |
| 验证 | 已交叉编译、核心与导出检查通过；Windows 实机未验收 | 已在 Mac 浏览器验证完整流程 |

如果 Windows 远程桌面禁止浏览器，请使用原生版。它不绕过公司软件策略，是否允许运行仍由工作环境决定。

## Windows 试用

如果公司电脑只有 Git 可以访问外网，发行包已经直接放进源码仓库：

```powershell
git clone --depth 1 https://github.com/zhuyihenzheng/evikit.git
cd evikit\release
Expand-Archive .\evikit-win-x64.zip -DestinationPath .\evikit
& ".\evikit\evikit-win-x64\evikit.exe"
```

也可以从 [GitHub Releases](https://github.com/zhuyihenzheng/evikit/releases) 下载同一个 `evikit-win-x64.zip`。完整解压后，在「プロジェクトを開く…」选择随包的 `sample-project`，然后编辑、保存并导出 Excel 和 ZIP。**不要只取出 EXE，其他文件包含运行时。**

无需另装 Bun、.NET、WebView2。生成 Excel 也不需要安装 Excel；查看和打印生成的文件时，可以使用 Excel。

快捷键：`Ctrl+S` 保存；`Ctrl+Shift+V` 导入剪贴板图片或文本；标注窗口的 `Ctrl+Z` 撤销。

**Windows alpha 0.5：可选择导出内容，支持多图证据和用例删除/恢复。**

- 选择导出内容：「成果物を出力」中勾选图片、视频、其他附件、日期/取得时间、担当者、环境、测试条件、备注、出典/SQL。默认全部输出，按项目记住。取消图片会排除图片证据块及图片附件；原数据保留。
- 删除用例：选中后点「用例を削除」（或用例列表按 Delete），确认后移到回收列表。「削除した用例」可在重启后恢复，步骤、截图和视频均保留。删除的用例不参与 Excel / ZIP 导出。
- 修改用例名称：选中用例后点击「名前を変更」，或在用例列表按 F2 / 双击。确定后立即保存，列表和后续导出的 Excel 使用新名称。
- 「＋ 画像（複数）」一次导入多图；「画像を追加・整理」继续追加、排序、逐图备注与标注。同一步骤的旧记录可多选后「選択を1件にまとめる」。
- 选择用例与步骤，点击「連続スクリーンショット」。`Ctrl+Alt+S` 框选截图，`Ctrl+Alt+R` 重复截取上次区域，每次自动追加到同一条证据并保存。「新しいエビデンス」可开始另一组。
- 结束后在「共通情報を編集」填写整条证据的说明与步骤；缩略图列表编辑逐图说明、排序和标注。Excel 共用一个证据标题，下面依次输出所有图片。
- 每个测试步骤新增「テスト条件」栏；可通过「テスト条件…」填写多行测试数据、权限和初始状态。Excel 在对应步骤下方显示。
- 「＋ 動画」导入 MP4 / MOV / AVI / WMV / MKV / WebM / M4V，单个文件最大 2 GiB，分段复制和校验。
- 「動画の確認画像」添加已截取的关键画面，填写 `00:35` 等时间点，自动关联同一步骤。
- Excel 显示关键画面、说明和视频链接；原视频随附件 ZIP 交付。先完整解压，再打开 Excel。
- 客户端首次打开视频时，选择公司允许的本地播放器 `.exe`；Excel 的视频链接则使用系统文件关联。需要可用的播放器与对应解码能力。

没有加入内置录屏、自动提取视频帧或 Excel 内直接播放视频。Windows 标准截图工具仍可以通过原有粘贴方式导入。详细操作见 [客户端指南](native-windows/README.md)。

支持原图保留、框、箭头、编号、文字、裁剪、证据删除恢复。尚未验证真实 Windows 的界面布局、IME、DPI、RDP 交互以及 Excel 的打印和超链接点击。包为未签名的 alpha。

## DB 查询结果 / CSV 如何添加

1. 在 DB 工具中导出 **UTF-8 CSV 或 TSV，并包含列名**。一份查询结果对应一条 `table` 证据。
2. 选择用例 →「証拠・プレビュー」→「＋ 証拠」→「ファイル選択…」。也可把 CSV/TSV 文件拖入窗口，自动识别为 `table / DB`。
3. 选择对应「ステップ」；「見出し」填写例如“登録後の顧客レコード”；将实际查询 SQL 放入「出典 / SQL」，将要确认的内容放入「確認事項」。SQL 只保存，不会执行。
4. 点击「追加」后在预览中检查列名和数据。导出会自动按行列生成 Excel，表头有背景色，文本换行，`00012`、长编号和形似公式的值都按字符串保留，CSV 也作为附件交付。

也可以从 DB 结果网格选择“复制（含列名）”，再点「貼り付け」（Ctrl+Shift+V）。制表符分隔的数据会识别成表；若复制的是逗号分隔的纯文本，请在弹窗手动把「種類」改为 `table`、「分類」改为 `DB`。第 1 行作为列名。下面的示例可保存为 UTF-8 CSV：

```csv
customer_id,name,status
00012,テスト顧客,ACTIVE
```

对应 SQL 例如 `SELECT customer_id, name, status FROM customers WHERE customer_id = '00012';`；确认事项可写“customer_id=00012、status=ACTIVE を確認”。操作前后各导出一次时，添加两条表证据，分别命名“変更前”“変更後”，关联到同一个测试步骤即可。

当前会解析分隔符、引用符和单元格内换行，并转换成统一的字符串 CSV；不会排序、删行、自动比对预期值或修改数据。Shift-JIS 需先转 UTF-8；带 `+---+` 边框的终端文本需改为 CSV/TSV 导出。当前上限为 2 MiB、10,000 条数据行和 256 列；界面只预览前 500 条，Excel 输出全部行。

导出选项只控制元数据字段和选中的证据种类。日志/CSV 正文、截图内原有的日期、文字不会被自动删除；导出文件夹时间和 manifest 的生成时间也保留。以普通 file 种类导入的文件由“其他附件”控制。

## Mac / 浏览器试用

先安装 [Bun](https://bun.com/docs/installation)，然后：

```bash
git clone https://github.com/zhuyihenzheng/evikit.git
cd evikit
export DO_NOT_TRACK=1
bun install --frozen-lockfile
bun start
```

首次启动会把固定的架空示例 `examples/reference` 复制到可编辑的 `examples/sample`。已有的编辑目录不会被覆盖。程序会构建并打开本地页面，终端按 `Ctrl+C` 退出。

Windows PowerShell 使用浏览器版时，先设置 `$env:DO_NOT_TRACK = '1'`，再执行 `bun install` 和 `bun start`。

新项目和命令行导出：

```bash
bun run src/cli.ts init ./projects/my-test
bun run serve ./projects/my-test
bun run src/cli.ts export ./projects/my-test
```

`examples/sample/`、`projects/`、构建产物、回收站和会话锁默认不提交 Git。测试使用独立的固定示例，因此不会因试用时增加用例而改变测试结果。

## 记录与交付

1. 建立用例和步骤，填写操作、期待结果、实际结果、判定。
2. 将图片、表格、文本或附件挂到步骤上。
3. SQL 放在出典字段，查询结果保存为字符串 CSV，保留 `00012` 等前导零。
4. 对截图加标注，原图另行保留。
5. 保存并导出，把整个 ZIP 交付给复核者。

四种证据类型是 `image / table / text / file`；八个分类是画面、DB、日志、API、命令、配置、错误、其他。工具只记录已采集结果，**不执行 SQL、HTTP 请求或命令**。

```text
项目文件夹/
  project.yaml
  cases/TC-001.yaml
  evidence/TC-001/E01.png
  evidence/TC-001/E02.csv
  .trash/
  .evikit.lock
  exports/
```

两版共用数据格式和项目锁，同一项目不能同时编辑。保存会检测 YAML 外部修改；导出会检查已记录的证据哈希，避免静默接纳被改动的证据。

Excel 的附件链接是相对路径，交付时应带上 `files/` 或交付完整 ZIP。**成果物 ZIP 不等于可继续编辑的项目**；需要交接编辑工作时，关闭工具后复制完整项目文件夹。

## 本地运行的边界

应用没有云同步、外部 API、CDN、在线字体或更新检查。浏览器版只监听 `127.0.0.1`，原生版没有 HTTP 服务。

初次安装源码依赖需要联网。Bun 在 macOS / Windows 的崩溃报告是运行时的独立行为，所以示例命令在启动 Bun 前设置 `DO_NOT_TRACK=1`，见 [Bun 官方说明](https://bun.com/docs/runtime/environment-variables)。Windows 成品 ZIP 已含依赖，正常使用时不需要下载运行时。操作系统或企业监控软件的联网行为不由本工具控制。

## 验证与开发

- 浏览器版：78 项测试、类型检查、构建，以及此前的 Mac 浏览器流程验证。网页版界面未改；多图证据项目需要 Desktop 0.3 以上，浏览器会拒绝加载以避免丢图。测试条件的编辑和导出也请使用客户端。
- 原生版：84 项核心检查、64 张图片导出、64 MiB 视频附件分段处理、步骤条件读写与导出、Windows x64 编译、Open XML / ExcelJS 验证。
- 尚未实现：内置录屏、自动提取视频帧、DB/API 自动采集、Runs、遮罩、Word/PDF。原生版还不支持 WebP、Shift-JIS、永久清空回收站。大视频请使用客户端；网页版未针对大视频适配和测试。

```bash
bun run check
cd native-windows
dotnet run --project tests/Evikit.Checks -c Release -- ../examples/reference
```

原生开发需要 .NET SDK 10.0.401，普通使用者不需要 SDK。Windows 下执行 `native-windows/build.ps1` 可生成自包含发布 ZIP。详细架构：[浏览器版](docs/05-architecture.md)、[原生版](native-windows/ARCHITECTURE.md)。

当前尚未为 evikit 源码设定公开分发许可证。第三方组件许可见 [说明](native-windows/THIRD-PARTY-NOTICES.md)。
