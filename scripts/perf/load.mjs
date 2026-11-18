#!/usr/bin/env node
// A small closed-loop HTTP load generator (T-10.04). Why not autocannon or hey: autocannon reports p50/p90/p97.5/p99 (no p95) and hey needs `go install`;
// SPEC 13 asks for p95 and p75, so this records every request and computes exact (nearest-rank) percentiles. No dependencies: node:http only.
//   node scripts/perf/load.mjs --name N --concurrency 20 --duration 20 --warmup 3 [--urls u1,u2,...] [--requests file.json] [--out file.json]
// GET mode: the URLs are cycled round-robin for --duration seconds (after --warmup seconds that are not counted).
// Requests mode: a JSON array of {method, url, headers, body}, each sent exactly once, in order, by --concurrency workers (POST bookings: every body is a distinct slot).
// Times: ttfb = request start to the response headers (time to first byte); total = request start to the end of the body. Milliseconds.
import http from "node:http";
import { readFileSync, writeFileSync } from "node:fs";
import { performance } from "node:perf_hooks";

const arg = (k, d) => { const i = process.argv.indexOf("--" + k); return i < 0 ? d : process.argv[i + 1]; };
const name = arg("name", "run"), conc = +arg("concurrency", "20"), duration = +arg("duration", "20"), warmup = +arg("warmup", "3");
const urls = (arg("urls", "") || "").split(",").filter(Boolean);
const requests = arg("requests") ? JSON.parse(readFileSync(arg("requests"), "utf8")) : null;
const agent = new http.Agent({ keepAlive: true, maxSockets: conc });

function once(r) {
  return new Promise((resolve) => {
    const u = new URL(r.url);
    const t0 = performance.now(); let ttfb = null;
    const req = http.request({ agent, host: u.hostname, port: u.port, path: u.pathname + u.search, method: r.method ?? "GET", headers: r.headers ?? {} }, (res) => {
      ttfb = performance.now() - t0;
      res.on("data", () => {});
      res.on("end", () => resolve({ status: res.statusCode, ttfb, total: performance.now() - t0 }));
    });
    req.on("error", () => resolve({ status: 0, ttfb: ttfb ?? performance.now() - t0, total: performance.now() - t0 }));
    if (r.body !== undefined) req.write(typeof r.body === "string" ? r.body : JSON.stringify(r.body));
    req.end();
  });
}

const pct = (sorted, p) => sorted.length === 0 ? null : sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(p / 100 * sorted.length) - 1))];
const summary = (xs) => { const s = [...xs].sort((a, b) => a - b); const r = (v) => v === null ? null : Math.round(v * 100) / 100; return { p50: r(pct(s, 50)), p75: r(pct(s, 75)), p95: r(pct(s, 95)), p99: r(pct(s, 99)), max: r(s.at(-1) ?? null) }; };

const results = [];
let elapsed;
if (requests) {
  let next = 0; const t0 = performance.now();
  await Promise.all(Array.from({ length: conc }, async () => { for (;;) { const i = next++; if (i >= requests.length) return; results.push(await once(requests[i])); } }));
  elapsed = (performance.now() - t0) / 1000;
} else {
  if (urls.length === 0) { console.error("give --urls or --requests"); process.exit(2); }
  let n = 0; const stopWarm = performance.now() + warmup * 1000; const stop = stopWarm + duration * 1000; let t0 = null;
  await Promise.all(Array.from({ length: conc }, async () => {
    for (;;) {
      const now = performance.now(); if (now >= stop) return;
      const res = await once({ url: urls[n++ % urls.length] });
      if (now >= stopWarm) { t0 ??= now; results.push(res); }
    }
  }));
  elapsed = (performance.now() - (t0 ?? performance.now())) / 1000;
}
const statusCounts = {}; for (const r of results) statusCounts[r.status] = (statusCounts[r.status] ?? 0) + 1;
const ok = results.filter((r) => r.status >= 200 && r.status < 300);
const out = { name, concurrency: conc, requests: results.length, ok: ok.length, nonOk: results.length - ok.length, statusCounts, seconds: Math.round(elapsed * 100) / 100,
  rps: Math.round(results.length / elapsed * 10) / 10, ttfbMs: summary(ok.map((r) => r.ttfb)), totalMs: summary(ok.map((r) => r.total)), urls: requests ? undefined : urls.slice(0, 20) };
if (arg("out")) writeFileSync(arg("out"), JSON.stringify(out, null, 2));
console.log(JSON.stringify(out));
agent.destroy();
