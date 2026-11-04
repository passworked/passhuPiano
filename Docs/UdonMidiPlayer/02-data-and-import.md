# 02 · 曲目数据与批量导入方案

## 2026-11-04：rc.3 曲库编辑器

当前操作教程见 [Unity MIDI 导入](../MIDI_IMPORT.md)，开发边界见 [编辑器操作契约](../Developer/MIDI_LIBRARY_EDITOR.md)。原 Import MIDI 入口保留，新增 Edit Library 同窗口入口；采用分类 / 曲目双栏草稿、拖动排序、行内曲名作者、外部 MIDI / ZIP / 文件夹 / 直链输入、哈希复用、明确启用的同名版本择优、选轨、保留身份的重导入 / 源文件替换 / 改名及 JSON 引用歌单。新增默认保留通道10与全部音高；既有歌曲的导入设置保持不变。

源事件计数只供 Editor 同版本选择，不新增运行数据或改变既有 MPC1 编码。保存只重解析需要更新的曲目，并通过现有同步链更新已加载普通场景；导入失败和场景应用失败分别报告。显示曲名更新内容哈希但不重新解析源。分发 Prefab、未打开场景与练习系统不自动迁移。

下文保留 v0.1 设计基线；当前已有 Editor 导入器与 MPC1 压缩数据实现，现行增量约定以本节及代码为准。字段、单位与状态以[统一约定](CONTRACTS.md)为准；运行消费方式见[时钟与音序器](03-transport-and-sequencer.md)。

## 2026-10-31：保留原 MIDI 音色元数据

导入保留 Program Change、CC0 Bank MSB、CC32 Bank LSB，原始值均为 0～127，不改为显示用的 1～128。按全文件稳定源顺序保存所有通道和轨道的音色事件，包括未选中的独立控制轨与被排除的打击乐通道；不去重同刻事件，不伪造缺省音色。尚不处理 SysEx 的 GM/GS/XG 模式或自定义音色。

源资产、导入结果和当前歌曲解码数据包含 `timbreDataVersion=1`，以及等长的 `timbreMs/channel/kind/value/trackIndex/eventIndex` 六组数组（实际字段统一以 `timbre` 为前缀）。`kind` 为 0=Program、1=Bank MSB、2=Bank LSB；`eventIndex` 是该事件插入原有音符动作数组的位置，范围 0～3N，使毫秒量化后的换音色与 NoteOn 先后关系仍可恢复。数组内顺序保留多次音色变更的源顺序。无音色事件的新导入曲目仍标记版本 1，旧压缩数据无扩展时为版本 0。

MPC1 可选 TLV tag 4 保存该信息：事件数，然后逐项写入时间差、通道/种类合并字节、值字节、来源轨道和动作索引差。tag 2 的 CC11 表情扩展独立保留。音色扩展计入内容哈希与导入内存预算；展开载荷为每事件 24 字节，不含数组/Udon 管理开销。运行只在当前歌曲加载时分步解码，切歌释放，不进入音符事件调度、音源选择或网络逐事件同步，现有发声方式保持不变。

现有歌曲可通过 `MusicMidiPlayer.Editor.Import.MidiTimbreMigration.Begin/ProcessBatch/UpdateRuntimeMetadata` 显式补全。先重新解析原 MIDI 并核对音符，再仅补写音色元数据；保留现有音符、踏板、CC11、曲名、作者、设置、songId、资产与压缩文件 GUID。只更新现有场景/Prefab 中歌曲的内容哈希、压缩长度与曲库哈希，不同步曲库成员，不 Apply 场景覆盖，不重建对象。报告位于 `Logs/MidiTimbreMigration.json`。边界与压缩/分步解码检查入口为 `MidiTimbreValidation.Run`。

本次已补全 560 份歌曲资产（含并行加入的 GM Drum Study），保存 30,614 条 Program Change，未发现 Bank Select；音色扩展合计增加 156,576 字节（约 153 KiB）。逐首核对既有播放数组、songId、源资产及已有压缩资源 GUID；主场景、两份分发 Prefab、示例与练习场景的对象 ID 和逐对象非空引用保持一致。Unity C# / UdonSharp 编译、Editor 压缩往返、分步解码边界与真实曲目、旧格式兼容检查通过；未据此声称完成真实 VRChat 或听音验收。备份位于 `RecoveryBackups/TimbreMetadata-20261031/`，文件与引用核对记录位于 `Logs/MidiTimbreMetadata-*-check.json`。

## 一、导入链路与资产边界

2026-11-04 新增 MPC1 可选 tag 6 / v1：Editor `ViolinArticulationBaker` 按原 noteId 烘焙小提琴自动奏法，每音符 2 bit，四个 noteId 共一个 byte。编码值 0=沿用旧规则、1=长弓、2=跳弓、3=拨弦；payload 为版本 byte 1 加 `ceil(noteCount/4)` 个 byte。新版运行解码器按既有加载预算读取到当前曲目的 `violinArticulations`，卸载清空；缺少扩展时自动退回原 GM 策略，重复、错误长度或不支持的版本被拒绝。源 MIDI、源曲目 contentHash / songId 及既有音符 / 事件数组不改，算法身份进入路由指纹及 catalogHash；发声 / 定位 / 运弓只查表。具体阈值与范围见[弦乐组接入准备](14-strings-preparation.md)。

计划使用纯 Editor C# 完成 `SMF 字节读取 → 源事件合并 → 时间换算 → 音符配对 → 踏板展开 → 规范化资产 → Udon 数据烘焙`。SDK 已注册 `.mid` 资产处理，因此使用显式 `MidiImportWindow`，通过 `File.ReadAllBytes` 或 Editor 专用解析库读取源文件，不另注册争夺 `.mid` 扩展名的 ScriptedImporter。MVP 通过窗口按钮重导入；后续若增加 AssetPostprocessor，只标脏，不默认自动重烘焙全曲库。`.mid` 解析、排序和诊断不进入 Udon 每帧逻辑，也不依赖在世界内读取本地文件、加载 MIDI 插件或访问普通 ScriptableObject 的自定义字段。

`MidiSongAsset` 保存源文件 GUID、选轨设置、标题、规范化音符、导入诊断与格式版本；`MidiLibraryAsset` 保存有序曲目引用。配置器再把结果写入计划中的 `MidiSongData` 与 `MidiLibraryData`，运行数据采用基础类型一维数组。源资产可使用普通 Editor 集合，不能据此推定对应集合在 Udon 可用。

每个源文件独立完成验证再提交资产；一首失败不阻断同批其他文件，失败不覆盖该首已有的成功资产。重导入更新原生成资产，保留其 `.meta` 与 `songId`；源 GUID 加导入设置建立映射，同名文件不合并。烘焙前检查空引用、重复曲目 ID、数组一致性及数据版本，任何未解决的致命错误均禁止生成可发布曲库。

## 二、SMF 支持边界

| 项目 | v0.1 处理 |
| --- | --- |
| 文件结构 | 支持 SMF Type 0 / 1；校验 MThd、MTrk、长度、VLQ、running status 与事件边界 |
| 时间基准 | 支持正 PPQ；Type 2、SMPTE division 明确拒绝 |
| 音符 | NoteOn、NoteOff、NoteOn velocity=0 标准化为 NoteOff |
| 表情 | 实现分通道 CC64；其余会影响还原的程序变更、弯音、其他 CC 给出汇总诊断 |
| 选取 | 默认合并旋律轨，排除零起始通道 9；保留完整 MIDI 0～127 原始音高。超出 88 键（21～108）的音符按黑白键规律延伸到键盘两侧，并使用边缘采样变调播放 |

解析器先读取全文件，不按选轨提前丢弃时间和控制信息。每个事件记录绝对 tick、轨道下标、轨内源序号。全文件以 `(absTick, trackIndex, sourceEventIndex)` 稳定合并，分配递增 `sourceOrdinal`；这只是跨轨同刻的确定性规则，不表示 SMF 提供了跨轨因果关系。

所有轨道的 Set Tempo 都进入 tempo map，不能只读取第一轨或选中的钢琴轨。tick 0 默认每四分音符 500000 微秒；同 tick 多条 tempo 采用上述源序最后一条，并报告冲突。每段时间为 `deltaTick × microsecondsPerQuarter / PPQ`，累加使用足够精度的数值；完整积分后统一以“四舍五入、恰好半毫秒向上”量化为非负 int 毫秒，不能逐段先舍入再相加。tempo 必须为正，累计时间超出 int 毫秒范围即拒绝导入。

先以全文件事件配对，再按 NoteOn 所在轨决定保留音符；选轨不会使跨轨 NoteOff 被提前删除。踏板控制也读取全文件，因同通道控制可能位于独立控制轨。原始轨道名只供配置显示，运行 `trackIndex` 仍保存源轨道下标。

## 三、音符、重叠与踏板算法

每个 `(channel, pitch)` 维护 FIFO 未松键队列。NoteOn 入队；NoteOff 关闭最早尚未关闭的 NoteOn，记录 `keyUpMs`。同音重叠不能用一个布尔值或“最后一次开始时间”覆盖。被配对的 NoteOff 可以来自另一轨；这一规则固定、可诊断，不猜测作曲者使用哪条轨作为声部。

每通道独立维护踏板状态，CC64 值至少 64 视为踩下。松键时未踩踏板，则 `releaseMs=keyUpMs`；踩下时加入该通道待制音集合，直到后续踏板抬起才写入 `releaseMs`。踏板抬起只释放已经松键的音符，不释放仍按住的音符。再次踩下不复活已经启动释放的音符。这里的 Release 表示开始制音包络，声音尾部仍由音源决定，不能把 releaseMs 当作立即静音时间。

文件结束时，尚未配对的 NoteOn 在全文件逻辑末尾补 KeyUp；尚被踏板保留的音符在同一末尾补 Release，并记录数量。孤立 NoteOff 跳过并警告；非法状态字、截断数据、溢出、非法 tempo 为该文件致命错误。未实现控制不悄悄改造成支持功能，也不以未经说明的音符截短掩盖错误。空文件或过滤后无可播放音符产生诊断，不加入可播放曲库。

全文件末尾取各轨有效结束位置的最大值，补全事件之后，`durationMs` 取末尾与全部 releaseMs 的最大值，保留文件尾部的有效休止。文件缺失 EndOfTrack 但数据块完整时，按轨数据结束位置处理并警告。解析结果始终满足 `0 <= startMs <= keyUpMs <= releaseMs <= durationMs`。

运行有效结束另由 `playbackEndMs=durationMs+PianoBankData.maxReleaseMs` 派生，预留音源释放尾音的安全上界，最多带来一个最大释放时长的静默尾垫；不修改 MIDI 数据的 durationMs。播放时钟和 UI 总时长使用有效结束，末尾仍为 Playing，直到尾垫完成才自然停止，不增加公共状态。曲库烘焙时检查这次整数加法不会溢出。用户主动 Stop 仍可立即取消曲目，不能被尾垫延迟。

## 四、扁平运行格式与稳定事件流

保留音符按开始毫秒、原 NoteOn 的 sourceOrdinal 稳定排序后分配 `noteId=数组下标`。noteId 只在同一曲目内容版本内有效，重导入不能用旧 noteId 恢复旧声部。音高采用 0～127，通道采用 0～15，力度采用 1～127；速度为零的 NoteOn 不生成新音符。旧导入器已跳过的音域外音符须重新导入原 MIDI 才能恢复。

| 字段组 | 内容与约束 |
| --- | --- |
| 标识 | `songId/title/contentHash` 字符串，`schemaVersion=1`，`durationMs` int |
| 每音符数组，长度 N | `startMs/keyUpMs/releaseMs/pitch/velocity/channel/trackIndex`，全部 int[] |
| 事件数组，长度 3N | `eventMs/eventKind/eventNoteId`，全部 int[]；Kind 为 Down=0、Up=1、Release=2 |
| 检查点事件索引，长度 K | `checkpointEventIndex` |
| 检查点活动 CSR | `checkpointActiveOffsets` 长 K+1，`checkpointActiveNoteIds` 长 A |

每个保留音符恰好生成三个动作，零长度也不删除。动作排序键为 `(eventMs, sourceOrdinal, subOrder, noteId)`，毫秒量化不丢失原始源顺序。由 KeyUp 派生的 Release 沿用其 sourceOrdinal，subOrder 排在 KeyUp 后；由踏板抬起派生的多个 Release 使用该 CC64 的 sourceOrdinal，并按 noteId 排序。末尾补全事件排在所有真实源事件之后，先补 KeyUp 再补 Release。

这一顺序保留同音符 Down→Up→Release，不额外强制所有不同音符的 Off 都先于 On。同一毫秒的一批动作必须全部完成后再刷新键盘高亮，避免零长度音符或同音替换造成一帧闪烁。sourceOrdinal 与 subOrder 是导入期排序信息，排序结束不必写入运行数组。

`contentHash` 对格式版本、规范化配置及最终运行内容计算，不能仅散列文件名。曲库 `catalogHash` 包含有序 songId、内容哈希、格式版本及音源 `bankHash`，使样本与发声配置不一致的构建被识别；本地音量和静音不计入。运行时不以曲库下标替代 songId。标题和诊断可保留源文件信息，但绝对本机文件路径无需进入运行数据。

## 五、检查点 CSR 的严格边界

间隔固定为 1000 ms。`K=floor(durationMs/1000)+1`，检查点 c 的时间 `C=1000*c`，因此 C=0 必定存在；若末尾恰好落在整数秒，也包含该末尾检查点。

`checkpointEventIndex[c]=lowerBound(eventMs,C)`，指向首个 `eventMs>=C`，无事件时等于 3N。CSR 切片 `[offsets[c],offsets[c+1])` 存储“C 之前已开始，C 之前尚未 Release”的 noteId，即 `startMs<C && releaseMs>=C`，按 noteId 升序。这里的等号必须保留：在 C 刚好发生 Release 的音符要先进入检查点状态，再由 C 的事件释放。C=0 的切片为空，最后一个 offset 等于 A。

恢复接收完整 `positionMs`，范围为 `[0,playbackEndMs]`；仅逻辑检查点查询使用 `t=min(positionMs,durationMs)`。这样尾垫期间不会索引不存在的检查点，同时音频仍使用真实 positionMs 计算已释放多久。逻辑步骤如下：

```text
c = floor(t / 1000)
载入 CSR[c]，并以 keyUpMs >= C 建立“C 前仍按键”计数
cursor = checkpointEventIndex[c]
依源顺序消费 eventMs <= t 的动作，仅重建逻辑，不逐条发声
cursor 最终等于 upperBound(eventMs, t)
```

恢复后的活动集合严格为 `startMs<=t && releaseMs>t`；物理按键集合为 `startMs<=t && keyUpMs>t`。刚在 t Release 的音符归入尾音候选，刚在 t Down 的音符已活动。CSR 不含声音尾音，也不代替 KeyUp 计数。生成器通过事件扫描生成 CSR，不能对每个检查点重新扫描整首而使导入随 K×N 增长。

## 六、尾音候选与数据预算

最大制音释放时长 L 由[钢琴音源配置](05-piano-audio.md)的运行 `PianoBankData` 提供，单位为 int 毫秒，不能写死在 MIDI 内容内。其 Editor 源 `PianoBankAsset` 先烘焙为基础类型数组和 AudioClip[] 引用，运行不直接读取自定义 SO 字段。声音重建先读取当前活动 ID，再用事件数组二分定位时间范围 `[max(0,positionMs-L),positionMs]`，只收集其中 Release 的 noteId，去重后交给音源。边界 age=L 的候选可以保守保留，最终由音源判定其增益已为零而拒绝起音；L=0 时无需恢复任何释放尾音。

候选不等于一定发声：音源还须检查样本是否自然结束、移调后的源位置、释放包络及声音池容量。不得为了重建一条尾音从曲首回放 NoteOn。窗口内原始事件较密时，查询成本为二分加窗口事件数；这是有限窗口，并非无条件常数开销。

基础数组约为 `28N + 36N = 64N` 字节；检查点另需 `4×(2K+1+A)` 字节。举例：5 分钟、10 万音符，基础数组约 6.4 MB（约 6.1 MiB）；K=301，若每检查点平均活动 12 音，A=3612，检查点约 16.9 KB。该估算不含字符串、Unity/Udon 堆、序列化副本、场景对象、音频与可见音符池。长踏板使 A 增大，导入报告须列 N、K、A、最大同时按键数、最大逻辑活动数、候选峰值估算和原始数组字节数；超出项目配置预算时明确阻止烘焙或要求调整输入，不能运行时静默截断曲目。

## 七、导入器交付内容

批量窗口计划提供文件/文件夹选择、选轨、预览摘要、逐文件诊断、重导入及烘焙入口。成功报告可查看 tempo 数、有效音符数、时长、忽略表达与补全情况；错误应定位源文件、轨和事件位置。诊断区分文件致命错误、可播放但还原受限的警告及普通统计，不能把三者合并成一个成功勾选。选轨或设置改变后应显示数据已过期，要求重新导入再烘焙，防止 Inspector 显示新配置却发布旧数据。最终资产在重新打开工程和重导入后仍保持 Prefab 引用。上述均为后续实现与验收要求，本次仅落盘设计文档。
