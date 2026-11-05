# 编辑器工具与 Prefab 封装

编辑器管线将复杂 MIDI 处理和资源接线提前完成，让世界作者只操作文件、曲库和 Inspector。运行结构见 [架构](01-architecture.md)，数据细节见 [导入分册](02-data-and-import.md)。

## 分发 Prefab 显式同步（2026-11-05）

按用户本次明确要求，两份分发资源从旧单钢琴配置增量接入主场景的完整便携播放器：

- 共享七个活动旋律目标、两种钢琴外观、独立鼓组及 64 个物理声音源；停用目标 6／模型 7 继续隐藏。
- 当前 `selected-gm-v13-track-pitch` 路由，文件音色／音轨映射、轨名适配、逐轨开关、逐轨八度和自动音域；硬件 MIDI、PC／VR 输入接线同步。
- 两种钢琴的逐键命中合并网格及两套低负载粒子、现有共用 Shader 配色、最新运行 bank／能量数组、规则哈希和 catalogHash；音量平衡默认开启。
- 独立借物表只列分发曲库的 13 首作品，并保留新增乐器音源署名。普通控件、弹层和 Backplate 沿当前层级排序。

`UdonMidiPlayer.prefab` 默认外观 1（三角钢琴），`UdonMidiPlayer_Upright.prefab` 默认外观 0（数码钢琴）；默认勾选钢琴与鼓组。保留各自配置源、原有 13 首歌曲顺序及默认 songId、原钢琴对象、歌曲对象和 64 个 AudioSource；更新资源本体而非 Apply 场景实例覆盖。两份 Prefab 的 `.meta`／GUID 不变。场景专属扇形布局、海底环境／法阵／鱼群和历史钟楼表现不属于便携 Prefab。

保存后重新加载，两份均通过 `MidiBuildValidator.ValidateRoot` 和分发歌曲白名单检查；检查内部对象引用及 MIDI／UI／路由／bank backing 接线，修正保留三角钢琴椅子的索引为 1。保留钢琴的序列化数值及布局数组与主场景对应外观一致。Editor 隔离副本完成默认歌曲解码及路由绑定，并渲染检查两份主面板、乐器菜单和按音轨映射。报告：`Logs/DistributionPrefabSync-20261105.json`；截图：`Captures/DistributionPrefabs/` 中带 `(Clone)` 的六张 PNG。它们是 Editor 代理预览，不代表实际播放、网络同步或客户端验收；未运行 Play Mode、构建、打包或发布。

补齐配置器 `MidiPlayerBuilder.Configure` 的多乐器分支：跳过弦乐的空瀑布槽与八音盒的独立滚筒表现，不再对它们套用普通钢琴的 512 槽要求；保留八音盒 28 槽及机械 timing。C# 刷新完成，Udon 程序无编译错误。完整重应用配置复测被并行操作的海底场景 Play Mode 门控挡住，未绕过门控，也未将并行播放当作本次验收。最终保存资源的 backing 默认值、四个座椅索引、唯一 Manual Transport 及无场景依赖检查见 `Logs/DistributionPrefabSync-20261105-final.json`。

生成临时新组件时，UdonSharp 的延迟 `RunBehaviourSetup` 在副本卸载后产生一次 `MissingReferenceException`（SDK `UdonSharpEditorUtility.cs:931`）；已定位到编辑器延迟 hideFlags 回写，保存资源重新加载校验通过。Impact 网格沿用既有资源名，也出现主对象名与文件名不同的导入警告；未为消除工具警告修改 SDK 或共享资产。

本次操作未保存主／海底／历史钟楼／练习场景，不改变分发曲库成员。初次 2,113 项资产哈希审计仅两份 Prefab 改变，受保护的原对象 fileID 无丢失，见 `Logs/DistributionPrefabSync-20261105-identities.json`；后期海底场景被并行操作另行保存及播放，保留其修改与状态。以后加歌或修改场景仍遵守显式同步边界，不自动更新 Prefab。

## 1. 世界作者的接入流程

1. 将 .mid 放入项目的 Assets 源文件目录。
2. 打开 `Tools/Music MIDI Player/Import MIDI`，选择文件或文件夹并批量导入。
3. 创建/选择 `MidiLibraryAsset`，确认曲目标题、顺序、轨道设置和默认曲目。
4. 将 `UdonMidiPlayer.prefab` 拖入场景，在配置器中选择曲库与默认钢琴音源。
5. 点击“应用配置”，工具烘焙数据、生成列表并完成引用；无效配置显示定位到资源的诊断。
6. 在示例或目标场景验证播放；多人功能在双客户端环境验证。

源 MIDI 已由 SDK 的 .mid 处理机制导入，本项目的显式窗口读取源文件进行自己的解析，不与 SDK 注册冲突的扩展名导入器。

## 2. 计划资源与目录

```text
Assets/UdonMidiPlayer/
  Editor/
    Import/              MidiImportWindow 与解析适配器
    Assets/              MidiSongAsset / MidiLibraryAsset / PianoBankAsset 定义
    Baking/              运行数据烘焙、哈希与诊断
    Prefab/              配置器、接线与构建检查
    Generated/           生成的 Editor 曲目/曲库源资产
  Runtime/
    Data/ Logic/ Networking/ Audio/ Views/ UI/
  Prefabs/
    UdonMidiPlayer.prefab
  Audio/
    Piano/               允许分发的采样及来源说明
  Materials/
  Meshes/
  Samples/
    MidiPlayerDemo.unity
    MIDI/                自制或允许分发的验收曲与示例曲
```

项目自身的原始 MIDI 可放在其他 Assets 文件夹，不要求迁移已有资源。Editor 源资产不由运行组件持有；运行侧只引用烘焙的 `MidiSongData`、`MidiLibraryData`、`PianoBankData`、AudioClip 和表现资源。

Runtime 与 Editor 分别规划 `MusicMidiPlayer.Runtime.asmdef` 和 Editor-only 的 `MusicMidiPlayer.Editor.asmdef`；Runtime 另关联当前 SDK 的 U# Assembly Definition，M0 验证注册与跨类型引用。运行类型使用 `MusicMidiPlayer.Data/Logic/Networking/Audio/Views/UI` 对应的点分命名空间，Editor 使用 `MusicMidiPlayer.Editor.*`。生成模板、CustomEditor、资源菜单和 Shader 名称都遵循 [命名空间与分发约定](12-namespaces-and-distribution.md)，不依赖 asmdef 的 rootNamespace 自动包装现有源码。

## 3. 导入工具

批量窗口支持多选文件和递归文件夹、曲目预览摘要、轨道/通道选择、标题编辑、目标资产目录以及“导入/重新导入选中项”。单个文件失败不阻止其他有效文件生成资产；汇总明确成功、跳过、警告和失败数量。

每首曲目的摘要显示音符数、事件数、时长、音域、最大按键并发、踏板后的逻辑并发、tempo 变化数及数据体积估计。下列情况必须能定位：不支持格式、异常配对、超音域跳过、打击乐排除、未实现控制表达、尾部挂音修复、检查点过大、并发超过配置声音池。

导入先在临时内存结构完成全部解析和校验，成功后更新该曲目的生成资产。失败时保留上一份有效资产并标记“源文件更新未成功应用”，不把旧数据冒充最新数据。无成功资产的新曲目不进入曲库烘焙。

## 4. 重导入与稳定引用

用源文件 GUID 和已保存的导入映射找到原生成资产。重导入更新资产内容、contentHash 和摘要，保留生成资产 GUID、songId 与 `.meta`。源文件重命名/移动且 GUID 未变时继续识别；源文件删除后保留最后有效生成资产并显示来源缺失，作者显式移除曲库条目。

修改轨道设置或源内容只将相关曲目和引用它的曲库标记为脏。原型阶段通过显式“应用配置”烘焙；AssetPostprocessor 可用于标脏，不在每次文件刷新时触发全曲库重建。

## 5. 配置与烘焙

`MidiPlayerConfigWindow` 是 EditorWindow，可从菜单或 Facade 的自定义 Inspector 按钮打开；它不作为 MonoBehaviour 挂入场景或 Prefab。配置保存为 Editor 源资产 `MidiPlayerConfigurationAsset`，持有曲库、音源及表现配置；运行侧 MidiLibraryData 仅保存其 `configurationAssetGuid` 字符串，供 Editor 定位脏配置，不持有 Editor 资产对象引用。交付包显式包含配置源资产；运行端不通过该 GUID 加载资源。应用配置时完成：

1. 校验源资产、默认 songId、音源区域、采样和字体/材质。
2. 根据稳定 GUID 映射增量更新实例下的 Data 子对象，避免整体删除重建。
3. 写入各 `MidiSongData` 基础数组，生成 `MidiLibraryData` 查找数组。
4. 从 PianoBankAsset 烘焙 PianoBankData，包括 clip、区域、包络、maxReleaseMs 和 bankHash。
5. 计算 catalogHash，绑定唯一 Manual Sync Transport 与所有本地模块。
6. 设置固定声音池、可见音符池和 88 键布局，生成或更新曲目按钮。
7. 通过当前 UdonSharp Editor 的代理/序列化路径写入 Udon 数据；具体 API 签名由 M0 确认。
8. 保存改动并输出逐项诊断；不重存无关场景和资源。

配置操作支持 Unity Undo，记录目标场景/Prefab 脏状态。Prefab 资源本体与场景实例分开处理，实例的曲库和布局配置保存在实例覆盖中；无授权不 Apply Overrides 回写共享 Prefab。

## 6. 计划 Prefab 层级

```text
UdonMidiPlayer
  Network
    Transport           MidiNetworkTransport，Manual Sync
  Runtime
    Facade              MidiPlayerFacade，None
    ControlGate         MidiControlGate，None
    Clock               MidiTransportClock，None
    Sequencer           MidiSequencer，None
    Audio               PianoVoicePool，None
      Voice_000 ...     固定 AudioSource，无逐 voice UdonBehaviour
  Data
    Library             MidiLibraryData，None
    PianoBank           PianoBankData，None
    Song_<stableId>     每首 MidiSongData，None，无 Update
  Presentation
    Keyboard            88 键几何及 PianoKeyboardView
    Waterfall           MidiWaterfallView 与固定音符条池
    UI                  World Space Canvas、MidiPlayerUI、曲目列表
```

网络组件不与 None Behaviour 混在同一个 GameObject。EditorWindow 和自定义 Inspector 没有场景宿主，不将 Editor 程序集中的 MonoBehaviour 放进层级。数据对象保持有效引用且没有逐帧工作，不依赖 inactive 对象的启动时序。根对象移动/统一缩放只影响空间表现，不改变音高或播放速度。

## 7. 必须自动检查的接线

| 检查 | 失败处理 |
| --- | --- |
| 一个实例恰好一个网络 Transport，所有引用指向本实例 | 显式配置或导出检查报错，定位错误对象 |
| 曲目 ID 唯一、数组长度一致、事件和 CSR 索引合法 | 阻止烘焙，不写损坏数据 |
| catalogHash/bankHash 为当前配置 | 标记脏，要求重新应用配置 |
| 音源覆盖 A0～C8，采样非空，释放时长有效 | 阻止有效音源烘焙 |
| AudioSource 池数量、clip 设置、空间配置一致 | 自动修正本实例可控字段或明确报错 |
| 88 键、音符条、共享材质、Canvas/字体/回调齐全 | 阻止“配置完成”状态 |
| 无跨实例 UI/数据/时钟引用 | 阻止应用，提供定位 |
| UdonSharp 程序可编译，数据已写入运行载体 | 阻止发布成功提示 |

世界全局音频 real voices 预算、总音频内存及多个播放器同时播放属于警告和验收项目；工具不能擅自重写世界全局设置。资源许可缺失时作为音源交付缺项，不能把未提供的默认采样标为完整成品。

## 8. 构建前一致性

2026-11-02 起，`MidiBuildValidator` 不再在 VRCSDK 构建回调中执行全量播放器校验，也不因播放器检查或音频设置异常中止世界构建。完整检查保留在显式配置、资源维护及包导出调用中，也可通过 `ValidateOpenPlayers()` 手动运行；UI 摘要、外观和旧版本配置等问题不再成为日常构建门槛。SDK 构建回调仅保留项目显式启用的全局音频设置同步，失败时输出警告并继续。SDK 自身的编译和构建检查仍按原流程执行。

弦乐奏法校验按当前路由配置区分：有 `instrumentRouter` 时要求长弓、跳弓、拨弦、自动四项及四把提琴行内的独立入口；无路由的既有单选配置仍要求三项。选项的按钮、标签、标记和入口引用及回调一并检查。2026-11-01 修正旧校验固定要求三项导致主场景构建被误拦截的问题；不修改场景或分发 Prefab。

首版不自动下载解析依赖、音源或字体。解析依赖随 Editor 工具固定版本分发，运行构建不得带入其调用代码。音源、材质、网格、示例和许可清单随最终交付整理。

## 9. 多实例与接入验收

在同场景放两个播放器，分别配置不同曲库并确认控制、声音、视觉、Owner 与 UI 互不串线。两者 AudioSource 仍共享世界全局音频资源预算，不能各自假定拥有全部可闻 voice。

最终在空白 VRChat World 场景执行“拖入 Prefab → 选曲库 → 应用配置 → 播放”，检查无手工脚本接线、无缺失引用、无修改代码步骤。重导入、曲库重排和源文件移动后重复验证 songId 和场景引用保持稳定。

发布验收还须在独立的干净 VRChat 世界工程执行实际导入，检查完整命名空间、程序集注册、Editor-only 隔离、第三方依赖与 `.meta` 引用；与同短类名的其他组件共存时无全局冲突。后续升级沿用已发布的 namespace、程序集名和 GUID；命名迁移需单独验证旧场景及 Prefab 引用。

## 2026-11-02：音量平衡包络随用户音源导入更新

钢琴/马林巴源资产准备时自动生成 20 ms 均方能量包络，应用配置时复制到运行 bank；配置器检测旧源缺失包络会重新准备。弦乐/鼓组安装、重导入及 Apply Instrument Tone Changes 同步重新烘焙。包络计入音源哈希，并验证运行数组和 Udon 持久化数据；修改采样后沿用现有导入/应用配置流程，不需用户手工制作包络。日常 MIDI 导入和场景曲库更新仍不自动修改分发 Prefab。

“音量平衡”默认开启，可在播放器设置中本地关闭。算法、空间开销口径与显式迁移入口见[乐器与音源](13-instruments.md#2026-11-02基于采样能量包络的音量平衡)。

## 八音盒维护（2026-11-04）

`Tools > Music MIDI Player > Add or Update Music Box`仅操作已打开的主场景，保留现场备份与现有GUID，增量添加／更新bank、模型8、路由、输入和UI并保存。音源准备命令为`node Tools/prepare_music_box.cjs`；音频及清单校验后烘焙能量、bankHash和catalogHash。不重导曲库，不Apply分发Prefab，不改练习场景，许可白名单仍需独立审核。
