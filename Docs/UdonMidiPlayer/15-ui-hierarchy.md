# UI 层级与绘制顺序

## 2026-11-02 曲库目录归类

uaaaak 的 425 首归入 `游戏音乐/uaaaak`；主场景复用原目录按钮对象，更新其路径、父目录索引、对象名称、游戏音乐子目录计数及搜索关键词，并同步 proxy / backing Udon 数据。进入、返回与搜索沿用既有逻辑。《Patchwork Staccato（ツギハギスタッカート）》与用户再次提供的文件哈希一致，已在 `VOCALOID/初音ミク`，保持唯一曲目及原 ID。完整曲库仍为 547 首，分发 Prefab 不变。本次核对 Editor 序列化数据与源 MIDI，未进行真实 VRChat 验收。

## 2026-11-01 四把提琴独立奏法入口

弓法入口已移到各自提琴行右侧：`InstrumentPanel/Content/Viewport/Options/Violin/Entry`、`Viola/EntryViola`、`Cello/EntryCello`、`Contrabass/EntryContrabass`。复用原对象、引用及回调，各有 Label / Chevron；标签只显示当前奏法。未勾选提琴的入口可见但禁用。每行左侧 146 单位为复选输入，右侧 108 单位为奏法按钮，两者不重叠。

底部不再堆放奏法行，主场景面板高 296、Viewport 高 280，七种乐器完整显示。`Content/StringsStyle` 只保留最后绘制的共享 `Options`，不在 Viewport 裁剪内；展开时按当前行的世界坐标换算位置，列表在该行上方展开。`Style0..2` 及 `StyleAuto` 身份与回调保留，视觉顺序为自动、长弓、跳弓、拨弦。选择后收起，取消当前目标勾选时关闭。普通控件 / 弹层 / Backplate 排序仍由 ConfigureHierarchy 统一；主场景 proxy / backing 引用同次更新。原路径图中的底部 StringsStyle 单入口为历史结构，以上述结构为准。

本次编译通过；主场景实际 backing Udon、原生 Button 和 GraphicRaycaster 的 39 项布局 / 交互检查通过，包括未启用时禁用、四行归属、左右点击分离、四个目标下拉及全部选项命中、选择后关闭及裁剪边界。报告 `Logs/inline-string-styles-ui.json`，中文实际渲染见 `Captures/InlineStringStyles/Menu-zh.png`、`Viola-Picker-zh.png`。并行音频修改触发重载期间未把空 VM 状态当作运行结果，待音频修改完成并重启 Play Mode 后完成上述检查；未进行真实 VRChat 验收。

## 2026-11-01 播放音色映射与提琴音域

`UI/FileMappingEntry` 位于当前播放区标题行右侧，直接打开 `UI/FileMappingPanel`，不经过 SettingsPanel。`NowPlayingCaption` 收窄以留出按钮空间。弹层包含 Title、Hint、Back、Program0～15（Source、Value）、Reset、Previous、Page、Next，最后绘制的 TargetPicker/Content 含 Title 和 Target0～6（自动＋六种乐器）。所有目标均可选，选择时由 Facade 自动启用未打开的目标；不复用硬件设置的选择列表状态。

音色弹层与乐器、语言、设置四者互斥；普通控件先绘制，再按 InstrumentPanel → LanguagePanel → SettingsPanel → FileMappingPanel 排序，Backplate 仍最后且不接收射线。创建/更新沿用 ConfigureHierarchy。生成入口仍由 MidiHardwareMappingUIBuilder 复用现有样式；该类只复用生成基础工具，文件映射 UI、权限和公共状态与硬件 MIDI 设置独立。

乐器列表只显示原八个稳定槽中的前七行（两种钢琴外观、马林巴、四把提琴）。旧 Strings 行和弦乐组舞台根停用，保留其身份与引用；滚动内容和视口按七行计算。硬件 MIDI 普通/高级选项也不再显示弦乐组。

## 2026-11-01 MIDI 演奏的普通玩家入口

设置标题栏原 `MidiMappingEntry` 保留对象身份及回调，但本地化显示“MIDI 演奏”，默认打开新 `SettingsPanel/MidiPerformance`。子页包括 Title、Back、Prompt、Hint、Instrument0～6（Label、Selected）、Rescan、Advanced。等待时只提示按键；官方硬件 NoteOn 捕获首个有效旋律输入后，展示已启用乐器的紧凑两列选项，选择后直接显示已设置状态。普通流程不显示通道编号，也不提供自动选项；自动路由保留在高级页。

Advanced 打开既有 `SettingsPanel/MidiMapping`，Back 返回 MIDI 演奏；普通页 Back 返回设置。关闭设置统一关闭普通页、高级页和 TargetPicker。原高级行、路径和回调不重建；`MidiHardwareMappingUIBuilder` 同时生成两种视图。等待／识别／完成、重新识别、高级入口及暂停演奏提示均走既有中英日语言表。玩家数据恢复后可显示上次选择，不自动更改公共已启用集合。

## 2026-11-01 游戏内 MIDI 通道映射

`SettingsPanel/Content/MidiMappingEntry` 位于设置标题栏，标题宽度缩为200，保留 Close 和其余原路径。高级页 `SettingsPanel/MidiMapping` 包含 Title、Back、Channel1～16（Channel、Value）；通道分两列显示，第10通道显示固定鼓组并禁止修改。其最后子节点 `TargetPicker` 为点击外部关闭的遮罩，`Content` 含 Title 和 Target0～7（Label、Selected）。自动选项始终存在，其他选项仅显示已启用目标；选项数变化时压缩列表高度。选中后只关闭选择列表，Back 返回 MIDI 演奏页；该硬件设置仍属于原设置弹层，播放音色映射另使用 FileMappingPanel。

生成入口为 `MidiHardwareMappingUIBuilder.Configure`，由 `MidiPlayerUIBuilder.ConfigureSettings` 调用；当前主场景增量安装。复用既有设置的背景、按钮状态、字体和选中标记，生成后执行 `ConfigureHierarchy`。动态文字由 `MidiPlayerUI` 通过既有 Tr 和四份 World JSON 刷新，英／中／日三个语言入口不变。修改映射仅调用 Facade 的本地设置方法，不申请 ownership、不改变播放状态或公共乐器集合。

## 2026-11-01 乐器行复选框更新

保留 InstrumentPanel/Content/Viewport/Options 下原来的八个稳定槽、Button 组件和回调。前七行显示，第八行停用。每行增加 Checkbox 底框及 CheckboxToggle 透明输入子节点（原生 Toggle，无 ToggleGroup）；原 Button 关闭 enabled，保留引用与既有 onClick。Selected 改为方形勾选填充，Label 向右留出方框空间。Toggle.onValueChanged 仍发既有 _Select… 本地事件，RefreshState 通过 SetIsOnWithoutNotify 更新状态，避免回调循环。勾选后不收起弹层；StringsStyle 在任意弦乐被选时显示。生成后继续调用 ConfigureHierarchy，Backplate 和弹层绘制规则不变。


核对日期：2026-10-31。以下以主场景 `Assets/Scenes/VRCDefaultWorldScene.unity` 的实际对象和当前生成代码为准。本文说明当前实现；[07 控制与 UI](07-controls-and-ui.md) 说明交互与状态职责。

## 入口与空间归属

- 主控制面板：`UdonMidiPlayer/Presentation/UI`。根节点包含 World Space Canvas、CanvasScaler、GraphicRaycaster、VRCUiShape、触发用 BoxCollider、背景 Image、Shadow 及 `MidiPlayerUI`。
- Canvas 大小为 1000 × 480，pivot 为 (0.5, 0.5)。位置、缩放沿用场景或配置，不用调整 Z 来修复同一 Canvas 内的覆盖关系。
- 开场曲名使用独立 Canvas，位于当前钢琴/马林巴的 `ViewRoot/Waterfall/SongOpening`，子节点为 `Title`、`Author`；选择弦乐时随现有开场对象移动至相应弦乐表现根。单乐器 Prefab 使用其自身的 `ViewRoot/Waterfall/SongOpening` 路径。
- Facade 的 `playerUI` 引用主面板；开场文字跟随当前乐器的瀑布表现。播放与网络状态仍由 Facade/Transport 管理。

## 主面板直接子节点

下面按主场景保存的 sibling 顺序列出。相同 Canvas 中，后面的 Graphic 通常后绘制、覆盖前面的 Graphic；背景固定在前，所有普通控件位于弹层之前。

```text
UI
├─ LibraryPanel                 曲库底板
├─ NowPlayingPanel              当前播放底板
├─ Brand
├─ SongTitle
├─ Status
├─ Time
├─ Play                         Icon、Label
├─ Pause                        Icon、Label
├─ Stop                         Icon、Label
├─ Progress                     Track、FillArea/Fill、HandleArea/Handle
├─ VolumeLabel
├─ Volume                       Track、FillArea/Fill、HandleArea/Handle
├─ Mute                         Check、Label
├─ BrandIcon
├─ LibraryName                  主场景乐器入口启用时隐藏
├─ HeaderDivider
├─ NowPlayingCaption
├─ StatusDot
├─ VolumeIcon
├─ LibraryCaption
├─ TrackCount
├─ LibraryScroll                结构见下文
├─ LibraryBack                  返回按钮
├─ LibraryPath                  当前位置
├─ LibraryDivider
├─ Language                     Icon，语言入口
├─ Settings                     Icon，设置入口
├─ Instrument                   Label、Chevron；收起时显示当前乐器的下拉条
├─ LibrarySearch                TextArea/{Text,Placeholder}、Clear/Label
├─ InstrumentPanel              乐器弹层，默认关闭
├─ LanguagePanel                语言弹层，默认关闭
├─ FileMappingEntry             当前播放区音色映射入口
├─ SettingsPanel                设置弹层，默认关闭
├─ FileMappingPanel             当前曲目的音色映射弹层，默认关闭
└─ Backplate                    背面遮挡，始终最后
```

普通控件之间不依赖固定数字索引；新增控件后应重新执行 `MidiPlayerUIBuilder.ConfigureHierarchy()`。单乐器 Prefab 可以没有 `Instrument`/`InstrumentPanel`，排序函数会跳过不存在的节点。

乐器下拉复用原选项对象和回调，迁移到 `Content/Viewport/Options` 以支持竖向列表裁剪；当前七项可同时显示，超出视口时滚动。弦乐奏法条位于列表下方，三项子下拉向上展开。`StringsStyle` 必须在 `Viewport` 后绘制，防止子下拉被乐器行遮住；选择奏法后只收起子列表，点击主弹层外部关闭两层。

## 曲库与弹层内部结构

```text
LibraryScroll                   ScrollRect；射线由根 Canvas 的 GraphicRaycaster 处理
├─ Viewport                     RectMask2D
│  ├─ Songs                     ScrollRect.content
│  │  ├─ Song_<songId>          Button、MidiSongButton
│  │  │  └─ Number、Label、Duration、Selected
│  │  ├─ AllSongs              全部曲目入口
│  │  └─ Folder_<escapedPath>   Button、MidiLibraryFolderButton
│  │     └─ Icon、Label、Count、Chevron
│  └─ Empty                    空目录/无搜索结果提示
└─ Scrollbar
   └─ SlidingArea/Handle

InstrumentPanel                 透明全屏点击关闭层
└─ Content                      下拉面板，阻止点击穿透
   ├─ Title                     保留引用，隐藏
   ├─ Viewport                  RectMask2D
   │  └─ Options                ScrollRect.content，竖向选项
   │     └─ DigitalPiano、GrandPiano、Marimba、Violin、Viola、Cello、Contrabass、Strings
   │                            每项包含 Label、Selected
   └─ StringsStyle              仅选中弦乐时显示
      ├─ Entry                  当前奏法下拉条，Label、Chevron
      └─ Options                奏法列表，默认关闭
         └─ Style0…2            长弓、跳弓、拨弦；Label、Selected

LanguagePanel                   透明全屏点击关闭层
└─ Choices
   ├─ Option_en                 Icon、Selected
   ├─ Option_zh-Hans            Icon、Selected
   └─ Option_ja                 Icon、Selected

SettingsPanel                   全屏设置层，含关闭事件
├─ Content                      左列通用设置
│  ├─ Title、Close/Label、MidiMappingEntry/Label
│  ├─ NoteLandingLabel
│  ├─ CurrentLanding、KeyRootLanding
│  ├─ StickScrollSensitivityLabel、StickScrollSensitivityValue
│  ├─ StickScrollSensitivity    Track、FillArea/Fill、HandleArea/{StepDots,Handle}
│  ├─ StickScrollMinimum、StickScrollMaximum
│  ├─ AudioSourceLabel、WorldAudio、TwoDAudio
│  └─ MeasureSnap、PeakProtection、DrumVisibility
├─ Performance                  右列演奏设置
   ├─ KeyMap                    隐藏的旧节点，保留现有引用
   ├─ VR
   │  ├─ Hand0、Hand1、Title、Left、Right
   │  └─ Finger0…9、FingerHint、SelectAll、ClearAll
   └─ Desktop
      ├─ DesktopTitle、Help、Hint
      └─ WhiteKey0…10、BlackKey0…6
├─ MidiMapping                  默认隐藏的高级通道子页
   ├─ Title、Back/Label
   ├─ Channel1…16               Channel、Value
   └─ TargetPicker              选择遮罩，默认隐藏
      └─ Content                Title、Target0…7/{Label,Selected}
└─ MidiPerformance              普通玩家的 MIDI 演奏子页
   ├─ Title、Back/Label、Prompt、Hint
   ├─ Instrument0…6             Label、Selected；仅显示已启用乐器
   └─ Rescan/Label、Advanced/Label
```

曲目和文件夹数量随曲库变化，不固定为主场景当前数量。`Songs` 下条目的显示与位置由 `OpenLibraryFolder()` 更新；曲库搜索覆盖整个曲库。设置中的 VR/Desktop 区域由演奏输入组件按设备激活，编辑状态下两者可能均关闭。

## 绘制与点击规则

1. `LibraryPanel`、`NowPlayingPanel` 始终最先，不能覆盖内容。
2. 曲库列表、搜索框、输入框动态生成的 caret、普通按钮与文字均位于弹层根节点之前。新增普通控件后必须统一排序，不能只在某个弹层创建时调用 `SetAsLastSibling()`。
3. 正面弹层顺序为 `InstrumentPanel → LanguagePanel → SettingsPanel → FileMappingPanel`；运行时四个入口互相关闭其他弹层，同一入口再次点击可关闭自身。
4. 弹层根 Image 接收射线。透明区域也应拦截底层按钮并执行关闭；不透明 Content 的 Button 接住面板空白区域，避免误触底层。文字、图标、选择标记和背板不接收射线。
5. `Backplate` 使用 `Assets/UdonMidiPlayer/Materials/UIBackplate.mat`，Shader 为 `MusicMidiPlayer/UI Backplate`，`Cull Front`、`ZWrite Off`。它最后绘制，从背面遮住镜像文字及 TMP fallback 子网格，从正面剔除；`raycastTarget=false`。不能因为名称含“背板”就把它移到第一位。
6. 保持现有父子路径、GUID、组件引用与按钮回调。不要为了排序增加中间容器；本地化和生成代码中有 `transform.Find("SettingsPanel/Content/…")` 等路径依赖。

## 维护入口

| 文件 | 职责 |
| --- | --- |
| `Assets/UdonMidiPlayer/Editor/Prefab/MidiPlayerUIBuilder.cs` | `Build` 生成主面板；`ConfigureHierarchy` 统一背景/弹层/背板顺序；搜索生成、乐器生成、设置与背板更新都会归一层级 |
| `Assets/UdonMidiPlayer/Editor/Prefab/MidiPlayerLocalizationBuilder.cs` | 语言入口及选项、本地化标签接线；语言弹层生成后统一排序 |
| `Assets/UdonMidiPlayer/Editor/Prefab/MidiPerformanceBuilder.cs` | 设置右列 VR/PC 演奏布局与设备可见性接线 |
| `Assets/UdonMidiPlayer/Runtime/UI/MidiPlayerUI.cs` | 弹层互斥、搜索/目录导航、滑条与命令、本地化及状态显示 |
| `Assets/UdonMidiPlayer/Shaders/UIBackplate.shader` | 背面遮挡的剔除与绘制规则 |

增量更新保留现有对象身份和空间位置，只在生成结束后归一 sibling 顺序。主场景为展开的对象，修改生成器不会自动修复已有场景，须同步更新实际 UI；Prefab 只有确实需要变化时才保存。

## 本次修复与核对范围

修复前 `InstrumentPanel` 位于 `LibrarySearch` 之前，打开乐器菜单时搜索框绘制在选项上方。修复后所有普通控件位于四个弹层之前，背面遮挡仍保持最后；当前主场景已保存。两个现有单乐器 Prefab 顺序正确，未重存。

UI 修改后的首次 Unity C# / UdonSharp 编译检查无错误；Editor 相机分别渲染默认面板、三个弹层和背面；乐器菜单与搜索框重叠处射线命中 `GrandPiano`，没有穿透到搜索框；Editor 代理方法的弹层互斥及重复排序稳定性通过。截图保存在 `Captures/UIHierarchy/`。这些是 Editor 检查，不代表 VRChat 客户端或 VR 射线实机验收。

最终复查期间工程内另有弦乐相关文件更新，控制台出现 `PianoVoicePool.cs(14,16): CS0246 StringsVoicePool`；后续刷新未在等待窗口内完成。该引用不属于本次 UI 修改，未改动弦乐代码；不能据首次编译检查宣称当前全工程编译通过。

本次播放音色映射 UI：中英日三种实际渲染已核对，主入口、原音色行、目标按钮、选择遮罩和标题阻挡共15项 GraphicRaycaster 检查通过；128音色分页覆盖8页及上下界。截图见 `Captures/PlaybackMapping/`；为 Editor/ClientSim 检查，非真实 VR 射线验收。
