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

**Windows alpha 0.2 已加入连续截图和视频证据：**

- 选择用例与步骤，点击「連続スクリーンショット」。`Ctrl+Alt+S` 框选截图，`Ctrl+Alt+R` 重复截取上次区域，逐张自动保存、编号。
- 结束后，在缩略图列表中集中修改说明、顺序和步骤，批量追加说明，并进行图片标注。
- 每个测试步骤新增「テスト条件」栏；可通过「テスト条件…」填写多行测试数据、权限和初始状态。Excel 在对应步骤下方显示。
- 「＋ 動画」导入 MP4 / MOV / AVI / WMV / MKV / WebM / M4V，单个文件最大 2 GiB，分段复制和校验。
- 「動画の確認画像」添加已截取的关键画面，填写 `00:35` 等时间点，自动关联同一步骤。
- Excel 显示关键画面、说明和视频链接；原视频随附件 ZIP 交付。先完整解压，再打开 Excel。
- 客户端首次打开视频时，选择公司允许的本地播放器 `.exe`；Excel 的视频链接则使用系统文件关联。需要可用的播放器与对应解码能力。

没有加入内置录屏、自动提取视频帧或 Excel 内直接播放视频。Windows 标准截图工具仍可以通过原有粘贴方式导入。详细操作见 [客户端指南](native-windows/README.md)。

支持原图保留、框、箭头、编号、文字、裁剪、证据删除恢复。尚未验证真实 Windows 的界面布局、IME、DPI、RDP 交互以及 Excel 的打印和超链接点击。包为未签名的 alpha。

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

- 浏览器版：77 项测试、类型检查、构建，以及此前的 Mac 浏览器流程验证。此次只增加测试条件的数据读写兼容，未改网页版界面；条件的编辑和导出请使用客户端。
- 原生版：48 项核心检查、64 张图片导出、64 MiB 视频附件分段处理、步骤条件读写与导出、Windows x64 编译、Open XML / ExcelJS 验证。
- 尚未实现：内置录屏、自动提取视频帧、DB/API 自动采集、Runs、遮罩、Word/PDF。原生版还不支持 WebP、Shift-JIS、用例删除和恢复列表。大视频请使用客户端；网页版未针对大视频适配和测试。

```bash
bun run check
cd native-windows
dotnet run --project tests/Evikit.Checks -c Release -- ../examples/reference
```

原生开发需要 .NET SDK 10.0.401，普通使用者不需要 SDK。Windows 下执行 `native-windows/build.ps1` 可生成自包含发布 ZIP。详细架构：[浏览器版](docs/05-architecture.md)、[原生版](native-windows/ARCHITECTURE.md)。

当前尚未为 evikit 源码设定公开分发许可证。第三方组件许可见 [说明](native-windows/THIRD-PARTY-NOTICES.md)。
