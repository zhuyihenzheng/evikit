import { cpSync, existsSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(import.meta.dir, "..");
const target = resolve(root, "examples/sample");
if (!existsSync(target)) {
  cpSync(resolve(root, "examples/reference"), target, { recursive: true, errorOnExist: true, force: false });
  console.log("編集用サンプルを examples/sample に作成しました。");
} else if (!existsSync(resolve(target, "project.yaml"))) {
  throw new Error("examples/sample は既に存在しますが project.yaml がありません。既存ファイルを確認してください（上書きしていません）。");
}
