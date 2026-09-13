# Windows 配布版

このディレクトリは、ブラウザを使用しない Windows Forms 版を Git だけで取得するための配布物です。

現在の配布版：**v0.4.0-native-alpha**。「用例を削除」と「削除した用例」からの復元を追加。削除後の用例は出力対象から外れ、画像・動画は保持されます。再起動後も復元できます。用例名変更、1 件に複数画像、既存画像エビデンスの統合、連続撮影の同じ行への追加、動画添付、Step ごとのテスト条件を含みます。[同じ ZIP の GitHub Release](https://github.com/zhuyihenzheng/evikit/releases/tag/v0.4.0-native-alpha)。

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

期待値：`56db53d05cd3ae6fbd4896cc03a0f13fbd80336c1ac93e901991bb3777f070b8`

ソース commit：`883a7b2d1bd15bb50da72436bb7ab1cba2c8757b`。ZIP 内の `BUILD-INFO.txt` にも記録しています。既に clone している場合は `git pull --ff-only` 後、ZIP を新しいフォルダーに展開して使用してください。ユーザーのプロジェクトを上書きする必要はありません。

状態は alpha です。Windows 実機、RDP、IME、DPI、実 Excel の印刷とリンク操作は未検証です。

複数画像を追加したプロジェクトは Desktop 0.3 以上で使用してください。ブラウザ版と旧クライアントは非対応です。同梱デモの E01 は 2 枚の画像を含みます。
