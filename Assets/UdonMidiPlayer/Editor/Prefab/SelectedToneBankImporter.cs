using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class SelectedToneBankImporter
    {
        private const string Folder = "Assets/UdonMidiPlayer/Audio/SelectedQ4";

        [MenuItem("Tools/Music MIDI Player/Apply Selected Q4 Banks")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Import("piano", SalamanderBankImporter.BankPath, 4);
            ImportMarimba();
            MidiInstrumentMigration.ApplyToneChanges();
            File.WriteAllText("Logs/selected-q4-scene.json", MidiInstrumentMigration.ValidateScene());
        }

        public static PianoBankAsset ImportMarimba() { return Import("marimba", MidiInstrumentMigration.BankPath, 1); }

        private static PianoBankAsset Import(string key, string assetPath, int layers)
        {
            var manifest = JObject.Parse(File.ReadAllText(Folder + "/BANK-MAPPING.json"));
            var rows = (JArray)manifest[key];
            string[] paths = rows.Select(r => (string)r["file"]).ToArray();
            if (paths.Any(p => !File.Exists(p))) throw new InvalidOperationException("Run Tools/prepare_selected_tones.py first.");
            foreach (string path in paths)
            {
                if (MidiContentHash.Bytes(File.ReadAllBytes(path)) != (string)rows[Array.IndexOf(paths, path)]["sha256"])
                    throw new InvalidOperationException("Source checksum mismatch: " + path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                importer.forceToMono = false; importer.loadInBackground = false;
                var settings = importer.defaultSampleSettings;
                settings.preloadAudioData = true;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                if (key == "piano") MidiAudioPacking.Configure(importer);
                else foreach (string platform in new[] { "Standalone", "Android" })
                    if (importer.ContainsSampleSettingsOverride(platform)) importer.ClearSampleSettingOverride(platform);
                importer.SaveAndReimport();
            }
            var bank = AssetDatabase.LoadAssetAtPath<PianoBankAsset>(assetPath);
            if (bank == null) throw new InvalidOperationException("Expected existing bank to preserve its GUID: " + assetPath);
            bank.velocityLayerCount = layers;
            bank.clips = paths.Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray();
            bank.rootPitch = rows.Select(r => (int)r["pitch"]).ToArray();
            bank.tuningCents = rows.Select(r => (float)r["tune"]).ToArray();
            bank.regionGain = rows.Select(r => (float)r["gain"]).ToArray();
            bank.releaseDurationMs = rows.Select(r => (int)r["release"]).ToArray();
            bank.lowPitch = new int[0]; bank.highPitch = new int[0];
            string subfolder = key == "piano" ? "Piano" : "Marimba";
            bank.sourceAttribution = File.ReadAllText(Folder + "/" + subfolder + "/ATTRIBUTION.txt");
            bank.redistributionLicense = key == "piano" ? "CC BY 3.0; retain ORIGINAL-README.txt and ATTRIBUTION.txt." : File.ReadAllText(Folder + "/Marimba/LICENSE.txt");
            MidiDataBaker.PrepareBank(bank);
            File.WriteAllText(Folder + "/" + subfolder + "/IMPORT-REPORT.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
                clips = bank.clips.Length, recordedClips = key == "piano" ? 120 : 60, velocityLayers = layers,
                sourceQuality = 4, encodedSourceBytes = paths.Sum(p => new FileInfo(p).Length),
                decodedFloat32EstimateBytes = bank.clips.Sum(c => (double)c.samples * c.channels * 4),
                observedEditorClipMemoryBytes = bank.clips.Sum(c => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c)),
                bank.bankHash, note = key == "piano"
                    ? "Standalone: Vorbis quality 70; default: PCM. Both use DecompressOnLoad. Source Ogg q4 is not build/download size."
                    : "PCM / DecompressOnLoad; source Ogg q4 is not build/download size."
            }, Newtonsoft.Json.Formatting.Indented));
            AssetDatabase.SaveAssetIfDirty(bank);
            return bank;
        }
    }
}
