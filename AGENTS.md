# musicMidiPlayer

## 核心约束

- 只改当前需求；保留并行修改、GUID、`.meta`、曲目 ID 和引用，优先增量更新。
- 主场景：`Assets/Scenes/VRCDefaultWorldScene.unity`；练习系统保持独立，不新建替代主场景。
- 场景曲库与分发 Prefab 分开；日常加歌不自动同步 Prefab，不擅自 Apply 实例覆盖。
- 沿用 `MusicMidiPlayer.*` 命名空间和现有架构，保持 Runtime/Editor 隔离。
- Unity 操作与截图优先 MCP；浏览器显式使用 Chrome，禁止 Edge 或自动回退。
- 按任务范围验证，遵守构建/测试限制；如实区分 Editor 与真实客户端结果。
- 以最新有效要求为准。AGENTS.md 只放核心约束和索引，详细约定写 docs 并同步维护。
- 仓库 README 默认语言为英文（`README.md`）；中文、日文翻译与英文事实同步，规则见 `Docs/Developer/AI_GUIDE.md`。

## 文档索引

- [AI / 开发者工作入口](Docs/Developer/AI_GUIDE.md) · [当前实现与源码地图](Docs/Developer/CURRENT_STATE.md)
- [乐器贡献规则](Docs/Contributing/README.md) · [机器可读政策](Docs/Contributing/instrument-policy.json) · [资源预算](Docs/Contributing/BUDGETS.md)

- [文档总览](Docs/UdonMidiPlayer/README.md) · [详细维护约定](Docs/UdonMidiPlayer/17-maintenance-conventions.md)
- [UI 层级与绘制顺序](Docs/UdonMidiPlayer/15-ui-hierarchy.md) · [控制与 UI](Docs/UdonMidiPlayer/07-controls-and-ui.md)
- [架构](Docs/UdonMidiPlayer/01-architecture.md) · [同步](Docs/UdonMidiPlayer/04-multiplayer-sync.md)
- [MIDI 导入](Docs/UdonMidiPlayer/02-data-and-import.md) · [编辑器与 Prefab](Docs/UdonMidiPlayer/08-editor-and-prefab.md)
- [乐器与音源](Docs/UdonMidiPlayer/13-instruments.md) · [打击乐](Docs/UdonMidiPlayer/14-percussion-preparation.md) · [弦乐](Docs/UdonMidiPlayer/14-strings-preparation.md)
- [新乐器追加规范（草案）](Docs/UdonMidiPlayer/18-adding-instruments.md) · [GM 音色分组与回退](Docs/UdonMidiPlayer/19-gm-program-fallback.md)
- [v1.0 发布与 monorepo 开源规划](Docs/UdonMidiPlayer/21-v1-release-and-open-source.md) · [发布实施记录](Docs/UdonMidiPlayer/22-release-implementation.md)
- [历史约定恢复依据](Docs/UdonMidiPlayer/16-project-conventions-recovery.md)
