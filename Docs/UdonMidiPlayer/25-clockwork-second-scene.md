# 钟楼维修街：独立第二场景

**历史版本（2026-11-04）**：用户已将当前美术方向切换为独立的[海底音乐庭院](27-underwater-music-garden.md)。本场景资产标记 `Historical` / `Clockwork-2026-11-04`，原 `.unity` 路径、GUID、环境、光照、机械和播放器接线保留，不作为海底版本的维护目标。以下内容保留为钟楼版本的实现与验证记录；其中“当前”仅指本历史版本。新场景不覆盖或替代主场景，也不将新布局回写钟楼。

2026-11-04，用户明确要求在 Unity 创建第二场景，使用已选定的第 4 号外部钟楼美术，并且只允许一个点光源。本场景不替代主场景。

## 资产与维护入口

- 场景：`Assets/Scenes/ClockworkMaintenanceStreet.unity`。
- 可编辑 Blender：`ArtSource/ClockworkMaintenanceStreet/ClockworkMaintenanceStreet.blend`。
- 环境导出：同目录 `export_unity.py`，调用 `upgrade_mechanism.py`；读取当前可编辑 `.blend` 运行，保留导入资源及 `.meta`。脚本仅替换自有的机械与封闭结构，导出移动轴心和材质批次。
- 模型、材质、纹理、碰撞清单：`Assets/Scenes/ClockworkMaintenanceStreet/`。
- 一次性创建器：`MusicMidiPlayer.Editor.SceneArt.ClockworkMaintenanceSceneBuilder.Create()`；仅允许已保存主场景，拒绝覆盖已存在的第二场景。
- 导入环境修改后，`UpdateEnvironmentColliders()` 只更新第二场景专属的生成碰撞层级，不处理播放器、曲库或共享 Prefab。
- 机械 / 时间 / 日月光接入：`MusicMidiPlayer.Editor.SceneArt.ClockworkMechanismBuilder.Upgrade()`，菜单 `Tools/Music MIDI Player/Scene Art/Upgrade Clockwork Mechanism and Daylight`。在第二场景、非播放状态执行；从 `mechanism-manifest.json` 接线并更新专属 Udon 程序、材质、碰撞及场景。
- 运行控制器：`Assets/UdonMidiPlayer/Runtime/Views/ClockworkSceneController.cs`，`MusicMidiPlayer.Views` 命名空间，网络 `None`。移动节点不使用静态合批。
- 10 MB 美术精修：`ClockworkArtRefinementBuilder.Install()`，菜单 `Tools/Music MIDI Player/Scene Art/Refine Clockwork Art (10 MB)`；只接受非播放的第二场景，先备份内存场景再增量安装。原创图纸图集由 `ArtSource/ClockworkMaintenanceStreet/create-workshop-atlas.ps1` 生成，其余精修纹理与网格由此 Builder 生成，资产位于第二场景的 `ArtRefinement/`。

场景从当时已保存的主场景独立复制，保留播放器、歌曲身份、音源及 Udon 引用，替换教室环境。当前初始状态仅启用一架三角钢琴（`enabledMask=1`、`pianoAppearance=1`），居中落地；其他乐器和鼓组保留，可通过菜单勾选加入本页下述固定组合扇形布局，八音盒独立放在自己的桌面上。此项工作没有新增 128 套音源或通用乐器注册协议。

控制面板位于中央组合区前侧左方，Credits 与作者／社群／赞助板分别挂在入口墙两侧，具体坐标见下节。出生点位于组合区前方。环境导出排除 Blender 摄影灯、相机、雾效以及示意乐器，使用实际 Unity 播放器。回廊楼板在导入源中切出楼梯口，并设置对应护栏与碰撞。

## 2026-11-04 UI 重新布局与社群板入场

同日后续：主控制台与许可／Credits 面板统一为深绿底、金色强调／描边与暖白正文，包含全部弹层和交互状态，维护入口与色板见[钟楼绿金主题](15-ui-hierarchy.md#2026-11-04-钟楼绿金主题)。这项场景覆盖不改变下面的位置或共享 Prefab。

用户要求将已导入的作者／赞助／社群预制体放入当前钟楼场景，并统一规划所有独立 UI 的位置。`ClockworkUILayoutBuilder.Install()`（菜单 `Tools/Music MIDI Player/Scene Art/Arrange Clockwork UI and Community Boards`）只接受非播放的钟楼场景，先将当前内存保存到 `RecoveryBackups/ClockworkUI-20261104/`，再增量放置并保存；不替换现有面板，不 Apply 到共享 Prefab。

| 对象 | 世界位置 XYZ（米） | 旋转 XYZ（度） | 用途 |
| --- | --- | --- | --- |
| 播放器 `Presentation/UI` | `(-3.8, 1.38, -4.85)` | `(12, 330, 0)` | 演奏区前侧左边，朝向出生点侧，略后倾；沿用 0.0018 缩放，补齐落地支架 |
| `Presentation/Credits` | `(-4.6, 1.7, -12.5)` | `(0, 180, 0)` | 入口墙西侧，沿用 0.002 缩放 |
| `ClockworkMaintenanceStreet/AuthorSponsorCommunityBoards` | `(4.7, 1.7, -12.5)` | `(0, 180, 0)` | 入口墙东侧，作者／四个社群入口及赞助者组成一组，保留原组合内部布局 |

中央入口与通往乐器的通道留空，查看信息无需经过楼梯或机械护栏。控制台支架为 5 个方体合并的单网格、复用钟楼铸铁材质，无新增运行组件或 Light。四个独立 Canvas 的碰撞体均为 trigger，不形成物理挡墙。曲库、设置、语言、乐器与映射弹层继续随原控制面板移动，原绘制顺序和回调保持；`SongOpening` 继续随各自乐器，避免破坏切换布局时的曲名位置。

Editor 几何检查：全部 512 个乐器配置的最低 Z 为 −1.98504 m，控制台支架最高 Z 为 −4.09055 m，前后净距至少 **2.10551 m**；四块面板前方 1.25 m 的查看射线没有实体遮挡。场景无缺失脚本，含未启用对象仍只有一个 Point Light。主场景、两份播放器分发 Prefab 及原组合预制体的前后 SHA256 均相同。记录见 `Logs/ClockworkUI/` 与 `Logs/ClockworkUI-protected-*.json`，实景见 `Captures/ClockworkUI/`。本轮没有执行 Play Mode、在线赞助名单下载、VRChat 客户端交互、构建或上传。

## 固定组合扇形布局（2026-11-04）

用户确认**仅钟楼场景**使用新排列。主场景、练习场景及两份分发 Prefab 不接入此布局，仍保留原有配置。

- 沿用乐器列表顺序：钢琴、马林巴、小提琴、中提琴、大提琴、低音提琴、八音盒、鼓组；两种钢琴外观互斥，停用的旧弦乐组不参与。
- 当前勾选集合决定完整布局，与点击顺序、切歌和播放时间无关。共享状态仍使用已有 `enabledInstrumentMask`、`pianoAppearance`；不新增网络字段或修改曲目 / 路由身份。
- 以 `Presentation` 局部 `(0, 0, -1.8)` 为当前组合占地包络中心（初版为 `(0, 0, 0.5)`；后续布局预览将整组向入口平移 2.3 m）。每组按实际模型、支撑物和座椅的局部包围盒计算，排除瀑布、粒子与隐藏占位；整体 X/Z 包络居中。单件也居中，空集不显示乐器。
- 乐器沿圆弧排列并朝向扇心。最小圆弧半径 4.5 m，展开角度上限 110°；数量增加时调整半径。相邻**占地矩形边界净距统一 0.65 m**，不按不同宽度模型的中心等距冒充等间隔；所有非相邻模型也检查不相交。
- 每件组合的最低支撑面落在 Y=0。钢琴脚／马林巴轮与座椅预先统一支撑面，消除导入模型根部的毫米／厘米级悬空。小提琴、中提琴保留演奏高度，增加从地面连接到琴体底部的实体展示支架；大提琴与低音提琴按底部调整根高度。修复八音盒 `SideTable` 的错误局部偏移，桌子、座椅随盒子一同排列；与三角钢琴同时启用也保留自己的桌子，不再挂到钢琴上。
- Editor 预先烘焙 256 个选择集合 × 2 种钢琴外观，共 512 组检查配置（钢琴未选时两种外观布局相同）。运行时按集合直接读取位置 / 朝向，没有逐帧布局计算。鼓组开关同样重排，并移动正在发声的旋律及鼓组空间原点；仅切鼓组仍保留旋律按住音、起音时间、代次和播放状态。

维护入口为 `ClockworkInstrumentLayoutBuilder.Install()`，菜单 `Tools/Music MIDI Player/Scene Art/Arrange Clockwork Instruments in Fan`；只接受非播放的钟楼场景，先保留内存场景副本，再增量接线和保存。模型或座椅的占地变更后重新执行此入口烘焙；不要只改运行时数组的一部分。运行组件 `MidiInstrumentFanLayout` 通过 `Facade.instrumentFanLayout` 在该场景显式接入，网络模式 `None`；其他场景的空引用继续使用既有排列。

检查记录：`Logs/ClockworkFanLayout/geometry.json` 检查全部 512 配置、3,584 对模型，最大相邻间距误差小于 0.000002 m，最大组合中心误差小于 0.000001 m。实际 Unity Editor Play Mode 的 backing Udon VM 通过 247 项检查，涵盖空集、鼓组单选、提琴单选 / 组合、两种钢琴外观、钢琴＋八音盒、原生鼓组复选框、按住音及活动鼓声随布局移动；记录见 `playmode.json`。实景图位于 `Captures/ClockworkFanLayout/`。包含未启用对象仍为 1 个 Point Light，物理声音池仍为 64；主场景、练习场景和两份分发 Prefab 的前后哈希相同。本轮未构建、上传或执行真实 VRChat 客户端验收。

## 单一点光源约束

2026-11-04 钢琴表现后续升级：本场景的三角／数码钢琴接入逐音高合并命中网格和力度落键提亮，尘粒／符号合并为两个无碰撞系统。两种落点网格互斥显示，不创建 Light；第06册记录实现、维护入口与 Editor／backing Udon 验证边界。本场景其他乐器、布局和钟楼日月灯保持既有职责。

第二场景包含且仅包含 **一个 `Light` 组件**，名称 `Clockwork_SolePointLight`，类型 `Point`，模式 `Realtime`。统计包含未启用对象。

- 不导入 Blender 灯光，不保留主场景方向光。
- 四组瀑布命中灯数组在 proxy / backing 中清空，`maxActiveLights=0`；对应额外灯组件删除。
- 反射环境强度、烘焙 / 实时 GI、旧光照图关闭或清空。
- 按后续封闭场景要求，唯一的点光源移到表盘外，随本地时间改变位置、亮度和颜色，太阳 / 月亮共用此组件。天空材质绘制渐变、日月圆盘及花形星点 / 星轨装饰，不增加光源。
- 封闭后的墙面漫反射使用随同一日月光变化的低成本 Flat Ambient 近似；这是环境着色，未烘焙反弹或添加隐藏灯。不能将本场景描述为“环境照明为零”。
- 钟面使用单层半透明表面；齿轮与乐器按用户后续授权使用少量表面自发光提高可读性，GI 标记为 `EmissiveIsBlack`，不照亮其他物体。其余既有琴键、粒子等表现保留自身材质，不增加灯组件。
- 不通过额外禁用灯、烘焙灯或隐藏补光绕过数量约束。

日后向第二场景重新运行其他乐器 Builder 时，须重新检查其是否生成命中灯，不能仅数启用的环境灯。

## 2026-11-04 机械、封闭与时间升级

后部装饰轮替换为统一模数 **0.16 m、20° 压力角**的渐开线齿轮：8 根活动轴、9 个直齿轮（其中一根轴前后复合），另有 30 齿擒纵轮。轮毂、辐条、轴、固定轴承、承座和落地框架均有实体连接。啮合中心距按 `m × (父轮齿数 + 子轮齿数) / 2` 布置；相邻轮反转，角速度满足 `N父 × ω父 + N子 × ω子 = 0`，初始齿相位避免齿尖相撞。后层复合 20 齿轮与擒纵轴的 20 齿小轮位于同一深度平面。

所有轮体从同一擒纵相位解析求解，避免独立动画逐渐失配。每半摆释放半个擒纵齿距，短暂平滑放行后锁止；齿轮同步跟随。7800 拍构成完整重复周期，跨午夜不重置机械相位。这里采用约束动画，不使用每齿刚体碰撞求解。齿轮面重新计算外向法线，保留倒角及逐面角点法线；Unity 导入模式为 `Import`。

钟摆补上与后部木结构连接的钢吊架、上夹座、双片悬簧、活动下夹座、摆杆、透镜形摆锤、调节螺母，以及锚形擒纵和叉形拨杆。拨杆与摆杆的横向位移保持一致。有效摆长 **8.94246 m**，以 `2π√(L/g)` 求周期，约 **5.999956 s**，振幅 3.2°。机械构造依据可参阅 [Met 的历史钟表介绍](https://www.metmuseum.org/essays/european-clocks-in-the-seventeenth-and-eighteenth-century) 中的锚形擒纵、悬挂与 crutch fork，以及 [Jönköping University 齿轮讲义](https://mechanics.ju.se/MachineElements/spur-gears.html) 的渐开线与齿轮传动关系。

时、分、秒针分别以当前客户端 `DateTime.Now` 的本地民用时间计算；每 2 秒重新锚定一次，帧间使用实时计时平滑推进。不同客户端各自显示自己的系统本地时间，不使用房主 / 服务器时间。修改系统时间后会在下一次锚定时跟随。

场景补齐前墙和关闭的入口、前后山墙、整片双坡屋顶、檐口与屋脊闭合。后墙为真正的圆形表盘开孔，外侧由半透明表盘封住；保留回廊、楼梯开口与护栏。环境当前导出 122 个网格批次、381464 三角形、22 种材质、46 个生成碰撞体；这些是环境自身统计，不包含完整播放器成本。

## 表盘透光与日月变化

- 表盘为一个 128 三角形圆片，`MusicMidiPlayer/Clockwork/DialGlass` 单 Pass Alpha Blend，无 GrabPass、折射、深度纹理或额外渲染相机，不投射实体玻璃阴影。
- 唯一 Point Light 从实际墙面开孔照入，框架和齿轮参与遮挡；透过率用光源强度乘数 **0.68** 近似，不做彩色透明阴影追踪。
- 日间定义为本地 06:00–18:00，夜间为其余时段。光源沿面向表盘的半弧移动，正午与午夜高度最高，低角度时衰减，在交替处降至零；天空和表盘色调在晨昏平滑过渡。
- 这是为立面构图设定的日月轨迹，未使用地理经纬度、季节或真实月相星历。点光源位置决定入射方向；其旋转本身不会改变 Point Light 的发光形状。
- 更新机械与指针仅操作少量 Transform，日月光与材质参数按秒更新。半透明表盘方案控制了额外渲染步骤，但未测量真实客户端 GPU 时间或帧率。

### 同日亮度调整

用户反馈整体过暗，尤其夜景难以辨认齿轮和乐器后，提高原有单一点光源的日 / 月强度系数至 `6.9 / 2.5`（仍乘表盘透过率与高度衰减），并提升现有 Flat Ambient 漫反射近似：日间 RGB `(0.64, 0.59, 0.50)`，夜间 `(0.44, 0.50, 0.64)`。晨昏保留可读的暗部下限，避免地平线交替时室内变黑。保留暖日光、冷月光和原来的动态阴影，未增加 Light、烘焙光照、后处理或材质发光补光。

重新编译 C# / Udon 后，用相同机位、相同机械姿态渲染日夜图并检查可见度；包含未启用对象仍只有 1 个 Point Light。最新图位于 `Captures/ClockworkBrightness/01-noon-brighter.png` 与 `02-midnight-brighter.png`；`00-before-*` 保留调整前对照。此轮为着色参数调整，未重复 Play Mode 或真实客户端测试；上一轮机械运行证据仍单独保留。

### 后续齿面可读性与天空装饰

用户继续要求提亮夜景的齿轮正面与乐器，并明确允许自发光和无光源粒子后，新增 `ClockworkReadabilityBuilder`。`Refresh()` 只更新第二场景的表现材质；完整机械 `Upgrade()` 也调用其配置，避免重装时遗漏。

- 四种钟楼金属材质使用场景专属 `MusicMidiPlayer/Clockwork/ReadableMetal`：保留原贴图、金属度、粗糙度和实时投影，在暗部加入与底色、磨损纹理及面法线相关的弱自发光，保留齿面与边缘的体积关系。
- 乐器的普通不透明 Standard 表面使用 `Materials/Readability/` 下 **31 份第二场景专属副本**，不更改模型内嵌材质或播放器共享材质。排除琴键、乌木、橡胶、毛毡及既有演奏特效；弦振动与击打表现沿用原 Shader。
- 控制器按秒更新共 35 种材质的可读性参数：白天偏暖、夜晚偏冷，保留昼夜整体照度与色温差。所有新增表面发光都不参与 GI，不产生 Light、光照贴图或额外阴影贴图。
- 天空在原单 Pass 中加入缓慢流动云带、五瓣花形星点、细星轨及轻微闪烁；夜间更清晰，白天减弱。为程序绘制的自发光天空图案，没有新增粒子系统或灯组件，不需要额外纹理、后处理或渲染相机。

本轮 C# / Udon / Shader 编译和实际相机日夜渲染检查通过；Unity Editor Play Mode 的真实 Udon VM 能更新 35 种材质、切换夜景天空参数，包含未启用对象仍为 1 个 Point Light，控制台无错误。记录位于 `Logs/ClockworkCelestial/`，最新图位于 `Captures/ClockworkCelestial/`。回到非播放状态后恢复当前本地时间并保存。主场景与两份分发 Prefab 哈希保持相同；未进行真实 VRChat 客户端或帧率验收。

## 2026-11-04 风景构图与 10 MB 美术精修

用户确认以风景作品的构图、材质和空间层次精修钟楼，新增资源预算为 **10 MB**。本轮直接增量更新已存在的第二场景，未重新导出或修改原 FBX，也未替换场景、曲库、机械传动或乐器组合布局。

- **材质与明暗**：保留既有环境材质 GUID，木材、石材和金属改用场景专属 `MusicMidiPlayer/Clockwork/HeritageSurface`。新增三套原创程序纹理；木材颜色为 1024，其余颜色与全部 RG 法线 / B 遮蔽 / A 粗糙度反向遮罩为 512。Standalone 颜色 DXT1、细节 BC7，启用 mipmap、三线性过滤和四倍各向异性过滤。保留顶点色、实时投影和法线细节，以宽范围着色降低梁顶、边角的视觉权重，黄铜增加氧化色、粗糙度变化和弱表面反射近似。金属表面补偿沿用原有 `_EmissionColor` 昼夜更新，不参与 GI、不照亮其他物体。
- **维修叙事**：西南侧新增守钟人工作桌，位置 `(-11.45, 0, -10.1)`，Y 旋转 270°，避开既有门；包括有倒角的桌面 / 抽屉、落地桌腿、原创机械蓝图、检修日志、绘图笔和油壶。现有维修柜台补图纸、备用齿轮、扳手和零件盒。新增实体桌的唯一 BoxCollider 位于西侧墙边，不进入乐器或楼梯的中央通道。
- **统一纹样**：原地毯上增加酒红底、细金边及钟表刻度纹样；闭合入口门增加绿金钟表徽记；墙脚少量黄铜镶条呼应既有绿金 UI。原地毯、柜台、门和 UI 对象均保留。屋顶增加连续内衬面，不移动原屋顶或机械。
- **空气层次**：5 条柔边透窗光带随唯一日月光源方向变形，另有最多 96 粒、不带 Light 的浮尘粒子，以及密度 `0.011` 的轻微 Exp2 距离雾。沿用 `ClockworkSceneController`，只增加两个表现材质的秒级参数更新；无新增 Udon 组件、网络字段、深度纹理、GrabPass、后处理或额外渲染相机。光带是低成本散射近似，未模拟真实体积遮挡或透明玻璃阴影。
- **天空**：保留花形星点和星轨，降低星点密度、白天强度及表盘中心附近的装饰强度，让指针和轮系更突出。

精修新增 21 个网格资产，合计 **11,080 三角形、14,424 顶点**；按局部区域 / 材质合并，含浮尘共 22 个 Renderer。生成器复用相同路径与已有 GUID，重复安装只更新自己的资产 / 节点，不 Apply 共享 Prefab。`ClockworkReadabilityBuilder.Configure()` 在完整机械维护时会恢复已安装的精修材质，避免被旧表现设置覆盖。可重复安装并非撤销工具；编辑过生成资源或参数后应先保留自己的版本。

### 预算与验证记录

`Logs/ClockworkArtRefinement/budget.txt` 的本轮统计：

| 口径 | 字节 | 十进制 MB |
| --- | ---: | ---: |
| 新增 `ArtRefinement/` 美术源资产，不含 `.meta` | 6,606,248 | 6.606 |
| Editor 中导入贴图的内存统计 | 5,597,432 | 5.597 |
| Editor 中导入网格的内存统计 | 3,143,880 | 3.144 |
| 两类导入资产内存合计 | 8,741,312 | 8.741 |

上述源资产大小、Editor 内存和世界下载体积是三个不同口径。此次**未构建或上传世界，最终世界包下载增量未测量**；本轮不得据此声称已经验证了压缩世界包的 10 MB 增量，也未完成真实客户端帧率或 VR 双眼验收。

实际 Editor 相机输出位于 `Captures/ClockworkArtRefinement/Before/` 与 `After/`，包括正午、17:00 夕照和午夜的入口、回廊与维修桌机位。最终维修桌机位随避门调整；入口 / 回廊对比机位保持一致。截图为实际场景渲染，拍摄后恢复客户端本地时间并保存。

`Logs/ClockworkArtRefinement/playmode.json` 记录真实 backing Udon VM 的参数化 `EvaluateAt` 调用：正午 / 午夜氛围参数分别为 1 / 0，光源方向、距离雾和表现材质有效，轮系 / 秒针 Transform 更新，浮尘存活 71 粒、上限 96，全场景含未启用对象仍为 1 个 Point Light，粒子 Light 模块关闭，Udon `HasError=false`。C# / Udon / Shader 编译与控制台错误检查通过。Play Mode 有双 EventSystem 警告（`UnityEventSystem` / `EventSystem`），本轮未改动 UI 事件系统。

练习场景、两份分发 Prefab 及原环境 FBX 的前后 SHA256 相同；没有 Apply 场景实例覆盖。主场景在中途检查时哈希相同，但末次检查发现 18:30:38 的同期文件变化；本轮没有打开或显式保存主场景，未确定该变化来源，已保留而未回滚，详见 `Logs/ClockworkArtRefinement/protected-hashes.json`。开始前场景与表现材质 / Shader 备份位于 `RecoveryBackups/ClockworkArt-20261104/`。退出 Play Mode 后保留当前本地时间、第二场景已保存。

## 2026-11-04 装饰文字：意大利语与手写花体

用户要求将场景中的装饰英文改为小语种与花体后，钟楼环境统一使用意大利语与 `Segoe Script` 手写字形：

- 两处既有店招：`Bottega del tempo`（时间工坊）。
- 图纸标题：`L’arte del tempo`（时间的艺术），图注与页脚同步为意大利语。
- 维修日志：`Il custode del tempo`（时间的守护者），栏目、六条维修记录与结语均改为意大利语。
- 入口徽章：`Bottega / 04 / d’orologeria`（04 号钟表工坊）。

只改环境装饰，不翻译播放器操作、曲库、作者／社群／赞助板或链接。字体仅在本机生成纹理与几何时读取 `C:/Windows/Fonts/segoesc.ttf`，没有将系统字体文件复制进资源；重新生成需安装该字体。

`ArtSource/ClockworkMaintenanceStreet/create-workshop-atlas.ps1` 增量更新原有 `WorkshopAtlas.png`，保留 1024×1024 图集与原 UV 分区，按牌面宽度适配文字；图纸底部为花体页脚留空。`refresh_decorative_lettering.py` 在 Blender 5.2 中更新可编辑 `.blend` 与原 FBX 内两处文字所属的材质批次，保留 FBX 节点 ID、名称、连接及其他 290 个对象；写入前逐项比较其他 FBX 数据。正常 `export_unity.py` 同样调用文字配置，后续环境导出不会恢复英文。资产及 `.meta` 的 GUID 不变，没有新增运行组件、材质、贴图或场景对象。

实际 Unity Editor 相机检查包含两处店招、入口徽章与工坊日／夜图，见 `Captures/ClockworkDecor/`；模型更新记录与保护文件哈希位于 `Logs/ClockworkDecor/`。分发 Prefab、练习场景与两份资源 `.meta` 的前后哈希一致；主场景与钟楼场景文件在本轮期间有同期保存变化，已保留，不宣称其哈希不变，本轮未调用场景保存或 Apply Prefab。场景无缺失脚本，包含未启用对象仍为一个 Point Light；未执行 Play Mode、世界构建、上传或真实 VRChat 客户端验收。

## 2026-11-04 第一工坊实体细化与共面检查

针对花体店招实景中的第一间工坊（Unity 中心 X/Z `(-5.7, 5.15)`），用户要求加强模型细节、补足厚度并检查 z-fighting。第二间工坊、机械、播放器和曲库未重建。

- 原棚顶只有两片单面网格，改为垂直厚度 **75 mm** 的封闭壳体，补齐底面、两侧山墙边、檐口、前后折边立缝、木压边与实体折弯屋脊盖板；边缘保留小倒角。
- 店招增加连续斜接木框、内嵌金属边、凹入牌面、角部螺钉与固定带；花体字改为 **8 mm** 实体浮雕，对相交笔画作精确实体并集，清除原字形重复封面。原 FONT 对象留作隐藏的可编辑源。
- 补足立柱金属底座、柱帽、柜台托木与搁板支架；既有油瓶增加肩部与细颈。藤蔓移到两侧立柱，叶片改为较小的曲面裂叶并补 **1.5 mm** 厚度，避免背面消失及遮挡文字。
- 通过几何尺寸与接缝处理，分开牌匾／立柱、柜台／立柱、压边／屋顶端面等原有或新增共面区域；相邻地面黄铜线缩短为有净隙的对接。未使用 Shader 深度偏移或双面渲染掩盖问题。

维护入口 `ArtSource/ClockworkMaintenanceStreet/refine_workshop_front.py` 在 Blender 5.2 中增量更新原 `.blend` 与 FBX；正常 `export_unity.py` 同样调用其 `apply_details()`。仅替换 9 个既有材质批次的 Geometry，保留其余 **283 个 FBX 对象**、节点 ID、连接、网格名称及模型 `.meta`；新源对象仍合并进原批次，没有新增 Unity 场景对象、材质、纹理、运行组件或 Light。环境仍为 **122 个网格批次**，三角形从本轮开始的 **429,824** 增至 **464,168**（增加 34,344），`clockwork-manifest.json` 的网格／三角形统计同步更新。FBX 源文件约增加 1.81 MB；此数字不代表世界包下载增量。

`audit_workshop_surfaces.py` 对第一工坊及周边区域检查三角形近共面重叠，平面间距容差 **0.5 mm**，投影重叠面积阈值 **1 mm²**；相反朝向的实体接触面单独记录，不作为可见同向争深度面。原源几何检出 8 组问题（含花体字自身封面重叠）；最终源几何扫描 258 个对象、72,111 个三角形，剩余同向共面重叠为 0。130 个细化实体网格的开口边、非流形边均为 0。

另通过 MCP 从实际 Unity 场景导出本区域的只读网格快照，包含独立 `ArtRefinement/RepairCounterDetails`：29 个 Renderer、76,646 个有效三角形的同向共面重叠同为 0。此检查覆盖该局部区域，不表示整世界或 VR 双眼都已验收。实际 Unity 相机日／夜、正面、斜侧、仰视、远景及左右贴边图位于 `Captures/ClockworkShopDetail/`；模型修改、源／场景表面检查和哈希证据位于 `Logs/ClockworkShopDetail/`。

主场景、练习场景、两份分发 Prefab 与 FBX `.meta` 的本轮前后哈希一致。钟楼场景有同期文件保存变化，已保留，本轮未显式保存场景或 Apply Prefab。最终含未启用对象仍为一个 Point Light，无缺失脚本，Console 无错误。没有执行 Play Mode、世界构建、上传或真实 VRChat 客户端检查。

## 2026-11-04 回廊木构连接与无新增光源的间接光烘焙

用户指出西侧回廊木构没有实际支撑关系，并要求补充不增加光源的烘焙照明。本节取代此前“无烘焙 GI”的当前状态描述；历史轮次的记录仍保留。

- `refine_gallery_structure.py` 增量修改可编辑 Blender 源与原 FBX 的两个既有胡桃木批次（03 / 11），保留其余 290 个 FBX 对象、节点 ID、材质连接和 `.meta`。西墙五根立柱落在回廊楼板上，顶部接连续顶梁；四根原本伸向空中的斜木改为墙平面内的柱／梁斜撑。楼板下补连续墙托木、横向托梁及外侧纵梁，楼梯段的斜撑与托梁缩短到楼梯口外。
- 正常 `export_unity.py` 同样调用结构修正。只读 `audit_gallery_structure.py` 检查四组斜撑两端均进入立柱／顶梁，柱脚与楼板相接；楼梯口附近新增托梁边界净退让 70 mm、斜撑 128.7 mm。此为几何连接检查，不是建筑工程结构计算。源几何统计为 122 个批次、466,848 三角形。
- `ClockworkIndirectLightingBuilder.Bake()`（菜单 `Tools/Music MIDI Player/Scene Art/Bake Clockwork Indirect Fill (No Added Lights)`）仅接受非播放的钟楼第二场景，先保存内存备份，再准备 UV、探针、LightingSettings 并启动 Progressive CPU 烘焙。沿用原 `ClockworkLighting.lighting`，12 texels/m、1024 图集、NonDirectional、三次反弹；只有静态建筑表面接收光照贴图，小型道具、移动机械和乐器使用探针。175 个探针覆盖地面、回廊及高处机械区。
- `HeritageSurface` 增加 `_BakedCeilingFill`：仅在 Meta Pass 中，向下的高处屋顶内衬表面提供中性间接光。正常运行 Pass 不显示这一烘焙发光；原昼夜材质补偿保持。场景没有新增或隐藏的 Light，唯一日月 Point Light 仍为 Realtime，不参与烘焙。固定烘焙补光提供暗部底色，实时昼夜光继续负责变化的直接光与阴影；烘焙图不会逐时重算。
- 后续修改几何、UV、材质或重装精修后，需重新执行上述烘焙入口；只导出 FBX 或保留旧光照贴图不会自动更新间接光。不要将移动节点设为 ContributeGI，也不要把场景覆盖 Apply 到分发 Prefab。

结构源检查、模型更新与本轮 Editor 检查记录位于 `Logs/ClockworkStructureBake/`，实际 Unity 相机图位于 `Captures/ClockworkStructureBake/`；开始前场景内存、Blender 源和 FBX 备份位于 `RecoveryBackups/ClockworkStructureBake-20261104/`。本轮未执行 Play Mode、世界构建、上传或真实 VRChat 客户端验收，新增光照资产大小不代表世界包下载增量。

最终烘焙已完成并保存：19 个建筑 Renderer 使用三张光照贴图（1024² × 2、512² × 1），175 个探针已写入 LightingData；三张 EXR 与 LightingData 源文件合计 **3,969,875 字节**，Editor 贴图内存合计 **6,293,656 字节**，均不是压缩世界包体积。最终 Console 无错误、无光照 UV 重叠警告，仅保留原 `customReflection` 过时 API 警告。场景包含未启用对象仍只有一个启用的 Realtime Point Light，缺失脚本及 Shader 错误均为 0。已检查正午、午夜、墙框架与楼板下支撑的实际相机图，并临时关闭唯一点光源检查固定烘焙补光（`09-baked-fill-light-disabled.png`）；交付前恢复光源及当前客户端本地时间。主场景、两份分发 Prefab 前后 SHA256 一致。

## 2026-11-04 演奏区布局预览，烘焙等待用户审阅

用户要求调整布局和光线、禁止新增摆件，随后明确要求先看布局再烘焙。本轮只完成并保存布局；没有调整日月光参数、Shader、烘焙设置或启动新的光照烘焙，截图仍使用上一轮照明。后续光线调整与重新烘焙尚待布局审阅后继续，不将旧光照图计为本轮烘焙结果。

- 现有 512 组乐器配置整体向入口平移 2.3 m，中心改为 `(0, 0, -1.8)`，保留两种钢琴外观、组合排序、朝向、落地和 0.65 m 乐器占地净距。
- 原地毯中心移到 `(0, 0, -1.8)`，X/Z 缩放为 `0.80 / 0.84`；主体约 `7.36 × 5.12 m`，原金边、流苏、纹样覆盖同步移动缩放。两张原有工作凳移到 X `±2.0`、Z `-5.5`，不新增凳子、物件或几何。控制台及原支架沿 Z 前移 0.85 m，操作 UI 当前中心为 `(-3.8, 1.38, -5.7)`，覆盖前述 UI 布局表的旧坐标，避免合奏展开时碰到支架。
- 源维护入口为 `ArtSource/ClockworkMaintenanceStreet/arrange_recital_space.py`，保存原始对象矩阵后做绝对变换，重复运行不会叠加位移。常规 `export_unity.py` 已接入；只替换原 FBX 的 5 个既有材质批次，保留其他 287 个 FBX 对象、全部节点 ID、连接和 `.meta`，环境仍为 122 批次、466,848 三角形。
- 场景维护入口为 `ClockworkComfortLayoutBuilder.Apply()`，菜单尾为 `Arrange Clockwork Recital Space (No Bake)`；仅更新既有布局数组和 backing、地毯覆盖以及控制台位置，不启动烘焙。既有美术／UI 维护工具识别当前布局后保留这些位置。

Editor 静态几何检查通过全部 512 配置、3,584 对乐器；各配置距控制台／左凳／右凳的保守占地最小净距分别约 `0.816 / 1.355 / 1.715 m`。场景包含未启用对象仍为一个 Point Light，缺失脚本与 Console 错误为 0。记录在 `Logs/ClockworkComfort/`；主场景、练习场景、两份分发 Prefab 和原 FBX `.meta` 的前后 SHA256 一致。布局图在 `Captures/ClockworkComfort/After/01-entry-layout.png`、`02-overview-layout.png`。本轮未执行 Play Mode、光照烘焙、世界构建、上传或真实 VRChat 客户端验收。

## 2026-11-04 西侧三组收纳柜装配偏移修复

用户指出西侧一排柜子模型错位。三个 `West lane storage bank` 的柜体、台面、抽屉面板、标签框及标签被赋予相同的装配根位置，丢失部件偏移；把手曲线仍保留装配内坐标，导致柜体半埋地下、台面落在地面、面板重叠、把手悬空。

- `ArtSource/ClockworkMaintenanceStreet/repair_west_lane_cabinets.py` 在原对象上恢复三组装配：根锚点仍为 Blender `(-11.75, -3.4 / 4.1 / 10.5, 0)`、Z 旋转 90°。柜体底面为 0，顶面及台面底面为 1.2 m；每柜四列三行，共 36 个独立抽屉面板，标签与把手逐一对齐，把手端部进入面板 4 mm。
- 正常 `export_unity.py` 同样调用 `apply_alignment()`；重复执行不改变对象变换或把手曲线坐标。保留 150 个既有源对象，增量替换 11 区的六个材质 Geometry，其他 286 个 FBX 对象、节点 ID、连接、材质、模型 `.meta` 与 GUID 保持。环境仍为 122 个网格、466,848 三角形。
- 修改前保存当前未保存场景的内存副本及 Blender / FBX 备份，位于 `RecoveryBackups/ClockworkCabinetFix-20261104/`。源几何检查与 FBX 保留证据位于 `Logs/ClockworkCabinetFix/`，Unity 实际场景截图位于 `Captures/ClockworkCabinetFix/`。沿用既有无新增 Light 的间接光烘焙入口更新光照。

三组柜子已在 Unity 场景中核对，间接光重新烘焙完成并保存（三张贴图：1024² × 2、512² × 1）；含未启用对象仍只有一个 Point Light，无缺失脚本，Console 无错误或警告。主场景、两份分发 Prefab、社群板 Prefab 与 FBX `.meta` 的前后哈希均一致。本轮只修复第二场景的柜子装配，不增加运行脚本或新光源，不 Apply 到共享 Prefab。未执行 Play Mode、构建、上传或真实 VRChat 客户端验收。

## 验证边界

首次场景接入检查及楼梯通行抽样位于 `Captures/ClockworkScene/`、`Logs/ClockworkScene/`。初次创建期间主场景有同期文件更新，已保留而未回滚；本次机械升级另取开始前哈希，主场景及两份分发 Prefab 前后均相同。不 Apply 实例覆盖，不改构建场景列表。

本次升级证据位于 `Logs/ClockworkMechanism/` 和 `Captures/ClockworkMechanism/`：

- 最终 FBX 的 9 组时间采样：中心距最大误差约 `7.75e-7 m`，齿相位误差约 `0.000977°`，轮毂中心误差约 `2.38e-7 m`；齿数传动比约束误差为零。
- 摆锤轨迹中心误差约 `1.91e-6 m`；检查午夜、完整机械周期边界及三组指针时间，指针实际 Transform 误差小于 `0.000008°`。
- 包含未启用对象共 1 个 Point Light，0 个缺失脚本；Udon 程序接线与 `None` 同步有效。室内六向射线均命中闭合碰撞结构。
- 实际 Unity Editor Play Mode 中从 Udon VM 连续采样 6 次，钟摆、主齿轮、秒针均运动；时钟与本地系统时间的最大采样误差约 **0.019 s**。此轮控制台无错误。记录见 `playmode.json`。
- 日景、月景为实际 Unity 相机渲染；交付前回到非播放状态，并恢复当前客户端时间。

未执行世界构建、上传或真实 VRChat 客户端验收；Editor / Udon VM 结果不代表客户端听音、多人同步或性能验收。早期接入曾出现的 MIDI 导入异常和 VRCSDK Control Panel 异常未通过修改无关代码来处理。
