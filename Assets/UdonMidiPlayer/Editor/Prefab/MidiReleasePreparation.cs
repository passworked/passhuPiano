// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Logic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiReleasePreparation
    {
        private const string LibraryPath = "Assets/UdonMidiPlayer/Editor/Generated/PrefabClassicalLibrary.asset";

        [MenuItem("Tools/Music MIDI Player/Release/Filter Distribution Repertoire")]
        public static void PrepareFromMenu() { Debug.Log(PrepareDistribution()); }

        public static string PrepareDistribution()
        {
            CheckIdle();
            var policy = MidiReleasePolicy.Load();
            var library = AssetDatabase.LoadAssetAtPath<MidiLibraryAsset>(LibraryPath);
            if (library == null) throw new InvalidOperationException("Missing independent distribution library");
            var filtered = FilterLibrary(library, policy);
            foreach (var song in filtered.songs) policy.ValidateSong(song);
            var roots = new List<GameObject>();
            var oldSongs = library.songs;
            var oldFolders = library.folders;
            string oldDefault = library.defaultSongId;
            bool committed = false;
            try
            {
                foreach (string prefab in MidiPackageExporter.PrefabPaths) roots.Add(PrefabUtility.LoadPrefabContents(prefab));
                library.songs = filtered.songs;
                library.folders = filtered.folders;
                library.defaultSongId = filtered.defaultSongId;
                foreach (GameObject root in roots)
                {
                    var facade = root.GetComponentInChildren<MidiPlayerFacade>(true);
                    string configPath = AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid);
                    var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(configPath);
                    if (config == null || config.library != library) throw new InvalidOperationException("Prefab does not use its independent release library: " + root.name);
                    if (!string.IsNullOrEmpty(config.defaultSongId) && !library.songs.Any(x => x.songId == config.defaultSongId))
                        throw new InvalidOperationException("Distribution default needs an explicit permitted replacement: " + configPath);
                    RebakeSelectedBank(facade, config);
                    MidiPlayerBuilder.SynchronizePlayerLibrary(facade);
                    policy.ValidateRoot(root);
                    MidiBuildValidator.ValidateRoot(root);
                }
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                for (int i = 0; i < roots.Count; i++)
                    if (PrefabUtility.SaveAsPrefabAsset(roots[i], MidiPackageExporter.PrefabPaths[i]) == null)
                        throw new IOException("Failed to save release Prefab");
                committed = true;
                return "Distribution repertoire: " + library.songs.Length + " permitted songs. Main scene and scene libraries were not saved or edited.";
            }
            finally
            {
                if (!committed)
                {
                    library.songs = oldSongs; library.folders = oldFolders; library.defaultSongId = oldDefault;
                    EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
                }
                foreach (GameObject root in roots) PrefabUtility.UnloadPrefabContents(root);
                UnityEngine.Object.DestroyImmediate(filtered);
            }
        }

        internal static MidiLibraryAsset FilterLibrary(MidiLibraryAsset original, MidiReleasePolicy policy)
        {
            var result = UnityEngine.Object.Instantiate(original);
            result.name = original.name;
            result.songs = original.songs.Where(policy.Approves).ToArray();
            if (result.songs.Length == 0)
                result.songs = new[] { AssetDatabase.LoadAssetAtPath<MidiSongAsset>(policy.songs[0].path) };
            var accepted = new HashSet<MidiSongAsset>(result.songs);
            result.folders = (original.folders ?? new MidiLibraryFolder[0]).Where(x => x != null)
                .Select(x => new MidiLibraryFolder { path = x.path, songs = x.songs.Where(accepted.Contains).ToArray() })
                .Where(x => x.songs.Length > 0).ToArray();
            result.defaultSongId = result.songs.Any(x => x.songId == original.defaultSongId)
                ? original.defaultSongId : result.songs[0].songId;
            return result;
        }

        public static string PreparePublicSourceAssets()
        {
            CheckIdle();
            var policy = MidiReleasePolicy.Load();
            string output = MidiPackageExporter.OutputDirectory + "/source/musicMidiPlayer";
            string manifestPath = MidiPackageExporter.OutputDirectory + "/source-assets.json";
            if (File.Exists(manifestPath)) throw new IOException("Source candidate already prepared; preserve it and use a new candidate VERSION before regenerating.");
            Directory.CreateDirectory(output);
            var assets = new SortedSet<string>(MidiPackageExporter.CollectAssets(), StringComparer.Ordinal);
            var overrides = new HashSet<string>(StringComparer.Ordinal);
            var libraries = new Dictionary<string, MidiLibraryAsset>(StringComparer.Ordinal);
            var configurations = new Dictionary<string, MidiPlayerConfigurationAsset>(StringComparer.Ordinal);
            string temp = "Assets/UdonMidiPlayer/Editor/ReleaseStaging_" + Guid.NewGuid().ToString("N");
            Scene active = SceneManager.GetActiveScene();
            bool activeDirty = active.isDirty;
            try
            {
                // Serialize filtered source assets outside Assets with their original references;
                // the snapshot copier retains original .meta files, never clone GUIDs.
                foreach (string guid in AssetDatabase.FindAssets("t:MidiLibraryAsset", new[] { "Assets/UdonMidiPlayer/Editor/Generated" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var original = AssetDatabase.LoadAssetAtPath<MidiLibraryAsset>(path);
                    var filtered = FilterLibrary(original, policy);
                    libraries.Add(path, filtered);
                    Serialize(filtered, output + "/" + path);
                    overrides.Add(path); assets.Add(path);
                    foreach (var song in filtered.songs) assets.Add(AssetDatabase.GetAssetPath(song));
                }
                foreach (string guid in AssetDatabase.FindAssets("t:MidiPlayerConfigurationAsset", new[] { "Assets/UdonMidiPlayer/Editor/Generated" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var original = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(path);
                    string libraryPath = AssetDatabase.GetAssetPath(original.library);
                    MidiLibraryAsset filtered;
                    if (!libraries.TryGetValue(libraryPath, out filtered)) throw new InvalidOperationException("Unregistered source library: " + libraryPath);
                    var copy = UnityEngine.Object.Instantiate(original); copy.name = original.name;
                    if (!filtered.songs.Any(x => x.songId == copy.defaultSongId)) copy.defaultSongId = filtered.defaultSongId;
                    configurations.Add(path, copy);
                    Serialize(copy, output + "/" + path); // library still references the original GUID
                    overrides.Add(path); assets.Add(path);
                }
                // Copy only on-disk scenes to a unique temporary export location. Never save
                // or replace the user's loaded (possibly dirty) scene or Apply its overrides.
                Directory.CreateDirectory(temp);
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes", "Assets/UdonMidiPlayer/Samples" }))
                {
                    string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                    string tempPath = temp + "/" + Guid.NewGuid().ToString("N") + ".unity";
                    File.Copy(scenePath, tempPath);
                    AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);
                    Scene scene = EditorSceneManager.OpenScene(tempPath, OpenSceneMode.Additive);
                    try
                    {
                        foreach (GameObject root in scene.GetRootGameObjects())
                        {
                            foreach (var facade in root.GetComponentsInChildren<MidiPlayerFacade>(true))
                            {
                                string configPath = AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid);
                                MidiPlayerConfigurationAsset config;
                                if (!configurations.TryGetValue(configPath, out config)) throw new InvalidOperationException("Missing source player configuration: " + configPath);
                                var ephemeral = UnityEngine.Object.Instantiate(config);
                                try
                                {
                                    ephemeral.library = libraries[AssetDatabase.GetAssetPath(config.library)];
                                    RebakeSelectedBank(facade, ephemeral);
                                    MidiPlayerBuilder.SynchronizePlayerLibrary(facade, ephemeral);
                                    policy.ValidateRoot(facade.transform.parent.parent.gameObject);
                                }
                                finally { UnityEngine.Object.DestroyImmediate(ephemeral); }
                            }
                        }
                        // A public copy must not claim ownership of the author's uploaded world.
                        foreach (GameObject root in scene.GetRootGameObjects())
                            foreach (var pipeline in root.GetComponentsInChildren<VRC.Core.PipelineManager>(true))
                                pipeline.blueprintId = "";
                        if (!EditorSceneManager.SaveScene(scene, tempPath)) throw new IOException("Cannot save sanitized export scene");
                        string destination = output + "/" + scenePath;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(tempPath, destination, true);
                        overrides.Add(scenePath); assets.Add(scenePath);
                        // Traverse one edge at a time: baked lighting can reference the
                        // original scene, whose public override is already sanitized.
                        foreach (string dependency in AssetDatabase.GetDependencies(tempPath, false))
                            if (!dependency.StartsWith(temp + "/", StringComparison.Ordinal)) MidiPackageExporter.Add(assets, dependency);
                    }
                    finally { EditorSceneManager.CloseScene(scene, true); }
                }
                // Retain editable/imported artwork, shaders and all non-test project code.
                foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/UdonMidiPlayer", "Assets/Scenes/DemoMusicClassroom" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path) || path.StartsWith(temp, StringComparison.Ordinal) || path.Contains("/RegressionTemp/")
                        || path.Contains("/Editor/Generated/") || path.Contains("/Generated/Songs/") || path.EndsWith(".unity")) continue;
                    if (path.EndsWith(".mid") || path.EndsWith(".midi")) { if (!policy.music.Any(x => x.path == path)) continue; }
                    if (path.Contains("/Audio/") && !policy.audioGroups.Any(x => path.StartsWith(x.prefix, StringComparison.Ordinal))) continue;
                    MidiPackageExporter.Add(assets, path);
                }
                var queue = new Queue<string>(assets);
                while (queue.Count > 0)
                {
                    string path = queue.Dequeue();
                    IEnumerable<string> dependencies;
                    MidiLibraryAsset filtered;
                    if (libraries.TryGetValue(path, out filtered))
                        dependencies = filtered.songs.Select(x => AssetDatabase.GetAssetPath(x)).Concat(new[] { AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(filtered)) });
                    else
                    {
                        if (!overrides.Contains(path)) policy.ValidateAsset(path);
                        dependencies = path.EndsWith(".unity") ? new string[0] : AssetDatabase.GetDependencies(path, false);
                    }
                    var source = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(path);
                    if (source != null) dependencies = dependencies.Concat(new[] { AssetDatabase.GUIDToAssetPath(source.sourceGuid) });
                    foreach (string dependency in dependencies) if (MidiPackageExporter.Add(assets, dependency)) queue.Enqueue(dependency);
                }
                File.WriteAllText(manifestPath, JsonUtility.ToJson(new SourceManifest {
                    version = MidiReleasePolicy.Version, assets = assets.ToArray(), overrides = overrides.OrderBy(x => x).ToArray(),
                    sceneSource = "Saved scene files only; loaded unsaved edits were preserved and are not included."
                }, true));
                return "Prepared public source assets: " + assets.Count + "; sanitized overrides: " + overrides.Count + ". " + manifestPath;
            }
            finally
            {
                foreach (var item in libraries.Values) UnityEngine.Object.DestroyImmediate(item);
                foreach (var item in configurations.Values) UnityEngine.Object.DestroyImmediate(item);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (Directory.Exists(temp)) AssetDatabase.DeleteAsset(temp);
                if (active.IsValid() && active.isDirty != activeDirty) Debug.LogWarning("Release preparation observed an authoring scene dirty-state change; it was not saved.");
            }
        }

        private static void Serialize(UnityEngine.Object value, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { value }, path, true);
            // This API writes a settings-style root fileID of 1. Existing project
            // assets reference 11400000; retain that identity with the original .meta.
            string yaml = File.ReadAllText(path);
            yaml = System.Text.RegularExpressions.Regex.Replace(yaml, @"(?m)^--- !u!114 &1\r?$", "--- !u!114 &11400000");
            File.WriteAllText(path, yaml);
        }

        private static void RebakeSelectedBank(MidiPlayerFacade facade, MidiPlayerConfigurationAsset configuration)
        {
            // Bring old distribution/scene-copy data up to its selected existing bank,
            // without rebuilding models/UI or changing the source bank and audio gains.
            if (facade.library.bank.bankHash == configuration.bank.bankHash) return;
            MidiDataBaker.CopyBankToRuntime(configuration.bank, facade.library.bank);
            UdonSharpEditorUtility.CopyProxyToUdon(facade.library.bank, ProxySerializationPolicy.All);
            EditorUtility.SetDirty(facade.library.bank);
        }

        private static void CheckIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Release preparation requires an idle Editor outside Play Mode");
        }

        [Serializable]
        public sealed class SourceManifest { public string version, sceneSource; public string[] assets, overrides; }
    }
}
