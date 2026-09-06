import type { Project, TestCase } from "../core/types";
export interface CaseDocument {
  data: TestCase;
  revision: string;
}
export interface ProjectDocument {
  data: Project;
  revision: string;
  path: string;
}
export interface ExportDocument {
  outDir: string;
  files: string[];
  urls: { xlsx: string; html: string; zip: string };
}
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message);
  }
}
export async function request<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...options,
    credentials: "same-origin",
  });
  const data = await response.json();
  if (!response.ok)
    throw new ApiError(data.error ?? "処理に失敗しました", response.status);
  return data;
}
export function json(
  method: string,
  body: unknown,
  revision?: string,
): RequestInit {
  return {
    method,
    body: JSON.stringify(body),
    headers: {
      "Content-Type": "application/json",
      ...(revision ? { "If-Match": revision } : {}),
    },
  };
}
export function form(
  body: FormData,
  revision: string,
  method = "POST",
): RequestInit {
  return { method, body, headers: { "If-Match": revision } };
}
export async function connect() {
  const hash = new URLSearchParams(location.hash.slice(1));
  const token = hash.get("token");
  if (token) {
    await request("/session", {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });
    history.replaceState(null, "", location.pathname);
  }
}
export const rawUrl = (caseId: string, eid: string, hash = "") =>
  `/api/cases/${caseId}/evidence/${eid}/raw?v=${hash}`;
