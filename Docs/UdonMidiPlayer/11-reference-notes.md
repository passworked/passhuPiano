# 11 · 参考依据与验证边界

日期：2026-10-23。本文区分阅读源码得到的事实、本项目的设计选择、当前 SDK 的静态证据，以及尚须在客户端确认的能力。设计分册不是已完成实现或测试报告。

## 1. VizVid 固定源码事实

参考仓库为 [JLChnToZ/VVMW](https://github.com/JLChnToZ/VVMW)，本次对照提交为 [956ae415972cafa69f94518e40214ebf9f0206ba](https://github.com/JLChnToZ/VVMW/commit/956ae415972cafa69f94518e40214ebf9f0206ba)，不将 develop 分支未来变动混入结论。

| 固定提交中可确认的事实 | 直接源码 |
| --- | --- |
| Core.Play/Pause/Stop 本地执行后 RequestSync；后者在非 Owner 时 SetOwner(LocalPlayer)，再序列化 | [Core.cs:541–572](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core.cs#L541-L572)、[799–803](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core.cs#L799-L803) |
| Frontend 的 locked 为本地字段，接口接外部权限系统；普通控制先检查锁 | [FrontendHandler.cs:37–64](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L37-L64)、[233–265](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L233-L265)、[489–499](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L489-L499) |
| Frontend 的列表状态与 Core 播放状态分别提交，不保证永久同一 Owner | [FrontendHandler.cs:360–375](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L360-L375)、[508–512](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L508-L512) |
| Owner 在序列化回调采样，接收端恢复媒体与时间 | [Core.cs:689–795](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core.cs#L689-L795) |
| 有轻量心跳、异常 Owner 接管，以及向 Owner 请求补同步的入口 | [Core_Timing.cs:237–307](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core_Timing.cs#L237-L307)、[394–432](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/Core_Timing.cs#L394-L432) |
| 自动下一首由 Core 当前 Owner 推进 | [FrontendHandler.cs:430–443](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/Packages/idv.jlchntoz.vvmw/Runtime/VVMW/FrontendHandler.cs#L430-L443) |

这些事实支持“可转移 Owner、本地播放、同步状态、补发和活跃性处理”的路线，不等于它的媒体时间计算可直接移植给 MIDI 合成器。尤其不把某段包含 sendTime 的延迟表达式复制到本项目服务器锚点公式后再相加。

## 2. 本项目借鉴后的独立设计

一个待提交完整意图、ownership 复查、失败撤销、Command/Apply 分离、generation 取消、曲库哈希、MIDI 长音及释放尾音恢复、事件 sender 校验和 Paused 无自动失活接管，均是本项目约束。不能写成参考仓库已实现同样的事务保证。本项目 P0 没有视频 URL 加载，也没有 VizVid 的 Frontend 队列同步需求，先保留单个网络 Transport。

本次仅阅读、整理和撰写设计，没有复制其实现进入运行目录。后续若复制或改编 VizVid 代码，应按其 [固定提交 MIT 许可证](https://github.com/JLChnToZ/VVMW/blob/956ae415972cafa69f94518e40214ebf9f0206ba/LICENSE) 保留版权声明与许可文本，并在交付记录标明来源；此为交付要求，不增加用户审批流程。

## 3. 当前工程的静态证据

以下路径相对工程根目录，行号只描述当前本地包；SDK 更新后重新核对。

| 本地文件 | 能证明的范围 |
| --- | --- |
| Packages/com.vrchat.worlds/package.json:4 | 本地 Worlds 包版本为 3.10.5 |
| Packages/com.vrchat.worlds/Integrations/UdonSharp/Runtime/UdonSharpBehaviour.cs:103、108、121–219 | 网络事件包装存在，包含带参重载；下划线与 NetworkCallable 的入口规则有注释 |
| 同文件:398、416–417、439–440 | ownership、反序列化和发送结果回调签名存在 |
| Packages/com.vrchat.worlds/Integrations/UdonSharp/Runtime/UdonSharpAttributes.cs:56–60 | None 禁用网络事件；NoVariableSync 与 None 语义不同 |
| Packages/com.vrchat.worlds/Runtime/Udon/UdonBehaviour.cs:1085–1110、2108–2125 | 运行包装转发序列化回调及网络调用 |
| Assets/UdonSharp/UtilityScripts/BoneFollower.cs:7；Packages/com.vrchat.worlds/Integrations/UdonSharp/Editor/Compiler/UdonSharpCompilerV1.cs:511 | 显式 namespace 的示例存在；主类型短名需与源码文件名相同，不能以全类型名作为文件名 |
| Packages/com.vrchat.worlds/Integrations/UdonSharp/Editor/UdonSharpAssemblyDefinition.cs:12；Editor/Compiler/CompilationContext.cs:261–264（均位于同 UdonSharp 包） | 自定义 Runtime asmdef 通过 U# Assembly Definition 的 sourceAssembly 登记；只建 Unity asmdef 不足以收集 Udon 源码 |

包装签名不能证明每个 API 已暴露给 Udon、所有平台行为相同或已完成双客户端测试。本文未通过运行 Unity 验证这些能力。

## 4. 官方规则与 M0 核对表

| 资料 | 用途及核对边界 |
| --- | --- |
| [Object Ownership](https://creators.vrchat.com/worlds/udon/networking/ownership/) | 当前 Owner 写同步状态、离开后自动交接；不依赖特定 Master |
| [Network Variables](https://creators.vrchat.com/worlds/udon/networking/variables/) | Manual Sync 与 RequestSerialization；晚加入使用持久状态 |
| [Network Components](https://creators.vrchat.com/worlds/udon/networking/network-components/) | OnPre/OnPostSerialization、DeserializationResult、发送时间所属时基和交接回调 |
| [Network Events](https://creators.vrchat.com/worlds/udon/networking/events/) | NetworkCallable、CallingPlayer/InNetworkCall、限流及遗留无参入口；M0 编译并验证可信 sender 和嵌套调用上下文 |
| [Late Joiners & Sync Issues](https://creators.vrchat.com/worlds/udon/networking/late-joiners/) | 网络事件不补历史，变量保存状态；M0 验证慢初始化后追到最新时刻 |
| [Networking Specs & Tricks](https://creators.vrchat.com/worlds/udon/networking/network-details/) | Manual 请求可能等待发送；M0 验证合并、失败重试与最终收敛 |
| [Unity 2022.3 AudioSource.PlayScheduled](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.PlayScheduled.html)、[AudioSettings.dspTime](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSettings-dspTime.html) | 说明 Unity 侧 DSP 调度语义；不能替代 Udon 暴露及 VRChat 内预约/取消/定位验证 |
| [MIDI Playback](https://creators.vrchat.com/worlds/udon/midi/midi-playback/) | SDK MIDI 播放/事件组件背景；不据此认定它提供本项目所需的钢琴采样合成 |

M0 还须用已知差值验证 CalculateServerDeltaTime 的参数方向、回绕及 double 时间映射；验证 SetOwner 竞争/拒绝、ownerId 与 seq 的作用域、心跳伪造拒绝、暂停时加入与长音采样定位。若 DSP 预约在当前 Udon 不可用，必须记录降级方案并重估同步目标，不能标为测试通过。

完整任务与验收顺序以 [总索引](README.md) 关联的里程碑分册为准；本次没有新增自动构建、运行测试或发布动作。
