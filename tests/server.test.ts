import { beforeEach, afterEach, describe, expect, test } from "bun:test";
import { mkdtempSync, rmSync, readFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { capturedNow, createApp, startServer } from "../src/server";
import { Workspace } from "../src/core/workspace";
import { saveProject } from "../src/core/store";
import { ProjectSchema } from "../src/core/types";
const origin = "http://127.0.0.1:4000";
const token = "a".repeat(64);
let dir: string;
let app: ReturnType<typeof createApp>;
beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), "evikit-api-"));
  saveProject(dir, ProjectSchema.parse({ name: "API テスト" }));
  app = createApp(new Workspace(dir), token, { origin: () => origin });
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));
async function call(
  path: string,
  method = "GET",
  body?: unknown,
  revision?: string,
  extra: Record<string, string> = {},
) {
  return app.request(
    new Request(origin + "/api" + path, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        ...(body instanceof FormData
          ? {}
          : { "Content-Type": "application/json" }),
        ...(revision ? { "If-Match": revision } : {}),
        ...extra,
      },
      body:
        body === undefined
          ? undefined
          : body instanceof FormData
            ? body
            : JSON.stringify(body),
    }),
  );
}
const create = async () =>
  (
    await call("/cases", "POST", {
      id: "TC-001",
      title: "API から作成",
      steps: [{ no: 1, verdict: "OK" }],
    })
  ).json();

describe("local API boundaries", () => {
  test("capture timestamps preserve the local offset and the recorded instant", () => {
    const instant = new Date("2026-09-05T04:12:34.000Z");
    const stamp = capturedNow(instant);
    expect(stamp).toMatch(/[+-]\d{2}:\d{2}$/);
    expect(new Date(stamp).getTime()).toBe(instant.getTime());
    expect(stamp.slice(11, 13)).toBe(
      String(instant.getHours()).padStart(2, "0"),
    );
  });
  test("requires a session, rejects cross-origin/Host, gives a scoped HttpOnly cookie", async () => {
    expect((await app.request(origin + "/api/project")).status).toBe(401);
    expect(
      (
        await call("/project", "GET", undefined, undefined, {
          Origin: "https://example.com",
        })
      ).status,
    ).toBe(403);
    expect(
      (
        await call("/project", "GET", undefined, undefined, {
          Host: "evil.test:4000",
        })
      ).status,
    ).toBe(403);
    expect(
      (
        await call("/project", "GET", undefined, undefined, {
          "Sec-Fetch-Site": "cross-site",
        })
      ).status,
    ).toBe(403);
    const result = await call("/session", "POST");
    const cookie = result.headers.get("Set-Cookie")!;
    expect(result.status).toBe(200);
    expect(cookie).toContain("HttpOnly");
    expect(cookie).toContain("SameSite=Strict");
    expect(cookie).toContain("evikit_aaaaaaaaaaaa");
    const authed = await app.request(origin + "/api/project", {
      headers: { Cookie: cookie.split(";")[0]! },
    });
    expect(authed.status).toBe(200);
  });
  test("POST, PUT, stale PUT, GET and soft DELETE/restore", async () => {
    const c = await create();
    expect(c.data.id).toBe("TC-001");
    expect(
      (
        await call(
          "/cases/TC-001",
          "PUT",
          { ...c.data, title: "更新" },
          c.revision,
        )
      ).status,
    ).toBe(200);
    expect(
      (await call("/cases/TC-001", "PUT", c.data, c.revision)).status,
    ).toBe(409);
    const current = await (await call("/cases/TC-001")).json();
    expect(current.data.title).toBe("更新");
    const deleted = await (
      await call("/cases/TC-001", "DELETE", undefined, current.revision)
    ).json();
    expect((await call("/cases/TC-001")).status).toBe(404);
    expect((await call(`/restore/${deleted.archiveId}`, "POST")).status).toBe(
      200,
    );
  });
  test("TSV paste persists CSV with leading zeros and metadata", async () => {
    const c = await create();
    const data = new FormData();
    data.set("text", "id\tstatus\n00012\tOK");
    data.set(
      "metadata",
      JSON.stringify({
        category: "DB",
        caption: "SQL 結果",
        step: 1,
        source: "select id,status from users",
        note: "1 件",
      }),
    );
    const response = await call(
      "/cases/TC-001/evidence",
      "POST",
      data,
      c.revision,
    );
    expect(response.status).toBe(201);
    const result = await response.json();
    expect(result.data.evidence[0].kind).toBe("table");
    expect(readFileSync(join(dir, "evidence/TC-001/E01.csv"), "utf8")).toBe(
      "id,status\n00012,OK",
    );
    const preview = await (
      await call("/cases/TC-001/evidence/E01/preview")
    ).json();
    expect(preview.rows[1][0]).toBe("00012");
    const raw = await call("/cases/TC-001/evidence/E01/raw");
    expect(raw.headers.get("content-type")).toBe("application/octet-stream");
    expect(raw.headers.get("content-disposition")).toContain("attachment");
  });
  test("bad images, empty input and invalid step do not add evidence", async () => {
    const c = await create();
    const data = new FormData();
    data.set("file", new File(["not png"], "image.png", { type: "image/png" }));
    data.set("metadata", JSON.stringify({ category: "画面", step: 1 }));
    expect(
      (await call("/cases/TC-001/evidence", "POST", data, c.revision)).status,
    ).toBe(400);
    data.delete("file");
    data.set("text", "");
    expect(
      (await call("/cases/TC-001/evidence", "POST", data, c.revision)).status,
    ).toBe(400);
    data.set("text", "text");
    data.set("metadata", JSON.stringify({ category: "ログ", step: 99 }));
    expect(
      (await call("/cases/TC-001/evidence", "POST", data, c.revision)).status,
    ).toBe(400);
    expect(
      (await (await call("/cases/TC-001")).json()).data.evidence.length,
    ).toBe(0);
  });
  test("log previews keep internal blank lines without counting trailing newlines", async () => {
    const c = await create();
    const data = new FormData();
    data.set("text", "INFO started\r\n\r\nERROR failed\r\n");
    data.set("metadata", JSON.stringify({ category: "ログ", step: 1 }));
    expect(
      (await call("/cases/TC-001/evidence", "POST", data, c.revision)).status,
    ).toBe(201);
    const preview = await (
      await call("/cases/TC-001/evidence/E01/preview")
    ).json();
    expect(preview).toEqual({ text: "INFO started\n\nERROR failed", total: 3 });
  });
  test("non-UTF-8 files fail visibly instead of corrupting text", async () => {
    const c = await create();
    const data = new FormData();
    data.set(
      "file",
      new File([new Uint8Array([0xff, 0xfe, 0x80])], "result.csv"),
    );
    data.set("metadata", JSON.stringify({ category: "DB" }));
    const result = await call(
      "/cases/TC-001/evidence",
      "POST",
      data,
      c.revision,
    );
    expect(result.status).toBe(400);
    expect((await result.json()).error).toContain("UTF-8");
  });
  test("deterministic formatting works without provider calls", async () => {
    const json = await (
      await call("/format", "POST", { lang: "json", text: '{"id":"00012"}' })
    ).json();
    expect(json.text).toContain('"00012"');
    expect(json.text).toContain("\n");
    const sql = await (
      await call("/format", "POST", {
        lang: "sql",
        text: "select id from customers where id = '00012'",
      })
    ).json();
    expect(sql.text).toContain("SELECT");
    const xml = await (
      await call("/format", "POST", {
        lang: "xml",
        text: "<root><id>00012</id></root>",
      })
    ).json();
    expect(xml.text).toContain("\n");
  });
  test("exports only session-generated artifacts and sandboxed standalone HTML", async () => {
    await create();
    const result = await (await call("/export", "POST")).json();
    expect(existsSync(result.zipPath)).toBe(true);
    const html = await call(result.urls.html.replace("/api", ""));
    expect(html.status).toBe(200);
    expect(html.headers.get("content-security-policy")).toContain("sandbox");
    expect((await call("/exports/unknown/project.yaml")).status).toBe(404);
    const zip = await call(result.urls.zip.replace("/api", ""));
    expect(zip.headers.get("content-type")).toBe("application/zip");
  });
  test("binds only loopback, refuses second writer, releases project lock on stop", async () => {
    const running = startServer(dir);
    try {
      expect(running.server.hostname).toBe("127.0.0.1");
      expect(() => startServer(dir)).toThrow("別の evikit");
    } finally {
      running.stop();
    }
    expect(existsSync(join(dir, ".evikit.lock"))).toBe(false);
  });
});
