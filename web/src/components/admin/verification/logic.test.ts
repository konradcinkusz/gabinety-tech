import { describe, expect, it } from "vitest";
import { allowedActions, reasonProblem } from "./api";

describe("allowedActions: the API's status decides what is offered", () => {
  it.each([
    ["Submitted", ["approve", "reject"]],
    ["Approved", ["suspend"]],
    ["Suspended", ["reinstate"]],
    ["Draft", []],
    ["Rejected", []],
  ] as const)("%s", (status, expected) => expect(allowedActions(status)).toEqual(expected));
});

describe("reasonProblem: 10 to 500 characters after trimming (CONTRACTS 3.4)", () => {
  it.each([
    ["", true],
    ["         x", true],
    ["a".repeat(9), true],
    ["a".repeat(10), false],
    ["  " + "a".repeat(10) + "  ", false],
    ["a".repeat(500), false],
    ["a".repeat(501), true],
  ])("%j -> problem %s", (text, problem) => expect(reasonProblem(text) !== null).toBe(problem));
});
