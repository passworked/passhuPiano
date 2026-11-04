<p align="center">
  <img src="Docs/Brand/passhuPiano-logo.png" width="160" alt="passhuPiano 标志">
</p>
<h1 align="center">passhuPiano</h1>
<p align="center">让 MIDI 在你的 VRChat 世界奏响。</p>
<p align="center">
  <a href="README.md">English</a> ·
  <strong>简体中文</strong> ·
  <a href="README.ja.md">日本語</a>
</p>
<p align="center">
  <a href="Docs/GETTING_STARTED.md">开始使用</a> ·
  <a href="Docs/Developer/AI_GUIDE.md">开发指南</a> ·
  <a href="Docs/Contributing/README.md">贡献乐器</a>
</p>

为 VRChat Worlds 提供 **MIDI 播放、多人共享播放与实时演奏**。

主场景支持钢琴、马林巴、八音盒、弦乐和鼓组，附可编辑美术与制作工具；两份独立钢琴 Prefab 单独维护。

文件播放通道采用黄金角配色。MIDI 曲库使用分类 / 曲目编辑器，支持拖入文件、文件夹和 ZIP，编辑署名、排序、维护源文件，统一“保存并应用”。见 [Unity MIDI 导入教程](Docs/MIDI_IMPORT.md)。

> 当前为 `1.0.0-rc.3` 开发候选，PC 优先。功能与验收边界见[当前实现](Docs/Developer/CURRENT_STATE.md)。

## 快速开始

安装 Git LFS，并取得私仓访问权限后：

```sh
git clone https://github.com/passworked/passhuPiano.git
cd passhuPiano
git lfs pull
```

通过 VCC / ALCOM 恢复 Worlds SDK，再用 **Unity 2022.3.22f1** 打开工程。记录的 SDK 基线为 **3.10.5**。打开主场景，或将钢琴 Prefab 拖入自己的世界。

详细步骤见[安装与使用](Docs/GETTING_STARTED.md)。[技术文档](Docs/UdonMidiPlayer/README.md) · [AI 工作指南](Docs/Developer/AI_GUIDE.md)

## 参与贡献

欢迎乐器、音源、美术、代码和文档贡献，可直接提交 PR，也可分阶段完成。

正式收录乐器前，需补齐模型、音源、接入和说明，提供 PC VRChat 单客户端实测，并由 **passhu** 评审。来源许可、资源硬上限与 AI 披露要求见[贡献指南](Docs/Contributing/README.md)。

## 许可

自有代码采用 [MIT](Licenses/MIT.txt)，自有美术采用 [CC BY 4.0](Licenses/CC-BY-4.0.txt)。第三方内容保留原许可与署名，详见[许可范围](LICENSE.md)及[来源清单](THIRD_PARTY_NOTICES.md)。
