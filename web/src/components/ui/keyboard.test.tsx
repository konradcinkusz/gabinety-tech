// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Button, LinkButton, Pagination, Select, TextArea, TextInput } from "./index";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: HTMLElement;
let root: Root;
beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
});
const render = (el: React.ReactElement) => act(() => root.render(el));

describe("keyboard operability", () => {
  it("Tab reaches each interactive primitive in DOM order", async () => {
    const user = userEvent.setup();
    render(
      <>
        <Button>b</Button>
        <LinkButton href="/x">l</LinkButton>
        <TextInput aria-label="i" />
        <TextArea aria-label="a" />
        <Select aria-label="s"><option>1</option></Select>
        <Pagination previous={{ href: "/p" }} next={{ onSelect: () => {} }} />
      </>,
    );
    const seen: string[] = [];
    for (let i = 0; i < 8; i++) {
      await user.tab();
      seen.push(document.activeElement?.tagName ?? "");
    }
    expect(seen.slice(0, 7)).toEqual(["BUTTON", "A", "INPUT", "TEXTAREA", "SELECT", "A", "BUTTON"]);
  });

  it("Enter and Space activate a Button", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(<Button onClick={onClick}>b</Button>);
    await user.tab();
    await user.keyboard("{Enter}");
    await user.keyboard(" ");
    expect(onClick).toHaveBeenCalledTimes(2);
  });

  it("disabled and loading Buttons do not fire", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    render(<><Button disabled onClick={onClick}>a</Button><Button loading onClick={onClick}>b</Button></>);
    await user.tab();
    await user.keyboard("{Enter} ");
    await user.click(host.querySelectorAll("button")[1]!);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("Enter activates a LinkButton", async () => {
    const user = userEvent.setup();
    const onClick = vi.fn((e: React.MouseEvent) => e.preventDefault());
    render(<LinkButton href="/x" onClick={onClick}>l</LinkButton>);
    await user.tab();
    await user.keyboard("{Enter}");
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("inputs take focus and typed text", async () => {
    const user = userEvent.setup();
    render(
      <>
        <TextInput aria-label="i" />
        <TextArea aria-label="a" maxLength={20} showCount />
        <Select aria-label="s"><option value="1">one</option><option value="2">two</option></Select>
      </>,
    );
    await user.tab();
    await user.keyboard("abc");
    expect((document.activeElement as HTMLInputElement).value).toBe("abc");
    await user.tab();
    await user.keyboard("hello");
    expect((document.activeElement as HTMLTextAreaElement).value).toBe("hello");
    expect(host.textContent).toContain("5 / 20");
    await user.tab();
    const select = document.activeElement as HTMLSelectElement;
    expect(select.tagName).toBe("SELECT");
    await user.selectOptions(select, "2");
    expect(select.value).toBe("2");
  });

  it("Pagination callback fires from the keyboard", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<Pagination previous={null} next={{ onSelect }} />);
    await user.tab();
    await user.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledTimes(1);
  });
});
