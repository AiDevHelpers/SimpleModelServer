import { defineConfig } from "vitest/config";
import { fileURLToPath } from "node:url";

// Unit tests run outside VS Code, so `vscode` resolves to a stub instead of the extension host API.
export default defineConfig({
  resolve: {
    alias: {
      vscode: fileURLToPath(new URL("./test/vscode-stub.ts", import.meta.url)),
    },
  },
  test: {
    include: ["test/**/*.test.ts"],
  },
});
