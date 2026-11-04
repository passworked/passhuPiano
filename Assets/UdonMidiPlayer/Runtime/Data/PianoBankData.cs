using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Data/Piano Bank")]
    public class PianoBankData : UdonSharpBehaviour
    {
        public string bankHash;
        // Flat 20 ms mean-square envelopes; repeated clips share one range.
        public int[] energyStarts = new int[0], energyCounts = new int[0];
        public float[] energyValues = new float[0];

        // Regions are contiguous per recorded velocity layer.
        public int velocityLayerCount = 1;
        public AudioClip[] clips = new AudioClip[0];
        public int[] rootPitch = new int[0];
        public int[] lowPitch = new int[0];
        public int[] highPitch = new int[0];
        public int[] sourceRate = new int[0];
        public int[] sourceChannels = new int[0];
        public int[] sourceFrameCount = new int[0];
        public float[] sourcePeak = new float[0];
        public float[] regionGain = new float[0];
        public float[] tuningCents = new float[0];
        public int[] releaseDurationMs = new int[0];
        // A baked array must differ in length from its program-default array. UdonSharp's
        // equal-length array serialization can copy into the default heap without persisting
        // an instance public-variable override. Keep this empty; the Editor bakes all 88 entries.
        public int[] pitchToRegion = new int[0];
        public int maxReleaseMs = 180;

        public float EnergyAt(int region, float sourceSeconds)
        {
            if (sourceSeconds < 0f || sourceSeconds >= clips[region].length) return 0f;
            return ReadEnergy(region, sourceSeconds, sourcePeak[region] * sourcePeak[region]);
        }

        private float ReadEnergy(int index, float seconds, float fallback)
        {
            if (energyStarts == null || energyCounts == null || index < 0 || index >= energyStarts.Length || index >= energyCounts.Length) return fallback;
            int first = energyStarts[index], count = energyCounts[index];
            if (count < 2 || energyValues == null || first < 0 || first > energyValues.Length - count) return fallback;
            float position = Mathf.Max(0f, seconds) * 50f;
            int point = Mathf.Min(count - 1, Mathf.FloorToInt(position));
            if (point == count - 1) return 0f;
            return Mathf.Lerp(energyValues[first + point], energyValues[first + point + 1], position - point);
        }

        public bool IsValid()
        {
            int count = clips == null ? 0 : clips.Length;
            if ((velocityLayerCount != 1 && velocityLayerCount != 3 && velocityLayerCount != 4) || count % velocityLayerCount != 0) return false;
            int perLayer = count / velocityLayerCount;
            if (count == 0 || rootPitch == null || lowPitch == null || highPitch == null || sourceRate == null || sourceChannels == null || sourceFrameCount == null) return false;
            if (rootPitch.Length != count || lowPitch.Length != count || highPitch.Length != count || sourceRate.Length != count || sourceChannels.Length != count || sourceFrameCount.Length != count) return false;
            if (sourcePeak == null || regionGain == null || tuningCents == null || releaseDurationMs == null || pitchToRegion == null) return false;
            if (sourcePeak.Length != count || regionGain.Length != count || tuningCents.Length != count || releaseDurationMs.Length != count || pitchToRegion.Length != 88 || maxReleaseMs <= 0) return false;
            int expectedLow = 21;
            int actualMaxRelease = 0;
            for (int i = 0; i < count; i++)
            {
                if (i % perLayer == 0) expectedLow = 21;
                if (clips[i] == null || sourceRate[i] <= 0 || sourceChannels[i] <= 0 || sourceFrameCount[i] <= 0 || releaseDurationMs[i] <= 0) return false;
                if (lowPitch[i] != expectedLow || highPitch[i] < lowPitch[i] || highPitch[i] > 108
                    || rootPitch[i] < lowPitch[i] || rootPitch[i] > highPitch[i]
                    || (i % perLayer > 0 && rootPitch[i] <= rootPitch[i - 1])
                    || (i % perLayer == perLayer - 1 && highPitch[i] != 108)) return false;
                if (float.IsNaN(sourcePeak[i]) || float.IsInfinity(sourcePeak[i]) || sourcePeak[i] < 0f
                    || float.IsNaN(regionGain[i]) || float.IsInfinity(regionGain[i]) || regionGain[i] < 0f
                    || float.IsNaN(tuningCents[i]) || float.IsInfinity(tuningCents[i]) || Mathf.Abs(tuningCents[i]) > 1200f) return false;
                expectedLow = highPitch[i] + 1;
                actualMaxRelease = Mathf.Max(actualMaxRelease, releaseDurationMs[i]);
            }
            if (expectedLow != 109 || actualMaxRelease != maxReleaseMs) return false;
            for (int layer = 0; layer < velocityLayerCount; layer++)
            for (int key = 0; key < 88; key++)
            {
                if (pitchToRegion[key] < 0 || pitchToRegion[key] >= perLayer) return false;
                int region = pitchToRegion[key] + layer * perLayer;
                int pitch = key + 21;
                if (region < 0 || region >= count || pitch < lowPitch[region] || pitch > highPitch[region]
                    || Mathf.Pow(2f, (pitch - rootPitch[region]) / 12f + tuningCents[region] / 1200f) > 3f) return false;
            }
            for (int layer = 0; layer < velocityLayerCount; layer++)
            for (int edge = 0; edge < 2; edge++)
            {
                int pitch = edge == 0 ? 0 : 127;
                int region = pitchToRegion[edge == 0 ? 0 : 87] + layer * perLayer;
                float ratio = Mathf.Pow(2f, (pitch - rootPitch[region]) / 12f + tuningCents[region] / 1200f);
                if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 3f) return false;
            }
            return true;
        }

        public int SelectRegion(int pitch, int velocity)
        {
            int region = pitchToRegion[Mathf.Clamp(pitch, 21, 108) - 21];
            if (velocityLayerCount == 3)
                region += (velocity <= 42 ? 0 : velocity <= 84 ? 1 : 2) * (clips.Length / 3);
            if (velocityLayerCount == 4)
                region += (velocity <= 44 ? 0 : velocity <= 69 ? 1 : velocity <= 100 ? 2 : 3) * (clips.Length / 4);
            return region;
        }
    }
}
