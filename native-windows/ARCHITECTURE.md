# Windows 原生版架构

这是与浏览器版并行维护的独立 alpha，不替换现有代码，也不包装现有网页。

## 技术选择

C# / .NET 10 + Windows Forms。窗口、列表、表格、文件选择器、剪贴板是 Windows 原生组件；图片由 GDI+ 绘制。没有 WebView2、Chromium、Electron、Bun、Node、HTTP 服务端或浏览器 UI。发布采用 self-contained 文件夹，随程序带运行时；选择文件夹而非单 EXE，避免单文件启动解压与运行时原生库加载的额外复杂性。

舍弃调用原 TypeScript 导出器的方式，因此公司电脑不需要额外批准 Bun。代价是桌面版维护一套 C# 数据校验和导出器；用契约互读和 Excel 独立回读检查控制两套实现的差异，而非宣称所有版式完全一致。

## 边界

```text
Evikit.Windows（仅 Windows）
  MainForm          原生表单、DataGridView、文件夹、剪贴板、草稿状态
  EvidenceDialog    四类录入与元数据
  AnnotationForm    GDI+ 注释、原图坐标、PNG 生成
  CaptureSessionForm / ScreenCapture  内置连续截图、全局快捷键、固定区域
  ImageReviewForm   缩略图、批量说明 / 步骤、排序、标注
        ↓ 直接方法调用
Evikit.Core（可在 Mac 测试）
  Models / Contract 数据契约、验证、判定
  Workspace         项目锁、revision、原子保存、证据哈希、恢复、快照
  CaptureInbox      PNG + YAML 提交间的持久化回收记录
  Tables            UTF-8 CSV/TSV，字符串保真
  Xlsx              标准 ZIP/XML OOXML 写入器
  Export            附件、manifest、ZIP、完整目录发布
        ↓
project.yaml + cases/*.yaml + evidence/<id>/*
```

YamlDotNet 是唯一直接运行时 NuGet 依赖。Excel 写入器采用 .NET 自带 XML / ZIP，不依赖 Office COM，不会启动 Excel，所有单元格为 inlineStr。导出器刻意控制范围：固定报告结构，不充当通用 Excel 编辑库。

## 一致性与失败

与浏览器版共用 PID 文本 `.evikit.lock`，拒绝双开；仅能确认 PID 已不存在时清理旧锁。外部 YAML 修改以原始字节 SHA-256 检测；写入用同目录临时文件、Flush(true)、rename。不是多文件数据库事务，也不能阻止 Git/文本编辑器无视锁修改，使用时不要并行切分支。

证据先写独立文件，再替换 YAML 引用。失败可能留下无引用文件，不覆盖原图。证据元数据保存不会重新认可一个被篡改的哈希。导出和原图编辑会核对已有哈希；旧数据缺少哈希时只能检查存在性，不能追溯证明其未被修改。

桌面 UI 导出调用 `Export.Deliver(workspace, outputRoot)`：在暂存目录分段复制全部附件并计算哈希，构造只含元数据与图片 / 表格 / 文本字节的快照，file 附件不进入内存快照；最后复核 YAML revision。基于这些已复制的数据生成 Excel 和 ZIP，全部成功后改名为完成目录。失败不发布残缺交付物。旧的小文件测试接口 `Snapshot()` 和 `Export.Deliver(snapshot, ...)` 仍保留，不用于大视频。

`Media` 通过扩展名识别视频，仍使用 file 证据和「画面」分类。文件路径导入通过 `NewEvidence.SourcePath`（仅调用参数，不写入 YAML），单个 file 上限 2 GiB，按 1 MiB 缓冲复制到唯一文件名；图片维持 25 MiB。SHA-256 校验、文件提取、删除恢复也不整段读取视频。视频 ZIP 条目不做重复压缩。确认画面是普通 image，在 source 文本中记录视频 ID 和时间点，不扩展数据契约。

## 与浏览器版不同的地方

按照用户要求，桌面版不生成 HTML，仅交付 Excel、附件与 ZIP。Excel 采用独立固定布局，保留关键列宽与图片锚定、字符串数据、相对附件链接，但不是原 ExcelJS 逐像素复刻。GDI+ 字体布局与 Canvas 有差异，保留相同注释数据而非承诺逐像素一样。

PNG/JPEG/GIF/BMP 图像可显示和注释；WebP 不支持。导入图像保留原始字节，标注另存 PNG。表格支持 CSV/TSV，不实现终端表格或 Shift-JIS 自动识别。证据恢复记录独立存放 `.trash/native-*/evidence.json`，当前界面仅支持恢复本会话最后一次删除。

没有用例删除、批次运行历史、蒙版、SQL 执行、API 调用、云同步或自动更新。真实 Windows 界面与 RDP 测试尚未完成，详见 WINDOWS-ACCEPTANCE.md。

alpha 0.2 的连续截图流程、恢复策略和实现边界见 [CAPTURE-DESIGN.md](CAPTURE-DESIGN.md)。项目 YAML 不新增字段；多张图继续作为独立 image 证据关联到同一步骤。批量整理保持非图片证据和全部图片原始信息。

截图本身不改数据契约。后续新增的测试条件使用 Step 可选字段 `condition`；空项目保持兼容，条件随 Step 对象一起保存、排序、删除。Windows UI 新增条件列和多行编辑，Excel 在该步骤主行下方插入跨列条件行，保持原有 6 列宽度、判定位置和图片锚点。共享 TypeScript schema 只做保留 condition 的数据兼容，浏览器界面与导出暂不显示条件。含条件的项目需要新版数据读取器，旧客户端会拒绝未知字段。
