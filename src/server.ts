import { Hono } from "hono";
import { getCookie, setCookie } from "hono/cookie";
import { randomBytes, timingSafeEqual } from "node:crypto";
import {
  existsSync,
  readFileSync,
  realpathSync,
  statSync,
  openSync,
  writeFileSync,
  closeSync,
  unlinkSync,
} from "node:fs";
import { basename, extname, join } from "node:path";
import Papa from "papaparse";
import { z } from "zod";
import {
  Workspace,
  WorkspaceError,
  EvidenceMetaSchema,
  validateImage,
} from "./core/workspace";
import { detectFile, extensionOf, TEXT_MAX_BYTES } from "./core/detect";
import { prepareText, MAX_UPLOAD_BYTES } from "./core/import";
import { evidencePath } from "./core/store";
import { safePath } from "./core/fs";
import { exportProject, type ExportResult } from "./export";
import { toLines } from "./export/format";
import type { NewEvidence } from "./core/store";

export function capturedNow(date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  const offset = -date.getTimezoneOffset();
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${offset >= 0 ? "+" : "-"}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}`;
}

const equal = (a: string, b: string) => {
  const aa = Buffer.from(a),
    bb = Buffer.from(b);
  return aa.length === bb.length && timingSafeEqual(aa, bb);
};
const MIME: Record<string, string> = {
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".gif": "image/gif",
  ".webp": "image/webp",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript",
  ".css": "text/css",
  ".svg": "image/svg+xml",
  ".xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  ".zip": "application/zip",
  ".json": "application/json",
};
const attachment = (name: string) =>
  `attachment; filename*=UTF-8''${encodeURIComponent(name).replace(/'/g, "%27")}`;

export function createApp(
  workspace: Workspace,
  token: string,
  options: { origin: () => string; uiDir?: string },
) {
  const app = new Hono();
  const cookieName = `evikit_${token.slice(0, 12)}`;
  const exports = new Map<string, ExportResult>();
  app.use("*", async (c, next) => {
    if (
      new URL(c.req.url).origin !== options.origin() ||
      (c.req.header("Host") &&
        c.req.header("Host") !== new URL(options.origin()).host)
    )
      return c.json({ error: "接続先が不正です" }, 403);
    const origin = c.req.header("Origin");
    if (
      (origin && origin !== options.origin()) ||
      c.req.header("Sec-Fetch-Site") === "cross-site"
    )
      return c.json(
        { error: "別のサイトからのアクセスは許可されていません" },
        403,
      );
    c.header("X-Content-Type-Options", "nosniff");
    c.header("Referrer-Policy", "no-referrer");
    c.header("Cache-Control", "no-store");
    c.header(
      "Content-Security-Policy",
      "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'",
    );
    if (Number(c.req.header("Content-Length") ?? 0) > MAX_UPLOAD_BYTES + 65536)
      return c.json({ error: "アップロードは 25 MB 以内にしてください" }, 413);
    await next();
  });
  app.post("/api/session", (c) => {
    if (
      !equal(
        c.req.header("Authorization")?.replace(/^Bearer /, "") ?? "",
        token,
      )
    )
      return c.json({ error: "起動時の URL から開いてください" }, 401);
    setCookie(c, cookieName, token, {
      httpOnly: true,
      sameSite: "Strict",
      path: "/",
    });
    return c.json({ ok: true });
  });
  app.use("/api/*", async (c, next) => {
    const value =
      getCookie(c, cookieName) ??
      c.req.header("Authorization")?.replace(/^Bearer /, "") ??
      "";
    if (!equal(value, token))
      return c.json(
        {
          error:
            "接続が切れました。ターミナルに表示された起動 URL から開き直してください。",
        },
        401,
      );
    await next();
  });
  const revision = (c: {
    req: { header: (name: string) => string | undefined };
  }) => c.req.header("If-Match");
  app.get("/api/project", (c) => c.json(workspace.project()));
  app.put("/api/project", async (c) => {
    const body = await c.req.json();
    return c.json(
      await workspace.serial(() => workspace.updateProject(body, revision(c))),
    );
  });
  app.get("/api/cases", (c) => c.json(workspace.cases()));
  app.post("/api/cases", async (c) => {
    const body = await c.req.json();
    return c.json(
      await workspace.serial(() => workspace.createCase(body)),
      201,
    );
  });
  app.get("/api/cases/:id", (c) => c.json(workspace.case(c.req.param("id"))));
  app.put("/api/cases/:id", async (c) => {
    const body = await c.req.json();
    return c.json(
      await workspace.serial(() =>
        workspace.updateCase(c.req.param("id"), body, revision(c)),
      ),
    );
  });
  app.delete("/api/cases/:id", async (c) =>
    c.json(
      await workspace.serial(() =>
        workspace.archive(c.req.param("id"), null, revision(c)),
      ),
    ),
  );
  app.post("/api/cases/:id/evidence", async (c) => {
    const data = await c.req.formData();
    const metadata = EvidenceMetaSchema.parse(
      JSON.parse(String(data.get("metadata") ?? "{}")),
    );
    const file = data.get("file");
    let input: NewEvidence;
    if (file instanceof File) {
      if (!file.size || file.size > MAX_UPLOAD_BYTES)
        throw new WorkspaceError(
          "空のファイル、または 25 MB を超えるファイルは追加できません",
          413,
        );
      const buffer = Buffer.from(await file.arrayBuffer());
      const detection = detectFile(file.name, file.size);
      if (detection.kind === "image") {
        const size = validateImage(buffer);
        input = {
          ...detection,
          ...metadata,
          data: buffer,
          extension: size.type === "jpeg" ? "jpg" : size.type,
        };
      } else if (detection.kind === "text" || detection.kind === "table") {
        let value;
        try {
          value = new TextDecoder("utf-8", { fatal: true }).decode(buffer);
        } catch {
          throw new WorkspaceError(
            "UTF-8 として読み取れません。文字コードを UTF-8 に変換してから追加してください",
          );
        }
        const prepared = prepareText(value, file.name);
        input = { ...prepared, ...metadata, data: prepared.text };
      } else
        input = {
          ...detection,
          ...metadata,
          data: buffer,
          originalName: file.name,
          extension: extensionOf(file.name) || "bin",
        };
    } else {
      const prepared = prepareText(String(data.get("text") ?? ""));
      input = { ...prepared, ...metadata, data: prepared.text };
    }
    input.capturedAt = capturedNow();
    return c.json(
      await workspace.serial(() =>
        workspace.addEvidence(c.req.param("id"), input, revision(c)),
      ),
      201,
    );
  });
  app.put("/api/cases/:id/evidence/:eid", async (c) => {
    const body = await c.req.json();
    return c.json(
      await workspace.serial(() =>
        workspace.updateEvidence(
          c.req.param("id"),
          c.req.param("eid"),
          body,
          revision(c),
        ),
      ),
    );
  });
  app.delete("/api/cases/:id/evidence/:eid", async (c) =>
    c.json(
      await workspace.serial(() =>
        workspace.archive(c.req.param("id"), c.req.param("eid"), revision(c)),
      ),
    ),
  );
  app.put("/api/cases/:id/evidence/:eid/image", async (c) => {
    const data = await c.req.formData();
    const png = data.get("file");
    const reset = data.get("reset") === "true";
    if (!reset && !(png instanceof File))
      throw new WorkspaceError("PNG が必要です");
    const buffer =
      png instanceof File ? Buffer.from(await png.arrayBuffer()) : null;
    return c.json(
      await workspace.serial(() =>
        workspace.image(
          c.req.param("id"),
          c.req.param("eid"),
          reset ? null : buffer,
          JSON.parse(String(data.get("annotations") ?? "{}")),
          revision(c),
        ),
      ),
    );
  });
  app.get("/api/cases/:id/evidence/:eid/raw", (c) => {
    const id = c.req.param("id");
    const e = workspace.findEvidence(
      workspace.case(id).data,
      c.req.param("eid"),
    );
    const file =
      c.req.query("original") === "1" ? (e.originalFile ?? e.file) : e.file;
    const path = evidencePath(workspace.dir, id, file);
    if (!existsSync(path))
      throw new WorkspaceError("証拠ファイルが見つかりません", 404);
    const imageMime =
      e.kind === "image" ? MIME[extname(file).toLowerCase()] : undefined;
    c.header("Content-Type", imageMime ?? "application/octet-stream");
    if (!imageMime || c.req.query("download") === "1")
      c.header("Content-Disposition", attachment(e.originalName || file));
    c.header("Content-Security-Policy", "sandbox; default-src 'none'");
    return c.body(readFileSync(path));
  });
  app.get("/api/cases/:id/evidence/:eid/preview", (c) => {
    const id = c.req.param("id");
    const e = workspace.findEvidence(
      workspace.case(id).data,
      c.req.param("eid"),
    );
    if (e.kind !== "table" && e.kind !== "text")
      throw new WorkspaceError("プレビュー対象ではありません");
    const path = evidencePath(workspace.dir, id, e.file);
    if (statSync(path).size > TEXT_MAX_BYTES)
      throw new WorkspaceError(
        "プレビュー上限は 2 MB です。原本をダウンロードしてください",
      );
    const text = readFileSync(path, "utf8");
    if (e.kind === "table") {
      const rows = Papa.parse<string[]>(text, {
        skipEmptyLines: true,
        dynamicTyping: false,
      }).data;
      return c.json({
        rows: rows.slice(0, 101),
        total: Math.max(0, rows.length - 1),
      });
    }
    const lines = toLines(text);
    return c.json({
      text: lines.slice(0, 200).join("\n"),
      total: lines.length,
    });
  });
  app.post("/api/restore/:archiveId", async (c) =>
    c.json(
      await workspace.serial(() => workspace.restore(c.req.param("archiveId"))),
    ),
  );
  app.post("/api/format", async (c) => {
    const input = z
      .object({
        text: z.string().max(TEXT_MAX_BYTES),
        lang: z.enum(["json", "xml", "sql"]),
      })
      .parse(await c.req.json());
    let text: string;
    if (input.lang === "json")
      text = JSON.stringify(JSON.parse(input.text), null, 2);
    else if (input.lang === "sql")
      text = (await import("sql-formatter")).format(input.text, {
        language: "sql",
        tabWidth: 2,
        keywordCase: "upper",
      });
    else
      text = (await import("xml-formatter")).default(input.text, {
        indentation: "  ",
        lineSeparator: "\n",
        collapseContent: true,
      });
    return c.json({ text });
  });
  app.post("/api/export", async (c) => {
    const result = await workspace.serial(() => exportProject(workspace.dir));
    const id = basename(result.outDir);
    exports.set(id, result);
    const urls = Object.fromEntries(
      (["xlsx", "html", "zip"] as const).map((type) => [
        type,
        `/api/exports/${id}/${encodeURIComponent(basename(result[`${type}Path`]))}`,
      ]),
    );
    return c.json({ ...result, urls });
  });
  app.get("/api/exports/:id/:name", (c) => {
    const result = exports.get(c.req.param("id"));
    if (!result)
      throw new WorkspaceError("このセッションの出力が見つかりません", 404);
    const name = c.req.param("name");
    if (
      ![
        result.xlsxPath,
        result.htmlPath,
        result.zipPath,
        result.manifestPath,
      ].some((p) => basename(p) === name)
    )
      throw new WorkspaceError("ファイルが見つかりません", 404);
    const path = safePath(result.outDir, name);
    const mime = MIME[extname(path)] ?? "application/octet-stream";
    c.header("Content-Type", mime);
    if (extname(path) !== ".html" || c.req.query("download") === "1")
      c.header("Content-Disposition", attachment(name));
    c.header(
      "Content-Security-Policy",
      "sandbox allow-scripts allow-downloads allow-popups; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'",
    );
    return c.body(readFileSync(path));
  });
  app.get("*", (c) => {
    if (c.req.path.startsWith("/api/"))
      throw new WorkspaceError("API が見つかりません", 404);
    const uiDir = options.uiDir ?? join(import.meta.dir, "..", "dist");
    const file =
      c.req.path === "/"
        ? "index.html"
        : decodeURIComponent(c.req.path.slice(1));
    const path = safePath(uiDir, file);
    if (!existsSync(path) || !statSync(path).isFile())
      return c.text(
        "UI が見つかりません。bun run build を実行してください。",
        404,
      );
    c.header("Content-Type", MIME[extname(path)] ?? "application/octet-stream");
    return c.body(readFileSync(path));
  });
  app.onError((error, c) => {
    const message =
      error instanceof z.ZodError
        ? error.issues
            .map((i) => `${i.path.join(".")}: ${i.message}`)
            .join("\n")
        : error.message;
    const status =
      error instanceof WorkspaceError
        ? error.status
        : (error as NodeJS.ErrnoException).code === "ENOENT"
          ? 404
          : 400;
    return c.json({ error: message }, status as 400);
  });
  return app;
}

export function startServer(dir: string, port = 0) {
  const root = realpathSync(dir);
  const workspace = new Workspace(root);
  const lock = safePath(root, ".evikit.lock");
  if (existsSync(lock)) {
    const pid = Number(readFileSync(lock, "utf8"));
    let stale = false;
    if (Number.isSafeInteger(pid) && pid > 0)
      try {
        process.kill(pid, 0);
      } catch (error) {
        stale = (error as NodeJS.ErrnoException).code === "ESRCH";
      }
    if (stale) unlinkSync(lock);
    else
      throw new Error(
        "このプロジェクトは別の evikit で開いています。先にそちらを終了してください。",
      );
  }
  const fd = openSync(lock, "wx", 0o600);
  writeFileSync(fd, String(process.pid));
  closeSync(fd);
  const token = randomBytes(32).toString("hex");
  let server: ReturnType<typeof Bun.serve>;
  try {
    const app = createApp(workspace, token, {
      origin: () => server.url.origin,
    });
    server = Bun.serve({
      hostname: "127.0.0.1",
      port,
      maxRequestBodySize: MAX_UPLOAD_BYTES + 65536,
      idleTimeout: 120,
      fetch: app.fetch,
    });
  } catch (error) {
    unlinkSync(lock);
    throw error;
  }
  const cleanup = () => {
    if (existsSync(lock) && readFileSync(lock, "utf8") === String(process.pid))
      unlinkSync(lock);
  };
  process.once("exit", cleanup);
  const stop = () => {
    server.stop(true);
    cleanup();
    process.removeListener("exit", cleanup);
  };
  // A fresh query forces navigation even when reusing an already-open tab after restart.
  const launch = randomBytes(8).toString("hex");
  return {
    server,
    url: `${server.url.origin}/?launch=${launch}#token=${token}`,
    stop,
  };
}
