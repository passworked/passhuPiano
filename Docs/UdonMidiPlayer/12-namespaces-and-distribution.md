# 命名空间与 Prefab 分发约定

2026-11-01：v1.0 与整个项目 monorepo 开源的后续实施计划见[发布与开源规划](21-v1-release-and-open-source.md)。本文的 v0.1 / M0 等表述保留其历史设计语境；当前发行范围、许可与放行条件以新规划及后续实际验收记录为准，不表示本次已经打包发布。

所有项目自有 C# 类型必须显式声明命名空间，避免作为 Prefab 导入其他世界工程时与既有脚本冲突。根命名空间按当前工程名统一为 `MusicMidiPlayer`；首次正式分发前冻结，不在命名空间中加入 v0.1 等版本号，也不使用宿主工程的默认根命名空间。公共接口与运行语义仍遵守 [统一约定](CONTRACTS.md)。

## 1. 运行模块完整类型名

`Runtime` 是代码目录和程序集职责边界，不自动成为命名空间片段。运行侧按职责使用下列命名空间；各分册为方便阅读使用类的短名，完整类型名以下表为准。

| 源文件目录 | 命名空间 | 项目自有类型 |
| --- | --- | --- |
| `Runtime/Data` | `MusicMidiPlayer.Data` | MidiSongData、MidiLibraryData、PianoBankData |
| `Runtime/Logic` | `MusicMidiPlayer.Logic` | MidiControlGate、MidiPlayerFacade、MidiTransportClock、MidiSequencer |
| `Runtime/Networking` | `MusicMidiPlayer.Networking` | MidiNetworkTransport |
| `Runtime/Audio` | `MusicMidiPlayer.Audio` | PianoVoicePool |
| `Runtime/Views` | `MusicMidiPlayer.Views` | PianoKeyboardView、MidiWaterfallView |
| `Runtime/UI` | `MusicMidiPlayer.UI` | MidiPlayerUI、MidiSongButton |

例如入口完整类型为 `MusicMidiPlayer.Logic.MidiPlayerFacade`，网络组件为 `MusicMidiPlayer.Networking.MidiNetworkTransport`。C# 文件名保持与主要 Unity/UdonSharp 类型的短名一致，例如 `MidiPlayerFacade.cs`；不用把完整命名空间写进文件名。

新增枚举、静态帮助类、常量类和测试辅助类型同样进入对应命名空间；不在全局作用域留下 `Utils`、`Settings`、`Core`、`Player` 等项目自有类型。继承的 Unity/VRChat 生命周期方法仍保留原名。

## 2. Editor 与示例类型

| 源文件目录 | 命名空间 | 计划类型或职责 |
| --- | --- | --- |
| `Editor/Assets` | `MusicMidiPlayer.Editor.Assets` | MidiSongAsset、MidiLibraryAsset、PianoBankAsset、MidiPlayerConfigurationAsset |
| `Editor/Import` | `MusicMidiPlayer.Editor.Import` | MidiImportWindow、Editor 解析适配器及诊断 |
| `Editor/Baking` | `MusicMidiPlayer.Editor.Baking` | 曲目/曲库/音源烘焙、哈希与数据校验 |
| `Editor/Prefab` | `MusicMidiPlayer.Editor.Prefab` | MidiPlayerConfigWindow、自定义 Inspector、布局与构建检查 |
| 示例脚本 | `MusicMidiPlayer.Samples` | 后续需要的演示脚本；原始 MIDI/场景不是 C# 类型 |

Editor 类型位于 Editor 目录及 Editor-only 程序集，运行程序集不能引用它们。配置窗口使用 `global::UnityEditor.EditorWindow`，自定义 Inspector 的基类明确写 `global::UnityEditor.Editor`，避免与项目自己的 `MusicMidiPlayer.Editor` 命名空间混淆。

第三方解析库保留原始命名空间与许可，不通过机械改名宣称为项目自有代码。适配器位于本项目 Editor.Import，依赖仅从 Editor 侧接入。

## 3. 源码书写与跨模块引用

源码采用显式大括号 namespace，先通过当前 Unity/UdonSharp 版本编译再固定模板。不依赖 asmdef 的 rootNamespace 自动修改现有脚本；该字段不能代替代码中的 namespace 声明。

下例只展示文件结构和类型引用，不是当前已实现的播放器：

```csharp
using UdonSharp;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Logic
{
    public class MidiPlayerFacade : UdonSharpBehaviour
    {
        public MidiLibraryData library;
    }
}
```

Editor 的 CustomEditor、工具生成模板和配置器使用明确的类型引用，例如 `typeof(global::MusicMidiPlayer.Logic.MidiPlayerFacade)`；组件引用使用具体类型或序列化对象引用，不通过全局短类名字符串猜测类型。不引入运行时反射作为 Udon 类型查找方案。

`SendCustomEvent`、Unity Button 回调和 `[NetworkCallable]` 指向的是目标 Behaviour 上的方法名。例如 `_OnPlayPressed`、`_Heartbeat` 和 `_RequestSnapshot` 保持原事件名，不改成 `MusicMidiPlayer.UI._OnPlayPressed`。普通 C# namespace 与 Udon 方法入口是两个独立约定；Command/Apply 及网络入口规则见 [同步分册](04-multiplayer-sync.md)。

## 4. 程序集边界

计划使用下面的独立程序集，名称在首次分发前与命名空间一起冻结：

| 程序集 | 源码位置 | 约束 |
| --- | --- | --- |
| `MusicMidiPlayer.Runtime` | `Runtime/MusicMidiPlayer.Runtime.asmdef` | Runtime 组件与数据；引用当前实际使用的 UdonSharp、VRChat、UI 等程序集，不引用本项目 Editor 或解析器 |
| `MusicMidiPlayer.Editor` | `Editor/MusicMidiPlayer.Editor.asmdef` | includePlatforms 仅 Editor；引用 Runtime、所需 Editor API 与解析依赖 |

Runtime 的 rootNamespace 设置为 `MusicMidiPlayer`，Editor 设置为 `MusicMidiPlayer.Editor`；子命名空间仍在每份源码中显式声明。Runtime 内部的六个职责分组共享一个程序集，不额外拆成六个 asmdef。asmdef 名称、文件名、C# 命名空间和 package id 是不同标识，不能相互替代。

当前 SDK 带有 `UdonSharpAssemblyDefinition`，通过 sourceAssembly 关联 Unity asmdef。计划注册资产为 `Runtime/MusicMidiPlayer.Runtime.UdonSharp.asset`，其 sourceAssembly 指向 Runtime asmdef。M0 必须一并创建该注册，确认编译器实际收集这些源码、跨类型引用可解析，以及导入干净世界后仍可编译；仅创建普通 asmdef 不足以让 UdonSharp 编译器收集自定义程序集。Editor-only 程序集不作为 Udon 行为源码注册。具体引用列表按本地 SDK 解析后确定，不照抄其他插件的 GUID 或本机依赖缓存路径。

本轮只冻结命名设计，不创建空程序集文件。实际脚本、asmdef、U# Assembly Definition、程序资产与 `.meta` 在 M0 一起生成和验证，避免把尚未验证的程序集注册标为完成。

## 5. 资源与工具标识

| 标识 | 约定 |
| --- | --- |
| 分发资源根目录 | 保留 `Assets/UdonMidiPlayer/`，避免散落在宿主 Assets 根目录 |
| 主 Prefab | `Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer.prefab` |
| 菜单 | `Tools/Music MIDI Player/Import MIDI`、`Tools/Music MIDI Player/Configure Player` |
| AddComponentMenu | 使用 `Music MIDI Player/` 前缀，再按 Data/Logic/Networking/Audio/Views/UI 组织 |
| CreateAssetMenu | 使用 `Music MIDI Player/` 前缀，Editor 源资产单独分组 |
| 自有 Shader 名称 | 使用 `MusicMidiPlayer/` 前缀，例如 `MusicMidiPlayer/Waterfall`；C# namespace 不会替 Shader 名称避冲突 |
| 生成模板 | 输出对应完整 namespace，按目标文件夹选模板；不能沿用宿主默认新脚本模板 |
| EditorPrefs/SessionState 键（如使用） | `MusicMidiPlayer.` 前缀，按项目/资源范围保存；运行设置不写入公共全局键 |

Prefab 内部的 Network、Runtime、Data、UI 等子对象名可以保留；接线依靠序列化引用和稳定 GUID，不用 `GameObject.Find("UI")` 等全场景通用名称搜索。只有真正项目自有 Shader 使用上述前缀，第三方 Shader 不批量改名。

v0.1 优先按已有目录组织 Prefab/资源包分发。后续若提供 VPM/UPM 包，另行确定带真实发布者身份的唯一 package id；不根据本机路径推测组织名，不创建第二份同 GUID 的 Assets/Packages 副本。包名变动不能顺带改变已经发布的 C# 类型名。

## 6. 稳定升级与干净工程验证

首次分发时保留所有 `.meta`、UdonSharp 程序资产、完整类型名和程序集名。升级对原资源增量更新，避免通过删除重建脚本或复制同 GUID 副本解决名称问题。namespace 和程序集不加版本后缀；同一产品升级覆盖原版本，不假定两份同 GUID 包能并存。

后续确需调整 namespace/程序集时，单独设计升级迁移，并验证序列化字段、Editor 源资产、Prefab、场景覆盖和 UdonSharp 程序引用；不能只改 using 后就宣布旧 Prefab 兼容。类型迁移本身不改变 songId，也不以重建曲库 GUID 绕过引用问题。

分发前将成品导入只有受支持 VRChat SDK 的干净世界工程；再与另一个含同短类名、不同命名空间的测试组件共存，确认没有全局类型、程序集、菜单或 Shader 冲突。验证两个播放器实例的本地接线隔离、重导入/升级后引用完整，以及接收工程不依赖当前开发场景、机器绝对路径或 Assets 默认程序集。具体实施项见 [里程碑](10-milestones-and-delivery.md) 与 [验收](09-performance-and-validation.md)。
