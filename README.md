# passhuPiano

VRChat Worlds / UdonSharp MIDI player, editable instrument art and offline authoring tools in one repository. Maintainer: **passhu**, laptop7921@gamil.com.

Repository: [passworked/passhuPiano](https://github.com/passworked/passhuPiano), private. Version: **1.0.0-rc.2 — source update**. No stable v1.0 download has been published. The initial stable scope is the two independent piano Prefabs; the multi-instrument main world, practice system and classroom remain separately documented modules. Internal `musicMidiPlayer` paths, `MusicMidiPlayer.*` types and Unity GUIDs are retained.

The November 2 update includes state-driven UI refresh, batched ensemble audio output, Shader-driven waterfall motion and incremental loading finalization. The main world also retains the updated per-instrument string-style controls and repertoire navigation. These are implementation changes; runtime performance, real VRChat synchronization and platform support have not been signed off. See [performance evidence and limits](Docs/UdonMidiPlayer/09-performance-and-validation.md).

## Use the player

The validation baseline is Unity **2022.3.22f1** and VRChat Worlds SDK **3.10.5**. Restore the SDK through VCC/ALCOM before opening the project. This is a recorded development baseline, not a claim of support for every newer SDK or mobile platform.

When a tested release package is available, import it into an existing Worlds project, drag either `UdonMidiPlayer.prefab` or `UdonMidiPlayer_Upright.prefab` into your world, then use **Tools → Music MIDI Player → Configure Player** for your own permitted repertoire. The release package must not replace global TMP/audio settings or install the SDK.

The release repertoire contains only reviewed public-domain/CC0 or attribution-only sources. The old 100-track development library is not the release library. BY-SA, NC, ND and unknown-permission MIDI/samples, including their baked derivatives, are excluded. Third-party licenses remain unchanged.

## Work on the source

Clone the public source snapshot with Git LFS installed, run `git lfs pull`, restore Unity/VPM dependencies, and open the repository root with the stated Unity version. `ArtSource/` holds editable art outside Unity's import tree; `Tools/` holds offline processing scripts. Unity MCP is an optional local development dependency and is removed from public project manifests.

The local authoring project may contain private repertoire and an unsaved main scene. Only the sanitized source snapshot produced by the release workflow is suitable for the remote repository; do not push the local directory wholesale. The same score/sample permission filters apply to this private repository and future public distributions.

- [Release workflow and current evidence](Docs/UdonMidiPlayer/22-release-implementation.md)
- [Architecture and documentation](Docs/UdonMidiPlayer/README.md)
- [Release plan](Docs/UdonMidiPlayer/21-v1-release-and-open-source.md)
- [License scope](LICENSE.md) · [Third-party notices](THIRD_PARTY_NOTICES.md) · [Contributing](CONTRIBUTING.md)

Project-owned code: **MIT**. Project-owned art: **CC BY 4.0**. Third-party resources retain their individual licenses.
