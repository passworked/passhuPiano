# DEMO 日系音乐教室

## 范围与资源

以 [首版概念图](Concepts/japanese-music-classroom-v1.png) 为美术依据，在原主场景
Assets/Scenes/VRCDefaultWorldScene.unity 中增加 DemoMusicClassroom 根节点。
教室约 10.5 × 8.9 m，净高约 3.84 m，包含木拼花地板、木护墙、侧窗和褶皱窗帘、
五线谱黑板、壁钟、谱柜、公告板、课椅、侧走廊与窗外校园景观。

- Blender 源文件：ArtSource/DemoMusicClassroom/JapaneseMusicClassroom.blend。
- Blender 制作脚本：ArtSource/DemoMusicClassroom/build_classroom.py。
- Unity 模型、材质、纹理和碰撞清单：Assets/Scenes/DemoMusicClassroom/。
- Editor 接入工具：Assets/UdonMidiPlayer/Editor/SceneArt/MusicClassroomSceneBuilder.cs。
- 建模软件：Steam 安装的 Blender 5.2.3 LTS。

.blend 保留分区集合、单独构件、倒角及布料厚度修改器；FBX 导出时才应用修改器并按
区域与材质合并。源文件放在 Assets 外，避免 Unity 自动调用 Blender 导入两套模型。
模型不包含替代钢琴、控制 UI 或演奏座椅。

## 现有功能保护

保留原 UdonMidiPlayer 层级、Transform、组件、序列化引用、曲库、音源、乐器、
座椅与播放器 Prefab 覆盖；不更新分发 Prefab，不改同步或播放逻辑，不增加多乐器合奏支持。
现有其他乐器对象也不因本次美术任务删除或重接线。

教室围绕原播放器坐标建模：琴位在原点，控制面板保留在左侧。出生点和玩家功能不移动。
原 Floor 对象及 Collider 保留，仅隐藏原地面 Renderer，表面改由新木地板呈现。
新增碰撞器只负责建筑边界和家具。两处侧门保留约 1.14 m 的通行净宽，
走廊末端和外窗有边界碰撞。

仅调整原主光、主相机和场景环境光/天空以配合午后采光；不改播放器内部灯光和反射引用。
ClassroomReflection 是独立 128 px 环境反射，不调用会更新分发 Prefab 的播放器反射工具。
新增教室有 47 个参与 GI 的美术网格和 60 个光照探针；原播放器有 0 个参与 GI 的 Renderer。
教室间接光烘焙已完成，生成 1 张光照图，同次烘焙前后原播放器序列化摘要保持一致。
光照配置保存在 ClassroomLighting.lighting，未启用自动烘焙。

## 继续制作

1. 优先直接编辑 .blend 中对应分区。尺寸以米为单位，布局参考点位于 LayoutGuides。
2. FBX 只导出环境 Mesh，使用 -Z Forward / Y Up。保留原 FBX、材质与纹理的 .meta。
   制作脚本处理了 Unity FBX 导入的 X 轴手性转换，不要额外镜像根节点。
3. 日常更新原 FBX 后直接重导入，原场景实例继续引用相同资源。门窗或家具布局变化时，
   同步对应的场景 BoxCollider 与 classroom-manifest.json，不能只更新可见网格。
4. 初次安装使用 Tools > Music MIDI Player > Scene Art > Install Blender Music Classroom。
   该命令拒绝 Play Mode、其他场景和重复安装；不会重建现有播放器。
5. 从脚本重新生成会覆盖本任务生成的 .blend 和 FBX，因此手工修模后不要直接重跑脚本。
   需要重生成时先保存手工版本。

安装器比较同次执行前后原播放器所有 GameObject / Component 的序列化摘要；
记录在 Logs/MusicClassroom-install-report.txt。项目本身没有 Git 元数据，
不能把这一摘要当作全工程版本差异记录。

## 验证口径

以 Unity 实际渲染截图、原播放器序列化摘要、场景引用和建筑碰撞检查为依据。
静态数量是资源规模，不是 VRChat 客户端性能或帧率结论。真实 VRChat / 双客户端验收
与世界构建、上传不包含在本次美术接入中。

## 当前进度

用户因另一聊天正在控制同一 Unity 工程，要求暂缓本聊天的 Unity 操作。
教室初版已接入并保存；光照烘焙调用已完成，但烘焙后的最终目视复查、反射更新和
最终保存核对尚未继续。恢复前不启动 Play Mode、不重新导入、不保存或修改 Unity 场景。
已检查出生点、UI 前方、演奏座椅后方和侧门/走廊抽样位置没有新增环境碰撞阻挡，
两处门洞射线可穿过；原播放器未发现 Missing Script，两份分发 Prefab 的文件哈希未变。

## Blender 封面素材

独立封面文件为 ArtSource/DemoMusicClassroom/JapaneseMusicClassroom-Cover.blend，
渲染脚本为同目录 render_cover.py。使用 Blender Cycles / OptiX GPU、AgX 色彩管理，
16:9 无文字构图，交付位置为 Docs/UdonMidiPlayer/Covers/：

- classroom-cover-4k.png：3840 × 2160，16-bit RGB PNG 母版。
- classroom-cover-1080p.jpg：1920 × 1080，便于上传的 JPEG。

封面读入工程现有 WhiteGrandPiano.fbx，并按现有尺寸复现深色演奏琴凳；
独立调整相机、玻璃和暖色补光，外部纹理打包到封面 .blend 内。
这些渲染专用设置不回写教室制作源文件、FBX、Unity 场景或材质，保持 Unity 操作暂停。

### 蓝色瀑布流与《小星星》谱面版本

2026-11-01 开源筛选：以下为本地历史制作记录。该版本源 MIDI 来自 mfiles，原条款限个人 / 非商业使用；因此 v2 封面 `.blend`、两份谱面 JSON 和 v2 渲染成片均从公开源码和发布资源中排除，不能套用项目自有美术的 CC BY 4.0。基础教室及不使用该谱面的早期封面保留；制作脚本本身可公开，但重新制作须提供符合发布白名单的谱面输入。

- 源文件：ArtSource/DemoMusicClassroom/JapaneseMusicClassroom-Cover-v2.blend。
- 成片：Covers/classroom-cover-v2-4k.png 与 Covers/classroom-cover-v2-1080p.jpg。
- 制作入口：render_cover_v2.py；谱面几何：engrave_twinkle.py，均位于上述 ArtSource 目录。

按用户最新要求取消世界名，黑板写 No.0《小星星》，署名“作者：ChatGPT”。
实际数据取自 Assets/music/Classic/01_Twinkle_Twinkle_Little_Star.mid，源文件 SHA256
与歌曲资产的 sourceHash 一致。谱面分两行钢琴大谱表，包含全部 12 小节、90 个音符
（旋律 42、伴奏 48），4/4 拍、四分音符约 95 BPM。音高及起拍一一对应；
MIDI 中约 95% 的演奏门限按常规四分/二分音符记谱，未把释音间隙写成细碎休止符。
twinkle-score.json 保留原始音符，twinkle-engraving-map.json 记录每个源音符到谱面的位置映射。

蓝色瀑布流也取该 MIDI 开头的音符，横坐标读取现有钢琴实体琴键的位置；
作为封面静帧呈现，不修改播放器运行逻辑。音乐符号采用
[Bravura 音乐字体](https://github.com/steinbergmedia/bravura)，字体及 OFL 许可保存在 ArtSource 下 Fonts 目录。
