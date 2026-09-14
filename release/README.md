# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.6.0-native-alpha**。CSV から用例・操作・テスト条件・期待結果を一括作成できます。同じ ID の行を 1 用例にまとめ、プレビュー後に新規作成。出力は「現在の用例のみ / 全用例」を選べ、画像・日時等の項目設定も引き続き使えます。用例削除・復元、複数画像、連続撮影、動画添付を含みます。[GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.6.0-native-alpha)。

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

ZIP の期待値：`a4369c2486ff700f996f42918c1f5465c7f8115684c0d32446e3ee305a6f213c`

ソース commit：`8fea59cf31782dec6364ecb4f47b025a2a48a2d9`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。

複数画像を追加したプロジェクトは Desktop 0.3 以上で使用してください。ブラウザ版と旧クライアントは非対応です。同梱デモの E01 は 2 枚の画像を含みます。

選択出力は Desktop 0.5 以上で行ってください。旧版とブラウザ版は出力設定を適用しません。

CSV 用例テンプレート：[case-template.csv](case-template.csv)。ZIP 内にも同梱。Excel で編集し CSV UTF-8 として保存してから「CSV から用例作成…」で取り込んでください。1 行＝1 ステップ。既存 ID は上書きしません。単用例出力・CSV 用例作成は Desktop 0.6 以上で使用してください。
