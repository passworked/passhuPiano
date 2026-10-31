using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Data/Library")]
    public class MidiLibraryData : UdonSharpBehaviour
    {
        public MidiSongData[] songs = new MidiSongData[0];
        public PianoBankData bank;
        public string catalogHash;
        public string defaultSongId;
        public string configurationAssetGuid;

        public int FindSongIndex(string id)
        {
            if (songs == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < songs.Length; i++) if (songs[i] != null && songs[i].songId == id) return i;
            return -1;
        }

        public MidiSongData FindSong(string id)
        {
            int index = FindSongIndex(id);
            return index < 0 ? null : songs[index];
        }
    }
}
