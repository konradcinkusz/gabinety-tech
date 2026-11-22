// Shared helpers of the UI audit: browser contexts per role/viewport/scheme and the mechanical checks that run inside a page.
import { chromium } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

export const BASE = process.env.E2E_WEB_URL ?? "http://localhost:3000";
export const PASSWORD = "Demo-Dev-Passw0rd!"; // the fixed development password of the DevIdentity (docs/GETTING-STARTED.md section 2); protects nothing
export const VIEWPORTS = { 1440: { width: 1440, height: 900 }, 390: { width: 390, height: 844 } };
export const SCHEMES = ["light", "dark"];

export async function launch() {
  const executablePath = process.env.PW_CHROMIUM_PATH ?? (process.env.CHROMIUM ?? undefined);
  return chromium.launch({ executablePath, args: ["--no-sandbox"] });
}

/** One context = one role x viewport x colour scheme. `userName` null = visitor. */
export async function newContext(browser, { userName, vp, scheme }) {
  const ctx = await browser.newContext({
    baseURL: BASE, locale: "pl-PL", timezoneId: "Europe/Warsaw", colorScheme: scheme,
    viewport: VIEWPORTS[vp], isMobile: vp === 390, hasTouch: vp === 390, deviceScaleFactor: 1,
  });
  await ctx.addInitScript(() => {
    window.__cls = 0;
    try {
      new PerformanceObserver((l) => { for (const e of l.getEntries()) if (!e.hadRecentInput) window.__cls += e.value; }).observe({ type: "layout-shift", buffered: true });
    } catch { /* not supported */ }
  });
  if (userName) {
    // The host has no login API: the same static form post as the harness (tests/e2e/harness/auth.ts signInViaFormPost): GET for the antiforgery token, POST the form, expect a redirect.
    const html = await (await ctx.request.get("/logowanie")).text();
    const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(html)?.[1];
    const handler = /name="_handler"[^>]*value="([^"]+)"/.exec(html)?.[1] ?? "login";
    if (!token) throw new Error("no antiforgery token on /logowanie");
    const res = await ctx.request.post("/logowanie", { form: { _handler: handler, __RequestVerificationToken: token, "Model.Email": `${userName}@demo.example`, "Model.Password": PASSWORD }, maxRedirects: 0 });
    if (![302, 303].includes(res.status())) throw new Error(`sign-in as ${userName} failed: ${res.status()}`);
  }
  return ctx;
}

export async function settle(page, extraMs = 900) {
  await page.waitForLoadState("domcontentloaded").catch(() => {});
  await page.waitForLoadState("networkidle", { timeout: 8000 }).catch(() => {});
  await page.waitForTimeout(extraMs);
}

/** Runs inside the page: geometry based checks. Returns small, capped lists. */
export function inPageChecks(isMobile) {
  const cap = (a, n = 8) => a.slice(0, n);
  const vis = (el) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== "hidden" && s.display !== "none" && s.opacity !== "0";
  };
  const isSrOnly = (el) => { const r = el.getBoundingClientRect(); return r.width <= 2 && r.height <= 2; };
  const label = (el) => {
    const t = (el.getAttribute("aria-label") || el.innerText || el.getAttribute("placeholder") || el.getAttribute("name") || el.getAttribute("href") || "").trim().replace(/\s+/g, " ").slice(0, 40);
    return `${el.tagName.toLowerCase()}${el.id ? "#" + el.id : ""}[${t}]`;
  };
  const de = document.documentElement;
  const out = {};
  out.hscroll = de.scrollWidth > de.clientWidth + 1 ? { scrollWidth: de.scrollWidth, clientWidth: de.clientWidth } : null;
  out.docHeight = de.scrollHeight;

  // elements that stick out of the viewport horizontally (the cause of a horizontal scroll)
  const off = [];
  for (const el of document.body.querySelectorAll("*")) {
    if (!vis(el) || isSrOnly(el)) continue;
    const r = el.getBoundingClientRect();
    if (r.right > innerWidth + 1 || r.left < -1) {
      // skip children of an element that is itself a scroll container
      let p = el.parentElement, inScroll = false;
      while (p && p !== document.body) { const o = getComputedStyle(p).overflowX; if (o === "auto" || o === "scroll" || o === "hidden") { inScroll = true; break; } p = p.parentElement; }
      if (!inScroll) off.push(`${label(el)} right=${Math.round(r.right)} left=${Math.round(r.left)}`);
    }
  }
  out.offscreen = cap(off);

  // text clipped by overflow
  const clip = [];
  for (const el of document.body.querySelectorAll("*")) {
    const s = getComputedStyle(el);
    if ((s.overflowX === "hidden" || s.textOverflow === "ellipsis") && el.scrollWidth > el.clientWidth + 1 && vis(el)) clip.push(`${label(el)} scroll=${el.scrollWidth} client=${el.clientWidth}`);
  }
  out.clipped = cap(clip);

  // overlapping interactive elements / images
  const items = [...document.querySelectorAll("a,button,input:not([type=hidden]),select,textarea,img,[role=button],[role=radio]")].filter((e) => vis(e) && !isSrOnly(e));
  const ov = [];
  for (let i = 0; i < items.length && ov.length < 8; i++) {
    const a = items[i].getBoundingClientRect();
    for (let j = i + 1; j < items.length; j++) {
      if (items[i].contains(items[j]) || items[j].contains(items[i])) continue;
      const b = items[j].getBoundingClientRect();
      const w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
      if (w > 2 && h > 2 && (w * h) / Math.min(a.width * a.height, b.width * b.height) > 0.2) { ov.push(`${label(items[i])} x ${label(items[j])}`); if (ov.length >= 8) break; }
    }
  }
  out.overlaps = ov;

  // tap targets under 44 px (only meaningful at 390 px)
  const tap = [];
  if (isMobile) {
    for (const el of document.querySelectorAll("a,button,input:not([type=hidden]),select,textarea,[role=button],[role=radio]")) {
      if (!vis(el) || isSrOnly(el)) continue;
      const r = el.getBoundingClientRect();
      if (r.width < 44 || r.height < 44) {
        const inline = el.tagName === "A" && getComputedStyle(el).display === "inline" && !!el.closest("p,li,dd");
        tap.push(`${label(el)} ${Math.round(r.width)}x${Math.round(r.height)}${inline ? " (inline link)" : ""}`);
      }
    }
  }
  out.tapTotal = tap.length;
  out.tapSmall = cap(tap, 10);

  out.imgNoAlt = cap([...document.querySelectorAll("img")].filter((i) => !i.hasAttribute("alt")).map((i) => i.currentSrc.slice(-60)));
  const main = document.querySelector("main");
  const text = (main?.innerText ?? "").trim();
  out.mainTextLength = text.length;
  out.h1 = [...document.querySelectorAll("h1")].map((h) => h.innerText.trim().slice(0, 80));
  out.emptyLists = document.querySelectorAll("main ul:empty, main ol:empty, main tbody:empty").length;
  out.landmarks = { header: !!document.querySelector("header,[role=banner]"), nav: document.querySelectorAll("nav").length, main: document.querySelectorAll("main").length, footer: !!document.querySelector("footer,[role=contentinfo]"), skip: !!document.querySelector('a[href="#main"],a[href^="#"][class*=skip]') };
  out.hrefs = [...new Set([...document.querySelectorAll("a[href]")].map((a) => a.getAttribute("href")).filter((h) => h && h.startsWith("/") && !h.startsWith("//")))];
  out.formControlsNoLabel = cap([...document.querySelectorAll("input:not([type=hidden]),select,textarea")].filter((c) => !(c.labels && c.labels.length) && !c.getAttribute("aria-label") && !c.getAttribute("aria-labelledby")).map(label));
  out.bodyBg = getComputedStyle(document.body).backgroundColor;
  out.bodyColor = getComputedStyle(document.body).color;
  out.cls = window.__cls ?? 0;
  return out;
}

/** Tabs through the page and reports interactive elements that show no focus indicator. */
export async function focusCheck(page, maxTabs = 30) {
  await page.evaluate(() => { document.activeElement?.blur?.(); window.scrollTo(0, 0); });
  const missing = []; const seen = new Set(); let reached = 0;
  for (let i = 0; i < maxTabs; i++) {
    await page.keyboard.press("Tab");
    const r = await page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) return null;
      const s = getComputedStyle(el);
      const outline = s.outlineStyle !== "none" && parseFloat(s.outlineWidth) > 0 && s.outlineColor !== "rgba(0, 0, 0, 0)";
      const shadow = s.boxShadow && s.boxShadow !== "none";
      const t = (el.getAttribute("aria-label") || el.innerText || el.getAttribute("name") || el.getAttribute("href") || "").trim().replace(/\s+/g, " ").slice(0, 40);
      return { key: `${el.tagName}|${t}|${Math.round(el.getBoundingClientRect().top + scrollY)}`, label: `${el.tagName.toLowerCase()}[${t}]`, ok: outline || shadow };
    });
    if (!r || seen.has(r.key)) continue;
    seen.add(r.key); reached++;
    if (!r.ok) missing.push(r.label);
  }
  return { reached, missing: missing.slice(0, 8), missingCount: missing.length };
}

export async function axeCheck(page) {
  try {
    const { violations } = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"]).analyze();
    return violations.map((v) => ({
      id: v.id, impact: v.impact, count: v.nodes.length,
      sample: v.nodes.slice(0, 3).map((n) => ({
        target: n.target.join(" ").slice(0, 90),
        data: n.any?.[0]?.data && v.id === "color-contrast" ? { fg: n.any[0].data.fgColor, bg: n.any[0].data.bgColor, ratio: n.any[0].data.contrastRatio, expected: n.any[0].data.expectedContrastRatio } : undefined,
      })),
    }));
  } catch (e) { return [{ id: "axe-error", impact: "n/a", count: 0, sample: [String(e).slice(0, 120)] }]; }
}
