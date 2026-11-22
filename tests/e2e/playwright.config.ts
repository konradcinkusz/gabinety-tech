import { defineConfig, devices } from "@playwright/test";
import { urls } from "./harness/env";

// The harness owns this file (T-08.03a); journeys only add `journeys/jN-*.spec.ts` files.
// Nothing is started here: the fake profile is already running (see README.md). `globalSetup` waits for it and for the seeded data.
export default defineConfig({
  testDir: ".",
  testMatch: ["journeys/**/*.spec.ts", "harness/**/*.spec.ts"],
  globalSetup: "./harness/global-setup.ts",
  // Journeys share one in-memory system (the clock offset, the demo data, the Directory's 60/min per address limit): one at a time.
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  forbidOnly: !!process.env.CI,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: urls.web,
    locale: "pl-PL",
    timezoneId: "Europe/Warsaw",
    trace: "retain-on-failure", // traces on failure only
    screenshot: "only-on-failure",
    // CI and the sandbox ship Chromium at a fixed path; locally Playwright finds its own.
    launchOptions: process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH, args: ["--no-sandbox"] } : {},
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
