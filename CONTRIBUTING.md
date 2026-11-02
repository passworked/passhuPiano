# Contributing / 参与贡献

乐器贡献可直接提交 PR，不要求先开 Issue。可以分阶段提交，但正式收录前必须补齐模型、音源、实际接入和说明，提供 **PC VRChat 单客户端实测**，并由 passhu 最终决定。美术风格接受逐件评审；音源格式由贡献者提议。MIDI 文件播放必需，其他输入及移动端支持逐项申报。允许 AI 辅助，须披露使用范围并对质量和权利负责。

- [已确认的完整贡献规则](Docs/Contributing/README.md)
- [硬预算与计算口径](Docs/Contributing/BUDGETS.md) · [机器可读政策](Docs/Contributing/instrument-policy.json)
- [提交记录](Docs/Contributing/INSTRUMENT_SUBMISSION_TEMPLATE.md) · [JSON 模板](Docs/Contributing/instrument-submission.template.json)
- [AI 工作入口](Docs/Developer/AI_GUIDE.md) · [当前实现](Docs/Developer/CURRENT_STATE.md)

The linked instrument policy is maintainer-confirmed. Partial PRs may be reviewed independently; a merged phase is not official instrument acceptance. The PR template collects evidence; no automatic resource-budget gate or required-review repository setting is claimed here. GitHub [PR templates](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/creating-a-pull-request-template-for-your-repository) become available from the default branch; they do not enforce these budgets.

Use the existing Unity project root and `MusicMidiPlayer.*` architecture. Keep Runtime and Editor code separate. Preserve `.meta`, GUIDs, song IDs, references and unrelated work. The main scene remains `Assets/Scenes/VRCDefaultWorldScene.unity`; distribution Prefabs have their own configurations and repertoire. Do not apply main-scene overrides to them.

Submit project-owned code and accompanying technical documentation under MIT, and project-owned art under CC BY 4.0. State your preferred credit. Only contribute material you have the right to license. Third-party contributions require an upstream URL, author, exact license, pinned version/hash, original notices and a description of modifications; do not relabel them as project-owned.

For scores and samples we accept public-domain/CC0, attribution-only CC BY 3.0/4.0 or documented unrestricted permissions. Do not add BY-SA, NC, ND or unverified content to release inventories. Conversion to MIDI, compressed samples or packed song data does not remove the source license. Fonts follow their own licenses.

Run `node Tools/Release/release.mjs inventory` after an explicitly reviewed source/permission change, and inspect the resulting diff. The script never treats an unknown license as approval. Do not edit inventories to bypass an export failure. New sources need explicit provenance support in the inventory tool.

Validate only the affected behavior. Distinguish static inspection, Unity Editor, ClientSim and real VRChat results. Reports should include the exact version, platform, reproduction steps and relevant logs, without credentials or personal paths. See [maintenance conventions](Docs/UdonMidiPlayer/17-maintenance-conventions.md).
