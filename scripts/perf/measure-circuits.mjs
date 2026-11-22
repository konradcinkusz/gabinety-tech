#!/usr/bin/env node
// Memory per interactive circuit of the Blazor host (ADR-0010 "Cost": "memory per open interactive page is the capacity limit and has not been measured"; T-10.04 extension).
//
//   node scripts/perf/measure-circuits.mjs --url http://localhost:3000 --pid <pid of the Blazor host> [--levels 10,25,50] [--page /moje/rezerwacje] [--out DIR]
//
// What it does: signs in N times through the real sign-in form (each sign-in is its own server-side session, as N visitors would have), opens `--page` (an INTERACTIVE page: it starts a
// circuit) in N browser pages, waits until each circuit answered `StartCircuit`, lets the host's memory settle, and reads the host process's memory from /proc/<pid> (VmRSS = the working
// set the OS charges the machine, RssAnon, and Pss from smaps_rollup). Levels are cumulative (10, then 25 = 15 more...). Per circuit MB = (RSS at level N - RSS at 0) / N, and the marginal
// slope between levels (RSS growth per added circuit) which is what capacity planning wants. Nothing here is a Fly measurement: the process is read where it runs (see the report).
//
// Prints a Markdown block and writes <out>/blazor-circuits.json and .md. Exit 0 when it measured, 1 when it could not (no host, no pid, a circuit that never started).
// Needs Playwright: it is loaded from tests/e2e/node_modules (pnpm --dir tests/e2e install; Chromium via PW_CHROMIUM_PATH or Playwright's own).
import { readFileSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "../..");
const arg = (name, fallback) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : fallback; };
const url = arg("url", "http://localhost:3000").replace(/\/$/, "");
const pid = Number(arg("pid", process.env.BLAZOR_PID ?? "0"));
const levels = arg("levels", process.env.CIRCUIT_LEVELS ?? "10,25,50").split(/[ ,]+/).map(Number).filter((n) => n > 0).sort((a, b) => a - b);
const page = arg("page", "/moje/rezerwacje");
const out = arg("out", join(process.cwd(), "perf-results"));
const budgetMb = Number(arg("budget-mb", "512"));
const headroom = Number(arg("headroom", "0.25")); // the share of the machine kept free for the OS, the runtime's own growth and a deploy's overlap
const user = arg("user", "demo-client@demo.example");
const password = process.env.E2E_DEMO_PASSWORD || "Demo-Dev-Passw0rd!"; // the fake profile's fixed development password, not a secret (tests/e2e/harness/env.ts)

if (!pid || !existsSync(`/proc/${pid}/status`)) { console.error(`FAIL: --pid ${pid || "(missing)"} is not a running process (need Linux /proc)`); process.exit(1); }
const { chromium } = createRequire(join(root, "tests/e2e/package.json"))("@playwright/test");

const kb = (file, key) => Number(new RegExp(`^${key}:\\s+(\\d+) kB`, "m").exec(readFileSync(file, "utf8"))?.[1] ?? NaN);
const read = () => ({
  rssMb: kb(`/proc/${pid}/status`, "VmRSS") / 1024,
  anonMb: kb(`/proc/${pid}/status`, "RssAnon") / 1024,
  pssMb: kb(`/proc/${pid}/smaps_rollup`, "Pss") / 1024,
  threads: Number(/^Threads:\s+(\d+)/m.exec(readFileSync(`/proc/${pid}/status`, "utf8"))?.[1]),
});
/** Waits until RSS is stable (three reads, 1 s apart, within 1 %) or 30 s pass: the GC and the thread pool settle after a burst. A settling wait, not a business-time sleep. */
async function settled() {
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const end = Date.now() + 30_000; let a = [];
  while (Date.now() < end) {
    a.push(read().rssMb); a = a.slice(-3);
    if (a.length === 3 && Math.max(...a) - Math.min(...a) <= Math.max(...a) * 0.01) break;
    await sleep(1000);
  }
  return read();
}

const browser = await chromium.launch(process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH, args: ["--no-sandbox"] } : {});
const open = []; // contexts kept open: each holds one circuit
async function openCircuit() {
  const ctx = await browser.newContext({ baseURL: url, locale: "pl-PL" });
  const p = await ctx.newPage();
  await p.goto("/logowanie");
  await p.getByLabel("Adres e-mail").fill(user);
  await p.getByLabel("Hasło").fill(password);
  await p.getByRole("button", { name: "Zaloguj się" }).click();
  await p.waitForURL((u) => !u.pathname.startsWith("/logowanie"), { timeout: 30_000 });
  let started = false, live; const ready = new Promise((r) => (live = r));
  p.on("websocket", (ws) => { let sent = false; ws.on("framesent", (f) => { if (String(typeof f.payload === "string" ? f.payload : f.payload.toString("latin1")).includes("StartCircuit")) sent = true; }); ws.on("framereceived", () => { if (sent) { started = true; live(); } }); });
  await p.goto(page);
  await Promise.race([ready, new Promise((_, rej) => setTimeout(() => rej(new Error(`the circuit of ${page} did not start in 30 s (is it an interactive page?)`)), 30_000))]);
  open.push(ctx); return started;
}

const rows = [];
try {
  // warm up: one circuit opened and closed, so JIT and the first-request allocations are not charged to the first level
  await openCircuit(); await open.pop().close();
  const base = await settled();
  rows.push({ circuits: 0, ...base });
  for (const n of levels) {
    while (open.length < n) await openCircuit();
    rows.push({ circuits: n, ...(await settled()) });
  }
} catch (e) { console.error(`FAIL: ${e.message}`); await browser.close(); process.exit(1); }
await browser.close();

const base = rows[0];
const table = rows.map((r, i) => ({
  ...r,
  perCircuitMb: r.circuits ? (r.rssMb - base.rssMb) / r.circuits : null,
  marginalMb: i > 1 ? (r.rssMb - rows[i - 1].rssMb) / (r.circuits - rows[i - 1].circuits) : r.circuits ? (r.rssMb - base.rssMb) / r.circuits : null,
}));
const last = table[table.length - 1];
const first = table.find((r) => r.circuits > 0);
const usable = budgetMb * (1 - headroom);
const fit = (slope) => Math.max(0, Math.floor((usable - base.rssMb) / Math.max(slope, 0.01)));
// Two bounds, both stated, because RSS is not linear (the GC grows the heap in steps and keeps headroom): the PESSIMISTIC slope is the per-circuit cost at the lowest level (heap growth
// included), the OPTIMISTIC one the marginal cost of the last circuits (what is left once the heap has grown). The capacity worth planning with is the pessimistic one, and nothing beyond the
// highest level measured is proven: the extrapolation is arithmetic, not a measurement.
const slopeHi = first.perCircuitMb, slopeLo = last.marginalMb ?? last.perCircuitMb;
const result = {
  date: new Date().toISOString(), url, page, pid, budgetMb, headroom, levels, baseRssMb: base.rssMb,
  rows: table, pessimisticSlopeMbPerCircuit: slopeHi, optimisticSlopeMbPerCircuit: slopeLo,
  planningCircuits: fit(slopeHi), optimisticCircuits: fit(slopeLo), measuredUpTo: last.circuits, rssAtHighestLevelMb: last.rssMb,
  kernel: `${process.platform} ${process.arch}`, node: process.version,
};
mkdirSync(out, { recursive: true });
writeFileSync(join(out, "blazor-circuits.json"), JSON.stringify(result, null, 2));
const f = (x) => (x == null ? "-" : x.toFixed(1));
const md = [
  `| Open circuits | RSS (MB) | RssAnon (MB) | Pss (MB) | Threads | MB per circuit (vs 0) | Marginal MB per added circuit |`,
  `|---|---|---|---|---|---|---|`,
  ...table.map((r) => `| ${r.circuits} | ${f(r.rssMb)} | ${f(r.anonMb)} | ${f(r.pssMb)} | ${r.threads} | ${f(r.perCircuitMb)} | ${f(r.marginalMb)} |`),
  ``,
  `Idle host after warm-up: ${f(base.rssMb)} MB. Highest level measured: ${last.circuits} circuits at ${f(last.rssMb)} MB. Budget ${budgetMb} MB with ${Math.round(headroom * 100)} % headroom (${f(usable)} MB usable).`,
  `Planning figure (pessimistic slope ${f(slopeHi)} MB per circuit, the cost at the lowest level): **${result.planningCircuits} concurrent interactive pages**. Optimistic (marginal slope ${f(slopeLo)} MB at the highest levels): ${result.optimisticCircuits}.`,
  `Only ${last.circuits} circuits were opened; anything above that is arithmetic, not a measurement.`,
].join("\n");
writeFileSync(join(out, "blazor-circuits.md"), md + "\n");
console.log(md);
