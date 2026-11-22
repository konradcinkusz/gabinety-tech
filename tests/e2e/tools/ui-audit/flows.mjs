// Flows: the client booking journey (J3/J5/J6), practitioner onboarding (J2) and the administrator's queue, driven through the UI.
// Each step is photographed at 1440 and 390 (the page is resized in place, so both show the same state) and gets the mechanical checks.
import { mkdirSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { VIEWPORTS, axeCheck, inPageChecks, newContext, settle } from "./lib.mjs";

export async function runFlows(browser, { OUT, shotCount, findings }) {
  const log = [];
  const MOBILE_SHOTS = new Set(["02-booking-form", "03-booking-validation", "05-checkout", "08-booking-detail", "09-cancel-dialog", "11-dispute-validation", "13-review-validation", "04-dashboard-draft", "05-profile-validation", "06-offerings-validation", "01-dispute-resolve-form", "01-register-validation"]);
  async function snap(page, role, id, note) {
    const rec = { flow: role, step: id, note, url: page.url().replace(/^https?:\/\/[^/]+/, "") };
    for (const vp of [1440, 390]) {
      await page.setViewportSize(VIEWPORTS[vp]); await page.waitForTimeout(250);
      const c = await page.evaluate(inPageChecks, vp === 390);
      const axe = await axeCheck(page);
      const file = `${role}/flow-${id}-${vp}-light.png`;
      mkdirSync(dirname(resolve(OUT, file)), { recursive: true });
      await page.evaluate(() => window.scrollTo(0, 0));
      const h = Math.min(c.docHeight, vp === 390 ? 4200 : 3000);
      const shoot = vp === 1440 || MOBILE_SHOTS.has(id); // image budget: every step on the desktop, the key steps on the phone
      if (shoot) { await page.screenshot({ path: resolve(OUT, file), fullPage: true, clip: { x: 0, y: 0, width: VIEWPORTS[vp].width, height: h } }); shotCount.n++; }
      rec[vp] = { screenshot: shoot ? `docs/ui/audit/${file}` : null, hscroll: c.hscroll, offscreen: c.offscreen, overlaps: c.overlaps, tapTotal: c.tapTotal, clipped: c.clipped, axe: axe.map((a) => `${a.id}:${a.count}`) };
    }
    await page.setViewportSize(VIEWPORTS[1440]);
    log.push(rec); console.log("flow", role, id, rec.url);
  }
  const attempt = async (name, fn) => { try { await fn(); } catch (e) { log.push({ step: name, error: String(e).slice(0, 220) }); console.log("flow FAIL", name, String(e).slice(0, 160)); } };

  // ---- client ----
  {
    const ctx = await newContext(browser, { userName: "demo-client", vp: 1440, scheme: "light" });
    const page = await ctx.newPage(); page.setDefaultTimeout(25000);
    let bookingId;
    await attempt("browse", async () => {
      await page.goto("/programisci"); await settle(page);
      await page.getByRole("navigation", { name: "Filtry" }).getByRole("link", { name: "Audyt kodu", exact: true }).click(); await settle(page);
      await snap(page, "client", "01-category", "after choosing a category");
    });
    await attempt("profile+slot", async () => {
      await page.goto("/gabinet/demo-celina-przykladowska"); await settle(page, 1500);
      const slot = page.getByRole("link", { name: /^\d{2}:\d{2}$/ }).first();
      await slot.click(); await page.waitForURL(/\/rezerwacja\//); await settle(page);
      await snap(page, "client", "02-booking-form", "booking page, empty");
    });
    await attempt("booking validation", async () => {
      await page.getByRole("button", { name: "Zapłać" }).click(); await page.waitForTimeout(400);
      await snap(page, "client", "03-booking-validation", "pay pressed with empty brief");
    });
    await attempt("assistant", async () => {
      await page.getByLabel("Opisz problem").fill("Aplikacja zaczęła się wieszać po aktualizacji biblioteki, szukam przyczyny w kodzie obsługi zamówień.");
      const b = page.getByRole("button", { name: "Pomóż mi opisać problem" });
      if (await b.count()) { await b.click(); await page.waitForTimeout(2500); await snap(page, "client", "04-booking-assistant", "after asking the assistant"); }
    });
    await attempt("pay", async () => {
      await page.getByLabel("Opisz problem").fill("Aplikacja zaczęła się wieszać po aktualizacji biblioteki, szukam przyczyny w kodzie obsługi zamówień.");
      await page.getByRole("button", { name: "Zapłać" }).click(); await page.waitForURL(/\/checkout\/mock\//); await settle(page);
      bookingId = page.url().split("/").pop().split("?")[0];
      await snap(page, "client", "05-checkout", "mock checkout");
      await page.getByRole("button", { name: "Symuluj udaną płatność" }).click(); await page.waitForURL(/potwierdzenie/); await settle(page, 1500);
      await snap(page, "client", "06-confirmation", "confirmation");
    });
    await attempt("my bookings", async () => { await page.goto("/moje/rezerwacje"); await settle(page); await snap(page, "client", "07-my-bookings", "list with the new booking"); });
    await attempt("detail+cancel", async () => {
      await page.goto(`/moje/rezerwacje/${bookingId}?role=client`); await settle(page);
      await snap(page, "client", "08-booking-detail", "new booking, confirmed");
      await page.getByRole("button", { name: "Anuluj rezerwację" }).click(); await page.waitForTimeout(400);
      await snap(page, "client", "09-cancel-dialog", "cancel confirmation dialog");
      await page.getByRole("button", { name: "Tak, anuluj rezerwację" }).click(); await page.getByText("Rezerwacja została anulowana.").waitFor(); await settle(page, 500);
      await snap(page, "client", "10-cancelled", "after cancel");
    });
    await attempt("review", async () => {
      // the review form is reached from a completed booking's detail page ("Wystaw opinię"), not from the list
      await page.goto("/moje/rezerwacje"); await settle(page);
      const hrefs = await page.$$eval('a[href^="/moje/rezerwacje/"]', (as) => as.map((a) => a.getAttribute("href")));
      let found = false;
      for (const h of hrefs) {
        await page.goto(h); await settle(page, 500);
        const link = page.getByRole("link", { name: "Wystaw opinię" });
        if (await link.count()) { await link.click(); found = true; break; }
      }
      if (!found) throw new Error("no booking offers 'Wystaw opinię'");
      await settle(page);
      await page.getByRole("button", { name: "Wyślij opinię" }).click(); await page.waitForTimeout(400);
      await snap(page, "client", "13-review-validation", "review submitted empty");
      await page.getByRole("radio", { name: "4 z 5" }).check();
      await page.getByLabel("Treść opinii").fill("Konkretna rozmowa, dostałem plan działania i listę ryzyk. Polecam.");
      await page.getByRole("button", { name: "Wyślij opinię" }).click(); await page.waitForTimeout(1500);
      await snap(page, "client", "14-review-sent", "after sending the review");
    });
    await attempt("dispute", async () => {
      await page.goto("/moje/rezerwacje"); await settle(page);
      const hrefs = await page.$$eval('a[href^="/moje/rezerwacje/"]', (as) => as.map((a) => a.getAttribute("href")));
      let done = false;
      for (const h of hrefs) {
        await page.goto(h); await settle(page, 500);
        if (await page.getByRole("button", { name: "Zgłoś spór" }).count()) {
          await page.getByRole("button", { name: "Zgłoś spór" }).click(); await page.waitForTimeout(300);
          await page.getByRole("button", { name: "Wyślij zgłoszenie" }).click(); await page.waitForTimeout(400);
          await snap(page, "client", "11-dispute-validation", "dispute form submitted empty");
          await page.getByLabel("Opis sporu").fill("Konsultacja nie odbyła się w umówionym terminie, programista się nie połączył.");
          await page.getByRole("button", { name: "Wyślij zgłoszenie" }).click(); await page.waitForTimeout(1500);
          await snap(page, "client", "12-dispute-sent", "after sending the dispute"); done = true; break;
        }
      }
      if (!done) throw new Error("no seeded booking offers 'Zgłoś spór' (canDispute false for all four) - the client dispute form is not reachable in the seeded state");
    });
    await ctx.close();
  }

  // ---- new practitioner ----
  {
    const ctx = await newContext(browser, { userName: null, vp: 1440, scheme: "light" });
    const page = await ctx.newPage(); page.setDefaultTimeout(25000);
    const email = `demo-audit-${Date.now()}@demo.example`;
    await attempt("register validation", async () => {
      await page.goto("/rejestracja"); await settle(page);
      await page.getByRole("button", { name: "Załóż konto" }).click(); await page.waitForTimeout(400);
      await snap(page, "newpractitioner", "01-register-validation", "register submitted empty");
      await page.getByLabel("Adres e-mail").fill(email); await page.getByLabel("Hasło").fill("short");
      await page.getByRole("button", { name: "Załóż konto" }).click(); await page.waitForTimeout(400);
      await snap(page, "newpractitioner", "02-register-short-password", "short password, no consent");
      await page.getByLabel("Hasło").fill("Demo-Dev-Passw0rd!"); await page.getByRole("checkbox", { name: /Akceptuję Regulamin/ }).check();
      await page.getByRole("button", { name: "Załóż konto" }).click(); await page.waitForURL((u) => !/rejestracja/.test(u.pathname)); await settle(page);
    });
    await attempt("login failure", async () => {
      const c2 = await newContext(browser, { userName: null, vp: 1440, scheme: "light" }); const p2 = await c2.newPage();
      await p2.goto("/logowanie"); await settle(p2);
      await p2.getByLabel("Adres e-mail").fill("demo-client@demo.example"); await p2.getByLabel("Hasło").fill("zle-haslo-123");
      await p2.getByRole("button", { name: "Zaloguj się" }).click(); await p2.waitForTimeout(1500);
      await snap(p2, "visitor", "login-wrong-password", "wrong password"); await c2.close();
    });
    await attempt("onboarding", async () => {
      await page.goto("/gabinet-programisty"); await settle(page);
      await snap(page, "newpractitioner", "03-create-profile", "no profile yet");
      await page.getByLabel("Nazwa wyświetlana").fill("Demo Audytowy"); await page.getByRole("button", { name: "Załóż gabinet" }).click();
      await page.getByRole("heading", { level: 1, name: "Panel programisty" }).waitFor(); await settle(page);
      await snap(page, "newpractitioner", "04-dashboard-draft", "draft dashboard with checklist");
      await page.goto("/gabinet-programisty/profil"); await settle(page);
      await page.getByRole("button", { name: "Zapisz" }).click(); await page.waitForTimeout(600);
      await snap(page, "newpractitioner", "05-profile-validation", "profile saved empty");
      await page.goto("/gabinet-programisty/oferty?nowa=1"); await settle(page);
      await page.getByRole("button", { name: "Zapisz" }).click(); await page.waitForTimeout(600);
      await snap(page, "newpractitioner", "06-offerings-validation", "offer saved empty");
      await page.goto("/gabinet-programisty/dostepnosc"); await settle(page);
      await snap(page, "newpractitioner", "07-availability-empty", "availability, nothing set");
    });
    await ctx.close();
  }

  // ---- admin: one open verification detail and one resolve dialog ----
  {
    const ctx = await newContext(browser, { userName: "demo-admin", vp: 1440, scheme: "light" });
    const page = await ctx.newPage(); page.setDefaultTimeout(25000);
    await attempt("admin dispute resolve", async () => {
      await page.goto("/admin/spory"); await settle(page);
      const b = page.getByRole("button", { name: "Rozstrzygnij" }).first();
      if (!(await b.count())) throw new Error("no open dispute to resolve (after the client flow one should exist)");
      await b.click(); await page.waitForTimeout(400);
      await snap(page, "admin", "01-dispute-resolve-form", "resolve form opened");
      await page.getByRole("button", { name: "Przejdź do potwierdzenia" }).click().catch(() => {}); await page.waitForTimeout(500);
      await snap(page, "admin", "02-dispute-resolve-validation", "continue pressed without choice");
    });
    await ctx.close();
  }
  return log;
}
