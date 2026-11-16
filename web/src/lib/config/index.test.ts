import { describe, expect, it } from "vitest";
import { readConfig } from "./index";

describe("readConfig", () => {
  it("splits candidate lists and trims", () => {
    const c = readConfig({ Backend__Directory: " http://a:1 , http://b:2 ", Site__BaseUrl: "https://x" });
    expect(c.backends.directory).toEqual(["http://a:1", "http://b:2"]);
    expect(c.siteBaseUrl).toBe("https://x");
  });
  it("gives empty lists, not undefined, for unset services", () => {
    expect(readConfig({}).backends.booking).toEqual([]);
  });
  it("reads the environment at call time", () => {
    const env: Record<string, string> = { Backend__Auth: "http://one" };
    expect(readConfig(env).backends.auth).toEqual(["http://one"]);
    env["Backend__Auth"] = "http://two";
    expect(readConfig(env).backends.auth).toEqual(["http://two"]);
  });
});

describe("readConfig identity", () => {
  it("reads optional issuer, audience and public url", () => {
    const c = readConfig({ Identity__Issuer: "i", Identity__Audience: "a", Identity__PublicUrl: "https://id" });
    expect(c.identity).toEqual({ issuer: "i", audience: "a", publicUrl: "https://id" });
    expect(readConfig({}).identity).toEqual({ issuer: undefined, audience: undefined, publicUrl: undefined });
  });
});
