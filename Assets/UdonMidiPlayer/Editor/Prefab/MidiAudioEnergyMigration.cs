using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Logic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Updates only audio metadata, catalog identities and the existing local toggle.</summary>
    public static class MidiAudioEnergyMigration
    {
        private static Dictionary<string, PianoBankAsset> PrepareSources()
        {
            AudioEnergyBaker.ClearCache();
            var sources = new Dictionary<string, PianoBankAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:PianoBankAsset", new[] { "Assets/UdonMidiPlayer" }))
            {
                var bank = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (bank.clips == null || bank.clips.Length == 0) continue;
                // Archived pre-tuning sources are not inputs to the current player. Do not repair them implicitly.
                if (bank.tuningCents == null || bank.tuningCents.Length != bank.clips.Length) continue;
                if (!string.IsNullOrEmpty(bank.bankHash)) sources[bank.bankHash] = bank;
                MidiDataBaker.PrepareBank(bank);
                sources[bank.bankHash] = bank;
            }
            return sources;
        }

        public static long UpdateRoot(GameObject root, Dictionary<string, PianoBankAsset> sources, bool enable)
        {
            var changed = new HashSet<UdonSharpBehaviour>();
            long bytes = 0;
            foreach (var bank in root.GetComponentsInChildren<PianoBankData>(true))
            {
                if (!sources.TryGetValue(bank.bankHash, out PianoBankAsset source))
                    source = sources.Values.Distinct().FirstOrDefault(s => s.clips.SequenceEqual(bank.clips)
                        && s.regionGain.SequenceEqual(bank.regionGain) && s.releaseDurationMs.SequenceEqual(bank.releaseDurationMs));
                if (source == null) throw new InvalidOperationException("Cannot locate source bank: " + bank.name);
                MidiDataBaker.CopyBankToRuntime(source, bank); changed.Add(bank);
                bytes += bank.energyValues.LongLength * 4 + (bank.energyStarts.LongLength + bank.energyCounts.LongLength) * 4;
            }
            foreach (var bank in root.GetComponentsInChildren<StringsBankData>(true))
            {
                AudioEnergyBaker.Bake(bank); bank.bankHash = MidiStringsBuilder.BankHash(bank); changed.Add(bank);
                bytes += bank.energyValues.LongLength * 4 + (bank.energyStarts.LongLength + bank.energyCounts.LongLength) * 4;
            }
            foreach (var bank in root.GetComponentsInChildren<PercussionBankData>(true))
            {
                AudioEnergyBaker.Bake(bank); bank.bankHash = MidiPercussionBuilder.ComputeBankHash(bank); changed.Add(bank);
                bytes += bank.energyValues.LongLength * 4 + (bank.energyStarts.LongLength + bank.energyCounts.LongLength) * 4;
            }
            foreach (var output in root.GetComponentsInChildren<PianoVoicePool>(true))
            {
                if (enable) output.peakProtectionEnabled = true;
                changed.Add(output);
            }
            foreach (var facade in root.GetComponentsInChildren<MidiPlayerFacade>(true))
            {
                string configPath = AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid);
                var config = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(configPath);
                if (config == null) throw new InvalidOperationException("Missing player configuration: " + configPath);
                facade.library.catalogHash = MidiInstrumentMigration.InstrumentCatalogHash(facade, MidiContentHash.Catalog(config.library, config.bank));
                facade.network.catalogHash = facade.library.catalogHash;
                changed.Add(facade.library); changed.Add(facade.network);
                var ui = facade.playerUI;
                if (ui == null) continue;
                Transform label = ui.transform.Find("SettingsPanel/Content/PeakProtection/Label");
                if (label != null)
                {
                    var text = label.GetComponent<TMP_Text>();
                    if (text != null) { text.text = "Volume leveling"; EditorUtility.SetDirty(text); }
                }
                if (ui.peakProtectionSelectionMark != null)
                    ui.peakProtectionSelectionMark.SetActive(facade.voicePool.peakProtectionEnabled);
                // Existing paths, callbacks and localization key remain stable.
                changed.Add(ui);
            }
            foreach (var behaviour in changed)
            {
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(behaviour);
                if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            }
            return bytes;
        }

        [MenuItem("Tools/Music MIDI Player/Rebake Audio Energy In Open Scene")]
        public static void RebakeOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var sources = PrepareSources();
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<PianoVoicePool>(true) != null) UpdateRoot(root, sources, false);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            AudioEnergyBaker.ClearCache();
        }

        // Explicit one-time project upgrade. Never called by ordinary song import or reimport.
        public static string UpgradeProject()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes before upgrading audio energy.");
            var sources = PrepareSources();
            var report = new List<object>();
            // Legacy demo/practice scenes retain their independently configured sample banks.
            string[] scenes = { MidiInstrumentMigration.MainScene };
            foreach (string path in scenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    long bytes = 0; int players = 0;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        if (root.GetComponentInChildren<PianoVoicePool>(true) != null)
                        { bytes += UpdateRoot(root, sources, true); players++; }
                    if (players > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                        report.Add(new { path, players, bytes });
                    }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            // Edit prefab contents independently. Never Apply scene overrides or synchronize song membership.
            foreach (string path in new[] { MidiPlayerBuilder.PrefabPath, "Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer_Upright.prefab" })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    long bytes = UpdateRoot(root, sources, true);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.Add(new { path, players = 1, bytes });
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); AudioEnergyBaker.ClearCache();
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/AudioEnergyMigration.json", json);
            return json;
        }
    }
}
