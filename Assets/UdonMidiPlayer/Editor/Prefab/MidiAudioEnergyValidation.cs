using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Baking;
using UdonSharpEditor;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiAudioEnergyValidation
    {
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static void Persisted<T>(UdonSharpBehaviour bank, string field, T[] expected)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(bank);
            Require(backing != null && backing.publicVariables.TryGetVariableValue<T[]>(field, out T[] actual)
                && actual != null && expected.SequenceEqual(actual), "Backing Udon energy array differs: " + bank.name + "." + field);
        }

        public static long ValidateRoot(GameObject root)
        {
            long bytes = 0;
            foreach (var bank in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                int[] starts, counts; float[] values; AudioClip[] clips;
                if (bank is PianoBankData piano) { starts = piano.energyStarts; counts = piano.energyCounts; values = piano.energyValues; clips = piano.clips; }
                else if (bank is StringsBankData strings) { starts = strings.energyStarts; counts = strings.energyCounts; values = strings.energyValues; clips = AudioEnergyBaker.Clips(strings); }
                else if (bank is PercussionBankData drums) { starts = drums.energyStarts; counts = drums.energyCounts; values = drums.energyValues; clips = drums.clips; }
                else continue;
                AudioEnergyBaker.Validate(clips, starts, counts, values);
                Persisted(bank, "energyStarts", starts); Persisted(bank, "energyCounts", counts); Persisted(bank, "energyValues", values);
                bytes += values.LongLength * 4 + starts.LongLength * 4 + counts.LongLength * 4;
            }
            foreach (var output in root.GetComponentsInChildren<PianoVoicePool>(true))
            {
                Require(output.peakProtectionEnabled, "Existing player is not enabled by default: " + root.name);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(output);
                Require(backing.publicVariables.TryGetVariableValue<bool>("peakProtectionEnabled", out bool enabled) && enabled,
                    "Default-on toggle was not persisted to backing Udon.");
            }
            return bytes;
        }

        public static string Run()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("AudioEnergyValidation"); SceneManager.MoveGameObjectToScene(root, preview);
            var clips = new List<AudioClip>();
            var checks = new List<string>();
            try
            {
                AudioClip clip = AudioClip.Create("energy-step-stereo", 3840, 2, 48000, false); clips.Add(clip);
                var pcm = new float[7680];
                float[] amplitudes = { 0.2f, 0.1f, 0.05f, 0f };
                for (int i = 0; i < 3840; i++) { float a = amplitudes[i / 960]; pcm[i * 2] = a; pcm[i * 2 + 1] = -a * 2f; }
                clip.SetData(pcm, 0);
                AudioEnergyBaker.Bake(new[] { clip, clip, null }, out int[] starts, out int[] counts, out float[] energy);
                AudioEnergyBaker.Validate(new[] { clip, clip, null }, starts, counts, energy);
                Require(energy.Length == 5 && starts[0] == starts[1] && counts[2] == 0, "Clip deduplication or null loop failed.");
                Require(Mathf.Abs(energy[0] - 0.1f) < 0.00001f && Mathf.Abs(energy[1] - 0.025f) < 0.00001f
                    && Mathf.Abs(energy[2] - 0.00625f) < 0.00001f && energy[3] == 0 && energy[4] == 0, "Stereo mean-square calculation failed.");
                checks.Add("50Hz stereo mean-square, decaying windows, silence, terminal point and duplicate/null clips");

                AudioClip partial = AudioClip.Create("energy-partial-window", 1001, 1, 48000, false); clips.Add(partial);
                var partialPcm = new float[1001]; for (int i = 0; i < partialPcm.Length; i++) partialPcm[i] = 0.25f;
                partial.SetData(partialPcm, 0);
                AudioEnergyBaker.Bake(new[] { partial }, out int[] ps, out int[] pc, out float[] pe);
                Require(pe.Length == 3 && Mathf.Abs(pe[1] - 0.0625f) < 0.00001f, "Partial final window wraps/averages incorrectly.");
                bool rejected = false; try { AudioEnergyBaker.Validate(new[] { partial }, new[] { 0 }, new[] { 99 }, pe); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Stale energy metadata was accepted.");
                AudioClip oddRate = AudioClip.Create("energy-11025Hz", 11025, 1, 11025, false); clips.Add(oddRate);
                var oddPcm = new float[11025]; for (int i = 0; i < oddPcm.Length; i++) oddPcm[i] = 0.25f;
                oddRate.SetData(oddPcm, 0);
                AudioEnergyBaker.Bake(new[] { oddRate }, out int[] os, out int[] oc, out float[] oe);
                Require(oe.Length == 51 && oe.Take(50).All(v => Mathf.Abs(v - 0.0625f) < 0.00001f), "20 ms envelope drifts at 11025 Hz.");
                checks.Add("Partial final window and stale-range rejection");

                var bank = root.AddUdonSharpComponent<PianoBankData>();
                bank.clips = new[] { clip }; bank.sourcePeak = new[] { 0.4f };
                bank.energyStarts = new[] { starts[0] }; bank.energyCounts = new[] { counts[0] }; bank.energyValues = energy;
                Require(Mathf.Abs(bank.EnergyAt(0, 0.01f) - 0.0625f) < 0.00001f && bank.EnergyAt(0, -0.01f) == 0
                    && bank.EnergyAt(0, 0.08f) == 0 && bank.EnergyAt(0, 0.06f) < bank.EnergyAt(0, 0f), "Envelope interpolation/timing failed.");
                var strings = root.AddUdonSharpComponent<StringsBankData>();
                strings.attacks = new[] { clip }; strings.loops = new[] { clip }; strings.peaks = new[] { 0.4f };
                AudioEnergyBaker.Bake(strings);
                Require(Mathf.Abs(strings.EnergyAt(0, 0.09f) - strings.EnergyAt(0, 0.17f)) < 0.00001f
                    && strings.EnergyAt(0, -0.01f) == 0, "Sustain loop phase failed.");
                strings.loops[0] = partial; AudioEnergyBaker.Bake(strings);
                Require(Mathf.Abs(strings.EnergyAt(0, clip.length + partial.length - 0.0001f) - 0.0625f) < 0.00001f,
                    "Sustain energy dips at the loop boundary.");
                checks.Add("Envelope interpolation, pre-start exclusion, natural tail and sustain loop wrapping");

                var output = root.AddUdonSharpComponent<PianoVoicePool>();
                Require(output.peakProtectionEnabled, "Compression is not enabled by default.");
                float single = output.CalculateCompressionGain(0.01f, 1.0);
                float chord = output.CalculateCompressionGain(0.16f, 1.01);
                Require(single > 1.4f && chord < single && 4f * chord / single < 2.1f, "Dense chord is not compressed relative to a single note.");
                float recovering = output.CalculateCompressionGain(0.01f, 1.10);
                Require(recovering > chord && recovering < single, "Gain recovery is not smoothed.");
                output.peakProtectionEnabled = false;
                Require(output.CalculateCompressionGain(100f, 1.11) == 1f, "Bypass retains compression or makeup.");
                output.peakProtectionEnabled = true;
                Require(output.CalculateCompressionGain(100000f, 1.12) >= Mathf.Pow(10f, (3f - 18f) / 20f) - 0.00001f, "Maximum gain reduction exceeded.");
                checks.Add("Default on, single/chord response, smooth release, exact bypass and reduction bound");

                output.localVolume = 2f;
                float protectedGain = output.LimitOutputGain(1.4125376f, 64f * .84f, 1f, 2.0);
                Require(2f * protectedGain * 64f * .84f <= .841396f,
                    "Correlated 64-voice chord exceeds the shared peak ceiling.");
                Require(2f * protectedGain * 64f * .84f * 1.25f <= .841396f,
                    "Codec/resampling peak allowance is missing.");
                output.peakProtectionEnabled = false;
                float bypassSafe = output.LimitOutputGain(output.CalculateCompressionGain(100f, 2.01), 4f, 2f, 2.01);
                Require(2f * bypassSafe * 4f <= .841396f && 2f * bypassSafe * 2f <= 1f,
                    "RMS bypass disables peak safety or saturates individual voice gains.");
                float quietLevel = .1f * bypassSafe, loudLevel = .8f * bypassSafe;
                Require(Mathf.Abs(loudLevel / quietLevel - 8f) < .0001f, "Headroom changes velocity/mix ratios.");
                float releaseGain = output.LimitOutputGain(1f, 0f, 0f, 2.06);
                Require(releaseGain > bypassSafe && releaseGain < 1f, "Peak safety recovery is not smoothed.");
                output.localVolume = 0f;
                float silentGain = output.LimitOutputGain(1f, 0f, 0f, 2.07);
                Require(!float.IsNaN(silentGain) && !float.IsInfinity(silentGain), "Silent output produces invalid gain.");
                checks.Add("64 correlated voices, slider maximum, RMS bypass, common velocity ratios, smooth peak recovery and silence");

                // Exercise the public authoring path with an existing imported clip, without creating assets.
                var player = UnityEngine.Object.FindObjectOfType<MusicMidiPlayer.Logic.MidiPlayerFacade>();
                if (player != null && player.library != null && player.library.bank != null)
                {
                    var source = ScriptableObject.CreateInstance<MusicMidiPlayer.Editor.Assets.PianoBankAsset>();
                    try
                    {
                        AudioClip imported = player.library.bank.clips[0];
                        source.clips = new[] { imported, imported, imported, imported, imported };
                        source.rootPitch = new[] { 21, 45, 69, 93, 108 }; source.tuningCents = new float[5];
                        MidiDataBaker.PrepareBank(source); MidiDataBaker.CopyBankToRuntime(source, bank);
                        Require(bank.IsValid() && bank.energyStarts.Length == 5 && bank.energyStarts[0] == bank.energyStarts[4]
                            && bank.energyValues.Length == bank.energyCounts[0] && bank.bankHash == source.bankHash,
                            "Author sample preparation/copy did not include deduplicated energy metadata.");
                        checks.Add("Author-imported sample PrepareBank/ValidateBank/CopyBankToRuntime, deduplication and bank hash");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(source); }
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(new { passed = true, scope = "Editor algorithm checks; not client listening validation", checks, singleGain = single, chordGain = chord, chordToSingleAmplitude = 4f * chord / single }, Newtonsoft.Json.Formatting.Indented);
                Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/AudioEnergyValidation.json", json);
                return json;
            }
            finally
            {
                AudioEnergyBaker.ClearCache();
                // UdonSharp queues setup work for newly added behaviours. Let it finish before destruction.
                EditorApplication.delayCall += () =>
                {
                    if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                    foreach (AudioClip clip in clips) if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                };
            }
        }
    }
}
