# 钢琴本体 · Blender 模型交付

本目录提供两套独立钢琴模型：石墨色数码钢琴和白色三角钢琴。只保留钢琴本体、键盘及必要的琴腿或支架；没有瀑布屏、显示框、命中线、侧面控制屏或示例音符。

另含五个八度的马林巴：`ConcertMarimba.blend`、`Exports/ConcertMarimba.fbx`、`Exports/ConcertMarimba.glb`，预览为 `Previews/ConcertMarimba_Hero.png`，重建脚本为 `build_marimba.py`。61 根琴条独立保留，其余硬件按材质合并。

2026-10-31 新增鼓组：`VirtuosityDrumKit.blend`、`Exports/VirtuosityDrumKit.fbx`、`Exports/VirtuosityDrumKit.glb`，预览 `Previews/VirtuosityDrumKit_Hero.png`，生成器 `build_drum_kit.py`，统计与落点见 `drum_kit_manifest.json`。模型由本项目程序建模生成，名称表示用于所选采样的鼓组外观，不是采样作者提供的模型。包含底鼓、军鼓、三只通鼓、Crash、Ride、上下踩镲、脚踏、鼓凳及辅助打击落点；鼓皮、镲片、鼓棒和击槌保留独立 pivot，静态硬件按材质合并，镲片有实体厚度和两面网格。重建时仍使用下方 Blender 路径，将 `--python` 参数改为此生成器；脚本同时原位更新 Unity 的 `Assets/UdonMidiPlayer/Models/VirtuosityDrumKit.fbx`，保留既有 `.meta`。

## 文件

| 模型 | Blender 源文件 | 通用导出 | 预览 |
| --- | --- | --- | --- |
| 石墨色数码钢琴 | `MidiPlayer_Visual.blend` | `Exports/MidiPlayer_Visual.fbx`、`Exports/MidiPlayer_Visual.glb` | `Previews/MidiPlayer_Hero.png`、`Previews/MidiPlayer_Keyboard.png` |
| 白色三角钢琴 | `WhiteGrandPiano.blend` | `Exports/WhiteGrandPiano.fbx`、`Exports/WhiteGrandPiano.glb` | `Previews/WhiteGrandPiano_Hero.png` |

以上路径均相对于 `ArtSource/UdonMidiPlayer/`。文件名 `MidiPlayer_Visual` 沿用原源文件名称，内容以当前纯钢琴本体版本为准。

- `build_models.py`：重建石墨色数码钢琴、导出文件和预览图。
- `build_white_grand.py`：重建白色三角钢琴、导出文件和预览图。
- `model_manifest.json`、`white_grand_manifest.json`：记录对应模型的布局及几何统计，具体以生成后的清单为准。

`.blend` 是可继续调整网格、材质和场景的源文件；FBX 与 GLB 用于其他三维工具中的交换。预览场景的摄像机、灯光和地面用于渲染，不作为钢琴本体导出。

## 查看与修改

直接用 Blender 打开所需 `.blend` 文件。数码钢琴采用石墨色琴体、木色侧板和黑白键；白色三角钢琴作为另一套独立造型提供。

琴键保留独立对象，便于修改造型、配色或以后制作按键表现。此次交付为静态模型，没有附加演奏、联网或播放逻辑。材质使用程序设置的基础颜色与表面参数，不依赖另行下载的图片。

修改源文件后可自行另存；若希望每次运行脚本都保留修改，应同时更新对应生成脚本。重新执行脚本会覆盖该模型的源文件、清单、导出和预览。

## 重建

已使用的版本为 Blender 5.2.3 LTS。从项目根目录运行以下 PowerShell 命令；换机器时将 `$blenderExe` 改为当地 Blender 的实际路径。

```powershell
$blenderExe = 'D:\SteamLibrary\steamapps\common\Blender\blender.exe'

# 石墨色数码钢琴
& $blenderExe --background --python '.\ArtSource\UdonMidiPlayer\build_models.py'

# 白色三角钢琴
& $blenderExe --background --python '.\ArtSource\UdonMidiPlayer\build_white_grand.py'
```

两个脚本分别生成对应模型；不需要同时重建。最终造型以 `.blend` 和对应预览为准，统计数据以各自清单为准。
