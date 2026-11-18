import { describe, expect, it } from "vitest";
import { clientAddressOf } from "./clientAddress";

const h = (o: Record<string, string>) => new Headers(o);

describe("clientAddressOf: the visitor's address as the platform reports it", () => {
  it("prefers the platform's own header", () => {
    expect(clientAddressOf(h({ "fly-client-ip": "203.0.113.5", "x-forwarded-for": "198.51.100.9" }))).toBe("203.0.113.5");
  });
  it("otherwise takes the LAST entry of X-Forwarded-For: the one the nearest proxy appended, not what the visitor wrote first", () => {
    expect(clientAddressOf(h({ "x-forwarded-for": "6.6.6.6, 203.0.113.7" }))).toBe("203.0.113.7");
    expect(clientAddressOf(h({ "x-forwarded-for": "203.0.113.7" }))).toBe("203.0.113.7");
  });
  it("accepts IPv6", () => expect(clientAddressOf(h({ "fly-client-ip": "2001:db8::1" }))).toBe("2001:db8::1"));
  it.each([["not-an-ip"], ["203.0.113.5:8080"], ["203.0.113.5, evil"], ["<script>"], ["999.1.1.1"], [""], ["a".repeat(200)]])("%j is not an address", (v) => {
    expect(clientAddressOf(h({ "fly-client-ip": v }))).toBeUndefined();
    expect(clientAddressOf(h({ "x-forwarded-for": v }))).toBeUndefined();
  });
  it("an invalid platform header does not hide a valid X-Forwarded-For", () => {
    expect(clientAddressOf(h({ "fly-client-ip": "junk", "x-forwarded-for": "203.0.113.8" }))).toBe("203.0.113.8");
  });
  it("no header, no address", () => expect(clientAddressOf(h({}))).toBeUndefined());
});
