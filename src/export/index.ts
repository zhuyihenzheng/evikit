import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  renameSync,
  rmSync,
  rmdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { randomUUID } from "node:crypto";
import JSZip from "jszip";
import { imageSize } from "image-size";
import { evidencePath, loadCases, loadProject, sha256 } from "../core/store";
import { safePath } from "../core/fs";
import { buildHtml } from "./html";
import { formatDirStamp, safeFileName } from "./format";
import { writeXlsx } from "./xlsx";

export interface ExportResult {
  outDir: string;
  xlsxPath: string;
  htmlPath: string;
  zipPath: string;
  manifestPath: string;
  files: string[];
}

/** Verify and snapshot before the first await; publish the completed directory only. */
export async function exportProject(
  projectDir: string,
  outDir?: string,
): Promise<ExportResult> {
  const project = loadProject(projectDir);
  const cases = loadCases(projectDir);
  const generatedAt = new Date();
  const target = outDir
    ? resolve(outDir)
    : safePath(
        projectDir,
        "exports",
        `${formatDirStamp(generatedAt)}-${randomUUID().slice(0, 6)}`,
      );
  if (existsSync(target) && readdirSync(target).length)
    throw new Error(
      "出力先にファイルが存在します。空のフォルダを指定してください",
    );
  mkdirSync(dirname(target), { recursive: true });
  const stage = mkdtempSync(join(dirname(target), ".evikit-export-"));
  const snapshot = join(stage, ".source");
  const name = safeFileName(project.name);
  const files: string[] = [];
  try {
    for (const c of cases)
      for (const e of c.evidence) {
        const source = evidencePath(projectDir, c.id, e.file);
        if (!existsSync(source))
          throw new Error(
            `${c.id} / ${e.id}: 証拠ファイルが見つかりません (${e.file})`,
          );
        const data = readFileSync(source);
        if (e.sha256 && sha256(data) !== e.sha256)
          throw new Error(
            `${c.id} / ${e.id}: 証拠ファイルのハッシュが一致しません。変更内容を確認して再登録してください`,
          );
        const dest = evidencePath(snapshot, c.id, e.file);
        mkdirSync(dirname(dest), { recursive: true });
        writeFileSync(dest, data);
        if (e.kind === "file") {
          const relative = `files/${c.id}/${e.file}`;
          mkdirSync(dirname(join(stage, relative)), { recursive: true });
          copyFileSync(dest, join(stage, relative));
          files.push(relative);
        }
      }
    // Excel has no WebP support. Convert only the export snapshot; preserve source bytes.
    for (const c of cases)
      for (const e of c.evidence)
        if (e.kind === "image") {
          const path = evidencePath(snapshot, c.id, e.file);
          if (imageSize(readFileSync(path)).type === "webp") {
            const sharp = (await import("sharp")).default;
            const png = await sharp(readFileSync(path)).png().toBuffer();
            e.file = `${e.id}.export.png`;
            writeFileSync(evidencePath(snapshot, c.id, e.file), png);
          }
        }
    await writeXlsx(
      snapshot,
      project,
      cases,
      join(stage, `${name}.xlsx`),
      generatedAt,
    );
    writeFileSync(
      join(stage, `${name}.html`),
      buildHtml(snapshot, project, cases, generatedAt),
      "utf8",
    );
    rmSync(snapshot, { recursive: true, force: true });
    const outputs = [`${name}.xlsx`, `${name}.html`, ...files];
    const manifest = {
      version: 1,
      project: project.name,
      generatedAt: generatedAt.toISOString(),
      cases: cases.length,
      evidence: cases.reduce((n, c) => n + c.evidence.length, 0),
      files: outputs.map((file) => ({
        file,
        sha256: sha256(readFileSync(join(stage, file))),
      })),
    };
    writeFileSync(
      join(stage, "manifest.json"),
      JSON.stringify(manifest, null, 2),
    );
    const zip = new JSZip();
    for (const file of [...outputs, "manifest.json"])
      zip.file(file, readFileSync(join(stage, file)));
    writeFileSync(
      join(stage, `${name}.zip`),
      await zip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }),
    );
    if (existsSync(target)) {
      if (readdirSync(target).length)
        throw new Error("出力先が変更されたため保存を中止しました");
      rmdirSync(target);
    }
    renameSync(stage, target);
    return {
      outDir: target,
      xlsxPath: join(target, `${name}.xlsx`),
      htmlPath: join(target, `${name}.html`),
      zipPath: join(target, `${name}.zip`),
      manifestPath: join(target, "manifest.json"),
      files,
    };
  } catch (error) {
    rmSync(stage, { recursive: true, force: true });
    throw error;
  }
}
