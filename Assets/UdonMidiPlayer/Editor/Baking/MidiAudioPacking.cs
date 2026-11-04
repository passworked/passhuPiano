using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Baking
{
    internal static class MidiAudioPacking
    {
        private const string Standalone = "Standalone";
        private const float Quality = 0.7f;
        // Unity 2022 serializes Standalone as platform 1. Match only the tested
        // packing override so all other importer changes still affect validation.
        private static readonly Regex PackingOverride = new Regex(
            @"(?m)^  platformSettingOverrides:(\r?\n)    1:\1      serializedVersion: 2\1      loadType: 0\1      sampleRateSetting: 0\1      sampleRateOverride: 44100\1      compressionFormat: 1\1      quality: 0\.7\1      conversionMode: 0\1      preloadAudioData: 1\1(?=  forceToMono: 0\r?$)");

        private static bool UsesVorbis(string path)
        {
            return path.EndsWith(".ogg", StringComparison.Ordinal) &&
                (path.StartsWith("Assets/UdonMidiPlayer/Audio/SelectedQ4/Piano/", StringComparison.Ordinal) ||
                 path.StartsWith("Assets/UdonMidiPlayer/Audio/VirtuosityDrums/", StringComparison.Ordinal));
        }

        private static AudioImporterSampleSettings StandaloneSettings(AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = Quality;
            return settings;
        }

        private static bool HasPackingOverride(AudioImporter importer)
        {
            return importer.ContainsSampleSettingsOverride(Standalone) &&
                importer.GetOverrideSampleSettings(Standalone).Equals(StandaloneSettings(importer));
        }

        internal static bool Configure(AudioImporter importer)
        {
            if (!UsesVorbis(importer.assetPath) || HasPackingOverride(importer)) return false;
            importer.SetOverrideSampleSettings(Standalone, StandaloneSettings(importer));
            return true;
        }

        internal static byte[] ReadBankMetadata(string path)
        {
            byte[] bytes = File.ReadAllBytes(path + ".meta");
            if (!UsesVorbis(path)) return bytes;
            string text = Encoding.UTF8.GetString(bytes);
            string normalized = PackingOverride.Replace(text, "  platformSettingOverrides: {}$1");
            return normalized == text ? bytes : Encoding.UTF8.GetBytes(normalized);
        }

        internal static void BakeWithSourcePcm(IEnumerable<string> paths, Action bake)
        {
            bool isStandalone = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Standalone;
            var overrides = new Dictionary<string, AudioImporterSampleSettings>();
            if (isStandalone)
            {
                foreach (string path in paths.Where(UsesVorbis).Distinct())
                {
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer != null && HasPackingOverride(importer))
                        overrides.Add(path, importer.GetOverrideSampleSettings(Standalone));
                }
            }

            if (overrides.Count == 0) { bake(); return; }
            try
            {
                UpdateOverrides(overrides, false);
                bake();
            }
            finally { UpdateOverrides(overrides, true); }
        }

        private static void UpdateOverrides(Dictionary<string, AudioImporterSampleSettings> overrides, bool restore)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in overrides)
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(entry.Key);
                    if (restore) importer.SetOverrideSampleSettings(Standalone, entry.Value);
                    else importer.ClearSampleSettingOverride(Standalone);
                    importer.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
    }
}
