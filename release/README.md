# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.5.0-native-alpha**。出力前に画像・動画・その他の添付、実施日 / 取得日時・担当者・環境・条件・備考・出典 / SQL を選択できます。プロジェクトごとに設定を記憶し、元データは保持します。用例削除・復元、名前変更、複数画像、連続撮影、動画添付、テスト条件も含みます。DB / CSV の追加方法は同梱 README に記載。[同じ ZIP の GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.5.0-native-alpha)。

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

期待値：`e2b16c48eda5b2b51dc94993d3f511eaa1e0a60756cbb345ac4d31d195154031`

ソース commit：`f48eee2cd310cab1e8ed055fecfe99014e71a709`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。

複数画像を追加したプロジェクトは Desktop 0.3 以上で使用してください。ブラウザ版と旧クライアントは非対応です。同梱デモの E01 は 2 枚の画像を含みます。

選択出力は Desktop 0.5 以上で行ってください。旧版とブラウザ版は出力設定を適用しません。
