# 编辑器工具与 Prefab 封装

编辑器管线将复杂 MIDI 处理和资源接线提前完成，让世界作者只操作文件、曲库和 Inspector。运行结构见 [架构](01-architecture.md)，数据细节见 [导入分册](02-data-and-import.md)。

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
| 一个实例恰好一个网络 Transport，所有引用指向本实例 | 阻止应用/构建，定位错误对象 |
| 曲目 ID 唯一、数组长度一致、事件和 CSR 索引合法 | 阻止烘焙，不写损坏数据 |
| catalogHash/bankHash 为当前配置 | 标记脏，要求重新应用配置 |
| 音源覆盖 A0～C8，采样非空，释放时长有效 | 阻止有效音源烘焙 |
| AudioSource 池数量、clip 设置、空间配置一致 | 自动修正本实例可控字段或明确报错 |
| 88 键、音符条、共享材质、Canvas/字体/回调齐全 | 阻止“配置完成”状态 |
| 无跨实例 UI/数据/时钟引用 | 阻止应用，提供定位 |
| UdonSharp 程序可编译，数据已写入运行载体 | 阻止发布成功提示 |

世界全局音频 real voices 预算、总音频内存及多个播放器同时播放属于警告和验收项目；工具不能擅自重写世界全局设置。资源许可缺失时作为音源交付缺项，不能把未提供的默认采样标为完整成品。

## 8. 构建前一致性

“应用配置”是作者可见的主要操作；构建前回调复查配置是否脏、引用和运行数据是否一致。构建回调使用当前 SDK 的受支持接口，在 M0 验证；若检查失败，输出定位信息并阻止带过期数据的构建，不在构建中悄悄修改无关资源。

弦乐奏法校验按当前路由配置区分：有 `instrumentRouter` 时要求长弓、跳弓、拨弦、自动四项及四把提琴行内的独立入口；无路由的既有单选配置仍要求三项。选项的按钮、标签、标记和入口引用及回调一并检查。2026-11-01 修正旧校验固定要求三项导致主场景构建被误拦截的问题；不修改场景或分发 Prefab。

首版不自动下载解析依赖、音源或字体。解析依赖随 Editor 工具固定版本分发，运行构建不得带入其调用代码。音源、材质、网格、示例和许可清单随最终交付整理。

## 9. 多实例与接入验收

在同场景放两个播放器，分别配置不同曲库并确认控制、声音、视觉、Owner 与 UI 互不串线。两者 AudioSource 仍共享世界全局音频资源预算，不能各自假定拥有全部可闻 voice。

最终在空白 VRChat World 场景执行“拖入 Prefab → 选曲库 → 应用配置 → 播放”，检查无手工脚本接线、无缺失引用、无修改代码步骤。重导入、曲库重排和源文件移动后重复验证 songId 和场景引用保持稳定。

发布验收还须在独立的干净 VRChat 世界工程执行实际导入，检查完整命名空间、程序集注册、Editor-only 隔离、第三方依赖与 `.meta` 引用；与同短类名的其他组件共存时无全局冲突。后续升级沿用已发布的 namespace、程序集名和 GUID；命名迁移需单独验证旧场景及 Prefab 引用。
