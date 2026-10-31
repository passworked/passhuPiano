# Udon MIDI Player

## v1.0 发布候选（2026-11-01）

当前候选版本 `1.0.0-rc.1`，尚未正式公开发布。两份钢琴分发 Prefab 使用独立古典曲库：现从原 100 首中保留 **13 首公有领域或 CC BY 曲目**，排除 87 首 BY-SA；不改写主场景曲库。两份 Prefab 的既有钢琴 bank 已重新烘焙为当前选定的 Salamander 四力度层配置，保留模型、资源 GUID、曲目 ID 与独立配置。

自有代码 MIT，自有美术 CC BY 4.0，作者 **passhu**，联系 **laptop7921@gamil.com**。第三方音源、字体与曲谱保留原许可；本地未核验资源不受上述默认授权。包内许可、署名和逐项白名单位于 `Documentation/`。主场景的多乐器能力不能直接视为分发 Prefab 的能力。

首次稳定发行仍需干净接收工程导入、真实 VRChat 听音及双客户端验收。详见包内[发布实施记录](Documentation/ReleaseWorkflow.md)。以下开发历史按记录日期阅读，不作为 v1.0 功能或验收承诺。

## 开发历史与功能说明

2026-11-01 弦乐表现改为琴体固定、对应琴弦亮起和近处轻量粒子，保留运弓；已取消音符窗、瀑布和落琴条。`MidiStringsBuilder.UpdatePresentation(player)` 仅更新表现，不重导音源。新乐器不再强求钢琴窗，详见 [弦乐专题](../../Docs/UdonMidiPlayer/14-strings-preparation.md)。

VRChat / UdonSharp 的 MIDI 钢琴播放器 MVP。已实现批量导入、复音调度、播放/暂停/停止/选曲、88 键高亮、瀑布流和可转移 Owner 的共享播放状态。

2026-10-24 集成状态：已接入白色三角钢琴模型和选定的 Salamander 单层音源。AudioClip 区域映射问题已修复，Editor 原生 Button.onClick → backing Udon 的实际播放、暂停、切歌和停止检查已通过；真实 VRChat 双客户端与听音验收尚未执行。

当前开发环境：Unity 2022.3.22f1、VRChat Worlds SDK 3.10.5。项目类型均位于 `MusicMidiPlayer.*`；Editor 与 Runtime 程序集分开，并附 Runtime 的 U# 程序集注册。

2026-10-26 主场景更新：`Assets/Scenes/VRCDefaultWorldScene.unity` 的两个实例已合并，通过同一个“乐器”菜单切换数码钢琴、白色三角钢琴和马林巴，两种钢琴共用同一音源。实时 MIDI 与踏板输入保留；音色资产修改后使用 `Tools/Music MIDI Player/Apply Instrument Tone Changes` 应用。模型、采样来源、预算及验证边界见 [主场景乐器切换](../../Docs/UdonMidiPlayer/13-instruments.md)。独立钢琴 Prefab 和练习场景保持原用途。

2026-10-31 鼓组更新：主场景已加入 Virtuosity Drums（CC0）及独立鼓组模型、鼓皮/镲片/脚踏动作和晶体击打音块。第 10 通道走鼓组，与手选旋律乐器并存，共用现有 64 个 AudioSource；“显示鼓组”为本地表现开关。曲库 `Demo/Drums` 中的 `GM Drum Study` 用于试听旋律与鼓组混合播放。采样映射、替代音色、预算、再生成入口和验证边界见 [鼓组说明](../../Docs/UdonMidiPlayer/14-percussion-preparation.md)。两份钢琴分发 Prefab 和独立练习场景未为本次鼓组覆盖。

## 快速体验

弦乐追加：主场景的乐器入口已改为下拉条，新增小提琴、中提琴、大提琴、低音提琴与弦乐组，并提供长弓、跳弓、拨弦子下拉。使用 VSCO 2 CE 的 CC0 采样，支持循环长音、CC11 音量表达和无人运弓表现；继续共用现有曲库、Transport 与 64 个物理 AudioSource。接入、声部映射及验证范围见 [弦乐组接入](../../Docs/UdonMidiPlayer/14-strings-preparation.md)。

建议在新场景中拖入 `Prefabs/UdonMidiPlayer.prefab`，使用面板的 PLAY、PAUSE、STOP 和曲目按钮。也可打开 `Samples/MidiPlayerDemo.unity`；其中仅 `sample` 目录为本项目生成的原创短曲，其余曲目来自已导入的 MIDI。保留现有场景的曲库与配置；工具不会为了更新演示而覆盖未保存场景。

正常接入世界时，将 `Prefabs/UdonMidiPlayer.prefab` 拖入场景。需要更换曲库或音源时，打开 `Tools/Music MIDI Player/Configure Player`，选择播放器根对象及源曲库/音源后应用配置；工具负责数据烘焙和引用接线。

编辑器窗口与世界面板仅提供 English、中文、日本語三个语言入口，初次使用自动检测语言并以英文兜底。中文共用一个入口：世界内按 VRChat 客户端语言显示简体或繁体，编辑器按编辑器／系统语言判断；其他语言环境点中文默认简体。两处均通过“文/A”图标打开单排三个国旗按钮，界面不提供自动语言选项。编辑器顶部的选择保存在本机；世界面板右上角的选择仅影响当前玩家、当前播放器，本次会话内生效。曲名、曲库名和自定义目录名保留原文。语言表与维护方式见 [国际化说明](Localization/README.md)。

现提供两套可直接拖入场景的独立播放器预制体：

| 造型 | 预制体 | 保存的配置 |
| --- | --- | --- |
| 白色三角钢琴 | `Prefabs/UdonMidiPlayer.prefab` | `Editor/Generated/PrefabConfiguration.asset` |
| 立式版本（石墨色数码钢琴、木色侧板与支架） | `Prefabs/UdonMidiPlayer_Upright.prefab` | `Editor/Generated/PrefabUprightConfiguration.asset` |

两套预制体共用独立的 `Editor/Generated/PrefabClassicalLibrary.asset`，v1.0 候选保留 13 首通过发布授权白名单的古典作品／乐章，默认曲目为《欢乐颂（主题短版）》。文件夹名 `Classical100` 沿用历史路径以保留 GUID；不表示候选包包含 100 首。逐曲实际路径、来源、许可、署名、哈希与派生资产见 `Documentation/ReleasePolicy.json` 和 `Documentation/THIRD_PARTY_NOTICES.md`。本地旧来源清单保留用于审核，不随候选包全量导出；公开源码中的场景曲库由发布流程独立筛选，不回写本地 `DemoLibrary` 或主场景。

立式版本直接复用现有 `MidiPlayer_Visual.fbx`，导入于 `Models/DigitalPiano/`，保留 88 键动画、曲目配色、瀑布流、侧面控制 UI 和相同曲库/音源。该模型本身没有踏板，踏板表现数组为空，MIDI 延音音频逻辑仍正常使用。两套预制体配置分别保存模型选择；编辑立式预制体时沿用 `PrefabUprightConfiguration`，现有场景的立式播放器继续使用 `UprightConfiguration`。默认生成及 Update Player UI 菜单仍以三角钢琴主预制体为目标。

面板以 [VizVid](https://github.com/JLChnToZ/VVMW) 为视觉参考：深蓝灰圆角背景、浅青线条图标，左侧当前曲目与播放控制、右侧固定高度曲库。曲库支持滚轮、拖动与溢出时的滚动条，列表时长与主计时均包含释放尾垫；有控制权限时可拖动进度条，松手后共享定位到目标时间。`Tools/Music MIDI Player/Update Player UI` 仅更新主 Prefab 的 UI 皮肤与接线，保留既有控件对象及面板摆放，不重烘焙音源、模型或 MIDI，也不保存当前场景。`UI/Skin` 中的圆角和图标由本项目生成，未复制 VizVid 素材。

收藏夹由世界作者在编辑器预设，玩家通过右侧曲库逐级进入目录选曲，类似 VizVid 歌单。根目录包含“全部曲目”和预设目录；目录标题显示当前路径，可返回上一级，目录条目显示直属曲目和子目录数量，空目录显示提示。浏览目录只影响本地界面；点击曲目仍通过现有共享选曲入口，切歌后停在 0 秒，按 PLAY 开始。当前不提供玩家在世界内编辑收藏夹或跨会话保存收藏。

2026-10-29：主场景曲库追加《Patchwork Staccato（ツギハギスタッカート）》，共 546 首，位于 `VOCALOID/初音ミク`；东方、Arcaea、Phigros 位于 `游戏音乐` 的子目录。目录继续逐层进入、返回上级。搜索框实时搜索完整曲库的曲名、作者、源文件名和分类路径，不区分英文大小写，空格分隔的多个关键词须全部匹配；结果按完整曲库顺序显示，每首只出现一次。清空搜索或在搜索时点击返回可恢复之前的目录和滚动位置；浏览和搜索不会更改共享播放状态。主场景和两套独立钢琴 Prefab 已接入搜索，独立 Prefab 仍使用自己的 100 首古典曲库。

配置时选中 `MidiLibraryAsset`，先把曲目加入“完整曲库”，再在“收藏夹目录”中添加路径和曲目。路径以 `/` 分级，例如 `游戏音乐/Arcaea`，缺少的父目录会自动显示。目录按配置中各路径及父路径首次出现的顺序显示；拖动调整分支顺序时，应把父目录与该分支的全部子目录一并调整。目录内曲目按该目录的 `songs` 数组顺序显示；同一曲目可放入多个目录，同一目录不能重复添加，所选曲目必须已在完整曲库中。点击“检查目录”验证，再点击“保存并同步场景目录”更新已加载场景中使用此曲库的播放器，完成后保存对应场景；主 Prefab 的菜单使用 `Tools/Music MIDI Player/Update Player UI` 更新。

UI 字体保留 Liberation Sans 的拉丁文字外观，通过项目独立的动态多 Atlas fallback 扩展字符支持，覆盖中日韩、VizVid 所用 Comfortaa 的字符范围，以及希伯来文、泰文。字体链供曲目标题和其余 UI 文字共用，之后执行 Update Player UI 或重新应用配置也会保留；它不代表完整 Unicode 或彩色 Emoji 支持。

## 导入 MIDI

1. 将 `.mid` 放入工程 Assets 内。
2. 退出播放模式，打开 `Tools/Music MIDI Player/Import MIDI`，批量选择文件或文件夹。
3. 检查“加入曲库并同步场景”字段：窗口优先使用当前选中播放器的曲库；否则只有已加载场景使用唯一一种曲库时才自动填写。可先点击“预览与诊断”检查内容，预览不会修改资产或场景。
4. 点击“导入 / 更新曲目”。成功曲目会加入所选曲库，整批完成后自动更新已加载场景中使用该曲库的播放器运行数据和曲目菜单；重导入同样自动同步。查看同步结果并保存对应场景。

曲库留空时只生成或更新曲目资产；可选择曲库后再次导入，原曲目引用会保留。后续加曲或重导入只自动同步已加载的普通场景实例，不同步预制体或 Prefab 编辑模式。给现有示例场景加曲时选择原 `DemoLibrary`，不要将加曲后的场景覆盖 Apply 到预制体。尚未接入场景或需要更换曲库、音源时，仍在 Configure Player 中应用配置。

支持 SMF Type 0/1、PPQ、曲内变速、多轨、NoteOn velocity=0、同音重叠、基础 CC64 延音及 CC64/66/67 踏板动画。默认排除打击乐通道，只播放 A0～C8。Type 2、SMPTE 和损坏文件明确报错；程序变更、弯音及其余控制表达不作为通用 GM 合成实现。CC66/67 当前驱动模型表现，尚未扩展音频合成语义。

选另一首曲目会停止旧曲并回到新曲的 0 秒，随后按 PLAY；再次点当前选曲不重启。自然结束保留释放尾音，主动 STOP 清理全部声音和表现。音量和静音只作用于本地。

## 模型与音源

默认配置选定 `Models/WhiteGrandPiano/WhiteGrandPiano.fbx`；源 Blender 文件和导出清单位于开发工程的 `ArtSource/UdonMidiPlayer`。模型保留 88 个独立琴键，通过 `Key_021_…`～`Key_108_…` 命名绑定；键宽、位置、原材质和后端铰链由 Editor 烘焙，保留导入模型的旋转、缩放及源 Prefab 连接。琴键以 35 ms 按下、90 ms 回弹，白键前端行程约 8 mm，黑键约 6 mm；同音重叠按物理按键计数处理，CC64 尾音不会让已松手琴键继续压住。

每首曲目用原始 MIDI 文件的 SHA256（`sourceHash`）决定唯一主色相。所有白键高亮及白键音符统一为这一色相的浅亮色，所有黑键高亮及黑键音符统一为相同色相的深饱和色；粒子、命中光环和环境光按命中的黑白键取对应颜色。整首只有一个色调、黑白键两种颜色，不再按音高轮换多色色相。相同 MIDI 重导入、改编辑器标题或切曲后返回均保持配色，源哈希也进入 contentHash/catalogHash 的一致性校验。

每个实例初始化时分别缓存 88 份琴键材质与 88 份体积音符材质，两者色调相同，瀑布使用独立发光着色，命中光尘、光环及环境灯也取同一曲目调色板；换曲只更新颜色，逐帧不创建材质，未按下的键恢复模型原材质。左、中、右踏板分别绑定 `Pedal_UnaCorda`/CC67、`Pedal_Sostenuto`/CC66、`Pedal_Sustain`/CC64，按 0～127 值绕后端铰链平滑踩下，80 ms 过渡、约 12 mm 满行程。动画全部跟随音乐时间，暂停冻结，停止或切歌立即复位；没有相应控制事件的踏板保持原位。

瀑布条使用有实际厚度的切角实心音符，带宝石切面、抛光高光和环境反射。正背面采用宽幅切面法线与深浅变化，低量同色内光保留通透的视觉层次，亮度集中于切角和随视角变化的 Fresnel 边缘；默认高度 1.85 m、厚度 8 mm，不依赖全局 Bloom。音符命中发出同色光尘与键床光环，12 个分区粒子池按音乐时间模拟，并与环境的静态 Collider 碰撞。每帧最多 64 颗、总量最多 768 颗；8 个局部点光区最多同时亮 4 盏，无阴影，直接照亮琴体及周围使用 Lit 材质的地面/物体。全部效果本地计算，不逐音符发送网络事件，暂停保留余光和粒子，停止/切歌清空，晚加入不补发历史粒子。

瀑布音符的正面、背面和斜面均为实心填充，Alpha=1、完全不透明并正常写深度，使用 Geometry 渲染队列；不再裁掉中心。黑白键同色相的两档配色保持鲜明，主色相仍由同一 MIDI 哈希决定。

音符仅拉伸中段，上下端的几何切角深度保持为音符宽度的 12%，内部端部切面同样按宽度定位，不随音符时长拉长；极短音符自动缩小端部，防止两端重叠。总长度、外端点和命中落点保持原有计算，切面法线与切线随端部变形修正。

当前默认场景的环境反射已单独烘焙到 `Art/Waterfall/WaterfallEnvironment.exr`（128 px Cubemap），主 Prefab 的反射探针使用该资源。改变摆放或接入其他世界后，可用 `Tools/Music MIDI Player/Bake Waterfall Environment Reflection` 重新捕获当前已加载场景，再写回原 Prefab；该工具只烘焙这一份反射，不重建整场光照贴图，也不保存未保存的场景。动态命中照明仍由上述有限实时灯完成。

命中粒子分为两层。原光尘取消重力与发射初速，使用始终向上的速度曲线、对称横向扩散曲线及生命周期限速，形成快升、慢漂的动势；大小与透明度也由曲线约束。上方叠加稀疏的音乐符号：四分音符、八分音符、连音符、高音谱号、低音谱号、四分休止符，较慢上浮并轻微转动。六种符号均为同曲色系的空心轮廓网格，音符头和笔画内部留空，仅轮廓显示颜色，无额外字体或图片依赖。

符号层可用 `MidiMusicSymbolAssets.UpdateExistingSymbolVisuals()` 单独更新其网格、材质和现有 Prefab 引用，保持琴键、矩形瀑布条及运动参数。更新既有 Mesh 时显式写入并上传顶点/索引，保留原资源 GUID，确保 CPU 几何与实际渲染一致。

符号层有 12 个池，每池 16 个、总符号最多 192 个，与原光尘共同受每帧 64 个和总量 768 个的预算约束。受 Unity 单渲染器最多 4 种网格限制，各池轮换选取四种、合计覆盖六种。符号只在部分命中时发射，沿用该音符所属的黑白键颜色；两层一同按音乐时间推进、暂停冻结和停止清空。

曲目数据使用 schema 2，新增分踏板时间线与定位查询。升级旧曲目资产时重新导入并应用配置，原 GUID 和 songId 保留。`MidiPlayerBuilder.UpdateExistingPrefab()` 可显式更新既有主 Prefab，优先沿用预制体自身的独立配置，不创建或打开场景；场景已有的曲库与菜单独立覆盖，不随预制体曲目变化。

Configure Player 的模型字段可选择其他具有同一 88 键命名约定的 FBX/Prefab；留空时使用基础几何键盘。真实模型模式关闭旧方块键与实体背景墙，仅保留悬浮音符；控制 UI 独立放在琴侧，不给钢琴本体添加屏幕、屏框或面板外壳。

选定音源位于 `Audio/SalamanderV9`：Salamander Grand Piano 的 v9 单力度层、30 个根音、48 kHz 立体声、Vorbis q8 源文件。全键力度以增益响应，此版本不保留原始 16 个力度层之间的音色变化。

音源源文件总量为 10,188,801 字节（9.72 MiB）。Unity 目前使用 PCM + Decompress On Load，保留源文件字节并避免第二次 Vorbis 有损编码；按 float32 立体声估算的纯采样缓冲约 154.29 MiB，本次 Editor 中 AudioClip 原生内存观察约 77.26 MiB，世界客户端和成品包需另测。Unity 导入产物、世界下载和分发包体积另计。实际导入报告及署名文件随音源保存，须与资源一同分发。

所选层保留 FreePats SF2 的分区域键区、音分调音和释放时长。当前 `BANK-MAPPING.json` 的调音范围为 −38～+13 cents；23 个区域释放为 1000 ms，F#6～C8 的最后 7 个区域为 4999 ms，`maxReleaseMs=4999`。这些参数与采样映射一起进入 bankHash，不把所有原采样当作零音分或固定 180 ms 释放。实际曲终按 MIDI 逻辑末尾加最大释放尾垫计算，主动停止仍执行短防爆音退场。

Salamander 原作者为 Alexander Holm，FreePats SF2 版由 FreePats 项目整理，许可为 CC BY 3.0。源说明、官方链接、许可链接及本项目的层选择/转码说明见该目录文件。旧 `GeneratedPiano` 仅保留为开发期可重新生成的合成音源备选，不作为新音源的录音或署名来源。

## 同步与配置

有权限的操作者本地操作后取得播放器对象 ownership 并提交状态，各端自行运行音符、声音和表现。接收状态不会反向申请 ownership；Owner 与实例 Master 分开。晚加入按时间锚点恢复当前音符及尾音，不回放全部历史事件。

默认允许控制；`Runtime/ControlGate` 的初始锁定可关闭本地公共操作。锁定不影响接收状态、本地音量或静音。当前 64 个 AudioSource 与 512 条可见音符为初始池配置，实际可闻复音还受世界全局音频预算影响。

已有记录包括 29 项 Parser 边界检查、Unity C#/UdonSharp 编译，以及默认场景原实例的 Editor Play Mode 检查：原生按钮调用进入实际 Udon，逐声部 clip/pitch/generation、琴键中间动画帧、曲目调色板与瀑布同色、三踏板姿态、暂停冻结及停止复位通过。三踏板完整按下/释放检查仅临时注入运行时控制数据，源 MIDI 未改动；Prefab 的网络 Manual、本地 None 序列化设置也通过校验。[本次 Editor 报告](../../Logs/MusicMidiPlayer-playmode-report.json)位于开发工程的 Logs，不随分发包提供。

瀑布效果另经默认场景实际 Play Mode 检查：立体网格与独立同色材质、原生粒子/灯光预算、暂停冻结、停止清空，以及开关局部灯后地面/琴体底部的真实渲染差异。截图位于开发工程 `Docs/UdonMidiPlayer/Previews/waterfall-volume-playing.png`，不包含在资源包内。

上述检查不包含鼠标/VR 射线点按、捕获音频或实际听感，也不代表网络功能已验收。真实 VRChat 双客户端、晚加入、所有权竞争及长时网络偏差仍需在客户端单独验证。Quest 专项优化、变速和外部音符事件 API 留在后续版本。

## 维护与分发

重导入保留曲目资产 GUID 和 songId。升级保留 `.meta`、完整类型名、程序集名和 Udon 程序资产；不要只复制 `.cs` 或手动删除重建程序资产。

`Tools/Music MIDI Player/Build Demo Prefab and Scene` 可显式重建示例；更新既有主 Prefab 时优先使用其自身的独立配置，保留其曲库、音源和模型，缺少配置时才回退到默认配置，脏示例场景会被拒绝覆盖。Editor 的 `MidiPlayerBuilder.BuildPrefabOnly()` 同样沿用主 Prefab 自身配置，不保存、关闭或重建现有场景。

构建校验器只检查场景中的本播放器，源配置过期、哈希或接线不一致时阻止构建。`Tools/Music MIDI Player/Export Package` 校验后生成本地 `Releases/UdonMidiPlayer-v0.1.0.unitypackage`，不上传或发布；当前不以存在导出代码作为最终分发包已验收的证明。导出包含模型、当前音源及署名、TMP/OFL 和实际需要的序列化 Udon 程序。默认选择 Salamander 时，旧合成 WAV 与旧默认 Bank 从发布种子中排除，真实依赖仍可补回，项目原资源不会被删除。

接收工程需安装受支持的 VRChat Worlds SDK 及其 UdonSharp、TextMesh Pro/UI 依赖。项目自有 Runtime/Editor 程序集、Runtime 的 U# 注册及每份 U# 程序资产随包保留；开发时使用的 MCP、Library、Temp、日志和下载目录不是接收工程依赖。

## VR / PC 实时演奏（2026-10-27）

`MidiPerformanceInput` 将 VR 手指触键和 PC 按键送入 `MidiPlayerFacade.PerformNoteOn / PerformNoteOff / PerformControlChange`；硬件 MIDI 回调也转发至这三个入口。音源、力度、琴键、瀑布流、特效、实时演奏同步、晚加入恢复和个人练习判定继续沿用 MIDI 链路。公共播放器须处于停止状态；个人练习场景沿用练习状态规则。

- 设置面板可独立勾选左右手各五根手指，默认只开启左右食指；这是本地设置，不影响其他玩家。使用 Avatar 的末节指骨估算指尖，缺少指骨时跳过对应手指。仅从上方下压触键，带松键容差；多根手指按同一键时，最后一根离开才松键。
- PC 必须坐在该乐器前的演奏椅上。默认从 C4 开始，`A W S E D F T G Y H U J K O L P ; '` 依次对应 18 个半音（C4～F5）；支持同时按多个键。`Z / X` 降低／升高一个八度，`Space` 为延音，`Enter` 离座。换八度时已按住的键保持原音高，按原键松开。
- 离座、停用输入、关闭对应手指、切换乐器、重生、换 Avatar、播放器状态变化时释放本输入持有的音符。PC 椅子禁用移动／跳跃的默认离座，避免 WASD 和空格与演奏冲突；Enter 和再次交互可离座，VR 保留通常离座方式。
- 虚拟输入默认使用 MIDI 通道 16（代码索引 15），力度 100；可在输入组件中调整。硬件 MIDI 与虚拟输入同时使用时，为硬件选择其他 MIDI 通道，避免同通道同音高共享 MIDI 音符身份。
- 触键区域按实际模型静止网格烘焙，钢琴为 88 键，马林巴只生成实际存在的 61 个琴条，不在空白处生成虚拟琴键。`MidiPerformanceBuilder.Configure` 负责接线；`UpgradeAll` 更新两个播放器预制体及主场景、Demo、Practice 场景。

真实 VR 指骨、不同 Avatar 的触碰手感、PC 键盘硬件多键冲突和双客户端网络效果仍需在 VRChat 内体验确认。

### 按设备显示输入设置（2026-10-27）

VR 输入区使用左右手示意图，十个手指开关分别位于对应指尖；“一键全选”和“全部取消”同时更新全部手指。取消已按下的手指会沿现有音符引用计数释放输入。PC 仅显示键盘布局、坐下演奏说明与快捷键；VR 仅显示手指设置及摇杆灵敏度。依据本地玩家 `IsUserInVR()` 选择界面，与 MIDI 设备连接状态无关，也不改变其他玩家的设置。
