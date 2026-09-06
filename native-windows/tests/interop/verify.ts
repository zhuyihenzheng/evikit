// Optional independent interoperability check using the untouched browser edition.
// Run from the repository root: bun native-windows/tests/interop/verify.ts <native-roundtrip-project> <output-folder> [original-project]
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import ExcelJS from "exceljs";
import { loadProject, loadCases } from "../../../src/core/store";
import { resolveVerdict } from "../../../src/core/verdict";

const [projectPath, outputPath, originalPath] = process.argv.slice(2);
assert(projectPath && outputPath, "project and output arguments are required");
const project = loadProject(projectPath);
const cases = loadCases(projectPath);
assert(cases.length > 0);
if (originalPath) {
  const originalCases = loadCases(originalPath);
  assert.equal(cases.length, originalCases.length);
  for (const c of cases) assert.deepEqual(c, originalCases.find((x) => x.id === c.id), `${c.id}: native YAML round trip changed browser schema values`);
}
const workbook = new ExcelJS.Workbook();
await workbook.xlsx.readFile(path.join(outputPath, "report.xlsx"));
assert.equal(workbook.worksheets.length, cases.length + 1);
assert.equal(workbook.worksheets[0].name, "サマリ");
assert.equal(workbook.worksheets[0].getCell("A1").value, project.name);
let imageCount = 0, stringCells = 0, leadingZeros = 0;
for (const [index, c] of cases.entries()) {
  const sheet = workbook.worksheets[index + 1];
  assert.equal(sheet.getCell("A1").value, `${c.id}　${c.title}`);
  assert.deepEqual([1, 2, 3, 4, 5, 6].map((n) => sheet.getColumn(n).width), [6, 36, 32, 32, 8, 14]);
  assert.equal(sheet.pageSetup.fitToWidth, 1);
  assert.equal(sheet.pageSetup.orientation, "landscape");
  const images = sheet.getImages();
  assert.equal(images.length, c.evidence.filter((e) => e.kind === "image").length);
  imageCount += images.length;
  for (const picture of images) {
    assert.equal(picture.range.tl.col, 1);
    assert(picture.range.ext && picture.range.ext.width > 0 && picture.range.ext.height > 0);
    assert(picture.range.ext!.width <= project.imageMaxWidth);
  }
  sheet.eachRow((row) => {
    assert(row.height && row.height > 0);
    row.eachCell((cell) => {
      assert.notEqual(cell.type, ExcelJS.ValueType.Formula);
      if (typeof cell.value === "string") { stringCells++; if (cell.value === "00012") leadingZeros++; }
    });
  });
  assert.equal(workbook.worksheets[0].getCell(index + 4, 5).text, resolveVerdict(c));
}
const manifest = JSON.parse(readFileSync(path.join(outputPath, "manifest.json"), "utf8"));
assert(!manifest.files.some((file: { path: string }) => file.path.endsWith(".html")));
console.log(JSON.stringify({ browserSchemaRoundtrip: "pass", exceljsReadback: "pass", cases: cases.length, sheets: workbook.worksheets.length, images: imageCount, stringCells, leadingZeroCells: leadingZeros, noHtml: true }, null, 2));
