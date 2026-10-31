# Contributing

Use the existing Unity project root and `MusicMidiPlayer.*` architecture. Keep Runtime and Editor code separate. Preserve `.meta`, GUIDs, song IDs, references and unrelated work. The main scene remains `Assets/Scenes/VRCDefaultWorldScene.unity`; distribution Prefabs have their own configurations and repertoire. Do not apply main-scene overrides to them.

Submit project-owned code and accompanying technical documentation under MIT, and project-owned art under CC BY 4.0. State your preferred credit. Only contribute material you have the right to license. Third-party contributions require an upstream URL, author, exact license, pinned version/hash, original notices and a description of modifications; do not relabel them as project-owned.

For scores and samples we accept public-domain/CC0, attribution-only CC BY 3.0/4.0 or documented unrestricted permissions. Do not add BY-SA, NC, ND or unverified content to release inventories. Conversion to MIDI, compressed samples or packed song data does not remove the source license. Fonts follow their own licenses.

Run `node Tools/Release/release.mjs inventory` after an explicitly reviewed source/permission change, and inspect the resulting diff. The script never treats an unknown license as approval. Do not edit inventories to bypass an export failure. New sources need explicit provenance support in the inventory tool.

Validate only the affected behavior. Distinguish static inspection, Unity Editor, ClientSim and real VRChat results. Reports should include the exact version, platform, reproduction steps and relevant logs, without credentials or personal paths. See [maintenance conventions](Docs/UdonMidiPlayer/17-maintenance-conventions.md).
