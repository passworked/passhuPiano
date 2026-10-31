using System;
using System.IO;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Import
{
    public static class MidiImportService
    {
        public const string DefaultOutputFolder = "Assets/UdonMidiPlayer/Editor/Generated/Songs";

        public static MidiSongAsset ImportAsset(string sourceAssetPath, string outputFolder = DefaultOutputFolder,
            MidiImportSettings settings = null, MidiSongAsset existing = null)
        {
            sourceAssetPath = ToAssetPath(sourceAssetPath);
            settings = settings ?? (existing != null ? existing.importSettings : new MidiImportSettings());
            settings = JsonUtility.FromJson<MidiImportSettings>(JsonUtility.ToJson(settings ?? new MidiImportSettings()));
            string guid = AssetDatabase.AssetPathToGUID(sourceAssetPath);
            if (existing == null && !string.IsNullOrEmpty(guid)) existing = FindSong(guid, MidiContentHash.Settings(settings));
            MidiSongAsset staged = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(Path.GetFullPath(sourceAssetPath));
                MidiImportResult result = SmfParser.Parse(bytes, settings);
                if (string.IsNullOrEmpty(guid))
                {
                    AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceUpdate);
                    guid = AssetDatabase.AssetPathToGUID(sourceAssetPath);
                }
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("无法取得源 MIDI 的资源 GUID");
                staged = ScriptableObject.CreateInstance<MidiSongAsset>();
                staged.title = existing != null && !string.IsNullOrWhiteSpace(existing.title)
                    ? existing.title : Path.GetFileNameWithoutExtension(sourceAssetPath);
                staged.name = existing != null ? existing.name : staged.title;
                staged.author = existing != null ? existing.author : "";
                staged.songId = existing != null ? existing.songId : "";
                staged.sourceGuid = guid;
                staged.sourcePath = sourceAssetPath;
                staged.sourceHash = MidiContentHash.Bytes(bytes);
                staged.importSettings = settings;
                staged.importedSettingsHash = MidiContentHash.Settings(settings);
                ApplyResult(staged, result);
                staged.contentHash = MidiContentHash.Song(staged);
                MidiDataBaker.ValidateSong(staged, false);
                byte[] packed = MidiSongCodec.Encode(staged);
                MidiSongCodec.ValidateRoundTrip(staged, packed);
                staged.packedDataBytes = packed.Length;
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(existing.sourceGuid) && existing.sourceGuid != guid)
                        throw new InvalidOperationException("不能把另一源文件覆盖到此曲目资产");
                    Undo.RecordObject(existing, "Reimport MIDI Song");
                    string stableId = existing.songId;
                    EditorUtility.CopySerialized(staged, existing);
                    existing.songId = string.IsNullOrEmpty(stableId) ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(existing)) : stableId;
                    UnityEngine.Object.DestroyImmediate(staged);
                    staged = null;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                    return existing;
                }
                EnsureFolder(outputFolder);
                outputFolder = outputFolder.Replace('\\', '/').TrimEnd('/');
                string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder.TrimEnd('/') + "/" + SafeName(staged.title) + ".asset");
                AssetDatabase.CreateAsset(staged, path);
                staged.songId = AssetDatabase.AssetPathToGUID(path);
                EditorUtility.SetDirty(staged);
                AssetDatabase.SaveAssetIfDirty(staged);
                MidiSongAsset created = staged;
                staged = null;
                return created;
            }
            catch (Exception exception)
            {
                if (existing != null)
                {
                    existing.importFailed = true;
                    existing.lastImportError = exception.Message;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                throw;
            }
            finally
            {
                if (staged != null) UnityEngine.Object.DestroyImmediate(staged);
            }
        }

        public static MidiSongAsset Reimport(MidiSongAsset song)
        {
            if (song == null) throw new ArgumentNullException(nameof(song));
            string sourcePath = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
            if (string.IsNullOrEmpty(sourcePath)) sourcePath = song.sourcePath;
            return ImportAsset(sourcePath, Path.GetDirectoryName(AssetDatabase.GetAssetPath(song)).Replace('\\', '/'), song.importSettings, song);
        }

        public static void ApplyResult(MidiSongAsset song, MidiImportResult result)
        {
            song.timbreDataVersion = result.timbreDataVersion;
            song.timbreMs = result.timbreMs; song.timbreChannel = result.timbreChannel; song.timbreKind = result.timbreKind;
            song.timbreValue = result.timbreValue; song.timbreTrackIndex = result.timbreTrackIndex; song.timbreEventIndex = result.timbreEventIndex;
            song.schemaVersion = 2; song.durationMs = result.durationMs;
            song.measureStartMs = result.measureStartMs;
            song.startMs = result.startMs; song.keyUpMs = result.keyUpMs; song.releaseMs = result.releaseMs;
            song.pitch = result.pitch; song.velocity = result.velocity; song.channel = result.channel; song.trackIndex = result.trackIndex;
            song.eventMs = result.eventMs; song.eventKind = result.eventKind; song.eventNoteId = result.eventNoteId;
            song.checkpointEventIndex = result.checkpointEventIndex; song.checkpointActiveOffsets = result.checkpointActiveOffsets;
            song.checkpointActiveNoteIds = result.checkpointActiveNoteIds;
            song.pedalMs = result.pedalMs; song.pedalController = result.pedalController;
            song.pedalValue = result.pedalValue; song.pedalOffsets = result.pedalOffsets;
            song.expressionMs = result.expressionMs; song.expressionValue = result.expressionValue; song.expressionOffsets = result.expressionOffsets;
            song.trackNames = result.trackNames; song.diagnostics = result.diagnostics;
            song.tempoChangeCount = result.tempoChangeCount; song.maxKeyPolyphony = result.maxKeyPolyphony;
            song.maxActivePolyphony = result.maxActivePolyphony; song.estimatedDataBytes = result.estimatedDataBytes;
            song.packedDataBytes = 0;
            song.importFailed = false; song.lastImportError = "";
        }

        public static MidiSongAsset FindSong(string sourceGuid, string settingsHash)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MidiSongAsset"))
            {
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (song != null && song.sourceGuid == sourceGuid && song.importedSettingsHash == settingsHash) return song;
            }
            return null;
        }

        public static string ToAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择 Assets 内的 MIDI 文件");
            string full = Path.GetFullPath(path);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请先将 MIDI 放入工程 Assets 目录，再批量导入");
            return "Assets/" + full.Substring(assets.Length + 1).Replace('\\', '/');
        }

        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("生成资产目录必须在 Assets 下");
            string[] parts = folder.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i] == "." || parts[i] == ".." || parts[i].Length == 0) throw new ArgumentException("生成目录包含无效路径片段");
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string SafeName(string title)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
            return string.IsNullOrWhiteSpace(title) ? "MidiSong" : title.Trim();
        }
    }
}
