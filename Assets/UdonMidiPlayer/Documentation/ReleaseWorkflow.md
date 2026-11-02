# v1.0 发布实施记录

## 2026-11-02 大更新跟进与 GitHub 私仓

当前源码候选升至 `1.0.0-rc.2`；已创建仓库 `https://github.com/passworked/passhuPiano`，通过 GitHub API 核实可见性为 **PRIVATE**，使用 GitHub CLI 当前已登录账号 passworked。本次同步 main 分支；不公开仓库，不创建正式 Release 或稳定标签，保留已封存 rc.1 及其初始历史。

本轮核对到 12 个既有 C# 文件和 1 个 Shader 的更新，涉及声音池、Facade、PC/VR 输入、实时网络输入、UI、鼓组表现、瀑布与材质生成器；同时接入最新主场景的提琴奏法布局及曲库目录分类。具体性能实现与未测边界见第 09 册；这些修改按既有源码增量进入新快照，不重写运行实现，不把静态分析写成性能实测结果。

私仓仍沿用 MIT / CC BY 4.0、自有署名 passhu 和既定曲谱 / 音源筛选。源场景与配置在隔离副本中筛选；本地 547 首完整曲库、未批准素材和回归对比代码不随私仓上传。保留主场景路径、GUID、songId、独立练习及两份 Prefab 配置，不 Apply 主场景覆盖。

本轮范围为代码 / 资产 / 文档跟进、源码快照与私仓创建推送；不运行 Play Mode、Profiler、测试套件或世界构建，不宣称真实 VRChat 验收。自动编译状态、资源引用、许可与远端状态检查将记录在完成信息中。以下 rc.1 记录保留为历史证据。

rc.2 源码快照为 2,487 个文件，13 首批准曲目及 757 个采样，采样与 rc.1 的哈希全部一致；源码与对应编译程序同步更新。检查 1,203 个 GUID、20,056 处引用，无缺失 GUID 或 ScriptableObject fileID 不匹配；许可 / 哈希审核、凭据特征扫描及本地 LFS 检查通过，当前 Editor Console 无编译错误。主场景、两份分发 Prefab 的磁盘哈希及 rc.1 封存清单均与本轮开始时一致。本轮不另外导出 `.unitypackage`，上传的是新的受审查源码仓库。

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

批准曲目的完整署名和来源见[第三方清单](THIRD_PARTY_NOTICES.md)，逐项路径 / GUID / SHA256 见[发布白名单](ReleasePolicy.json)。本地未批准清单写入 `Logs/Release/license-audit.json`，不作为随包内容。

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

## 本地候选封存

已生成 `Releases/1.0.0-rc.1/` 下的 Prefab 包与完整源码 ZIP。源码公开副本有 2,487 个文件，本地初始提交为 `54f186306fb3190343e06b486c60bbd366df7a27`；未配置公开远端。另在本地新目录实际克隆该仓库，Git LFS fsck 与 783 个批准曲目 / 采样 / 派生资产的哈希检查通过，克隆后的工作树干净。此项只验证 Git / LFS 与文件完整性，尚未执行克隆工程的 Unity / VPM 恢复。

Prefab 包为 40,787,340 字节；源码 ZIP 为 174,947,195 字节，包含实际 LFS 资源与仓库 dotfiles，不含 `.git` 或 SDK / 本机缓存。`SHA256SUMS` 和 `SEALED.json` 记录完整哈希、源码提交与仍待验收项目。封存后的同版本源码生成会被阻止；后续修改使用新的候选版本。封存源码中的实施文档保持生成时的记录，本节为本地完成后的追加记录。
