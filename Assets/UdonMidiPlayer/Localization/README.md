# 播放器国际化

编辑器与世界面板各自提供 English、中文、日本語三个语言入口；中文按客户端环境使用简体或繁体译文，没有独立的繁体选项。两者分别选择语言，修改编辑器语言不会改变世界面板的语言。

- **编辑器**：导入、配置窗口与曲库 Inspector 顶部的“文/A”图标可打开单排三个国旗按钮，悬停显示语言名称。初次使用优先采用 Unity 编辑器语言，无法读取时采用系统语言；不支持的语言使用英文。点击中文时，繁体中文环境显示繁体，其余环境显示简体。选择保存在本机 EditorPrefs，界面没有自动语言按钮。
- **世界面板**：右上角“文/A”图标展开单排三个语言按钮。英国、中国、日本国旗依次对应英语、中文、日本語，不提供自动语言按钮。首次进入按 VRChat 客户端语言初始化，并标记当前语言。点击中文后，客户端设为繁体中文时使用繁体译文，简体中文及其余客户端语言使用简体译文；客户端语言变化时，中文选项同步切换简繁，手选英语和日语保持不变。选中项带浅青标记，点选或点击面板外侧会收起。不支持的客户端语言使用英文。手动选择仅影响本地的当前播放器，在本次会话内生效，不影响其他玩家或播放同步，也不写入玩家持久数据。
- 当前语言缺少译文时回退到英文；英文也没有的键会显示键名。曲目标题、曲库标题与目录身份路径保留原文。玩家可见的标准分类名（合成器浪潮 / Synthwave、EDM、游戏音乐、东方、classic、sample、Demo、Drums）通过 `library.folder.*` 显示当前语言译名，文件夹按钮与顶部目录路径同步刷新；其他自定义目录和专名保留原文。

## 维护与接线

世界译文位于本目录的 `World/en.json`、`World/zh-Hans.json`、`World/zh-Hant.json`、`World/ja.json`。四份文件保持相同键；修改译文时保留 `{count}`、`{error}` 等占位符。构建验证会检查语言表完整性与字体字形。编辑器译文位于 `Assets/UdonMidiPlayer/Editor/MidiEditorTranslations.cs`，使用英文原文作为稳定键，三项译文依次为简体中文、繁體中文、日本語。

`Tools > Music MIDI Player > Update Player UI` 更新主三角钢琴 Prefab 的语言文件、图标面板和文字接线。已有场景播放器可通过曲库 Inspector 的“保存并同步场景目录”（`Save and sync scene folders`）更新，导入后的场景同步、重新应用配置也会更新语言接线；完成后保存场景。立式 Prefab 保留自己的配置，可在 Prefab 编辑模式重新应用该配置。新增语言时，还需同步维护编辑器语言列表、世界构建器的语言列表与图标选项、运行时语言检测及字体覆盖。图标通过 `MidiLanguageIconAssets` 在 `UI/Skin/Language` 生成，不依赖国旗 Emoji 字体。

## 参考

2026-11-04：钟楼场景的作者／赞助／社群板通过 `MidiPlayerUI.languageListeners` 与组合的 `LocalizationManager.playerUI` 接入同一世界语言选择。`community.*` 键在四份 World JSON 同步维护；群号、URL、作者及赞助者专名不翻译。绑定播放器时不使用组合原有的独立持久语言偏好；源组合 Prefab 保留未绑定时的原四语回退。

参考了本机保留的 VizVid **1.8.2** 与 VRCW Foundation **0.0.68**：`C:/Users/<user>\ALCOM\Projects\PullTabIdle\Artifacts\VizVid_1.8.2\staging`。版本由各包 `package.json` 确认；语言选择、英文回退与文字刷新机制参考 Foundation 的 `Runtime/I18N/EditorI18N.cs`、`LanguageManager.cs`、`LanguageReceiver.cs`、`LanguageSetter.cs`。

官方语言表：[世界内语言表](https://github.com/JLChnToZ/VVMW/blob/main/Packages/idv.jlchntoz.vvmw/Resources/lang.json)、[编辑器语言表](https://github.com/JLChnToZ/VVMW/blob/main/Packages/idv.jlchntoz.vvmw/Resources/editor-lang.json)。本项目独立实现；分发不需要安装 VizVid 或 VRCW Foundation。

## Music box (2026-11-04)

`instrument.music_box` is provided in en / zh-Hans / zh-Hant / ja. Model index8 / melodic target7 uses the existing instrument selector, file mapping and hardware MIDI target naming. `credits.music_box_detail` describes the actual lossy preview sources and derived range.
