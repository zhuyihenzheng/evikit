# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.3.1-native-alpha**。「名前を変更」/ 一覧の F2 / ダブルクリックによる用例名変更を追加。1 件に複数画像、既存画像エビデンスの統合、連続撮影の同じ行への追加、動画添付、Step ごとのテスト条件を含みます。[同じ ZIP の GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.3.1-native-alpha)。

```powershell
git clone --depth 1 https://github.com/zhuyihenzheng/evikit.git
cd evikit\release
Expand-Archive .\evikit-win-x64.zip -DestinationPath .\evikit
& ".\evikit\evikit-win-x64\evikit.exe"
```

`evikit.exe` だけを取り出さず、展開したフォルダー内の全ファイルを保持してください。Bun、Node.js、ブラウザ、WebView2、.NET の追加インストールは不要です。

ハッシュ確認：

```powershell
Get-FileHash .\evikit-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

期待値：`34c962bcdec5197674721b03aa7de39305425cd23ac59147f1727b81a857daf7`

ソース commit：`a5cfb9aa674c9bc4b2e6f5967e034801d9dc3c63`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。

複数画像を追加したプロジェクトは Desktop 0.3 以上で使用してください。ブラウザ版と旧クライアントは非対応です。同梱デモの E01 は 2 枚の画像を含みます。
