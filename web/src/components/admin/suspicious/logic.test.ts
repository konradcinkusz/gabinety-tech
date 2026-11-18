import { describe, expect, it } from "vitest";
import { PAGE_LIMIT } from "./api";
import { cursorOf } from "./server";
import { sp } from "./strings";

describe("cursorOf: Booking's opaque cursor passes through only in its own alphabet", () => {
  it.each([["abc_-1", "abc_-1"], ["ZGVtby1j.0a1b2c", "ZGVtby1j.0a1b2c"], ["a".repeat(512), "a".repeat(512)]])("%j is kept", (raw, out) => expect(cursorOf(raw)).toBe(out));
  it.each([["a b"], ["x&limit=9"], ["<script>"], [""], ["a".repeat(513)], [["x"]], [undefined], [42]])("%j is dropped", (raw) => expect(cursorOf(raw)).toBeUndefined());
});
describe("constants", () => {
  it("the page asks for the contract's default admin page size", () => expect(PAGE_LIMIT).toBe(20));
  it("the title is Polish and the placeholders are filled", () => {
    expect(sp("title")).toBe("Podejrzane pary");
    expect(sp("fact.bookings")).toBe("Opłacone rezerwacje");
  });
});
