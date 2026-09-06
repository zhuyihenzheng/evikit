# evikit Desktop for Windows

**独立した Windows Forms 版・alpha 0.1。** ブラウザ版のソースは親ディレクトリに残しています。このディレクトリだけでビルドできます。

Windows のネイティブコントロールと GDI+ を使用します。Chromium、Electron、WebView2、ブラウザ、Bun、Node.js、ローカル HTTP サーバーは使用しません。Excel の作成に Microsoft Excel のインストールも不要です。

## 配布版の起動

[リポジトリ内の release ディレクトリ](../release/) または [GitHub Releases](https://github.com/zhuyihenzheng/evikit/releases) から Windows 用 ZIP を取得できます。Git のみ外部接続できる環境では `git clone --depth 1 https://github.com/zhuyihenzheng/evikit.git` を実行してください。

1. `evikit-win-x64.zip` を Windows の書き込み可能な場所に展開します。
2. **フォルダー内の全ファイルを保持したまま `evikit.exe` をダブルクリック**します。
3. 「プロジェクトを開く…」で `project.yaml` のあるフォルダーを選択するか、「新規プロジェクト…」で作成します。
   同梱の `sample-project` は架空データのデモ用です。まずそのフォルダーを開くと、画像・SQL 付き表・ログを試せます。
4. 用例・ステップを編集し、証拠を追加して「保存」。最後に「成果物を出力」を選択します。

配布フォルダーは .NET ランタイム同梱です。利用者による Bun / .NET / WebView2 の追加インストールは不要です。`evikit.exe` だけを取り出すと動作しません。対象は .NET 10 がサポートする Windows x64 環境です。Mac 上ではこの Windows アプリを起動できません。

会社のアプリ許可ポリシーは別途適用されます。未署名 alpha のため、許可リストや SmartScreen によって実行が制限される場合があります。ネットワークやアプリ制限を解除する機能はありません。

## 操作

- 左側：用例の検索、NG フィルター、用例追加。
- 用例の情報：タイトル・担当・日付・環境・前提条件・備考・判定。判定を空欄にするとステップから導出します。
- テストステップ：追加、編集、上下移動、削除。移動時は証拠との関連も更新します。ステップ削除時は関連証拠を「共通」に移します。
- 証拠：`image / table / text / file` の 4 種と 8 分類。分類は表示用です。
- 貼り付け：クリップボードの画像・テキスト。Excel のセルをコピーした TSV は表として扱います。テキスト中のカンマだけでは自動で表と判定しません。必要に応じて種類を `table` に設定してください。
- ファイル：証拠追加ダイアログから選択、またはメインウィンドウへドロップ。複数ファイルは 1 件ずつ情報を確認します。
- 画像：PNG / JPEG / GIF / BMP を読み込み、元ファイルのバイト列を保持。注釈保存時は別の PNG を生成。枠・矢印・番号・文字・切り抜き・取り消し。原図と注釈は別保存です。
- 表：UTF-8 CSV / TSV。先頭ゼロ、引用符内の改行を保持します。SQL は「出典 / SQL」に保存し、実行しません。
- テキスト：ログ、JSON、XML、SQL、コマンド結果など。実行機能はありません。
- 削除：証拠を一覧から除き、元ファイルと `.trash/native-*/evidence.json` を保持。「削除を復元」はこのセッションの直近の削除を復元します。
- 内蔵スクリーンショット機能は未実装です。Windows の `Win+Shift+S` などで撮影してから貼り付けます。
- `Ctrl+S`：保存。`Ctrl+Shift+V`：証拠貼り付け。注釈ウィンドウの `Ctrl+Z`：取り消し。

編集中は「未保存」が表示されます。用例・プロジェクトの切り替えと終了時に保存／破棄／キャンセルを選べます。証拠操作と出力の前には現在の草稿を保存します。

## 出力

選択した保存先に日時付きフォルダーを作成します。

```text
20260905-123456-xxxxxxxx/
  report.xlsx      # サマリ + 用例シート、画像、表、ログ抜粋
  files/          # 全証拠ファイル
  manifest.json   # ファイルごとのサイズと SHA-256
  delivery.zip    # 上記一式（ZIP 自身を除く）
```

Excel は全セルを文字列で保存し、数式として実行しません。基本列幅は 6 / 36 / 32 / 32 / 8 / 14、画像幅はプロジェクト設定、A4 横・横 1 ページに設定します。長いログは設定行数を抜粋し、全文は添付に残します。非常に長いセルは Excel の制限により出力を拒否します。

Excel からの添付リンクは相対パスです。`report.xlsx` と `files` を一緒に渡すか、ZIP 全体を展開してください。**デスクトップ版は HTML を出力しません。** 出力完了時は保存先をエクスプローラーで開けます。

## ブラウザ版との互換性

- `project.yaml`、`cases/*.yaml` / `*.yml`、`evidence/<caseId>/` を共用します。移行は不要です。
- 同じプロジェクトを両版で同時に開かないでください。共通の `.evikit.lock` で二重起動を拒否します。
- 外部で YAML が変更された場合、古い画面からの上書きを拒否します。草稿を控えてから再読込してください。
- 注釈は原図座標を保持するため、両版で再編集できます。ただし描画エンジンの違いで文字や矢印の見た目は完全一致しません。
- 将来の未知の YAML フィールドは、消して保存する代わりに読み込みエラーにします。
- ブラウザ版の WebP 画像は現在のネイティブ画像処理では非対応です。PNG に変換して取り込んでください。非対応の証拠を黙って抜かして出力することはありません。
- ごみ箱の復元記録は版ごとに異なります。ブラウザ版で削除したものはブラウザ版から復元してください。

## 開発・ビルド

ビルド用 PC のみ .NET SDK 10.0.400（または同 feature band の新しいパッチ）が必要です。初回は Microsoft / NuGet への依存取得が発生します。利用時のアプリには更新確認・テレメトリー・HTTP クライアント・外部通信コードを実装していません。OS や企業の監視ソフトの通信は別です。

```powershell
cd native-windows
./build.ps1
```

PowerShell 実行ポリシーを変更せずに手動でビルドする場合：

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project tests/Evikit.Checks -c Release -- ../examples/reference
dotnet publish src/Evikit.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o artifacts/evikit-win-x64
```

`src/Evikit.Core` は OS 非依存なので Mac でもテストできます。Windows Forms のコンパイルも `EnableWindowsTargeting=true` で可能ですが、UI の実行試験には Windows が必要です。

## 現在の制限

ビルド・Core 28 項・Open XML・ExcelJS 回読・YAML 互換性は確認済みです（詳細：`QA.md`）。Windows 実機での起動、RDP のクリップボード・ドラッグ、IME、日本語フォント、100/150/200% DPI、実 Excel の印刷・ハイパーリンクは現時点で未検証です。検証項目は `WINDOWS-ACCEPTANCE.md` を参照してください。

用例の削除・復元一覧、実行履歴（Runs）、マスク処理、DB / HTTP の自動採集、ログ整形、Word / PDF 出力、端末表の自動認識、Shift-JIS、署名・自動更新・インストーラーは実装していません。ファイルは 25 MiB、テキスト・表は 2 MiB、表はヘッダー + 10,000 行 / 256 列、画像は 80 メガピクセル以内です。表プレビューは先頭 500 行、テキストプレビューは先頭 1,000 行まで。出力はプロジェクト全体をメモリに保持するため、大規模データは未検証です。
