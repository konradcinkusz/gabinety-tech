import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import {
  Alert,
  Badge,
  Button,
  Card,
  Field,
  LinkButton,
  Pagination,
  PriceTag,
  Select,
  Spinner,
  TextArea,
  TextInput,
} from "./index";

const html = (el: React.ReactElement) => renderToStaticMarkup(el);

describe("ui primitives render", () => {
  it("Button renders a typed button; loading sets aria-busy and disabled", () => {
    expect(html(<Button>x</Button>)).toContain('type="button"');
    const busy = html(<Button loading>x</Button>);
    expect(busy).toContain('aria-busy="true"');
    expect(busy).toContain("disabled");
  });

  it("LinkButton renders an anchor", () => {
    expect(html(<LinkButton href="/a">go</LinkButton>)).toMatch(/^<a [^>]*href="\/a"/);
  });

  it("Field binds label to control and wires hint and error", () => {
    const out = html(
      <Field label="Name" hint="a hint" error="bad">
        {(c) => <TextInput {...c} />}
      </Field>,
    );
    const forId = /<label[^>]* for="([^"]+)"/.exec(out)?.[1];
    expect(forId).toBeTruthy();
    expect(out).toContain(`id="${forId}"`);
    expect(out).toContain('aria-invalid="true"');
    const describedBy = /aria-describedby="([^"]+)"/.exec(out)?.[1] ?? "";
    const ids = describedBy.split(" ");
    expect(ids).toHaveLength(2);
    for (const id of ids) expect(out).toContain(`id="${id}"`);
  });

  it("Field without error has no aria-invalid", () => {
    const out = html(<Field label="Name">{(c) => <TextInput {...c} />}</Field>);
    expect(out).not.toContain("aria-invalid");
    expect(out).not.toContain("aria-describedby");
  });

  it("TextArea renders a counter when asked", () => {
    expect(html(<TextArea maxLength={10} showCount defaultValue="abc" />)).toContain("3 / 10");
  });

  it("Select renders options", () => {
    expect(html(<Select><option value="1">one</option></Select>)).toContain("<option");
  });

  it("Badge and Card render children", () => {
    expect(html(<Badge variant="success">ok</Badge>)).toContain("ok");
    expect(html(<Card>body</Card>)).toContain("body");
  });

  it("Alert maps role", () => {
    expect(html(<Alert variant="danger">x</Alert>)).toContain('role="alert"');
    expect(html(<Alert variant="info">x</Alert>)).toContain('role="status"');
    expect(html(<Alert>x</Alert>)).toContain('role="status"');
  });

  it("Spinner has status role and a label", () => {
    const out = html(<Spinner />);
    expect(out).toContain('role="status"');
    expect(out).toContain("sr-only");
  });

  it("Pagination is a labelled nav; missing targets are not links", () => {
    const out = html(<Pagination previous={null} next={{ href: "/p?c=2" }} />);
    expect(out).toMatch(/^<nav [^>]*aria-label="[^"]+"/);
    expect(out).toContain('aria-disabled="true"');
    expect(out).toContain('href="/p?c=2"');
    expect((out.match(/<a /g) ?? []).length).toBe(1);
  });

  it("PriceTag formats grosz", () => {
    expect(html(<PriceTag amount={25000} />)).toContain("250,00 zł");
  });
});
