# Changelog

## 1.0.0-rc.3 — 2026-11-04 Golden-angle channels and MIDI library editor

- Rotate file-playback channel hues by the golden angle across keyboards, waterfalls, particles, strings, drums and clockwork links; retain player-based colors for live input.
- Replace the path-and-log import window with a VizVid-style folder/song draft editor: inline title and author fields, drag ordering, multiple folder memberships, search, reload, save and apply, and playlist JSON.
- Accept external MIDI files, folders, ZIPs and direct download URLs; reuse exact source hashes, optionally prefer same-title versions by source event count and date, and retain GUID/songId during reimport, rename and backed-up source replacement.
- Include the current main-world instrument/routing improvements in the reviewed source snapshot; keep standalone piano Prefabs and their approved repertoire separate. Include CC0 music-box sample attribution.
- Add a Unity MIDI import tutorial and an explicit editor operation contract. Preserve the private repository's visibility and use a prerelease tag.

Editor/source checks are recorded with the release artifacts. Clean receiving-project import, hardware/VR input, real VRChat listening, two-client synchronization and platform performance remain unverified; this is a release candidate.

## 1.0.0-rc.2 — 2026-11-02 source update

- Follow up the Windows optimization changes: state-driven UI refresh, batched ensemble output, instanced waterfall Shader timing and incremental loading finalization.
- Retain the main-world per-instrument string-style UI and revised repertoire categories, while filtering unauthorized songs and derived data out of the remote source snapshot.
- Prepare the private GitHub monorepo `passworked/passhuPiano`, retaining the reviewed rc.1 history, GUIDs and MIT / CC BY 4.0 license scope.
- Update source and compiled program assets together; preserve local main-scene content and the sealed rc.1 artifacts.

This update is a private source candidate. No Play Mode, Profiler, test suite, world build or real VRChat acceptance is claimed. Existing automatic Editor compilation and static export/reference checks are recorded separately.

## 1.0.0-rc.1 — in preparation

- Prepare one monorepo for player code, Unity world, editable art, tools and documentation.
- License project-owned code under MIT and art under CC BY 4.0; credit passhu.
- Apply an explicit score/sample release allowlist, including derived assets; exclude BY-SA, NC, ND and unknown permissions.
- Keep the two piano Prefabs and their distribution repertoire independent of local main-scene content.

This candidate is not a stable release. Real VRChat listening, two-client synchronization, clean-project import and supported-platform sign-off must be recorded before `1.0.0`.
