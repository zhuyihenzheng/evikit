# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.2.0-native-alpha**。連続スクリーンショット、画像の集中整理、2 GiB までの動画添付、Step ごとのテスト条件を含みます。[同じ ZIP の GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.2.0-native-alpha)。

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

期待値：`0f1613cf2f17b3d401fca1d8544d12820334d5e1c2f60e779f573bf542f08349`

ソース commit：`3a2ddc5db85f29f97e639fd44d607600993a1e20`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。
