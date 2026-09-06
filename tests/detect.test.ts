// §6 kind 判定表
import { describe, expect, test } from "bun:test";
import {
  detect,
  detectFile,
  detectText,
  TEXT_MAX_BYTES,
} from "../src/core/detect";

describe("detect", () => {
  test("TSV 貼り付け -> table", () => {
    const tsv =
      "user_id\tuser_name\tlast_login_at\nU001\t山田 太郎\t2026-09-04 10:12:35";
    expect(detectText(tsv)).toEqual({ kind: "table", lang: "" });
  });

  test("タブ数が揃わない複数行 -> text", () => {
    expect(detectText("a\tb\nc\td\te").kind).toBe("text");
  });

  test("カンマを含むログ -> text/plain（表として扱わない）", () => {
    const log =
      "10:12:03 INFO  select a, b, c from users\n10:12:04 INFO  done, elapsed=12ms";
    expect(detectText(log)).toEqual({ kind: "text", lang: "plain" });
  });

  test("JSON 貼り付け -> text/json", () => {
    expect(detectText('{"status":"ok","count":3}')).toEqual({
      kind: "text",
      lang: "json",
    });
    expect(detectText("[1, 2, 3]")).toEqual({ kind: "text", lang: "json" });
  });

  test("JSON として壊れていれば plain", () => {
    expect(detectText("{status: ok").lang).toBe("plain");
  });

  test("XML 貼り付け -> text/xml", () => {
    expect(detectText("<result><ok/></result>")).toEqual({
      kind: "text",
      lang: "xml",
    });
  });

  test(".csv / .tsv -> table", () => {
    expect(detectFile("customers.csv", 177)).toEqual({
      kind: "table",
      lang: "",
    });
    expect(detectFile("customers.tsv", 177).kind).toBe("table");
  });

  test("画像拡張子 -> image", () => {
    for (const f of ["a.png", "a.JPG", "a.jpeg", "a.gif", "a.webp"]) {
      expect(detectFile(f, 1000).kind).toBe("image");
    }
  });

  test("テキスト系拡張子 -> text、lang は拡張子から", () => {
    expect(detectFile("app.log", 912)).toEqual({ kind: "text", lang: "log" });
    expect(detectFile("res.json", 316)).toEqual({ kind: "text", lang: "json" });
    expect(detectFile("a.xml", 10).lang).toBe("xml");
    expect(detectFile("q.sql", 10).lang).toBe("sql");
    expect(detectFile("export.yaml", 220)).toEqual({
      kind: "text",
      lang: "plain",
    });
  });

  test(".bin -> file", () => {
    expect(detectFile("dump.bin", 1000)).toEqual({ kind: "file", lang: "" });
    expect(detectFile("customers.zip", 328).kind).toBe("file");
  });

  test("テキスト系でも 2 MB 超は file", () => {
    expect(detectFile("huge.log", TEXT_MAX_BYTES + 1).kind).toBe("file");
    expect(detectFile("huge.log", TEXT_MAX_BYTES).kind).toBe("text");
  });

  test("クリップボード画像 -> image", () => {
    expect(detect({ type: "clipboardImage" })).toEqual({
      kind: "image",
      lang: "",
    });
  });
});
