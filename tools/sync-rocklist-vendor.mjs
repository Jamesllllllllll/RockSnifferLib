#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import { access, cp, mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, "..");
const managedEntries = [
  "Cache",
  "Config",
  "Events",
  "Library",
  "Logging",
  "RSHelpers",
  "Sniffing",
  "SysHelpers",
  "CHANGELOG.md",
  "LICENSE",
  "README.md",
  "RockSnifferLib.csproj",
  "RockSnifferLib.sln",
];

const argumentsList = process.argv.slice(2);
const checkOnly = argumentsList[0] === "--check";
const rockListArgument = checkOnly ? argumentsList[1] : argumentsList[0];

if (!rockListArgument || argumentsList.length !== (checkOnly ? 2 : 1)) {
  console.error(
    "Usage: node tools/sync-rocklist-vendor.mjs [--check] /path/to/rocklist"
  );
  process.exit(1);
}

const rockListRoot = path.resolve(rockListArgument);
const vendorRoot = path.join(
  rockListRoot,
  "packages",
  "rocksmith-native-sidecar",
  "vendor",
  "rocksniffer",
  "RockSnifferLib"
);
const sourceRecordPath = path.join(
  rockListRoot,
  "packages",
  "rocksmith-native-sidecar",
  "vendor",
  "rocksniffer",
  "ROCKSNIFFERLIB_SOURCE.md"
);

await access(path.join(rockListRoot, "package.json"));
await access(vendorRoot);

const status = execFileSync("git", ["status", "--porcelain"], {
  cwd: repositoryRoot,
  encoding: "utf8",
}).trim();
if (status.length > 0) {
  throw new Error(
    "RockSnifferLib has uncommitted changes. Commit them before syncing a reproducible vendor snapshot."
  );
}

const commit = execFileSync("git", ["rev-parse", "HEAD"], {
  cwd: repositoryRoot,
  encoding: "utf8",
}).trim();

async function collectFiles(root, relativeEntry) {
  const absoluteEntry = path.join(root, relativeEntry);
  const entries = await readdir(absoluteEntry, { withFileTypes: true }).catch(
    () => null
  );
  if (!entries) {
    return [relativeEntry];
  }

  const files = [];
  for (const entry of entries.sort((left, right) =>
    left.name.localeCompare(right.name)
  )) {
    const child = path.join(relativeEntry, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await collectFiles(root, child)));
    } else if (entry.isFile()) {
      files.push(child);
    }
  }
  return files;
}

async function fileDigest(filePath) {
  return createHash("sha256").update(await readFile(filePath)).digest("hex");
}

async function compareEntry(relativeEntry) {
  const sourceFiles = await collectFiles(repositoryRoot, relativeEntry);
  const vendorFiles = await collectFiles(vendorRoot, relativeEntry);
  if (sourceFiles.join("\n") !== vendorFiles.join("\n")) {
    return false;
  }

  for (const relativeFile of sourceFiles) {
    const sourceDigest = await fileDigest(
      path.join(repositoryRoot, relativeFile)
    );
    const vendorDigest = await fileDigest(path.join(vendorRoot, relativeFile));
    if (sourceDigest !== vendorDigest) {
      return false;
    }
  }
  return true;
}

if (checkOnly) {
  const mismatches = [];
  for (const relativeEntry of managedEntries) {
    if (!(await compareEntry(relativeEntry))) {
      mismatches.push(relativeEntry);
    }
  }

  if (mismatches.length > 0) {
    console.error(`RockList vendor differs: ${mismatches.join(", ")}`);
    process.exit(1);
  }

  console.log(`RockList vendor matches RockSnifferLib commit ${commit}.`);
  process.exit(0);
}

for (const relativeEntry of managedEntries) {
  const sourcePath = path.join(repositoryRoot, relativeEntry);
  const destinationPath = path.join(vendorRoot, relativeEntry);
  await rm(destinationPath, { force: true, recursive: true });
  await mkdir(path.dirname(destinationPath), { recursive: true });
  await cp(sourcePath, destinationPath, { recursive: true });
}

const sourceRecord = `# RockSnifferLib source

- Repository: https://github.com/Jamesllllllllll/RockSnifferLib
- Commit: \`${commit}\`
- License: MIT; see \`RockSnifferLib/LICENSE\`

This directory is a generated vendor snapshot. Make reusable library changes
in the public RockSnifferLib repository, then run its sync tool to update this
copy.
`;
await writeFile(sourceRecordPath, sourceRecord, "utf8");

console.log(`Synced RockSnifferLib commit ${commit} into ${vendorRoot}.`);
