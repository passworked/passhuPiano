// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
import fs from 'node:fs';
import path from 'node:path';
import { root, read, walk, auditSnapshot, write } from './release.mjs';

const version = read('VERSION').trim();
const output = `Releases/${version}/source/musicMidiPlayer`;
auditSnapshot(output);
const files = walk(output);
const metadata = new Map(), external = new Set(), errors = [];
for (const p of files.filter(p => p.endsWith('.meta'))) {
  const id = fs.readFileSync(path.join(root, p), 'utf8').match(/^guid: ([a-f0-9]{32})$/m)?.[1];
  if (!id) { errors.push(`Missing GUID: ${p}`); continue; }
  if (metadata.has(id)) errors.push(`Duplicate GUID ${id}: ${p} and ${metadata.get(id)}`);
  metadata.set(id, p.slice(0, -5));
}
// Package references are restored externally; a missing *local* resource must
// not be mistaken for a declared SDK dependency.
for (const p of [...walk('Packages'), ...walk('Library/PackageCache')].filter(p => p.endsWith('.meta'))) {
  const id = read(p).match(/^guid: ([a-f0-9]{32})$/m)?.[1];
  if (id) external.add(id);
}
let referenceCount = 0;
const anchors = new Map();
for (const p of files.filter(p => /\.(?:unity|prefab|asset)$/.test(p))) {
  const text = fs.readFileSync(path.join(root, p), 'utf8');
  if (!text.startsWith('%YAML')) continue;
  for (const m of text.matchAll(/\{fileID: (-?\d+), guid: ([a-f0-9]{32})(?:, type: \d+)?\}/g)) {
    referenceCount++;
    const [, fileId, guid] = m;
    if (fileId === '0' || guid.startsWith('0000000000000000')) continue;
    const target = metadata.get(guid);
    if (!target) {
      if (!external.has(guid)) errors.push(`Unresolved GUID ${guid}: ${p}`);
      continue;
    }
    if (!target.endsWith('.asset')) continue;
    if (!anchors.has(target)) {
      const targetText = fs.readFileSync(path.join(root, target), 'utf8');
      anchors.set(target, targetText.startsWith('%YAML') ? new Set([...targetText.matchAll(/^--- !u!\d+ &(-?\d+)/gm)].map(x => x[1])) : null);
    }
    const ids = anchors.get(target);
    if (ids && !ids.has(fileId)) errors.push(`Invalid fileID ${fileId} in ${target}, referenced by ${p}`);
  }
}
const result = { version, files: files.length, guids: metadata.size, references: referenceCount, errors: [...new Set(errors)] };
write(`Logs/Release/source-reference-check.json`, result);
if (result.errors.length) { console.error(JSON.stringify({ ...result, errors: result.errors.slice(0, 15) })); process.exitCode = 1; }
else console.log(JSON.stringify({ ...result, status: 'passed' }));
