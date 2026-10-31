using UnityEngine;
using MusicMidiPlayer.Audio;

namespace MusicMidiPlayer.Editor.Assets
{
    [CreateAssetMenu(menuName = "Music MIDI Player/Source Assets/Player Configuration", fileName = "MidiPlayerConfiguration")]
    public sealed class MidiPlayerConfigurationAsset : ScriptableObject
    {
        public MidiLibraryAsset library;
        public PianoBankAsset bank;
        public GameObject pianoModel;
        [Range(1, PianoVoicePool.VoiceCapacity)] public int voiceLimit = PianoVoicePool.VoiceCapacity;
        [Range(1, 2048)] public int visibleNoteLimit = 512;
        [Min(1)] public int windowMs = 3000;
        [Min(0.01f)] public float viewHeight = 6f;
        public string defaultSongId;
    }
}
