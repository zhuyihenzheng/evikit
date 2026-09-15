# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.6.1-native-alpha**。DB / Excel の列名付き CSV・TSV を Ctrl+V すると自動整形・表プレビュー。クリップボードに画像と表が同時にある場合は表を優先し、大量貼り付けの文字数制限も修正しました。CSV 用例作成、単用例出力、出力項目の選択、用例削除・復元、複数画像、連続撮影、動画添付を含みます。[GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.6.1-native-alpha)。

```powershell
git clone --depth 1 https://github.com/zhuyihenzheng/evikit.git
cd evikit\release
Expand-Archive .\evikit-win-x64.zip -DestinationPath .\evikit
& ".\evikit\evikit-win-x64\evikit.exe"
```

`evikit.exe` だけを取り出さず、展開したフォルダー内の全ファイルを保持してください。Bun、Node.js、ブラウザ、WebView2、.NET の追加インストールは不要です。

ハッシュ確認：

```powershell
Get-FileHash .\evikit-win-x64.zip, .\case-template.csv -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

ZIP の期待値：`005946ebc1d28143be31c377e32e36ff19f4bccddf2eb7b35a332da472833680`

ソース commit：`0f2c36571a8c1f6c704346dfb96c7d7f5bd1386c`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

検証：111 Core checks、8 Windows クリップボード / 本体ダイアログ checks、ブラウザ 78 tests・型検査・ビルドが通過。状態は alpha です。利用先の DB アプリ、AWS RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。

複数画像を追加したプロジェクトは Desktop 0.3 以上で使用してください。ブラウザ版と旧クライアントは非対応です。同梱デモの E01 は 2 枚の画像を含みます。

選択出力は Desktop 0.5 以上で行ってください。旧版とブラウザ版は出力設定を適用しません。

CSV 用例テンプレート：[case-template.csv](case-template.csv)。ZIP 内にも同梱。Excel で編集し CSV UTF-8 として保存してから「CSV から用例作成…」で取り込んでください。1 行＝1 ステップ。既存 ID は上書きしません。単用例出力・CSV 用例作成は Desktop 0.6 以上で使用してください。

DB の貼り付け：「＋ 証拠」→ 種類「自動（表 / テキスト）」→ 左の「原文 / 編集」に列名付きデータを Ctrl+V → 右の表を確認 →「追加」。既に text として保存済みの証拠を自動変換することはありません。
