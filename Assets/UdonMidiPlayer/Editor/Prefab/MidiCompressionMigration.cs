using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Import;
using MusicMidiPlayer.Logic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Explicit migration of existing assets; never replaces song identities or presentation.</summary>
    public static class MidiCompressionMigration
    {
        [Serializable] public sealed class SongResult
        {
            public string songId, title, assetPath;
            public int notes, packedBytes;
            public long rawBytes;
        }

        [Serializable] public sealed class MigrationReport
        {
            public bool success;
            public string timestamp, message;
            public int reimportedSongs, librarySongs, scenePlayers, prefabPlayers;
            public long libraryRawBytes, libraryPackedBytes;
            public List<SongResult> songs = new List<SongResult>();
            public List<string> savedScenes = new List<string>();
        }

        [MenuItem("Tools/Music MIDI Player/Compress and Reimport Existing Songs")]
        public static void ReimportFromMenu()
        {
            try { Debug.Log(ReimportAndBakeAll()); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        public static string ReimportAndBakeAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请等待编译完成并退出播放模式后重新导入曲库");
            var report = new MigrationReport { timestamp = DateTime.UtcNow.ToString("o") };
            MidiSongCodec.ValidateBoundaryCases();
            string[] guids = AssetDatabase.FindAssets("t:MidiSongAsset", new[] { "Assets/UdonMidiPlayer" });
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MidiSongAsset song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(path);
                if (song == null) throw new InvalidOperationException("无法读取曲目: " + path);
                string stableId = song.songId, stableTitle = song.title;
                MidiImportService.Reimport(song);
                if (song.songId != stableId || song.title != stableTitle || AssetDatabase.AssetPathToGUID(path) != guid)
                    throw new InvalidOperationException("重新导入改变了曲目身份或标题: " + path);
                byte[] packed = MidiSongCodec.Encode(song);
                MidiSongCodec.ValidateRoundTrip(song, packed);
                AssetDatabase.SaveAssetIfDirty(song);
                report.songs.Add(new SongResult { songId = stableId, title = song.title, assetPath = path,
                    notes = song.startMs.Length, rawBytes = song.estimatedDataBytes, packedBytes = packed.Length });
                report.reimportedSongs++;
            }

            // Keep the existing prefab's piano, materials, effect settings and UI placement intact.
            if (File.Exists(MidiPlayerBuilder.PrefabPath))
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(MidiPlayerBuilder.PrefabPath);
                try
                {
                    foreach (MidiPlayerFacade facade in contents.GetComponentsInChildren<MidiPlayerFacade>(true))
                    {
                        MidiPlayerBuilder.SynchronizePlayerLibrary(facade);
                        MidiBuildValidator.ValidateRoot(facade.transform.parent.parent.gameObject);
                        report.prefabPlayers++;
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(contents, MidiPlayerBuilder.PrefabPath) == null)
                        throw new InvalidOperationException("无法保存紧凑曲库 Prefab");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            Scene originalActive = SceneManager.GetActiveScene();
            Scene demo = SceneManager.GetSceneByPath(MidiPlayerBuilder.DemoScenePath);
            bool openedDemo = !demo.IsValid() || !demo.isLoaded;
            if (openedDemo && File.Exists(MidiPlayerBuilder.DemoScenePath))
                demo = EditorSceneManager.OpenScene(MidiPlayerBuilder.DemoScenePath, OpenSceneMode.Additive);
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                    bool changed = false;
                    foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                    {
                        foreach (MidiPlayerFacade facade in sceneRoot.GetComponentsInChildren<MidiPlayerFacade>(true))
                        {
                            MidiPlayerBuilder.SynchronizePlayerLibrary(facade);
                            MidiBuildValidator.ValidateRoot(facade.transform.parent.parent.gameObject);
                            report.scenePlayers++;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        if (string.IsNullOrEmpty(scene.path) || !EditorSceneManager.SaveScene(scene))
                            throw new InvalidOperationException("无法保存曲库场景: " + scene.name);
                        report.savedScenes.Add(scene.path);
                    }
                }
            }
            finally
            {
                if (openedDemo && demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            }

            MidiLibraryAsset library = AssetDatabase.LoadAssetAtPath<MidiLibraryAsset>(MidiPlayerBuilder.DefaultLibraryPath);
            if (library != null)
            {
                report.librarySongs = library.songs.Length;
                foreach (MidiSongAsset song in library.songs)
                {
                    report.libraryRawBytes += song.estimatedDataBytes;
                    report.libraryPackedBytes += song.packedDataBytes;
                }
            }
            report.success = true;
            report.message = "Existing songs reimported with stable IDs; packed data baked into existing players. All 17 playback arrays round-trip exactly.";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MusicMidiPlayer-compression-report.json", json);
            return json;
        }
    }
}
