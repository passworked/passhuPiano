# UI 层级与绘制顺序

## 2026-11-05 分发 Prefab 同步

两份旧单琴 Prefab 已按本次明确要求增量接入主场景的乐器多选、鼓组开关、弦乐奏法、硬件 MIDI 映射和 `FileMappingPanel`，包括 `ByProgram`／`ByTrack`、逐轨开关、八度及自动音域。既有 UI 根位置与分发歌曲对象保留；沿 `ConfigureHierarchy()` 排序，普通控件在弹层之前，Backplate 最后绘制。独立 `Presentation/Credits` 绑定当前播放器并只列其 13 首分发歌曲。两份主面板、乐器菜单与按音轨映射已通过 Editor 相机渲染检查；未进行真实 VR 射线或客户端验收，见[第08册](08-editor-and-prefab.md#分发-prefab-显式同步2026-11-05)。

## 2026-11-05 海底场景蓝色主题

海底场景全部 UI 按播放器预制体恢复 VizVid 蓝灰／青蓝色系，包含弹层、借物表和作者／赞助／社群板；完整控制台 Canvas 前移约37.2 mm，避免支柱穿过静音区域，最小净距25 mm。维护入口为 `UnderwaterUIThemeBuilder.Install()`，不 Apply 共享预制体，不改控件层级、回调或曲库；范围及验证见[第27册](27-underwater-music-garden.md#蓝色-ui-与控制台间隙2026-11-05)。下方绿金主题仅保留为历史钟楼约定。

## 2026-11-04 钟楼绿金主题

按最新要求，钟楼场景的 `Presentation/UI` 与 `Presentation/Credits` 改为统一绿金风格：面板 `#143E32`、次级卡片 `#1C4A3D`、内嵌底 `#09261E`，金色强调／描边 `#D8B96A`，正文暖白 `#F6EDD5`，次级文字 `#C5C9A8`。主按钮、链接、进度填充、复选标记、全部未启用弹层、输入框插入符／文字选区与 Selectable 的悬停／按下／选中／禁用状态同步调整；原 Logo／旗帜图像保持原色。

维护入口 `ClockworkUIThemeBuilder.Install()`，菜单 `Tools/Music MIDI Player/Scene Art/Apply Clockwork Green Gold UI Theme`。只更新当前钟楼场景组件序列化颜色，增加两块面板的金色 Outline；无运行时换色循环、不修改共享材质和原 UI 生成器的其他场景默认配色。重新生成控件后可再次运行此入口。位置、层级、回调、许可文案与数据不变，Backplate 继续最后绘制且使用原正面剔除材质。主场景及两份播放器 Prefab 不自动同步。

Editor 渲染见 `Captures/ClockworkUITheme/`，覆盖主面板、许可面板与设置弹层；色板／位置／单点光源检查见 `Logs/ClockworkUITheme/validation.json`，受保护资产前后哈希见 `Logs/ClockworkUITheme-protected-*.json`。本轮未执行 Play Mode、真实 VRChat 客户端或构建。

## 2026-11-04 独立作者／赞助／社群板

新增资产 `Assets/UdonMidiPlayer/CommunityBoards/Prefabs/AuthorSponsorCommunityBoards.prefab`，同日按后续要求放入钟楼场景入口墙东侧，Credits 位于入口墙西侧，播放器控制台移至演奏区前侧左方。保留 `Author wall/Author card/08 · 宣传栏` 下的四个社群快捷按钮与原生复制输入框，及 `Sponsor wall/Sponsor list` 下的 16 个姓名槽和前后翻页按钮；依赖在组合内部接线。它不参与播放器弹层排序；曲名仍随乐器。现有控件和弹层局部层级不变，仅调整完整根节点的世界位置／朝向；四个独立 Canvas 碰撞体均为 trigger。详情见[独立组合预制体](26-community-boards.md)和[钟楼 UI 布局](25-clockwork-second-scene.md#2026-11-04-ui-重新布局与社群板入场)。

## 2026-11-04 音轨八度与自动音域

此段更新下文逐音轨开关的布局。按音轨页顶部 `AutoTrackRouting` 与新增 `AutoRange` 分占两列，调用原回调和 `_OnFileAutoRangeChanged`；仅在该页显示。行中心从124开始、间距38，左侧32×36为原开关，中间276×36为原映射按钮（Source／Value 改为上下两行），右侧 `TrackOctaveDown0～15 / TrackOctaveValue0～15 / TrackOctaveUp0～15` 为32宽减号、56宽数值、32宽加号。两列各有本地化八度标题；说明移至底部，TargetPicker 保持最后绘制。切回音色页隐藏全部新增控件，恢复原行位置与文字布局。

加减回调 `_FileOctaveDown0～15 / _FileOctaveUp0～15` 通过当前页查找原始轨号；翻页后复用行，不把显示下标当轨号。复选刷新使用 `SetIsOnWithoutNotify`，所有公共修改沿用 Facade 权限与曲目有效性检查。生成仍使用 `MidiHardwareMappingUIBuilder.ConfigurePlaybackMapping`，保留原按钮、GUID、场景引用并同步 backing Udon。

## 2026-11-04 乐器列表鼓组

`UI/InstrumentPanel/Content/Viewport/Options/Drums` 追加稳定菜单索引9，`CheckboxToggle` 调用 `_SelectDrums`，名称复用四份语言表的 `midi.drums`。原模型数组不追加鼓组，八音盒索引8及停用索引7保持不变。四组菜单数组扩展至10项，隐藏旧弦乐组后为九个可见行；Viewport 和滚动内容高360，面板高376，鼓组整行直接可见。原生 Toggle 样式、保持展开、权限检查和 `SetIsOnWithoutNotify` 刷新沿用现有乐器行。

仅切换鼓组保留旋律声部、按键和时间；本地“显示鼓组”不覆盖公共禁用状态。验证报告 `Logs/DrumInstrumentToggle-Validation.json`，实际渲染 `Captures/DrumInstrumentToggle/Menu-zh.png`；验证限于 Unity Editor／ClientSim backing Udon，不代表真实 VRChat 多客户端验收。

## 2026-11-04 音轨独立开关

`UI/FileMappingPanel/TrackEnabled0～15` 是与 `Program0～15` 并列的原生 Toggle，仅在按原始音轨页且对应行有效时显示。左侧32×36区域操作开关，映射按钮向右移动19、收窄至418，点击区域不重叠；切回音色页恢复原456宽度。使用现有空心框／填充样式，刷新用 `SetIsOnWithoutNotify`，回调 `_FileTrackEnabled0～15` 按当前页定位原始轨号，沿用权限、加载与曲目有效性检查。关闭行显示本地化的“已关闭 · 目标”，仍可打开映射列表；TargetPicker 继续最后绘制。

## 2026-11-04 乐器映射目标滚动与鼓组

`UI/FileMappingPanel/TargetPicker/Content` 的 Title 固定在顶部；新增 `TargetScroll/Viewport/Options` 容纳原 Target0～8，并追加 Target9（鼓组）。原对象、引用和回调保留，Target7（停用弦乐组）仍隐藏。Target9 调用 `_FileDrums`，沿用现有文件映射权限、提交和关闭行为；中英日名称复用 `midi.drums`。

`TargetScroll` 使用竖向 Clamped ScrollRect、280 高视口和 360 高内容，支持鼠标滚轮及右侧 `Scrollbar/SlidingArea/Handle`；九个有效选项可全部访问，打开新目标列表时回到顶部。标题、点击外部关闭遮罩和主面板绘制顺序不变。生成入口仍是 `MidiHardwareMappingUIBuilder.ConfigurePlaybackMapping`，增量更新后同步 proxy / backing Udon，并调用 `ConfigureHierarchy`。

此列表仅用于文件的按音色 / 按音轨映射；硬件 MIDI 页与顶部乐器复选菜单沿用各自规则。映射编码和鼓键语义见 [19 册](19-gm-program-fallback.md#手动文件鼓组目标2026-11-04)。

## 2026-11-04 可选轨名适配

`UI/FileMappingPanel/AutoTrackRouting` 是仅在“按原始音轨”页显示的原生 Toggle，位于提示行左侧，默认关闭。`Checkbox/Inset` 提供空心底框，`Checkbox/Selected` 是受 Toggle 控制的填充；Label 不接收射线。`onValueChanged` 发 `_OnAutoTrackRoutingChanged`，刷新使用 `SetIsOnWithoutNotify`，沿用公共控制权限。提示文字在音轨页让出左侧勾选位置，切回音色页恢复完整宽度；行数、分页和 TargetPicker 覆盖关系沿用既有结构。自动、关闭、手动优先级与鼓组近似规则见 [19 册](19-gm-program-fallback.md#可选轨名自动适配2026-11-04)。

## 2026-11-04 场景借物表

最新布局：双列、每页最多 8 条，按左到右、上到下阅读。`Row0～3` 保留对象身份，追加 `Row4～7`，数组与 backing Udon 一起更新。卡片宽 550、列间距 16、行间距 6；短标题 / 作者压为单行，长文字保留双行高度。空许可证仍留空，缺少说明 / 链接时不预留对应行。`Sources` 和新增的 `LicenseSource` 各使用一条只读单行输入框，保留完整 URL 供选择复制，避免长网址自动折成多行后被裁掉。同行取两张卡片的最大高度。按最新要求，页面固定为 1180 × 880，Canvas、Backplate 和 trigger 碰撞器均不随内容收缩；Previous、Next、Page 固定在面板顶部坐标系的 y=-858，切分类、切页和切语言均不移动底部翻页栏。547 首曲目现为 69 页，最后一页 3 条。固定页高截图与记录见 `Captures/CreditsUI/FixedPage/` 和 `Logs/CreditsUI/FixedPage/`；早先双列截图在 `TwoColumns/` 中，仅代表当时的自动收缩布局。

文案精简（同日后续）：移除字段说明、复制提示、重复的作者前缀、采样编码参数、通用许可说明和空链接提示；四份 World JSON 同步删除 19 组废弃文案。当前主要显示资源名称、署名、许可证和来源 / 许可原文，仅保留简短的采样修改、MIDI 转换和编配者禁止转载条款。没有许可证记录时显示为空，内部仍保留未记录状态，不借此授予许可。`Subtitle` / `Hint` 保留对象与引用但停用；无说明或无来源时停用对应行内对象，翻页时按新条目恢复。页脚只显示页码。资源条目、Logo 与仓库地址保留；当前场景为 561 条记录（含后续加入的音乐盒），文案删除不减少条目。重新调整顶部与行间距，中英日渲染见 `Captures/CreditsUI/Simplified/`。

主场景新增独立世界空间面板 `UdonMidiPlayer/Presentation/Credits`，与播放器 `UI` 同级。初装位于教室后墙 `(0, 1.69, -5.04)`，Y 旋转 `180°`，1180 × 880、缩放 0.002；再次更新保留顶边位置与旋转。触发碰撞器、VRCUiShape、GraphicRaycaster 沿用主面板方式，Backplate 最后绘制且不接收射线。

面板包含 Title、Brand、Logo、Subtitle、Repository、Tab0～3（Label、Selected）、Hint、Row0～3（Name、Author、License、Details、Sources/Viewport/Text）、Previous、Page、Next、Backplate。Logo 复用 `Docs/Brand/passhuPiano-logo.png` 的原始图像，Unity 副本为 `Assets/UdonMidiPlayer/UI/passhuPiano-logo.png`（CC BY 4.0，passhu），以保留透明度和比例的 Sprite 显示在右上标题区，不接收射线。四类为项目与美术、音源、字体、曲目，每页复用四行；曲目来自当前场景运行曲库，不复制为数百个控件。Repository 与每行来源 / 许可原文使用只读可选择 URL 输入框，不自动打开浏览器。

`MidiCreditsPanel` 仅处理本地分类、翻页及文字；`MidiPlayerUI.creditsPanel` 在 ApplyLanguage 时刷新借物表，复用四份 World JSON 与既有字体 fallback，不增加语言入口。它不提交播放命令或申请 ownership。分类和页码不随语言刷新重置。

编辑器入口 `Tools > Music MIDI Player > Scene > Install or Update Credits Board`，实现为 `MidiCreditsBuilder`。资源事实来自 `MidiCreditsCatalog.json` 及原 ATTRIBUTION / font-manifest / 根许可证；曲目按 `sourceHash` 匹配 ReleasePolicy 或本地 Classical100/SELECTION，不以曲名授予许可。已有 Uaaaaak 署名的条目链接本地 SOURCES 记录的作者目录和指南；未知来源 / 作者 / 许可分别明示。日后曲库变动可用此入口刷新借物表，不自动更新分发 Prefab、练习场景或发布清单。

此面板是署名展示，不替代许可证原文，也不改变原曲、编配、采样或字体的许可范围。

本次保存 560 条记录：项目 4、音源 4、字体 5、曲目 547；其中 425 首显示已记录的 Uaaaaak 编配条款，22 首许可未记录。Unity / UdonSharp 编译、backing Udon 的全部 560 条记录与 None 同步模式、六个按钮的 GraphicRaycaster 命中、翻页边界、最后一页三行显示及切换语言保留页码已在 Editor 核对。还在已有的 Play Mode 会话中通过原生 Button 回调验证四个分类和末页边界，完成后恢复借物表原分类与页码，Console 无 Error。中英日实际渲染见 `Captures/CreditsUI/`，报告 `Logs/CreditsUI/verification.json` 与 `playmode-callbacks.json`。未进行真实 VRChat 验收。

## 2026-11-03 播放文件音轨映射

根据最新要求移除文件通道映射。播放区 `UI/FileMappingEntry` 保持“乐器映射”，`FileMappingPanel` 标题行仅保留 `ByProgram/{Label,Selected}` 和 `ByTrack/{Label,Selected}`，分别调用 `_FileByProgram` / `_FileByTrack`；由原 ByChannel 对象增量改名并更新接线，保留对象身份。原 `Program0～15` 行及回调继续复用，按页签列当前曲目的原始 Program 或来源音轨，轨道数量超过 16 时翻页。无原始名称的音轨只显示本地化的 1 起始编号，有名称则附上名称。

共享 `TargetPicker` 按当前页签提交对应映射；音轨的自动选项为“跟随音色映射”。切页、切歌和关闭弹层会关闭旧列表；Reset 仅重置当前页签。选择遮罩保持最后子节点，更新继续调用 `ConfigurePlaybackMapping`、`ConfigureHierarchy` 并同步 proxy / backing Udon。设置内的硬件 MIDI 通道页与回调不变。

新版语言 JSON 显式同步导入后，中英日真实渲染及 18 项 GraphicRaycaster 检查通过；截图 `Captures/FileTrackMapping/Patchwork-Tracks-{zh,en,ja}.png`，报告 `Logs/FileTrackMapping-UI.json`。合成 129 音轨的九页列表验证通过，返回真实曲目后仅列《拼凑的断音》的两轨。生成器还会定点移除 backing Udon 中已废弃的文件通道字段，不清理其他设置。

## 2026-11-02 主场景面板位置

`UdonMidiPlayer/Presentation/UI` 移至教室后墙中央，世界坐标 `(0, 1.35, -5.05)`、Y 旋转 `180°`，正面朝向室内。原对象、缩放、子层级、UI 引用与 trigger 碰撞器保留；不更新分发 Prefab。Unity Editor 截图确认可见，面板前方抽样区域无实体碰撞阻挡；未进行真实 VRChat 验收。

## 2026-11-05 plum 曲库目录

按用户要求新增 `游戏音乐/plum`，包含既有《R - Plum》和新导入的《Orca》。R 的源文件、歌曲资产、GUID 与 songId 保持不变；Orca 保留全部音轨与 MIDI 音高，作者填写 Plum（[曲目署名来源](https://music.apple.com/us/song/1611003147)）。作者工程完整曲库由 570 首增至 571 首，使用现有曲库同步入口更新并保存当前打开的 `UnderwaterMusicGarden`，目录、搜索与 backing Udon 已核对。此前场景的 547 首运行曲库同步到当前完整曲库；已有分类及 570 首歌曲的顺序和身份保持不变。

同步前发现既有 Rush E 源 MIDI 和 `.meta` 缺失；从 Downloads 中 SHA-256 与歌曲资产记录完全一致的原件恢复源文件，沿用原源 GUID，不重导入或修改歌曲资产。未打开的主场景、历史钟楼、练习场景及两份分发 Prefab 不自动同步。本次为 Editor 数据检查，未进行真实 VRChat 播放验收。

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

2026-11-02：设置项 `SettingsPanel/Content/PeakProtection` 的文案改为“音量平衡”（Volume leveling / 音量バランス），默认选中，可本地关闭。路径、回调 `_TogglePeakProtection`、选择标记和本地化键保持不变；没有新增控件或调整绘制顺序。

## 八音盒入口（2026-11-04）

`InstrumentPanel/Content/Viewport/Options/MusicBox`追加独立复选框，调用`_SelectMusicBox`，模型索引8。停用Strings行7隐藏，八个可见外观行沿原布局连续排列，Viewport最高320。文件与硬件TargetPicker保留稳定编号，`Target8`调用`_MidiTarget8`选择目标7，旧`Target7`（停用目标6）隐藏；文件面板显示自动＋七件乐器。MIDI学习页追加`Instrument7`。两类文件映射、四份语言和原有弹层层级规则保持，生成器继续调用`ConfigureHierarchy`。

同日并行补充：文件TargetPicker已采用`Content/TargetScroll/Viewport/Options/Target0～9`滚动结构，Target7隐藏，Target8为八音盒，Target9调用`_FileDrums`。本次安装沿用该最新结构；不把文件专用鼓组加入旋律勾选集合。
