# 安装、使用与开发入口

面向 `passworked/passhuPiano` 的私有源码使用者。记录的开发基线为 Unity **2022.3.22f1**、VRChat Worlds SDK **3.10.5**；它不是所有平台、后续 SDK 或客户端的兼容保证。当前源码 `1.0.0-rc.2`，没有正式 v1.0 稳定包。

## 获取工程

1. 先取得维护者授予的仓库访问权限，安装 Git 与 Git LFS，并完成自己的 GitHub 认证。
2. 克隆仓库并拉取实际二进制资源：

   ```powershell
   git clone https://github.com/passworked/passhuPiano.git
   cd passhuPiano
   git lfs pull
   ```

3. 使用 VCC / ALCOM 为此项目恢复清单中记录的 Worlds SDK 及依赖，再用匹配 Unity 打开仓库根。SDK 安装目录和 Library 不随源码分发；不要从另一台电脑复制 Library 来绕过恢复。
4. 等待导入与 C# / UdonSharp 编译完成。遇到错误先记录实际提交、Unity / SDK 版本和首个错误；不要删 `.meta` 或重建 GUID 来清除错误。

打开后模型 / 音源看似损坏时，先确认 LFS 文件已经落地，而不是几十到几百字节的 `version https://git-lfs.github.com/spec/v1` 指针。Node 仅用于仓库的离线发布工具；普通配置播放器不要求先安装 Node 或所有 Python 工具。

## 选择正确对象

| 用途 | 入口 | 注意 |
| --- | --- | --- |
| 把独立钢琴接进自己的世界 | `Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer.prefab` 或 `UdonMidiPlayer_Upright.prefab` | 使用独立分发配置；不会因此具备主场景所有乐器扩展 |
| 查看完整开发世界 | `Assets/Scenes/VRCDefaultWorldScene.unity` | 继续使用原主场景，不为普通修改新建替代场景 |
| 个人练习 | `Assets/UdonMidiPlayer/Samples/MidiPractice.unity` | 独立用途，不把练习模式强加到分发 Prefab |
| 修改模型 | `ArtSource/` 中对应源文件及制作脚本 | 保留导入资源的 `.meta`；重跑生成器可能覆盖手工模型 |

## 配置与曲库

在自己的目标场景放入相应 Prefab，使用 **Tools → Music MIDI Player → Configure Player** 选择目标、源曲库及音源，再由配置工具烘焙运行数据与接线。使用项目现有导入工具添加自己具有使用权的 MIDI，填写曲名 / 作者，保留既有 songId 与重导入映射。

源曲库资产、配置资产和运行时烘焙数据是三层，不要只换 Inspector 引用或只改生成器。应用配置、同步目录、修改音源和 Apply Prefab Overrides 的作用不同；先核对实际命令和目标，避免覆盖用户未保存内容。

私仓分发只保留合规曲库。自己在本地导入并不自动获得公开分发权，发布到此仓库仍必须通过项目许可规则；它也不会自动进入两份分发 Prefab 的独立曲库。

## 继续阅读

- [当前实现与源码地图](Developer/CURRENT_STATE.md)：类型、ID、主场景 / Prefab 边界。
- [AI 与开发者指南](Developer/AI_GUIDE.md)：阅读顺序、任务定位、证据和最小变更方式。
- [提交新乐器](Contributing/README.md)：维护者已确认的收录流程、预算与模板。
- [技术专题索引](UdonMidiPlayer/README.md)：更细的播放、同步、音源、UI 与发布说明。

本页不宣告 Play Mode、实际接收工程、硬件设备、PC VRChat 或双客户端已经通过。执行任何测试前，核对当前任务授权及工程状态。
