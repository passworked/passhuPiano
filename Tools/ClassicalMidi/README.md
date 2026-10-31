# 古典 MIDI 收集器

本轮下载持续最多 8 小时，并在达到 1,000,000 个有效、SHA256 去重 MIDI 时提前停止；两者以先到者为准。数量包含本批次已经保存的有效 MIDI，ZIP、目录页、来源别名和重复文件不占名额。数据放在 `Downloads/ClassicalMidi/<批次>/`，不会触发 Unity Assets 批量导入。

来源：

- Mutopia：按 Baroque、Classical、Romantic、Renaissance、Technique 目录发现，逐曲保存公有领域 / Creative Commons 许可和制谱者。
- Classical Piano MIDI Page（piano-midi.de）：Bernd Krueger，CC BY-SA 3.0 Germany；遵守 robots.txt 禁止路径。
- MAESTRO v3.0.0：Google 官方 MIDI 专用 ZIP，校验官网公布的 SHA256；CC BY-NC-SA 4.0。
- ASAP：固定官方仓库提交 `afc815c75c42e83a79c03feb6da8a35e77d4c6b8`；CC BY-NC-SA 4.0。

每站至少间隔 1.1 秒，遵守 robots.txt / Crawl-delay / Retry-After。网络异常指数退避；不绕过登录、付费或下载限制。ZIP 支持续传；下载的 MIDI 原始字节不作修改，校验 SMF 音轨和事件边界并记录 Unity 兼容性。相同 SHA256 只保留一份文件，来源别名仍保留。

启动：在 PowerShell 中执行 `& .\Tools\ClassicalMidi\start.ps1`。启动器使用隐藏窗口，首次启动写下固定终止时间；重启同批次不会重新计算 8 小时。可用 `-OutputDirectory '<原批次绝对路径>'` 恢复。OS 文件锁阻止同批次重复进程。

批次目录：

- `midi/`：按来源分组的去重 MIDI。
- `status.json`：实时进度、进程 ID、起止时间、各来源计数、排队/失败任务。
- `manifest.jsonl`：每行一条完整来源、许可、标题、作曲者、哈希和本地路径；重复来源也有记录。
- `state.sqlite`：断点、任务及内容索引。
- `archives/`：原始整包与未完成的 `.partial`。
- `metadata/`、`evidence/`：原始目录页、robots、整包自带的许可/目录元数据。
- `collector.log`、`stderr.log`：运行及错误日志。
- `rejected.jsonl`：整包内没有通过 SMF 校验的成员。
- `summary.json`：停止后的最终统计。

本批次 `config.json` 中的 `file_limit=1000000` 控制数量上限；保存时在同一锁内检查名额和写入，并发下载也不会突破上限。`stop_reason=file_limit` 表示达到数量上限，`deadline` 表示原定时间到期。重启读取已有数量和原始截止时间。

提前停止：在该批次目录创建名为 `STOP` 的空文件。恢复前移除该文件。正常到期自行停止；目录早于到期下载完毕时每小时复查目录、继续重试临时故障和下载新增文件，不重复下载已完成 MIDI。

电脑需保持开机和联网；休眠会暂停后台工作，恢复时若已过终止时间便结束。下载许可不等于可以直接打包分发：尤其 MAESTRO / ASAP 仅允许非商业使用，公开使用应按各曲来源记录履行署名等条件。
