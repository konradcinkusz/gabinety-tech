// Waits until every service answers /health, the Directory lists the seeded practitioners AND the demo seeder has finished. Used by globalSetup. Exit 1 on timeout.
const { execFile } = require("node:child_process");
const e = (n, d) => process.env[n] || d;
const webUrl = (process.env.E2E_WEB_URL || process.env.BASE_URL || "http://localhost:3000").replace(/\/$/, "");
const urls = {
  devidentity: `${e("E2E_IDENTITY_URL", "http://localhost:5100")}/health`,
  directory: `${e("E2E_DIRECTORY_URL", "http://localhost:5101")}/health`,
  booking: `${e("E2E_BOOKING_URL", "http://localhost:5102")}/health`,
  reputation: `${e("E2E_REPUTATION_URL", "http://localhost:5103")}/health`,
  assistant: `${e("E2E_ASSISTANT_URL", "http://localhost:5104")}/health`,
  web: `${webUrl}/health`,
};
const list = `${e("E2E_DIRECTORY_URL", "http://localhost:5101")}/api/v1/practitioners?limit=50`;
const want = Number(e("WANT_PRACTITIONERS", "6"));
const deadline = Date.now() + Number(e("E2E_WAIT_SECONDS", "240")) * 1000;
const booking = e("E2E_BOOKING_URL", "http://localhost:5102");
const reputation = e("E2E_REPUTATION_URL", "http://localhost:5103");
const internalKey = e("E2E_INTERNAL_KEY", "demo-development-internal-key-not-a-secret");
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const status = async (u) => { try { return (await fetch(u, { signal: AbortSignal.timeout(5000) })).status; } catch { return 0; } };

/**
 * True when `ps -eo args` output contains a running demo seeder: the AppHost's resource (executable or `dotnet Gabinety.DemoSeeder.dll`) or a
 * `dotnet run --project .../Gabinety.DemoSeeder`. Matching the executable, not any line that mentions the name, keeps the AppHost and this
 * process (whose arguments never name the seeder as the program) from counting.
 */
function seederRunning(psArgs) {
  const direct = /^\s*(\S*[\\/])?(dotnet\s+(\S+\s+)*\S*[\\/])?Gabinety\.DemoSeeder(\.dll|\.exe)?(\s|$)/; // the executable or `dotnet <path>/Gabinety.DemoSeeder.dll`
  const viaRun = /^\s*(\S*[\\/])?dotnet\s+run\s.*--project\s+\S*Gabinety\.DemoSeeder(\s|$)/;
  return psArgs.split("\n").some((l) => direct.test(l) || viaRun.test(l));
}

/** `ps` output, or null where it cannot be had (Windows, a minimal image): the caller then relies on the clock alone. */
const processList = () => new Promise((ok) => execFile("ps", ["-eo", "args"], { timeout: 5000 }, (err, out) => ok(err ? null : String(out))));

const clockOffset = async (base) => {
  try {
    const r = await fetch(`${base}/internal/v1/test/clock`, { headers: { "X-Internal-Key": internalKey }, signal: AbortSignal.timeout(5000) });
    return r.ok ? (await r.json()).offsetMinutes : null;
  } catch { return null; }
};

/**
 * Waits until the demo seeder has FINISHED. The seeder moves the global test clock while it seeds Booking (CONTRACTS 10.4) and puts it back in a
 * `finally`, so a test that reads or moves the clock meanwhile fails or corrupts the seeding. The AppHost exposes no "seeder done" state over HTTP and
 * the seeder writes no marker, and "the clock is 0" alone is also true before Booking's first move and between its moves; so the signal is:
 * (1) no seeder process runs (the seeder only targets this machine, SeedGuard) and (2) both clocks read the baseline 0, both held for `stableMs`
 * so a process that has not started yet or a gap between two steps does not pass. Where `ps` is unavailable only (2) is used, with a longer hold.
 * Call it after the practitioners are listed (the Directory step is the seeder's first, so it is running or done by then).
 */
async function waitForSeeder({ timeoutMs, stableMs = 10000, pollMs = 1000, now = Date.now, list = processList, offset = clockOffset } = {}) {
  const limit = now() + (timeoutMs ?? Number(e("E2E_SEEDER_WAIT_SECONDS", "480")) * 1000);
  let calm = null, last = "no reading yet";
  for (;;) {
    const ps = await list();
    const running = ps === null ? null : seederRunning(ps);
    const [b, r] = [await offset(booking), await offset(reputation)];
    last = `seeder process ${running === null ? "unknown" : running ? "running" : "gone"}, booking clock ${b}, reputation clock ${r}`;
    if (running !== true && b === 0 && r === 0) {
      calm ??= now();
      if (now() - calm >= (running === null ? stableMs * 3 : stableMs)) return;
    } else calm = null;
    if (now() > limit) throw new Error(`the demo seeder did not finish in time (${last}); see apphost.log / the AppHost dashboard, or set E2E_SEEDER_WAIT_SECONDS`);
    await sleep(pollMs);
  }
}

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
      if (r.ok && ((await r.text()).match(/"slug":"demo-/g) ?? []).length >= want) break;
    } catch { /* retry */ }
    if (Date.now() > deadline) throw new Error(`fewer than ${want} seeded practitioners listed`);
    await sleep(2000);
  }
  if (e("E2E_WAIT_FOR_SEEDER", "1") !== "0") await waitForSeeder(); // E2E_WAIT_FOR_SEEDER=0: a profile seeded earlier and running no seeder
}

module.exports = { waitForProfile, waitForSeeder, seederRunning };

if (require.main === module) {
  waitForProfile().then(() => console.log("fake profile ready"), (err) => { console.error(String(err.message ?? err)); process.exit(1); });
}
