# 04 · 多人同步与控制权

## 2026-11-01 独立提琴奏法快照

`stringArticulationOverrides` 用一个 0～255 的整数保存四把提琴各自的自动 / 长弓 / 跳弓 / 拨弦选项，编码见 CONTRACTS。完整意图、确认状态、序列化候选、接收去重、初始发布、晚加入、所有权交接和回滚全部包含它。奏法命令使用 `_CommitInstrumentState` 保留实时输入会话；应用只替换发生变化的目标声部，保留其他乐器起点，不移动时间轴。非法编码拒收；规则版本更新进入配置哈希。

## 2026-11-01 多音色快照补充

当前主场景快照增加 enabledInstrumentMask 与 pianoAppearance；所有权等待中的完整意图、最近确认状态和序列化发送候选均保存这两个字段，重复判断、回滚、晚加入及所有权交接沿同一应用路径恢复。instrumentIndex 和 stringsArticulation 继续保留。路由规则及音源哈希加入 catalogHash；旧配置不能混用。播放尾垫使用已安装音源的稳定最大释放值，不因勾选变化缩短时间轴。代码与 Editor/ClientSim 验证不代表真实双客户端网络已验收。


日期：2026-10-23。本文是 v0.1 待实现设计，遵守 [统一约定](CONTRACTS.md)；当前没有运行验证结果。代码依据见 [参考与证据边界](11-reference-notes.md)。

## 1. 权威边界与命令路径

MidiNetworkTransport 位于独立 GameObject，使用 Manual Sync。它的当前 Owner 是公共播放快照的唯一写入者，但可以随用户操作转移。MidiControlGate 判断本地用户是否能操作，默认开放；可接入外部权限系统。Owner、实例 Master、实例创建者和被允许操作的人是不同概念，不以 IsMaster 授权。

每个客户端本地计算音符、声音、按键和瀑布流，只交换小型播放状态。音量、静音、表现降级不取得 ownership，也不写公共状态。公开控制仅有 CommandPlay、CommandPause、CommandStop、CommandSelectSong；同一客户端中的 UI 调用 MidiPlayerFacade，不发送带参 Command RPC。

完整路径是：权限检查 → 本地立即操作 → _CommitLocalState → 必要时 SetOwner → 复查本地仍是 Owner → RequestSerialization。远端路径是：OnDeserialization → 校验完整快照 → ApplyRemoteSnapshot → 恢复本地时钟及音符。Apply 不得调用 Command、SetOwner 或提交入口，防止收到一次同步又回发一次。

参考项目 VizVid 在 Core.Play/Pause/Stop 本地执行后调用 RequestSync，后者 SetOwner(LocalPlayer) 再 RequestSerialization。本项目保留这条分布式控制路径，并新增显式待提交、ownership 复查和推测撤销；这些步骤是本项目设计，不宣称 VizVid 已实现同一协议。[Core.cs 固定提交](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core.cs#L541-L572)、[RequestSync](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core.cs#L799-L803)。

## 2. 本地意图、确认与竞争

本地分别保存 confirmedSnapshot 和 speculativeTransport，另有一个可替换的 pendingIntent。待提交意图保存完整目标：曲目、状态、位置锚点、产生时的服务器时刻及本地代次，不只保存按钮名称。因此等待 ownership 时依次选 B、播放，最终意图仍是“B 正在播放”，不会丢掉选曲。重复 Play/Pause 幂等；替换意图不建立网络命令队列。

提交前验证曲库就绪、曲目存在和权限；本地推测立即响应声音与 UI。已是 Owner 则复查身份后请求序列化；否则标记 TakeoverPending 并申请一次 ownership。通过 OnOwnershipTransferred 及后续帧的 IsOwner 检查统一恢复提交，回调不能重复提交同一意图。用户撤销、权限撤回、申请超时或被其他玩家夺走 ownership 时，清空待提交，递增 applyGeneration，撤销推测及预约，应用最近有效的已确认快照并请求补发。

SetOwner 返回、一次本地 IsOwner=true 和 OnPostSerialization.success 都不等于所有客户端已经确认。序列化前再次检查 Owner；发送失败且仍持有 ownership 时合并为一次限流重试，失去 ownership 后绝不重试旧意图。未收到当前 Owner 快照时，保留已确认状态作为临时显示，不能把无效数据当确认；必要时暂时静音等待补同步。

并发不承诺两个用户的每次按钮操作都保留，也不承诺跨用户全序。最终 ownership 及其有效快照决定公共结果。冲突失败方取消推测并跟随结果，不循环抢回 ownership。自动失活接管没有用户意图，必须沿用公共时间轴，不能发布某人的本地音量、尚未确认的选曲或错误状态。

“已确认”表示本客户端接受了与当前权威一致的状态，不是全员确认屏障。Owner 保存最近一次发送候选及其本地意图代次；发送回调成功且自己仍为 Owner 时，可把候选记为最近发布状态。若等待发送期间又产生了新意图，旧回调不得清除新意图。发送失败只恢复可重试标志，不重做音符操作；一个快照的多次发送不能造成多次暂停或切曲。

| 本地阶段 | 到来的情况 | 处理 |
| --- | --- | --- |
| 无待提交 | 收到当前 Owner 有效快照 | 直接 Apply |
| 申请中 | 原 Owner 发来新快照 | 更新恢复基线；申请仍有本地有效意图时保留推测 |
| 申请中 | 新命令替换旧意图 | 更新完整目标，沿用一次申请，不追加网络消息 |
| 已取得 ownership | 最新意图仍有效 | 本地复查后提交，等待期间的播放时间按锚点继续推算 |
| 申请失败或失去 ownership | 收到新 Owner 状态 | 取消意图，递增代次，恢复权威状态 |

首次进入且未获得任何有效快照时，用户操作入口保持等待同步；不能用工程默认值覆盖正在播放的公共状态。只有确认自己是初始 Owner 且网络就绪，才可初始化默认选曲。申请的本地超时值作为可调参数在 M0 确定，超时只取消本地意图，不强行要求其他客户端回滚。

## 3. 完整快照与序列化取样

同步字段只使用统一约定中的 schema：

| 字段 | 类型及规则 |
| --- | --- |
| catalogHash | string；必须与本地烘焙曲库一致 |
| songId | string；稳定 GUID，空曲库可空 |
| state | int；Stopped=0、Playing=1、Paused=2 |
| anchorPositionSeconds | double；基准曲目进度，秒 |
| anchorServerSeconds | double；对应服务器时间，秒 |
| writerPlayerId | int；快照发布者，结合实际 Owner 判断 |
| snapshotSequence | int；发布者本地递增辅助序号 |

OnPreSerialization 确认仍为 Owner 后，在同一回调取一次服务器时刻，并从统一 Transport 时钟取得对应进度，填完所有字段。Stopped 固定位置 0，Paused 保存冻结位置，Playing 保存该时刻的实际逻辑进度；不能混用旧曲目字段和新状态。相同状态的补发也重新取样。快照内的对应关系必须自洽，不在按钮按下时就把一个会等待发送的“当前时间”写死。

接收 Playing 后计算：

    targetSeconds = anchorPositionSeconds
        + Networking.CalculateServerDeltaTime(nowServerSeconds, anchorServerSeconds)

Paused 固定为 anchorPositionSeconds，Stopped 固定为 0。函数方向、回绕行为及 double 暴露须在 M0 用已知时间差核对；使用统一封装，其他模块不得各写一套时间差。

例如暂停在 12 秒时反复补发，位置仍是 12；恢复时记录“12 秒对应恢复操作的服务器时刻”。补发若等待了半秒，回调取样就是 12.5 秒对应发送前的服务器时刻，接收端只再加此后经过的时间。恢复过程既不从 12 秒重新慢半拍，也不把那半秒补两次。

本设计通过服务器锚点涵盖序列化等待与传输经过的时间。因此 DeserializationResult.sendTime/receiveTime 只做网络诊断，不再把 realtimeSinceStartup-sendTime 加到 targetSeconds，也不再叠加 SimulationTime 延迟。后两者所属时基和语义不同；双重补偿会让客户端越同步越超前。P0 不同步 DSP 时刻，DSP 只用于各端预约。

snapshotSequence 仅辅助同一已知 ownership 周期内去重；重新取得 ownership 时不能用旧 Owner 的序号阻止新 Owner。保留本客户端计数，不把序号重置解释为新的全局纪元；相同 writer 和 seq 只有字段也相同时才可当重复包。实际 Owner 已改变就清理该周期比较基线。此 schema 没有全局事务号，不能据此证明跨 Owner 的所有历史包全序。

## 4. 接收、晚加入与恢复

OnDeserialization 一次读取完整字段，先验证哈希、songId、有限数值、位置范围和 state；检查 writerPlayerId 是否匹配当前可见 Owner。交接时身份尚不一致则暂缓应用并请求新 Owner 补发，不能为了“看起来更新”而抢所有权。无效或异曲库数据保持本地安全停止并显示简短诊断，不按曲库下标猜测。

曲目时长用于区分坏字段和自然结束：durationMs 覆盖源 MIDI 的逻辑末尾，包括源 End-of-Track 与最后 Release，不能直接作为清理声音的时刻。运行时派生 playbackEndMs = durationMs + PianoBankData.maxReleaseMs，作为安全尾垫上界；统一时钟的 clamp、自然结束判断及 UI 总时长均使用 playbackEndMs。锚点本身应在这个合法范围内；合法 Playing 锚点向当前时间外推超过 playbackEndMs 属于正常情况，本地结束并等待 Owner 终态，不报数据损坏。重复快照只更新活动记录，不能让每次补发都中断同一首歌；曲目未变、状态未变且误差小于阈值时走校准路径。

未完成曲库和音源初始化时，只保存最近有效快照。Ready 后使用那份锚点及此刻服务器时间重新求目标，不能从数据刚到达时的位置起播；加载结束按最新目标恢复，避免初始化耗时造成固定落后。网络尚未就绪时不自动播放默认曲覆盖公共状态。

Owner 在 OnPlayerJoined 后合并安排一次完整补发；晚加入者初始化完成仍无有效数据，向当前 Owner 请求快照。请求可限流重试，Owner 合并同一批加入与请求。同步变量保存晚加入状态，网络事件不保存历史，这与官方 [晚加入说明](https://creators.vrchat.com/worlds/udon/networking/late-joiners/) 一致。

ApplyRemoteSnapshot 对同曲 Playing 普通快照只更新目标锚点；需要主动定位或恢复时，按曲目时间调用 Sequencer.RebuildAt，使用检查点恢复尚未释放的音符及有限尾音窗口，不重播全部历史 NoteOn。恢复中的声音按音符年龄定位，已经衰减完的样本不重新起音。Playing 中加入、Paused 中加入和曲库加载较慢都走同一恢复入口。暂停冻结逻辑及释放包络；停止、切曲、主动定位和显式恢复递增 applyGeneration，所有延迟任务、音符预约和回调执行前检查曲目及代次。

## 5. 校准、心跳与失活接管

每 0.5 秒本地比较共享目标与本地 Transport/DSP 映射，通常不发送任何数据。20 ms 内保持；超过 20 ms 时每次最多修正 5 ms 并保持时间单调。2026-11-01 已删除本地校准和同曲 Playing 快照的位置误差 100 ms 自动重建规则；普通快照只更新目标锚点。主动 Seek 提交唯一 `seekToken`（玩家 ID 与本地定位序号），接收端仅在标记变化时显式重定位；补发保留同一标记，不重复执行 Seek。曲目/状态变化及显式恢复仍走重建路径。音频前视预约的撤销与重排必须跟随 applyGeneration，不能为追赶误差修改整曲音高。

Playing 的当前 Owner 在约 3 秒没有发布新快照时发送无参轻量 _Heartbeat；新有效快照或已验证的当前 Owner 心跳刷新接收端活动时间。心跳不携带曲目权威状态，不触发声音重建。进度异常、身份交接或缺失快照时再请求完整快照，不能把 0.5 秒本地检查变成全员网络轮询。

只有曲库就绪、持有有效 Playing 快照、没有待提交意图的客户端参与失活接管。自最后有效活动起 6～10 秒随机退避，到期重新检查状态、当前 Owner、活动时间及本地网络就绪；期间收到新活动就取消。仍失活才申请 ownership。成功后重新确认身份，沿用原锚点推算的当前曲目位置发布完整快照，不能从零重播；竞争失败即退出。Paused/Stopped 不发送维持播放的心跳，也不因安静而自动接管；用户主动控制仍可正常取得 ownership。

Owner 离开导致 VRChat 自动交接时，新 Owner 不重新选曲或重启。若 Playing 则从已确认锚点继续并补发，Paused/Stopped 保持原状态，按补同步请求回应。随机接管及新 Owner 回调都复查身份，避免多个客户端持续互相争夺。

## 6. 事件入口与公共曲终

仅 _Heartbeat 与 _RequestSnapshot 标记 [NetworkCallable]，使用无参事件：前者发 Others，后者发 Owner。接收时在事件入口立即读取 NetworkCalling.InNetworkCall、CallingPlayer，并使用 Utilities.IsValid 校验。心跳发送者必须等于 Networking.GetOwner(networkObject)，不能相信传入 writerId，也不能让任意玩家伪造活跃信号。请求只允许有效实例内玩家，接收端必须仍为 Owner，并对请求做本地冷却、合并和限流。迟到的旧 Owner 心跳直接忽略。

SDK 支持 sender 访问与属性语法，但必须完成当前版本 Udon 编译及真实双客户端确认。若 M0 不能取得可信 sender，不能启用未校验心跳；应先修正事件封装，或改为低频完整 Owner 快照作为活跃信号并重新记录方案。禁止退回信任事件参数中的身份。

_CommitLocalState 无 NetworkCallable，并拒绝 InNetworkCall 上下文；其他无参本地辅助入口使用下划线前缀。Facade 放在 BehaviourSyncMode.None 的独立对象，避免遗留 public 无参网络入口改变公共状态。事件入口不调用 Command/_CommitLocalState；请求快照只设置待补发标志，后续本地更新统一执行，避免网络调用上下文传播误伤本地提交。[官方事件规则](https://creators.vrchat.com/worlds/udon/networking/events/)。

到 durationMs 后保留 Playing，继续推进已有 Release 的释放包络，让最后和弦自然消退；安全尾垫期间不产生源数据以外的新音符，也不新增 Draining 公共状态。到 playbackEndMs 时所有端自然结束声音与表现，只有当前 Owner 将公共状态提交为 Stopped/0，保留 songId。Owner 用本地曲目/代次及曲终标志防止逐帧重复提交，切曲或再次播放才复位；非 Owner 不申请 ownership 来宣布曲终。网络暂断时本地先自然停止，恢复后跟随有效终态；刚接管且已越过 playbackEndMs 的 Owner 只发布一次终态。主动 Stop 或切曲仍立即取消预约并清理旧声音，不等待尾垫；在尾垫中暂停仍按 Paused 冻结曲目时间和释放包络。

## 7. M0 必须验证的边界

先验证 ownership 拒绝与同帧竞争、快照延迟/发送失败、交接时暂停、晚加入长音及慢初始化、Owner 卡住与恢复、Paused 无误接管、曲终一次提交、事件 sender 和调用上下文，再锁定实现。检查本地 generation 能取消 DSP 预约且旧任务不会复活。不承诺已达到跨端误差指标；当前文档只给实现与验收要求。
