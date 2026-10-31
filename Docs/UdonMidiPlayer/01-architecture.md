# 架构与权威边界

本方案将静态数据、共享播放意图、本地音符逻辑、声音与画面分为独立模块。统一约定见 [CONTRACTS](CONTRACTS.md)，本文规定模块怎样连接，以及哪些状态只能由哪个模块修改。

## 1. 两个执行环境

Editor 执行 MIDI 文件读取、第三方解析库、复杂集合运算、音符配对、tempo 换算、索引生成、资源校验与 Prefab 接线。运行环境只处理已经烘焙的基础类型数组及资源引用，不依赖文件访问、运行时 MIDI 库或自定义 ScriptableObject 业务字段读取。

计划使用显式导入窗口读取 .mid 源文件。SDK 已有 .mid 处理，项目不再注册同扩展名的第二个 ScriptedImporter。重导入沿用生成资产 GUID，不整体重建曲库和资源引用。

运行侧以具体 UdonSharp Behaviour 互相引用。所有内部“接口”是调用契约，不要求实现 C# interface、委托或泛型集合。具体可调用 API 在 M0 中通过当前 SDK 编译和客户端试验确认。

## 2. 模块所有权

| 状态 | 唯一修改者 | 消费方 |
| --- | --- | --- |
| 源 MIDI 与导入设置 | 世界作者，通过 Editor 工具 | 导入器 |
| 烘焙曲库、音源指纹 | Editor 烘焙器 | 本地查找、网络校验 |
| 当前曲目、播放/暂停/停止、时间快照 | `MidiNetworkTransport` 当前 Owner | 所有客户端 |
| 当前客户端的曲目时间与 DSP 映射 | `MidiTransportClock` | Sequencer、音频、视觉、UI |
| 当前音符、事件游标、按键计数 | `MidiSequencer` | 音频与键盘 |
| 声音预约、声部和释放增益 | `PianoVoicePool` | 本地音频输出 |
| 可见音符条和键盘颜色 | 两个 View | 本地画面 |
| 本地音量、静音、控制许可 | UI/ControlGate 对应本地模块 | 本地音频或命令入口 |

Owner 只维护必要公共状态、补发与公共终态。每个客户端自行计算全部音符和表现；Master 不参与日常播放命令中转。可修改公共状态的许可由 ControlGate 决定，维护状态的接管资格单独判断。

## 3. 计划组件与代码文件

所有下列组件均使用项目自有命名空间。Runtime/Data 对应 `MusicMidiPlayer.Data`，Logic、Networking、Audio、Views、UI 分别对应 `MusicMidiPlayer` 下同名分组；Editor 使用 `MusicMidiPlayer.Editor.*`。表中的类名是短名，完整映射及程序集边界见 [命名空间与分发](12-namespaces-and-distribution.md)。Runtime 不引用 Editor 程序集，源码不能落入宿主的全局命名空间。

| 计划文件 | 核心内容 |
| --- | --- |
| `Runtime/Data/MidiSongData.cs` | 单曲扁平数组，纯数据，无 Update |
| `Runtime/Data/MidiLibraryData.cs` | songId 查找、catalogHash、音源引用 |
| `Runtime/Data/PianoBankData.cs` | 采样 AudioClip 引用与区域、释放配置，纯数据 |
| `Runtime/Logic/MidiPlayerFacade.cs` | Command*、ApplyRemoteSnapshot、生命周期协调 |
| `Runtime/Logic/MidiControlGate.cs` | 本地许可及 UI 可操作性 |
| `Runtime/Logic/MidiTransportClock.cs` | 公共目标时间到本地时钟的映射与纠偏 |
| `Runtime/Logic/MidiSequencer.cs` | 事件推进、区间恢复、按键引用计数 |
| `Runtime/Networking/MidiNetworkTransport.cs` | 唯一 Manual Sync 组件、ownership、快照与心跳 |
| `Runtime/Audio/PianoVoicePool.cs` | 固定 AudioSource 池与音源映射 |
| `Runtime/Views/PianoKeyboardView.cs` | 键盘静态几何及高亮 |
| `Runtime/Views/MidiWaterfallView.cs` | 音符条可见窗口和几何更新 |
| `Runtime/UI/MidiPlayerUI.cs` | 状态显示、按钮、本地设置 |
| `Runtime/UI/MidiSongButton.cs` | 编辑器生成的单个曲目选择绑定 |
| `Editor/Import/MidiImportWindow.cs` 等 | 资产解析、诊断；Editor/Baking 与 Editor/Prefab 负责烘焙、配置与检查 |

这些文件在实施阶段创建；本目录不是已实现代码的 API 文档。

## 4. 启动与依赖顺序

1. Facade 验证 LibraryData、SoundBank、网络组件、时钟、Sequencer、音频和 View 引用；初始化固定缓冲区。
2. 本地模块完成引用绑定及资源就绪，默认保持无声、停止。
3. 网络组件缓存可能已先到达的最新快照。初始化不得发布默认 Stopped 覆盖远端播放状态。
4. Facade 就绪后应用最新有效快照。若本地最早为 Owner 且未收到/发布过公共状态，才初始化默认选曲和 Stopped。
5. 进入 Playing 后启用音频预约、本地校准和必要心跳；Stopped 下只保留低成本 UI/网络响应。

初始化依靠显式 Ready 标记和 Facade 协调；不依赖跨 GameObject 的偶然 Start 调用顺序。数据缺失、hash 不一致或 API 故障转入本地错误状态并静音，不通过网络改写其他玩家的正常状态。

## 5. 三条调用链

### 5.1 本地用户命令

`UI → Facade.Command* → ControlGate → 本地 Transport 操作 → _CommitLocalState → ownership 与快照提交`。

本地操作先响应并保留最后确认状态；取得 ownership 前仅有一个待提交意图。再次操作替换该意图。ownership 丢失、失败或超时后撤销尚未确认的推测状态，从最新确认快照重新计算当前进度。

确认依据是当前 SDK 提供的 IsOwner 和 ownership 回调，不把本地检查包装成跨玩家全局事务确认。序列化成功也不等于每个远端都已经应用。

### 5.2 远端状态应用

`OnDeserialization → 快照校验 → 缓存最新快照 → ApplyRemoteSnapshot → 切曲/恢复状态 → 本地输出`。

没有本地就绪条件时保留快照；就绪后按此时的服务器时间追到最新位置。该路径没有 SetOwner、Command* 或 RequestSerialization 副作用。重复的同义快照只做校准，不重复清空声音和创建可见对象。

### 5.3 正常运行帧

1. Clock 生成当前曲目时间和本地 DSP 映射。
2. Sequencer 消费到期事件，同毫秒事件作为完整批次应用。
3. VoicePool 更新已释放声部和未来 50 ms 预约，预约不会提前点亮按键。
4. Keyboard、Waterfall、UI 读取本帧同一份曲目时间与状态。

音频前视读取独立事件索引，不提前移动逻辑事件游标。大卡顿、切曲或强制纠偏走统一 Rebuild，不把错过的历史音符成批瞬时播放。

## 6. 重建事务

重建由 Facade 协调，内部顺序固定：

`递增 applyGeneration → 取消旧 DSP 预约/延迟工作 → 绑定目标曲目 → 设定时钟 → 重建音符和事件游标 → 恢复音频 → 重建可见窗口 → 发布本地 UI 状态`。

这里的事务只指同一客户端内部的应用顺序，不提供网络原子提交。若一次重建需分帧，先进入本地 Rebuilding 并暂时静音；完成时重新取最新目标时间，避免用重建开始时的旧时间起播。新的快照或用户命令可以淘汰旧重建。

暂停、停止、切曲、重定位和撤销推测都使旧代次失效。所有预约和延迟回调同时校验曲目绑定与 generation，不能只检查数组索引仍在范围内。

## 7. 公共流程与本地流程

曲终使用 `playbackEndMs = durationMs + PianoBankData.maxReleaseMs`，各端保留末尾释放尾音并自然结束声音与表现；只有当前 Owner 发布 Stopped/0 的公共终态。Owner 的终态发布带当前曲目、当前状态检查，避免旧曲目的延迟曲终覆盖新曲。P0 没有自动下一首。

本地静音、音量调整、视觉池不足、音频 voice 被回收均不改变公共播放进度。音源无法恢复某个已经自然结束的采样时，该声部保持无声，键盘仍按实际 keyUp 语义显示。

## 8. 扩展边界

P1 的进度拖动复用重建；变速统一扩展 Clock 和采样策略，不能只改变视觉速度。粒子消费本地已确认的音符逻辑变化；搜索与歌单位于曲库/控制层；Quest 优化可替换 View 和池预算。稳定外部音符事件 API 在 P1 单独定义，避免把内部调用签名提前承诺给世界作者。

继续阅读：[播放逻辑](03-transport-and-sequencer.md)、[多人同步](04-multiplayer-sync.md)、[Prefab 接入](08-editor-and-prefab.md)。
