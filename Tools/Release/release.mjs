// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
export const policyPath = 'Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json';
export const read = p => fs.readFileSync(path.join(root, p), 'utf8').replace(/^\uFEFF/, '');
// Unity text assets are normalized by Git; pin semantic bytes with LF so clean
// clones do not fail permission checks solely because of Windows CRLF.
export const sha = p => crypto.createHash('sha256').update(p.endsWith('.asset') ? read(p).replaceAll('\r\n', '\n') : fs.readFileSync(path.join(root, p))).digest('hex');
export const walk = p => !fs.existsSync(path.join(root, p)) ? [] : fs.readdirSync(path.join(root, p), { withFileTypes: true }).flatMap(e => e.isSymbolicLink() || e.name === '.git' ? [] : e.isDirectory() ? walk(`${p}/${e.name}`) : [`${p}/${e.name}`]);
export function write(p, value) {
  fs.mkdirSync(path.dirname(path.join(root, p)), { recursive: true });
  fs.writeFileSync(path.join(root, p), typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n', 'utf8');
}
const scalar = (text, name) => text.match(new RegExp(`^  ${name}: (.*)$`, 'm'))?.[1]?.trim() || '';
const guid = p => fs.existsSync(path.join(root, p + '.meta')) ? read(p + '.meta').match(/^guid: ([a-f0-9]{32})$/m)?.[1] || '' : '';
const allowed = license => /^(Public Domain(?: \(PD-self; Public Domain Mark 1\.0\))?|Creative Commons Attribution (?:3\.0|4\.0)|CC0(?:-1\.0)?)$/.test(license);
const normalize = license => license.startsWith('Public Domain') ? 'LicenseRef-Public-Domain' : license.includes('3.0') ? 'CC-BY-3.0' : license.includes('4.0') ? 'CC-BY-4.0' : 'CC0-1.0';
const audioGroups = [
  { prefix: 'Assets/UdonMidiPlayer/Audio/SelectedQ4/Piano/', license: 'CC-BY-3.0', author: 'Alexander Holm; FreePats / Roberto', source: 'https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html', notices: ['ATTRIBUTION.txt', 'ORIGINAL-README.txt'] },
  { prefix: 'Assets/UdonMidiPlayer/Audio/SalamanderV9/', license: 'CC-BY-3.0', author: 'Alexander Holm; FreePats / Roberto', source: 'https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html', notices: ['ORIGINAL-README.txt'] },
  { prefix: 'Assets/UdonMidiPlayer/Audio/SelectedQ4/Marimba/', license: 'LicenseRef-Iowa-MIS-Unrestricted', author: 'Lawrence Fritts; Andrew Thierauf; University of Iowa', source: 'https://theremin.music.uiowa.edu/MIS.html', notices: ['LICENSE.txt', 'ATTRIBUTION.txt'] },
  { prefix: 'Assets/UdonMidiPlayer/Audio/Strings/', license: 'CC0-1.0', author: 'Versilian Studios / Sam Gossner; Ivy Audio / Simon Dalzell', source: 'https://versilian-studios.com/vsco-community/', notices: ['LICENSE', 'ATTRIBUTION.txt', 'ORIGINAL-README.md'] },
  { prefix: 'Assets/UdonMidiPlayer/Audio/VirtuosityDrums/', license: 'CC0-1.0', author: 'Versilian Studios LLC; Karoryfer Samples; Austin McMahon', source: 'https://github.com/sfzinstruments/virtuosity_drums/tree/9f04cf9a734527edfbb0a4eee1f674e45bbf71bc', notices: ['LICENSE.txt', 'ATTRIBUTION.txt'] },
  { prefix: 'Assets/UdonMidiPlayer/Audio/MusicBox/', license: 'CC0-1.0', author: 'rbnuser', source: 'https://freesound.org/people/rbnuser/packs/19724/', notices: ['LICENSE.txt', 'ATTRIBUTION.txt'] }
];

function excludedPublicFile(p) {
  const scopePath = `Tools/Release/candidates/${read('VERSION').trim()}.json`;
  if (fs.existsSync(path.join(root, scopePath))) {
    const scope = JSON.parse(read(scopePath));
    if (scope.version !== read('VERSION').trim()) throw Error('Invalid candidate scope');
    const name = p.replace(/\.meta$/, '');
    if (scope.excludedPaths.includes(name) || scope.excludedPrefixes.some(prefix => name.startsWith(prefix) || name === prefix.replace(/\/$/, ''))) return true;
  }
  const rules = JSON.parse(read('Tools/Release/public-exclusions.json'));
  return rules.groups.some(g => g.paths.includes(p) || g.prefixes.some(prefix => p.startsWith(prefix)));
}

export function inventory() {
  // Existing curated provenance is reviewed input. No source URL or filename alone grants rights.
  const selectionPath = 'Assets/music/Classical100/SELECTION.json';
  const previous = fs.existsSync(path.join(root, policyPath)) ? JSON.parse(read(policyPath)) : null;
  const selection = fs.existsSync(path.join(root, selectionPath)) ? JSON.parse(read(selectionPath)) : null;
  if (!selection && !previous) throw Error('No reviewed provenance or release inventory found.');
  const approvedHashes = new Map();
  for (const item of selection || []) {
    if (!allowed(item.metadata.license)) continue;
    if (!item.metadata.source_page || !item.metadata.license_url || !item.metadata.attribution) throw Error(`Incomplete provenance: ${item.asset_path}`);
    if (sha(item.asset_path) !== item.sha256) throw Error(`Source changed; review required: ${item.asset_path}`);
    approvedHashes.set(item.sha256, { license: normalize(item.metadata.license), author: item.metadata.maintainer || item.metadata.attribution, source: item.metadata.source_page, licenseUrl: item.metadata.license_url, attribution: item.metadata.attribution });
  }
  if (!selection) for (const item of previous.music) approvedHashes.set(item.sha256, item);
  const music = [], deniedMusic = [];
  for (const p of [...walk('Assets/music'), ...walk('Assets/UdonMidiPlayer/Samples')].filter(p => /\.(mid|midi)$/i.test(p)).sort()) {
    const hash = sha(p), provenance = approvedHashes.get(hash);
    if (provenance) music.push({ path: p, guid: guid(p), sha256: hash, ...provenance, path: p, guid: guid(p) });
    else {
      const known = selection?.find(x => x.sha256 === hash);
      deniedMusic.push({ path: p, guid: guid(p), sha256: hash, license: known?.metadata.license || 'NOASSERTION', reason: known ? 'License outside release policy' : 'No reviewed score provenance' });
    }
  }
  const sourceByGuid = new Map(music.map(x => [x.guid, x]));
  const songs = [], deniedSongs = [];
  for (const p of walk('Assets/UdonMidiPlayer/Editor/Generated/Songs').filter(p => p.endsWith('.asset')).sort()) {
    const yaml = read(p), songId = scalar(yaml, 'songId'), sourceGuid = scalar(yaml, 'sourceGuid'), sourceHash = scalar(yaml, 'sourceHash');
    if (!songId) continue;
    const provenance = sourceByGuid.get(sourceGuid);
    const item = { path: p, guid: guid(p), sha256: sha(p), songId, sourceGuid, sourceHash };
    if (provenance && provenance.sha256 === sourceHash) songs.push({ ...item, license: provenance.license });
    else deniedSongs.push({ ...item, reason: 'Unapproved or changed source MIDI' });
  }
  const audio = [], deniedAudio = [];
  for (const p of walk('Assets/UdonMidiPlayer/Audio').filter(p => /\.(ogg|wav|flac|aif|aiff|mp3)$/i.test(p)).sort()) {
    const group = audioGroups.find(g => p.startsWith(g.prefix));
    if (group) audio.push({ path: p, guid: guid(p), sha256: sha(p), license: group.license });
    else deniedAudio.push({ path: p, reason: 'Not in reviewed sample groups' });
  }
  for (const group of audioGroups) for (const notice of group.notices) if (!fs.existsSync(path.join(root, group.prefix, notice))) throw Error(`Missing sample notice: ${group.prefix}${notice}`);
  const policy = { schemaVersion: 1, hashNormalization: 'LF for song .asset text; raw bytes for MIDI and audio', owner: 'passhu', contact: 'laptop7921@gamil.com', codeLicense: 'MIT', artLicense: 'CC-BY-4.0', allowedMusicLicenses: ['LicenseRef-Public-Domain', 'CC0-1.0', 'CC-BY-3.0', 'CC-BY-4.0', 'LicenseRef-Iowa-MIS-Unrestricted'], audioGroups, music, songs, audio };
  write(policyPath, policy);
  write('Logs/Release/license-audit.json', { curatedTotal: selection?.length, curatedApproved: selection?.filter(x => allowed(x.metadata.license)).length, musicApproved: music.length, songsApproved: songs.length, audioApproved: audio.length, deniedMusic, deniedSongs, deniedAudio });
  const credits = [
    '# Third-party notices', '', 'Project-owned code: MIT. Project-owned art: CC BY 4.0. Author: passhu; laptop7921@gamil.com.', '',
    'This release excludes BY-SA, NC, ND and unknown-permission scores and sound samples. Third-party works retain their original terms. Generated song data follows its source MIDI license.', '',
    '## Samples', '',
    ...audioGroups.flatMap(g => [`- **${g.author}** — ${g.license}. [Source](${g.source}). Notices: ${g.notices.map(n => `\`${g.prefix}${n}\``).join(', ')}.`, '']),
    'Sample conversions, velocity/root selection, tuning, loop processing and aliases are described in the original bank mappings and local ATTRIBUTION files. Salamander remains CC BY 3.0. Iowa permission remains its original unrestricted permission, not CC0 or a project-owned CC license.', '',
    '## Fonts', '', 'Noto and Comfortaa source fonts and TMP derivatives retain SIL OFL 1.1. See `Assets/UdonMidiPlayer/Fonts/font-manifest.json` and each original OFL file. Bravura font material used by classroom production retains its original license in `ArtSource/DemoMusicClassroom/Fonts/Bravura-LICENSE.txt`. Liberation Sans and TMP support assets retain their original notices. SDK dependencies are restored from upstream and are not relicensed as project code.', '',
    '## Scores', '', 'The following approved recordings/typesettings may have several byte-identical local paths. All retained paths, hashes, song IDs and derived assets are recorded in `Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json`.', '',
    ...[...approvedHashes.values()].map(g => `- ${g.attribution} License: ${g.license}; [license/source terms](${g.licenseUrl}).`), '',
    'MIDI bytes are retained; filenames, display metadata and playback/packed representations may differ. No upstream endorsement is implied. Local repertoire outside this inventory is not licensed by this project and is not included in public snapshots.', ''
  ].join('\n');
  write('THIRD_PARTY_NOTICES.md', credits);
  for (const [src, dst] of [['LICENSE.md', 'LICENSE.md'], ['THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_NOTICES.md'], ['Licenses/MIT.txt', 'MIT.txt'], ['Licenses/CC-BY-4.0.txt', 'CC-BY-4.0.txt'], ['Licenses/CC-BY-3.0.txt', 'CC-BY-3.0.txt']]) {
    if (!fs.existsSync(path.join(root, src))) throw Error(`Missing release license: ${src}`);
    write(`Assets/UdonMidiPlayer/Documentation/${dst}`, read(src).replaceAll('(Licenses/', '('));
  }
  write('Assets/UdonMidiPlayer/Documentation/VERSION.txt', read('VERSION'));
  if (fs.existsSync(path.join(root, 'Docs/MIDI_IMPORT.md')))
    write('Assets/UdonMidiPlayer/Documentation/MidiImport.md', read('Docs/MIDI_IMPORT.md')
      .replace('[GETTING_STARTED](GETTING_STARTED.md)', 'repository `Docs/GETTING_STARTED.md`'));
  if (fs.existsSync(path.join(root, 'Docs/UdonMidiPlayer/22-release-implementation.md')))
    write('Assets/UdonMidiPlayer/Documentation/ReleaseWorkflow.md', read('Docs/UdonMidiPlayer/22-release-implementation.md')
      .replaceAll('(../../THIRD_PARTY_NOTICES.md)', '(THIRD_PARTY_NOTICES.md)')
      .replaceAll('(../../Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json)', '(ReleasePolicy.json)'));
  console.log(JSON.stringify({ curatedApproved: selection?.filter(x => allowed(x.metadata.license)).length, curatedExcluded: selection?.filter(x => !allowed(x.metadata.license)).length, approvedMusicPaths: music.length, approvedSongAssets: songs.length, approvedAudio: audio.length, excludedMusicPaths: deniedMusic.length, excludedSongAssets: deniedSongs.length, excludedAudio: deniedAudio.length }));
}

export function sourceSnapshot() {
  const version = read('VERSION').trim();
  if (fs.existsSync(path.join(root, `Releases/${version}/SEALED.json`))) throw Error('Candidate is sealed; use a new VERSION before regenerating source.');
  const output = `Releases/${version}/source/musicMidiPlayer`;
  const manifest = JSON.parse(read(`Releases/${version}/source-assets.json`));
  if (manifest.version !== version) throw Error('Source manifest VERSION mismatch');
  const overrides = new Set(manifest.overrides);
  const files = new Set(manifest.assets);
  for (const p of manifest.assets) {
    if (!p.startsWith('Assets/') || p.includes('..') || p.includes('RegressionTemp') || p.includes('ReleaseStaging_')) throw Error(`Unsafe source asset path: ${p}`);
    if (!fs.existsSync(path.join(root, p + '.meta'))) throw Error(`Missing GUID sidecar: ${p}`);
    files.add(p + '.meta');
    for (let parent = path.posix.dirname(p); parent !== '.' && parent !== 'Assets'; parent = path.posix.dirname(parent))
      if (fs.existsSync(path.join(root, parent + '.meta'))) files.add(parent + '.meta');
  }
  // Authoring sources and documentation are copied as source, not caches or bundles.
  for (const directory of ['ArtSource', 'Docs', 'Licenses', 'Tools', '.github']) for (const p of walk(directory)) {
    if (excludedPublicFile(p)) continue;
    if (/(?:^|\/)(?:__pycache__|python_packages|Extracted|Layout|Payload|Portable|References)(?:\/|$)/.test(p) || /\.(?:pyc|exe|dll|zip|7z|wav|mp3|mid|midi)$/i.test(p)) continue;
    if (!/\.(?:blend|fbx|glb|png|jpg|jpeg|otf|ttf|txt|md|json|mjs|py|ps1|cmd|yml|yaml)$/i.test(p)) continue;
    files.add(p);
  }
  for (const p of walk('ProjectSettings')) files.add(p);
  for (const p of ['Packages/manifest.json', 'Packages/packages-lock.json', 'Packages/vpm-manifest.json', '.gitignore', '.gitattributes', 'AGENTS.md', 'README.md', 'README.zh-CN.md', 'README.ja.md', 'LICENSE.md', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', 'CHANGELOG.md', 'VERSION'])
    if (fs.existsSync(path.join(root, p))) files.add(p);
  // Remove only known withheld files from this unsealed generated snapshot;
  // authoring originals and other candidates are never touched.
  const outputAbsolute = path.resolve(root, output);
  for (const p of walk(output)) if (excludedPublicFile(p.slice(output.length + 1))) {
    const target = path.resolve(root, p);
    if (!target.startsWith(outputAbsolute + path.sep)) throw Error('Snapshot cleanup escaped its output root');
    fs.rmSync(target);
  }
  for (const p of [...files].sort()) {
    const destination = path.join(root, output, p);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    if (overrides.has(p)) {
      if (!fs.existsSync(destination)) throw Error(`Missing sanitized asset override: ${p}`);
    } else fs.copyFileSync(path.join(root, p), destination);
  }
  const publicRead = p => fs.readFileSync(path.join(root, output, p), 'utf8').replace(/^\uFEFF/, '');
  const publicWrite = (p, v) => fs.writeFileSync(path.join(root, output, p), typeof v === 'string' ? v : JSON.stringify(v, null, 2) + '\n');
  publicWrite('.gitignore', publicRead('.gitignore').split('# Publication safety:')[0]);
  const packages = JSON.parse(publicRead('Packages/manifest.json'));
  delete packages.dependencies['com.coplaydev.unity-mcp'];
  publicWrite('Packages/manifest.json', packages);
  if (files.has('Packages/packages-lock.json')) {
    const lock = JSON.parse(publicRead('Packages/packages-lock.json'));
    delete lock.dependencies?.['com.coplaydev.unity-mcp'];
    publicWrite('Packages/packages-lock.json', lock);
  }
  // Public snapshots do not inherit cloud/project identity or local credentials.
  for (const p of ['ProjectSettings/ProjectSettings.asset', 'ProjectSettings/UnityConnectSettings.asset']) {
    if (!files.has(p)) continue;
    publicWrite(p, publicRead(p).replace(/^(\s*(?:cloudProjectId|organizationId|projectName|m_ProjectId|m_OrganizationId|m_ProjectName|m_AccessToken|m_RefreshToken):).*$/gm, '$1 '));
  }
  // Display-only historical paths are generalized; asset GUIDs and relative paths stay intact.
  for (const p of files) {
    if (!/\.(?:cs|py|mjs|ps1|cmd|md|txt|json|asset|unity|prefab)$/i.test(p) || overrides.has(p)) continue;
    let s = publicRead(p);
    s = s.replace(/C:[\\/]+Users[\\/]+24514/gi, 'C:/Users/<user>');
    publicWrite(p, s);
  }
  // Regenerate the public policy to include only the bytes actually shipped. These
  // records remain pinned; future new content still needs upstream provenance review.
  const policy = JSON.parse(publicRead(policyPath));
  const shipped = new Set(files);
  policy.music = policy.music.filter(x => shipped.has(x.path));
  policy.songs = policy.songs.filter(x => shipped.has(x.path));
  policy.audio = policy.audio.filter(x => shipped.has(x.path));
  publicWrite(policyPath, policy);
  auditSnapshot(output);
  write(`Releases/${version}/source-files.json`, { version, files: [...files].sort(), sceneSource: manifest.sceneSource });
  console.log(JSON.stringify({ sourceRoot: output, files: files.size, sanitizedAssets: overrides.size }));
}

export function auditSnapshot(output = `Releases/${read('VERSION').trim()}/source/musicMidiPlayer`) {
  const policy = JSON.parse(fs.readFileSync(path.join(root, output, policyPath), 'utf8'));
  const all = walk(output);
  const rel = p => p.slice(output.length + 1);
  const approved = new Map([...policy.music, ...policy.songs, ...policy.audio].map(x => [x.path, x]));
  const ids = new Set(policy.songs.map(x => x.songId));
  const denied = [];
  for (const p of all) {
    const name = rel(p);
    if (excludedPublicFile(name)) denied.push(`Withheld musical derivative/preview: ${name}`);
    if (/\.(?:mid|midi|ogg|wav|flac|aif|aiff|mp3)$/i.test(name)) {
      const item = approved.get(name);
      if (!item || sha(p) !== item.sha256) denied.push(`Unapproved/changed musical file: ${name}`);
    }
    if (name.startsWith('Assets/UdonMidiPlayer/Editor/Generated/Songs/') && name.endsWith('.asset')) {
      const item = approved.get(name);
      if (!item || sha(p) !== item.sha256) denied.push(`Unapproved/changed song asset: ${name}`);
    }
    if (name.startsWith('Assets/UdonMidiPlayer/Generated/Songs/') && name.endsWith('.bytes') && !ids.has(path.posix.basename(name, '.bytes'))) denied.push(`Unapproved packed song: ${name}`);
    if (/\.(?:prefab|unity|asset)$/.test(name)) {
      const s = fs.readFileSync(path.join(root, p), 'utf8');
      for (const m of s.matchAll(/^  songId: ([a-f0-9]{32})\s*$/gm)) if (!ids.has(m[1])) denied.push(`Unapproved embedded song ${m[1]}: ${name}`);
      if (/blueprintId:\s*wrld_/.test(s)) denied.push(`World upload identity: ${name}`);
    }
    if (name.includes('RegressionTemp') || name.includes('ReleaseStaging_') || /(^|\/)(?:Library|RecoveryBackups|Downloads|ClientSimStorage)\//.test(name)) denied.push(`Local-only file: ${name}`);
  }
  if (denied.length) throw Error(denied.slice(0, 20).join('\n'));
  console.log(JSON.stringify({ audit: 'passed', files: all.length, music: policy.music.length, songs: policy.songs.length, audio: policy.audio.length }));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const command = process.argv[2] || 'inventory';
    if (command === 'inventory') inventory();
    else if (command === 'source') sourceSnapshot();
    else if (command === 'audit') auditSnapshot();
    else throw Error('Usage: node Tools/Release/release.mjs inventory|source|audit');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
