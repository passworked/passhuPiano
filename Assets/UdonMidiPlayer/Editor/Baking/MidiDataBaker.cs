using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Baking
{
    public static class MidiDataBaker
    {
        public static void ValidateSong(MidiSongAsset source, bool checkSource = true)
        {
            if (source == null) throw new InvalidOperationException("曲库存在空曲目");
            if (source.schemaVersion != 2 || source.durationMs < 0) throw new InvalidOperationException("曲目数据版本或时长无效（旧曲目须重新导入）: " + source.name);
            if (source.importFailed) throw new InvalidOperationException("源 MIDI 更新未成功应用: " + source.lastImportError);
            if(source.expressionMs!=null && source.expressionMs.Length>0)
            {
                int count=source.expressionMs.Length;
                if(source.expressionValue==null || source.expressionValue.Length!=count || source.expressionOffsets==null
                    || source.expressionOffsets.Length!=17 || source.expressionOffsets[0]!=0 || source.expressionOffsets[16]!=count)
                    throw new InvalidOperationException("表达时间线数组无效");
                for(int channel=0;channel<16;channel++)
                {
                    int first=source.expressionOffsets[channel],last=source.expressionOffsets[channel+1];
                    if(first<0 || last<first || last>count) throw new InvalidOperationException("表达时间线边界无效");
                    for(int i=first;i<last;i++)
                        if(source.expressionMs[i]<0 || source.expressionMs[i]>source.durationMs || (i>first && source.expressionMs[i]<source.expressionMs[i-1])
                            || source.expressionValue[i]<0 || source.expressionValue[i]>127) throw new InvalidOperationException("表达时间线数值无效");
                }
            }
            if (string.IsNullOrEmpty(source.sourceHash) || source.sourceHash.Length != 64)
                throw new InvalidOperationException("源MIDI SHA256缺失，请重新导入: " + source.name);
            foreach (char digit in source.sourceHash)
                if (!Uri.IsHexDigit(digit)) throw new InvalidOperationException("源MIDI SHA256格式无效: " + source.name);
            if (source.measureStartMs != null && source.measureStartMs.Length > 0)
            {
                int[] grid = source.measureStartMs;
                if (grid[0] != 0 || grid[grid.Length - 1] != source.durationMs) throw new InvalidOperationException("小节边界端点无效: " + source.name);
                for (int i = 1; i < grid.Length; i++)
                    if (grid[i] <= grid[i - 1]) throw new InvalidOperationException("小节边界必须递增: " + source.name);
            }
            int n = source.startMs == null ? 0 : source.startMs.Length;
            if (n == 0) throw new InvalidOperationException("曲目没有音符: " + source.name);
            int[][] arrays = { source.keyUpMs, source.releaseMs, source.pitch, source.velocity, source.channel, source.trackIndex };
            foreach (int[] data in arrays) if (data == null || data.Length != n) throw new InvalidOperationException("曲目音符数组长度不一致");
            for (int i = 0; i < n; i++)
            {
                if (source.startMs[i] < 0 || source.startMs[i] > source.keyUpMs[i] || source.keyUpMs[i] > source.releaseMs[i] || source.releaseMs[i] > source.durationMs
                    || source.pitch[i] < 0 || source.pitch[i] > 127 || source.velocity[i] < 1 || source.velocity[i] > 127
                    || source.channel[i] < 0 || source.channel[i] > 15 || source.trackIndex[i] < 0 || (i > 0 && source.startMs[i] < source.startMs[i - 1]))
                    throw new InvalidOperationException("曲目音符区间/数值无效: noteId=" + i);
            }
            int events = checked(n * 3);
            ValidateTimbreTimeline(source, events);
            if (source.eventMs == null || source.eventMs.Length != events || source.eventKind == null || source.eventKind.Length != events
                || source.eventNoteId == null || source.eventNoteId.Length != events) throw new InvalidOperationException("曲目事件数组长度无效");
            int[] phases = new int[n];
            for (int e = 0; e < events; e++)
            {
                int id = source.eventNoteId[e], kind = source.eventKind[e], time = source.eventMs[e];
                if (id < 0 || id >= n || kind < 0 || kind > 2 || time < 0 || time > source.durationMs || (e > 0 && time < source.eventMs[e - 1]))
                    throw new InvalidOperationException("曲目事件索引/时间无效: event=" + e);
                if (phases[id] != kind) throw new InvalidOperationException("同音符事件因果次序无效: noteId=" + id);
                int expected = kind == 0 ? source.startMs[id] : kind == 1 ? source.keyUpMs[id] : source.releaseMs[id];
                if (time != expected) throw new InvalidOperationException("事件时间与音符数组不一致: event=" + e);
                phases[id]++;
            }
            int k = source.durationMs / 1000 + 1;
            if (source.checkpointEventIndex == null || source.checkpointEventIndex.Length != k || source.checkpointActiveOffsets == null
                || source.checkpointActiveOffsets.Length != k + 1 || source.checkpointActiveNoteIds == null || source.checkpointActiveOffsets[0] != 0
                || source.checkpointActiveOffsets[k] != source.checkpointActiveNoteIds.Length) throw new InvalidOperationException("检查点 CSR 长度/端点无效");
            int cursor = 0;
            var active = new SortedSet<int>();
            for (int c = 0; c < k; c++)
            {
                int time = c * 1000;
                while (cursor < events && source.eventMs[cursor] < time)
                {
                    if (source.eventKind[cursor] == 0) active.Add(source.eventNoteId[cursor]);
                    else if (source.eventKind[cursor] == 2) active.Remove(source.eventNoteId[cursor]);
                    cursor++;
                }
                int begin = source.checkpointActiveOffsets[c], end = source.checkpointActiveOffsets[c + 1];
                if (source.checkpointEventIndex[c] != cursor || begin < 0 || end < begin || end > source.checkpointActiveNoteIds.Length || end - begin != active.Count)
                    throw new InvalidOperationException("检查点索引或活动数量无效: " + c);
                foreach (int id in active)
                    if (source.checkpointActiveNoteIds[begin++] != id) throw new InvalidOperationException("检查点活动集合与音符区间不一致: " + c);
            }
            ValidatePedalTimeline(source);
            if (!checkSource) return;
            if (string.IsNullOrEmpty(source.songId) || string.IsNullOrEmpty(source.contentHash)) throw new InvalidOperationException("曲目身份或内容哈希为空");
            if (source.importedSettingsHash != MidiContentHash.Settings(source.importSettings)) throw new InvalidOperationException("轨道设置已改变，请重新导入: " + source.title);
            if (source.contentHash != MidiContentHash.Song(source)) throw new InvalidOperationException("曲目数据或标题已改变，请重新导入: " + source.title);
            string path = AssetDatabase.GUIDToAssetPath(source.sourceGuid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new InvalidOperationException("源 MIDI 已缺失: " + source.title);
            if (MidiContentHash.Bytes(File.ReadAllBytes(path)) != source.sourceHash) throw new InvalidOperationException("源 MIDI 已更新，请重新导入: " + source.title);
        }

        public static void ValidateLibrary(MidiLibraryAsset library)
        {
            if (library == null || library.songs == null || library.songs.Length == 0) throw new InvalidOperationException("请选择非空曲库");
            var ids = new HashSet<string>();
            foreach (MidiSongAsset song in library.songs)
            {
                ValidateSong(song);
                if (!ids.Add(song.songId)) throw new InvalidOperationException("曲库有重复 songId: " + song.songId);
            }
            if (!string.IsNullOrEmpty(library.defaultSongId) && !ids.Contains(library.defaultSongId)) throw new InvalidOperationException("默认 songId 不在曲库内");
            ValidateFolders(library);
        }

        public static void ValidateFolders(MidiLibraryAsset library)
        {
            if (library == null) throw new InvalidOperationException("请选择曲库");
            if (library.folders == null || library.folders.Length == 0) return;
            var catalogSongs = new HashSet<MidiSongAsset>(library.songs ?? Array.Empty<MidiSongAsset>());
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < library.folders.Length; i++)
            {
                MidiLibraryFolder folder = library.folders[i];
                string context = "收藏夹目录 " + (i + 1);
                if (folder == null || string.IsNullOrWhiteSpace(folder.path))
                    throw new InvalidOperationException(context + " 的路径不能为空");
                if (folder.path.IndexOf('\\') >= 0)
                    throw new InvalidOperationException(context + " 请使用 / 分隔层级，例如：游戏音乐/Arcaea");
                string[] segments = folder.path.Split('/');
                for (int segment = 0; segment < segments.Length; segment++)
                {
                    if (string.IsNullOrWhiteSpace(segments[segment]))
                        throw new InvalidOperationException(context + " 的路径不能以 / 开头或结尾，也不能包含连续的 /：" + folder.path);
                    if (segments[segment] != segments[segment].Trim())
                        throw new InvalidOperationException(context + " 的各级名称首尾不能有空白：" + folder.path);
                    foreach (char character in segments[segment])
                        if (char.IsControl(character))
                            throw new InvalidOperationException(context + " 的路径不能包含换行或控制字符");
                }
                if (!paths.Add(folder.path))
                    throw new InvalidOperationException("收藏夹目录路径重复：" + folder.path);
                var folderSongs = new HashSet<MidiSongAsset>();
                if (folder.songs == null) continue;
                foreach (MidiSongAsset song in folder.songs)
                {
                    if (song == null) throw new InvalidOperationException("收藏夹目录“" + folder.path + "”存在空曲目");
                    if (!catalogSongs.Contains(song))
                        throw new InvalidOperationException("收藏夹目录“" + folder.path + "”中的曲目不在完整曲库内：" + song.title);
                    if (!folderSongs.Add(song))
                        throw new InvalidOperationException("收藏夹目录“" + folder.path + "”中有重复曲目：" + song.title);
                }
            }
        }

        private static void ValidateTimbreTimeline(MidiSongAsset source, int events)
        {
            int count = source.timbreMs == null ? -1 : source.timbreMs.Length;
            if (count < 0 || source.timbreChannel == null || source.timbreKind == null || source.timbreValue == null ||
                source.timbreTrackIndex == null || source.timbreEventIndex == null || source.timbreChannel.Length != count ||
                source.timbreKind.Length != count || source.timbreValue.Length != count || source.timbreTrackIndex.Length != count ||
                source.timbreEventIndex.Length != count || source.timbreDataVersion < 0 || source.timbreDataVersion > 1 ||
                (source.timbreDataVersion == 0 && count != 0)) throw new InvalidOperationException("音色时间线版本或数组无效");
            for (int i = 0; i < count; i++)
            {
                int boundary = source.timbreEventIndex[i], time = source.timbreMs[i];
                if (time < 0 || time > source.durationMs || source.timbreChannel[i] < 0 || source.timbreChannel[i] > 15 ||
                    source.timbreKind[i] < 0 || source.timbreKind[i] > 2 || source.timbreValue[i] < 0 || source.timbreValue[i] > 127 ||
                    source.timbreTrackIndex[i] < 0 || boundary < 0 || boundary > events ||
                    (i > 0 && (time < source.timbreMs[i - 1] || boundary < source.timbreEventIndex[i - 1])))
                    throw new InvalidOperationException("音色时间线数值或顺序无效: " + i);
            }
        }

        private static void ValidatePedalTimeline(MidiSongAsset source)
        {
            if (source.pedalMs == null || source.pedalController == null || source.pedalValue == null || source.pedalOffsets == null ||
                source.pedalOffsets.Length != 4 || source.pedalController.Length != source.pedalMs.Length ||
                source.pedalValue.Length != source.pedalMs.Length || source.pedalOffsets[0] != 0 || source.pedalOffsets[3] != source.pedalMs.Length)
                throw new InvalidOperationException("踏板动画数组/CSR端点无效，请重新导入: " + source.title);
            for (int pedal = 0; pedal < 3; pedal++)
            {
                int begin = source.pedalOffsets[pedal], end = source.pedalOffsets[pedal + 1];
                if (begin < 0 || end < begin || end > source.pedalMs.Length) throw new InvalidOperationException("踏板动画CSR分段越界: " + pedal);
                int controller = pedal == 0 ? 67 : pedal == 1 ? 66 : 64;
                int lastValue = 0;
                for (int i = begin; i < end; i++)
                {
                    if (source.pedalController[i] != controller || source.pedalMs[i] < 0 || source.pedalMs[i] > source.durationMs ||
                        (i > begin && source.pedalMs[i] <= source.pedalMs[i - 1]) || source.pedalValue[i] < 0 || source.pedalValue[i] > 127 || source.pedalValue[i] == lastValue)
                        throw new InvalidOperationException("踏板动画时间/控制器/值无效或未去重: pedal=" + pedal + ", event=" + i);
                    lastValue = source.pedalValue[i];
                }
            }
        }

        public static void PrepareBank(PianoBankAsset bank)
        {
            if (bank == null || bank.clips == null || bank.clips.Length == 0) throw new InvalidOperationException("音源没有采样");
            int count = bank.clips.Length;
            if ((bank.velocityLayerCount != 1 && bank.velocityLayerCount != 3 && bank.velocityLayerCount != 4) || count % bank.velocityLayerCount != 0)
                throw new InvalidOperationException("Velocity layers must be 1, 3 or 4 with equal region counts.");
            int perLayer = count / bank.velocityLayerCount;
            if (bank.rootPitch == null || bank.rootPitch.Length != count) throw new InvalidOperationException("音源根音数量不一致");
            bool explicitKeyRanges = bank.lowPitch != null && bank.lowPitch.Length == count && bank.highPitch != null && bank.highPitch.Length == count;
            if (!explicitKeyRanges) { bank.lowPitch = new int[count]; bank.highPitch = new int[count]; }
            bank.sourceRate = new int[count]; bank.sourceChannels = new int[count]; bank.sourceFrameCount = new int[count];
            bank.sourcePeak = new float[count]; bank.pitchToRegion = new int[88];
            if (bank.tuningCents == null || bank.tuningCents.Length != count) throw new InvalidOperationException("音源必须显式提供每区域 tuningCents；无调音的合成源也须填 0");
            if (bank.regionGain == null || bank.regionGain.Length != count) { bank.regionGain = new float[count]; for (int i = 0; i < count; i++) bank.regionGain[i] = 1; }
            if (bank.releaseDurationMs == null || bank.releaseDurationMs.Length != count) { bank.releaseDurationMs = new int[count]; for (int i = 0; i < count; i++) bank.releaseDurationMs[i] = 180; }
            for (int i = 0; i < count; i++)
            {
                if (bank.rootPitch[i] < 21 || bank.rootPitch[i] > 108 || (i % perLayer > 0 && bank.rootPitch[i] <= bank.rootPitch[i - 1])) throw new InvalidOperationException("根音须在21～108内严格升序");
                if (!explicitKeyRanges)
                {
                    bank.lowPitch[i] = i % perLayer == 0 ? 21 : (bank.rootPitch[i - 1] + bank.rootPitch[i]) / 2 + 1;
                    bank.highPitch[i] = i % perLayer == perLayer - 1 ? 108 : (bank.rootPitch[i] + bank.rootPitch[i + 1]) / 2;
                }
                AudioClip clip = bank.clips[i];
                if (clip == null || clip.samples <= 0) throw new InvalidOperationException("音源区域采样为空: " + i);
                bank.sourceRate[i] = clip.frequency; bank.sourceChannels[i] = clip.channels; bank.sourceFrameCount[i] = clip.samples;
                if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) throw new InvalidOperationException("无法载入采样: " + clip.name);
                float[] samples = new float[checked(clip.samples * clip.channels)];
                if (!clip.GetData(samples, 0)) throw new InvalidOperationException("采样必须支持解码读取，请使用Decompress On Load: " + clip.name);
                float peak = 0;
                foreach (float value in samples)
                {
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("采样包含无效浮点数: " + clip.name);
                    peak = Math.Max(peak, Math.Abs(value));
                }
                bank.sourcePeak[i] = peak;
                if (i < perLayer)
                    for (int p = bank.lowPitch[i]; p <= bank.highPitch[i]; p++) bank.pitchToRegion[p - 21] = i;
            }
            bank.maxReleaseMs = 0;
            foreach (int release in bank.releaseDurationMs) bank.maxReleaseMs = Math.Max(bank.maxReleaseMs, release);
            ValidateBank(bank, false);
            bank.bankHash = MidiContentHash.Bank(bank);
            EditorUtility.SetDirty(bank);
        }

        public static void ValidateBank(PianoBankAsset bank, bool checkHash = true)
        {
            if (bank == null || bank.clips == null || bank.clips.Length == 0) throw new InvalidOperationException("请选择有效钢琴音源");
            int count = bank.clips.Length;
            if ((bank.velocityLayerCount != 1 && bank.velocityLayerCount != 3 && bank.velocityLayerCount != 4) || count % bank.velocityLayerCount != 0)
                throw new InvalidOperationException("Velocity layers must be 1, 3 or 4 with equal region counts.");
            int perLayer = count / bank.velocityLayerCount;
            int[][] integerArrays = { bank.rootPitch, bank.lowPitch, bank.highPitch, bank.sourceRate, bank.sourceChannels, bank.sourceFrameCount, bank.releaseDurationMs };
            foreach (int[] array in integerArrays) if (array == null || array.Length != count) throw new InvalidOperationException("钢琴音源数组长度不一致");
            if (bank.sourcePeak == null || bank.sourcePeak.Length != count || bank.regionGain == null || bank.regionGain.Length != count || bank.tuningCents == null || bank.tuningCents.Length != count || bank.pitchToRegion == null || bank.pitchToRegion.Length != 88)
                throw new InvalidOperationException("音源增益或映射数组无效");
            int maxRelease = 0;
            for (int i = 0; i < count; i++)
            {
                if (bank.clips[i] == null || bank.rootPitch[i] < 21 || bank.rootPitch[i] > 108 || bank.lowPitch[i] > bank.highPitch[i]
                    || bank.lowPitch[i] != (i % perLayer == 0 ? 21 : bank.highPitch[i - 1] + 1) || (i % perLayer == perLayer - 1 && bank.highPitch[i] != 108)
                    || (i % perLayer > 0 && bank.rootPitch[i] <= bank.rootPitch[i - 1])
                    || bank.sourceRate[i] != bank.clips[i].frequency || bank.sourceChannels[i] != bank.clips[i].channels || bank.sourceFrameCount[i] != bank.clips[i].samples
                    || bank.sourceFrameCount[i] <= 0 || bank.releaseDurationMs[i] <= 0
                    || float.IsNaN(bank.tuningCents[i]) || float.IsInfinity(bank.tuningCents[i]) || Math.Abs(bank.tuningCents[i]) > 1200
                    || !FiniteNonNegative(bank.sourcePeak[i]) || !FiniteNonNegative(bank.regionGain[i])) throw new InvalidOperationException("钢琴区域数值/采样元数据无效: " + i);
                maxRelease = Math.Max(maxRelease, bank.releaseDurationMs[i]);
            }
            for (int layer = 0; layer < bank.velocityLayerCount; layer++)
            for (int p = 21; p <= 108; p++)
            {
                if (bank.pitchToRegion[p - 21] < 0 || bank.pitchToRegion[p - 21] >= perLayer) throw new InvalidOperationException("Invalid base-layer region.");
                int region = bank.pitchToRegion[p - 21] + layer * perLayer;
                if (region < 0 || region >= count || p < bank.lowPitch[region] || p > bank.highPitch[region] || Mathf.Pow(2f, (p - bank.rootPitch[region]) / 12f + bank.tuningCents[region] / 1200f) > 3f)
                    throw new InvalidOperationException("音源映射缺失或播放倍率超过3: " + p);
            }
            for (int layer = 0; layer < bank.velocityLayerCount; layer++)
            for (int edge = 0; edge < 2; edge++)
            {
                int pitch = edge == 0 ? 0 : 127;
                int region = bank.pitchToRegion[edge == 0 ? 0 : 87] + layer * perLayer;
                float ratio = Mathf.Pow(2f, (pitch - bank.rootPitch[region]) / 12f + bank.tuningCents[region] / 1200f);
                if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 3f)
                    throw new InvalidOperationException("边缘采样无法准确覆盖 MIDI " + pitch + ": pitchRatio=" + ratio + "，须大于0且不超过AudioSource的3倍上限；请调整边缘根音或调音配置");
            }
            if (maxRelease != bank.maxReleaseMs) throw new InvalidOperationException("maxReleaseMs 与区域释放配置不一致");
            if (checkHash && bank.bankHash != MidiContentHash.Bank(bank)) throw new InvalidOperationException("音源配置已改变，请重新准备音源");
        }

        public static void CopySongToRuntime(MidiSongAsset source, MidiSongData target)
        {
            ValidateSong(source);
            byte[] packed = MidiSongCodec.Encode(source);
            MidiSongCodec.ValidateRoundTrip(source, packed);
            // Stable song IDs keep existing .bytes GUIDs and runtime references across reimports.
            if (source.songId.Length != 32) throw new InvalidOperationException("曲目 songId 不是有效资源 GUID");
            foreach (char digit in source.songId)
                if (!Uri.IsHexDigit(digit)) throw new InvalidOperationException("曲目 songId 不是有效资源 GUID");
            MusicMidiPlayer.Editor.Import.MidiImportService.EnsureFolder(MidiSongCodec.RuntimeOutputFolder);
            string packedPath = MidiSongCodec.RuntimeOutputFolder + "/" + source.songId + ".bytes";
            bool changed = !File.Exists(packedPath);
            if (!changed)
            {
                byte[] previous = File.ReadAllBytes(packedPath);
                changed = previous.Length != packed.Length;
                for (int i = 0; !changed && i < packed.Length; i++) changed = previous[i] != packed[i];
            }
            if (changed) File.WriteAllBytes(packedPath, packed);
            if (changed || AssetDatabase.LoadAssetAtPath<TextAsset>(packedPath) == null)
                AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextAsset packedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(packedPath);
            if (packedAsset == null) throw new InvalidOperationException("无法导入紧凑曲目数据: " + packedPath);
            target.songId = source.songId; target.title = source.title; target.contentHash = source.contentHash;
            target.author = source.author ?? "";
            target.sourceHash = source.sourceHash;
            target.schemaVersion = source.schemaVersion; target.durationMs = source.durationMs;
            target.trackNames = source.trackNames;
            target.measureStartMs = source.measureStartMs;
            if (target.measureStartMs == null || target.measureStartMs.Length == 0)
            {
                string midiPath = AssetDatabase.GUIDToAssetPath(source.sourceGuid);
                if (string.IsNullOrEmpty(midiPath)) midiPath = source.sourcePath;
                target.measureStartMs = MusicMidiPlayer.Editor.Import.SmfParser.Parse(File.ReadAllBytes(midiPath), source.importSettings).measureStartMs;
            }
            target.packedData = packedAsset; target.packedFormatVersion = MidiSongCodec.FormatVersion;
            target.noteCount = source.startMs.Length; target.packedDataBytes = packed.Length;
            target.ReleaseDecodedData();
            // NonSerialized runtime arrays must also shed overrides from older prefab/scene bakes.
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            if (backing != null && backing.publicVariables != null)
            {
                string[] oldArrays = { "startMs", "keyUpMs", "releaseMs", "pitch", "velocity", "channel", "trackIndex",
                    "eventMs", "eventKind", "eventNoteId", "checkpointEventIndex", "checkpointActiveOffsets",
                    "checkpointActiveNoteIds", "pedalMs", "pedalController", "pedalValue", "pedalOffsets" };
                foreach (string field in oldArrays) backing.publicVariables.RemoveVariable(field);
                EditorUtility.SetDirty(backing);
            }
            if (source.packedDataBytes != packed.Length)
            {
                source.packedDataBytes = packed.Length;
                EditorUtility.SetDirty(source);
            }
        }

        public static void CopyBankToRuntime(PianoBankAsset source, PianoBankData target)
        {
            ValidateBank(source);
            target.velocityLayerCount = source.velocityLayerCount;
            target.bankHash = source.bankHash; target.clips = (AudioClip[])source.clips.Clone();
            target.rootPitch = Clone(source.rootPitch); target.lowPitch = Clone(source.lowPitch); target.highPitch = Clone(source.highPitch);
            target.sourceRate = Clone(source.sourceRate); target.sourceChannels = Clone(source.sourceChannels); target.sourceFrameCount = Clone(source.sourceFrameCount);
            target.sourcePeak = (float[])source.sourcePeak.Clone(); target.regionGain = (float[])source.regionGain.Clone();
            target.tuningCents = (float[])source.tuningCents.Clone();
            target.releaseDurationMs = Clone(source.releaseDurationMs); target.pitchToRegion = Clone(source.pitchToRegion); target.maxReleaseMs = source.maxReleaseMs;
        }

        public static string ComputeCatalogHash(MidiLibraryAsset library, PianoBankAsset bank)
        { ValidateLibrary(library); ValidateBank(bank); return MidiContentHash.Catalog(library, bank); }

        private static int[] Clone(int[] source) { return (int[])source.Clone(); }
        private static bool FiniteNonNegative(float value) { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0; }
    }
}
