# 乐器接入技术矩阵与历史草案

## 当前贡献入口（2026-11-02）

正式贡献政策已由维护者确认，见[乐器贡献规则](../Contributing/README.md)、[资源预算](../Contributing/BUDGETS.md)和[机器可读政策](../Contributing/instrument-policy.json)。允许直接 PR 和分阶段贡献，正式收录由 passhu 决定；音源格式不统一锁定为现有某种规格。新贡献必须满足已确认硬上限并提供 PC VRChat 单客户端实测。

本页以下内容保留为分日期的技术接入位置与历史快照，**不是另一份准入政策**。旧“待审阅”问题、七目标 / 弦乐组有效槽、3 / 8 固定长度等说明不可直接作为当前实现；先核对[当前源码地图](../Developer/CURRENT_STATE.md)与实际 checkout。第 9 节之后记录的能量烘焙属于作者工程后续变化，不能据此推断私仓 rc.2 已有相应代码。

## 2026-11-01 路由实现补充

主场景已接入 MidiInstrumentRouter / MidiEnsembleVoicePool，乐器选择为共享复选集合，优先级采用七个稳定目标的 11 组完整排序＋家族默认＋例外。新增音色需更新目标登记、每组完整排序、分组/例外、规则哈希、烘焙与实际场景接线；不能只加菜单项。弦乐组保留五声部映射，每个音只使用一个分部。以下 2026-10-31 全局单音源链的静态快照保留为历史；当前规则优先第 19 册。


起草日期：2026-10-31。状态：**待用户审阅**。本文依据当前源码、音源清单和已保存主场景制定追加流程；“现状”是静态核对结果，“追加要求”是本次拟定规范。本次只修改文档与索引，没有执行导入器、更新场景、修改运行代码或进行 Unity / VRChat 验收。

状态快照：2026-10-31 23:33（Asia/Shanghai）。起草期间检测到并行源码更新，已见 CC11 链路及 Program / Bank Select 保存字段追加；本文保留这部分工作，不将字段存在当作完整音色路由已验收。后续实现优先复用届时已存在的数据与工具。

## 1. 当前实现基线

主场景为 `Assets/Scenes/VRCDefaultWorldScene.unity`。下表的索引是 `MidiPlayerFacade.selectedInstrument`，与 GM Program 编号无关。已保存场景含 8 个 `instrumentRoots` 和对应弦乐引用，默认选择 1；存在引用不等于已验证运行效果。

| 选项索引 | 乐器 / 外观 | 实际音源及发声链 | 静态状态 |
| --- | --- | --- | --- |
| 0 | 数码钢琴 | Salamander 四层钢琴 → `PianoBankData` / `PianoVoicePool` | 已有场景引用，与 1 共用同一 bank |
| 1 | 白色三角钢琴 | 同上 | 已有场景引用；仅换钢琴外观不重新起音 |
| 2 | 马林巴 | Iowa Yarn 单层 → `PianoBankData` / `PianoVoicePool` | 已有场景引用；60 个实录根音及派生 C8，保留完整音高 |
| 3 | 小提琴 | VSCO `SoloViolin` → `StringsBankData` / `StringsVoicePool` | 已有场景引用；专用 `StringsView` |
| 4 | 中提琴 | VSCO `ViolaSection` → 同上 | 已有场景引用；合奏录音，不称为独奏实录 |
| 5 | 大提琴 | VSCO `CelloSection` → 同上 | 已有场景引用；合奏录音 |
| 6 | 低音提琴 | VSCO `Contrabass` → 同上 | 已有场景引用；来自独奏录音 |
| 7 | 弦乐组 | 第一 / 第二小提琴、 中提琴、大提琴、低音提琴五声部 | 已有场景引用；两小提琴声部共用 `ViolinSection` bank |
| 独立伴奏模块 | GM 鼓组 | Virtuosity → `PercussionBankData` / `PercussionVoicePool` / `MidiDrumKitView` | 代码、bank 清单和素材已存在；本次读取的主场景 `voicePool.drums`、`clock.drums`、`facade.drumView` 仍为空，不标成场景已接通 |

弦乐当前有 5 个 bank、长弓 / 跳弓 / 拨弦 3 种奏法，音源清单记录 48 kHz、单声道、单力度层；长弓分离音头和循环主体。`instrumentBanks[3..7]` 指向钢琴 bank，是现有时钟和切换契约的占位引用；真正弦乐音源在 `voicePool.strings.banks`，不能据占位引用判断实际音色。

`StringsVoicePool.SectionFor` 在弦乐组模式下先用 `trackSections` 非负项，再用 `channelSections`；单件模式固定单声部。默认通道索引 0～4 对应五声部，其他通道默认第一小提琴，通道 9 排除。这是显式声部分配，**不是从 Program Change 推断配器**。

鼓组清单覆盖 note 35～81，含逐项替代信息；`MidiPercussionBuilder` 提供安装、烘焙和曲库打击乐重导入代码。默认 `MidiImportSettings.excludePercussion` 仍为 `true`。源码中的安装能力不能作为已运行安装、已重导入曲库或已验收的证据。

## 2. 追加前先确定类型与范围

每次新增先在对应专题文档填写一条登记：

| 登记项 | 必须说明 |
| --- | --- |
| 身份 | 稳定音源身份、中文 / 英文 / 日文显示名、实际 GM Program（若适用）、独奏 / 合奏 / 合成 / 替代 |
| 接入类型 | 只追加外观、复用采样 bank、专用持续音、独立打击乐，或新增 GM 路由能力 |
| 作用范围 | 本次更新主场景、独立练习系统、指定分发 Prefab 中的哪些对象 |
| 路由与优先级 | 整机手选、轨道 / 通道配置、自动 Program；是否与鼓组并存，未支持项如何回退 |
| 声音语义 | NoteOff、CC64、释放、循环、奏法、力度 / 表达、暂停与定位恢复规则 |
| 模型与输入 | 实体音域、运动部件、落点、PC / VR / MIDI 输入支持及域外显示 |
| 表现设计 | 说明发声部件高亮、粒子与演奏动作如何适配乐器，不强制钢琴窗 |
| 资源与预算 | 来源、固定版本、许可原件、实录 / 派生清单、物理源开销和测量口径 |

继续使用同一曲库、Facade、Transport、时钟和 Sequencer。新增外观若共用 bank，复用现有“不换音源、不重新起音”的切换路径。持续音、打击乐应复用各自专用结构；只有现有结构确实不能表达时才扩充数据与调度，不因类名中有 Piano 就重命名整个工程。

新增菜单项按现有索引末尾追加，保留 0～7 的意义及现有回调。当前 `GetStringsMode(index) == index - 3`、3 / 8 项检查和定长数组是实现边界，追加时按实际需要更新，不能把任意第 9 项误识别为现有弦乐模式。GM Program 到音源的映射使用 [0～127 音色 fallback 草案](19-gm-program-fallback.md)，不能直接用菜单索引代替 Program。

### 2.1 表现层按乐器适配，不强求钢琴窗

**2026-11-01 用户最新调整：取消“新乐器必须有钢琴窗”的要求。** 本节替代此前强制音符窗、前瞻 note 与直接落琴的规定。弦乐撤销该套表现，改为琴体固定、相应琴弦发光、发音附近的轻量粒子，并保留运弓。现有钢琴 / 马林巴的瀑布和鼓组的表现不因这次调整自动删除。

- **围绕乐器本体。** 用实际发声部件、接触点、运弓 / 拨弦 / 击打等适合的动作，让玩家辨认正在演奏的声部与发音状态。可选择高亮、粒子、形变或适合的音符展示，不统一套用钢琴窗。
- **控制动效。** 弦乐琴体保持固定；和弦可点亮多根实际琴弦，粒子集中在发音附近。优先轻量 Shader 与有上限的粒子池，不加逐音实时灯或无关的大幅漂移。
- **沿用视觉体系。** 保留曲目主色调与既有反馈风格。完整 MIDI 音高仍由声音链保留，域外音不强行映射到不存在的模型部件，也不要求另建窗显示；不重新生成实体 HitLine。
- **共用播放状态。** 演奏反馈跟随现有时钟、路由、音符状态与代次，覆盖文件及已支持的实时输入；暂停冻结、停止 / 切曲清理，定位 / 晚加入恢复当前状态。表现不反向触发音符或修改公共播放状态。
- **随功能落地。** 对应 View、Builder、资源、目标场景接线及文档同次更新。说明采用的反馈设计与预算，按任务允许范围检查实际渲染和状态切换；不以是否有钢琴窗作为验收条件。

## 3. 音源准备与烘焙要求

1. **保留来源证据。** 下载和离线生成脚本放 `Tools/`，研究原件放对应 `Downloads/`，正式资源放 `Assets/UdonMidiPlayer/Audio/<音源>/`。清单记录原路径、固定提交 / 版本、原始 SHA256、实录根音、力度边界、声道、采样率、增益、调音、释放、派生变调及输出 SHA256。许可与署名随实际使用的资源分发；本次不重新判定已有素材许可。
2. **按发声类型准备。** 钢琴 / 马林巴走 `PianoBankAsset` → `MidiDataBaker` → `PianoBankData`；弦乐走独立音头 / 循环 / 奏法及 `MidiStringsBuilder`；鼓组走 note / 力度 / 轮替 / lane 映射及 `MidiPercussionBuilder`。当前弦乐和鼓组直接从 JSON 烘焙，不要求为它们虚构一个已有的 Editor bank 资产类型。
3. **保持音高。** 有调性乐器接受 MIDI pitch 0～127，保持原音高，不丢音或折叠八度。`PianoBankData` 的 88 项选样表只是选样索引，域外仍以原 pitch 计算倍率；弦乐每奏法 128 项。按当前校验保证倍率不大于 3，必要时离线制作辅助根音并登记派生关系。鼓组 note 是鼓件身份，缺项不得套用最近音高算法。
4. **逐 bank 定义力度与持续音。** 当前钢琴四层边界为 44 / 69 / 100，取消额外力度幂衰减；马林巴沿用单层规则。`PianoVoicePool.VelocityGain` 仍以 `velocityLayerCount == 4` 识别钢琴特例，新四层音源若复用此类，必须显式处理增益语义，不能意外继承。弦乐目前是单层、力度幂增益；鼓组有独立力度分区与参考力度。不得逐层 / 逐音擅自归一化。
5. **保留资产身份并更新派生数据。** 原位更新既有资产、`.meta`、GUID 和对象引用；同时更新映射、峰值、帧数、释放上限、bankHash、catalogHash 及 Udon backing。修改 JSON 后只重算哈希不等于已重建运行数据。`Apply Instrument Tone Changes` 当前覆盖钢琴 / 马林巴重烘焙与弦乐哈希更新，不应当作鼓组映射重烘焙入口。

钢琴 / 马林巴继续采用既定 48 kHz 立体声 Ogg q4 源文件和 PCM / DecompressOnLoad 导入。新音源按自己的清单登记；当前弦乐为单声道 WAV，不为追求形式统一重新转成立体声或 Ogg。区分压缩源体积、解码缓冲估算、Editor 内存、构建下载和真实客户端内存。

## 4. 调度、恢复与物理声源预算

总预算沿用现有 **64 个物理 AudioSource**，PC 与移动端一致。当前钢琴 / 马林巴每音一个源；弦乐每逻辑音使用一对音头 / 主体源，最多 32 个逻辑音。鼓组代码在有歌曲鼓声或实时鼓声时预留末尾 16 源，旋律可用 48 源、弦乐最多 24 个逻辑音；这是源码分配上限，不是实机性能测量。新乐器不得另建一套 64 源绕过预算。

- 文件起音、DSP 预约、补帧、暂停 / 恢复、定位、切曲、晚加入和实时输入必须使用一致的音色选择与释放语义。预约任务带代次，停止 / 切换清除过期任务，不重复攻击音。
- 每次起音固定实际 bank、region、奏法、根音、增益与释放；后续 Program / 菜单变化不能让旧音松键时访问新音源的数据。手动切换是否迁移按住音沿用当前 Facade 的捕获 / 恢复策略。
- 鼓为 one-shot，NoteOff / CC64 不截断自然尾音，开镲被闭镲 / 脚镲收音；定位恢复不能复活已 choke 的尾音。轮替使用稳定事件身份，不能各端独立随机。
- 长音按实际年龄恢复循环相位，释放与曲终按所用音源计算。若扩展表达、Program 或 Bank Select，同步核对导入、编码、解码、恢复与实时快照。当前静态代码已见 CC11 导入时间线、压缩扩展 / 解码、`ExpressionAt`、弦乐音量表达及实时快照接点；本次没有验证运行效果。CC1 和完整 Program 自动路由不能由此推定已完成。
- 保留本地音量、静音、3D / 2D、默认开启且可本地关闭的音量平衡及既定空间落点；不让表现层修改播放权威状态或新增逐音网络广播。

## 5. 追加时必须检查的代码位置

下表路径相对 `Assets/UdonMidiPlayer/`。按本次改动选择相关行；未涉及的项记录“不适用及原因”，不为勾表扩大修改范围。

| 变更内容 | 检查 / 更新位置 | 完成标准 |
| --- | --- | --- |
| 音源结构 / 选样 | `Editor/Assets/PianoBankAsset.cs`、`Editor/Baking/MidiDataBaker.cs`；`Runtime/Data/{Piano,Strings,Percussion}BankData.cs` | 字段、数组覆盖、合法性、峰值和哈希一致 |
| 资源生成与安装 | `Editor/Prefab/SelectedToneBankImporter.cs`、`MidiInstrumentMigration.cs`、`MidiStringsBuilder.cs`、`MidiPercussionBuilder.cs`；项目 `Tools/` 对应脚本 | 可增量重建运行数据；保留 GUID；明确命令的场景保存和曲库重导入副作用 |
| 音符路由 / 生命周期 | `Runtime/Audio/{Piano,Strings,Percussion}VoicePool.cs`、`Runtime/Logic/MidiSequencer.cs`、`MidiTransportClock.cs` | 调度、恢复、尾音、满载与 64 源共享一致 |
| 选项 / 模式 / 输入 | `Runtime/Logic/MidiPlayerFacade.cs`、`MidiPerformanceInput.cs`、`MidiPracticeController.cs`；`Editor/Prefab/MidiPerformanceBuilder.cs` | 选项数组长度、HasInstrument、切换、持续音、练习和输入入口有效 |
| MIDI 内容格式 | `Editor/Import/{SmfParser,MidiImportSettings,MidiImportService}.cs`、`Editor/Assets/MidiSongAsset.cs`、`Editor/Baking/{MidiSongCodec,MidiDataBaker,MidiContentHash}.cs`、`Runtime/Data/MidiSongData.cs` | 仅新增事件 / 字段时修改；编码版本、旧曲兼容、重导入和身份保护一起设计 |
| 网络与配置指纹 | `Runtime/Networking/MidiNetworkTransport.cs`、`Editor/Prefab/MidiInstrumentMigration.InstrumentCatalogHash` | 公共选项 / 奏法 / 路由配置进入快照与指纹，晚加入可恢复；本地显示偏好不乱入公共状态 |
| 模型与表现 | `Runtime/Views/` 对应 View、`Editor/Prefab/` 对应 Builder | 按第 2.1 节交付适合乐器的部件高亮、轻量粒子与动作，暂停冻结、停止复位、域外不驱动不存在的模型 |
| UI 与翻译 | `Runtime/UI/MidiPlayerUI.cs`、`Editor/Prefab/MidiPlayerUIBuilder.cs`、`MidiPlayerLocalizationBuilder.cs`、`Localization/World/{en,zh-Hans,zh-Hant,ja}.json`、必要的 Editor 译文 | 按现有风格接按钮、可用性与文字；层级更新后遵守 ConfigureHierarchy |
| 校验与分发 | `Editor/Prefab/MidiBuildValidator.cs`、`MidiPlayerBuilder.cs`、`MidiPackageExporter.cs` | 更新 3 / 8 选项等固定假设、bank / backing 接线与依赖；仅在明确要求分发时更新目标 Prefab |

## 6. 同次更新的文档

| 文档 | 更新条件与内容 |
| --- | --- |
| [本文](18-adding-instruments.md) | 新增实现后修订现状清单、追加入口及硬编码边界 |
| [GM fallback](19-gm-program-fallback.md) | 新音源覆盖的 Program、精确 / 近似状态、优先级及缺失回退；更新全部受影响行，不只新增一个名字 |
| [13 乐器与音源](13-instruments.md) | 菜单索引、音源版本、操作命令、当前规格和预算 |
| [弦乐](14-strings-preparation.md) / [鼓组](14-percussion-preparation.md) 或新专题 | 素材清单、映射、模型、发声语义、替代关系、实际接入和验证状态 |
| [CONTRACTS](CONTRACTS.md)、[数据与导入](02-data-and-import.md)、[多人同步](04-multiplayer-sync.md) | 仅在字段、格式、恢复或公共状态变更时，同步契约和受影响链路 |
| [控制与 UI](07-controls-and-ui.md)、[编辑器与 Prefab](08-editor-and-prefab.md)、[UI 层级](15-ui-hierarchy.md) | 入口、安装副作用、层级或接线发生变化时更新 |
| [资源使用说明](../../Assets/UdonMidiPlayer/README.md)、[本地化说明](../../Assets/UdonMidiPlayer/Localization/README.md) | 面向使用者的能力、接入步骤或翻译规则发生变化时更新 |
| [文档总览](README.md)、[维护约定](17-maintenance-conventions.md)、根目录 [AGENTS.md](../../AGENTS.md) | 维护导航与规则入口；AGENTS.md 只保留核心约束与目录，不复制详细乐器规范或 128 行表 |

## 7. 验证与交付记录

本节供未来实施按任务范围选用，本次起草不执行构建、安装、场景重存或运行测试。

- 数据：新增选项、音源哈希、许可清单、力度边界、pitch 0 / 21 / 60 / 108 / 127、各奏法映射；鼓组另外核对 note 35～81、未知 note 和替代项。
- 行为：文件与实时输入、按住 / 踏板、播放和暂停中切换、定位、停止 / 切曲、尾音、预约代次、密集复音及混合声源预算。GM 路由另外按第 19 册核对全部 128 项及缺失回退。
- 表现与接线：按第 2.1 节核对乐器部件、高亮、粒子与演奏反馈，以及模型部件、暂停 / 复位、菜单 / 翻译 / 射线层级、proxy / backing 一致。生成器修改必须落到本次指定对象。
- 记录区分静态检查、Editor、backing Udon、ClientSim、真实 VRChat、硬件 MIDI、听音及双客户端；未做的写明未做。不能用源码存在或旧报告替代本次验收。

交付记录建议包含：本次音源与目标对象、受影响 Program / 回退、变更文件、实际执行的工具与检查、预算测量口径、仍待审阅 / 验证项。场景曲库和分发曲库继续分开维护，不因追加乐器自动 Apply 实例覆盖或同步歌曲。

## 8. 2026-10-31 历史审阅问题（不再作为当前决策入口）

1. 是否采用“保持手选模式，GM 自动路由作为单独后续能力”的边界。
2. 是否接受第 19 册按现有音源制定的近似替代，以及无原效果的占位项。
3. 是否采用本册“相关代码与文档同次更新”的交付要求。

本文的实现依据为所链接的源码和项目清单；鼓组与弦乐原准备文档中的“尚未实现”仅代表当时的调研阶段。后续追加应以当时最新源码、清单和指定目标对象重新核对。

## 9. 静态核对入口

- 选项与切换：[MidiPlayerFacade](../../Assets/UdonMidiPlayer/Runtime/Logic/MidiPlayerFacade.cs)；已保存接线：[主场景](../../Assets/Scenes/VRCDefaultWorldScene.unity)。本次未用 Editor 内存状态代替场景文件。
- 音源与发声：[PianoBankData](../../Assets/UdonMidiPlayer/Runtime/Data/PianoBankData.cs)、[PianoVoicePool](../../Assets/UdonMidiPlayer/Runtime/Audio/PianoVoicePool.cs)、[StringsVoicePool](../../Assets/UdonMidiPlayer/Runtime/Audio/StringsVoicePool.cs)、[PercussionVoicePool](../../Assets/UdonMidiPlayer/Runtime/Audio/PercussionVoicePool.cs)。
- 当前准备清单：[钢琴 / 马林巴](../../Assets/UdonMidiPlayer/Audio/SelectedQ4/BANK-MAPPING.json)、[弦乐](../../Assets/UdonMidiPlayer/Audio/Strings/BANK-MAPPING.json)、[鼓组](../../Assets/UdonMidiPlayer/Audio/VirtuosityDrums/BANK-MAPPING.json)。
- 烘焙与边界：[MidiStringsBuilder](../../Assets/UdonMidiPlayer/Editor/Prefab/MidiStringsBuilder.cs)、[MidiPercussionBuilder](../../Assets/UdonMidiPlayer/Editor/Prefab/MidiPercussionBuilder.cs)、[MidiInstrumentMigration](../../Assets/UdonMidiPlayer/Editor/Prefab/MidiInstrumentMigration.cs)、[MidiBuildValidator](../../Assets/UdonMidiPlayer/Editor/Prefab/MidiBuildValidator.cs)。

2026-11-02：新增或重导入音源须通过 `AudioEnergyBaker` 生成 20 ms 的 `energyStarts/energyCounts/energyValues`，在实际源时间轴上取能量，并接入现有共享压缩系数。包络纳入 bank 哈希与构建前 backing Udon 数组校验，不新增逐帧 PCM 读取；持续音循环、变调、释放和未来预约分别按第 13 册处理。
