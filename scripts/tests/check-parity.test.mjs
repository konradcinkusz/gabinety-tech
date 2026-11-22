import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { canonical, compare, nextRoutes } from "../check-parity.mjs";

test("dynamic segments compare by position, not by name", () => {
  assert.equal(canonical("/gabinet/[slug]"), canonical("/gabinet/{Slug}"));
  assert.equal(canonical("/programisci/[problem]/[tech]"), "/programisci/{}/{}");
  assert.notEqual(canonical("/gabinet/[slug]"), canonical("/gabinet"));
});

test("Next routes drop route groups and include sitemap and robots", () => {
  const r = nextRoutes();
  assert.ok(r.includes("/programisci") && r.includes("/gabinet/[slug]") && r.includes("/sitemap.xml") && r.includes("/robots.txt"));
  assert.ok(!r.some((x) => x.includes("(")), "no route group in a route");
});

test("the API routes of the BFF are exempt with a reason, never counted as missing", () => {
  const { rows } = compare();
  const api = rows.filter((x) => x.route.startsWith("/api/"));
  assert.ok(api.length > 0 && api.every((x) => x.status === "exempt" && x.reason));
});

test("report-only exits 0; the gate exits 1 exactly when something is missing", () => {
  const run = (gate) => spawnSync("node", ["scripts/check-parity.mjs"], { env: { ...process.env, CUTOVER_GATE: gate }, encoding: "utf8" });
  const missing = compare().missing.length;
  assert.equal(run("0").status, 0);
  assert.equal(run("1").status, missing > 0 ? 1 : 0);
});
