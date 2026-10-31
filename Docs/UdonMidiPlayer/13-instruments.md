# 主场景乐器切换

## 2026-11-01 多音色选择

主场景乐器菜单已采用复选框，多件同时启用时按 GM 分组＋例外将每条音符分配给一件已选乐器，不叠加复制。单选统一落到该音源，空集不播放旋律。两种钢琴外观仍共用一个音源位。实际规则、运行链、共享预算及维护入口见第 19 册；下文历史单选切换说明不再代表主场景当前交互。独立练习与分发 Prefab 未安装此扩展。


## 弦乐本体反馈（2026-11-01）

用户已取消弦乐钢琴窗、音符瀑布与落琴条。琴体保持固定，保留运弓；`StringsView` 按实际路由点亮各根发声琴弦，并在发音处生成有上限的光粒与漂浮音乐符号，复用钢琴两层效果的资源和生命周期曲线并缩小运动范围。和弦支持多弦高亮，源 MIDI 哈希的亮 / 暗配色与钢琴一致；单件最多 48 光粒＋16 符号，暂停冻结、停止 / 切曲清理、定位恢复当前弦光且不补播旧符号。MIDI 域外音继续发声，不强行驱动不存在的实体位置。新乐器不强求钢琴窗；详细规则见第 18 册第 2.1 节，实现与验证见 [弦乐专题](14-strings-preparation.md)。

四把提琴（小提琴、中提琴、大提琴、低音提琴）的上述配色和两层粒子均已逐把验证。主场景乐器弹层按启用项分别提供自动 / 长弓 / 跳弓 / 拨弦入口，独立选择同时驱动声音与动作并进入公共快照；更改一把琴时保留其他乐器的声音起点。独立旧链与分发 Prefab 保持原配置。详见 [控制与 UI](07-controls-and-ui.md) 和弦乐专题的最新验证记录。

## 2026-10-31 文档草案入口

新增乐器请先阅读 [追加规范（待审阅）](18-adding-instruments.md) 的当前实现清单及代码 / 文档更新矩阵；音色覆盖与候选回退见 [GM Program 0～127 fallback 草案](19-gm-program-fallback.md)。本次静态读取的主场景已有 8 个乐器选项和专用弦乐引用；鼓组代码与素材存在，但主场景鼓组引用仍为空。以下三种外观、旧音源和历史验证记录保留各自日期，不作为当前完整选项清单；Program 保存能力有并行追加，本文 fallback 表未写入音频路由。

## 音符落点：键盘后沿

2026-10-27：“根部”指整排键盘的最后沿。设置显示为“键盘后沿”，只汇总已绑定琴键／琴条在瀑布流局部坐标中的最远 Z 边界，扣除音符半厚度及 2 mm 余量，保留音高对应的 X 和落点高度，所有音符共用后沿 Z。琴壳、琴盖、琴架不参与边界计算。旧序列化字段和按钮事件名保留，以保护场景、Prefab 和 Udon 引用。

马林巴的两排琴条及实体范围外音符使用同一琴条布局后沿线；“当前位置”模式继续落在对应琴条前部，实体范围外则沿用最近同排琴条的高度与深度，避免落点跳回默认坐标。主场景和两份钢琴 Prefab 的落点一起更新。

## 当前试听定案：2026-10-26，q4

本节取代下文旧 VCSL / 单层钢琴规格，旧记录仅保留迁移历史。当前主场景共用 64 声部池，PC 与移动端容量一致。

* 钢琴：Salamander 原始 v3、v7、v11、v15，每层 30 个实录根音（A0～C8，每 3 半音），共 120 份立体声采样。力度区间 1～44、45～69、70～100、101～127，与试听页四层的最近中心选择一致。原始 SF2 调音与释放保留。两个钢琴外观共享同一个 bank。
  * 2026-10-27 力度响应调整：四层钢琴仅用 MIDI 力度选层，取消额外的 `(velocity/127)^1.6` 衰减，保留录音本身的响度差异，不做逐层归一化。曲目起音、预约、暂停/定位恢复和实时 MIDI 共用此规则；区域增益、释放包络、本地音量、空间音频及峰值保护设置保持原样。单层马林巴及旧三层音源继续使用原力度曲线。运行时按 `velocityLayerCount == 4` 识别本项目的四层钢琴。
* 马林巴：Iowa Yarn 绒线槌 ff，全部 60 个实录根音，C2～C7，一个力度层；原标签 Eb2 实测为 D2，按 D2 映射，缺少的 Eb2 使用最近根音。恒定增益约 −2.224 dB，与试听页参考响度匹配一致，不逐音归一化。
* 两套源文件统一 48 kHz、立体声 Ogg Vorbis q4。马林巴额外含一份由 C7 派生的 C8 辅助采样，避免 MIDI127 超过 Unity AudioSource.pitch 的 3 倍上限；共 61 个播放文件，但只有 60 个实录音。域外继续保留原 MIDI 音高，不折叠八度、不丢音；大跨度变调音色仍有变化。

| 当前测量口径 | 钢琴 | 马林巴 |
|---|---:|---:|
| q4 压缩源文件 | 21,439,157 bytes（20.45 MiB） | 1,193,586 bytes（1.14 MiB） |
| float32 缓冲估算 | 637,250,688 bytes（607.73 MiB） | 44,370,944 bytes（42.32 MiB） |
| Editor AudioClip 内存观测 | 319,083,864 bytes（304.30 MiB） | 22,418,553 bytes（21.38 MiB） |

导入仍使用 PCM / DecompressOnLoad，避免再次有损编码。q4 只代表压缩源音质和体积，不等于构建下载体积，也不减少解码缓冲。旧采样文件保留以保护既有引用，新 bank 仅引用 `Assets/UdonMidiPlayer/Audio/SelectedQ4/`。

可复现步骤：运行 `Tools/prepare_selected_tones.py`，再执行 **Tools → Music MIDI Player → Apply Selected Q4 Banks**。该命令原位更新既有源 bank（保留 GUID），并更新主场景的运行时 bank、catalogHash 与 Udon 接线。音色继续通过下文 Apply Instrument Tone Changes 修改；完整重新导入会恢复本次选定的调音、增益、释放规格。

Iowa 授权与来源保存在新音频目录的 `Marimba/LICENSE.txt`，Salamander 原始署名在 `Piano/ORIGINAL-README.txt`。逐文件 SHA256 和映射见 `SelectedQ4/BANK-MAPPING.json`；导入测量见两套 `IMPORT-REPORT.json`。验证报告：`Logs/selected-q4-validation.json`、`Logs/selected-q4-playmode.json`。变更前备份：`RecoveryBackups/SelectedQ4-20261026/`。

## 旧迁移记录

2026-10-26。主场景为 `Assets/Scenes/VRCDefaultWorldScene.unity`，原来两个播放器已合并为一个。练习场景保持独立。

菜单只有一个“乐器”入口：数码钢琴、白色三角钢琴、马林巴。前两个选项共用 Salamander 钢琴音源；更换钢琴外观不改变播放代次、不取消声音槽位、不重新起音。三种外观共用一套曲库、输入监听、网络 Transport、时钟、Sequencer 和 64 声部池，仅启用当前外观与其瀑布流表现。

## 模型与音源

马林巴实体覆盖 C2～C7（MIDI 36～96），含 61 根独立琴条、双排布局、共鸣管、琴架与脚轮。源文件、FBX、GLB 和预览位于 `ArtSource/UdonMidiPlayer/`，生成脚本为 `build_marimba.py`。Unity 资源为 `Assets/UdonMidiPlayer/Models/ConcertMarimba.fbx`。

采样来自 [Versilian Community Sample Library](https://github.com/sgossner/VCSL)，固定提交 `c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e`，CC0-1.0。原始 WAV 下载到 `Downloads/MarimbaVCSL/`，授权和署名随 `Assets/UdonMidiPlayer/Audio/Marimba/` 分发。

保留原始 soft、med、loud 三个实录力度层，MIDI 力度 1～42、43～84、85～127 分别选择对应层，再应用原有力度增益。每层原始录音是 F1、C2、G2、B2、F3、C4、G4、B4、F5、C6 十个稀疏根音；原始素材合计 30 个 WAV。`Tools/prepare_marimba.py` 在各层内从最近录音离线变调，制作 A0～C8 的 30 个播放根音，共 90 个 AudioClip，48 kHz、Vorbis q8、保留完整自然尾音、无循环。逐文件原录音、变调量及 SHA256 见 `BANK-MAPPING.json`。高低音扩展具有变调音色特征。

声音池在实时 MIDI、曲目起音、预调度、暂停/定位恢复时都按原始力度选层，每个音符仍只占一个声部。钢琴保持原有单层采样、配置和 bankHash。

为复用钢琴已验证的发声链路，采样选择表继续覆盖 21～108，域外只钳制选样索引，以原 MIDI 音高计算倍率；MIDI 0～127 都保留原音高，不折叠八度、不丢音。扩展根音保证运行时 `AudioSource.pitch` 不超过 3。实体范围外不按下琴条，瀑布流按音高向两侧延伸，颜色继续使用同曲目调色板。

## 音色修改

编辑源资产的 `clips`、`rootPitch`、`regionGain`、`tuningCents`、`releaseDurationMs`，然后在合并后的主场景运行 **Tools → Music MIDI Player → Apply Instrument Tone Changes**。马林巴源资产为 `Assets/UdonMidiPlayer/Editor/Generated/MarimbaBank.asset`；钢琴继续使用播放器配置指向的钢琴源资产。钢琴两种外观始终共享修改结果。

该命令重新计算映射、峰值与哈希，并更新运行时 bank 和包含两个音源的 catalogHash；不是单纯替换 Inspector 引用。马林巴默认释放为 900 ms，钢琴维持原有释放参数。重新导入马林巴时保留已编辑的增益、调音与释放参数。修改乐器期间歌曲进度、暂停状态和现有 MIDI 输入入口继续保留；按住与踏板持续音会迁移到新音源，之后的松键／踏板抬起仍有效。

## 预算与验证

| 口径 | 钢琴基线 | 马林巴 |
|---|---:|---:|
| 压缩源音频 | 10,188,801 bytes（9.72 MiB） | 1,545,693 bytes（1.47 MiB） |
| float32 纯采样缓冲估算 | 154.29 MiB | 117,743,080 bytes（112.29 MiB） |
| Editor AudioClip 原生内存观测 | 77.26 MiB | 59,220,406 bytes（56.48 MiB） |
| AudioSource | 64 | 共用同一池，无新增声部 |

两套音源都使用 PCM / Decompress On Load 导入。以上是相同口径的音源测量，不等于最终世界下载、构建体积或 VRChat 客户端内存。

实际完成 Unity/UdonSharp 编译、构建前接线校验与 ClientSim 内 backing Udon 验证：三个菜单按钮，MIDI 0、35、36、60、96、97、127 的 NoteOn/Off，钢琴外观切换不重新起音，按住音跨音源切换，CC64 持续音及切换后踏板释放，播放中／暂停中切换、定位、快照恢复选项。测试中手动驱动了 ClientSim 未回调的序列化生命周期，不能据此声称真实多人通信已验收。

报告：`Logs/instrument-merge-report.json`、`Logs/instrument-midi-playmode.json`、`Logs/instrument-pedal-playmode.json`、`Logs/instrument-transport-playmode.json`。实际 VRChat 双客户端、硬件 MIDI 设备及人工听音仍需实机验收。合并前的主场景保存在 `RecoveryBackups/InstrumentMerge-20261026/`。

三层升级补充记录：`Logs/marimba-velocity-playmode.json` 检查力度 1、42、43、84、85、127 与音高 0、36、60、96、127 的 30 个交叉组合，确认实际 backing Udon 声部选层和原音高倍率、钢琴 hash 保持不变及共用 64 声部。序列化回调在 ClientSim 中模拟，不能替代真实客户端多人验收。
