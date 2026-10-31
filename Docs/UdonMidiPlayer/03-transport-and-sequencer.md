# 03 · Transport、统一时钟与音序器方案

本文为待实现工程设计，不表示类、接口或调度行为已在当前工程可用。公共语义遵循[统一约定](CONTRACTS.md)，数据边界见[曲目数据与导入](02-data-and-import.md)，声音细节见[钢琴音源](05-piano-audio.md)，权威提交见[多人同步](04-multiplayer-sync.md)。

## 一、职责与状态模型

`MidiPlayerFacade` 统一接受 UI 命令、应用确认快照及组织重建。`MidiTransportClock` 是唯一曲目时间来源；`MidiSequencer` 是唯一音符逻辑写入者；音频和视图读取同一时刻。任何 AudioSource 的播放时间、瀑布流物体位置都不反向驱动整曲进度。

公共状态仅 Stopped=0、Playing=1、Paused=2。本地 Ready/Rebuilding/Error/TakeoverPending 表示资源或流程状态，不能作为同步枚举。选曲存在、数据就绪与操作许可是进入本地命令的前提；空曲库保留 Stopped/0 并禁用播放入口。

| 命令/来源 | 当前状态 | 结果 |
| --- | --- | --- |
| Play | Stopped | 选中曲目从 0 开始 Playing |
| Play | Paused | 从冻结进度恢复 Playing |
| Play | Playing | 幂等，不重起曲目、不重复提交 |
| Pause | Playing | 先取当前进度，再转 Paused，冻结声音与视觉时间 |
| Pause | Paused/Stopped | 幂等 |
| Stop | 任意 | Stopped/0，保留选曲并清理音符和预约 |
| SelectSong(songId) | 任意 | 不同曲目校验后切换为该曲 Stopped/0；相同 songId 幂等 |
| 自然结束 | Playing | 到 playbackEndMs 后 Stopped/0；仅 Owner 发布终态 |

同一曲目的 SelectSong 保持现有状态，不重复停止、重启或提交；需要从头播放时使用 Stop 后 Play。无效 songId、未知状态、非有限网络时间或曲库不匹配进入本地可见错误，并终止该快照的声音应用；不能用相近下标播放另一首。停止与曲终回零后，单独保存最后进度用于 UI 诊断也不能使公共时钟继续前进。

## 二、统一时钟与一次取样

所有音符查询使用 int 毫秒；网络和 DSP 使用 double 秒。播放目标为 `anchorPositionSeconds + CalculateServerDeltaTime(nowServerSeconds, anchorServerSeconds)`，暂停为固定 anchorPositionSeconds，停止为 0。原始锚点或重新取样后的锚点可以互换，但发送等待时间只能计算一次。服务器时间用于玩家收敛，本地 `AudioSettings.dspTime` 仅负责将曲目时刻映射成预约时间。

每帧顺序读取一次 serverNow 与 dspNow，形成同一帧的时间对，再由 Clock 计算并缓存 `framePositionSeconds`。帧位置使用 `floor(framePositionSeconds*1000)` 并限制到 `[0,playbackEndMs]`；导入时使用的四舍五入不用于每帧时钟。`playbackEndMs=durationMs+PianoBankData.maxReleaseMs` 是包含最大释放尾垫的运行有效结束，UI 总时长也用该值。尾垫期间保持 Playing；仅检查点逻辑查询截到 durationMs，声音释放年龄继续增长。后续逻辑、声音、UI、瀑布流共享缓存，不能各自再次取样造成一帧内越过不同事件边界。

Playing 时本地输出必须单调。每 0.5 秒比较共享目标与本地相位：绝对误差不超过 20 ms 时保持；超过 20 ms 时每次最多修正 5 ms，落后时温和追赶、领先时减缓进度，不改变音高，也不逐帧倒退。2026-11-01 已删除误差达到 100 ms 就重建的规则；同曲 Playing 普通快照只更新目标锚点。主动 Seek 用网络 `seekToken` 明确通知重定位，曲目/状态变化及显式恢复仍可重建。Paused 不持续相位追赶，新的确认暂停位置需按恢复路径应用。

DSP 预约公式为 `targetDsp = dspNow + (eventMs/1000.0-framePositionSeconds)`。正常推进中首次处理的 Down 即使迟到也从采样开头起音，并把该声音的释放和结束时间顺延相同迟到量，不设 80 ms 起音门控，不因原采样/释放区间已过而直接丢弃；曲目时钟不等待。定位、晚加入和暂停恢复仍按真实年龄恢复，不重打历史起音。校准只调整未来映射；已经预约的事件若必须撤销，进入新代次重建，不能留下两套预约。

## 三、内部接口清单

以下均是计划中的同客户端 UdonSharp 方法，参数使用基础类型与具体 Behaviour 引用，不是网络 RPC。Bind 操作在初始化或切曲时调用。

| 计划接口 | 参数和责任 |
| --- | --- |
| `Clock.BindSong(MidiSongData song,PianoBankData bank)` | 派生 playbackEndMs 并设置时长边界 |
| `Clock.ApplyAnchor(int state,double positionSeconds,double serverSeconds)` | 校验并接纳自洽锚点，不做网络提交 |
| `Clock.SampleFrame(double serverNowSeconds,double dspNowSeconds)` | 缓存本帧进度及 DSP 映射 |
| `Clock.GetPositionSeconds()` | 返回统一缓存进度；序列化取样前须刷新时钟 |
| `Clock.GetState()` | 只读返回公共状态 int；仅 Clock 接纳锚点时写内部状态 |
| `Clock.GetFrameDspSeconds()` | 只读返回 SampleFrame 缓存的 DSP 时间，供退场清理与音频输出保护使用 |
| `Sequencer.BindSong(MidiSongData song)` | 绑定数据、清空游标与计数 |
| `Sequencer.RebuildAt(int positionMs,int generation)` | 从检查点建立截至该时刻的最终逻辑，不逐条起音 |
| `Sequencer.AdvanceTo(int positionMs)` | 消费所有到期事件，保持顺序 |
| `VoicePool.BindSong(MidiSongData song,MidiSequencer sequencer)` | 绑定只读数据与恢复集合 |
| `VoicePool.NoteDown(int noteId,int positionMs,int generation)` | 确认到期音符；已预约的同代次音符不得重复起音 |
| `VoicePool.NoteRelease(int noteId,int positionMs,int generation)` | 启动或定位制音包络，不立即清零声音 |
| `VoicePool.UpdateVoices(int positionMs,int generation)` | 计算释放包络、输出保护、自然结束与回收 |
| `VoicePool.PrepareWindow(int fromExclusiveMs,int toInclusiveMs,int generation,double songPositionSeconds,double dspNowSeconds)` | 对半开闭窗口内未来 Down 预约，不改逻辑计数 |
| `VoicePool.RebuildAt(int positionMs,int generation)` | 读取逻辑活动 ID 与有限尾音候选，定位声音 |
| `VoicePool.CancelGeneration(int generation)` | 撤销指定旧代次的预约、活动声部及任务 |
| `Facade.ApplyRemoteSnapshot(string catalogHash,string songId,int state,double anchorPositionSeconds,double anchorServerSeconds,int writerPlayerId,int snapshotSequence)` | 只应用已经网络层校验的快照，不申请 ownership、不提交 |

网络入口为 `MidiNetworkTransport._CommitLocalState()`。命令方法只负责本地许可和流程；实际 ownership、去重和失败撤销见同步分册。VoicePool 序列化持有 transportClock 引用，通过 GetState() 判断 Paused/Stopped，RebuildAt 不需要另传一份可能过期的状态。键盘刷新计划使用 `Refresh(int positionMs,int generation,bool force)`，读取 Sequencer 的 88 项 `keyDownCount`；瀑布流使用 `Refresh(int positionMs,int generation)` 读取曲目区间。重建后键盘 force=true、瀑布流先 RebuildAt；正常帧键盘 force=false。

## 四、命令与快照的执行顺序

本地命令路径为“许可检查 → 取当前统一位置 → 应用本地 Transport → `_CommitLocalState()`”。Playing 中 Play、非 Playing 的 Pause 等幂等命令在提交前返回。等待 ownership 时可以预览本地结果，但仍只有一个可替换待提交意图；新意图覆盖时取消旧代次并应用新的完整意图。申请失败或丢失 ownership 后才撤销推测并恢复最近确认快照，不能让旧意图的撤销覆盖刚产生的新意图。

重定位、切曲、停止或撤销统一按下列顺序进行：

```text
oldGeneration = applyGeneration
applyGeneration++
VoicePool.CancelGeneration(oldGeneration)
清空旧逻辑、高亮和可见音符，废弃旧待处理请求
必要时 BindSong，再 ApplyAnchor，再 SampleFrame
若 Playing/Paused：先 Sequencer.RebuildAt，再 VoicePool.RebuildAt
两次 RebuildAt 均传入 positionMs 与 applyGeneration
若 Stopped：保留空逻辑，不消费 0 ms 的 Down
Paused 的声部保持冻结；Stopped 不恢复声部
Playing/Paused：刷新键盘，瀑布流 RebuildAt 后再 Refresh
Stopped：键盘/瀑布流只 Clear，不调用会填充前视条带的 Refresh
刷新 UI
仅 Playing 准备新的 50 ms 未来窗口
```

远端反序列化先由网络层验证字段、发布者与曲库，再调用 ApplyRemoteSnapshot。相同曲目的 Playing 普通快照更新目标/校准，不因位置误差清空声部。曲目、公共状态、主动 Seek 标记变化或显式恢复才走完整路径；远端路径永远不调用 Command、SetOwner 或提交入口。

## 五、事件推进与逻辑状态

正常 Playing 帧固定顺序为 `SampleFrame → AdvanceTo → UpdateVoices → PrepareWindow → 键盘/瀑布流 Refresh → UI`。到有效结束则走自然停止，不再预约。Paused 仍取固定帧位置并刷新视图，但不推进事件、不预约未来 Down；UpdateVoices 按冻结位置维持包络并保持输出暂停。Stopped 仍执行 SampleFrame 和 UpdateVoices 的短退场维护段，其他时间只维护停止画面，不重复清池或反复提交；不处理 PendingStart、不刷新前视音符。

Sequencer 保存 `eventCursor`、`lastPositionMs`、当前 generation、88 项 `keyDownCount`、可查询的活动音符 ID 与状态数组。活动表示尚未 Release，包含已松键但受踏板延音的音符。物理按键计数与音频声部占用是两套信息；声音池偷音不能减少键盘计数。

AdvanceTo 从 cursor 消费 `eventMs<=positionMs` 的全部事件。Down 把该 noteId 标为活动，增加对应键计数并调用 NoteDown；Up 只减少对应计数；Release 将逻辑音符标为不活动并调用 NoteRelease。同音重复按压使用计数，不用一个布尔值覆盖；计数不得为负，越界 ID 或格式错误进入本地错误处理，不能让数组异常终止整个播放器后继续假装播放。

同一毫秒的一组动作完成后再对外刷新状态，顺序服从导入结果，零长度链可完整消费而不产生一帧错误高亮。cursor 始终指向首个尚未消费事件，正常推进结束为 `upperBound(eventMs,positionMs)`。positionMs 小于 lastPositionMs 时禁止倒序执行事件，必须重建。

声音预约拥有独立的窗口游标，不能推进 Sequencer 的逻辑 cursor。音频为 `(songId,contentHash,noteId,generation)` 保存独立于声音槽的状态，未来 Down 在逻辑到期前不点亮琴键。NoteDown 只确认已预约或等待空槽的音符；Suppressed/Finished 不重新起音，因此被窃声部不会因找不到槽而复活。每帧预约窗口为 `(positionMs,min(playbackEndMs,positionMs+50)]`，窗口重叠通过该状态表幂等，不重复分配声部。超过 durationMs 后不存在新 MIDI 动作，事件游标保持数组末尾。

## 六、帧卡顿与直接恢复

2026-11-01 已移除单帧推进 100 ms 时间门控及 256 条事件预算，并删除对应的直接重建分支。正常前进时逐条消费当前统一时间之前（含当前时刻）的全部到期事件，按导入顺序执行 Down、KeyUp 与 Release；不因帧耗时或事件数量跳过音符。同一批事件完整处理后再刷新表现。向后定位、主动 Seek、暂停恢复与网络重定位仍使用各自的重建路径。

重建使用数据分册定义的检查点前态，逻辑以 `t=min(positionMs,durationMs)` 消费到 t（含 t）但不逐条通知声音；最终获得 `start<=t<release` 的活动 ID和 `start<=t<keyUp` 的按键计数。尾音则在真实 positionMs 上查询 `[max(0,positionMs-maxReleaseMs),positionMs]` 的 Release 候选，包含边界后交音源剔除零增益结果。声音池根据年龄恢复仍可闻的样本，已结束的历史短音直接略过。逻辑可以处理完整区间而不受 64 声音池或 512 可见池容量限制。

即使发生帧卡顿，曲目时间也继续服从共享时钟，恢复音符不会把 Transport 暂停来等待声音。若客户端数据尚未就绪，暂时保持本地 Rebuilding；就绪后按最新快照重算目标，不能从收到第一份快照的旧位置开始播放。

## 七、代次取消、暂停与恢复

`applyGeneration` 是本地取消标记，不参与网络先后裁决。所有音乐延迟工作、预约确认和分帧重建执行前检查当前 songId、contentHash 与 generation；旧音乐任务即使晚到也立即失效。旧声部的短 Retiring 退场清理则核验 slotToken、槽位状态与其旧 generation，允许完成停止回收，不能因全局代次变化永久占槽，也不能清掉已重用槽位。Udon 延迟回调本身不携带任意参数，计划实现必须使用保存身份值的固定任务槽并在回调中检查，不能只在安排回调时检查一次。

RebuildAt 可先筛出最多 voiceLimit 个恢复候选。旧声部全部处于短退场时，将候选保存到有界 PendingStart 槽，不因暂时无空声部而丢失；UpdateVoices 完成退场后按最新时刻重新定位，已经自然结束或释放完的候选直接结束。新代次取消旧待起音集合，不能用旧年龄在数帧后启动。

暂停先读取当刻 t，再把公共状态切为 Paused；冻结曲目、可视位置和释放包络。暂停使用一次新的重建代次撤销未来 DSP 预约。实现可以保存现有声部源位置，也可以从 t 重建并保持 AudioSource 暂停，但不能让处于 Paused 的重建短暂发声。暂停时晚加入也直接建立该冻结状态。

恢复时以冻结进度和当前服务器时间建立 Playing 锚点，再建立新的 DSP 映射。先恢复活动声部及释放尾音的采样位置、音符年龄和释放年龄，再允许声音继续并预约未来 Down。恢复不能重新触发 Attack，不能把已经经历的 Release 包络从头播放。暂停期间过去的真实时间不得计入音符年龄或释放年龄；新的远端确认位置优先于本地保存位置。

停止/切曲立即取消全部预约并回收声音和表现池，可使用极短防爆音淡出，不等待制音尾部自然结束。自然曲终则先走完有效结束 playbackEndMs，在最后逻辑 Release 后保留最多 maxReleaseMs 的释放尾垫，再回到 Stopped/0。尾垫仍使用 Playing，不新增公共 Draining 状态。音源不得通过任意 AudioSource 的实际播放长度反向延长整曲时间；最大释放值必须与 bankHash 对应，所有客户端由相同数据派生同一结束边界。

## 八、实现边界

上述调用顺序依赖 M0 确认 Udon 数据烘焙、DSP 调度、样本定位与取消的实际行为。若 DSP 预约不可用，需显式记录降为逐帧触发及精度变化；同一 Transport、数据区间、generation 和恢复语义仍须保留。本次未创建运行脚本、测试或测试结论，也未扩展拖动、变速或外部音符事件 API。
