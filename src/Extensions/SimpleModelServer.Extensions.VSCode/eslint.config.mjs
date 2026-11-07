// ESLint flat config: @eslint/js recommended plus typescript-eslint recommended.
// One linter for this package (see the lint skill). Generated output is not linted.
import js from "@eslint/js";
import tseslint from "typescript-eslint";

export default tseslint.config(
  {
    ignores: ["out/**", "dist/**", "node_modules/**", "*.vsix"],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    // Build and check scripts run under Node, so declare the Node globals they use.
    files: ["scripts/**/*.mjs"],
    languageOptions: {
      globals: { console: "readonly", process: "readonly" },
    },
  },
);
