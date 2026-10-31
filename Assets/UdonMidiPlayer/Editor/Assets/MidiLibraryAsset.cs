using System;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Assets
{
    [Serializable]
    public sealed class MidiLibraryFolder
    {
        public string path;
        public MidiSongAsset[] songs = Array.Empty<MidiSongAsset>();
    }

    [CreateAssetMenu(menuName = "Music MIDI Player/Source Assets/MIDI Library", fileName = "MidiLibrary")]
    public sealed class MidiLibraryAsset : ScriptableObject
    {
        public string title = "MIDI 曲库";
        public MidiSongAsset[] songs = Array.Empty<MidiSongAsset>();
        public string defaultSongId;
        public MidiLibraryFolder[] folders = Array.Empty<MidiLibraryFolder>();
    }
}
