import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

/** T-08.03: axe on every page a journey visits; serious and critical violations fail the test. Call after the page has settled. */
export async function expectNoSeriousA11yViolations(page: Page): Promise<void> {
  const { violations } = await new AxeBuilder({ page }).analyze();
  const bad = violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${v.id} (${v.impact}): ${v.nodes.length} node(s)`), `axe on ${page.url()}`).toEqual([]);
}
