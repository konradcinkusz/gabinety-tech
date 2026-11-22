#!/usr/bin/env node
// UI audit: visits every route of src/Gabinety.Web (Blazor host) as visitor, client, practitioner and administrator at 1440x900 and 390x844, light and dark,
// records mechanical findings (overflow, overlap, clipping, contrast via axe, focus, tap targets, alt, console, requests, layout shift, empties)
// and writes screenshots + findings.json under docs/ui/audit. Usage: node tools/ui-audit/audit.mjs   (from tests/e2e, fake profile running).
// Env: PW_CHROMIUM_PATH, E2E_WEB_URL, AUDIT_OUT (default ../../docs/ui/audit), AUDIT_ONLY=<role>.
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { BASE, SCHEMES, VIEWPORTS, axeCheck, focusCheck, inPageChecks, launch, newContext, settle } from "./lib.mjs";
import { runFlows } from "./flows.mjs";

const here = dirname(fileURLToPath(import.meta.url));
export const OUT = resolve(process.env.AUDIT_OUT ?? resolve(here, "../../../../docs/ui/audit"));
mkdirSync(OUT, { recursive: true });

const slug = "demo-celina-przykladowska";
const PUBLIC_ROUTES = [
  { id: "home", path: "/", dark: "both" },
  { id: "programisci", path: "/programisci", dark: "both" },
  { id: "programisci-category", path: "/programisci/audyt-kodu" },
  { id: "programisci-category-tech", path: "/programisci/audyt-kodu/dotnet", shots: "none" },
  { id: "programisci-filtered-empty", path: "/programisci?q=zzzzzz", shots: "mobile" },
  { id: "gabinet-profile", path: `/gabinet/${slug}`, dark: "both" },
  { id: "gabinet-unknown", path: "/gabinet/demo-nie-ma-takiego", shots: "mobile" },
  { id: "jak-to-dziala", path: "/jak-to-dziala", shots: "mobile" },
  { id: "zasady-rankingu", path: "/zasady-rankingu", shots: "mobile" },
  { id: "cennik", path: "/cennik", dark: true },
  { id: "regulamin", path: "/regulamin", shots: "desktop" },
  { id: "polityka-prywatnosci", path: "/polityka-prywatnosci", shots: "none" },
  { id: "logowanie", path: "/logowanie", dark: true },
  { id: "rejestracja", path: "/rejestracja", shots: "none" },
  { id: "rejestracja-sprawdz-skrzynke", path: "/rejestracja/sprawdz-skrzynke", shots: "mobile" },
  { id: "not-found", path: "/nie-ma-takiej-strony", shots: "mobile" },
];
const AUTH_GUARDS = ["/konto", "/moje/rezerwacje", "/gabinet-programisty", "/admin/spory", "/checkout/mock/00000000-0000-0000-0000-000000000000"];

/** Routes per role; `{...}` placeholders are filled from discovery. */
const ROLE_ROUTES = {
  visitor: PUBLIC_ROUTES,
  client: [
    { id: "home-signed-in", path: "/", shots: "desktop" },
    { id: "konto", path: "/konto" },
    { id: "moje-rezerwacje", path: "/moje/rezerwacje", dark: true },
    { id: "moje-rezerwacje-practitioner-tab", path: "/moje/rezerwacje?role=practitioner", shots: "none" },
    { id: "moje-opinie", path: "/moje/opinie", shots: "desktop" },
    { id: "admin-forbidden", path: "/admin/spory", shots: "desktop" },
    { id: "gabinet-programisty-forbidden", path: "/gabinet-programisty", shots: "desktop" },
  ],
  practitioner: [
    { id: "gabinet-programisty", path: "/gabinet-programisty", dark: true },
    { id: "gabinet-programisty-profil", path: "/gabinet-programisty/profil" },
    { id: "gabinet-programisty-oferty", path: "/gabinet-programisty/oferty" },
    { id: "gabinet-programisty-dostepnosc", path: "/gabinet-programisty/dostepnosc", dark: true },
    { id: "gabinet-programisty-platnosci", path: "/gabinet-programisty/platnosci", shots: "desktop" },
    { id: "moje-rezerwacje-practitioner", path: "/moje/rezerwacje?role=practitioner" },
    { id: "konto", path: "/konto", shots: "none" },
    { id: "booking-detail-practitioner", path: "/moje/rezerwacje/{practitionerBooking}?role=practitioner" },
  ],
  admin: [
    { id: "admin-weryfikacja", path: "/admin/weryfikacja", dark: true },
    { id: "admin-weryfikacja-submitted", path: "/admin/weryfikacja?status=Submitted", shots: "none" },
    { id: "admin-weryfikacja-detail", path: "/admin/weryfikacja/{practitionerId}" },
    { id: "admin-spory", path: "/admin/spory", dark: true },
    { id: "admin-ksiega", path: "/admin/ksiega" },
    { id: "admin-podejrzane", path: "/admin/podejrzane", shots: "desktop" },
    { id: "admin-opinie", path: "/admin/opinie", shots: "desktop" },
  ],
};
const USERS = { visitor: null, client: "demo-client", practitioner: "demo-practitioner-3", admin: "demo-admin" };

const findings = []; const links = {}; const guards = [];
const shotCount = { n: 0 };
const fileOf = (role, id, vp, scheme) => `${role}/${id}-${vp}-${scheme}.png`;

/** Discovers the ids the routes need (booking ids of the client and of the practitioner, practitioner id for the admin detail). */
async function discover(browser) {
  const ids = {};
  const ctx = await newContext(browser, { userName: "demo-client", vp: 1440, scheme: "light" });
  const page = await ctx.newPage();
  await page.goto("/moje/rezerwacje"); await settle(page);
  ids.clientBookings = [...new Set(await page.$$eval('a[href^="/moje/rezerwacje/"]', (as) => as.map((a) => a.getAttribute("href").split("?")[0].split("/").pop())))];
  await ctx.close();
  const pc = await newContext(browser, { userName: "demo-practitioner-3", vp: 1440, scheme: "light" });
  const pp = await pc.newPage();
  await pp.goto("/moje/rezerwacje?role=practitioner"); await settle(pp);
  ids.practitionerBookings = [...new Set(await pp.$$eval('a[href^="/moje/rezerwacje/"]', (as) => as.map((a) => a.getAttribute("href").split("?")[0].split("/").pop())))];
  await pc.close();
  const ac = await newContext(browser, { userName: "demo-admin", vp: 1440, scheme: "light" });
  const ap = await ac.newPage();
  await ap.goto("/admin/weryfikacja"); await settle(ap);
  ids.practitionerIds = [...new Set(await ap.$$eval('a[href^="/admin/weryfikacja/"]', (as) => as.map((a) => a.getAttribute("href").split("/").pop())))];
  await ac.close();
  console.log("discovered", JSON.stringify({ clientBookings: ids.clientBookings.length, practitionerBookings: ids.practitionerBookings.length, practitionerIds: ids.practitionerIds.length }));
  return ids;
}

async function visit(page, role, route, vp, scheme, wantShot) {
  const errors = []; const failed = []; const badStatus = [];
  const onConsole = (m) => { if (m.type() === "error") errors.push(m.text().slice(0, 160)); };
  const onPageErr = (e) => errors.push(`pageerror: ${String(e.message).slice(0, 160)}`);
  const onFail = (r) => failed.push(`${r.method()} ${new URL(r.url()).pathname} ${r.failure()?.errorText ?? ""}`);
  const onResp = (r) => { if (r.status() >= 400 && r.url().startsWith(BASE)) badStatus.push(`${r.status()} ${r.request().method()} ${new URL(r.url()).pathname}`); };
  page.on("console", onConsole); page.on("pageerror", onPageErr); page.on("requestfailed", onFail); page.on("response", onResp);
  const f = { role, route: route.id, path: route.path, vp, scheme };
  try {
    const res = await page.goto(route.path, { waitUntil: "domcontentloaded" });
    f.status = res?.status(); f.finalUrl = new URL(page.url()).pathname + new URL(page.url()).search;
    await settle(page);
    f.title = await page.title();
    Object.assign(f, await page.evaluate(inPageChecks, vp === 390));
    f.axe = await axeCheck(page);
    f.focus = await focusCheck(page);
    if (wantShot) {
      const file = fileOf(role, route.id, vp, scheme);
      mkdirSync(dirname(resolve(OUT, file)), { recursive: true });
      await page.evaluate(() => window.scrollTo(0, 0));
      const h = Math.min(f.docHeight ?? 900, vp === 390 ? 5200 : 3600);
      await page.screenshot({ path: resolve(OUT, file), fullPage: true, clip: { x: 0, y: 0, width: VIEWPORTS[vp].width, height: h } });
      f.screenshot = `docs/ui/audit/${file}`; f.truncated = (f.docHeight ?? 0) > h; shotCount.n++;
    }
    if (vp === 1440 && scheme === "light") (links[role] ??= new Set()), f.hrefs?.forEach((h) => links[role].add(h));
  } catch (e) { f.error = String(e).slice(0, 200); }
  f.consoleErrors = [...new Set(errors)].slice(0, 6); f.failedRequests = failed.slice(0, 6); f.badResponses = [...new Set(badStatus)].slice(0, 6);
  delete f.hrefs;
  page.off("console", onConsole); page.off("pageerror", onPageErr); page.off("requestfailed", onFail); page.off("response", onResp);
  findings.push(f);
  return f;
}

/** Clicks the buttons that are safe to click and reports the ones that change nothing (no DOM change, no request, no navigation). */
async function deadButtons(page, role, route) {
  const dead = [];
  const names = await page.$$eval("main button:not([type=submit]):not([disabled]), main [role=button]:not([disabled])", (bs) => bs.filter((b) => b.getBoundingClientRect().width > 0).map((b) => (b.innerText || b.getAttribute("aria-label") || "").trim()).slice(0, 8));
  for (const name of names) {
    if (/anuluj|zatwierd|odrzuć|odrzuc|zapłać|zaplac|symuluj|wyślij|wyslij|usuń|usun|rozstrzygnij|zgłoś|zgloś|wyloguj|zaloguj|zapisz|dodaj|pobierz|eksport|usuń konto/i.test(name)) continue;
    await page.goto(route.path); await settle(page, 500);
    const btn = page.locator("main button:not([type=submit]):not([disabled]), main [role=button]:not([disabled])").filter({ hasText: name }).first();
    if (!(await btn.count())) continue;
    let reqs = 0; const onReq = () => reqs++; page.on("request", onReq);
    const sig = () => page.evaluate(() => `${location.href}|${document.querySelector("main")?.innerHTML.length}|${document.querySelectorAll("[role=dialog],[role=alertdialog],dialog[open]").length}|${document.activeElement?.tagName}`);
    const before = await sig();
    await btn.click({ timeout: 3000 }).catch(() => {}); await page.waitForTimeout(700);
    const after = await sig(); page.off("request", onReq);
    if (before === after && reqs === 0) dead.push(name);
  }
  return dead;
}

const browser = await launch();
const ids = process.env.AUDIT_ONLY === "flows" ? {} : await discover(browser);
const flowsOnly = process.env.AUDIT_ONLY === "flows";
const roles = flowsOnly ? [] : process.env.AUDIT_ONLY ? [process.env.AUDIT_ONLY] : Object.keys(ROLE_ROUTES);
const deadFound = [];

for (const role of roles) {
  for (const vp of [1440, 390]) for (const scheme of SCHEMES) {
    const ctx = await newContext(browser, { userName: USERS[role], vp, scheme });
    const page = await ctx.newPage();
    for (const base of ROLE_ROUTES[role]) {
      let path = base.path;
      path = path.replace("{practitionerBooking}", ids.practitionerBookings[0] ?? "none").replace("{practitionerId}", ids.practitionerIds[0] ?? "none");
      const route = { ...base, path };
      const light = scheme === "light";
      // Screenshot budget (about 100 images): light at the viewports named by `shots` (default both; "mobile" or "desktop" for pages that
      // only need one; "none" when a flow photographs the page), dark only for routes with `dark` (desktop; "both" adds the phone).
      const shots = route.shots === "light" ? "both" : (route.shots ?? "both");
      const wantShot = light ? (shots === "both" || (shots === "mobile" && vp === 390) || (shots === "desktop" && vp === 1440))
        : (route.dark === "both" || (route.dark === true && vp === 1440));
      const f = await visit(page, role, route, vp, scheme, wantShot);
      process.stdout.write(`${role} ${vp} ${scheme} ${route.id} -> ${f.status} ${f.error ?? ""}\n`);
      // inert-button probe: public, read-only pages only. It clicks real buttons, so it must never run where a click writes
      // (the first version clicked "Wyłącz" on a practitioner's offer and disabled it: that is itself a finding, see UI-AUDIT.md).
      if (light && vp === 1440 && role === "visitor" && ["home", "programisci", "gabinet-profile", "cennik", "logowanie"].includes(route.id)) {
        const d = await deadButtons(page, role, route); if (d.length) deadFound.push({ role, route: route.id, buttons: d });
      }
    }
    await ctx.close();
  }
}

// route guards: what a visitor (and a client for admin) gets
for (const g of flowsOnly ? [] : AUTH_GUARDS) {
  const ctx = await newContext(browser, { userName: null, vp: 1440, scheme: "light" });
  const r = await ctx.request.get(g, { maxRedirects: 0 }); const loc = r.headers()["location"];
  guards.push({ who: "visitor", path: g, status: r.status(), location: loc ?? null });
  await ctx.close();
}
if (!flowsOnly) {
  const ctx = await newContext(browser, { userName: "demo-client", vp: 1440, scheme: "light" });
  for (const g of ["/admin/weryfikacja", "/admin/ksiega", "/gabinet-programisty"]) { const r = await ctx.request.get(g, { maxRedirects: 0 }); guards.push({ who: "client", path: g, status: r.status(), location: r.headers()["location"] ?? null }); }
  await ctx.close();
}

// dead links: every internal href seen at 1440 light, fetched as the role that saw it
const deadLinks = [];
for (const role of roles) {
  const ctx = await newContext(browser, { userName: USERS[role], vp: 1440, scheme: "light" });
  for (const h of links[role] ?? []) {
    if (/\/api\/|\/csv|\/export|logout|\/oauth\//.test(h)) continue; // /oauth/github/start is a redirect that needs a sign-in context: a bare GET is a 400 by design
    const r = await ctx.request.get(h, { timeout: 20000 }).catch((e) => ({ status: () => 0, err: e }));
    if (r.status() >= 400 || r.status() === 0) deadLinks.push({ role, href: h, status: r.status() });
  }
  await ctx.close();
}

// flows (client booking journey, practitioner onboarding, admin) with their own screenshots
const flows = (process.env.AUDIT_ONLY && !flowsOnly) ? [] : await runFlows(browser, { OUT, shotCount, findings, ids });

writeFileSync(resolve(OUT, flowsOnly ? "flows.json" : "findings.json"), JSON.stringify({ generatedAt: new Date().toISOString(), base: BASE, shots: shotCount.n, guards, deadLinks, deadButtons: deadFound, flows, findings }, null, 1));
console.log(`done: ${findings.length} page visits, ${shotCount.n} screenshots, ${deadLinks.length} dead links, ${deadFound.length} pages with inert buttons`);
await browser.close();
