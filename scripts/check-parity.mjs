#!/usr/bin/env node
// Route parity between the Next.js app (web/src/app) and the Blazor Web App (src/Gabinety.Web), ADR-0010 / docs/operations/PARITY.md.
//
//   node scripts/check-parity.mjs              report only: lists what is missing, exits 0
//   CUTOVER_GATE=1 node scripts/check-parity.mjs   the cutover gate: exits 1 when a Next route has no Blazor route and no documented exemption
//   node scripts/check-parity.mjs --json       the report as JSON (for tooling)
//
// Next routes: every `page.tsx` and `route.ts` under web/src/app, route groups `(x)` dropped, plus `sitemap.ts` -> /sitemap.xml and `robots.ts` -> /robots.txt.
// Blazor routes: every `@page "..."` of src/Gabinety.Web, every `MapGet/MapPost/MapMethods("/...")` literal, and every `const string ...Path = "/..."` (routes that are
// declared once and mapped by name). Dynamic segments compare by position, not by name: `/gabinet/[slug]` equals `/gabinet/{Slug}`; a catch-all `[...x]` equals `{*x}`.
// Exemptions live in scripts/parity-exemptions.json: a route, a reason, and the decision that makes it unnecessary. An exemption without a reason fails the script.
// No dependency: node built-ins only (the e2e job runs it before any install it does not already have).
import { readdirSync, readFileSync, statSync, existsSync } from "node:fs";
import { join, relative, sep, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const nextApp = join(root, "web/src/app");
const blazorSrc = join(root, "src/Gabinety.Web");
const gate = process.env.CUTOVER_GATE === "1";
const asJson = process.argv.includes("--json");

function* walk(dir) {
  if (!existsSync(dir)) return;
  for (const name of readdirSync(dir)) {
    if (name === "node_modules" || name === "bin" || name === "obj" || name === ".next") continue;
    const p = join(dir, name);
    if (statSync(p).isDirectory()) yield* walk(p);
    else yield p;
  }
}

/** "/gabinet/{}" for both hosts: params by position. */
export const canonical = (route) =>
  ("/" + route.split("/").filter(Boolean).map((s) => (/^\{.*\}$/.test(s) || /^\[.*\]$/.test(s) ? "{}" : s)).join("/")).toLowerCase();

export function nextRoutes() {
  const routes = new Set();
  for (const file of walk(nextApp)) {
    const rel = relative(nextApp, file).split(sep);
    const leaf = rel.pop();
    const dirs = rel.filter((s) => !/^\(.*\)$/.test(s));
    if (leaf === "page.tsx" || leaf === "route.ts") routes.add("/" + dirs.join("/"));
    else if (leaf === "sitemap.ts" && rel.length === 0) routes.add("/sitemap.xml");
    else if (leaf === "robots.ts" && rel.length === 0) routes.add("/robots.txt");
  }
  return [...routes].map((r) => (r === "/" ? "/" : r.replace(/\/$/, ""))).sort();
}

export function blazorRoutes() {
  const routes = new Set();
  for (const file of walk(blazorSrc)) {
    const text = file.endsWith(".razor") || file.endsWith(".cs") ? readFileSync(file, "utf8") : "";
    if (file.endsWith(".razor")) for (const m of text.matchAll(/^@page\s+"([^"]+)"/gm)) routes.add(m[1].split("?")[0]);
    if (file.endsWith(".cs")) {
      for (const m of text.matchAll(/\.Map(?:Get|Post|Methods)\(\s*(?:\[[^\]]*\]\s*,\s*)?"(\/[^"]*)"/g)) routes.add(m[1]);
      for (const line of text.split("\n").filter((l) => /const\s+string\s/.test(l))) for (const m of line.matchAll(/\w*Path\s*=\s*"(\/[^"]*)"/g)) routes.add(m[1]);
    }
  }
  return [...routes].sort();
}

function exemptions() {
  const file = join(root, "scripts/parity-exemptions.json");
  const list = JSON.parse(readFileSync(file, "utf8")).exemptions;
  const bad = list.filter((e) => !e.route || !e.reason || e.reason.trim().length < 20);
  if (bad.length) throw new Error(`parity-exemptions.json: every exemption needs a route and a reason of at least 20 characters (${bad.map((b) => b.route ?? "?").join(", ")})`);
  return list;
}

/** `/api/*` style patterns: a trailing `/*` matches the prefix and everything below. */
const exempt = (route, list) =>
  list.find((e) => (e.route.endsWith("/*") ? route === e.route.slice(0, -2) || route.startsWith(e.route.slice(0, -1)) : e.route === route));

export function compare() {
  const next = nextRoutes();
  const blazor = blazorRoutes();
  const have = new Set(blazor.map(canonical));
  const list = exemptions();
  const rows = next.map((route) => {
    const ex = exempt(route, list);
    const ported = have.has(canonical(route));
    return { route, status: ported ? "ported" : ex ? "exempt" : "missing", reason: ex?.reason };
  });
  return { rows, blazor, missing: rows.filter((r) => r.status === "missing") };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const { rows, blazor, missing } = compare();
  if (asJson) console.log(JSON.stringify({ gate, rows, blazor }, null, 2));
  else {
    for (const r of rows) console.log(`${r.status.padEnd(8)} ${r.route}`);
    for (const e of [...new Set(rows.filter((r) => r.reason).map((r) => r.reason))]) console.log(`\nexemption: ${e}`);
    const n = (s) => rows.filter((r) => r.status === s).length;
    console.log(`\nNext routes: ${rows.length}; ported ${n("ported")}, exempt ${n("exempt")}, missing ${missing.length}; Blazor routes found: ${blazor.length}`);
    if (missing.length) console.log(`${gate ? "FAIL" : "REPORT ONLY"} (CUTOVER_GATE=${gate ? 1 : 0}): ${missing.length} Next route(s) without a Blazor route. Port them or document an exemption in scripts/parity-exemptions.json.`);
  }
  process.exit(gate && missing.length ? 1 : 0);
}
