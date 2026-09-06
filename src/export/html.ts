// §8 HTML（単一ファイル・CSS/JS インライン・画像は data: base64）
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import Papa from "papaparse";
import { evidencePath } from "../core/store";
import type { Evidence, Project, TestCase } from "../core/types";
import { resolveVerdict, verdictStyle, VERDICT_STYLES } from "../core/verdict";
import {
  formatBytes,
  formatCapturedAt,
  formatStamp,
  looksLikeSql,
  orderEvidence,
  stepLabel,
  toLines,
} from "./format";

/** §8.7: ユーザーデータはすべて HTML エスケープする。 */
function esc(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/** 判定を CSS クラス用の ASCII 名にする（日本語をセレクタに出さないため）。 */
const VERDICT_SLUG: Record<string, string> = {
  OK: "ok",
  NG: "ng",
  保留: "hold",
  対象外: "skip",
  未実施: "todo",
};
const slug = (verdict: string) => VERDICT_SLUG[verdict] ?? "none";

function badge(verdict: string): string {
  return `<span class="v v-${slug(verdict)}">${esc(verdict)}</span>`;
}

const MIME: Record<string, string> = {
  png: "image/png",
  jpg: "image/jpeg",
  jpeg: "image/jpeg",
  gif: "image/gif",
  webp: "image/webp",
};

function verdictCss(): string {
  return Object.entries(VERDICT_STYLES)
    .map(([verdict, style]) => {
      const fill = style.fill ? `background:#${style.fill};` : "";
      const color = style.font ? `color:#${style.font};` : "";
      return `.v-${slug(verdict)}{${fill}${color}}`;
    })
    .join("\n");
}

// §8.5 内容レンダリング --------------------------------------------------------

function renderImage(path: string, file: string): string {
  const ext = (file.split(".").pop() ?? "png").toLowerCase();
  const base64 = readFileSync(path).toString("base64");
  return `<img class="ev" alt="${esc(file)}" src="data:${MIME[ext] ?? "image/png"};base64,${base64}">`;
}

function renderTable(path: string): string {
  const rows = Papa.parse<string[]>(readFileSync(path, "utf8"), {
    skipEmptyLines: true,
  }).data;
  if (rows.length === 0) return "";
  const [header, ...body] = rows;
  const head = header!.map((c) => `<th>${esc(String(c))}</th>`).join("");
  const lines = body
    .map(
      (r) => `<tr>${r.map((c) => `<td>${esc(String(c))}</td>`).join("")}</tr>`,
    )
    .join("\n");
  return `<table class="data"><thead><tr>${head}</tr></thead><tbody>\n${lines}\n</tbody></table>
<p class="count">${body.length} 件</p>`;
}

function numbered(lines: string[], offset: number): string {
  const width = String(offset + lines.length).length;
  const body = lines
    .map((line, i) => {
      const no = String(offset + i + 1).padStart(width, " ");
      return `<span class="ln">${no}</span> ${esc(line)}`;
    })
    .join("\n");
  return `<pre class="text">${body}</pre>`;
}

/** §8.5: 先頭 excerptLines 行はそのまま、残りは `<details>` に畳む。 */
function renderText(path: string, excerptLines: number): string {
  const lines = toLines(readFileSync(path, "utf8"));
  const shown = lines.slice(0, excerptLines);
  const rest = lines.slice(excerptLines);
  let html = numbered(shown, 0);
  if (rest.length > 0) {
    html += `\n<details><summary>残り ${rest.length} 行を表示</summary>${numbered(rest, shown.length)}</details>`;
  }
  return html;
}

function renderFile(caseId: string, evidence: Evidence, path: string): string {
  const bytes =
    evidence.size > 0
      ? evidence.size
      : existsSync(path)
        ? statSync(path).size
        : 0;
  const name = evidence.originalName || evidence.file;
  const href = `data:application/octet-stream;base64,${readFileSync(path).toString("base64")}`;
  return `<p class="file"><a href="${href}" download="${esc(name)}">添付ファイル: ${esc(name)}</a>（${esc(formatBytes(bytes))}）</p>`;
}

function renderEvidence(
  projectDir: string,
  project: Project,
  testCase: TestCase,
  evidence: Evidence,
): string {
  const path = evidencePath(projectDir, testCase.id, evidence.file);
  let content = `<p class="missing">ファイルが見つかりません: ${esc(evidence.file)}</p>`;
  if (existsSync(path)) {
    if (evidence.kind === "image") content = renderImage(path, evidence.file);
    else if (evidence.kind === "table") content = renderTable(path);
    else if (evidence.kind === "text")
      content = renderText(path, project.excerptLines);
    else content = renderFile(testCase.id, evidence, path);
  }

  let source = "";
  if (evidence.source !== "") {
    const isSql = evidence.category === "DB" && looksLikeSql(evidence.source);
    const label = isSql ? "SQL: " : "出典: ";
    source =
      isSql || evidence.source.includes("\n")
        ? `<pre class="source">${esc(label + evidence.source)}</pre>`
        : `<p class="source">${esc(label + evidence.source)}</p>`;
  }

  const note =
    evidence.note !== ""
      ? `<p class="note">確認ポイント: ${esc(evidence.note)}</p>`
      : "";

  return `<article class="ev" id="ev-${esc(testCase.id)}-${esc(evidence.id)}">
<div class="ev-head"><span class="eid">${esc(evidence.id)}</span>
<span class="cat">${esc(evidence.category)}</span>
<span class="cap">${esc(evidence.caption)}</span>
<span class="step">${esc(stepLabel(evidence.step))}</span>
<span class="at">${esc(formatCapturedAt(evidence.capturedAt))}</span></div>
${source}
${content}
${note}
</article>`;
}

function renderCase(
  projectDir: string,
  project: Project,
  testCase: TestCase,
): string {
  const verdict = resolveVerdict(testCase);
  const ordered = orderEvidence(testCase.evidence);

  const head: [string, string][] = [
    ["テストID", esc(testCase.id)],
    ["件名", esc(testCase.title)],
    ["判定", badge(verdict)],
    ["実施者", esc(testCase.tester)],
    ["実施日", esc(testCase.date)],
    ["環境", esc(testCase.env)],
    ["前提条件", esc(testCase.precondition)],
    ["備考", esc(testCase.note)],
  ];
  const headRows = head
    .map(([label, value]) => `<tr><th>${label}</th><td>${value}</td></tr>`)
    .join("\n");

  const stepRows = testCase.steps
    .map((step) => {
      // §8.3: エビデンス列は該当ブロックへのアンカーリンク。
      const links = ordered
        .filter((e) => e.step === step.no)
        .map(
          (e) =>
            `<a href="#ev-${esc(testCase.id)}-${esc(e.id)}">${esc(e.id)}</a>`,
        )
        .join(", ");
      return `<tr><td class="c">${step.no}</td><td>${esc(step.action)}</td><td>${esc(step.expected)}</td><td>${esc(step.actual)}</td><td class="c">${badge(step.verdict)}</td><td>${links}</td></tr>`;
    })
    .join("\n");

  const evidence = ordered
    .map((e) => renderEvidence(projectDir, project, testCase, e))
    .join("\n");

  return `<section class="case" id="case-${esc(testCase.id)}" data-id="${esc(testCase.id)}">
<h2>${esc(testCase.id)} ${esc(testCase.title)} ${badge(verdict)}</h2>
<table class="head">${headRows}</table>
<table class="steps"><thead><tr><th>No</th><th>操作</th><th>期待結果</th><th>実際結果</th><th>判定</th><th>エビデンス</th></tr></thead>
<tbody>${stepRows}</tbody></table>
<h3>エビデンス</h3>
${evidence}
</section>`;
}

const CSS = `
*{box-sizing:border-box}
body{margin:0;font-family:"Meiryo UI","Hiragino Sans","Yu Gothic UI",sans-serif;font-size:13px;color:#222;background:#fff}
header.top{position:sticky;top:0;z-index:2;background:#1f3864;color:#fff;padding:10px 16px;display:flex;gap:16px;align-items:baseline;flex-wrap:wrap}
header.top h1{font-size:16px;margin:0}
header.top .stamp{opacity:.85;font-size:12px}
.layout{display:flex;align-items:flex-start}
#nav{width:260px;flex:none;border-right:1px solid #d0d0d0;padding:12px;position:sticky;top:44px;max-height:calc(100vh - 44px);overflow:auto}
#nav input[type=search]{width:100%;padding:4px 6px;font-size:12px;margin-bottom:6px}
#nav ul{list-style:none;margin:8px 0 0;padding:0}
#nav li{margin-bottom:4px;line-height:1.4}
#nav a{color:#1f3864;text-decoration:none}
#nav a:hover{text-decoration:underline}
#nav .cid{font-family:"MS Gothic",monospace}
main{flex:1;min-width:0;padding:12px 16px}
h2{font-size:15px;border-bottom:2px solid #1f3864;padding-bottom:4px;margin-top:24px}
h3{font-size:13px;margin:18px 0 8px}
table{border-collapse:collapse;margin-bottom:10px;max-width:100%}
th,td{border:1px solid #bfbfbf;padding:4px 6px;vertical-align:top;text-align:left}
table.head th{background:#f2f2f2;width:100px;white-space:nowrap}
table.steps{width:100%}
table.steps th{background:#d9d9d9;text-align:center}
table.steps td.c{text-align:center;white-space:nowrap}
table.data th{background:#d9d9d9}
p.count{margin:0 0 8px;color:#595959}
.v{display:inline-block;padding:0 6px;border:1px solid #bfbfbf;border-radius:3px;font-size:12px}
article.ev{border:1px solid #bfbfbf;margin-bottom:12px}
.ev-head{background:#ddebf7;padding:4px 8px;display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}
.ev-head .eid{font-weight:bold;font-family:"MS Gothic",monospace}
.ev-head .cat{background:#fff;border:1px solid #9dc3e6;padding:0 5px;font-size:12px}
.ev-head .cap{font-weight:bold}
.ev-head .step{margin-left:auto}
.ev-head .step,.ev-head .at{color:#44546a;font-size:12px}
article.ev>p,article.ev>pre,article.ev>img,article.ev>table,article.ev>details{margin:8px}
article.ev>table{margin-bottom:0}
img.ev{max-width:100%;border:1px solid #bfbfbf;cursor:zoom-in}
pre{font-family:"MS Gothic",monospace;font-size:12px;background:#fafafa;border:1px solid #e0e0e0;padding:6px;overflow-x:auto;white-space:pre;margin:8px}
pre.source{background:#fff;border:0;padding:0 8px;color:#44546a}
p.source{color:#44546a}
p.note{color:#7f6000;background:#fffbe6;border-left:3px solid #ffd966;padding:4px 8px}
p.missing{color:#9c0006}
.ln{color:#a0a0a0;user-select:none}
details summary{cursor:pointer;color:#1f3864;margin:8px}
p.file a{display:inline-block;overflow-wrap:anywhere}
@media(max-width:720px){
 header.top{position:static;gap:8px}
 .layout{display:block}
 #nav{width:100%;position:static;max-height:none;border-right:0;border-bottom:1px solid #d0d0d0}
 #nav ul{display:flex;flex-wrap:wrap;gap:6px 14px}
 #nav li{margin:0}
 main{padding:12px}
 table.steps{display:block;overflow-x:auto}
 table.steps th:nth-child(2),table.steps th:nth-child(3),table.steps th:nth-child(4){min-width:160px}
 table.data{display:block;overflow-x:auto;max-width:calc(100% - 16px)}
 table.head{width:100%;overflow-wrap:anywhere}
 img.ev{max-width:calc(100% - 16px)}
 .source,.note{overflow-wrap:anywhere}
}
@media print{
 #nav{display:none}
 header.top{position:static}
 section.case{page-break-after:always}
 img.ev{cursor:auto}
}
`;

const JS = `
const q = document.getElementById('q');
const ng = document.getElementById('ngonly');
const items = Array.prototype.slice.call(document.querySelectorAll('#nav li'));
function apply() {
  const t = q.value.trim().toLowerCase();
  items.forEach(function (li) {
    const hit = li.dataset.text.toLowerCase().indexOf(t) >= 0;
    li.hidden = !(hit && (!ng.checked || li.dataset.verdict === 'NG'));
    const sec = document.getElementById('case-' + li.dataset.id);
    if (sec) sec.hidden = li.hidden;
  });
}
q.addEventListener('input', apply);
ng.addEventListener('change', apply);
// Some embedded browsers do not download data URLs. Create a local Blob on demand.
document.querySelectorAll('a[download]').forEach(function (link) {
  const data = link.getAttribute('href');
  link.addEventListener('click', function () {
    if (!data || !data.startsWith('data:') || link.href.startsWith('blob:')) return;
    const raw = atob(data.slice(data.indexOf(',') + 1));
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    link.href = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
  });
});
document.querySelectorAll('img.ev').forEach(function (img) {
  img.addEventListener('click', function () {
    const w = window.open('', '_blank');
    if (w) w.document.write('<img src="' + img.src + '">');
  });
});
`;

export function buildHtml(
  projectDir: string,
  project: Project,
  cases: TestCase[],
  generatedAt = new Date(),
): string {
  const counts = project.verdicts
    .map((v) => {
      const n = cases.filter((c) => resolveVerdict(c) === v).length;
      return `<span class="v v-${slug(v)}">${esc(v)} ${n}</span>`;
    })
    .join(" ");

  const navItems = cases
    .map((c) => {
      const verdict = resolveVerdict(c);
      const text = `${c.id} ${c.title}`;
      return `<li data-id="${esc(c.id)}" data-verdict="${esc(verdict)}" data-text="${esc(text)}"><a href="#case-${esc(c.id)}"><span class="cid">${esc(c.id)}</span> ${esc(c.title)}</a> ${badge(verdict)}</li>`;
    })
    .join("\n");

  const sections = cases
    .map((c) => renderCase(projectDir, project, c))
    .join("\n");

  // §8.1: 外部参照を一切持たない単一ファイル。
  return `<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(project.name)}</title>
<style>${CSS}${verdictCss()}</style>
</head>
<body>
<header class="top">
<h1>${esc(project.name)}</h1>
<span class="stamp">出力日時 ${esc(formatStamp(generatedAt))}</span>
<span class="stamp">環境 ${esc(project.env)}</span>
<span class="counts">${counts}</span>
</header>
<div class="layout">
<nav id="nav">
<input type="search" id="q" placeholder="検索（ID・件名）">
<label><input type="checkbox" id="ngonly"> NG のみ</label>
<ul>
${navItems}
</ul>
</nav>
<main>
${sections}
</main>
</div>
<script>${JS}</script>
</body>
</html>
`;
}
