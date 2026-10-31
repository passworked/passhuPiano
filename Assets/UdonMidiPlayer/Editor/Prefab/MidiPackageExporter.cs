// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPackageExporter
    {
        public static readonly string[] PrefabPaths = {
            MidiPlayerBuilder.PrefabPath,
            "Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer_Upright.prefab"
        };
        public static string OutputDirectory { get { return "Releases/" + MidiReleasePolicy.Version; } }
        public static string PackagePath { get { return OutputDirectory + "/UdonMidiPlayer-v" + MidiReleasePolicy.Version + ".unitypackage"; } }

        [MenuItem("Tools/Music MIDI Player/Export Package")]
        public static void ExportFromMenu() { Debug.Log(Export()); }

        [MenuItem("Tools/Music MIDI Player/Release/Review Package Contents")]
        public static void ReviewFromMenu() { Debug.Log(Review()); }

        public static string Review()
        {
            List<string> assets = CollectAssets();
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(OutputDirectory + "/package-assets.json", JsonUtility.ToJson(new AssetList {
                version = MidiReleasePolicy.Version, assets = assets.ToArray()
            }, true));
            return "Release policy passed: " + assets.Count + " assets. Manifest: " + OutputDirectory + "/package-assets.json";
        }

        public static string Export()
        {
            if (File.Exists(PackagePath)) throw new IOException("Release artifact already exists; use a new VERSION or move the local candidate deliberately: " + PackagePath);
            Review();
            var manifest = JsonUtility.FromJson<AssetList>(File.ReadAllText(OutputDirectory + "/package-assets.json"));
            AssetDatabase.ExportPackage(manifest.assets, Path.GetFullPath(PackagePath), ExportPackageOptions.Default);
            if (!File.Exists(PackagePath) || new FileInfo(PackagePath).Length == 0) throw new IOException("Empty release package");
            File.WriteAllText(OutputDirectory + "/SHA256SUMS", MidiReleasePolicy.FileHash(PackagePath) + "  " + Path.GetFileName(PackagePath) + "\n");
            return Path.GetFullPath(PackagePath) + "\nAssets: " + manifest.assets.Length + "\nBytes: " + new FileInfo(PackagePath).Length;
        }

        public static List<string> CollectAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and finish compilation before export");
            var policy = MidiReleasePolicy.Load();
            var seeds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string prefabPath in PrefabPaths)
            {
                if (!File.Exists(prefabPath)) throw new FileNotFoundException("Missing release Prefab", prefabPath);
                GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    policy.ValidateRoot(contents);
                    MidiBuildValidator.ValidateRoot(contents);
                    Add(seeds, prefabPath);
                    MidiPlayerFacade facade = contents.GetComponentInChildren<MidiPlayerFacade>(true);
                    string configPath = AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid);
                    Add(seeds, configPath);
                    var configuration = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(configPath);
                    if (configuration == null) throw new InvalidOperationException("Missing release configuration: " + configPath);
                    foreach (MidiSongAsset song in configuration.library.songs) policy.ValidateSong(song);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            // Explicit tooling groups; local libraries, scenes and regression programs are not seeds.
            foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/UdonMidiPlayer/Editor", "Assets/UdonMidiPlayer/Runtime", "Assets/UdonMidiPlayer/Documentation", "Assets/UdonMidiPlayer/Localization" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Editor/Generated/") || path.Contains("/RegressionTemp/")) continue;
                if (path.StartsWith("Assets/UdonMidiPlayer/Runtime/", StringComparison.Ordinal)
                    && !(path.EndsWith(".cs") || path.EndsWith(".asmdef") || path.EndsWith(".asset"))) continue;
                Add(seeds, path);
            }
            Add(seeds, "Assets/UdonMidiPlayer/README.md");
            Add(seeds, "Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt");
            foreach (var group in policy.audioGroups)
                foreach (string notice in group.notices) Add(seeds, group.prefix + notice);
            foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/UdonMidiPlayer/Fonts" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".txt") || path.EndsWith(".json")) Add(seeds, path);
            }
            var queue = new Queue<string>(seeds);
            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                policy.ValidateAsset(path);
                var extra = new List<string>(AssetDatabase.GetDependencies(path, false));
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(path);
                if (song != null) extra.Add(AssetDatabase.GUIDToAssetPath(song.sourceGuid));
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (program != null)
                {
                    if (program.SerializedProgramAsset == null) throw new InvalidOperationException("Uncompiled Udon program: " + path);
                    extra.Add(AssetDatabase.GetAssetPath(program.SerializedProgramAsset));
                }
                foreach (string dependency in extra)
                    if (Add(seeds, dependency)) queue.Enqueue(dependency);
            }
            return seeds.ToList();
        }

        internal static bool Add(ISet<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path)) return false;
            if (!File.Exists(path)) throw new FileNotFoundException("Missing release dependency", path);
            return paths.Add(path);
        }

        [Serializable]
        public sealed class AssetList { public string version; public string[] assets; }
    }
}
