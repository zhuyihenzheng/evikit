#!/usr/bin/env bun
// CLI: init / export / serve
import { existsSync, mkdirSync, readFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import { Command } from "commander";
import { ProjectSchema } from "./core/types";
import { projectFile, saveProject } from "./core/store";
import { exportProject } from "./export";
import { spawn } from "node:child_process";
import { startServer } from "./server";
import { atomicWrite, safePath } from "./core/fs";

function initProject(dir: string): string {
  const target = resolve(dir);
  for (const sub of ["cases", "evidence", "exports"]) {
    mkdirSync(`${target}/${sub}`, { recursive: true });
  }
  if (existsSync(projectFile(target))) {
    throw new Error(`project.yaml が既に存在します: ${projectFile(target)}`);
  }
  const project = ProjectSchema.parse({
    name: basename(target),
    tester: "",
    env: "",
  });
  saveProject(target, project);
  const ignorePath = safePath(target, ".gitignore");
  const previous = existsSync(ignorePath)
    ? readFileSync(ignorePath, "utf8")
    : "";
  const missing = ["exports/", ".evikit.lock", ".trash/"].filter(
    (rule) => !previous.split(/\r?\n/).includes(rule),
  );
  if (missing.length)
    atomicWrite(
      ignorePath,
      previous +
        (previous && !previous.endsWith("\n") ? "\n" : "") +
        missing.join("\n") +
        "\n",
    );
  return target;
}

const program = new Command();
program
  .name("evi")
  .description("テストエビデンス成果物ジェネレータ")
  .version("0.2.0");

program
  .command("init")
  .argument("<dir>", "プロジェクトディレクトリ")
  .description("project.yaml / cases / evidence / exports を作成する")
  .action((dir: string) => {
    const target = initProject(dir);
    console.log(`初期化しました: ${target}`);
  });

program
  .command("export")
  .argument("<dir>", "プロジェクトディレクトリ")
  .option(
    "--out <dir>",
    "空の出力先ディレクトリ（既定: <dir>/exports/<日時>-<識別子>）",
  )
  .description("Excel・単一 HTML・納品 ZIP を書き出す")
  .action(async (dir: string, options: { out?: string }) => {
    const result = await exportProject(
      resolve(dir),
      options.out ? resolve(options.out) : undefined,
    );
    console.log(`出力先: ${result.outDir}`);
    console.log(`  ${result.xlsxPath}`);
    console.log(`  ${result.htmlPath}`);
    console.log(`  ${result.zipPath}`);
    for (const f of result.files) console.log(`  ${result.outDir}/${f}`);
  });

program
  .command("serve")
  .argument("<dir>", "プロジェクトディレクトリ")
  .option("--port <port>", "ポート（既定: 自動割り当て）", "0")
  .option("--no-open", "ブラウザを自動で開かない")
  .description("ローカルのエビデンス編集画面を起動する")
  .action(async (dir: string, options: { port: string; open: boolean }) => {
    const port = Number(options.port);
    if (!Number.isInteger(port) || port < 0 || port > 65535)
      throw new Error("ポートは 0〜65535 を指定してください");
    if (!existsSync(resolve(import.meta.dir, "../dist/index.html"))) {
      console.log("編集画面をビルドしています…");
      const result = Bun.spawnSync([process.execPath, "run", "build"], {
        cwd: resolve(import.meta.dir, ".."),
        stdout: "inherit",
        stderr: "inherit",
      });
      if (result.exitCode !== 0) throw new Error("UI のビルドに失敗しました");
    }
    const running = startServer(resolve(dir), port);
    console.log(`evikit — ${resolve(dir)}`);
    console.log(`起動 URL: ${running.url}`);
    console.log(
      "終了: Ctrl+C（すべてのデータはプロジェクトフォルダに保存されます）",
    );
    for (const signal of ["SIGINT", "SIGTERM"] as const)
      process.once(signal, () => {
        running.stop();
        process.exit(0);
      });
    if (options.open) {
      const command =
        process.platform === "darwin"
          ? "open"
          : process.platform === "win32"
            ? "explorer.exe"
            : "xdg-open";
      const child = spawn(command, [running.url], {
        detached: true,
        stdio: "ignore",
      });
      child.on("error", () =>
        console.log("上記の URL をブラウザで開いてください。"),
      );
      child.unref();
    }
  });

program.parseAsync(process.argv).catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
