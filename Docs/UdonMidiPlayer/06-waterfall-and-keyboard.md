# 06 · 88 键键盘与钢琴瀑布流

本册将 [数据与导入](02-data-and-import.md) 的音符区间转成键盘和瀑布流。`PianoKeyboardView` 与 `MidiWaterfallView` 只读 [统一约定](CONTRACTS.md) 中的当前时间和 Sequencer 状态，不决定发声、不写网络字段，也不根据 Transform 运动反推播放进度。音频生命周期见 [钢琴音源](05-piano-audio.md)，控制入口见 [控件与 UI](07-controls-and-ui.md)。参数是待验证的设计起点。

## 作者工程：文件通道黄金角配色（2026-11-04）

文件播放及暂停以源 MIDI 哈希色相为起点，按原始 MIDI 通道索引 0～15 使用 `Repeat(baseHue + channel * 0.38196601125, 1)` 取色，相邻通道递增约 137.507764°。同通道跨音轨／映射乐器保持同色；改变乐器或八度不重新编号通道。保留黑白键的同色系深浅与实体黑键落下时的提亮。实时演奏继续按玩家 Display Name 取统一主色。

瀑布 GPU 槽及非 GPU 条带、实体琴键、命中网格、尘粒／符号、弦乐接触点／弦振动、鼓组条带／命中光圈和钟楼音符连线共用原始通道颜色。通道调色板仅在主题变化时生成，颜色经缓存及 MaterialPropertyBlock 传递，不按通道克隆材质。同键多通道重叠时实体琴键显示最近起音且仍按下的音符，释放后恢复剩余音符的通道色；各瀑布条带保留自身颜色。定位仅恢复安静持音层，不重播粒子；停止／实时切换清除琴键通道覆盖。

UdonSharp 编译及 87 项隔离 Editor C# 代理检查通过，覆盖 16 通道黄金角／深浅、重叠键与定位恢复、GPU 颜色、实时切换、跨乐器一致性、鼓组及钟楼连线。记录：`Logs/GoldenChannelPalette-Validation.json`、`Logs/GoldenChannelPalette-CrossInstrument.json`。未进行真实 VRChat／多客户端验收。只更新共用 Runtime／Shader 与文档，不重存场景、分发 Prefab 或练习场景。

## 作者工程：玩家实时配色（2026-11-04）

实时演奏的主色调改由演奏者原始 Display Name 的 UTF-16 字符逐个执行 32 位 FNV-1a 哈希（初值 `2166136261`、乘数 `16777619`）生成，沿用现有 HSV 与亮／暗调色板映射；同名同色，保留大小写、空格与 Unicode，不使用不保证跨进程稳定的 `string.GetHashCode()`。硬件 MIDI、PC 键盘与 VR 输入共用 Facade，远端从已校验演奏者的 transport Owner 获取名称，个人练习从 LocalPlayer 获取；不新增同步字段。首次起音、完整实时快照恢复及 Owner 切换均更新配色，键盘、瀑布 GPU 槽／材质、两层粒子、弦乐和鼓组统一跟随。颜色变化复用缓存，不清理持音、踏板或动画。

文件播放与暂停继续使用源 MIDI 哈希，停止后的实时演奏使用玩家配色；仅切换所选曲目不改变玩家配色。UdonSharp 编译通过；独立 Editor preview 的 21 项 C# 代理检查覆盖名称大小写／Unicode、跨乐器一致性、切歌与文件配色恢复、GPU 实时槽及符号材质，记录见 `Logs/DisplayNamePalette-Validation.json`。未进行 ClientSim、真实 VRChat 或多客户端验收。下文保留早期设计说明，以本段和当前源码为现行实现依据。

## 作者工程：钢琴逐键命中与低负载粒子（2026-11-04）

本段为现行实现；下文早期对象池／材质设想不覆盖本段。主场景和钟楼第二场景的两种钢琴已增量接入，马林巴、八音盒、独立练习场景及两份分发 Prefab 不自动迁移。共用 Runtime 的活动槽遍历优化仍适用于原配置，不改变声音、音符路由、网络字段或曲目身份。

- 保留 `WaterfallVolume` 的不透明单 Pass 水晶切面、宽度相关倒角和静态环境反射。钢琴通过可选 `pianoImpactPresentation` 启用力度响应，复用 `_NoteTiming.w` 传入归一化力度；文件取最终路由音符的原力度，实时事件和持音快照取对应实时力度。落键端按实际宽度局部提亮，约 25 ms 达峰后迅速衰减，不整根长音泛白；原全局同步呼吸改由较低的琴键持续底光与逐音冲击分担。其他乐器维持原着色分支。
- `MidiWaterfallEffects` 的可选 `keyImpactRenderer`／`keyImpactRootRenderer` 分别绑定前沿和整排键盘后沿的预烘焙网格，运行时只启用当前落点的一份。每网格覆盖 MIDI 0–127，1024 顶点、512 三角形、一个材质、一个 Pass；域外沿同音名八度间距外推，和弦逐音高独立，同音高重叠复用对应落点，以实际物理按键计数决定持音。每音高仅保存起音／力度／松键参数，变化时上传固定 128 项数组；Shader 根据音乐／本地演奏时钟计算亮核、窄光晕和向上短光。没有逐键 GameObject、逐键 Update、额外 Light 或粒子碰撞。
- 松键立即进入退光，CC64 不延长物理按键状态。文件暂停保持音乐时间和已发射粒子，恢复继续；停止清理。定位／回退／晚加入只恢复低亮持音层，不回放旧冲击，过期 generation 不修改新画面。文件按既有 `maxCatchUpMs`／`maxHitAgeMs` 丢弃过时的装饰起音。
- 钢琴尘粒与符号各合并为一个世界空间系统：分别上限 160／32，总上限 192、每帧最多发射 24 个；同帧最多处理 128 个近期起音。高密度时减少尘粒，逐键冲击独立于装饰粒子预算。符号发射至少间隔 140 ms，一套 4×2 图集随机覆盖原有六种符号，`startFrame` 使用归一化范围 `[0,0.7499]`，避免采到末尾两格空白。取消世界碰撞、拖尾、粒子灯和距离排序；仍由音乐时间手动模拟，暂停不继续飘散。
- 保留 512 槽容量和按当前时间裁切的 GPU 下落；文件回收和实时尾部回收遍历活动槽，不再每帧检查全部空槽。对象分配、重建、换色与定位仍是独立边界，不通过逐帧重建网格或材质实现动画。

维护入口：`MidiPianoImpactBuilder.InstallLoadedScene()`，菜单 `Tools/Music MIDI Player/Presentation/Upgrade Piano Impacts in Current Scene`。仅在保存过的场景、非播放状态执行；先保留内存场景副本，再增量重接既有尘粒／符号池并移除退役分区、光斑和钢琴命中灯。水晶 GUID、键盘、声音源和曲库引用保留，按既有模型名原位更新命中网格 GUID；不 Apply 到分发 Prefab。常规钢琴表现 Builder 也调用此配置，仍须遵守目标场景的单灯约束。

证据：`Logs/PianoImpacts/editor-preview.json` 的 22 项隔离 C# 代理检查覆盖轻重音和弦、文件／实时力度、暂停代次、松键、定位恢复、过期代次、前／后沿切换、128 音高密集命中、粒子预算及实际符号渲染 UV；`Logs/PianoImpacts/playmode.json` 的 10 项 backing Udon VM 检查覆盖场景开关、实时输入、力度传入 GPU、踏板下松键、暂停、停止与单灯／64 声源约束。实景样式预览在 `Captures/PianoImpacts/`。C#／UdonSharp 和 Shader 编译通过；未构建、上传或进行真实 VRChat／多客户端／物理 MIDI／VR 验收，未测量客户端 FPS 或 GPU 毫秒收益。

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
