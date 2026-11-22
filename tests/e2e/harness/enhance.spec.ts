import { expect, test } from "@playwright/test";
import { join } from "node:path";

// UX-005: wwwroot/enhance.js on a static page (no stack needed beyond the harness global setup). Each behaviour has a no-script fallback that is the page as served, so the spec
// checks the script adds the behaviour and leaves the fallback markup intact.
const script = join(__dirname, "..", "..", "..", "src", "Gabinety.Web", "wwwroot", "enhance.js");

const DIALOG = `<!doctype html><html lang="pl"><body><a id="opener" href="/o">Anuluj rezerwację</a>
  <dialog id="d" data-enhance="dialog" open class="dialog"><h2>Czy na pewno?</h2><a id="dismiss" data-dialog-dismiss href="/back">Nie anuluj</a></dialog></body></html>`;

const PAGE = `<!doctype html><html lang="pl"><body>
  <form method="post" action="/echo" id="f">
    <div class="field"><textarea id="t" class="field__control" name="t"></textarea><p class="field__counter">0 / 2000</p></div>
    <button class="btn" type="submit" name="intent" value="pay" id="pay">Zapłać</button>
  </form>
  <form method="get" action="/x" id="g"><select data-enhance="autosubmit" name="status"><option value="">a</option><option value="b">b</option></select></form>
  <input id="pw" type="password" value="secret"/>
  <button type="button" id="reveal" data-enhance="reveal" data-target="#pw" data-show="Pokaż" data-hide="Ukryj" hidden>Pokaż</button>
  <button type="button" id="copy" data-enhance="copy" data-copy="/gabinet/x" data-done="Skopiowano" hidden>Kopiuj</button>
</body></html>`;

test.describe("enhance.js", () => {
  test.beforeEach(async ({ page }) => {
    await page.route("**/echo", (r) => r.fulfill({ status: 200, contentType: "text/plain", body: "ok" }));
    await page.route("**/x**", (r) => r.fulfill({ status: 200, contentType: "text/plain", body: "ok" }));
    await page.route("**/back", (r) => r.fulfill({ status: 200, contentType: "text/plain", body: "back" }));
    await page.goto("/echo"); // a real origin (route-fulfilled), so the forms resolve their actions
    await page.setContent(PAGE);
    await page.addScriptTag({ path: script });
  });

  test("the counter follows the text and counts a newline as two characters, like the server", async ({ page }) => {
    await page.locator("#t").fill("ab\ncd ");
    await expect(page.locator(".field__counter")).toHaveText("6 / 2000"); // "ab" CR LF "cd", trailing space trimmed
  });

  test("a submit marks the clicked button busy, posts its value, and a second submit is ignored", async ({ page }) => {
    let posts = 0, body = "";
    await page.route("**/echo", (r) => { posts++; body = r.request().postData() ?? ""; return r.fulfill({ status: 200, contentType: "text/plain", body: "ok" }); });
    await page.locator("#pay").click();
    await page.waitForLoadState();
    expect(posts).toBe(1);
    expect(body).toContain("intent=pay"); // the button is not disabled, so name and value still travel
  });

  test("the busy state is on the button and the form refuses a second submit", async ({ page }) => {
    await page.evaluate(() => { document.getElementById("f")!.addEventListener("submit", (e) => e.preventDefault()); }); // keep the page, observe the state
    await page.locator("#pay").click();
    await expect(page.locator("#pay")).toHaveAttribute("aria-busy", "true");
    await expect(page.locator("#f")).toHaveAttribute("data-submitting", "true");
  });

  test("a filter select submits its GET form on change", async ({ page }) => {
    await page.locator("select").selectOption("b");
    await page.waitForURL("**/x?status=b");
  });

  test("show and hide controls appear and toggle the password field", async ({ page }) => {
    await expect(page.locator("#reveal")).toBeVisible();
    await page.locator("#reveal").click();
    await expect(page.locator("#pw")).toHaveAttribute("type", "text");
    await expect(page.locator("#reveal")).toHaveText("Ukryj");
    await expect(page.locator("#reveal")).toHaveAttribute("aria-pressed", "true");
    await page.locator("#reveal").click();
    await expect(page.locator("#pw")).toHaveAttribute("type", "password");
  });

  test("an open dialog becomes modal and Esc follows the dismiss link", async ({ page }) => {
    await page.setContent(DIALOG); // the shared page has no dialog: a modal makes everything outside it inert
    await page.addScriptTag({ path: script });
    expect(await page.evaluate(() => document.querySelector("dialog")!.matches(":modal"))).toBe(true);
    await page.keyboard.press("Escape");
    await page.waitForURL("**/back");
  });

  test("data-enhance=off leaves an element alone", async ({ page }) => {
    await page.evaluate(() => document.getElementById("f")!.setAttribute("data-enhance", "off"));
    await page.evaluate(() => document.getElementById("f")!.addEventListener("submit", (e) => e.preventDefault()));
    await page.locator("#pay").click();
    await expect(page.locator("#pay")).not.toHaveAttribute("aria-busy", "true");
  });
});
