using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Prefab;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Import
{
    /// <summary>Explicit, resumable metadata backfill; never rebuilds a library or changes its membership.</summary>
    public static class MidiTimbreMigration
    {
        public const string ReportPath = "Logs/MidiTimbreMigration.json";
        [Serializable] public class SongReport
        {
            public string path, guid, songId, title, author, playbackHash, packedGuid;
            public int events, packedBefore, packedAfter, timbreBytes;
        }
        [Serializable] public class Report
        {
            public string started, finished;
            public int processed, events, timbreBytes;
            public bool success;
            public List<SongReport> songs = new List<SongReport>();
            public List<string> updatedContainers = new List<string>();
        }

        public static string Begin()
        {
            Ready();
            var report = new Report { started = DateTime.UtcNow.ToString("o") };
            foreach (string guid in AssetDatabase.FindAssets("t:MidiSongAsset", new[] { "Assets/UdonMidiPlayer" }).OrderBy(s => s, StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(path);
                string source = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
                if (string.IsNullOrEmpty(source)) source = song.sourcePath;
                if (!File.Exists(source)) throw new InvalidOperationException("源 MIDI 缺失: " + path);
                string packed = PackedPath(song.songId);
                report.songs.Add(new SongReport { path = path, guid = guid, songId = song.songId, title = song.title,
                    author = song.author, playbackHash = PlaybackHash(song), packedGuid = AssetDatabase.AssetPathToGUID(packed),
                    packedBefore = File.Exists(packed) ? checked((int)new FileInfo(packed).Length) : 0 });
            }
            Write(report);
            return Summary(report);
        }

        public static string ProcessBatch(int maximum = 25)
        {
            Ready(); var report = Read();
            int end = Math.Min(report.songs.Count, report.processed + Math.Max(1, maximum));
            for (; report.processed < end; report.processed++)
            {
                SongReport row = report.songs[report.processed];
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(row.path);
                CheckIdentity(song, row);
                string source = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
                if (string.IsNullOrEmpty(source)) source = song.sourcePath;
                byte[] original = File.ReadAllBytes(source);
                if (MidiContentHash.Bytes(original) != song.sourceHash)
                    throw new InvalidOperationException("源 MIDI 已变化，不能只补全音色: " + row.path);
                var parsed = SmfParser.Parse(original, song.importSettings);
                if (!SameNotes(song, parsed)) throw new InvalidOperationException("重新解析的音符与现有数据不同，停止补全以保护发声: " + row.path);
                var staged = UnityEngine.Object.Instantiate(song);
                try
                {
                    staged.timbreDataVersion = 1;
                    staged.estimatedDataBytes += (parsed.timbreMs.Length - staged.timbreMs.Length) * 24L;
                    staged.timbreMs = parsed.timbreMs; staged.timbreChannel = parsed.timbreChannel; staged.timbreKind = parsed.timbreKind;
                    staged.timbreValue = parsed.timbreValue; staged.timbreTrackIndex = parsed.timbreTrackIndex; staged.timbreEventIndex = parsed.timbreEventIndex;
                    staged.diagnostics = parsed.diagnostics;
                    staged.contentHash = MidiContentHash.Song(staged);
                    MidiDataBaker.ValidateSong(staged);
                    byte[] packed = MidiSongCodec.Encode(staged);
                    MidiSongCodec.ValidateRoundTrip(staged, packed);
                    staged.timbreDataVersion = 0;
                    var times = staged.timbreMs; var channels = staged.timbreChannel; var kinds = staged.timbreKind;
                    var values = staged.timbreValue; var tracks = staged.timbreTrackIndex; var boundaries = staged.timbreEventIndex;
                    staged.timbreMs = staged.timbreChannel = staged.timbreKind = staged.timbreValue = staged.timbreTrackIndex = staged.timbreEventIndex = new int[0];
                    int withoutTimbre = MidiSongCodec.Encode(staged).Length;
                    staged.timbreDataVersion = 1; staged.timbreMs = times; staged.timbreChannel = channels; staged.timbreKind = kinds;
                    staged.timbreValue = values; staged.timbreTrackIndex = tracks; staged.timbreEventIndex = boundaries;
                    staged.packedDataBytes = packed.Length;
                    Undo.RecordObject(song, "Backfill MIDI Timbre Metadata");
                    EditorUtility.CopySerialized(staged, song);
                    EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song);
                    string packedPath = PackedPath(song.songId);
                    File.WriteAllBytes(packedPath, packed);
                    AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    CheckIdentity(song, row);
                    string newGuid = AssetDatabase.AssetPathToGUID(packedPath);
                    if (!string.IsNullOrEmpty(row.packedGuid) && newGuid != row.packedGuid) throw new InvalidOperationException("压缩资源 GUID 改变: " + row.path);
                    row.events = times.Length; row.packedAfter = packed.Length; row.timbreBytes = packed.Length - withoutTimbre;
                    report.events += row.events; report.timbreBytes += row.timbreBytes;
                }
                finally { UnityEngine.Object.DestroyImmediate(staged); }
                // Persist after each committed song so interrupted batches can resume safely.
                report.processed++; Write(report); report.processed--;
            }
            Write(report); return Summary(report);
        }

        public static string UpdateRuntimeMetadata()
        {
            Ready(); var report = Read();
            if (report.processed != report.songs.Count) throw new InvalidOperationException("先完成全部曲目补全");
            var songs = report.songs.ToDictionary(r => r.songId, r => AssetDatabase.LoadAssetAtPath<MidiSongAsset>(r.path));
            // Update only components already present. No library synchronization, new song
            // objects, instance Apply or changes to the distribution catalog are performed.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UdonMidiPlayer/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = PatchRoot(root, songs);
                    if (changed && PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new InvalidOperationException("保存 Prefab 元数据失败: " + path);
                    if (root.GetComponentsInChildren<MidiSongData>(true).Length > 0 && !report.updatedContainers.Contains(path))
                    { report.updatedContainers.Add(path); Write(report); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Scene active = SceneManager.GetActiveScene();
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes", "Assets/UdonMidiPlayer/Samples" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            foreach (string path in paths)
            {
                Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    // Preserve the complete live scene before the incremental metadata save.
                    if (!opened)
                    {
                        Directory.CreateDirectory("RecoveryBackups/TimbreMetadata-20261031");
                        string backup = "RecoveryBackups/TimbreMetadata-20261031/" + scene.name + "-before-metadata.unity";
                        if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true))
                            throw new InvalidOperationException("无法备份已打开场景: " + path);
                    }
                    bool changed = false;
                    foreach (GameObject root in scene.GetRootGameObjects()) changed |= PatchRoot(root, songs);
                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("保存场景元数据失败: " + path);
                        if (!report.updatedContainers.Contains(path)) report.updatedContainers.Add(path);
                        Write(report);
                    }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            foreach (SongReport row in report.songs) CheckIdentity(songs[row.songId], row);
            report.success = true; report.finished = DateTime.UtcNow.ToString("o"); Write(report);
            return Summary(report);
        }

        private static bool PatchRoot(GameObject root, Dictionary<string, MidiSongAsset> sources)
        {
            bool changed = false;
            foreach (var target in root.GetComponentsInChildren<MidiSongData>(true))
            {
                if (string.IsNullOrEmpty(target.songId) || !sources.TryGetValue(target.songId, out var source)) continue;
                if (target.contentHash == source.contentHash && target.packedDataBytes == source.packedDataBytes) continue;
                if (target.packedData == null || AssetDatabase.GetAssetPath(target.packedData) != PackedPath(source.songId))
                    throw new InvalidOperationException("运行歌曲的压缩引用不匹配: " + target.name);
                target.contentHash = source.contentHash; target.packedDataBytes = source.packedDataBytes;
                Persist(target); changed = true;
            }
            foreach (var facade in root.GetComponentsInChildren<MidiPlayerFacade>(true))
            {
                if (facade.library == null || string.IsNullOrEmpty(facade.library.configurationAssetGuid)) continue;
                var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid));
                if (config == null) throw new InvalidOperationException("播放器配置缺失: " + facade.name);
                // Practice and distribution players can intentionally contain subsets of
                // their authoring library. Hash their actual references without replacing them.
                var actualLibrary = ScriptableObject.CreateInstance<MidiLibraryAsset>();
                string hash;
                try
                {
                    actualLibrary.songs = facade.library.songs.Select(s => sources.TryGetValue(s.songId, out var source)
                        ? source : throw new InvalidOperationException("运行歌曲缺少源资产: " + s.songId)).ToArray();
                    hash = MidiInstrumentMigration.InstrumentCatalogHash(facade, MidiContentHash.Catalog(actualLibrary, config.bank));
                }
                finally { UnityEngine.Object.DestroyImmediate(actualLibrary); }
                if (facade.library.catalogHash != hash) { facade.library.catalogHash = hash; Persist(facade.library); changed = true; }
                if (facade.network != null && facade.network.catalogHash != hash)
                { facade.network.catalogHash = hash; Persist(facade.network); changed = true; }
            }
            return changed;
        }

        private static void Persist(UdonSharpBehaviour proxy)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.All);
            EditorUtility.SetDirty(proxy);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (backing != null) EditorUtility.SetDirty(backing);
            if (PrefabUtility.IsPartOfPrefabInstance(proxy)) PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            if (backing != null && PrefabUtility.IsPartOfPrefabInstance(backing)) PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
        }

        private static bool SameNotes(MidiSongAsset song, MidiImportResult result)
        {
            return song.startMs.SequenceEqual(result.startMs) && song.keyUpMs.SequenceEqual(result.keyUpMs) && song.releaseMs.SequenceEqual(result.releaseMs) &&
                song.pitch.SequenceEqual(result.pitch) && song.velocity.SequenceEqual(result.velocity) && song.channel.SequenceEqual(result.channel) &&
                song.trackIndex.SequenceEqual(result.trackIndex) && song.eventMs.SequenceEqual(result.eventMs) &&
                song.eventKind.SequenceEqual(result.eventKind) && song.eventNoteId.SequenceEqual(result.eventNoteId);
        }
        private static string PlaybackHash(MidiSongAsset song)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                foreach (int[] array in new[] { song.startMs, song.keyUpMs, song.releaseMs, song.pitch, song.velocity, song.channel, song.trackIndex,
                    song.eventMs, song.eventKind, song.eventNoteId, song.checkpointEventIndex, song.checkpointActiveOffsets, song.checkpointActiveNoteIds,
                    song.pedalMs, song.pedalController, song.pedalValue, song.pedalOffsets, song.expressionMs, song.expressionValue, song.expressionOffsets })
                { writer.Write(array.Length); foreach (int value in array) writer.Write(value); }
                writer.Flush(); return MidiContentHash.Bytes(stream.ToArray());
            }
        }
        private static void CheckIdentity(MidiSongAsset song, SongReport row)
        {
            if (song == null || song.songId != row.songId || AssetDatabase.AssetPathToGUID(row.path) != row.guid ||
                song.title != row.title || song.author != row.author || PlaybackHash(song) != row.playbackHash)
                throw new InvalidOperationException("曲目身份、文案或既有播放数据改变: " + row.path);
        }
        private static string PackedPath(string id) { return MidiSongCodec.RuntimeOutputFolder + "/" + id + ".bytes"; }
        private static void Ready()
        { if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("等待编译完成并退出播放模式"); }
        private static Report Read() { return JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)); }
        private static void Write(Report report) { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
        private static string Summary(Report report)
        { return "processed=" + report.processed + "/" + report.songs.Count + ", timbreEvents=" + report.events +
            ", timbreBytes=" + report.timbreBytes + ", containers=" + report.updatedContainers.Count + ", success=" + report.success; }
    }
}
