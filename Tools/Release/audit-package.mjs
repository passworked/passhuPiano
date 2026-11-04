// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { root, read, write } from './release.mjs';

const version = read('VERSION').trim();
const policy = JSON.parse(read('Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json'));
const manifest = JSON.parse(read(`Releases/${version}/package-assets.json`));
const directory = path.resolve(root, process.argv[2] || 'Logs/Release/package-inspection');
const actual = new Set(), errors = [], records = new Map([...policy.music, ...policy.songs, ...policy.audio].map(x => [x.path, x]));
const ids = new Set(policy.songs.map(x => x.songId));
let audio = 0, music = 0;
for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
  if (!entry.isDirectory()) continue;
  const folder = path.join(directory, entry.name), pathname = path.join(folder, 'pathname'), asset = path.join(folder, 'asset');
  if (!fs.existsSync(pathname)) continue;
  const logical = fs.readFileSync(pathname, 'utf8').replace(/\0/g, '').trim();
  if (!fs.existsSync(asset)) continue; // folder metadata
  actual.add(logical);
  const bytes = fs.readFileSync(asset);
  const record = records.get(logical);
  if (record) {
    const normalized = logical.endsWith('.asset') ? bytes.toString('utf8').replace(/^\uFEFF/, '').replaceAll('\r\n', '\n') : bytes;
    const hash = crypto.createHash('sha256').update(normalized).digest('hex');
    if (hash !== record.sha256) errors.push(`Changed approved resource: ${logical}`);
  }
  if (/\.(?:mid|midi|ogg|wav|flac|mp3|aif|aiff)$/i.test(logical) && !record) errors.push(`Unapproved music/sample: ${logical}`);
  if (/\.(?:mid|midi)$/i.test(logical)) music++;
  if (/\.(?:ogg|wav|flac|mp3|aif|aiff)$/i.test(logical)) audio++;
  if (logical.startsWith('Assets/UdonMidiPlayer/Editor/Generated/Songs/') && logical.endsWith('.asset') && !record) errors.push(`Unapproved song asset: ${logical}`);
  if (logical.startsWith('Assets/UdonMidiPlayer/Generated/Songs/') && logical.endsWith('.bytes') && !ids.has(path.posix.basename(logical, '.bytes'))) errors.push(`Unapproved packed data: ${logical}`);
  if (/\.(?:prefab|unity|asset)$/.test(logical))
    for (const m of bytes.toString('utf8').matchAll(/^  songId: ([a-f0-9]{32})\s*$/gm)) if (!ids.has(m[1])) errors.push(`Unapproved embedded song: ${logical}`);
  if (logical.includes('RegressionTemp') || logical.includes('ReleaseStaging_') || logical.startsWith('Assets/UdonSharp/') || logical === 'Assets/TextMesh Pro/Resources/TMP Settings.asset') errors.push(`Forbidden package resource: ${logical}`);
}
for (const p of manifest.assets) if (!actual.has(p)) errors.push(`Missing manifest resource: ${p}`);
for (const p of actual) if (!manifest.assets.includes(p)) errors.push(`Unexpected payload resource: ${p}`);
for (const p of ['MIT.txt', 'CC-BY-4.0.txt', 'CC-BY-3.0.txt', 'LICENSE.md', 'THIRD_PARTY_NOTICES.md', 'ReleasePolicy.json', 'VERSION.txt', 'ReleaseWorkflow.md'])
  if (!actual.has('Assets/UdonMidiPlayer/Documentation/' + p)) errors.push(`Missing package license/documentation: ${p}`);
const result = { version, payloadAssets: actual.size, music, audio, errors };
write(`Logs/Release/package-payload-check.json`, result);
console.log(JSON.stringify({ ...result, status: errors.length ? 'failed' : 'passed' }));
if (errors.length) process.exitCode = 1;
