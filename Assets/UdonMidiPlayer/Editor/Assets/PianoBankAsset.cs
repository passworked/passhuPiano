using System;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Assets
{
    [CreateAssetMenu(menuName = "Music MIDI Player/Source Assets/Piano Bank", fileName = "PianoBank")]
    public sealed class PianoBankAsset : ScriptableObject
    {
        public string bankHash;
        public int velocityLayerCount = 1;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        public int[] rootPitch = Array.Empty<int>(), lowPitch = Array.Empty<int>(), highPitch = Array.Empty<int>();
        public int[] sourceRate = Array.Empty<int>(), sourceChannels = Array.Empty<int>(), sourceFrameCount = Array.Empty<int>();
        public float[] sourcePeak = Array.Empty<float>(), regionGain = Array.Empty<float>();
        public float[] tuningCents = Array.Empty<float>();
        public int[] releaseDurationMs = Array.Empty<int>(), pitchToRegion = Array.Empty<int>();
        public int maxReleaseMs;
        [TextArea] public string sourceAttribution;
        [TextArea] public string redistributionLicense;
    }
}
