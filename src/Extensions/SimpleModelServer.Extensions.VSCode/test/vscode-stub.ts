// Stand-in for the `vscode` module, which only exists inside the VS Code extension host.
// Unit tests import code that references `vscode` but never call into it, so an empty
// module is enough. vitest.config.ts aliases `vscode` to this file.
export {};
