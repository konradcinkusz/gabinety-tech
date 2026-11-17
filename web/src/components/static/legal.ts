import { readFileSync } from "node:fs";
import { join } from "node:path";
import { createElement, Fragment, type ReactNode } from "react";

export type LegalName = "regulamin" | "polityka-prywatnosci";
export type LegalDoc = { version: string; date: string; draft: boolean; body: string };

/** Splits the `---` front matter (version, date, draft) from the body. The owner's text is data; a file without version and date is refused. */
export function parseLegal(source: string): LegalDoc {
  const m = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?([\s\S]*)$/.exec(source);
  if (!m) throw new Error("legal_front_matter_missing");
  const meta: Record<string, string> = {};
  for (const line of m[1].split(/\r?\n/)) {
    const kv = /^([a-z]+):\s*(.*)$/.exec(line.trim());
    if (kv) meta[kv[1]] = kv[2].trim();
  }
  if (!meta["version"] || !/^\d{4}-\d{2}-\d{2}$/.test(meta["date"] ?? "")) throw new Error("legal_version_or_date_missing");
  return { version: meta["version"], date: meta["date"], draft: meta["draft"] === "true", body: m[2] };
}

/** Read from web/content/legal/ at the process working directory (the web app's root, in tests, in `next build` and in the standalone server). */
export function loadLegal(name: LegalName): LegalDoc {
  return parseLegal(readFileSync(join(process.cwd(), "content", "legal", `${name}.md`), "utf8"));
}

/**
 * A minimal renderer for the owner's text: headings, paragraphs, bulleted and numbered lists. Everything becomes React text, so markup in the
 * file is escaped (no dangerouslySetInnerHTML). The file's `#` becomes h2, because the page itself owns the single h1.
 */
export function renderBlocks(body: string): ReactNode[] {
  const out: ReactNode[] = [];
  const lines = body.split(/\r?\n/);
  let para: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;
  const key = () => out.length;
  const flushPara = () => {
    if (para.length) out.push(createElement("p", { key: key() }, para.join(" ")));
    para = [];
  };
  const flushList = () => {
    if (list) out.push(createElement(list.ordered ? "ol" : "ul", { key: key() }, list.items.map((t, i) => createElement("li", { key: i }, t))));
    list = null;
  };
  for (const raw of lines) {
    const line = raw.trim();
    const heading = /^(#{1,3})\s+(.+)$/.exec(line);
    const bullet = /^[-*]\s+(.+)$/.exec(line);
    const numbered = /^\d+[.)]\s+(.+)$/.exec(line);
    if (heading) {
      flushPara();
      flushList();
      out.push(createElement(`h${heading[1].length + 1}`, { key: key() }, heading[2]));
    } else if (bullet || numbered) {
      flushPara();
      const ordered = !bullet;
      if (list && list.ordered !== ordered) flushList();
      list ??= { ordered, items: [] };
      list.items.push((bullet ?? numbered)![1]);
    } else if (line === "") {
      flushPara();
      flushList();
    } else {
      flushList();
      para.push(line);
    }
  }
  flushPara();
  flushList();
  return out.length ? out : [createElement(Fragment, { key: 0 })];
}
