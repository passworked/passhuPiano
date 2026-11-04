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

2026-11-04 起，四层钢琴的 Standalone 导入使用 Unity Vorbis 质量 70，默认平台和马林巴继续使用 PCM。两种编码均保留原采样率、立体声和 Decompress On Load，峰值与能量表仍从 PCM 烘焙。上表的 Editor 内存为 PCM 时的观测。钢琴与鼓组同时切换的 PC 对照构建从 200.9 MB 降到 74.1 MB，试听取自 Unity 解码。源文件仍为 Ogg q4，旧采样文件保留以保护既有引用，当前 bank 仅引用 `Assets/UdonMidiPlayer/Audio/SelectedQ4/`。

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

## 2026-11-02：基于采样能量包络的音量平衡

当前主场景和两份分发钢琴 Prefab 默认开启“音量平衡”，玩家可在原设置项本地关闭。保持原 `PeakProtection` 对象路径、`settings.peak_protection` 本地化键及 `peakProtectionEnabled` / `_TogglePeakProtection` 接口身份，只更新显示名称与算法。不会同步玩家的开关或音量，也不修改歌曲、力度选层、区域增益、64 个物理声音源及空间化设置。

Editor 将每份解码采样按 20 ms 窗口计算各声道平均的均方能量，最后补零点；同一 bank 内重复采样引用共享包络范围。运行数据为扁平 `energyStarts` / `energyCounts` / `energyValues` 三组数组，弦乐索引按 `region * 2 + attack/body` 排列。按实际音频年龄和 pitch 查表线性插值，再乘音符增益、CC11、起音/释放/制音包络的平方；合计钢琴、马林巴、弦乐与鼓组的能量。未到实际起音时刻的预约不计入，恢复与定位使用实际源位置，弦乐持续段按真实循环相位取值。

统一软拐点压缩默认阈值 −16 dB（RMS），比例 4:1，拐点宽度 6 dB，最大衰减 18 dB；增益立即压低、按 180 ms 时间常数恢复，并补偿 +3 dB。补偿仅在开启时生效；关闭后系数立即为 1，恢复原来的本地音量路径。原 `peakBudget` 仅保留历史序列化兼容，不再控制新算法。

这是按采样估算的音乐能量，不是混音后采样级 DSP、真峰值限幅或 LUFS 标准化；不计玩家距离、空间化和各声部相位相关性。+3 dB 与既有 AudioSource 音量上限共同作用，不能据模型检查承诺所有曲目听感或最终峰值。当前阈值是可试听调整的初始参数。

用户侧钢琴/马林巴的 `PrepareBank → CopyBankToRuntime`、配置器的缺失数据升级，以及弦乐/鼓组安装、重导入、Apply Instrument Tone Changes 均生成包络并更新 bank/catalog 哈希。构建前检查包络范围、有限值、源数据与 backing Udon 持久化数组。日常 MIDI 加歌仍不改音源、不自动同步分发曲库。已有旧音源若缺少包络，运行时暂用原采样峰值平方保守估计；正式使用应先准备音源并应用配置。独立旧示例/练习场景的历史音源配置没有批量替换。

显式升级入口 `MidiAudioEnergyMigration.UpgradeProject` 仅迁移主场景与两份分发 Prefab 的音频元数据、哈希及开关，独立编辑 Prefab 内容，不 Apply 场景覆盖、不改曲库成员。菜单 **Tools → Music MIDI Player → Rebake Audio Energy In Open Scene** 可重新烘焙当前场景，不覆盖用户开关，也不自动保存场景。

算法检查：`MidiAudioEnergyValidation.Run`；报告 `Logs/AudioEnergyValidation.json`、迁移报告 `Logs/AudioEnergyMigration.json`。变更前备份 `RecoveryBackups/EnergyCompression-20261102/`。Editor 数学模型、Udon 编译、backing Udon 检查和实际 VRChat/人工听音结果须分别报告。

本次实测包络及索引单份净数据：主场景 629,372 bytes（约 0.60 MiB），每份钢琴分发 Prefab 333,580 bytes（约 0.32 MiB）。不是最终构建下载增量或客户端总内存。Editor 算法、用户音源准备/复制、实际 backing Udon 的 16 音及原生开关检查通过；整体构建前校验仍被原有 `Invalid MIDI localized folder summary: 1` 阻止，未改动该曲库 UI 数据。没有构建或真实 VRChat/人工听音验收。

## 八音盒（2026-11-04）

主场景追加模型8／目标7的八音盒，88个实体拨片 A0–C8；专用梳齿、旋转针带与手柄。采样使用rbnuser CC0音乐盒实录（12根音＋6个派生辅助根音），声音保持MIDI 0–127。维护入口 `Tools > Music MIDI Player > Add or Update Music Box`，首次需运行 `node Tools/prepare_music_box.cjs`。来源、预算、同步迁移和未验收项目见[专题](24-music-box.md)。

## 乐器响度校准与合奏余量（2026-11-04）

本节更新马林巴／八音盒的播放采样响度；上文早期试听增益继续保留为区域增益，不再代表最终总补偿。钢琴四层仍只选力度层，单层乐器仍用原 `(velocity/127)^1.6`，弦乐、鼓组保留各自力度／奏法规则。校准不改释放、调音、MIDI 内容、64 源预算、本地音量及空间配置。

`Tools/calibrate_instrument_loudness.cjs` 对马林巴请求固定 +6 dB、八音盒请求固定 +15 dB。每份播放采样只乘一个恒定系数；按完整文件解码后的四倍过采样峰值把增益限制在 −1.5 dBFS 以下，编码后重新测量，必要时再降低系数。没有逐时刻压缩、削波、限制波形或逐力度层归一化。实际每文件增益、原始 SHA256 与解码峰值记录在各自 `BANK-MAPPING.json` 的 `loudnessCalibration`。高峰值采样会少补偿，保留的自然尾音与力度曲线仍有意义。

校准从保留的未校准 Ogg 播放文件生成一次新的 Ogg（沿用马林巴 q4／八音盒 q5）；原件保存在 `RecoveryBackups/LoudnessCalibration-20261104/OriginalAudio/`，`.meta`、GUID 和引用不变。重复执行会验证并跳过同版本文件，避免反复重编码或叠加增益。`--verify` 只检查，`--bank marimba|musicBox` 限定目标；改变配方时先用原准备工具重新生成未校准 bank。`prepare_selected_tones.py` 和 `prepare_music_box.cjs` 在生成后自动调用校准；导入器继续核对最新 SHA256。之后执行 **Apply Instrument Tone Changes**，现在也更新已安装的八音盒 bank，并重新烘焙峰值／20 ms 能量、bankHash、catalogHash 与 backing Udon。

四套输出路径（合奏、独立钢琴、旧弦乐及鼓组）共用 `LimitOutputGain`：根据当前音符增益乘完整采样峰值求和，额外预留 25% 的编解码／重采样峰值余量后，以 −1.5 dBFS 为保守的干声混音余量；共用一个系数保持乐器比例和力度比例，同时保证单个 `AudioSource.volume` 不超过 1。当前 PC 导入实测鼓组最大解码／烘焙峰值比为 1.214，钢琴为 1.116，因此不能把源 PCM 峰值直接当作平台解码峰值。即时压低、180 ms 平滑恢复，不额外削平某件乐器。RMS“音量平衡”关闭后，基础余量保护仍保留。批量起音的新源从零音量开始，整批结算后一起应用安全增益。

该界限针对播放器采样和增益的数学上界，相关／同音叠奏也计入。使用整份采样峰值会让密集和弦更保守；它不是混音后的采样级真峰值限制器，也不涵盖后续空间化、混响、其他世界音源和客户端音频处理。Editor 检查与真实 VRChat／人工合奏试听仍须分别报告。

本次 Editor 复测：C3–C6、力度 80、前 0.5 s、逐音高相对钢琴能量差的中位数，马林巴由 −7.09 dB 改为 −1.27 dB，八音盒由 −17.19 dB 改为 −2.15 dB；这不是 LUFS 或每个音都等响的承诺。61 份马林巴实际增益 5.25～6 dB，18 份八音盒 1.68～15 dB，所有校准文件解码四倍过采样峰值均低于 −1.5 dBFS。检查主场景 745 份不同的导入 AudioClip 未见满幅样本；字符串 bank 的 3 类奏法另外记录前 0.5 s 和后续 0.5 s 的能量，独奏小提琴和低音提琴仍存在偏弱段，保留其现有增益，没有按短奏法衰减尾音反推并统一放大。

`MidiAudioEnergyValidation.Run` 的算法／序列化检查，以及实际 backing Udon 的 308 项检查通过（全 MIDI 域抽样、力度 1/32/64/80/100/127、实际音量比例、64 同音、全部活动乐器＋鼓组、RMS 开关及原 64 源预算）。报告：`Logs/loudness-reference-before.json`、`Logs/loudness-reference-after.json`、`Logs/loudness-all-banks-audit.json`、`Logs/loudness-other-instruments.json`、`Logs/loudness-backing-udon.json`、`Logs/AudioEnergyValidation.json`。未进行构建、真实 VRChat、双客户端或人工合奏听音验收。
