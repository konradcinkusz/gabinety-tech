import { spawn } from "node:child_process";
import { resolve } from "node:path";
import { internalKey, urls } from "./env";
import { fetchRetrying } from "./http";

/**
 * Reset and reseed of the demo data (CONTRACTS 10.4). The reset removes every `demo-` row of Directory, Booking and Reputation, the seeder writes the
 * dataset again (SPEC 7). It goes through the seeder's own `--reset` (the same code the AppHost's `seeder-reset` resource runs), so the journeys see
 * exactly the dataset the seeder defines. A run takes 1 to 6 minutes (the Directory rate limit and the wait for Booking's auto-complete job; measured 5.8 min on a slow container, PR #93 saw ~80 s on a limit hit): do it once per journey, in
 * `afterAll`, not per test.
 */
const repoRoot = resolve(__dirname, "../../..");

function runSeeder(args: string[], timeoutMs: number): Promise<void> {
  return new Promise((ok, fail) => {
    const p = spawn(
      "dotnet",
      ["run", "--no-launch-profile", "--project", resolve(repoRoot, "tools/Gabinety.DemoSeeder"), "--", ...args],
      {
        cwd: repoRoot,
        detached: true, // its own process group, so a timeout can stop `dotnet run` and the seeder it started
        stdio: ["ignore", "ignore", "pipe"], // stderr only; stdout is the seeder's log, which names demo data only but is noise
        env: {
          ...process.env,
          GABINETY_FAKE: "1",
          Environment__Kind: "Development",
          Backend__Directory: urls.directory, Backend__Booking: urls.booking, Backend__Reputation: urls.reputation,
          Internal__Keys__Directory: internalKey, Internal__Keys__Booking: internalKey, Internal__Keys__Reputation: internalKey,
          Identity__Authority: urls.devidentity,
        },
      },
    );
    let err = "";
    p.stderr.on("data", (d) => (err = (err + d).slice(-2000)));
    const timer = setTimeout(() => { try { process.kill(-p.pid!, "SIGKILL"); } catch { /* already gone */ } fail(new Error(`seeder ${args.join(" ")} timed out after ${timeoutMs} ms`)); }, timeoutMs);
    p.on("exit", (code) => { clearTimeout(timer); code === 0 ? ok() : fail(new Error(`seeder ${args.join(" ")} exited ${code}: ${err.trim()}`)); });
    p.on("error", (e) => { clearTimeout(timer); fail(e); });
  });
}

/** Removes the `demo-` data and seeds it again. */
export const resetAndReseed = (timeoutMs = 600_000) => runSeeder(["--reset"], timeoutMs);
/** Idempotent top-up: a no-op when the data is there. */
export const seed = (timeoutMs = 600_000) => runSeeder([], timeoutMs);

/** Number of `demo-` practitioners the Directory lists (public read; rate-limit aware). */
export async function countListedDemoPractitioners(): Promise<number> {
  const res = await fetchRetrying(`${urls.directory}/api/v1/practitioners?limit=50`);
  if (!res.ok) return 0;
  return ((await res.text()).match(/"slug":"demo-/g) ?? []).length;
}

export { internalKey };
