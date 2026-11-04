using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StringsBankData : UdonSharpBehaviour
    {
        public string bankId;
        public string bankHash;
        // Flat 20 ms mean-square envelopes; repeated clips share one range.
        public int[] energyStarts = new int[0], energyCounts = new int[0];
        public float[] energyValues = new float[0];

        public AudioClip[] attacks = new AudioClip[0];
        public AudioClip[] loops = new AudioClip[0];
        public int[] roots = new int[0];
        public int[] articulations = new int[0];
        public int[] releaseMs = new int[0];
        public float[] gains = new float[0];
        public float[] peaks = new float[0];
        // Explicit 0..127 map for each articulation, independent of piano key geometry.
        public int[] pitchToRegion = new int[0];

        public int SelectRegion(int pitch, int articulation)
        {
            int index = Mathf.Clamp(articulation, 0, 2) * 128 + Mathf.Clamp(pitch, 0, 127);
            return pitchToRegion != null && index < pitchToRegion.Length ? pitchToRegion[index] : -1;
        }

        public float EnergyAt(int region, float sourceSeconds)
        {
            if (sourceSeconds < 0f) return 0f;
            float fallback = peaks[region] * peaks[region];
            float attackLength = attacks[region].length;
            if (sourceSeconds < attackLength) return ReadEnergy(region * 2, sourceSeconds, fallback, 0f);
            AudioClip loop = loops[region];
            if (loop == null) return 0f;
            return ReadEnergy(region * 2 + 1, (sourceSeconds - attackLength) % loop.length, fallback, loop.length);
        }

        private float ReadEnergy(int index, float seconds, float fallback, float loopLength)
        {
            if (energyStarts == null || energyCounts == null || index < 0 || index >= energyStarts.Length || index >= energyCounts.Length) return fallback;
            int first = energyStarts[index], count = energyCounts[index];
            if (count < 2 || energyValues == null || first < 0 || first > energyValues.Length - count) return fallback;
            float position = Mathf.Max(0f, seconds) * 50f;
            int point = Mathf.Min(count - 1, Mathf.FloorToInt(position));
            if (point == count - 1) return 0f;
            if (loopLength > 0f && point == count - 2)
            {
                float phase = (position - point) / Mathf.Max(0.0001f, loopLength * 50f - point);
                return Mathf.Lerp(energyValues[first + point], energyValues[first], phase);
            }
            return Mathf.Lerp(energyValues[first + point], energyValues[first + point + 1], position - point);
        }

        public bool IsValid()
        {
            int n = attacks == null ? 0 : attacks.Length;
            if (n == 0 || loops.Length != n || roots.Length != n || articulations.Length != n
                || releaseMs.Length != n || gains.Length != n || peaks.Length != n || pitchToRegion.Length != 384) return false;
            for (int i = 0; i < n; i++)
                if (attacks[i] == null || attacks[i].samples <= 0 || roots[i] < 0 || roots[i] > 127
                    || articulations[i] < 0 || articulations[i] > 2 || releaseMs[i] <= 0
                    || gains[i] < 0 || float.IsNaN(gains[i]) || float.IsInfinity(gains[i])
                    || (articulations[i] == 0 && (loops[i] == null || loops[i].samples <= 0))) return false;
            for (int i = 0; i < 384; i++)
            {
                int r = pitchToRegion[i];
                if (r < 0 || r >= n || articulations[r] != i / 128
                    || Mathf.Pow(2f, ((i % 128) - roots[r]) / 12f) > 3f) return false;
            }
            return true;
        }
    }
}
