# AI アシスト（任意機能・Phase 2）— 方針メモ

> 2026-09-05 ユーザーの質問「無料で使えるブラウザ AI / 無料 API で品質を上げられないか」への回答を記録。MVP（Phase 0/1）には含めない。

## 1. 結論

1. 「整形」の大半は AI 不要。決定論的な整形（JSON / XML / SQL / CSV / CLI 表出力）を Phase 1 の UI に入れる。無料・オフライン・確実。
2. AI が本当に効くのは 4 つだけ：OCR（画面 → 文字）、日本語の言い回し整形（中→日、下書き→自然な日本語）、長いログから該当行の抽出、証拠キャプションの下書き。
3. エビデンスは顧客データを含むため、**既定はクラウド送信なし**。AI は「プロバイダ設定」で明示的に有効化した場合のみ動く（プロジェクト単位）。
4. 実装は 1 つの抽象 `askLLM(prompt) → text` に集約。OpenAI 互換 Chat Completions を話すエンドポイントなら何でも刺せる（Ollama / LM Studio / Gemini / Groq / OpenRouter / Mistral / 社内ゲートウェイ）。ベンダー別 SDK は入れない。
5. AI の出力は必ず入力欄への「提案」止まり。自動保存しない。

## 2. 選択肢の評価

| 層 | 選択肢 | 費用 | データが外に出るか | 導入条件 | 用途 | 判断 |
|---|---|---|---|---|---|---|
| 0 | 決定論的整形（`JSON.stringify` / `sql-formatter` / `xml-formatter` / papaparse / psql・mysql CLI 表パーサ） | 無料 | 出ない | なし | 整形全般 | **Phase 1 で実装** |
| 1 | Tesseract.js（ブラウザ内 OCR、jpn + eng） | 無料 | 出ない | 言語データ ~10MB を同梱 | 画面 → 検索用テキスト | Phase 2。精度は「そこそこ」、表の復元は不可 |
| 1 | Windows 標準 OCR（`Windows.Media.Ocr`、PowerShell から呼べる）/ macOS Vision | 無料 | 出ない | OS 標準、インストール不要 | 同上、Tesseract より日本語精度が高い | Phase 2 の候補。現場 Windows で使える点が大きい |
| 1 | Chrome 内蔵 AI（Gemini Nano：Translator / Summarizer / Prompt API） | 無料 | 出ない（端末内） | Chrome デスクトップ最新版 + それなりの PC（モデル ~2GB を DL、空き容量・GPU 要件あり）。実行時に `self.Translator` / `LanguageModel` の有無で判定 | 日本語整形・翻訳、要約 | Phase 2。**あれば使う、なければボタンを出さない** |
| 2 | Ollama / LM Studio（ローカル LLM：qwen3 / gemma3 など） | 無料 | 出ない | インストール必要 → 現場 PC では不可のことが多い。開発機・自宅では最良 | 全部 | Phase 2。OpenAI 互換で刺す |
| 3 | クラウド無料枠（Gemini API 無料枠 / Groq / OpenRouter の free モデル / Mistral 無料枠 など） | 無料（枠・条件は頻繁に変わる） | **出る**。無料枠は入力データを学習・改善に使う規約が多い（Gemini 無料枠は明記） | API キー | 全部 | 顧客データには使わない。個人・非機密プロジェクトで明示的に有効化する場合のみ |
| 4 | 有料 API（Claude / OpenAI / Gemini 有料枠 / 社内ゲートウェイ） | 有料だが本用途は極小（キャプション 1 件 = 数百トークン。Claude Haiku 4.5 で $1/$5 per 1M tokens） | 出る。ただし商用規約では学習に使わない（Anthropic 商用 API は既定で不使用） | API キー | 日本語品質が最も高い | 顧客が許可した場合の選択肢。Claude は無料枠なし |

## 3. 設計（Phase 2 で実装する場合）

```yaml
# project.yaml（既定は無効）
ai:
  provider: none            # none | chrome | openai-compatible
  baseUrl: ""               # 例 http://localhost:11434/v1（Ollama）
  model: ""                 # 例 qwen3:8b
  apiKeyEnv: EVIKIT_AI_KEY  # キーは環境変数から。ファイルに書かない
  allowCloud: false         # true にしない限り localhost 以外の baseUrl を拒否
```

- `src/core/ai.ts`：`askLLM(prompt, {system}) → string` 一関数。`provider=chrome` は `LanguageModel` / `Translator` を UI 側で直接使う
- UI のボタン：「日本語に整える」（caption / 実際結果 / 確認ポイント欄）、「該当行を抽出」（text 証拠、キーワード・時刻フィルタを先に適用してから LLM）、「説明文を提案」（step + 証拠種別から）、「OCR」（image 証拠 → 検索用テキストを `ocrText` に保存。出力物には載せない）
- 送信前にプレビュー：何を送るかを表示して OK を押させる（顧客データ事故防止）
- 失敗時は黙って何もしない（トースト 1 行）。AI が落ちても本体機能に影響させない
