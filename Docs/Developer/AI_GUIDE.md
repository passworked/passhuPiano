# AI 与开发者工作指南

状态：当前工作入口。新贡献规则由 passhu 于 2026-11-02 确认。实现事实基线为私仓提交 `21c0593`（源码 `1.0.0-rc.2`），后续使用者须先核对自己的 checkout。本文不表示已经存在通用乐器插件 API、自动预算检查或真实客户端验收。

## 先读什么

1. 仓库根 [AGENTS.md](../../AGENTS.md)：当前操作边界。
2. [CURRENT_STATE](CURRENT_STATE.md)：当前模块、身份与源码路径。
3. 新增乐器时读 [贡献规则](../Contributing/README.md)、[机器政策](../Contributing/instrument-policy.json) 和 [预算口径](../Contributing/BUDGETS.md)。
4. 按下面的任务表再读必要专题；不要把所有历史文档一股脑当作现行规范。[doc-map.json](../doc-map.json) 提供文档状态与入口。

用户最新明确要求优先。维护者确认的准入政策定义“允许什么”；当前源码 / 保存资产定义“实际有什么”。两者冲突时报告缺口，不能修改准入上限来让现有资源看似达标。旧计划、旧测试目标和旧接口表只能解释历史，不能推翻已确认规则或当前实现。

## 先确定你在哪个工程

作者的本地工程可能含完整私人曲库、未保存场景与尚未推送的代码；远端是经过筛选的源码快照。新文档、当前 checkout、用户口述和 Editor 内存是不同证据来源，必须标清。

- 开始前查看工作目录、当前提交和既有修改。保留并行工作，不回滚陌生差异。
- Unity 任务再通过 MCP 核对工程、主场景、播放 / 编译状态和场景是否未保存。纯文档任务不启动 Unity、Play Mode 或构建。
- 有文件不等于完成接线；有源码不等于 backing Udon 已更新；Editor 检查不等于 ClientSim / VRChat 实测。
- 被用户限制测试或构建时遵守本次范围，写明未执行，不拿旧任务的授权当永久授权。

## 按任务找代码

下列路径均相对仓库根，完整入口见 [CURRENT_STATE](CURRENT_STATE.md)。

| 任务 | 先读 | 通常检查的实现 |
| --- | --- | --- |
| 提交乐器或音源 | 贡献政策、预算、18 / 19 册 | `Editor/Prefab/MidiInstrumentRoutingBuilder.cs`、`Runtime/Logic/MidiInstrumentRouter.cs`、bank / VoicePool / Builder / Validator |
| 导入或重导入 MIDI | 02 册、当前 `MidiSongAsset` | `Editor/Import/`、`Editor/Baking/`、`Runtime/Data/`；songId、源 GUID、哈希和编码一起检查 |
| 文件与实时播放 / 恢复 | 03 / 04 册及最新 17 册 | `MidiPlayerFacade`、`MidiSequencer`、`MidiTransportClock`、`MidiNetworkTransport`、声音池 |
| 模型 / 动作 / 瀑布 | 13 / 14 册、当前 View | `Runtime/Views/`、`Shaders/`、对应 Builder；保留暂停、定位和停止语义 |
| UI / 翻译 | 07 / 15 册、本地化 README | `Runtime/UI/`、`MidiPlayerUIBuilder`、语言表；层级排序及 proxy / backing 接线 |
| 发布资源与私仓 | 21 / 22 册、根 LICENSE | `Tools/Release/`、`MidiReleasePolicy`、`MidiReleasePreparation`、`MidiPackageExporter` |

表内 `Editor/`、`Runtime/`、`Shaders/` 都位于 `Assets/UdonMidiPlayer/`；不要据表中的短路径另建一套目录。

## 最小变更流程

1. 把目标对象、贡献阶段、允许修改的目录与不依赖答案的工作写清。维护者未定的设计用“待决定”，不伪造批准。
2. 搜索已有实现与调用链。新增乐器先确认是外观、复用 bank、新 bank、持续音 / 打击乐，还是要扩展目标编码。
3. 列出 Runtime、Editor、资源、序列化、接线、UI / 翻译、哈希、许可与文档的实际受影响位置。只改需要的部分。
4. 保留 GUID、`.meta`、songId、命名空间和程序集。源模型在 `ArtSource/`，导入资源在 `Assets/`；不新建替代主场景，不把实例覆盖 Apply 回共享 Prefab。
5. 按任务范围验证并留下证据。阶段贡献可以未完成；正式乐器收录必须具备政策要求的 PC VRChat 单客户端实测和维护者确认。
6. 汇报结果、改变的文件、实际验证、未执行项与风险。保留来源 / 预算 / 验收记录与实现同步。

## 不要猜测的边界

- 当前六个旋律目标 ID 为 0～5，旧 6 停用；它们不等于模型菜单索引或 GM Program。不能通过简单“追加到数组末尾”宣告任意新乐器已接入。
- 当前共用 64 个物理 AudioSource；一个逻辑音可能占两条源。不能改成每件乐器 64 条，或靠队列掩盖预算。
- 当前类型、字段长度、Manual / None 网络边界与规则版本都要查源码。`CONTRACTS.md` 中保留了早期 v0.1 字段和计划，不能直接照抄。
- 维护者允许音源格式提案。现有 Ogg / PCM、立体声 / 单声道和力度层是相应 bank 的事实，不是所有新乐器必须复制的统一格式。
- 维护者接受不同美术风格；不强制钢琴窗。音频 / 反馈仍要符合实际声明的乐器和生命周期。
- 作者工程有新功能但当前 checkout 没有时，先说明差异，不造同名空类、不改旧规则凑通过、不把未发布代码的验证写成自己的结果。

## 可复用任务说明

```text
阅读 AGENTS.md、Docs/Developer/CURRENT_STATE.md 和对应任务政策。
目标：<乐器/修复/文档>；本阶段：<模型/音源/接入/完整>。
基线提交：<hash>；允许改动目标：<场景/Prefab/目录>。
先核实源码和既有修改，列出必须更新的调用链与资产引用。
保留 GUID、songId 和并行修改。未知数据标未测，不编造审批或测试。
按 Docs/Contributing/instrument-policy.json 填写提交与预算记录。
交付实现/接线/文档以及实际验证与未执行项；仅在授权范围内测试。
```

这是工作提示模板，不是额外的执行授权。
