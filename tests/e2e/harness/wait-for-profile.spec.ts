import { expect, test } from "@playwright/test";
import { seederRunning, waitForSeeder } from "./wait-for-profile.cjs";

// The seeder-wait logic of globalSetup (no service needed): the process matcher and the "finished" decision, with a fake clock, ps and offsets.
test.describe("wait for the seeder", () => {
  test("seederRunning matches the seeder, not the AppHost or Playwright", () => {
    expect(seederRunning("/repo/bin/Gabinety.DemoSeeder\n")).toBe(true);
    expect(seederRunning("dotnet /repo/tools/Gabinety.DemoSeeder/bin/Debug/net9.0/Gabinety.DemoSeeder.dll --Backend:Directory=x\n")).toBe(true);
    expect(seederRunning("dotnet run --no-launch-profile --project /repo/tools/Gabinety.DemoSeeder -- --reset\n")).toBe(true);
    expect(seederRunning("dotnet run --project src/Gabinety.AppHost --launch-profile fake\nnode playwright test --grep Gabinety.DemoSeeder\n/repo/bin/Gabinety.Booking\n")).toBe(false);
    expect(seederRunning("")).toBe(false);
  });

  function sim(script: { running: boolean; offset: number }[]) {
    let t = 0, i = 0;
    const step = () => script[Math.min(i, script.length - 1)];
    return {
      now: () => t,
      pollMs: 1000,
      stableMs: 5000,
      list: async () => (step().running ? "/x/Gabinety.DemoSeeder\n" : "ps\n"),
      offset: async () => step().offset,
      tick: () => { t += 1000; i++; },
    };
  }
  const run = async (s: ReturnType<typeof sim>, timeoutMs: number) => {
    // advance the fake time on every poll: pollMs sleeps for real, so keep it tiny and tick on each list() call
    const list = s.list;
    return waitForSeeder({ timeoutMs, stableMs: s.stableMs, pollMs: 1, now: s.now, offset: s.offset, list: async () => { const r = await list(); s.tick(); return r; } });
  };

  test("waits while the seeder runs or moves the clock, passes once calm and stable", async () => {
    const s = sim([...Array(3).fill({ running: true, offset: 0 }), ...Array(2).fill({ running: true, offset: 8059 }), { running: false, offset: 8059 }, { running: false, offset: 0 }]);
    let polls = 0;
    const list = s.list;
    s.list = async () => { polls++; return list(); };
    await run(s, 60_000);
    expect(polls).toBeGreaterThanOrEqual(6 + 5); // 6 busy readings, then the 5 s hold
  });

  test("a gap between two steps (clock 0, process gone for less than the hold) does not pass", async () => {
    const s = sim([{ running: false, offset: 0 }, { running: false, offset: 0 }, { running: true, offset: 5 }, ...Array(20).fill({ running: true, offset: 5 })]);
    await expect(run(s, 15_000)).rejects.toThrow(/seeder did not finish/);
  });

  test("times out with a clear message", async () => {
    const s = sim([{ running: true, offset: 0 }]);
    await expect(run(s, 10_000)).rejects.toThrow(/did not finish in time \(seeder process running, booking clock 0/);
  });
});
