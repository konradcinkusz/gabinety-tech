import { execFile } from "node:child_process";
import { mkdtemp, readFile, readdir, writeFile, rm } from "node:fs/promises";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { promisify } from "node:util";
import { afterAll, beforeAll, describe, expect, it } from "vitest";

const run = promisify(execFile);
const web = path.resolve(__dirname, "../../..");
const script = (n: string) => path.join(web, "scripts", n);
const spec = {
  openapi: "3.0.1",
  info: { title: "Demo", version: "1" },
  paths: { "/api/v1/things": { get: { responses: { "200": { description: "ok", content: { "application/json": { schema: { type: "array", items: { type: "string" } } } } } } } } },
};

let server: Server;
let url: string;
let dir: string;
let sources: string;
beforeAll(async () => {
  server = createServer((req, res) => {
    if (req.url === "/openapi/v1.json") res.setHeader("content-type", "application/json").end(JSON.stringify(spec));
    else res.writeHead(404).end();
  });
  await new Promise<void>((r) => server.listen(0, "127.0.0.1", r));
  url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/openapi/v1.json`;
  dir = await mkdtemp(path.join(tmpdir(), "gt-types-"));
  sources = path.join(dir, "sources.json");
  await writeFile(sources, JSON.stringify({ demo: url, missing: "http://127.0.0.1:1/openapi/v1.json" }));
});
afterAll(async () => {
  server.close();
  await rm(dir, { recursive: true, force: true });
});

const gen = (...a: string[]) => run("node", [script("gen-types.mjs"), "--sources", sources, "--out", path.join(dir, "out"), ...a]);
const check = (...a: string[]) => run("node", [script("check-types.mjs"), "--sources", sources, "--out", path.join(dir, "out"), ...a]);

describe("gen:types and check:types", () => {
  it("gen fails when a service does not answer, unless --allow-missing", async () => {
    await expect(gen()).rejects.toMatchObject({ code: 1 });
    const ok = await gen("--allow-missing");
    expect(ok.stdout).toContain("skipped");
    expect(await readdir(path.join(dir, "out"))).toEqual(["demo.ts"]);
    expect(await readFile(path.join(dir, "out", "demo.ts"), "utf8")).toContain("/api/v1/things");
  });
  it("check passes when the committed file is identical", async () => {
    const r = await check("--allow-missing");
    expect(r.stdout).toContain("up to date");
  });
  it("check fails and names the file after a deliberate edit", async () => {
    const f = path.join(dir, "out", "demo.ts");
    const original = await readFile(f, "utf8");
    await writeFile(f, original + "\n// hand edit\n");
    await expect(check("--allow-missing")).rejects.toMatchObject({ code: 1, stdout: expect.stringContaining("demo.ts") });
    await writeFile(f, original);
    expect((await check("--allow-missing")).stdout).toContain("up to date");
  });
  it("check fails when a committed file is missing", async () => {
    await rm(path.join(dir, "out", "demo.ts"));
    await expect(check("--allow-missing")).rejects.toMatchObject({ code: 1 });
  });
});
