#!/usr/bin/env node
// Prepares the inputs of scripts/measure-performance.sh from the running, seeded system: the filtered-list URLs, the slots URLs, the web page URLs and the POST /bookings
// requests (one distinct, non-overlapping slot each; signed in as the demo clients, who need a booking-creation limit of their own: see RateLimit__PerMinute in perf-stack.sh).
//   node scripts/perf/prepare.mjs OUT_DIR [BOOKINGS=120]
import { writeFileSync, mkdirSync } from "node:fs";
import { randomUUID } from "node:crypto";

const [outDir, bookingsArg] = process.argv.slice(2);
if (!outDir) { console.error("usage: prepare.mjs OUT_DIR [BOOKINGS]"); process.exit(2); }
mkdirSync(outDir, { recursive: true });
const DIR = process.env.DIRECTORY_URL ?? "http://localhost:5101", BOOK = process.env.BOOKING_URL ?? "http://localhost:5102", ID = process.env.IDENTITY_URL ?? "http://localhost:5100", WEB = process.env.WEB_URL ?? "http://localhost:3000";
const PASSWORD = "Demo-Dev-Passw0rd!"; // the fixed development password of tools/Gabinety.DevIdentity; protects nothing
const wanted = +(bookingsArg ?? 120);
const get = async (url, token) => { const r = await fetch(url, { headers: token ? { authorization: `Bearer ${token}` } : {} }); if (!r.ok) throw new Error(`${url} answered ${r.status}`); return r.json(); };
const login = async (name) => { const r = await fetch(`${ID}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email: `${name}@demo.example`, password: PASSWORD }) }); if (!r.ok) throw new Error(`login ${name}: ${r.status}`); return (await r.json()).accessToken; };

const tax = await get(`${DIR}/api/v1/taxonomy`);
const problems = tax.problems.map((p) => p.slug), techs = tax.techs.map((t) => t.slug);

// filtered list, 12 items: every combination of a problem category and a duration, plus problem and tech, rotated so no single query is measured
const lists = [];
for (const p of problems) for (const d of [30, 60]) lists.push(`${DIR}/api/v1/practitioners?problem=${p}&duration=${d}&limit=12`);
for (const p of problems.slice(0, 4)) for (const t of techs.slice(0, 4)) lists.push(`${DIR}/api/v1/practitioners?problem=${p}&tech=${t}&limit=12`);
const full = [];
for (const u of lists) { const j = await get(u); if (j.items.length === 12) full.push(u); }
if (full.length === 0) throw new Error("no filtered query returns 12 items: is the scaled dataset seeded (--factor 200)?");

// the practitioners with availability: the six Approved demo practitioners of the catalog (the scaled ones have none, which does not change a slots query)
const base = ["demo-anna-demowska", "demo-bartosz-testowy", "demo-celina-przykladowska", "demo-dariusz-fikcyjny", "demo-ewa-wzorcowa", "demo-filip-syntetyczny"];
const today = new Date(); const day = (n) => new Date(today.getTime() + n * 864e5).toISOString().slice(0, 10);
const slotUrls = [], pools = [];
for (const slug of base) {
  const prof = await get(`${DIR}/api/v1/practitioners/${slug}`);
  for (const o of prof.offerings) {
    const url = `${BOOK}/api/v1/practitioners/${prof.id}/slots?offeringId=${o.id}&from=${day(0)}&to=${day(13)}`;
    const s = await get(url);
    slotUrls.push(url);
    if (s.slots.length) pools.push({ slug, offeringId: o.id, minutes: o.durationMinutes, slots: s.slots.map((x) => x.startUtc) });
  }
}

// POST /bookings: slots that do not overlap each other on one practitioner's calendar, so no request of this run collides with another
const clients = ["demo-client", "demo-client-2", "demo-client-3", "demo-client-4"];
const tokens = []; for (const c of clients) tokens.push(await login(c));
const requests = [], usedBy = {};
for (let round = 0; requests.length < wanted; round++) {
  let added = false;
  for (const pool of pools) {
    if (requests.length >= wanted) break;
    const used = (usedBy[pool.slug] ??= []); pool.next ??= 0;
    let start = null;
    while (pool.next < pool.slots.length) { // the next slot that overlaps nothing this run already asked for on this practitioner's calendar (with a minute to spare)
      const t = Date.parse(pool.slots[pool.next++]), end = t + pool.minutes * 60000;
      if (!used.some(([a, b]) => t < b + 60000 && a < end + 60000)) { start = pool.slots[pool.next - 1]; used.push([t, end]); break; }
    }
    if (start === null) continue;
    added = true;
    const token = tokens[requests.length % tokens.length];
    requests.push({ method: "POST", url: `${BOOK}/api/v1/bookings`,
      headers: { authorization: `Bearer ${token}`, "content-type": "application/json", "idempotency-key": randomUUID() },
      body: { offeringId: pool.offeringId, startUtc: start, brief: { text: `[demo-perf-${requests.length}] Pomiar wydajnosci, dane syntetyczne.`, problemSlug: null, aiAssisted: false } } });
  }
  if (!added) break;
}
writeFileSync(`${outDir}/list-urls.json`, JSON.stringify(full));
writeFileSync(`${outDir}/slots-urls.json`, JSON.stringify(slotUrls));
writeFileSync(`${outDir}/booking-requests.json`, JSON.stringify(requests));
const slug0 = base[5], problem0 = problems[0];
writeFileSync(`${outDir}/prep.json`, JSON.stringify({ listQueries: full.length, slotsUrls: slotUrls.length, bookingRequests: requests.length, bookingsWanted: wanted,
  web: { category: `${WEB}/programisci/${problem0}`, list: `${WEB}/programisci`, profile: `${WEB}/gabinet/${slug0}`, terms: `${WEB}/regulamin`, home: `${WEB}/` } }, null, 2));
console.log(`prepared: ${full.length} list queries, ${slotUrls.length} slots urls, ${requests.length} of ${wanted} booking requests`);
