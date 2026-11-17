// Waits until every service answers /health and the Directory lists the seeded practitioners. Used by globalSetup and by CI. Exit 1 on timeout.
const e = (n, d) => process.env[n] || d;
const urls = {
  devidentity: `${e("E2E_IDENTITY_URL", "http://localhost:5100")}/health`,
  directory: `${e("E2E_DIRECTORY_URL", "http://localhost:5101")}/health`,
  booking: `${e("E2E_BOOKING_URL", "http://localhost:5102")}/health`,
  reputation: `${e("E2E_REPUTATION_URL", "http://localhost:5103")}/health`,
  assistant: `${e("E2E_ASSISTANT_URL", "http://localhost:5104")}/health`,
  web: `${e("E2E_WEB_URL", "http://localhost:3000")}/api/health`,
};
const list = `${e("E2E_DIRECTORY_URL", "http://localhost:5101")}/api/v1/practitioners?limit=50`;
const want = Number(e("WANT_PRACTITIONERS", "6"));
const deadline = Date.now() + Number(e("E2E_WAIT_SECONDS", "240")) * 1000;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const status = async (u) => { try { return (await fetch(u, { signal: AbortSignal.timeout(5000) })).status; } catch { return 0; } };

async function waitForProfile() {
  for (const [name, u] of Object.entries(urls)) {
    while ((await status(u)) !== 200) {
      if (Date.now() > deadline) throw new Error(`${name} did not answer 200 on ${u}`);
      await sleep(1000);
    }
  }
  // seeded data: the seeder starts after the services are healthy; 429 from the Directory's per-address limit just means "ask again"
  for (;;) {
    try {
      const r = await fetch(list, { signal: AbortSignal.timeout(10000) });
      if (r.ok && ((await r.text()).match(/"slug":"demo-/g) ?? []).length >= want) return;
    } catch { /* retry */ }
    if (Date.now() > deadline) throw new Error(`fewer than ${want} seeded practitioners listed`);
    await sleep(2000);
  }
}

module.exports = { waitForProfile };

if (require.main === module) {
  waitForProfile().then(() => console.log("fake profile ready"), (err) => { console.error(String(err.message ?? err)); process.exit(1); });
}
