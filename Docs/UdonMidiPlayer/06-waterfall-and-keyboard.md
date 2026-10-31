# 06 · 88 键键盘与钢琴瀑布流

本册将 [数据与导入](02-data-and-import.md) 的音符区间转成键盘和瀑布流。`PianoKeyboardView` 与 `MidiWaterfallView` 只读 [统一约定](CONTRACTS.md) 中的当前时间和 Sequencer 状态，不决定发声、不写网络字段，也不根据 Transform 运动反推播放进度。音频生命周期见 [钢琴音源](05-piano-audio.md)，控制入口见 [控件与 UI](07-controls-and-ui.md)。参数是待验证的设计起点。

## 1. 布局与坐标约定

键盘覆盖 MIDI 21～108，A0～C8，共 88 键，其中 52 白键、36 黑键。建立长度为 88 的 `keyX/keyWidth/keyIsBlack` 缓存，数组下标始终为 `pitch-21`；不直接用 MIDI 音高等距排布，否则黑白键宽度和八度边界会失真。

曲目可保留完整 MIDI 0～127。21～108 之外的瀑布音符和命中特效沿键盘两侧延伸：初始化时建立 128 音高缓存，以边缘同音名琴键的落点、黑白类型、宽度及相邻八度的 X 间距外推。域内落点保持原样；虚拟键继承同音名键的 Y/Z，E/F 与 B/C 仍没有黑键。实体琴键和 `keyDownCount` 保持 88 项，域外音符参与活动音符及音频生命周期，但不挤到实体边缘键，也不触发其按压。

白键按白键序号连续排列，中心为 `(whiteIndex+0.5)*whiteWidth`；整体再减去键盘中心，使局部 X=0 位于中间。黑键放在相邻白键的分界线上：C/D、D/E、F/G、G/A、A/B 之间存在黑键，E/F 与 B/C 之间没有。第一个 A0 后有 A#0，最后 C8 后没有额外键。生成器在编辑器验证 52/36 数量、相邻八度边界和 A0、C4、C8 的位置。

默认白键宽度为 1 个局部单位，黑键宽度约 0.62，白键深度 5、黑键深度约 3，黑键顶面略高。整套 Prefab 通过一个 ViewRoot 的统一缩放控制世界尺寸；宽度、深度和瀑布高度都用局部坐标计算，不把 worldScale 重复乘入音高映射。父级非等比缩放给出编辑器提示，避免键盘与条带比例失真。允许开发者替换键盘 Mesh，但必须保留统一音高索引和命中线位置。

音符条沿局部 Y 向命中线下落，命中线固定为 Y=0，未来区域为 Y>0；音高决定 X，局部 Z 只用于黑白条和键盘的遮挡层次。运行时不通过 Rigidbody 或碰撞器判断“命中”。初始显示高度 H=6 局部单位、未来窗口 W=3000 ms，下降速度为 `H/(W/1000)`；改变 ViewRoot 缩放不改变曲目时间与命中瞬间。

## 2. 绝对时间决定音符几何

每帧只从 Transport 取得一次 positionMs，所有条带、按键和 UI 使用相同帧样本。音符条表示实际按键持续区间 `[startMs,keyUpMs]`，长度不延长到 releaseMs；踏板延长的是可闻声部，不能把踏板持续误画成手指一直按键。

对音符 i，未裁剪的两端为：

```text
bottomY = (startMs[i] - positionMs) * H / W
topY    = (keyUpMs[i] - positionMs) * H / W
```

条带的底端对应起音，顶端对应松键。KeyDown 时 bottomY 正好经过命中线；随后已按下部分裁在命中线之外，剩余部分缩短直到 KeyUp。实际绘制把区间与 `[0,H]` 相交，中心为两端平均、长度为差；禁止逐帧 `Translate(speed*deltaTime)` 累积误差。正在播放的长音从命中线向上延伸，顶部可被视窗边界裁掉。

条带宽度根据对应 keyWidth 减去约 8% 间隙，黑键条使用黑键宽度，不能所有音符一律同宽。零时长或特别短的音符可给约 0.03 局部单位的最小视觉厚度；该厚度只影响 Mesh 外观，开始、松键和高亮仍按真实毫秒时间。非零时长音符到达 `keyUpMs<=positionMs` 时隐藏，不能因最小厚度继续停在命中线；零时长音符只在临近前视区显示薄片，不承诺以帧率保证可见按压。条带不越过相邻琴键，不允许负缩放。

命中只由 Sequencer 的 KeyDown/KeyUp 状态确定，不能凭一个条带是否碰到键盘来修改声音。色彩首版固定白键音/黑键音两个配色及命中亮色，保证两类键有可辨明度；不加入粒子、动态调色或音符特效 API。

## 3. 可见查询与 512 条对象池

默认预建 512 个条带槽位，每槽只保存 `noteId/generation/transformIndex/visible` 等基础信息，没有每音符 UdonBehaviour。播放过程中不 Instantiate/Destroy，不创建临时数组或把每条音符注册为 Update。池容量不足只减少画面，不改变播放逻辑、音频复音或公共状态。

候选区间必须按交集判断：`startMs<=positionMs+W && keyUpMs>=positionMs`。不能只查询最近 W 秒内“开始”的音符；一个十秒前开始、现在仍按住的长音仍应显示在命中线上。顺序推进时保留尚未 KeyUp 的既有候选，再从按 start 排序的索引添加将进入视窗者；已结束者回收，整条在远端视窗外的候选不占渲染槽位。

重建必须读取 `Sequencer.RebuildAt(positionMs,generation)` 完成后的最终活动集合，再筛出 `keyUpMs>positionMs` 的已开始音符；不能直接读取原始 checkpoint CSR，否则会漏掉检查点后、当前位置前开始的长音。随后查询 `[positionMs,positionMs+W]` 内即将开始的音符，按 noteId 去重，保证恰在 positionMs 开始者只出现一次。重建利用数据层索引，不能每帧扫描全部历史。Release 尾音候选不自动加入条带：已 KeyUp 的音符可以继续发声，条带仍按按键区间结束。

超容量时优先当前物理按住且穿过命中线的音符，再按距离命中线最近的起音优先，最后按 startMs、noteId 稳定排序；保留新候选只替换较低优先级槽位。这个规则避免过密远期和弦把正在演奏的长音挤掉。开发诊断统计 `visibleCandidateCount/visibleRenderedCount/droppedVisualCount`，用户面板不暴露实现计数。

每帧只更新活跃槽位，X、宽度、基础颜色在分配时从缓存写入；Y 和长度使用当帧 positionMs。回收槽位时清 noteId 与旧 generation，恢复局部缩放，防止上一条长音或黑键宽度污染下一条。池的隐藏方式由渲染实现选定并在 M0 验证，不能假设缩放为零必然没有渲染成本。

## 4. 按键高亮与踏板声部分离

Sequencer 维护 `keyDownCount[88]`：每个 KeyDown 加一，匹配的 KeyUp 减一，结果不得小于零。同音重叠时只有计数变成零才熄灭；不同通道同音也共同贡献该键计数。视图读取计数是否大于零，无法按“收到任意 NoteOff 就灭灯”处理。

CC64 延音期间，松键之后键盘立即回到未按下状态，即使声音仍在；Release 和声音池窃音都不修改按键灯。暂停保留冻结时刻的键盘高亮，停止和切曲清空计数。晚加入、重建和内部重定位直接刷新全部 88 键状态，不能靠回放音符动画逐步追赶。v0.1 只要求基础颜色/亮度高亮；可选约 1～2 mm 的视觉下压只是本地表现，不创建网络 Transform 同步或机械动画控制器。

键盘材质只在亮灭发生变化时更新；条带可以用共享材质配合顶点色、共享 Mesh 的实例参数或已验证的属性块路径。不得在逐帧循环访问会隐式复制材质的路径，不为 512 条音符各创建一个独立材质。首先用单一兼容 Shader/材质完成基本显示，渲染合批能力和 Udon 属性写入成本必须以当前 SDK 实测，不能把“单材质”等同“只有一次 draw call”。

## 5. 暂停、重建与边界行为

Playing 按统一时间绘制；Paused 继续渲染但固定 positionMs，条带与按键不随现实时间移动；Stopped 清条带和高亮并回到零。调用契约由 Facade 保证：进入 Stopped 只执行两视图 Clear 和 UI 更新，不调用视图 RebuildAt/Refresh；后续停止帧不重复清池。只有 Playing/Paused 才允许重建和刷新，避免以 positionMs=0 重新填入首音和未来条带。Paused 中加入应一次得到相同冻结画面，随后从该时间继续。首版虽不提供进度拖动，网络校准和晚加入仍须使用内部恢复能力。

每次停止、切曲、重定位或撤销本地推测时，视图先清旧代次映射，再绑定正确曲目及新 generation。目标为 Playing/Paused 时，先完成 Sequencer 重建，再按新 positionMs 填池和高亮；目标为 Stopped 则保留空池。不要从旧位置滑到新位置，以免短暂显示不存在的命中。远端快照仍处于本地 Rebuilding 时，可先隐藏视窗、保留加载状态；只有该 generation 的数据齐备才显示，禁止旧曲音符闪回。

`durationMs` 是 MIDI 逻辑末尾，包含源 EOT 与最后 Release；曲终、Clock 和 UI 使用运行时派生的 `playbackEndMs=durationMs+PianoBankData.maxReleaseMs`。尾垫期间仍保持 Playing，在最后一个 KeyUp 后即使存在 Release 尾音，也不伪造条带或按键高亮；到 playbackEndMs 再按统一终态清理，不新增 Draining 状态。大卡顿后由 Sequencer 决定顺序推进还是重建，视图直接显示当前时间，不补演每个错过的命中动画。视图关闭再打开也走当前时间重建，不依赖隐藏期间累计位移。

## 6. 计划接口与 Prefab 接线

以下为计划中的本地具体 Behaviour 方法，不是接口类型或网络事件。引用类型只使用当前工程可烘焙的 UdonSharp Behaviour，所有数组在初始化阶段分配并缓存。

| 方法/字段 | 参数及责任 |
| --- | --- |
| `PianoKeyboardView.BindSequencer(MidiSequencer sequencer)` | 绑定只读 keyDownCount 来源 |
| `PianoKeyboardView.Refresh(int positionMs,int generation,bool force)` | 仅 Playing/Paused 调用；force 重写全部 88 键，否则只写变化键 |
| `PianoKeyboardView.Clear(int generation)` | 清高亮、位移与旧缓存，记录新代次 |
| `MidiWaterfallView.BindSong(MidiSongData data,MidiSequencer sequencer)` | 绑定音符数组及恢复候选来源 |
| `MidiWaterfallView.RebuildAt(int positionMs,int generation)` | 仅 Playing/Paused；Sequencer 重建完成后，按最终活动及未来区间填池 |
| `MidiWaterfallView.Refresh(int positionMs,int generation)` | 仅 Playing/Paused；维护候选与裁剪几何，旧代次调用无效 |
| `MidiWaterfallView.Clear(int generation)` | 回收全部条带并清理可见查询游标 |
| `windowMs`, `viewHeight`, `barGapRatio`, `minimumBarHeight` | 默认 3000、6、0.08、0.03；校验窗口和高度大于零 |
| `keyTransforms[88]`, `barTransforms[512]`, `sharedMaterial` | 编辑器检查长度、唯一引用及缓存完整 |

Prefab 的 KeyboardRoot、WaterfallRoot、HitLine 共用同一个 ViewRoot，命中线与白键前沿一致；显示窗口尺寸、条带间隙和颜色可在 Inspector 调整。生成布局由 [编辑器与 Prefab](08-editor-and-prefab.md) 负责，运行时仅消费缓存，不要求世界作者手工接 88 个键和 512 个条带。

## 7. 验收与契约补充

画面用可精确核对的测试曲覆盖 A0/C8、黑白键相邻、同音重叠、零时长、跨视窗长音、踏板松键、密集和弦、暂停恢复及中途加入。逐帧核对音符底端、按键亮起和同一 Transport 时刻，不以听感替代几何正确性。峰值超过 512 的测试应只出现可解释的远期条带降级，声音和按键计数继续正确。

统一契约需要保留“检查点恢复后的最终逻辑集合可找到跨窗长音”“独立 keyDownCount 可恢复”“Refresh 共用同帧时间且不在 Stopped 调用”三个前提；若数据层把 active 只定义为可闻采样而丢掉自然结束但仍按住的音符，就无法恢复键盘和长音条，必须保留逻辑活动区间。单材质、对象池和缓存并不构成已通过的性能结论；实际帧时、渲染和 Udon 成本按 [性能与验证](09-performance-and-validation.md) 执行。当前仅交付设计，未生成模型、图片或材质，未运行 Unity。
