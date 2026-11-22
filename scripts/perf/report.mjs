#!/usr/bin/env node
// Reads the raw results of scripts/measure-performance.sh, judges each SPEC 13 row against its target, prints the table and writes results.json.
// A row is "pass" or "miss" on the measured number exactly as it is (no rounding in favour of the target), "invalid" when a request failed or the run is incomplete,
// and "not measured" with the reason when it cannot be measured on this machine. Numbers measured here are NOT the staging numbers the targets are about.
import { readFileSync, existsSync, writeFileSync, readdirSync } from "node:fs";

const out = process.argv[2];
const raw = (id) => existsSync(`${out}/raw/${id}.json`) ? JSON.parse(readFileSync(`${out}/raw/${id}.json`, "utf8")) : null;
const commands = Object.fromEntries(readFileSync(`${out}/commands.txt`, "utf8").split("\n").filter(Boolean).map((l) => { const i = l.indexOf("\t"); return [l.slice(0, i), l.slice(i + 1)]; }));
const env = JSON.parse(readFileSync(`${out}/environment.json`, "utf8"));
const prep = JSON.parse(readFileSync(`${out}/inputs/prep.json`, "utf8"));
const rows = [];

function load(id, row, tool, metric, pick, target, extra = "") {
  const r = raw(id);
  if (!r) return rows.push({ id, row, tool, command: commands[id], metric, value: null, target, verdict: "not measured", note: "no raw result" });
  const value = pick(r); const bad = r.nonOk > 0 || r.ok === 0;
  const verdict = bad ? "invalid" : value <= target ? "pass" : "miss";
  rows.push({ id, row, tool, command: commands[id], metric, value, unit: "ms", target, verdict, requests: r.requests, rps: r.rps, statusCounts: r.statusCounts, ttfbMs: r.ttfbMs, totalMs: r.totalMs,
    note: (bad ? `${r.nonOk} of ${r.requests} requests did not answer 2xx ${JSON.stringify(r.statusCounts)}. ` : "") + extra });
}

load("list", "GET /api/v1/practitioners (filtered, 12 items), server time, 20 clients", "scripts/perf/load.mjs", "p95 total ms", (r) => r.totalMs.p95, 300,
  `${env.approvedPractitioners} approved practitioners; the client-side total includes JSON transfer on loopback, so it is at or above the server time`);
load("slots", "GET .../slots, 14 days", "scripts/perf/load.mjs", "p95 total ms", (r) => r.totalMs.p95, 200, "20 clients rotating over the slots URLs of the six practitioners with availability");
load("bookings", "POST /api/v1/bookings", "scripts/perf/load.mjs", "p95 total ms", (r) => r.totalMs.p95, 500,
  `${prep.bookingRequests} of ${prep.bookingsWanted} wanted requests prepared, 4 clients, distinct slots; the mock payment provider answers in-process, so the provider call is not part of it`);

// category TTFB: the verdict takes the worse of the unloaded and the loaded run
const c1 = raw("category-c1"), c20 = raw("category-c20");
if (c1 && c20) {
  const worst = Math.max(c1.ttfbMs.p75, c20.ttfbMs.p75), bad = c1.nonOk + c20.nonOk > 0 || c1.ok === 0 || c20.ok === 0;
  rows.push({ id: "category-ttfb", row: "Category page time to first byte, cached, p75", tool: "scripts/perf/load.mjs", command: `${commands["category-c1"]}  |  ${commands["category-c20"]}`,
    metric: "p75 ttfb ms (worse of 1 and 20 clients)", value: worst, unit: "ms", target: 800, verdict: bad ? "invalid" : worst <= 800 ? "pass" : "miss",
    detail: { unloaded: c1.ttfbMs, loaded20: c20.ttfbMs, rpsUnloaded: c1.rps, rpsLoaded20: c20.rps },
    note: "local loopback to the Release build of the Blazor host (Production), NOT from outside and NOT staging" });
} else rows.push({ id: "category-ttfb", row: "Category page time to first byte, cached, p75", tool: "-", metric: "p75 ttfb ms", value: null, target: 800, verdict: "not measured", note: "no raw result" });

const lh = readdirSync(`${out}/raw`).filter((f) => /^lighthouse-\d+\.json$/.test(f)).map((f) => JSON.parse(readFileSync(`${out}/raw/${f}`, "utf8")));
if (lh.length) {
  const med = (xs) => [...xs].sort((a, b) => a - b)[Math.floor((xs.length - 1) / 2)]; // the lower median: never the better of two middle values
  const lcp = lh.map((x) => x.lcpMs), worst = Math.max(...lcp);
  rows.push({ id: "lcp", row: "Category page Largest Contentful Paint, mobile profile, lab", tool: `lighthouse ${env.lighthouse} (npx)`, command: commands["lighthouse-1"], metric: "LCP ms, median of runs (all runs listed)",
    value: med(lcp), unit: "ms", target: 2500, verdict: med(lcp) <= 2500 ? (worst <= 2500 ? "pass" : "pass (median; worst run misses)") : "miss",
    detail: { runs: lh.map((x) => ({ lcpMs: x.lcpMs, cls: x.cls, fcpMs: x.fcpMs, tbtMs: x.tbtMs, ttfbMs: x.ttfbMs, score: x.score })), throttling: lh[0].throttling, formFactor: lh[0].formFactor },
    note: "local production build, Chromium of this container, Lighthouse default mobile emulation with simulated throttling; SPEC 13 asks for CI against staging" });
} else rows.push({ id: "lcp", row: "Category page Largest Contentful Paint, mobile profile, lab", tool: "lighthouse", metric: "LCP ms", value: null, target: 2500, verdict: "not measured", note: "Lighthouse did not run (SKIP_LIGHTHOUSE=1 or it failed)" });

rows.push({ id: "brief", row: "`brief` end to end, p95", tool: "-", metric: "p95 s", value: null, target: 20000, verdict: "not measured", note: "needs the Assistant on a real bring-your-own-key model (T-06.05, owner's key); the fake model proves nothing about it" });
rows.push({ id: "joblag", row: "Job lag (hold expiry, auto-complete, payout), p99", tool: "-", metric: "p99 s over 24 h", value: null, target: 120000, verdict: "not measured", note: "staging not deployed; the row asks for 24 h on staging" });

// PR #113
const pr113 = [];
for (const page of ["programisci", "gabinet", "regulamin"]) for (const c of [1, 20]) {
  const b = raw(`pr113-baseline-${page}-c${c}`), n = raw(`pr113-current-${page}-c${c}`);
  if (b && n) pr113.push({ page, clients: c, baseline: { rps: b.rps, ttfbMs: b.ttfbMs, nonOk: b.nonOk }, current: { rps: n.rps, ttfbMs: n.ttfbMs, nonOk: n.nonOk } });
}

writeFileSync(`${out}/results.json`, JSON.stringify({ environment: env, rows, pr113, note: "Measured on one container; NOT comparable with staging." }, null, 2));

const fmt = (v) => v === null ? "-" : Number.isInteger(v) ? String(v) : v.toFixed(1);
const lines = [["row", "tool", "metric", "measured", "target", "result"], ...rows.map((r) => [r.row, r.tool, r.metric, r.value === null ? "not measured" : fmt(r.value) + (r.unit ? " " + r.unit : ""), r.target + " ms", r.verdict])];
const w = lines[0].map((_, i) => Math.min(60, Math.max(...lines.map((l) => String(l[i]).length))));
for (const l of lines) console.log(l.map((c, i) => String(c).padEnd(w[i])).join("  "));
if (pr113.length) {
  console.log("\nPR #113 (ttfb p50/p95 ms, requests per second):");
  if (pr113.some((p) => p.baseline.nonOk || p.current.nonOk)) console.log("WARNING: some requests of the PR #113 runs did not answer 2xx; those numbers are invalid");
  for (const p of pr113) console.log(`${p.page.padEnd(10)} c=${String(p.clients).padEnd(2)} before ${p.baseline.ttfbMs.p50}/${p.baseline.ttfbMs.p95} ${p.baseline.rps} rps   after ${p.current.ttfbMs.p50}/${p.current.ttfbMs.p95} ${p.current.rps} rps`);
}
console.log(`\nwritten: ${out}/results.json`);
