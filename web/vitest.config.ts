import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// Default environment stays "node"; component tests opt in to jsdom per file
// with `// @vitest-environment jsdom`.
export default defineConfig({
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  test: { environment: "node", include: ["src/**/*.test.{ts,tsx}"] },
});
