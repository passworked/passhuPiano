# 海底音乐庭院

2026-11-04，用户确认以海底空间替换钟楼作为当前美术方向，并明确要求**新建场景，钟楼保留为历史版本**。这是独立的作者工程美术场景，不替代 `VRCDefaultWorldScene.unity`，也不更改分发 Prefab 或练习系统。

首版场景落地与最终实景核对完成于 2026-11-05（上海时间）。

## 场景与维护入口

- 新场景：`Assets/Scenes/UnderwaterMusicGarden.unity`，标签 `Current-Art-Direction` / `Underwater`。
- 历史场景：`Assets/Scenes/ClockworkMaintenanceStreet.unity`，标签 `Historical` / `Clockwork-2026-11-04`。保留原场景内容与 GUID，仅修改标签元数据。
- 主视觉：`ArtSource/UnderwaterMusicGarden/Concepts/main-visual-v1.png`；生成提示词与布局说明保存在同目录。这张图是概念图，不是 Unity 实景，也不是运行时背景。
- 创建器：`MusicMidiPlayer.Editor.SceneArt.UnderwaterMusicGardenBuilder.Create()`，菜单 `Tools/Music MIDI Player/Scene Art/Create Underwater Music Garden`。只接受已保存、非播放、无其他未保存场景的钟楼源，拒绝覆盖已有海底场景。
- 环境维护：`RebuildEnvironment()`，菜单 `Rebuild Underwater Environment Only`。只在海底场景内执行，先保留内存副本，再重建自有 `Environment`、材质、碰撞和面板支架／位置；不重建播放器、曲库、组合坐标或共享 Prefab。自定义环境与面板位置修改前应保留自己的版本。
- 环境模型的可编辑源为上述 Editor 创建器，不新增 Blender 或第三方模型依赖。生成网格与材质位于 `Assets/Scenes/UnderwaterMusicGarden/`，重复维护沿用已有资产 GUID。
- 更新已有网格时显式重新设置顶点、法线、UV、颜色和索引并上传缓冲；不只依靠 `CopySerialized`，避免 CPU 数据已更新但 Editor 仍显示旧 GPU 几何。环境与八乐器布局实景见 `14-final-refreshed.png` / `15-final-ensemble.png`；这两张保留替换鱼群前的环境状态，后续网格鱼见 `16-seven-mesh-species.png` / `17-playing-mesh-fish.png` / `18-playing-floor-seal.png`。早期图保留为过程记录。

## 蓝色 UI 与控制台间隙（2026-11-05）

按用户最新要求，海底场景全部 UI 恢复播放器预制体的 VizVid 风格：蓝灰面板 `#37424F`、深蓝灰卡片 `#25313E`、青蓝强调 `#87E2E5`、白色正文与蓝灰次级文字。主面板、未展开的乐器／语言／设置／文件映射弹层、借物表、作者／赞助／社群板、输入框及全部 Selectable 状态一起更新。能对应预制体路径的控件直接读取原配色，后加控件按同一色板恢复；不重建控件或改变回调。社群图像使用海底场景独立 `CommunitySocialPalette.mat`，语言管理器的 proxy／backing 颜色同步，历史钟楼材质与共享预制体保持。

控制台支柱原顶端穿出面板约12 mm；完整主 Canvas 沿正面法线前移约37.2 mm，触发器与点击区域一同移动，支柱与面板最小净距为25 mm。其他 Canvas 位置、朝向及缩放保持。维护入口 `UnderwaterUIThemeBuilder.Install()`／菜单 `Apply Underwater Blue UI Theme`；完整环境重建也应用相同主题和间隙规则，不累积偏移。

Editor 检查覆盖3,787个 Graphic 与818个 Selectable，旧绿金主色残留0；19处主控件／弹层 GraphicRaycaster 命中通过。1,060项 UI 对象引用、755个 Button 的持久回调、571首曲目 ID 与 catalogHash 保持。305项受保护文件哈希不变，包括原场景光照、环境网格、历史钟楼材质及三份共享预制体。截图见 `Captures/UnderwaterMusicGarden/BlueUI/`，记录见 `Logs/UnderwaterBlueUI/`，备份见 `RecoveryBackups/UnderwaterBlueUI-20261105/`。本次没有重烘焙、Play Mode、构建或真实VRChat客户端验收。

## 穹顶肋架接缝修复（2026-11-05）

`BronzeDomeRibs` 改为共享连续截面及平滑法线，消除原逐段管道端口不重合、截面参考轴切换造成的裂隙与明暗跳变。十二条半肋合为六条贯穿穹顶的完整拱肋，顶部不再叠加端盖；两道横环的首尾截面精确闭合。原半径、截面粗细、材质、对象引用与网格 GUID 保持，通用 `Geometry.Tube()` 及其他环境网格不变。

维护入口为 `UnderwaterMusicGardenBuilder.UpdateDomeRibs()`／菜单 `Update Underwater Dome Ribs Only`，只更新原网格资产；完整环境重建共用相同生成方法。Editor 几何检查为17,760三角面、开放边0、非流形边0、退化三角形0；近景和室内前后图见 `Captures/UnderwaterMusicGarden/DomeSeams/`，记录见 `Logs/UnderwaterDomeSeams/`，修复前副本在 `RecoveryBackups/UnderwaterDomeSeams-20261105/`。254项文件哈希核对确认场景、原光照与其他海底资源保持。肋架继续接收Light Probes，本次没有重烘焙、Play Mode、构建或真实VRChat客户端验收。

## 空间与表现

穹顶室内为干燥空间，地面净跨草案约 24 m；稀疏黄铜肋架、连续透明玻璃和 0.8 m 周边矮墙围合。宽椭圆演奏区以双道细黄铜嵌线标识；2026-11-05 后续按用户最新要求改为50 cm整体舞台，前方入口缓坡删除，改为左右两侧楼梯，所有乐器共用同一台面。听众／自由活动区、弧形座席和中央入口位于演奏区前方，控制台位于前侧左方；Credits 和作者／社群／赞助板在入口两侧，保留原交互及内容。

外部海床、分层礁石、沉没拱门、稀疏珊瑚与海草组成远景。2026-11-05 按用户要求移除常驻低模鱼群、鳐鱼，改为演奏生成的七种 3D 网格粒子鱼，生命周期与游动使用音乐时钟，详见下节。焦散是表面着色近似，光束是少量透明网格，不是流体、折射或真实体积散射模拟。玻璃单层透明，无 GrabPass、深度纹理或额外运行相机。

场景保留且仅保留一个 Point Light，包括未启用对象；2026-11-05 本轮改为 Mixed / Baked Indirect，保留实时直接光与阴影，并单独烘焙海底环境的间接光。不沿用钟楼的本地时钟、日月控制器、天空、机械或旧光照贴图；当前没有昼夜循环。环境材质与普通乐器表面使用海底场景专属材质副本，部分纹理只读复用原有石材资源；本轮琴键发光使用场景专属按键材质，详见下节。

## 空间层次调整（2026-11-05）

针对海床布景高度接近、前后景缺少遮挡关系的问题，新增独立的 `Environment/DepthScenery`：约19 m处的近景礁石、珊瑚扇与海草，25～48 m处错落礁脊，72～104 m处远景轮廓，以及覆盖原海床边缘的远海延展。中央演奏区与遗迹拱门视线保留，未移动乐器、面板、座席或既有环境网格。

新增外景与原有海床／礁石／遗迹共用随相机距离淡入水色的材质表现；Surface Shader 默认关闭该项，仅场景外景材质显式启用，ForwardAdd 只衰减附加光，避免重复叠加水色。场景雾密度改为0.019，室内石材与海床焦散分别降至0.065／0.055，减少均匀纹理对主体的干扰。

维护入口：`UnderwaterMusicGardenBuilder.UpdateDepthScenery()`，菜单 `Update Underwater Depth Scenery Only`，可编辑源码为 `UnderwaterDepthScenery.cs`。只更新自有外景组、对应网格和材质；新资产后续沿用GUID。完整环境重建与外海升级也保留此层次，维护不额外重置乐器布局。新增六个Renderer、34,544三角面，无Collider、Light、运行脚本或网络字段；使用现有探针且不参与Contribute GI，远海延展与远礁关闭投影，保留室内既有烘焙。此次没有重烘焙；原有参与GI的几何或光照变化仍须按上文重新烘焙。

已保存场景并完成Editor实景前后对比，截图为 `Captures/UnderwaterMusicGarden/Revision/*-depth-before.png` / `*-depth-final.png`。`Logs/UnderwaterDepth/validation.json` 记录512种布局、3,072对净距检查，最小约0.65 m，唯一Point Light、缺失脚本0、Shader错误0；仍有两组光照图与68个探针。`protected.json` 确认主场景、钟楼、练习场景、两份分发Prefab、原LightingData、颜色光照图和反射探针文件哈希未变。修改前文件在 `RecoveryBackups/UnderwaterDepth-20261105/`。本轮未进入Play Mode、构建、上传或进行真实VRChat／VR双眼验收。

## 室内整体低台（2026-11-05）

### 当前高度：50 cm

用户后续明确要求将舞台从18 cm升到50 cm，并删除前方入口缓坡，改成两侧楼梯；以下首版18 cm数据保留为历史记录。当前台面顶高0.50 m，保留浅暖米色色值、平坦椭圆占地和既有GUID；正面恢复连续收边，左右两侧各一组三等高踏步。每级高约16.7 cm、踏面进深32 cm、总进深96 cm，梯宽约1.55 m，沿原椭圆侧边衔接；踏步与舞台共用一个网格和MeshCollider。既有黄铜嵌线底部定位到0.512 m，保持台面上方12 mm净距。

512组布局整体再抬高0.32 m，全部乐器和座椅随根对象抬高，琴上八音盒相对承托关系不变；法阵继续按布局中心计算地面高度。`Logs/UnderwaterStage50/geometry.json`记录当前512种组合、最小约0.65 m净距、唯一Point Light、缺失脚本0；两侧踏步射线碰撞均为0.50／0.333／0.167 m，正面旧入口位置不再命中舞台碰撞体。更新前场景、源文件、网格与光照备份位于 `RecoveryBackups/UnderwaterStage50-20261105/`，改为两侧楼梯前另保存在 `RecoveryBackups/UnderwaterStage50SideStairs-20261105/`。

左右楼梯最终网格1,336三角面，沿用舞台网格GUID；最终间接光烘焙完成，台面接入光照图0，仍为两组方向光照图与68个探针，见同目录 `lighting.json`／`final.json`。同期植物更新触发过重新编译并中断一次烘焙，后续海草UV修复后完成最终烘焙；保留这些并行修改。50 cm的Play Mode复核又被同期编辑器修改打断，记录为 `playback-status.json` 的未完成项，不将首版18 cm的VM结果当作本版验证。未执行世界构建、上传或真实VRChat／VR双眼测试。最终图为 `Captures/UnderwaterMusicGarden/Revision/stage50-side-stairs-final.png`。

### 首版18 cm记录

用户确认将演奏区整体抬高15～20 cm，本轮采用18 cm。`Environment/PerformanceStage`为独立的浅暖米色石材椭圆台面，最终色值(0.71,0.64,0.53)，略深于周围地面；平面半轴9.95／4.75 m，中心仍为(0,2.5)。边缘圆缓收口，前方中央通过约1.9 m进深的宽缓坡接到原地面，包含同形MeshCollider。既有双道黄铜嵌线抬到台面上，保留原网格GUID与Renderer；控制台、信息板、观众座席与出生点保留。

维护源码为 `UnderwaterPerformanceStage.cs`，入口 `UnderwaterMusicGardenBuilder.UpdatePerformanceStage()`／菜单 `Update Underwater Performance Stage Only`。重复执行以布局center.y为基准计算差值，不叠加抬高；完整环境重建同样保留低台。全部512组布局的y坐标与当前乐器根对象同步抬高，x/z、朝向、间距、琴上八音盒相对承托高度保持。`UnderwaterMusicLink`运行时及海洋表现安装器均从布局中心计算世界地面高度，法阵在台面上方28 mm显示，不再返回原地面。

新增单个Renderer、1,152三角面和一个MeshCollider；台面参与Contribute GI并使用独立UV2。最终浅色材质的间接光重烘焙已完成，台面接入光照图0，场景仍为两组方向光照图、68个探针和唯一Point Light，见 `Logs/UnderwaterStage/lighting.json`。几何检查见同目录 `geometry.json`：512种组合全部落在平坦台面内，归一化椭圆占用最大约0.596；最小乐器净距仍约0.65 m。实测坡面碰撞高度从约0.123／0.064／0.006 m连续降至原地面。备份位于 `RecoveryBackups/UnderwaterStage-20261105/`。

ClientSim环境中的实际backing Udon VM已检查12次初始／切换状态，包括两种钢琴、琴上／独立桌八音盒、全八件组合；落地高度误差为0，相关VM无错误，记录在 `switching.json`。实际播放Testify至23.706 s，钢琴法阵高度0.2082 m，与台面加28 mm间隔及索引偏移一致；停止清空法阵，见 `playback.json`。本轮没有世界构建、上传或真实VRChat／VR双眼验收。

最终实景图为 `Captures/UnderwaterMusicGarden/Revision/*-stage-light-final.png`；`*-stage-playing.png`保留调浅前的运行法阵高度证据。`protected.json`确认主场景、历史钟楼、练习场景与两份分发Prefab文件哈希保持。Console中保留SDK RemoteConfig及MCP重连的初始化日志，未观察到舞台相关编译／Udon VM错误；未据此声称整个Editor Console没有错误。

## 十二等分中空法阵与分阶段展开（2026-11-05）

根据用户对对称性、纹样与中心星芒的反馈，使用内置 `image_gen.imagegen` 重新生成环形设计参考，再由 `UnderwaterGroundSealBuilder` 重描为条带网格。当前参考为 `ArtSource/UnderwaterMusicGarden/Seal-v2/ring-study-v3.png`，完整提示词保存在同名 `.prompt.txt`；早先两版只作为被否决的过程资料保留。参考图不作为运行时贴图。

当前造型为留白双内环、十二段交织弧带、十二等分外圈骨架、统一的弯月／圆点／箭纹符印及96格刻度封环。所有扇区从相同几何旋转复制，保证运行时十二重旋转对称；不靠生成图的手绘误差决定对称。原 `Performance/GroundSeal.asset` 的 GUID 和九处 Renderer 引用保留，历史钟楼法阵不变。单份网格25,200顶点／12,600三角面，依然每法阵一个 Renderer，无贴图、额外 Light、粒子系统或网络字段。

每次从完全隐藏状态起音后，按音乐时钟在2.8秒内依次展开五阶段：

| 起音后时间 | 新展开部分 |
| --- | --- |
| 0～0.56 s | 留白中心的双内环 |
| 0.56～1.12 s | 交织弧带、菱形节点与圆点 |
| 1.12～1.68 s | 外圈双边界及等分骨架 |
| 1.68～2.24 s | 十二组相同符纹 |
| 2.24～2.8 s | 外缘刻度、闭合圈与定位方框 |

Shader 的 UV2 保存阶段号及该阶段内的逐笔顺序；同一顺序在十二扇区同步执行，已经完成的阶段持续保留。整体透明度与展开进度分离，短音也至少保留至首次起音后3.4秒，连续音只延长停留、不重启展开；完全淡出后再次演奏才重走五阶段。暂停冻结、恢复继续，停止／切歌／定位沿用清理行为；固定通道0明色与钢琴／八音盒共用法阵规则保留。

维护入口为 `UnderwaterGroundSealBuilder.UpdateSeal()`／菜单 `Update Underwater Ground Seal Only`，只更新当前海底法阵及其材质。完整海洋表现安装器也使用同一网格创建器，后续维护不会再复制旧钟楼法阵。`UnderwaterSealReview.CaptureStages()` 生成五阶段实际 Shader 渲染；`StartPlaybackReview()` 使用实际 Testify backing Udon VM 检查展开、连续音不重启、暂停与停止。证据目录为 `Logs/UnderwaterSealV2/` 和 `Captures/UnderwaterMusicGarden/SealV2/`。原场景、网格、材质、Shader 与脚本副本在 `RecoveryBackups/UnderwaterSeal-v2-20261105/`。光照贴图不受此次法阵更新影响，未重烘焙或构建世界。

本轮已完成实际 Shader 五阶段图与 `unfolding.gif` 预览。几何十二重旋转最大误差约1.58×10⁻⁷，中心留白半径0.3937（单位半径）；原网格GUID保持。Testify实际backing Udon VM中，首次起音0.018 s，五次采样进度0.190／0.389／0.594／0.792／1.0，约2.834 s完整展开；暂停冻结、密集音符不重启、停止清理均通过，VM无错误。主场景、历史钟楼、练习场景以及原LightingData和两张颜色光照图哈希未变，记录在 `Logs/UnderwaterSealV2/protected.json`。未进行真实VRChat客户端或VR双眼验收。

## 音符奇偶双色更新（2026-11-05）

根据后续澄清，文件音符限制为显示通道奇偶两种色调，使用当前主题下相差50.4°的邻近色调，仍保留每种色调的黑白键深浅。单通道多轨的音轨色槽回退保留，Testify两轨为两种色调、四种深浅色；琴键、瀑布、粒子、弦乐和鼓组共用，鱼群仅取两组明色。法阵固定第一组、实时演奏者主题及原力度包络保持。验证和实际Shader预览见[第06册](06-waterfall-and-keyboard.md#作者工程文件音符奇偶双色2026-11-05)。本次不重存场景或重烘焙。

## 大陆坡、四色与力度发光调整（2026-11-05）

鱼粒子双色复核（2026-11-05）：当前八个实际粒子系统均关闭生命周期／速度变色，使用统一调色盘通道0／1的明色；起音按显示通道奇偶选择，所有存量鱼每帧复用同一对RGB，淡入淡出仅改变alpha。隔离Editor代理使用这八个系统的克隆，按33 ms步进模拟Testify全曲171.236秒、2,539个音符，累计347,943个可见粒子颜色样本只出现两种RGB，覆盖八种生物；另对八个乐器目标×16通道×黑白键共256次取色检查，也只有两色。证据：`Logs/FishTwoColorAudit/editor.json`。未改变Runtime、材质或场景；这是Editor代理检查，不是backing Udon或真实VRChat验收。

本节取代下方初版的折返路径、窄高度带和逐音符法阵配色规则。

- Testify 钢琴谱实为两条源音轨共用 MIDI 通道 0。`MidiSongData.ColorChannelForNote()` 在全曲只有一个 MIDI 通道且存在多条有音符音轨时，按原始音轨顺序分配显示色槽；多 MIDI 通道的曲目继续使用原始通道。黑／白键保留深浅，因此两轨得到四色。回退只影响表现，不修改 MIDI、压缩数据、通道、曲目 ID、鼓路由、音轨映射或同步字段；解码数据释放时一起失效。
- 法阵固定使用当前歌曲／演奏者调色盘的通道 0 明色，统一以白键音高查询，黑键、音轨和乐器不再改变法阵色。琴上八音盒仍共用钢琴法阵。
- 鱼群只使用调色盘通道 0／1 的两种明色，显示通道按奇偶分组；实时起音交替分组。现存鱼同步使用同一对调色盘，透明度仅表示淡入淡出，不增加第三种 RGB 色。
- 生物仍交替从乐器和外围生成，但外围生物不再游向乐器。源头生物在约 8～10.5 个音乐秒抵达外部水域后，外围生成者从出现起，均绕行并持续向外移动；寿命末端淡出，不折返。
- 黄金角方位与独立高度序列覆盖整个上半球，基础椭球半径16～24 m、高度分量0.04～0.96，纵轴比例0.8；不再按物种将鱼限制在低空水平带。大型鱼是否缩小按完整椭球距离判断，顶部不会被误判成室内。容量72及各物种、发射与30 Hz预算不变。
- 琴键和 GPU Note 使用相同的 Shader 包络：`velocity^1.35` 决定峰值，25 ms smoothstep 从零上升，之后按280 ms半衰期衰减，1～1.4 s平滑收尾。文件使用音乐时钟，实时使用本地时间；停止立即清理。琴键松开后允许发光尾部继续，机械抬键和延音状态不变，不新增 Light、Bloom 或逐音符 Update。
- `UnderwaterRevisionReview.ApplyPresentation()` 仅在当前海底场景接入按键材质，并把原本穿入地面的细黄铜嵌线抬离地面，底部净距12 mm；重复维护不继续叠加位移。整圈 Arc 创建器不再生成重合的首尾封面；同朝向重复三角面检查保留反面植物几何。不把静态检查写成所有客户端零闪烁保证。
- `UnderwaterMusicGardenBuilder.UpgradeOceanAndLighting()` 只更新外围地形、承托平台、外海布景与光照，保留室内布局、播放器和面板。大陆坡随 z、x 高程变化，礁石、珊瑚和拱门随地面落位；穹顶底部承托在半径14.4 m的混凝土人工平台上，附16道放射支撑。
- 唯一 Point Light 使用4096自定义软阴影、强度0.8、bias 0.012、normal bias 0.035；当前 Editor 高质量档维持 Very High，Point Light 另显式指定4096分辨率。主要环境网格生成加大间距的第二套 UV 并标记 Contribute GI；20 texels/m、2048 atlas、64直接／256间接／128环境采样、3次反弹，方向光照图，使用 Progressive CPU。细肋架、嵌线、珊瑚与双面植物使用探针间接光；68个探针覆盖室内，普通乐器表面同时接入。重建环境或修改几何后须重新烘焙；场景保存不等于完成烘焙。

本轮证据目录：`Logs/UnderwaterRevision/`、`Captures/UnderwaterMusicGarden/Revision/`；修改前副本为 `RecoveryBackups/UnderwaterRevision-20261105/`。`colors-and-glow.json` 记录 Testify 的原始通道／音轨、四色和三档力度的实际 HDR 像素曲线，`note-glow-render.json` 补充普通／钢琴 GPU Note 的实际发光增量曲线。

最终烘焙完成，`lighting.json` 记录两组方向光照图、独立 LightingData 和68个探针；第二轮加大 UV 间距并改用探针后，没有再出现 UV 重叠警告。

实际 ClientSim 环境中的 backing Udon VM 播放 Testify 至30.0386 s：72条鱼，八方位分布9／8／10／8／12／7／9／9，低于5 m／5～10 m／高于10 m各17／23／32条；粒子RGB只有两种。播放前30秒累计出现四种瀑布颜色（暂停瞬间该3秒窗口只有三种，按当时实际音符检查，不要求每帧强行出现四色），活动法阵只有通道0明色。暂停姿态冻结、恢复推进，35条同一槽位外围鱼径向距离均增加，停止清空；Facade／Clock／海洋效果VM无错误。记录见 `playback.json`、`transport.json`。

最终场景验证仍为512种组合、3,072对乐器净距、最小约0.65 m、缺失脚本0、最多八件、唯一Point Light。主场景、历史钟楼与练习场景文件哈希保持一致。近景另对比关闭阴影／隐藏琴弦的图，确认细琴弦的高频点线属于几何走样，未据此改动源乐器模型；地面嵌线的实际穿插已修正，未声称真实客户端所有距离零闪烁。未执行世界构建、上传或真实 VRChat 客户端验收。

## 演奏法阵与点线海洋生物（2026-11-05）

用户最新要求：七种鱼加水母，向四面八方游动；既从乐器向外，也从外海淡入、游向室内再回到外海；约 30 秒后四周形成分布。鱼只呈现音符染色的点、线与很薄的透明膜，不使用鱼类贴图，不保留真实物种配色。

`UnderwaterMarineMeshFactory` 生成七种鱼形轮廓（小鱼、小丑鱼、蓝吊鱼、蝠鲼、礁鲨、鲸鲨、蓝鲸）和水母。按用户进一步反馈，鱼身降为 8 个纵向分段 × 6 个周向分段，鱼鳍只保留实体三角轮廓，眼睛去掉细部；水母为 4 × 8 伞面与六条简化触须。最终普通鱼 130～136 个三角面、蝠鲼 256 面、水母 200 面，相比上一版再减少约 77%～86%；八类网格三个 GPU 实例的对照为 `FishFix/coarse-gpu.png`。所有三角形带独立重心 UV，通过顶点／片元 Shader 绘制屏幕空间抗锯齿线和顶点，不使用几何 Shader、贴图、噪点裁剪、发光或新增光源；透明膜默认 alpha 0.035。灰度顶点只表达明暗，完整色相来自实际音符原有的文件通道／实时音符配色。

`UnderwaterPerformanceBuilder.Install()` 仅维护当前海底场景，先备份内存场景，保留已有网格 GUID，接线独立本地 `UnderwaterMusicLink`；不 Apply Prefab。`MarinePerformance` 位于 `Environment` 外，环境重建不删除效果，也不再生成旧常驻鱼群与鳐鱼。未接入早先生成的图集。

每次实际音符攻击激活对应地面法阵；法阵跟随乐器占地中心移动，琴上的八音盒共用钢琴法阵。原击键、键盘瀑布、弦动作保留，本场景旧击键／符号／弦粒子禁用，不更改源 Prefab。未启用或静音目标不生成新鱼。

新生物按黄金角分散到 360°，交替使用向外、向内路线：向外者约 8～10.5 个音乐秒到达半径 20～32 m 水域后环游；向内者在四周用 3.2 秒淡入，短暂环游后靠近乐器，再返回外海继续环游。寿命约 48～79 个音乐秒，不在刚到外海时立即消失。大型鱼在半径 12.8 m 内缩小，离开后展开；水母有独立伞体收缩和触须摆动。音乐暂停冻结路径、朝向、尺寸、寿命和 Shader；恢复继续，停止／跳转／切歌清理。实时演奏用连续本地时间，panic 清理。

全局同时最多 72 条，八种上限为 24／16／14／6／5／3／1／10。保留每来源 0.14 秒、全局 0.09 秒发射节流；合并攻击仍激活法阵。复用数组以 30 Hz 更新，八个 Mesh ParticleSystem 关闭自动发射、碰撞、灯光、拖尾，按距离排序透明实例。不新增网络字段、AudioSource 或运行时几何生成。

2026-11-05 按后续反馈继续柔化点线：将窄范围 `smoothstep` 描边改为连续 Gaussian alpha 衰减，线羽化 `_LineSoftness=1.4` 像素、节点 `_PointSoftness=1.7` 像素；线／节点峰值 alpha 降至 0.42／0.62，保留既有粗网格、音符色相和薄膜。没有增加贴图、灯光或后处理。八类各三个 GPU 实例的 Editor 预览已检查，Shader 无错误；对照保存在 `FishFix/soft-edge-before.png` 与 `FishFix/soft-edge-after.png`。本次仅修改材质着色，未重跑播放生命周期测试，也未做真实 VRChat 客户端验收。

### 错位与闪烁修复

多实例实景复现发现，原 Renderer 未启用 `AnimFrame` 流，但 Shader 使用 Unity 的 `DefaultParticleInstanceData`（transform + packed color + animFrame）。第二条起的实例矩阵按错误步长读取，因此 CPU 粒子数量／坐标检查正确，实际渲染仍会错位、在中央闪动。显式补齐 `AnimFrame`，重心线框补齐 `UV2`，并验证维护器不再生成错误布局。移除先前屏幕点阵渐隐，改普通 alpha 混合；非 GPU 路径不再按局部坐标执行鳍形变，避免世界坐标被当作模型坐标放大。

另修正鱼尾极点：浮点 `sin(π)` 可能略为负数，直接求非整数幂会得到 NaN；先夹到非负值，再生成几何，验证所有顶点有限。更新已有网格显式上传 CPU／GPU 缓冲并保留 GUID。

初版验证保留于 `Logs/UnderwaterPerformance/`，它检查过暂停、恢复、UI 跳转、停止和七种实时入口，但**不代表旧多实例渲染没有问题**。本次修复记录与 GPU 多实例对照另存 `Logs/UnderwaterFishFix/`、`Captures/UnderwaterMusicGarden/FishFix/`。Editor 检查不能替代 VRChat 客户端、VR 双眼或多人验收。

本轮最终 Editor Udon VM 记录：实际歌曲播放至 30.04 s，共 72 条（八种数量 24／16／14／6／3／3／0／6），八个方向均非空，分区为 10／10／8／11／7／8／6／12；向内／向外各 36，39 条已处于外海环游阶段。暂停保留全部姿态／颜色／尺寸／寿命，恢复推进，停止清理；Facade／海洋效果 VM 均无错误。该次采样未随机生成蓝鲸，但八类各三个 GPU 实例的独立实景检查完整显示；不据此宣称每首歌前 30 秒必定出现全部物种。`UnderwaterPerformanceReview` 是 Editor 维护工具，提供 `CaptureModels()` 与 `StartThirtySecondReview()`，用于复查多实例 GPU 渲染与实际歌曲时间线。最终网格预览为 `FishFix/coarse-gpu.png`，四向实景为 `FishFix/30s-surroundings.png`。记录位于 `Logs/UnderwaterFishFix/installation.json`、`thirty-seconds.json` 和 `transport.json`。

## 水母动作与粒子尺寸更新（2026-11-05）

本节取代前文的大型鱼室内缩小／外海展开尺寸规则。八类生物生成时统一取 `0.8–1.2` 的粒子缩放，每条存活期间保持固定，不再按物种或室内外距离改变大小；这表示网格缩放值，不保证不同形状的外包围盒都为相同米数。

水母不再沿用鱼类的前向朝向，改为伞顶朝上并向路径轻微倾斜。每个音乐时钟周期为2.6秒，前28%快速收伞、后72%缓慢舒展；沿原路径的推进速度配合收伞冲程变化，触须从根部至末端延迟摆动，根部保留连接。每条水母以固定缩放推导独立相位，CPU路径与GPU形变共用节奏，不增加粒子顶点流、网络字段、网格、灯光或相机。暂停沿用音乐时钟冻结，停止清理。

三实例GPU实渲染40帧保存在 `Captures/UnderwaterMusicGarden/JellyMotion/`，预览为 `jelly-motion.gif`；UdonSharp与Shader编译无错误。实际Editor backing Udon VM播放Testify至22.636秒，66个粒子缩放范围为0.805263–1.196748，其中5只水母最大倾角21.27°，Facade／Clock／海洋效果VM均无错误。记录见 `Logs/UnderwaterJellyMotion/playback.json`；暂停／恢复／停止检查另见同目录 `transport.json`。本次不重存场景、修改网格或烘焙光照，未构建世界或做真实VRChat客户端验收。

## 周边绿植细化（2026-11-05）

室内四盆在原花盆位置上增加弯曲阔叶、中央叶脉、叶柄、低层蕨叶和内凹土面；年轻叶与外层成熟叶形成不同高度和伸展方向。外海海草改为36簇疏密错落的细叶，八处近礁海带使用弯曲主茎与交替侧叶，保留遗迹和远海的开阔视线。叶面使用连续法线、轻微拱面与顶点色渐变，正反面均有几何；不增加贴图、透明裁剪、动画、运行脚本或Light。

维护源码为 `UnderwaterVegetation.cs`，入口 `UnderwaterMusicGardenBuilder.UpdateVegetation()`／菜单 `Update Underwater Vegetation Only`。单独更新沿用 `InteriorFoliage`、`SeagrassPatches`、`NearKelpSilhouettes` 三个场景对象、Renderer和网格GUID；完整环境／外海／景深维护也调用同一生成器。室内使用独立 `InteriorBotanicalGreen` 材质，海草和海带沿用原材质，未移动花盆、礁石或乐器。

三组网格分别为24,432／12,960／11,200三角面，共48,592面，相比本轮前2,464面增加46,128面，新增Renderer为0。网格更新清除旧顶点布局，单独维护显式写入有效UV2，避免顶点数变化后Progressive烘焙读取到旧UV流；曲面法线在叶片局部坐标内计算，避免远处小叶尖的浮点精度损失。三组均无非有限顶点、无效法线、UV或退化三角形，室内全部顶点位于穹顶内，唯一Point Light、缺失脚本0、Shader错误0，见 `Logs/UnderwaterVegetation/geometry.json`。植物维持原有探针间接光设置。

四向同机位实际Editor渲染保存在 `Captures/UnderwaterMusicGarden/Vegetation/*-before.png`／`*-after.png`，烘焙后为 `*-final.png`。修改前场景、源文件与三组网格在 `RecoveryBackups/UnderwaterVegetation-20261105/`。光照已完成重烘焙（约151.46秒），仍为两组方向光照图和68个探针，结果见 `Logs/UnderwaterVegetation/lighting.json`。保留同期舞台修改；最终截图拍摄时其他工作已启动ClientSim，不作为本次植物播放验收。未构建、上传或进行真实VRChat／VR双眼验收。

## 全场景 3D 声音覆盖（2026-11-05）

按用户反馈修正台下听不见：海底场景共用64个AudioSource的 `VRCSpatialAudioSource.Near` 与 `AudioSource.minDistance` 从5 m增至30 m，`Far`／`maxDistance` 保持80 m。完整音量区覆盖舞台、台下听众区和入口，保留3D空间化、原Gain、本地音量和2D切换。`MidiPlayerBuilder.ConfigureVoicePool` 仅在缺少空间音频组件时初始化默认距离，保留已有场景手工配置，避免重新配置时恢复为5 m。

ClientSim运行检查确认全部64源的Near=30、Far=80、spatialBlend=1、spatialize=true，实际距离曲线在5／15／25／30 m的增益均为1；运行时SDK将最大距离设为80.001 m。512组预烘焙布局的乐器根到室内地面包围盒角点最远约23.44 m，此项为布局范围检查，不是听音验收。验证记录位于 `Logs/UnderwaterAudioCoverage/`，修改前磁盘与Editor内存场景副本位于 `RecoveryBackups/UnderwaterAudioCoverage-20261105/`。主场景、历史钟楼、练习场景与两份分发Prefab哈希保持；无世界构建、上传或真实VRChat客户端听音验收。验证后恢复原有ClientSim播放状态。

## 最多八件与八音盒摆法

本轮按钢琴、马林巴、小提琴、中提琴、大提琴、低音提琴、八音盒、鼓组八件同时启用设计；两种钢琴外观互斥。30 件与未来管乐扩展暂缓，旧容量草案明确标记为已被取代，不作为当前接入规格。

复用现有 `MidiInstrumentFanLayout` 的 256 个选择集合 × 两种钢琴外观，并在新场景预烘焙坐标，组合包络中心为 `(0,0,2.5)`，相邻地面占地净距 0.65 m。启用集合决定布局，不按点击顺序改变。没有运行时几何搜索或布局 Update。

- **有钢琴且有八音盒**：八音盒不占独立地面槽，桌椅隐藏，其他乐器按去掉八音盒后的集合排列。八件共用七个地面位置。
- **三角琴承托**：新增场景内琴盖轴心，将既有琴盖与边条归入该轴心；共存时合盖并隐藏原撑杆，八音盒底脚落在实际合盖表面。仅钢琴时恢复原开盖与撑杆。不改源钢琴网格或 Prefab。
- **数码琴承托**：使用柜体后方顶面。承托高度由既有八音盒脚底与琴面确定，不缩放八音盒。
- **无钢琴时**：八音盒保留独立桌椅与落地坐标，包括独奏或与其他非钢琴乐器组合。

`MidiInstrumentFanLayout.musicBoxSharesPiano` 为场景显式启用项，默认 false；钟楼和其他既有实例继续原来的独立桌策略。没有修改路由 ID、bank、歌曲 ID、曲库、权威或同步字段；依然使用原共享 64 个物理 AudioSource。新场景复制既有播放器默认选择，不强制默认开启全部乐器。

## 验证与边界

Editor 几何验证入口为 `UnderwaterMusicGardenBuilder.Validate()`，证据位于 `Logs/UnderwaterMusicGarden/`；实际相机图位于 `Captures/UnderwaterMusicGarden/`，创建前保护与内存备份位于 `RecoveryBackups/UnderwaterMusicGarden-20261104/`。

已检查 512 个组合、3,072 对地面包络，最小净距约 0.649999 m，包络居中误差小于 0.000001 m；所有选中地面乐器落地。场景含未启用对象只有一个 Point Light，无缺失脚本，曲库哈希与复制源一致。琴上与独立桌两种八音盒摆法已通过实际相机检查；四个独立 Canvas 均在穹顶内，环境退化三角形为零，从室内中心的六向射线均命中玻璃或地面。

实际 Unity Editor Play Mode 中，通过原生 UI 事件进入 backing Udon VM，13 次选择切换、333 项检查通过，覆盖独立八音盒、两种钢琴承托、全八件、取消钢琴后恢复独立桌、琴盖与撑杆、布局和可见性；Facade、UI、布局组件 `HasError=false`。另通过真实 Udon 启动曲目播放，时钟从 −3 s 预备段推进到约 8.52 s，采样时 7 个 AudioSource 正在发声，物理池总数仍为 64，Facade / Clock 无运行错误。记录分别为 `playmode.json` 与 `playback.json`；未进行听音品质验收。

保护哈希对比记录于 `protected-before.json` / `protected-after.json`。钟楼 `.unity` 内容、GUID 及其环境资源保留，元数据仅增加历史标签；主场景和练习场景哈希相同。两份分发 Prefab 在任务期间有同期文件变化，已保留，不能声称其哈希不变；本创建器没有调用 Apply 或保存分发 Prefab，也没有用旧文件覆盖这些变化。

没有执行世界构建、上传或真实 VRChat 客户端验收。网格／源资产大小不等于世界下载体积；没有声称 VR 双眼、多人、客户端帧率或最终下载增量已验收。
