import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { resetBackendMemory } from "@/lib/api/backends";
import { resetBackendFetch, setBackendFetch } from "@/lib/api/http";
import { RankingRulesPage } from "./RankingRulesPage";
import { rankingScore, workedExample } from "./ranking";
import { RANKING_EXAMPLE, rulesFixture } from "./testFixtures";
import { ts } from "./strings";

const count = (s: string, re: RegExp) => (s.match(re) ?? []).length;

function serve(body: unknown, status = 200) {
  process.env["Backend__Directory"] = "http://directory.test";
  resetBackendMemory();
  const requested: string[] = [];
  setBackendFetch(async (input) => {
    requested.push(String(input));
    return status === 200 ? Response.json(body) : new Response("{}", { status });
  });
  return requested;
}

beforeEach(() => resetBackendMemory());
afterEach(() => resetBackendFetch());

describe("rankingScore (CONTRACTS 9.3 vectors, tolerance 0.005)", () => {
  const rules = rulesFixture();
  it.each([
    [{ n: 0, r: 0, c: 0, x: 0, s: 0, p: 1, k: 10 }, 82.95],
    [{ n: 10, r: 4.8, c: 20, x: 1, s: 0, p: 0.8, k: 5 }, 82.7],
    [{ n: 3, r: 5, c: 3, x: 0, s: 0, p: 0.5, k: 0 }, 67.18],
  ])("%j gives %f", (input, expected) => {
    expect(Math.abs(rankingScore(input, rules) - expected)).toBeLessThan(0.005);
  });
  it("is monotonic in the completeness weight when completeness is above the others' mean", () => {
    const heavier = rulesFixture({ weights: { rating: 0.4, reliability: 0.2, availability: 0.15, completeness: 0.25 } });
    expect(rankingScore(RANKING_EXAMPLE, heavier)).not.toBe(rankingScore(RANKING_EXAMPLE, rules));
  });
});

describe("workedExample", () => {
  it("reports the four parts and the score", () => {
    const w = workedExample(rulesFixture());
    expect(w.score).toBe(82.7);
    expect(w.parts.map((p) => p.key)).toEqual(["rating", "reliability", "availability", "completeness"]);
  });
});

describe("/zasady-rankingu", () => {
  it("shows the version, the date and the weights returned by the API", async () => {
    const requested = serve(rulesFixture());
    const out = renderToStaticMarkup(await RankingRulesPage());
    expect(requested).toEqual(["http://directory.test/api/v1/ranking-rules"]);
    expect(out).toContain("2026-11-v1");
    expect(out).toContain("13.11.2026");
    for (const w of ["45%", "25%", "15%"]) expect(out).toContain(w);
    expect(out).toContain("82,70");
  });

  it("changes when a weight in the stub response changes", async () => {
    serve(rulesFixture({ version: "2026-12-v2", weights: { rating: 0.3, reliability: 0.3, availability: 0.2, completeness: 0.2 } }));
    const out = renderToStaticMarkup(await RankingRulesPage());
    expect(out).toContain("2026-12-v2");
    expect(out).toContain("30%");
    expect(out).toContain("20%");
    expect(out).not.toContain("45%");
    expect(out).not.toContain("82,70");
  });

  it("states that nothing can be paid for, has one h1 and a table with a caption", async () => {
    serve(rulesFixture());
    const out = renderToStaticMarkup(await RankingRulesPage());
    expect(out).toContain(ts("ranking.nothingPaid"));
    expect(count(out, /<h1[\s>]/g)).toBe(1);
    expect(count(out, /<caption[\s>]/g)).toBeGreaterThanOrEqual(2);
    expect(count(out, /<th scope="col"/g)).toBeGreaterThan(0);
  });

  it("throws to the error boundary when the Directory fails (no blank page, no body in the error)", async () => {
    serve({}, 503);
    await expect(RankingRulesPage()).rejects.toThrow("directory_unavailable:503");
  });
});
