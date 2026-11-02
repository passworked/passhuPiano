# passhuPiano

VRChat Worlds / UdonSharp MIDI player, editable instrument art and offline authoring tools in one repository. Maintainer: **passhu**, laptop7921@gamil.com.

Repository: [passworked/passhuPiano](https://github.com/passworked/passhuPiano), private. Version: **1.0.0-rc.2 — source update**. No stable v1.0 download has been published. The initial stable scope is the two independent piano Prefabs; the multi-instrument main world, practice system and classroom remain separately documented modules. Internal `musicMidiPlayer` paths, `MusicMidiPlayer.*` types and Unity GUIDs are retained.

The November 2 update includes state-driven UI refresh, batched ensemble audio output, Shader-driven waterfall motion and incremental loading finalization. The main world also retains the updated per-instrument string-style controls and repertoire navigation. These are implementation changes; runtime performance, real VRChat synchronization and platform support have not been signed off. See [performance evidence and limits](Docs/UdonMidiPlayer/09-performance-and-validation.md).

## Documentation and instrument contributions

- **使用工程 / Setup:** [安装与使用入口](Docs/GETTING_STARTED.md).
- **AI / Developers:** [工作指南](Docs/Developer/AI_GUIDE.md), [当前实现与源码地图](Docs/Developer/CURRENT_STATE.md), [machine-readable document map](Docs/doc-map.json).
- **提交乐器 / Contribute instruments:** [维护者确认的规则](Docs/Contributing/README.md), [资源预算与公式](Docs/Contributing/BUDGETS.md), [提交模板](Docs/Contributing/INSTRUMENT_SUBMISSION_TEMPLATE.md), [machine-readable policy](Docs/Contributing/instrument-policy.json).

Direct PRs and phased contributions are welcome. Official instrument inclusion requires the model, sound source, integration, documentation, PC VRChat single-client evidence and passhu's acceptance. Different visual styles and audio format proposals are reviewed individually. AI assistance is allowed with its scope disclosed; contributors remain responsible for rights and quality. These are review rules, not an automatic admission system.

New instruments share the existing 64 physical AudioSources and must meet the confirmed per-instrument caps: 50,000 triangles, 8 distinct concurrent materials, 128 Renderers; textures at most 2048×2048 and 64 MiB RGBA32 full-mip estimate; 32 MiB source audio and 640 MiB float32 PCM estimate. See the budget document for the counting boundary. Existing resources are not automatically reworked by this policy.

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
