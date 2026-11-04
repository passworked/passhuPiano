# 作者、赞助与社群交流板

2026-11-04 从 MCP 连接的 `PullTabIdle` 工程、`Assets/PullTabIdle/Scenes/Prototype_Ledger.unity` 的 Editor 内存提取。源场景已有未保存修改，本次没有保存源场景。保存的源组合预制体为 `Assets/CommunityBoardsExport/AuthorSponsorCommunityBoards.prefab`。

## 当前工程入口

- 组合预制体：`Assets/UdonMidiPlayer/CommunityBoards/Prefabs/AuthorSponsorCommunityBoards.prefab`。
- 包含 `Author wall`、`Sponsor wall`、作者板内的 `08 · 宣传栏`，以及内部接线的语言管理器与赞助数据读取器。
- 保留原有作者署名、QQ／VRChat／Discord／Patreon 联系入口、木框、字体、材质与按钮事件；社群板保持嵌入作者板的原布局。源组合预制体保留原 `PULLTAB IDLE` 标签；钟楼场景实例按 2026-11-04 后续要求将底部项目标签改为 `MIDI PLAYER`，沿用当前绿金配色与布局。
- 导入时保留源资源 GUID／`.meta`，共用已有 TMP Shader 与 SDK 包；不覆盖本工程的已有字体。字体许可随 `PullTabIdle/Fonts/LICENSE.txt` 一起导入。
- 根节点位于双板中心，可统一移动／旋转组合。最初只导入资产；同日经用户后续授权，已在钟楼场景入口墙东侧放入一个保留连接的实例，坐标与维护入口见[钟楼 UI 布局](25-clockwork-second-scene.md#2026-11-04-ui-重新布局与社群板入场)。主场景和播放器分发 Prefab 未添加实例；组合预制体不包含 Light，场景覆盖未 Apply 回源资产。

## 独立依赖与维护

所有导入脚本使用 `MusicMidiPlayer.CommunityBoards` 命名空间及独立 Runtime／UdonSharp 程序集。`MembershipAccess` 保留原下载端点、响应校验、启动重试与赞助名单分帧处理，移除彩票扇、游戏控制器和奖励回调；不会提供彩票玩法。赞助者每帧最多处理 32 条、最多 1000 条，名单验证后一次提交；翻页在点击事件内立即刷新。社群复制沿用原生 URL 输入框与合并的两帧弹出流程。

语言管理器只持有本组合的监听者；原场景海报、语言按钮及 VizVid 桥接依赖不带入。源组合预制体仍保留独立四语及 `musicMidiPlayer.communityBoards.language` 偏好键。钟楼场景实例现已接入播放器语言，接线与验证见下节。

## 跟随播放器翻译与统一配色（2026-11-04）

`ClockworkCommunityBoardsBuilder.Install()`（菜单 `Tools/Music MIDI Player/Scene Art/Localize and Style Clockwork Community Boards`）增量更新钟楼现有实例，先保存内存备份，再更新场景覆盖；不 Apply 回源组合，不修改主场景或播放器分发 Prefab。

- 组合的 `LocalizationManager.playerUI` 绑定场景播放器，播放器的 `languageListeners` 在初始化、手选语言及客户端语言变化时通知组合刷新。标题、社群入口与已保留的宣传文案共用 `Localization/World` 四份语言表，支持英语、简体／繁体中文、日语；接入时不读取或写入独立板的持久语言偏好，也不增加轮询。作者名、赞助者名、群号与链接保持原文，底部项目标签固定为 `MIDI PLAYER`。
- `AuthorWallDisplay` 统一负责作者与四入口标签，移除同标签上 `LocalizedText` 的重复翻译键，避免语言事件后被另一写入者覆盖；赞助页标题走同一翻译来源，名单与翻页逻辑不变。
- 板面、标题、分隔线、文字及按钮状态从已安装的播放器场景组件读取当前绿金主题：面板 `#143E32`、标题底 `#1C4A3D`、内嵌底 `#09261E`、金色 `#D8B96A`、暖白 `#F6EDD5`、次级文字 `#C5C9A8`，保留木框；不使用通用 UI 生成器为其他场景保留的旧默认配色。四个原图标通过 `UICommunityPalette.shader` 和场景专属 `CommunitySocialPalette.mat` 映射原配色，保留源纹理、Sprite GUID 与点击事件。

Unity C# 与 UdonSharp 编译无错误；133 项 Editor 场景引用、当前主题配色、四语文字适配及六按钮 GraphicRaycaster／事件目标检查通过，报告为 `Logs/CommunityBoardsLocalization-validation.json`，四语截图位于 `Captures/CommunityBoardsLocalization/Boards-{zh-Hans,zh-Hant,en,ja}.png`。预览显式调用 proxy 刷新方法，不将其当作 backing Udon 事件运行结果。未执行 Play Mode、真实 VRChat、在线名单下载、实际复制／群组跳转、构建或上传；场景仍只有一个 Light。

源资产到导入资产的路径清单见 `Logs/CommunityBoards-import-manifest.json`。日后更新应增量替换此目录并保留 GUID，不回写源场景或自动 Apply 到其他 Prefab。

## 已完成检查与边界

Unity C# 与 UdonSharp 编译通过；Editor 检查无缺失脚本／悬空序列化引用，作者／赞助语言引用、双向数据读取引用、四个社群入口、两个翻页按钮与 Udon program 已接线。记录为 `Logs/CommunityBoards-validation.json` 与 `Logs/CommunityBoards-backing-validation.json`，隔离渲染为 `Logs/CommunityBoards-preview.png`。预览工具曾产生临时 RenderTexture 清理顺序错误，临时相机与预览场景已关闭，该错误不来自预制体脚本。没有执行 Play Mode、ClientSim、真实 VRChat、在线赞助数据下载、实际复制／群组页面交互或世界构建。
