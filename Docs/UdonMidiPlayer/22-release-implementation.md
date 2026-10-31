# v1.0 发布实施记录

日期：2026-11-01（Asia/Shanghai）。版本：`1.0.0-rc.1`。状态：本地候选准备，未公开发布，未宣告稳定验收。

## 已确定的授权规则

- 自有代码、工具与技术文档：MIT；自有美术：CC BY 4.0。作者 / 版权人 **passhu**，联系 **laptop7921@gamil.com**，邮箱按用户原文记录。
- 曲谱、音源只纳入公有领域 / CC0、CC BY 3.0 / 4.0 或核验过的无限制授权。排除 BY-SA、NC、ND、授权未知；MIDI 原件、曲目资产、packed 数据与内嵌播放数据一起筛选。许可版本数字不作为限制程度的排序。
- Salamander 保留 CC BY 3.0；VSCO / Virtuosity 保留 CC0；Iowa 保留原始无限制授权。字体的 OFL 单独保留，不把字体、SDK 或第三方内容重标为自有 MIT / CC BY。
- 本地原曲库不删除；只更新独立分发曲库及 Prefab，完整项目通过隔离副本清理，不能直接推送本机全部目录。

## 已落实的仓库与工具

- 根目录 README、LICENSE、MIT / CC BY 全文、第三方署名、贡献说明、CHANGELOG、统一 VERSION、Git 忽略与 LFS 规则。
- 本地 Git 已初始化并配置本地 LFS；没有远端地址，没有执行推送或远端发布。
- `Tools/Release/release.mjs`：盘点、固定哈希与来源、生成包内许可与白名单；生成 / 审核公开源码快照。无需第三方 Node 包。
- `MidiReleasePolicy`：拒绝不在白名单或哈希 / GUID 已变化的 MIDI、采样、派生曲目；检查 Prefab 内嵌曲目；对源码 / 包共用规则。
- `MidiReleasePreparation`：独立分发曲库筛选与增量 Prefab 数据同步；源码副本的曲库 / 配置 / 场景清理，保留原 `.meta`，去除世界 Blueprint ID。
- `MidiPackageExporter`：显式种子与依赖闭包、两份 Prefab 验证、补源 MIDI 间接依赖、版本化产物与清单；不自动覆盖 TMP / AudioManager，不导出 SDK 或回归脚本，不覆盖同名发布包。

## 当前盘点结果

首轮本地盘点：原 100 首精选中批准 13 首，排除 87 首 BY-SA；全工程批准 17 个 MIDI 路径及 17 个曲目资产（含相同内容的副本），757 个采样文件。其余 543 个 MIDI 路径及 543 个曲目资产、114 个未纳入本次核验的旧采样排除出发布集合。数字是文件路径数量，不是独立作品数量，也不是音频性能数据。

批准曲目的完整署名和来源见[第三方清单](../../THIRD_PARTY_NOTICES.md)，逐项路径 / GUID / SHA256 见[发布白名单](../../Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json)。本地未批准清单写入 `Logs/Release/license-audit.json`，不作为随包内容。

派生美术额外排除：mfiles《小星星》的原条款限个人 / 非商业；教室 v2 封面 `.blend`、谱面 / 位置 JSON 及 v2 成片同样不公开。历史播放截图尚无谱面来源审查记录，也先排除。规则集中在 `Tools/Release/public-exclusions.json`；本地原件保留。基础教室、无该谱面的早期封面与制作代码可公开。

两份钢琴 Prefab 已各保留 13 首合规曲目，并将内嵌 bank 同步到现有选定的四力度层钢琴源资产；未重建模型、改变现有音量设置或 Apply 主场景覆盖。主场景磁盘哈希已与本轮开始时的备份核对一致。Unity 脚本编译检查无错误；首次候选包依赖审核为 391 项资源，加入包内发布说明后最终为 392 项。

## 本轮实际证据

- 已通过 Unity 导出实际 `.unitypackage`：**40,787,340 字节（约 38.90 MiB）**，392 项资源、13 个 MIDI、120 个钢琴采样文件。
- 已解包检查实际 payload，逐项与发布清单、原始 / 派生曲目白名单及采样哈希比对：无缺项、额外资源或未批准音乐文件；包内包含完整许可、署名、白名单、版本和发布说明。
- 公开源码副本保留 13 个实际使用的 MIDI 和 13 个源曲目资产、757 个核验采样；2 个曲库、4 个配置及 3 个场景共 9 份清理副本保留原 GUID / 相对路径。
- 源码静态依赖检查：1,203 个 GUID、20,056 处资源引用，无缺失 GUID 或不匹配的 ScriptableObject fileID。SDK / Unity 包引用按已安装的对应包元数据核对，尚未代替干净项目导入。
- 拒绝路径检查：BY-SA MIDI、未批准 packed 数据、回归脚本、全局 TMP 设置均被拦截；批准文件的 SHA256 被改动时同样拒绝。
- Node 语法、所改 Python AST、PowerShell 解析检查通过。离线工具的本机解释器 / 采样分析目录已改为参数或工程相对路径；没有重新生成采样或美术。
- 本地完整排除明细、资源引用与包 payload 报告在 `Logs/Release/`。对外证据摘要和 SHA256 位于 `Releases/1.0.0-rc.1/`；这些均为 Editor / 静态 / 产物验证结果，不是真实客户端结果。

## 本地执行顺序

在仓库根目录运行：

```powershell
node Tools/Release/release.mjs inventory
```

在 Unity 完成刷新与编译后，通过 MCP 调用以下 Editor 方法，或使用对应 Release 菜单。先检查当前工程与场景，保留用户未保存内容：

```csharp
MusicMidiPlayer.Editor.Prefab.MidiReleasePreparation.PrepareDistribution();
MusicMidiPlayer.Editor.Prefab.MidiPackageExporter.Review();
MusicMidiPlayer.Editor.Prefab.MidiReleasePreparation.PreparePublicSourceAssets();
```

`PreparePublicSourceAssets` 只从已保存场景文件创建临时的独立导出副本，清理后写入 `Releases/<VERSION>/source/musicMidiPlayer/` 的原相对路径；不保存本地打开场景，也不将未保存修改混进产物。源曲库和配置用过滤后的序列化副本，保留原 GUID；临时导出场景结束后清除。主场景、多乐器、练习与教室仍各自保留原用途。

随后补全工具 / 美术源 / 文档与干净依赖清单，并执行独立检查：

```powershell
node Tools/Release/release.mjs source
node Tools/Release/release.mjs audit
node Tools/Release/verify-source.mjs
```

最后生成 `.unitypackage`：

```csharp
MusicMidiPlayer.Editor.Prefab.MidiPackageExporter.Export();
```

已存在同名包或源码候选清单时会停止，防止覆盖既有候选。需要重新出候选时明确保存旧结果并使用新的 `1.0.0-rc.N`，正式产物不可覆盖。同一个候选生成期间，修改了代码 / 文档 / 资源时应重新检查并更新尚未封存的产物。

解包到 `Logs/Release/package-inspection/` 后运行 `node Tools/Release/audit-package.mjs` 核对实际包。生成源码初始提交与归档后，以 `SEALED.json` 记录提交和产物哈希；此后源码生成器拒绝改写同一候选。

公开源码副本会移除可选开发 MCP 依赖、SDK 缓存与本机环境，保留 Unity / VPM 清单、原 `.meta`、源模型和必要运行 / 编译数据；旧 MIDI 来源元数据可能继续用于审计，但不携带被排除的谱子或音源文件。

## 尚未放行

- 干净接收工程导入 / U# 编译、重导入与旧版本升级，宿主全局设置对比。
- 真实 VRChat 听音、双客户端同步 / 晚加入 / Owner 交接，以及声明支持平台的性能和下载体积。
- 完整源码在另一目录恢复 SDK / LFS 后的独立打开验证。
- 托管平台、公开仓库 URL、最终远端上传与正式 `v1.0.0` 标签。未设置远端，不创建虚构下载链接。

上述项目未完成时保持 RC；Editor 静态 / 接线 / 打包检查不代替客户端验收。
