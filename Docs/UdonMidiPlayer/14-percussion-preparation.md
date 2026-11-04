# 通用 MIDI 鼓组

> 2026-10-31 实施更新：鼓组已安装到主场景，`voicePool.drums`、`clock.drums`、`drumView` 与 backing Udon 引用已接通，安装报告见 `Logs/percussion-install.json`。下半部分保留接入前调研记录，其中“尚未修改运行时代码”等只描述当时状态。追加规范和 Program fallback 的独立草案见 [18](18-adding-instruments.md)、[19](19-gm-program-fallback.md)，其较早静态快照不代表当前鼓组安装状态。

## 当前接入与维护

- 主场景：`Assets/Scenes/VRCDefaultWorldScene.unity`；鼓组与当前选中的旋律乐器并存，不占用 `selectedInstrument` 的互斥选项索引。
- 数据：`UdonMidiPlayer/Data/PercussionBank`；音频逻辑：`Runtime/PercussionAudio`；模型、鼓件动作与轨道：`Presentation/DrumKit`。第 10 通道（代码索引 9）进入独立 one-shot 链，其他通道保留既有钢琴、马林巴和弦乐路由。
- 音源：Virtuosity Drums 固定提交 `9f04cf9a734527edfbb0a4eee1f674e45bbf71bc`、CC0，`Assets/UdonMidiPlayer/Audio/VirtuosityDrums/` 内附授权、署名、逐文件 SHA256 和 GM 替代表。47 个 GM note 35～81 均有显式映射，共 232 个 48 kHz 立体声 Vorbis q4 文件，完整尾音、不逐音归一化。采样层最多保留四层、每层最多两份轮替，原始层边界和重新分区均可追溯。
- 体积：压缩源文件 10,570,665 bytes（约 10.08 MiB）；float32 解码缓冲估算 320,387,160 bytes（约 305.54 MiB）。这是资源测量，不是世界构建下载量，也不是已验证的移动端预算。
- 2026-11-04 起，232 段鼓组采样的 Standalone 导入使用 Unity Vorbis 质量 70，默认平台使用 PCM。原采样率、立体声、Decompress On Load、力度和轮替沿用原规格，峰值与能量表仍从 PCM 烘焙。PC 对照构建中鼓组在压缩包内估算从 32.3 MiB 降到 4.7 MiB。
- 同一组 64 个 AudioSource：仅在歌曲含鼓轨或实时鼓尾音尚在播放时保留尾部最多 16 槽给鼓组，其余通常为 48 槽钢琴/马林巴，或 24 个使用双源的弦乐声部。没有鼓轨/尾音时恢复原旋律容量。较小 `voiceLimit` 时鼓组按四分之一预算缩减。该分区避免并行引入的双源弦乐覆盖鼓尾音；没有新增一套 AudioSource。
- 普通鼓和镲片忽略 NoteOff / CC64 的音频释放；CC120 清音仍有效。同一鼓多次 NoteOn 保留多次攻击音；闭镲、脚镲仅收掉既有开镲，目标淡出为 25 ms。歌曲尾音终点与 choke 在绑定时独立计算，定位/晚加入从真实年龄恢复，不复演旧攻击；网络进度校验也包含鼓组尾音上界。
- 表现：底鼓击槌和脚踏、独立鼓皮回弹、三只通鼓、Crash/Ride 阻尼摆动、上下踩镲开合、各鼓短击棒轨迹、局部冲击光及固定 64 个晶体音块。八个套鼓落点加辅助打击落点共九个锚点；音块表示击打，不将长混响画成长按。
- 本地设置 `SettingsPanel/Content/DrumVisibility` 的“显示鼓组”仅隐藏模型和表现，音频不受影响，含英/简中/繁中/日文文案。该控件与峰值保护同一行，沿用统一 Canvas 排序。
- 混合示例：`Assets/UdonMidiPlayer/Samples/GMDrumStudy.mid`，曲库 `Demo/Drums`，曲名 `GM Drum Study`。包含旋律、连续军鼓、开闭镲、通鼓以及全部 GM 鼓键位。默认曲目未更改；现有 546 首曲目的选轨未检测到保留的第 10 通道音符，因此没有改动这些曲目的排除设置。新增导入窗口默认保留鼓通道，历史/API 设置仍保留显式排除能力。

可复现步骤：运行 `Tools/prepare_percussion.py`；模型由 Blender 运行 `ArtSource/UdonMidiPlayer/build_drum_kit.py` 生成，含 `.blend`、FBX、GLB、预览与 manifest；运行 `Tools/create_drum_demo.py` 生成示例；最后在合并播放器场景执行 **Tools → Music MIDI Player → Install or Update GM Drum Kit**。安装器原位更新 bank、接线、音频导入设置和 catalogHash，保留现有乐器和曲库引用。单独更改曲库后仍须执行现有同步入口。

鼓组模型已在 Unity 内实际渲染检查，截图在 `Captures/DrumKit/`。已完成的运行观测、编译与检查结果以 `Logs/percussion-playmode.json` 和最终交付说明为准，不将 Editor 代理调用或浏览器预览当作 VRChat 运行验收。真实硬件 MIDI、多客户端及人工听音尚未验收。

实际检查记录：解析回归 163 项；ClientSim 中通过原生 SDK MIDI 事件进入 backing Udon，检查 GM 35～81 × 力度 1/44/85/127 的 188 组映射；重复军鼓保留两声部，NoteOff / CC64 不截断，CC120 清音；闭镲对开镲的收音终点约 25 ms。钢琴与鼓声各一声部同播，隐藏鼓组和跨旋律音源切换后鼓尾音仍在；鼓组与钢琴的曲目色差为 0，物理 AudioSource 数量为 64。

混合示例经原生曲目按钮和进度控件定位到 2.25 秒，七个有效尾音使用非零 `timeSamples`，其中开镲仍有效；定位到 2.6 秒后已经收掉的开镲没有恢复。暂停时音乐位置差约 8.2e-8 秒、鼓皮位移差约 6.0e-8 m，均在浮点误差范围。源 MIDI 终点为 31.473 秒，33 秒仍有七个发声尾音，最终播放终点为 41.837 秒；停止后时钟、声音和音块归零。上述为单客户端 backing Udon 结果，不代表真实网络已验收。

当前教室中的音块落差设置为 1.85 m。鼓件 View 使用预先筛出的鼓音符索引，每帧命中处理和几何池均有上限；颜色由原 MIDI 哈希生成，与钢琴主色一致。音块使用每播放器缓存的一份私有材质，后续曲目仅更新该材质颜色，不逐音符创建材质。

## 接入前调研记录

日期：2026-10-31。用户本次选择：底鼓、军鼓、通鼓、踩镲、吊镲的通用 MIDI 鼓组。本文是音源调查与表现方案，尚未修改运行时代码、音源 bank、Prefab 或场景，尚未做 Unity / VRChat 验收。

## 音源选择

首选试听 [Virtuosity Drums](https://versilian-studios.com/virtuosity-drums/)，源码与采样为 [sfzinstruments/virtuosity_drums](https://github.com/sfzinstruments/virtuosity_drums)，固定提交 `9f04cf9a734527edfbb0a4eee1f674e45bbf71bc`，随仓库的 `LICENSE` 标明 CC0-1.0。这是偏爵士、自然现场感的真鼓采样；适配流行/摇滚的程度需要人工听音决定。官网完整库约 1.1 GB，不能将完整多麦克风库直接当世界音频预算。

原库包含底鼓、军鼓中心/边击/鼓边、两只通鼓、闭镲/开镲/脚踩镲、Crash、Ride、镲帽及辅助打击乐。官网说明部分鼓最多 36 个力度层，镲片有轮替录音；具体分组数应以本次提交的文件清单和 SFZ 为准。SFZ 的原始键位、CC 和私有定义不等于本工程的 GM 映射，需要 Editor 转换，VRChat 运行时不加载 SFZ/VST。

第一轮采样采用同一 OH 麦克风，便于核对空间和音色一致性。正式 bank 可离线制作统一近场/顶麦混合，但每次敲击仍只播放一份成品 AudioClip，避免按麦克风数放大声部成本。先试听，再从原始层中选取约 4～6 个代表层，镲片保留约 2～4 份轮替；这些是待测预算建议，不是已选定规格。不要逐样本归一化，也不要沿用现有“4 层即钢琴、不额外衰减”的隐式规则：鼓组单独保存力度分区及增益。

本次固定提交的 OH 文件清单实测如下；官网的“最多 36 层”不能应用到所有鼓件。每组只下载中间编号层的一份代表录音，并没有下载表中全部文件。

| 代表发音 | 文件命名中的力度层数 | 该发音 OH 文件总数 |
| --- | ---: | ---: |
| 底鼓，snares off | 4 | 16 |
| 军鼓中心击打 | 36 | 36 |
| 高 / 低通鼓中心击打 | 各 16 | 各 16 |
| 闭镲 | 4 | 16 |
| 开镲 | 4 | 12 |
| Crash | 3 | 12 |
| Ride | 3 | 12 |

层号不是等距 MIDI 力度分区，正式转换须读对应 SFZ 的 `lovel/hivel`。原 `keymap_basic.sfz` 把 43、45、47 等映射到同一低通鼓的 off-center、rimshot、cross-stick，37 是 stickshot；这些都不能原样当成严格 GM。手拍、电子军鼓、中国镲等也须逐项核对来源和替代关系。

来源原件、SFZ 证据、代表采样和 SHA256 保存于 `Downloads/PercussionResearch/`，具体文件见 `source-manifest.json`。试听副本采用 32 kHz 单声道 Vorbis q2、完整尾音、不归一化，仅供方案预览；正式源拟沿用当前 48 kHz 立体声 q4 流程，须测量再定。代表采样不能当成完整鼓组或完整力度测试。

备选调查：[Salamander Drum Kit](https://sfzinstruments.github.io/drums/salamander/) 的索引当前标注 CC BY-SA 3.0，网上也有作者后续变更授权的说法；本次不混用授权结论、不纳入候选采样。VSCO 2 CE / VCSL 的 CC0 管弦素材保留在独立研究目录，本轮不接入。

## 当前工程的真实接入点

| 代码位置 | 现状与必须处理的边界 |
| --- | --- |
| `Editor/Import/MidiImportSettings.cs`、`SmfParser.BuildNotes` | 默认 `excludePercussion = true`，删除第 10 通道（代码索引 9）；需要对准备支持鼓组的曲目显式重新导入。不要悄悄改变全部旧曲的导入设置。 |
| `Runtime/Data/MidiSongData.cs`、`Editor/Baking/MidiSongCodec.cs` | 已保留逐音 `channel` 和 `trackIndex`，普通 GM 鼓组路由不必因此重做曲目编码。 |
| `Runtime/Data/PianoBankData.cs` | 强制 88 键连续音域、最近根音变调以及 1/3/4 个等长力度层；不适合以音符号表示不同鼓件的鼓组。 |
| `Runtime/Audio/PianoVoicePool.cs` | `RegionFor` 和实时 MIDI 都只使用一个 `bank`；当前 NoteOff、`releaseMs`、跨音色恢复均有钢琴语义。需要在同一池内按音符路由和记录 bank 类型，不能播放鼓组时全局 `BindBank` 换掉旋律音源。 |
| `Runtime/Logic/MidiPlayerFacade.cs` | `_LiveNoteOn` 按 `channel*128+note` 作为持续音身份；重复同音先释放旧音。鼓的每次击打要有独立实例，不能把滚奏或连击折叠成一个按键。 |
| `Runtime/Logic/MidiSequencer.cs`、`PianoKeyboardView.cs` | 钢琴用 88 个按键计数，无法表达鼓的多次击打。鼓音符须排除出钢琴键盘、钢琴命中粒子、练习判定及钢琴瀑布流。 |
| `Runtime/Views/MidiWaterfallEffects.cs` | 已有歌曲时间、代次、补帧年龄与命中特效预算，可参考其方式实现鼓组效果，避免复制另一套音频计时。 |
| `Editor/Prefab/MidiInstrumentMigration.cs` | catalogHash 包含当前乐器列表与 bank；新增鼓组要包含映射、音源和规则指纹。这里以及构建校验仍有三个外观的假设，不能直接追加索引就结束。 |

## 逻辑与发声方案

保持统一 Transport、时钟、Sequencer 和现有共用 64 声部池。增加 `PercussionBankData`（暂定类名），以 GM note → articulation → velocity region → sample 的烘焙数组查找。每槽缓存发声类型、采样、起点、尾音终点、chokeGroup 和代次；复用 DSP 预约、自然结束回收及峰值保护。开始时不增加一整套 64 声部池。

路由：文件及实时 MIDI 的第 10 通道交给鼓组；其他通道继续走已选钢琴/马林巴。鼓组实体作为伴奏模块与旋律乐器并存，显示开关为本地表现设置；如果将来需要鼓组静音，明确其本地/公共职责再接控制，不把它混成第四个互斥旋律乐器。电子鼓输入非第 10 通道可后续支持明确的通道配置，本轮先支持标准映射。

GM 核心键位，依据 [General MIDI percussion key map](https://computermusicresource.com/GM.Percussion.KeyMap.html)：

| 鼓件 | MIDI note | 资源及表现要求 |
| --- | --- | --- |
| 底鼓 | 35、36 | 两个 note 可共用同一实体；正式采样选择和替代关系逐项登记。 |
| 军鼓 | 38、40；鼓边 37 | 中心击打与鼓边采样独立；40 不能声称有独立电子军鼓实录。 |
| 六档通鼓 | 41、43、45、47、48、50 | 源库两只通鼓不足六只独立实录；如离线调音派生，记录来源、音高偏移和试听结果，表现可分成三只通鼓。 |
| 踩镲 | 闭 42、脚 44、开 46 | 共用一只实体、不同动作；闭镲和脚镲收掉先前开镲尾音。 |
| 吊镲 | 49、57；中国镲 52、Splash 55 | 不同发音可共用实体但保留各自采样身份；缺失的音色明确标记为替代。 |
| Ride | 51、59；镲帽 53 | 可共用 Ride 实体；镲帽为不同击打区域。 |

首批把上述常见套鼓键位做全，再补 35～81 的辅助打击乐。完整 GM 一级还有手拍、铃鼓、牛铃、邦戈等，八份试听不等于已经覆盖全部 47 个键位。未支持键位在 Editor 明确报告，并在 bank 标成未映射或明确替代，不可套钢琴最近音高算法把它变成错误鼓件。

发声规则：

- 普通鼓、镲为 one-shot；NoteOff 和 CC64 不截断自然尾音。仍消费 NoteOff 以维护输入状态，CC120 等明确清音命令、停止/切曲等清场继续有效。
- 同鼓连续 NoteOn 可以重叠，踩镲互斥独立处理；正式实现短淡出抑制爆音，不能让整组镲互相误截。为 DSP 预约预先确定 choke 时间，不能在预约开镲时提前截掉当前正在响的开镲。
- 声音生命周期与 MIDI 按键间隔分开。鼓的尾音终点必须进入恢复与调度索引，并纳入曲目播放终点，否则 MIDI 松键以后、曲终或暂停/定位时会提前消失。保留原始按键数据，不依赖虚构超长 NoteOff 修补整个序列。
- Seek / 晚加入可从采样真实年龄恢复仍有效的尾音；从检查点到目标位置解析 choke，已经被闭镲收掉的开镲不能恢复。恢复只还原当前状态，不重新演一遍旧的打击动作。暂停行为与现有播放器约定一致，恢复时不重复攻击音。
- 轮替样本由稳定事件身份（歌曲 noteId、实时已传输的事件序号）决定，避免各端独立随机。实时同音连击、踏板和预约不可共用一个全局递增轮替计数来决定结果。
- 64 声部满载时优先保住短促攻击音，再按有效响度和年龄选择尾音回收；具体容量和镲片保留量须混合钢琴与密集鼓点测量，不直接宣称够用。

## 表现层

新增 `MidiDrumKitView`，独立于 88 键 `PianoKeyboardView`；配一个固定池的 `MidiPercussionLaneView`。共享 `positionMs` / `applyGeneration`，状态仍由播放逻辑负责，View 不调用 NoteOn、不改声音或网络状态。

| 对象 | 建议动作（待视觉调校） | 模型拆分 |
| --- | --- | --- |
| 底鼓 | 击槌短促前摆/回弹，鼓面局部冲击光 | `KickBody`、`KickHead`、`KickBeaterPivot`、脚踏独立 |
| 军鼓、通鼓 | 鼓皮约 1～2 mm 短形变或材质波纹，60～120 ms 回弹 | 各鼓 `Shell`、`Head`、`Rim` 独立，支架不随鼓皮动 |
| Crash / Ride | 围绕镲心做小角度阻尼摆动，约 0.7～1.8 s，力度控制幅度 | `CymbalPivot` 位于镲心，镲片与支架独立；镲帽单独命中点 |
| 踩镲 | 开镲分开上下片，闭/脚镲合拢；叠加短余振 | 上下镲、中心杆、脚踏独立，开合与摆动用两级 pivot |
| 鼓棒 | 首版可做每鼓一段短击打轨迹；两根实体鼓棒的分配后续再加 | 棒独立 pivot；同一时刻多鼓不得因只有两根棒丢音或丢命中 |

瀑布流使用“鼓件轨道”，不按 88 个半音排布。建议先用底鼓、军鼓、踩镲、低/中/高通鼓、Crash、Ride 八轨；同实体的发音保留独立形状/符号，例如开镲用空心标记、闭镲用实心标记。使用短音块表示击打时点，长混响不画成长按条。音块落在各鼓件锚点，延续当前晶体音符和冲击风格；同时用轨道名/形状区分，不只靠颜色。

文件播放按歌曲时间计算击打年龄，实时 MIDI 按本地到达时间计算；不在收到未来声音预约时先演动画。对短卡顿可只补很新的命中，超过年龄窗口的音只恢复余振或静止态。切曲、停止、隐藏及代次变化要清光、回到模型基姿；暂停冻结歌曲时间下的运动。每件鼓保留固定长度的最近击打环形缓冲，避免快连击覆盖上一击，也避免为每击创建 GameObject / Animator / 材质实例。共享材质，按需更新少量 pivot 与命中属性，首版不增实时灯。

预览的俯视示意用于试听真实代表采样、查看鼓件落点、短促鼓面回弹、镲片余振及踩镲收音；不代表最终 3D 模型、正式力度层、GM 全映射或 Unity 的空间声效果。

已完成的检查仅为：8 份代表采样经 ffmpeg 转换、ffprobe 读取源元信息；Chrome 中解码/触发、两小节演示开始/停止，以及 736、360、320 像素宽度的页面检查。结果见 `Downloads/PercussionResearch/preview-check.json`。尚未人工听音，也未执行 Unity / Udon / VRChat 运行检查。

## 下一步实施顺序与验收边界

1. 人工试听并选择鼓音色；明确两只通鼓扩成六档 GM 的派生方案及缺失键位替代清单。
2. 准备 bank 与离线转换/测量报告；记录原采样、力度分区、变调量、轮替、尾音、峰值、SHA256、授权以及压缩/解码预算。
3. 接文件和实时 MIDI 的通道路由、one-shot / choke / 恢复；保护当前钢琴/马林巴切换，更新 catalogHash、校验器与接线。
4. 制作独立鼓件模型和八轨表现，接到统一时钟，保留当前 UI / 本地化风格。
5. 实施时再验证：核心 GM 键位、未知 note、同音密集重击、开闭镲、CC64/120、钢琴+鼓同播、密集复音、暂停/定位/曲尾/晚加入，以及真实电子鼓与 VRChat 多人。此清单尚未执行。
