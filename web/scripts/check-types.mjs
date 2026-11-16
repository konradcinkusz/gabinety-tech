// Fails when the committed generated types differ from what the services publish now.
//   node scripts/check-types.mjs [--allow-missing] [--sources <json>] [--out <dir>]
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs, render } from "./gen-types.mjs";

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const { files } = await render(args);
  const tmp = await mkdtemp(path.join(tmpdir(), "gt-check-types-"));
  const bad = [];
  try {
    for (const [file, text] of files) {
      await writeFile(path.join(tmp, file), text); // regenerated copy, kept for inspection while running
      let committed = null;
      try {
        committed = await readFile(path.join(args.out, file), "utf8");
      } catch {
        bad.push(`${file}: committed file is missing`);
        continue;
      }
      if (Buffer.compare(Buffer.from(committed), Buffer.from(text)) !== 0) bad.push(`${file}: differs from the regenerated output`);
    }
  } finally {
    await rm(tmp, { recursive: true, force: true });
  }
  if (bad.length) {
    console.log(bad.join("\n"));
    console.log("Run `pnpm gen:types` and commit the result.");
    process.exit(1);
  }
  console.log(`generated types are up to date (${files.size} file(s))`);
}

main().catch((e) => {
  console.error(e.message);
  process.exit(1);
});
