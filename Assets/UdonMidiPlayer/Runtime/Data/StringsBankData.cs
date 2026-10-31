using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StringsBankData : UdonSharpBehaviour
    {
        public string bankId;
        public string bankHash;
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
