# v1.0 预制体发布与 monorepo 开源规划

日期：2026-11-01（Asia/Shanghai）。状态：**已开始本地实施，尚未公开发布**。实际进度与复现入口见[实施记录](22-release-implementation.md)。

用户已确定：整个项目采用 monorepo，自有代码使用 MIT，自有美术使用 **CC BY 4.0 International**。版权人与署名为 **passhu**，联系邮箱 **laptop7921@gamil.com**；公开仓库地址尚未设置。第三方内容保留原许可，不纳入项目自有内容的统一授权。曲谱与音源只纳入公有领域 / CC0、CC BY 3.0 / 4.0 或已明确核验的无限制授权；排除 BY-SA、NC、ND 及授权不明的内容，原始文件与派生数据同样适用。

原规划阶段仅修改文档；用户随后授权开始实施。以下保留实施前的基线与阶段设计；不能将表中待办或目标当作已验收结果，当前完成情况以实施记录为准。

## 1. 发布目标与范围

建议 v1.0 先将现有两份独立钢琴 Prefab 固定为稳定产品；整个项目源码、美术源文件、主场景及工具在同一个仓库公开，分别声明成熟度。主场景拥有某项功能，不等于两份分发 Prefab 已接入该功能。

| 交付部分 | v1.0 边界 | 完成标准 |
| --- | --- | --- |
| 白色三角钢琴 | `Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer.prefab` | 干净 VRChat 工程可安装、配置与播放，引用完整 |
| 立式 / 数码钢琴 | `Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer_Upright.prefab` | 使用独立配置，延音音频正常；不宣称模型具有踏板动画 |
| 播放与交互 | 固定已有导入、曲库、播放控制、同步、表现及输入能力 | 建立两份 Prefab 的实际功能矩阵；修复其发布阻塞，不借发布扩充新功能 |
| 分发曲库 | 沿用独立古典曲库范围 | 逐曲核对原始 MIDI 与生成数据的来源、署名和许可；不能仅因属于古典作品就认定无版权 |
| 完整项目 | 主场景、多乐器、音色映射、练习、教室及制作工具 | 公开可再分发的源文件与配置，按模块说明验证状态、复现方法和缺项 |
| 可选示例 | 独立练习与音乐教室 | 与播放器包分开列出依赖及安装方式；不得改建替代主场景 |

v1.0 不以新增乐器、重写架构或改成 VPM 为前提。PC 作为建议的首发验收目标；移动端 / Quest 只有独立完成客户端验证后才列入支持矩阵，现有 64 物理 AudioSource 规则不因此改变。

发布前必须冻结一张“主场景 / 三角钢琴 Prefab / 立式 Prefab / 练习场景”功能矩阵，至少包含音源版本、曲库、鼓组、弦乐、GM 路由、PC / VR / 硬件 MIDI 输入、同步与语言。README 和发行说明只承诺对应交付物实测具备的能力。

## 2. 当前状态与具体缺口

以下来自本次文件静态检查，不是 Unity 或真实客户端验收结果：

| 证据 | 当前情况 | 发布前动作 |
| --- | --- | --- |
| 项目根目录 | `git status` 返回不是 Git 仓库 | 先完成公开内容边界，再建立首次公开历史；不把整个工作目录直接加入 Git |
| `ProjectSettings/ProjectVersion.txt`、VPM 清单 | Unity 2022.3.22f1、Worlds SDK 3.10.5 | 作为待验证基线；锁定真实验收版本，不写成已支持所有新版 SDK |
| `Editor/Prefab/MidiPackageExporter.cs` | 输出路径仍为 `Releases/UdonMidiPlayer-v0.1.0.unitypackage` | 改为单一版本来源与独立输出目录，旧版本产物不覆盖 |
| 同一导出器 | 以 `Assets/UdonMidiPlayer` 下几乎所有文件为种子，再递归收集依赖与 MIDI `sourceGuid` | 改为明确发布种子和可审阅依赖清单；当前行为不能直接用于 v1.0 |
| `Runtime/RegressionTemp` | 存在对比用脚本与程序资产 | 审核其实际引用；禁止随稳定包泄漏，也不能通过删除依赖掩盖引用问题 |
| TMP 设置 | 导出器主动加入全局 `TMP Settings.asset` | 审核并去除对宿主全局设置的覆盖；保留必要字体与局部 fallback 引用 |
| 随资源 README | 两份 Prefab 使用独立古典曲库；主场景另有多乐器更新 | 发布配置与场景配置分开冻结，不 Apply 场景覆盖 |
| 现有文档 | 同时保留早期单层音源和后续四层音源等历史描述 | 按实际分发资产重写发布入口与功能矩阵，历史设计不能充当当前规格 |
| 验收说明 | 仍有真实 VRChat 双客户端与听音未验收记录 | 重新建立 RC 对应的证据表，不能沿用旧 Editor 报告宣告 v1.0 通过 |

## 3. monorepo 组织

一个仓库承载播放器、完整 Unity 世界工程、美术源文件、离线工具和文档，首发采用统一发布版本。保留现有根目录作为 Unity 工程根，不为了 monorepo 搬进新的 `apps/` 或复制一套 `Packages/` 代码。

```text
musicMidiPlayer/
  Assets/
    UdonMidiPlayer/          # Runtime、Editor、Prefab、音源、模型、UI、示例
    Scenes/                 # 原主场景及音乐教室；保持现有 GUID
    music/                  # 仅公开已核验授权的曲目及来源清单
    ...                     # 实际需要且允许分发的其他 Unity 依赖
  ArtSource/                # .blend、制作脚本、可编辑美术源文件与来源
  Tools/                    # 采样处理、MIDI、离线制作工具及依赖锁定
  Packages/                 # Unity / VPM 清单与锁文件
  ProjectSettings/          # 复现所需且经过公开检查的项目设置
  Docs/UdonMidiPlayer/      # 架构、维护、接入、验收与发布文档
  Licenses/                 # 拟新增：MIT、CC BY 4.0 及第三方许可原文
  .github/                  # 拟新增：Issue / PR 模板及必要检查流程
  README.md                 # 拟新增：下载、安装、模块状态、开发与许可入口
  LICENSE.md                # 拟新增：按内容划分的许可说明
  THIRD_PARTY_NOTICES.md    # 拟新增：第三方来源、版权与改动记录
  CONTRIBUTING.md           # 拟新增：贡献、验证与分类型许可规则
  CHANGELOG.md              # 拟新增：公开发行记录
  VERSION                   # 拟新增：发布版本的单一来源
  .gitignore / .gitattributes
```

各部分只有一份权威源文件；发布包从同一提交生成。`ArtSource` 保留在 `Assets` 外，避免 Blender 源文件和导出模型被重复导入。后续如果提供 VPM，从这一份源数据生成，并单独处理 Assets 安装与 Packages 安装之间的 GUID 冲突和迁移。

### Git 与大文件

- 纳入 Git：自有源码、Unity YAML、`.meta`、必要 UdonSharp 程序资产 / 注册、编辑器配置、源模型、许可、来源与可复现脚本。不能一概忽略 `Generated`、所有 `.asset` 或 `Assets/SerializedUdonPrograms`；按实际引用和再生能力确定范围。
- 在首次提交前配置 Git LFS：对选定美术 / 音频目录中的 `.blend`、`.fbx`、`.glb`、纹理、字体及采样等二进制资源按清单跟踪。C#、Shader、JSON、`.unity`、`.prefab`、文本 `.asset`、`.meta` 保持普通 Git 文本。先统计大小并决定规则，再首次提交，避免事后搬迁历史。
- 忽略本地内容：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Captures/`、`ClientSimStorage/`、`RecoveryBackups/`、`Downloads/`、`Releases/`、缓存和个人 IDE 文件。公开证据应摘出并脱敏到文档，不直接公开这些目录。
- `Tools/LiveMidi` 下已有打包 / Python 环境目录；公开工具源码、依赖声明和锁定信息，审查后按需提供单独便携产物，不直接把本机 Python 环境纳入源码。
- `Packages/com.vrchat.*` 等已安装 SDK 内容通过官方依赖恢复；公开清单和锁定信息，不把 SDK 当自有源码重新授权。开发用 Unity MCP 依赖与用户必需依赖分开，公开复现说明不得无意强制安装开发代理工具；保留时固定已核验版本，避免只依赖浮动 `main`。
- 完整克隆流程写明 Git LFS 拉取、SDK 恢复和首次编译；验证没有 LFS 指针被 Unity 当真实资源导入。若选用 GitHub，明确自动 Source ZIP 是否含 LFS 内容，另提供已实测可用的完整源码快照。

## 4. 许可范围

这是**同仓、按内容划分许可**，不是给每个文件同时套用 MIT 和 CC BY。

| 内容 | 拟采用许可 | 标记方式 |
| --- | --- | --- |
| 自有 C#、Shader / HLSL、Python、构建与制作脚本 | MIT | 源文件 SPDX 或目录规则，附版权人与许可全文 |
| 自有技术文档、配置逻辑、示例代码 | 建议 MIT，作为软件配套文档与配置 | 文档中嵌入的第三方图片 / 内容另行标注 |
| 自有模型、纹理、图标、插画、动画、材质及可编辑美术源 | CC BY 4.0 | 明确作者、作品、来源、许可链接及改动；源文件与导出物关联 |
| Prefab、场景、混合 `.asset` | 按自有配置与所含 / 所引内容登记 | 配置建议 MIT；内嵌原创美术与第三方数据保留对应许可，不能仅按扩展名统一授权 |
| 第三方采样、字体、MIDI、代码及其派生数据 | 各自原许可 / 授权 | 逐项例外清单及许可原文；不能统一改为 MIT 或 CC BY 4.0 |

脚本生成模型时，脚本属于代码，生成的自有模型属于美术；第三方输入数据的权利不会因转换、压缩或烘焙消失。`.meta` 与对应资源保持关联，许可登记不能遗漏内嵌数据和跨目录引用。

MIT 允许商业使用、修改与再分发，需保留版权及许可声明。CC BY 4.0 同样允许商业使用与改编，要求合理署名、许可链接及改动说明；不含 NC 或 SA 条件。具体以 [MIT 正文](https://opensource.org/license/mit) 与 [CC BY 4.0 正文](https://creativecommons.org/licenses/by/4.0/legalcode.en) 为准。不要另加“禁止商用 / 禁止二次分发”条款。

### 已发现的第三方例外

| 内容及本地证据 | 现有标注 | 发布处理 |
| --- | --- | --- |
| `Audio/SelectedQ4/Piano/ATTRIBUTION.txt`、`ORIGINAL-README.txt` | Salamander / Alexander Holm、FreePats，CC BY 3.0 | 保留原署名和许可；记录四力度层选择、转码及调音等改动，不改称 CC BY 4.0 |
| `Audio/SelectedQ4/Marimba/LICENSE.txt` | University of Iowa，来源站点的自由使用授权 | 保留原授权文本、来源与采样制作说明，不擅自标成 CC0 或自有 CC BY |
| `Audio/Strings/ATTRIBUTION.txt` | VSCO 2 CE，CC0-1.0 | 保留来源、版本、派生映射与现有许可文件 |
| `Audio/VirtuosityDrums/ATTRIBUTION.txt` | Virtuosity Drums，CC0-1.0 | 保留固定上游提交及分层 / 替代 / 转码记录 |
| `Fonts/font-manifest.json` | Noto、Comfortaa 等，SIL OFL 1.1 | 保留每款字体的版权与原许可；字体不纳入项目美术 CC BY 默认规则 |
| `Tools/ClassicalMidi/curated_100.json`、随资源 README | 包含 piano-midi.de 的 CC BY-SA 3.0 Germany，以及其他来源的不同许可 | 按最新要求排除全部 BY-SA；目前 100 首中筛出 13 首公有领域或 CC BY 曲目，原始来源记录保留用于审核 |
| 其他场景曲目、贴图、参考图片、字体、外部代码 | 本轮尚未完成全量登记 | 权利不明的内容先隔离出公开快照与包；本地原件保持，逐项补证或替换 |

最新发布规则不保留 BY-SA 曲谱或音源；相关本地原件继续保留，但不进入公开源码或包。生成的曲目资产和运行数据一并登记其输入与许可，不能只排除原始 `.mid`；也不能把 BY-SA 重标成普通 CC BY。参见 [CC BY-SA 3.0 DE](https://creativecommons.org/licenses/by-sa/3.0/de/deed.en)。

### 许可交付格式

1. 根 `LICENSE.md` 解释默认范围、例外优先级及全文位置，分别放置 MIT 和 CC BY 4.0 全文。仅给根目录放一份 MIT 会误导美术使用者。
2. 建立资源登记表，字段至少包含：路径 / GUID、作者与版权人、来源 URL、固定版本 / 哈希、许可标识及原文路径、改动、关联派生物、入包范围、核验状态。自定义授权使用清晰的 `LicenseRef`，不编造 SPDX 许可名。
3. `THIRD_PARTY_NOTICES.md` 提供可阅读的署名汇总，明示代码与美术的默认规则；机器登记表支持逐项追溯。两者都从公开内容清单维护。
4. `.unitypackage` 内也包含适用于包内资源的许可与署名副本，例如 `Assets/UdonMidiPlayer/Documentation/`；不能只放在仓库根目录，导致下载包缺失许可。不强制新增启动弹窗或打断玩家流程。
5. 贡献说明要求提交者拥有贡献权利，代码与美术分别按对应许可贡献；第三方资源必须携带来源及许可。署名身份由贡献者提供，不从本机用户名猜测。

## 5. 发布物与导出规则

| 发布物 | 内容 | 受众 |
| --- | --- | --- |
| `UdonMidiPlayer-v1.0.0.unitypackage` | 两份钢琴 Prefab、必需代码 / 资源 / 程序资产、已审核分发曲库、快速指南、许可与署名 | 世界作者，主要下载入口 |
| `musicMidiPlayer-source-v1.0.0.zip` | 同一提交对应的完整可公开 Unity 工程、美术源与工具，含实际 LFS 文件；不含 SDK 缓存和本地数据 | 二次开发与美术修改 |
| 可选示例包 | 练习或教室示例，分别说明对基础包的依赖 | 需要示例的作者；与源码状态一致 |
| 发行说明、资源清单、`SHA256SUMS`、验收摘要 | 精确版本、支持矩阵、已知限制、内容 / 许可与哈希 | 所有使用者 |

导出器后续修改要求：

- 从两份正式 Prefab、必要 Editor 工具、选定配置、许可与署名文件等**显式种子**收集依赖；Editor 功能的隐式加载资源需要登记。生成包前输出最终资源清单与来源清单供审阅。
- 必须审核依赖闭包，拒绝临时回归脚本、未经授权曲目、测试数据、开发场景及无关编辑器资源。遇到不允许的必需依赖应报错并修复接线，不能静默删掉后继续导出。
- 保留源 MIDI 的 `sourceGuid` 间接依赖处理，并将补入文件也放入授权检查；不以 Unity 自动依赖分析替代来源审核。
- 不把 Unity / VRChat SDK 整包带入，不覆盖接收工程的 TMP、音频或其他全局设置；动态资源、字体 fallback、Udon 程序引用必须可解析。
- 版本、包名、发行说明与资源清单从同一个 `VERSION` 值派生。输出放入 `Releases/1.0.0/`，候选版独立命名；不改写既有 `0.x` 文件。
- 打包过程不更新主场景、不自动同步场景曲库、不 Apply Overrides。需要修正分发 Prefab 时，只对其独立配置与资源增量修改，并保留原 GUID。
- 完整项目快照也要执行公开清单检查；对权利不明的主场景引用，在隔离的发布副本中做明确剔除 / 替换并验证，保持原主场景路径及 `.meta`。公开源码不能留下断引用，也不能为了“不漏功能”泄漏未授权资源。

首次发布优先 `.unitypackage`。VPM 安装属于后续独立改造：需确定真实发布者 package id、依赖、包结构、目录迁移与升级策略；不得同时导入两份同 GUID 的 Assets / Packages 资源。依据 [VPM Packages](https://vcc.docs.vrchat.com/vpm/packages/)。

## 6. 实施阶段与放行条件

按依赖顺序推进，不在资产盘点前承诺发布日期。各阶段的检查范围限定为发布交付所需，不借机全工程重构。

| 阶段 | 工作 | 可审阅产出 / 放行条件 |
| --- | --- | --- |
| A 内容与范围冻结 | 核验两份 Prefab 功能、公开模块、全量第三方内容与版权人 | 功能矩阵、授权登记表、公开 / 排除清单；未核验必需资源有处理方案 |
| B 仓库准备 | 建立忽略与 LFS 规则、公开依赖、许可证、首页、贡献说明；检查凭据、个人路径及世界发布身份 | 可审阅的首次提交清单；记录 Blueprint ID 等公开处理，保留本地配置；主场景及 GUID 不丢失 |
| C 发布工具与文档 | 统一版本来源、收紧导出器、补包内署名、清理发布入口的历史规格歧义 | 最终包路径清单与依赖 / 许可报告；代码 / 美术源 / 产物可追溯同一提交 |
| D RC 候选验收 | 生成 `1.0.0-rc.1`；干净接收工程、源码克隆与客户端验收 | 下表所需证据齐全；失败修复后只重验受影响项，变更候选版本 |
| E 正式公开 | 固定通过验收的提交，创建 `v1.0.0` 标签，上传对应发布物并复核下载 | 标签、包、源码、哈希、许可与发行说明一致；下载后可用 |
| F 维护 | Issue 分类、复现信息、兼容修复、许可新增审查 | `1.0.x` 修复、`1.x` 兼容功能、破坏稳定契约时升级主版本 |

### RC 最低证据

| 验证 | 必须核对 | 记录口径 |
| --- | --- | --- |
| 静态公开检查 | 许可覆盖、清单、版本一致、无凭据 / 用户目录 / 缓存；无漏资源或 LFS 指针 | 精确到候选提交与产物 SHA256 |
| 源码复现 | 新目录完整克隆、LFS 与依赖恢复、原主场景及已声明模块可打开 | 不使用原开发目录的 Library、Downloads 或本机隐藏文件 |
| 干净工程导入 | 按声明 Unity / SDK 安装，导入包、C# / U# 编译、两份 Prefab 配置与基本播放 | 无 Missing Script / 资源；不改变宿主全局设置 |
| 导入 / 升级与隔离 | 重导入、保留用户曲库及覆盖、两实例互不串线、既有旧版升级 | 保留 GUID / songId；列出实际覆盖的旧版本；未测版本不承诺 |
| 基本播放与听音 | 播放、暂停、停止、切歌、定位及已声明输入；力度 / 延音 / 释放与表现 | 分别记录 Editor、ClientSim、实际 VRChat 听音，不互相替代 |
| 双客户端 | 同步、晚加入、Owner 转移 / 竞争 / 离开、权限和恢复 | 真实 VRChat 两客户端结果；未执行则候选保持未通过 |
| 资源与平台 | 密集曲压力、声音源预算、长时间播放、实际内存与下载体积 | 源文件压缩量、包体、构建下载量、解码内存分别报告；平台逐项声明 |

验收脚本 / 流程只覆盖必要风险。Unity 操作优先 MCP；开始实施时再次核对正在运行的聊天、场景脏状态与当前操作限制。本规划不解除其他任务对 Unity 操作的限制，也不构成世界上传指令。

## 7. 版本与维护契约

- 产品展示名 v1.0；机器版本 `1.0.0`；Git 标签 `v1.0.0`。候选版用 `1.0.0-rc.N`，正式发布后不替换同名产物。
- 首次稳定契约包含命名空间 / 程序集、已分发 GUID、稳定曲目 ID、声明的配置与外部入口。未明确列为公共 API 的内部方法，不因公开源码就自动成为稳定接口。
- 冻结 `MusicMidiPlayer.*`、Runtime / Editor 隔离及分发根路径。必要的序列化迁移单独说明，不通过删除重建绕过兼容问题。
- 一个主分支维护同一权威源，首发各交付物统一版本；允许注明某模块仍在开发。新增独立包后再评估独立版本，不预先引入多个发布系统。
- 修复按 [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html) 约定发布。撤回问题版本时说明原因并发布新版本，不移动旧标签或静默替换资源。
- 主场景加歌继续不自动同步分发曲库。每次发布审核新增资源的许可、署名和依赖闭包，美术更新与曲库变更分别记录。

## 8. 开始实施时需补齐的信息

- 公开仓库的托管平台、组织 / 用户名、仓库名与下载入口。本规划以 GitHub Release 作为可选落点，不假定已有账号或远端。
- 版权人与美术署名已确定为 passhu，邮箱 laptop7921@gamil.com；美术已确定采用 CC BY 4.0。主页未提供，不从邮箱或本机用户名推测。
- 已确定不发布 BY-SA / NC / ND / 授权不明的谱子与音源；保留独立古典曲库中通过白名单的曲目，其余仅存本地。
- 两份 Prefab 最终支持矩阵及升级来源版本；主场景、多乐器、练习与教室哪些标为稳定、预览或开发中，必须以相应证据确定。

这些信息不妨碍先做本地清单、文档与发布工具准备；身份 / 许可范围不明的内容不先作不可撤回的公开授权声明。

## 9. 参考与证据入口

- [维护约定](17-maintenance-conventions.md)、[命名空间与分发](12-namespaces-and-distribution.md)、[资源说明](../../Assets/UdonMidiPlayer/README.md)。
- [现有导出器](../../Assets/UdonMidiPlayer/Editor/Prefab/MidiPackageExporter.cs)、[精选曲目来源清单](../../Tools/ClassicalMidi/curated_100.json)。
- [MIT](https://opensource.org/license/mit)、[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)、[CC BY-SA 3.0 DE](https://creativecommons.org/licenses/by-sa/3.0/de/deed.en)。
- [Iowa 原始授权页面](https://theremin.music.uiowa.edu/MIS.html)、[GitHub LFS 与源码归档说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-git-large-file-storage)。

外部规则核对日期：2026-11-01；第三方授权表是本轮已有证据的登记起点，不是已完成的全量许可审计。
