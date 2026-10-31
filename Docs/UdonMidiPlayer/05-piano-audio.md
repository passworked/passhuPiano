# 05 · 钢琴音源、复音与恢复

## 当前实现补充（2026-10-31）

以下旧章节保留最初设计；当前高密度播放策略以此补充和 Runtime 源码为准。

- PC、Android、iOS 统一使用 64 个声音槽，默认 `voiceLimit=64`、配置范围 1～64；以 `PianoVoicePool.VoiceCapacity` 作为共同容量。运行时不再额外设置平台预算。主场景、练习场景、示例场景和两份 Prefab 均清理了多余 AudioSource，保留前 64 个源的引用；实时 MIDI 状态、待起音数组、构建器及校验同步为 64。
- 工程 AudioManager 的真实声部数设置为 64。VRChat SDK 3.10.5 的 `EnvConfig.SetAudioSettings()` 会写入桌面 32/64、移动端 24/32 的真实/管理默认值；这些工程默认值不能证明客户端硬上限，也不能通过修改工程配置保证客户端采用相同数值。当前未找到官方对双端 64 个真实声部的明确保证，客户端仍需实测。Udon 不调用未暴露的 `AudioSettings.GetConfiguration/Reset`，不修改 SDK 包或加入自动覆盖 SDK 的钩子。
- 正常播放提前预约 150 ms。满池时未来音符暂不占槽、不抑制、不提前打断当前声音，后续窗口及到期回调继续尝试。到期起音可立即回收较旧/较弱的声音，避免等待一帧退场再错失起音；压力下可能截短旧尾音。被撤销的未起音预约可以重试，已响过而被回收的旧音不会复活。
- 2026-11-01 已移除单帧推进 100 ms 时间门控、256 条事件预算、本地校准及同曲 Playing 快照误差 100 ms 自动重建、80 ms 起音限制。卡顿或密集事件帧均逐条消费全部到期事件；首次处理的迟到 Down 从采样开头起音，释放与采样结束随迟到量顺延，不因原可闻区间已过而直接丢弃。共用链、钢琴/弦乐独立路径及鼓组一致；补起音方法也不再截取最近 80 ms 或最多 128 音。曲目时钟继续前进，64 个物理源、鼓组容量与窃音规则保持不变。主动定位、中途加入、暂停恢复保持按真实年龄恢复，不重打历史起音。
- 声音槽缓存增益和采样结束时刻；关闭峰值保护时，单次起音只更新自己的音量，不再逐音重写整个声音池。运行时额外暴露 `effectiveVoiceLimit` 与 `virtualVoiceCount` 供编辑器诊断，玩家 UI 不增加技术信息。
- 2026-11-01 起音门控移除已通过 C# / UdonSharp 编译及 Editor / ClientSim backing-Udon 定向检查：迟到 10 秒且原释放区间已过的钢琴、弦乐与鼓音仍起音，释放/结束时间有效，重复 Down 不重起；定位恢复仍保留真实年龄；普通大误差快照不重建，新 Seek 标记重建且补发不重复。独立钢琴/弦乐路径也已检查。记录见 `Logs/LateNoteGateRemoval-validation.json`，不等同于听音、真实 VRChat 双客户端或移动端验收。
- 已完成 C#、UdonSharp 编译以及真实 backing-Udon VM 的定向检查（满池、预约重试/幂等、300 事件短音恢复、Seek 采样年龄、实时 MIDI 混用和暂停）。64 声部迁移记录见 `Logs/Voice64-migration.json`，运行检查见 `Logs/Voice64-validation.json`；不等同于音频采集、VRChat 双客户端或移动端验收。超过实际可用真实声部的同刻和弦仍可能被客户端裁减。
- Unity Editor 实测音频配置为真实/管理 64/64，低音量测试中 64 个 AudioSource 均 `isPlaying=true`、`isVirtual=false`；这是编辑器音频状态证据，不是 VRChat 客户端录音或硬上限证明。此前将 SDK 工程默认值直接当作客户端上限的说法撤回。

本册定义 P0 的本地钢琴发声后端。模块边界以 [统一约定](CONTRACTS.md) 为准，输入来自 [时钟与 Sequencer](03-transport-and-sequencer.md)，网络只提供 Transport 状态，见 [多人同步](04-multiplayer-sync.md)。以下数量、时长和增益均为起始设计，尚未经过 Unity 或 VRChat 验证。

## 1. 音源交付与区域映射

MIDI 不含声音；可分发、可在世界中打包使用的 Piano Bank 是 MVP 的交付依赖。首版使用同一套自然衰减、无循环的钢琴采样，约 24 个音区、单力度层。力度通过增益响应，不把单层采样表述为真实多层钢琴。v0.1 按 PC 验证；未来如提供 Android 构建，仍须采用同一 bank 身份、采样内容与映射，并单独验证平台导入差异。本册不增加 Quest 专项方案。

每个目标音高 21～108 必须唯一映射到一个区域；根音覆盖 A0～C8，优先选择最近根音，等距时固定选较低根音。相邻根音间距优先为 3～4 个半音，最大移调优先控制在 ±3 半音；若源素材不能满足，由编辑器显示具体音高和移调量，验收前补齐，不靠静默跨大音区掩盖缺样。每个区域的 low/high 为含端点的 MIDI 音高区间，全部区间连续、互不重叠；边界按相邻根音中点生成后保存，运行时不反复搜索音源。

曲目保留 MIDI 0～127 的原音高。低于 21 的音符使用 21 对应区域，高于 108 的音符使用 108 对应区域，再按原音高与根音的差值变调；只钳制选样索引，不折叠八度或改变曲目音高。瀑布流按黑白键排列延伸到键盘两侧，域外音符不按下实体 88 键。

`PianoBankAsset` 是编辑器配置资产；打包时烘焙为 None 同步模式的运行时组件 `PianoBankData`，保存下列 AudioClip 引用及基础类型一维数组，不能把运行时读取普通 ScriptableObject 当作已验证路径。

| 字段 | 类型与约束 |
| --- | --- |
| `bankHash` | string；覆盖采样内容与所有影响发声的配置 |
| `clips[]` | AudioClip 引用；每区域一个，非循环、已就绪 |
| `rootPitch[]`, `lowPitch[]`, `highPitch[]` | int[]；长度等于区域数 |
| `sourceRate[]`, `sourceChannels[]`, `sourceFrameCount[]` | int[]；使用实际导入后 AudioClip 元数据，帧数按每通道采样帧计算，不能混用原 WAV 与重采样后的帧率 |
| `sourcePeak[]`, `regionGain[]` | float[]；真实采样峰值的保守上界、区域响度校准增益 |
| `releaseDurationMs[]` | int[]；区域制音释放时长，默认 180 ms，初始上限 300 ms |
| `pitchToRegion[88]` | int[]；下标为 pitch−21 |
| `maxReleaseMs` | int；等于各区域释放时长最大值，供恢复查询 |

编辑器验证数组长度、连续覆盖、根音有效、无空引用、无 NaN/Infinity、增益非负、释放时长大于零。`bankHash` 纳入曲库构建哈希；每个客户端使用相同 bank 才能保证逻辑恢复与声音寿命一致。本地音量和静音不参与哈希。

编辑器与运行时还验证 MIDI 0、127 的边缘变调倍率大于 0 且不超过 `AudioSource.pitch` 的 3 倍上限。不满足时拒绝该音源并提示调整边缘根音或调音配置，不能通过钳制倍率改变目标音高。域内 ±3 半音限制继续适用；域外允许使用边缘采样跨更大音程。

## 2. 一次发声对应一个 noteId

`PianoVoicePool` 默认预置 64 个 AudioSource，每个槽位记录 `noteId/generation/region/scheduledDsp/startMs/releaseMs/state/slotToken`，每次借出槽位都递增 slotToken。同一琴键可以同时拥有多个槽位；不能让一个 voice 固定绑定一个 key，否则同音重叠会被后一个音覆盖。按键亮灭只读 Sequencer 的物理按键计数，与声音池分配、窃音、踏板持续发声分别处理。

KeyDown 建立或确认声部，KeyUp 只改变按键逻辑，Release 才启动制音包络。CC64 已烘焙为 releaseMs，音频后端不再解释踏板事件。声音池以 `(generation,noteId)` 幂等：已预约的 KeyDown 到达逻辑时间时只确认，不二次 Play；Release 即使晚一帧，也使用事件原始时间计算正确包络年龄。

另预分配独立于 voice 槽位的 `audioNoteGeneration[]/audioNoteState[]`，按 noteId 索引；当前代次无记录视为 Unseen，其后只进入 Scheduled、Sounding、PendingStart、Suppressed 或 Finished。Release 属于声部的 Releasing 状态，不重新创建 noteId。被窃音或因预算拒绝的音符标 Suppressed，自然/释放结束者标 Finished；这些记录保留到代次更换，即使原 voice 已复用，重复窗口和逻辑 NoteDown 都不得重新起音。PendingStart 的 NoteDown 也只确认已有候选，不再入队；表只记录音频处理，不改变 Sequencer 的逻辑活动及按键计数。

P0 不改变播放速度，音高倍率为：

```text
region = pitchToRegion[clamp(pitch, 21, 108) - 21]
pitchRatio = 2 ^ ((pitch - rootPitch[region]) / 12 + tuningCents[region] / 1200)
noteAgeSeconds = max(0, (positionMs - startMs[noteId]) / 1000.0)
sourceFrame = floor(noteAgeSeconds * pitchRatio * sourceRate[region])
```

采样播放速度使用 pitchRatio；定位使用源采样帧，不能只把 noteAge 直接当原音频偏移。`sourceFrame >= sourceFrameCount` 即自然完结，不重新发声，也不因 MIDI 仍按住而循环采样。音色本身自然衰减，长踏板不能制造不存在的持续能量。源素材开头只保留可控的极短预留，禁止每个音带不同长度的明显前导静音。

初始力度曲线为 `(velocity/127)^1.6`，乘以 regionGain 和本地总音量；这是音量响应，不额外随机化时长、移调或起音。Release 后令 `u=clamp01((positionMs-releaseMs)/releaseDurationMs[region])`，制音增益 `G=(1-u)^2`；Release 前 G=1。u=1 或采样自然完结即回收。停止、切曲和撤销代次立即撤销旧预约，已发声槽位只允许下节定义的短去点击退场；正常 Release 使用上述包络，避免把 NoteOff 直接处理成硬切声音。

## 3. DSP 前视与旧任务取消

时钟每帧成对采样 `songPositionSeconds` 与 `dspNowSeconds`，同一对值供预约使用。Playing 时只查看前方 50 ms 内的 KeyDown：

```text
targetDsp = dspNowSeconds + startMs[noteId]/1000.0 - songPositionSeconds
```

窗口不推进 Sequencer 的事件游标、按键计数或瀑布流命中。首次预约负责选样、占槽和设置 pitch/偏移/增益；越过预约时间后的 NoteDown 不再重播。voice 槽位状态区分 Free、Scheduled、Sounding、Releasing、Retiring，与独立音符状态表分开。已过期但尚有可闻内容的音符按当前 noteAge 定位后启动，不把迟到音符一律从 attack 播起；已自然完结或释放完者标 Finished，不发声。

停止、暂停、切曲、重定位、远端快照重建与撤销推测操作均先增大 applyGeneration，再取消旧代次及 PendingStart。取消必须真正停止 Scheduled 槽位的底层预约，不能只清空 C# 标志；槽位在取消完成前不借给新音符。已发声槽位可进入 Retiring，保留短去点击退场后停止回收；3～5 ms 只是目标时长，主线程包络受帧粒度限制，不能承诺精确 5 ms，必须在 M0 实听。

Retiring 记录 `slotToken/slotOldGeneration/retireEndDsp` 及取消瞬间的输出增益，只在该增益上按本地 DSP 时间退场，不再读取新曲数组计算旧声音包络。清理按槽位身份与原代次核验，允许 global generation 已更换；不能因旧代次而跳过必要回收，也不能操作已复用槽位。它只退场和释放本槽，不执行旧音符事件或改旧逻辑。普通音乐延迟任务仍须核验当前 generation、songId 与 Playing，不保留每音符一个延迟事件的无界队列。Release 包络按曲目时间计算，暂停后不继续制音。

具体 DSP 预约、源帧定位、取消后重用的 Udon 可调用性与客户端行为列为 M0 验证项。优先采用 DSP 调度；若不可用，必须在 [性能与验证](09-performance-and-validation.md) 记录逐帧触发退化与新的精度目标，不能静默更换。

## 4. 暂停、晚加入与尾音恢复

暂停保存唯一冻结进度，并取消未来预约、停止输出；音符年龄和 Release 包络年龄都停在该进度，现实时间不参与其增长。恢复播放使用新 generation 的 `RebuildAt`，重新定位到冻结源帧与当前 G 后续播。可添加短去点击淡入，目标约 3～5 ms、实际受帧粒度限制，但不能重置采样位置或重新播放 attack；该淡入不改变曲目时间。

恢复查询由 Sequencer 提供两类候选：当前 `start<=position<release` 的活动音，以及 `position-maxReleaseMs<=release<=position` 的尾音。声音池逐个按区域的 releaseDurationMs 复核释放完结，并按 sourceFrame 复核自然完结；不能仅恢复 checkpoint 中尚未 Release 的音符，否则中途加入会丢掉刚松键的尾音。查询使用检查点和有限 Release 索引，不从曲首重放所有历史 NoteOn，详见 [数据与导入](02-data-and-import.md)。

Paused 状态晚加入只重建逻辑、冻结音频恢复所需状态，不输出声音；随后 Play 才按冻结进度恢复。Playing 状态晚加入从当前网络目标时间直接定位，不等待播放曲首，也不把旧曲攻击音补播一遍。`durationMs` 保持为 MIDI 逻辑末尾，包含源 EOT 与最后 Release；运行时另派生 `playbackEndMs=durationMs+PianoBankData.maxReleaseMs`。尾垫期间保持 Playing、处理有限释放尾音，Clock、UI 和自然曲终统一采用 playbackEndMs，结束后才 Stop/0，不新增 Draining 公共状态；用户主动 Stop 直接清逻辑并执行短去点击退场。不同客户端的音频驱动仍可能存在输出延迟，统一时钟不等于硬件采样级锁相。

## 5. 复音预算、窃音与峰值保护

64 是池容量，包含未来 50 ms 内已预约及正在短退场的槽位，不另建隐藏的额外声部，也不保证 64 个声音都能实际混音。世界应在 Unity/VRChat 全局 real voices 预算中给播放器留出配额，并为其他世界音频留余量；Prefab 不自动修改项目全局音频设置。`voiceLimit` 可下调，必须不大于池容量和场景约定配额。全局虚拟化、音频优先级与极密曲目效果需要实机确认。

满池先回收自然结束及包络结束者；仍不足时按“释放中且估计输出最小→其余输出最小→较旧 noteId”的稳定规则窃音。被窃音立即在音符状态表标 Suppressed，不伪造 KeyUp/Release，不清其他同键声部；voice 短退场后才复用。重建先按相同评分选最多 voiceLimit 个音符，未选中者标 Suppressed，不因扫描顺序改变可闻结果。

另预分配容量不大于 voiceLimit 的 PendingStart 候选槽，只存 noteId、generation 和所需时刻，不增加 AudioSource。重建时若全部 voice 尚在 Retiring，已选候选保留为 PendingStart，不能因短暂无空槽而永久丢失；正常窃音交接同样使用该槽。候选满时按同一稳定评分取舍，被拒或替换者标 Suppressed，禁止重复窗口重新争抢。

每帧 `UpdateVoices` 先读取 Clock 缓存的 DSP 时间，结束可回收的 Retiring，再把空 voice 分给当前代次 PendingStart。实际启动前按最新曲目位置重算 noteAge、sourceFrame、释放年龄与增益；已过期者标 Finished，尚未到起音者按最新 DSP 映射预约，迟到者定位后起音。Paused/Stopped 不启动候选，但仍执行退场清理；新代次先清旧候选。暂停后立即恢复或整池重建时，仍可闻的候选可在退场完成后起音，旧预约不能覆盖新声部。

峰值保护先统一素材响度与峰值，再计算活动、已预约及短退场槽位的保守峰值和：`S=sum(velocityGain*regionGain*G*sourcePeak)`，目标总线系数 `min(1,0.8/max(S,epsilon))`。所有新增发声，包括预约、迟到起音、PendingStart 与恢复声部，都先计入 S 并设置保护增益，再允许输出；系数下调立即生效，上调约 80 ms 平滑。密集和弦整体降低音量，不能把 64 条 voice 的默认音量直接相加。此保护只控制播放器输出，最终世界混音仍需检查削波；不加入外部 DSP 插件。

## 6. 计划接口与 Prefab 接线

所有接口均为同客户端具体 Behaviour 方法，参数并非跨网络 RPC。

| 方法 | 参数及责任 |
| --- | --- |
| `BindBank(PianoBankData bank)` | 初始化区域数组、AudioClip 引用及 maxReleaseMs；拒绝无效 bank |
| `BindSong(MidiSongData data, MidiSequencer sequencer)` | 更换只读曲目和恢复查询来源；调用前取消旧代次 |
| `PrepareWindow(int fromExclusiveMs,int toInclusiveMs,int generation,double songPositionSeconds,double dspNowSeconds)` | 窗口为左开右闭，预约 KeyDown；检查去重与当前代次 |
| `NoteDown(int noteId,int positionMs,int generation)` | 查独立状态表；确认已有预约/候选，Unseen 才尝试起音，Suppressed/Finished 不复活 |
| `NoteRelease(int noteId,int positionMs,int generation)` | 使用数据中的 releaseMs，更新制音状态，不改变逻辑数据 |
| `UpdateVoices(int positionMs,int generation)` | 每帧均调用；先按缓存 DSP 清 Retiring，再仅于 Playing 分配 PendingStart，更新包络/保护/回收 |
| `RebuildAt(int positionMs,int generation)` | 读取绑定 Sequencer 候选，选择后保存为 PendingStart 或定位恢复；Paused 不发声 |
| `CancelGeneration(int generation)` | 撤销指定代次底层预约与 PendingStart；已发声声部退场，槽位按 token 回收 |
| `SetLocalOutput(float volume,bool mute)` | 唯一本地音量/静音入口；volume 限定 0～1，静音不改变时钟和逻辑 |

Prefab 暴露 PianoBankData、MidiTransportClock、64 个 AudioSource 引用、voiceLimit、本地音量默认值、峰值预算和只读诊断计数。声音池通过 `clock.GetState()` 判断播放状态，通过 `clock.GetFrameDspSeconds()` 读取同帧 DSP 缓存，不再次独立取样；Paused 重建只保存恢复候选。AudioSource 关闭 playOnAwake/loop，默认使用世界内 3D 声音，可本地切换 2D。`SetWorldSpaceAudio(false)` 对整个共用声音池设置 `spatialBlend=0`、`spatialize=false`、`bypassReverbZones=true`，避免玩家移动或转头改变响度与声像；切回 3D 恢复各源原有空间化和混响设置。UI 只调用 SetLocalOutput，不直接改槽位音量。编辑器检查引用唯一、无缺失、无额外独立播放脚本，参见 [编辑器与 Prefab](08-editor-and-prefab.md)。

## 7. 资源验收与契约补充

资源交付需附来源、作者、许可和随包再分发范围；逐区域检查起音、无爆音、自然尾部、底噪、跨区响度和 ±3 半音内移调效果。导入配置应支持预加载及定位，具体解码方式由 M0 测定；不能默认流式/压缩采样适合频繁随机定位。

内存预算按解码后大小计算：`总帧数×声道数×解码字节数`。例如 24 区域各平均 8 秒、44.1 kHz、单声道，若解码为 float32，约 32.3 MiB；立体声约 64.6 MiB，另加元数据、AudioSource 和平台开销。WAV PCM16 文件约一半大小，不能拿磁盘体积直接当运行内存；不同采样时长及真实导入结果要在报告中替换示例。

本册补足统一契约必须明确的 bankHash、区域释放时长、maxReleaseMs、帧定位和自然完结语义；这些值需与构建哈希、恢复窗口一起版本化。验收覆盖同音重叠、踏板松开、暂停发生在起音/释放/预约之间、暂停加入、长音加入、取消后槽位复用及满池，判据详见 [性能与验证](09-performance-and-validation.md)。本次仅落盘设计，不生成音频、不修改工程音源或全局设置，也未执行上述验收。
