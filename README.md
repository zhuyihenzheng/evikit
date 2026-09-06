# evikit

テストの操作・期待結果・実際結果に証拠を紐づけ、提出用の **Excel と添付 ZIP** を作るローカルツールです。スクリーンショットだけでなく、SQL と行データ、ログ、API 応答、設定ファイルを構造化して保存します。

[中文说明](README.zh-CN.md) · [Windows ダウンロード](https://github.com/zhuyihenzheng/evikit/releases) · [Windows 操作ガイド](native-windows/README.md) · [アーキテクチャ](docs/05-architecture.md)

**現在は alpha です。** ブラウザ版と、ブラウザを使わない Windows 原生デスクトップ版があります。Windows 版はビルドとデータ・出力検証まで完了し、Windows 実機の画面・RDP 操作は未検証です。

## どちらを使うか

| | Windows デスクトップ版 | ブラウザ版 |
| --- | --- | --- |
| インターフェース | Windows Forms のネイティブウィンドウ | ローカル Web UI |
| 実行環境 | .NET 10 対応の Windows x64 | macOS / Windows、Bun 1.3+ とブラウザ |
| 利用者による追加インストール | 配布 ZIP は .NET ランタイム同梱 | Bun と npm パッケージの取得が必要 |
| ブラウザ・WebView2・Bun | 使用しない | Bun + ブラウザを使用 |
| 出力 | Excel / 添付 / manifest / ZIP。HTML なし | Excel / 単一 HTML / 添付 / manifest / ZIP |
| 現在の状態 | 0.1 alpha、Windows へのクロスコンパイル済み | 0.2 alpha、Mac の実ブラウザで一連の操作を確認済み |

ブラウザ禁止の Windows リモートデスクトップには、Windows デスクトップ版が対象です。アプリの実行可否には会社の許可ポリシーが適用されます。

## Windows で試す

1. [Releases](https://github.com/zhuyihenzheng/evikit/releases) のプレビュー版から **`evikit-win-x64.zip`** をダウンロードします。
2. ZIP を展開し、フォルダー内の **`evikit.exe`** をダブルクリックします。
3. 「プロジェクトを開く…」で同梱の **`sample-project`** を開きます。
4. ステップ・証拠を編集し、「成果物を出力」で Excel と ZIP を作ります。

**EXE だけを取り出さず、展開したフォルダー全体を保持してください。** Bun / .NET / WebView2 / Excel 本体の追加インストールは不要です。Excel 本体は、生成した Excel を閲覧・印刷する場合に使用します。

- `Ctrl+S`：保存。
- `Ctrl+Shift+V`：クリップボードの画像・テキストを証拠として追加。
- 注釈ウィンドウの `Ctrl+Z`：直前の操作を取り消す。
- **アプリ内のスクリーンショット取得は未実装。** Windows の `Win+Shift+S` などで撮影後、evikit に貼り付けます。RDP 内のキー・クリップボード転送は接続設定によります。

未署名の試用版です。詳しい操作・制限は [Windows README](native-windows/README.md)、実機確認項目は [Windows 受け入れチェック](native-windows/WINDOWS-ACCEPTANCE.md) を参照してください。

## Mac / ブラウザで試す

[Bun](https://bun.com/docs/installation) 1.3 以上を用意し、リポジトリを clone します。

```bash
git clone https://github.com/zhuyihenzheng/evikit.git
cd evikit
export DO_NOT_TRACK=1
bun install --frozen-lockfile
bun start
```

Windows PowerShell からブラウザ版を使う場合は、Bun をインストール後、`$env:DO_NOT_TRACK = '1'` を設定して同じ `bun install` / `bun start` を実行します。

`bun start` は初回のみ架空データの `examples/reference` を **`examples/sample` にコピー**してから、画面をビルド・起動します。既存の `examples/sample` は上書きしません。終了はターミナルの `Ctrl+C` です。

編集用の `examples/sample/` と `projects/` は Git 管理対象外です。自動テストは固定の `examples/reference/` を参照するので、デモを編集してもテスト入力は変わりません。

### 自分のプロジェクト

```bash
bun run src/cli.ts init ./projects/my-test
bun run serve ./projects/my-test

# 自動ブラウザ起動なし・ポート指定
bun run serve ./projects/my-test --port 4317 --no-open

# ブラウザを使わず成果物だけ出力
bun run src/cli.ts export ./projects/my-test
```

起動時に表示される接続 URL を使用してください。接続キーはローカルセッションに交換され、アドレス欄から消えます。更新後は `bun run build` とサーバー再起動が必要です。

## 証拠を記録して提出する

1. **用例とステップを作成**：操作、期待結果、実際結果、判定を記入します。
2. **証拠を紐づける**：画像、CSV / TSV、ログ、JSON、API 応答、ファイルなどを追加します。
3. **出典と確認点を残す**：SQL は SQL のまま、DB 結果は文字列の行データとして保存します。`00012` の先頭ゼロを保持します。
4. **画像に注釈**：枠、矢印、番号、文字、切り抜き。原図を保持し、注釈は別 PNG と座標データで保存します。
5. **保存・出力**：編集内容を確定し、Excel と全添付を ZIP で渡します。

SQL、HTTP リクエスト、コマンドを実行するツールではありません。取得済みの証拠を整理して成果物にするツールです。

## データ形式と安全な保存

```text
my-test/
  project.yaml
  cases/TC-001.yaml
  evidence/TC-001/
    E01.png
    E02.csv
    E03.log
  .trash/             # 復元用の記録
  .evikit.lock        # 使用中のプロジェクトを保護
  exports/            # 生成物（再生成可能）
```

- **プロジェクトは普通のフォルダー**。YAML と証拠ファイルが正本で、DB やサーバーアカウントは不要です。
- **4 種類**：`image / table / text / file`。表示カテゴリは画面・DB・ログ・API・コマンド・設定・エラー・その他です。
- **両版でファイルを共用**。同一プロジェクトの同時起動をロックで拒否し、外部 YAML 編集との保存競合を検出します。
- **ハッシュ検証**：記録済み SHA-256 が一致しない証拠は出力を中止します。原図の上書きや削除 ID の再利用を避けます。
- **完全な出力単位**：スナップショットから生成し、全出力成功後に日時フォルダーを公開します。

Excel 内の添付リンクは相対パスです。**XLSX 単独ではなく `files/` と一緒に渡すか、納品 ZIP を渡してください。** 編集を引き継ぐ場合は、evikit を終了して元のプロジェクトフォルダーをコピーします。納品 ZIP は編集プロジェクトの代わりにはなりません。

## オフライン利用

アプリの編集・出力処理に、クラウド、外部 API、CDN、外部フォント、更新確認はありません。ブラウザ版のサーバーは `127.0.0.1` のみを使用し、Windows 原生版は HTTP サーバー自体を起動しません。

ソースからの初回セットアップ・ビルド依存取得には通信が必要です。**Bun の macOS / Windows クラッシュ報告は別の仕組み**なので、ブラウザ版では Bun の起動前に `DO_NOT_TRACK=1` を設定してください。[Bun 環境変数の公式説明](https://bun.com/docs/runtime/environment-variables)

Windows 配布 ZIP の利用にはパッケージ取得は不要です。企業の監視ソフトや OS 自体の通信、アプリ実行許可までは本ツールが制御しません。

## 検証状況・制限

| 対象 | 確認済み | 未確認 / 未実装 |
| --- | --- | --- |
| ブラウザ版 | 75 tests、型検査、ビルド、Mac で編集→証拠→注釈→出力 | Windows サーバー実行、実機スマートフォン |
| Windows 原生版 | 28 Core checks、x64 ビルド、Microsoft Open XML 検証、ExcelJS 回読、両版 YAML 互読 | Windows ウィンドウ、RDP、IME、DPI、実 Excel の印刷・クリック |
| 共通 | 四種証拠、CSV 文字列保持、原図保留、保存競合、納品 ZIP | 自動撮影、DB / API 自動採集、Runs、マスキング、Word / PDF |

原生版は WebP / Shift-JIS / 端末表の自動認識に未対応です。用例削除と復元一覧も未実装です。両版とも大規模プロジェクト、複数端末同時編集、障害復旧の網羅検証は完了していません。

詳細：[ブラウザ版の設計と QA](docs/05-architecture.md) · [原生版の設計](native-windows/ARCHITECTURE.md) · [原生版 QA](native-windows/QA.md)

## 開発

```bash
# ブラウザ版
bun run check

# 原生版 Core（.NET SDK 10.0.400 が必要、Mac でも実行可）
cd native-windows
dotnet run --project tests/Evikit.Checks -c Release -- ../examples/reference

# Windows x64 自包含パッケージ（開発 PC でのみ実行）
dotnet publish src/Evikit.Windows -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -o artifacts/evikit-win-x64
```

Windows では `native-windows/build.ps1` が Core チェック・ビルド・説明書とライセンスの同梱・ZIP 作成を行います。ビルド用 SDK は利用者の Windows PC には不要です。

```text
src/core/          ブラウザ版のデータ契約・保存・編集処理
src/export/        Excel / HTML / ZIP
src/ui/            React の編集画面
src/server.ts      ローカル API
native-windows/    独立 C# / Windows Forms 版
examples/reference/ 固定の架空サンプル
examples/sample/   ローカル編集用（Git 対象外）
tests/             ブラウザ版の自動テスト
docs/              設計・過去の検討記録
```

`docs/00`〜`04` は検討・Phase 0 の履歴を含みます。実装済みの範囲は本 README と各版の現在の設計書・QA を優先してください。

## ライセンス

evikit 自体の公開配布ライセンスは未設定です。GitHub に置いたことを、任意の再配布・商用利用の許諾と解釈しないでください。依存ライブラリの権利はそれぞれのライセンスに従います。Windows 同梱ライブラリは [第三者ライセンス](native-windows/THIRD-PARTY-NOTICES.md) に記載しています。
