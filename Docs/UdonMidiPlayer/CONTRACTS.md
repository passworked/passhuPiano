# 接口契约记录（含 v0.1 历史设计）

阅读提示：本文保留了 2026-10-23 的计划、初始 P0 / P1 范围和早期字段 / 预算，不应直接作为当前 API 清单。先读[当前实现](../Developer/CURRENT_STATE.md)与本页最新分日期更新，再核对实际源码；最新维护边界见[第 17 册](17-maintenance-conventions.md)。新增乐器的收录政策与硬上限以[已确认贡献规则](../Contributing/README.md)为准。未知或冲突项应报告，不能复制旧接口或用历史测试目标覆盖当前规则。

## 2026-11-01 四把提琴独立奏法

新增完整快照字段 `stringArticulationOverrides`（int，0～255）：目标 ID 2～5 分别占低位起的两位，值 0 自动、1 长弓、2 跳弓、3 拨弦；默认全自动。pending / confirmed / flight、重复快照判断、接收校验、初始状态、晚加入、交接及回滚均保存该字段。原 `stringsArticulation` 保留为自动模式的共享默认和独立旧链兼容。手动选择优先于 GM 奏法推断，文件、定位恢复、实时 / 显式目标输入和运弓使用同一目标查询；由 Facade 校验权限后提交完整状态。路由规则版本为 `selected-gm-v4-independent-string-style`，进入 catalogHash。

## 2026-11-01 多音色选择补充

主场景增加七位 enabledInstrumentMask（0～127，0 表示无旋律音源）与 pianoAppearance（0 数码外观、1 三角外观）。两者进入网络完整快照及 pending/confirmed/flight；instrumentIndex 保留为现有显示索引，不再代表完整发声集合。目标 ID 0～6 与菜单 0～7 分离。分组模板、例外、逐音 Program 与 64 源共享分配见第 19 册。


日期：2026-10-23。本文是各分册的共同接口契约；修改字段、时间单位、状态语义或权威边界时，先修改本文，再同步受影响分册。

本文中的类名、字段和默认参数是待实现设计，不表示工程已经存在对应脚本。当前基线为 Unity 2022.3.22f1、VRChat Worlds SDK 3.10.5 与其内置 UdonSharp。

## 1. 范围与目录

交付 P0：编辑器批量导入、钢琴复音、播放/暂停/停止/切歌、88 键瀑布流、多人及晚加入、零代码配置 Prefab。P1 的拖动、变速、粒子、搜索/随机/歌单、Quest 专项优化、外部音符事件 API 不实现，只保留内部边界。

计划实现目录为 `Assets/UdonMidiPlayer/{Editor,Runtime,Prefabs,Audio,Materials,Meshes,Samples}`。本次落盘的是 `Docs/UdonMidiPlayer` 下的设计文件。

项目自有 C# 类型全部显式位于 `MusicMidiPlayer` 根命名空间：运行侧按 Data/Logic/Networking/Audio/Views/UI 分组，编辑器类型按 `MusicMidiPlayer.Editor.*` 分组。本文及各分册的短类名对应关系以 [命名空间与分发约定](12-namespaces-and-distribution.md) 为准，不允许在全局命名空间创建同名实现。计划程序集为 `MusicMidiPlayer.Runtime` 和仅 Editor 的 `MusicMidiPlayer.Editor`，自定义 Udon 程序集注册列入 M0 验证。

## 2. 模块及单向依赖

| 计划模块 | 职责 | 网络模式 |
| --- | --- | --- |
| `MidiSongAsset` / `MidiLibraryAsset` | Editor 源资产、元数据、导入设置、诊断 | 不进入 Udon 运行逻辑 |
| `MidiSongData` / `MidiLibraryData` | 烘焙后的基础类型一维数组和曲目查找 | 本地、无同步 |
| `PianoBankAsset` / `PianoBankData` | Editor 音源源资产，以及烘焙后的采样引用与区域配置 | 运行数据本地、无同步 |
| `MidiControlGate` | 本地操作许可；默认开放，外部系统可改变许可 | 本地、无同步 |
| `MidiPlayerFacade` | UI 的唯一命令入口，协调本地操作与提交 | 本地、无同步 |
| `MidiNetworkTransport` | 当前 Owner 发布完整快照、补发、心跳与交接 | Manual Sync，独立 GameObject |
| `MidiTransportClock` | 统一曲目时间、服务器时间与本地 DSP 时间的映射 | 本地、无同步 |
| `MidiSequencer` | 音符生命周期、事件游标、恢复查询、按键引用计数 | 本地、无同步 |
| `PianoVoicePool` | 采样选择、复音分配、定位、释放及回收 | 本地、无同步 |
| `PianoKeyboardView` / `MidiWaterfallView` | 88 键高亮、可见音符池、绝对时间定位 | 本地、无同步 |
| `MidiPlayerUI` | 标题、状态、曲目选择、控制、本地音量与静音 | 本地、无同步 |

仅网络组件写同步字段；仅时钟推进曲目时间；仅 Sequencer 改音符逻辑状态。音频与画面不能反向推进 Transport。各无同步 Behaviour 放在与网络组件不同的子对象。

## 3. 运行时曲目格式

`songId` 为生成曲目资产的稳定 GUID 字符串，重导入保留生成资产与 `.meta`。`catalogHash` 是对有序曲库 ID、内容哈希、数据格式版本及 `bankHash` 计算的构建期哈希；不同哈希的客户端不按曲库下标猜测曲目。bankHash 覆盖采样资源身份和影响发声的音源配置，本地音量/静音不纳入。源文件 GUID 与导入设置作为重导入映射，不以文件名作唯一 ID。

运行时统一使用毫秒整数；MIDI tick 在 Editor 通过完整 tempo map 转成毫秒。服务器时间及 DSP 映射使用 double 秒，进入音符查询时再换算毫秒。通道为 0～15，音高保留 MIDI 0～127；实体钢琴范围为 21～108（A0～C8），范围外音符按黑白键排列延伸到键盘两侧。

| 曲目字段 | 类型/语义 |
| --- | --- |
| `songId`, `title`, `contentHash` | 字符串 |
| `schemaVersion` | 数据格式版本，v0.1 为 1 |
| `durationMs` | MIDI 逻辑结束，覆盖源文件 EndOfTrack 及所有 release 事件；不写入音源尾音长度 |
| `startMs[]`, `keyUpMs[]`, `releaseMs[]` | int[]；每个音符的开始、物理松键、启动制音释放时间；`start <= keyUp <= release` |
| `pitch[]`, `velocity[]`, `channel[]`, `trackIndex[]` | int[]；相同数组下标即 `noteId`，noteId 仅在该曲目内容版本内稳定 |
| `eventMs[]`, `eventKind[]`, `eventNoteId[]` | int[]；已经稳定排序的事件流 |
| `checkpointEventIndex[]` | 每 1000 ms 检查点对应的首个 `eventMs >= checkpointMs` 的索引 |
| `checkpointActiveOffsets[]`, `checkpointActiveNoteIds[]` | CSR 扁平索引，检查点前已开始、尚未释放的音符；详见数据分册 |

事件类型固定：`KeyDown=0`、`KeyUp=1`、`Release=2`。`Release` 是释放包络的起点，不等于立即静音。KeyUp 更新按键计数，Release 更新音频；CC64 已在 Editor 烘焙为 releaseMs。无踏板时 keyUpMs=releaseMs。Editor 先按 `(absTick, trackIndex, sourceEventIndex)` 建立 sourceOrdinal，派生事件按 `(timeMs, sourceOrdinal, subOrder, noteId)` 稳定排序；零时长事件保留同一音符的 Down→Up→Release 因果次序。同毫秒的整批事件应用完成后才刷新表现。

导入至少支持 SMF Type 0/1、PPQ、曲内 tempo 变化、NoteOn velocity=0、同通道同音重叠的 FIFO 配对、分通道 CC64。默认合并选中的旋律轨，排除 MIDI 通道 9 的打击乐；保留完整 0～127 音高，88 键音域外的音符使用最近边缘采样变调播放原音高，并统计提示。旧资产须重新导入源 MIDI 才能恢复此前跳过的音符。Type 2、SMPTE、损坏文件明确报错；程序变更/弯音等未实现表达给出诊断，不假装还原。

## 4. Transport 状态及命令

公共状态固定为 `Stopped=0`、`Playing=1`、`Paused=2`。Ready、Rebuilding、Error、TakeoverPending 是本地状态，不作为所有客户端必须同时达成的公共状态。

运行派生有效结束时间 `playbackEndMs = durationMs + PianoBankData.maxReleaseMs`，为末尾释放包络保留安全尾垫。时钟上限、自然曲终和 UI 总时长使用 playbackEndMs；该值由相同曲目和音源配置得到，不额外同步。尾垫期间公共状态仍为 Playing。主动 Stop 不等待尾垫。

公共命令为 `CommandPlay()`、`CommandPause()`、`CommandStop()`、`CommandSelectSong(string songId)`。这些是同一客户端内的 UdonSharp 方法调用，不是跨网络带参 RPC。曲目选择默认切到新曲并保持 Stopped；用户随后播放。Playing 中点 Play、Paused 中点 Pause 幂等；Stop 回到 0；自然曲终在 playbackEndMs 回到 Stopped/0，保留选曲，所有端自然停止，只有当前 Owner 发布终态。

再次选择当前 songId 幂等，不隐式停止或重启。Stopped 只执行键盘/瀑布流 Clear 和 UI 刷新，不调用会填充前视音符的 RebuildAt/Refresh。

用户命令路径：权限检查 → 本地 Transport 操作 → 网络提交入口 → 必要时申请 ownership → 确认本地仍为 Owner 后请求序列化。取得所有权前只有一个可替换的本地待提交意图，不创建发给固定 Owner 的播放命令队列。失败或失去 ownership 后取消待提交并服从已确认快照。

远端路径：`OnDeserialization` → 校验曲库和字段 → `ApplyRemoteSnapshot` → 本地曲目/状态/时间恢复。此路径不得调用 Command、SetOwner 或再次提交。

## 5. 同步快照

| 字段 | 类型/用途 |
| --- | --- |
| `catalogHash` | string，校验同一曲库构建 |
| `songId` | string，空曲库时可为空 |
| `state` | int，0/1/2 |
| `anchorPositionSeconds` | double，基准曲目进度 |
| `anchorServerSeconds` | double，对应的服务器时间 |
| `writerPlayerId` | int，发布者；交接期间结合实际 Owner 判断 |
| `snapshotSequence` | int，发布者本地递增序号，供同一 ownership 周期内去重 |

播放时 `targetSeconds = anchorPositionSeconds + Networking.CalculateServerDeltaTime(nowServerSeconds, anchorServerSeconds)`；暂停固定 anchorPositionSeconds；停止固定 0。P0 速度为 1。实际字段在 `OnPreSerialization` 从统一 Transport 时钟取样，生成自洽快照；原有锚点与当前进度表示可互换，但不能重复累加序列化等待时间或网络传输时间。

snapshotSequence 不是跨 Owner 的全局版本号，不承诺并发操作全序或每次操作都被保留。发生并发时以最终取得 ownership 的发布者快照收敛；旧提交失败需要重同步。ownerId/sequence 只能辅助验证及去重，不能替代 VRChat ownership。本地 `applyGeneration` 每次停止、切曲、重建、撤销推测操作或重定位时递增，取消旧音符任务和 DSP 预约，不参与网络裁决。

## 6. 时钟、恢复和默认参数

共享目标时间决定统一 Transport 时钟；本地 DSP 锚点用于音频预约，不能从任意一个采样 AudioSource 的播放位置反推出整曲时间。每帧取一次统一曲目时间并提供给逻辑、音频及画面。

| 参数 | 初始设计值 | 说明 |
| --- | --- | --- |
| checkpoint 间隔 | 1000 ms | 导入期生成，恢复期间使用 |
| 本地校准检查 | 0.5 s | 不等于每次发送网络包 |
| 小偏差免修正区 | 20 ms | 初始实验参数 |
| 播放误差自动重建 | 已移除 | 2026-11-01 删除 100 ms 阈值；普通 Playing 快照只校准，主动 Seek 使用 seekToken |
| 心跳发送间隔 | 3 s | Playing 且没有新快照时发送轻量 Owner 心跳 |
| 失活接管 | 6～10 s 随机退避 | 仅本地数据就绪且 Playing 时；复查最后活动后再尝试 |
| 音频前视预约 | 50 ms | 本地 DSP 预约窗口；需 M0 验证 |
| 默认声音池 | 64 voices | 可配置，实际可闻复音受 Unity/VRChat 全局音频预算影响 |
| 默认可见音符池 | 512 条 | 不足时只降级表现 |
| 瀑布流前视 | 3 s | 局部坐标与配置高度决定速度 |

参数均为设计起点，不是已经测试通过的指标。播放误差只逐步调整本地时钟相位，保持时间单调，不因误差大小重建；主动 Seek、曲目/状态变化及显式恢复保留重建语义。正常推进首次处理的迟到 Down 保留完整起音并顺延该声音的生命周期，不设 80 ms 起音门控；定位恢复仍按真实年龄查询。

恢复使用音符区间、检查点和有限长度的 Release 事件窗口，直接重建当前状态，不回放全部历史 NoteOn。音源按音符年龄和移调倍率定位采样；已自然衰减完的采样不重新起音。尾音候选窗口使用音源的最大释放时长，不把音源相关尾音写死进 MIDI 数据。

PianoBankData 包含 `clips[]`、`rootPitch[]`、`lowPitch[]`、`highPitch[]`、采样率/帧数/峰值、`regionGain[]`、`releaseDurationMs[]` 及派生 `maxReleaseMs`。移调倍率为 `2^((pitch-rootPitch)/12)`；恢复帧位置为 `floor(noteAgeSeconds * pitchRatio * sourceRate)`，越过采样自然末尾即不恢复。释放增益统一为 `(1-clamp01((positionMs-releaseMs)/releaseDurationMs))^2`；时长必须大于 0，初值 180 ms、最大值 300 ms，随音源验收调整。

暂停冻结曲目及释放包络，取消未来预约并保存/重建活动声部；恢复从冻结进度续播。停止/切曲取消全部预约、逻辑音符、按键计数和视觉池。所有延迟任务执行前校验 applyGeneration 和当前曲目。

音频去重状态独立于声音槽位：同一代次被窃或拒绝的音符保留 Suppressed 标记，到期 NoteDown 不重新起音。全池退场时，最多 voiceLimit 个有界 PendingStart 候选等待空槽，起音前按最新曲目时间重新判断年龄和释放状态。旧声部 Retiring 清理仅核验 slotToken 和该槽旧 generation，允许在全局代次更新后完成停止回收；它不能执行旧音乐任务或操作已重用槽位。具体规则见音频分册。

## 7. 接口边界

| 接口 | 责任 |
| --- | --- |
| `MidiTransportClock.GetPositionSeconds()` | 唯一曲目时间读入口 |
| `MidiTransportClock.GetState()` / `GetFrameDspSeconds()` | 返回本帧缓存状态与 DSP 时间；音频维护不自行重复取样 |
| `MidiNetworkTransport._CommitLocalState()` | ownership/待提交/完整快照；只由本地命令或 Owner 公共推进调用，无 NetworkCallable，拒绝网络调用上下文 |
| `MidiPlayerFacade.ApplyRemoteSnapshot(...)` | 应用快照，无网络副作用 |
| `MidiSequencer.RebuildAt(int positionMs, int generation)` | 直接重建逻辑区间、事件游标和计数 |
| `MidiSequencer.AdvanceTo(int positionMs)` | 顺序处理到期事件；卡顿恢复按重建策略处理 |
| `PianoVoicePool.PrepareWindow(...)` | 读取已排序事件，预约未来 KeyDown；不提前改变按键逻辑 |
| `PianoVoicePool.RebuildAt(...)` | 根据当前逻辑区间与释放尾音定位采样 |
| `PianoVoicePool.CancelGeneration(...)` | 撤销旧代次预约并回收声部 |
| `PianoVoicePool.SetLocalOutput(float volume,bool mute)` | 唯一本地输出设置入口；不暂停 Transport、不触发网络提交 |
| `PianoKeyboardView.Refresh(...)` / `MidiWaterfallView.Refresh(...)` | 读取同一时间与模型状态；无公共状态写入 |

以上签名中的 `...` 表示后续在对应分册定义参数清单，不是可直接编译的 C#。内部使用具体 UdonSharp Behaviour 引用和基础类型数组；不以 C# interface、事件委托或运行时普通 ScriptableObject 字段读取作为可用性前提。

## 8. 交付与验证边界

先完成 M0：当前 SDK 中数据烘焙、基本 Udon 编译、DSP 调度/定位/取消，以及双客户端 ownership 与时钟试验，再锁定具体实现。API 签名存在不等于客户端行为已验证。优先采用 DSP 预约；若不可用，明确降为逐帧触发并重新评估精度目标，不静默替换。

端到端验收包括所有权竞争、失活接管、暂停时加入、长音/踏板恢复、密集曲目池耗尽、帧卡顿、重导入引用与新场景零代码接入。当前文件只定义后续应执行的验收，不表示已经执行。

相关分册从 [总索引](README.md) 进入。
