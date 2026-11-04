using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Data;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Baking
{
    /// <summary>50 Hz channel-averaged mean-square energy; shared by repeated clip references.</summary>
    public static class AudioEnergyBaker
    {
        public const string Format = "sample-energy-v1:50hz:mean-square:channel-average";
        private static readonly Dictionary<AudioClip, float[]> Cache = new Dictionary<AudioClip, float[]>();
        private static readonly Dictionary<AudioClip, Hash128> CacheHashes = new Dictionary<AudioClip, Hash128>();

        public static void ClearCache() { Cache.Clear(); CacheHashes.Clear(); }

        public static void Bake(AudioClip[] clips, out int[] starts, out int[] counts, out float[] energy)
        {
            starts = new int[clips.Length]; counts = new int[clips.Length];
            var values = new List<float>();
            var offsets = new Dictionary<AudioClip, int>();
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[i];
                if (clip == null) continue;
                Hash128 hash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(clip));
                if (!Cache.TryGetValue(clip, out float[] curve) || !CacheHashes.TryGetValue(clip, out Hash128 cachedHash) || hash != cachedHash)
                {
                    if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
                        throw new InvalidOperationException("Cannot load audio energy: " + clip.name);
                    int frames = Math.Max(1, (clip.frequency + 49) / 50);
                    int windows = (int)(((long)clip.samples * 50 + clip.frequency - 1) / clip.frequency);
                    curve = new float[windows + 1]; // Final point is silence; loop readers wrap before it.
                    var buffer = new float[Math.Min(frames * 32, clip.samples) * clip.channels];
                    for (int first = 0; first < windows; first += 32)
                    {
                        int firstFrame = (int)((long)first * clip.frequency / 50);
                        if (!clip.GetData(buffer, firstFrame))
                            throw new InvalidOperationException("Audio energy requires Decompress On Load: " + clip.name);
                        int end = Math.Min(windows, first + 32);
                        for (int w = first; w < end; w++)
                        {
                            int beginFrame = (int)((long)w * clip.frequency / 50);
                            int endFrame = (int)Math.Min(clip.samples, (long)(w + 1) * clip.frequency / 50);
                            int length = (endFrame - beginFrame) * clip.channels;
                            int offset = (beginFrame - firstFrame) * clip.channels;
                            double sum = 0;
                            for (int s = 0; s < length; s++)
                            {
                                float value = buffer[offset + s];
                                if (float.IsNaN(value) || float.IsInfinity(value))
                                    throw new InvalidOperationException("Non-finite audio sample: " + clip.name);
                                sum += (double)value * value;
                            }
                            curve[w] = (float)(sum / Math.Max(1, length));
                        }
                    }
                    Cache[clip] = curve; CacheHashes[clip] = hash;
                }
                if (!offsets.TryGetValue(clip, out int start))
                { start = values.Count; offsets.Add(clip, start); values.AddRange(curve); }
                starts[i] = start; counts[i] = curve.Length;
            }
            energy = values.ToArray();
        }

        public static void Validate(AudioClip[] clips, int[] starts, int[] counts, float[] energy)
        {
            if (starts == null || counts == null || energy == null || starts.Length != clips.Length || counts.Length != clips.Length)
                throw new InvalidOperationException("Audio energy is missing; prepare/reimport the sample bank.");
            for (int i = 0; i < clips.Length; i++)
            {
                int expected = clips[i] == null ? 0 : (int)(((long)clips[i].samples * 50 + clips[i].frequency - 1) / clips[i].frequency) + 1;
                if (counts[i] != expected || starts[i] < 0 || starts[i] > energy.Length - counts[i])
                    throw new InvalidOperationException("Audio energy range is stale: " + i);
            }
            foreach (float value in energy)
                if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("Invalid audio energy value.");
        }

        public static void Bake(StringsBankData bank)
        {
            var clips = Clips(bank);
            Bake(clips, out bank.energyStarts, out bank.energyCounts, out bank.energyValues);
        }
        public static AudioClip[] Clips(StringsBankData bank)
        {
            var clips = new AudioClip[bank.attacks.Length * 2];
            for (int i = 0; i < bank.attacks.Length; i++) { clips[i * 2] = bank.attacks[i]; clips[i * 2 + 1] = bank.loops[i]; }
            return clips;
        }
        public static void Bake(PercussionBankData bank)
        {
            MidiAudioPacking.BakeWithSourcePcm(bank.clips.Where(clip => clip != null).Select(AssetDatabase.GetAssetPath),
                () => Bake(bank.clips, out bank.energyStarts, out bank.energyCounts, out bank.energyValues));
        }

        public static string Fingerprint(int[] starts, int[] counts, float[] energy)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Format);
                foreach (int value in starts) writer.Write(value);
                foreach (int value in counts) writer.Write(value);
                foreach (float value in energy) writer.Write(value);
                writer.Flush();
                return MidiContentHash.Bytes(stream.ToArray());
            }
        }
    }
}
