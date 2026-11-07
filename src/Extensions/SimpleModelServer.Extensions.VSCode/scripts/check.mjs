// The single JS check for this package. CI and the full gate both run `npm run check`.
// Each step runs in its own process and the first failure stops the run, so the first
// error is the one reported. Plain Node, so it behaves the same on Windows and POSIX.
import { spawnSync } from "node:child_process";

const steps = ["typecheck", "lint", "test:unit"];

for (const step of steps) {
  console.log(`\n==> npm run ${step}`);
  // One command string, not an args array: with shell: true, an array is concatenated unescaped.
  const result = spawnSync(`npm run ${step}`, { stdio: "inherit", shell: true });
  if (result.status !== 0) {
    console.error(`\ncheck failed at: ${step}`);
    process.exit(result.status ?? 1);
  }
}

console.log("\ncheck passed");
