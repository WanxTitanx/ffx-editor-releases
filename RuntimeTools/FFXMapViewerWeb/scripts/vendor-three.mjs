// vendor-three.mjs — copy the npm-installed three@0.165.0 build + examples/jsm into
// ./vendor/three so the app runs fully offline (no node_modules, no internet at runtime).
//
// Run: npm run vendor:three   (after `npm install three@0.165.0`)
//
// Source of truth for the version is node_modules/three (pinned in package.json).
// This script only COPIES files; it never mutates the immutable three source.
import { cp, mkdir, readFile, rm, access } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const src = join(root, "node_modules", "three");
const dest = join(root, "vendor", "three");

async function exists(p) {
  try { await access(p); return true; } catch { return false; }
}

async function main() {
  if (!(await exists(src))) {
    throw new Error(`three not installed at ${src}. Run: npm install three@0.165.0`);
  }
  const pkg = JSON.parse(await readFile(join(src, "package.json"), "utf8"));
  if (pkg.version !== "0.165.0") {
    throw new Error(`expected three@0.165.0, found ${pkg.version}`);
  }

  // Clean previous vendor copy for a deterministic result.
  if (await exists(dest)) await rm(dest, { recursive: true, force: true });
  await mkdir(dest, { recursive: true });

  // 1) the ESM build entry point -> vendor/three/three.module.js
  await cp(join(src, "build", "three.module.js"), join(dest, "three.module.js"));

  // 2) the whole addons tree -> vendor/three/jsm/
  await cp(join(src, "examples", "jsm"), join(dest, "jsm"), { recursive: true });

  // 3) license + version stamp so the vendored copy is self-describing.
  await cp(join(src, "LICENSE"), join(dest, "LICENSE"));

  console.log(`vendored three@${pkg.version} -> ${dest}`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
