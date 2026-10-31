using System;
using MusicMidiPlayer.Editor.Import;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Assets
{
    [CreateAssetMenu(menuName = "Music MIDI Player/Source Assets/MIDI Song", fileName = "MidiSong")]
    public sealed class MidiSongAsset : ScriptableObject
    {
        public string songId;
        public string title;
        public string author = "";
        public string contentHash;
        public string sourceGuid;
        public string sourcePath;
        public int schemaVersion = 2;
        public int durationMs;
        public int[] measureStartMs = new int[0];
        public int[] startMs = Array.Empty<int>(), keyUpMs = Array.Empty<int>(), releaseMs = Array.Empty<int>();
        public int[] pitch = Array.Empty<int>(), velocity = Array.Empty<int>(), channel = Array.Empty<int>(), trackIndex = Array.Empty<int>();
        public int[] eventMs = Array.Empty<int>(), eventKind = Array.Empty<int>(), eventNoteId = Array.Empty<int>();
        public int[] checkpointEventIndex = Array.Empty<int>(), checkpointActiveOffsets = Array.Empty<int>(), checkpointActiveNoteIds = Array.Empty<int>();
        public int[] pedalMs = Array.Empty<int>(), pedalController = Array.Empty<int>(), pedalValue = Array.Empty<int>(), pedalOffsets = Array.Empty<int>();
        public int[] expressionMs = Array.Empty<int>(), expressionValue = Array.Empty<int>();
        public int[] expressionOffsets = new int[17];
        // Kind: 0=Program Change, 1=Bank MSB (CC0), 2=Bank LSB (CC32).
        // eventIndex is the insertion boundary in the unchanged note action stream.
        public int timbreDataVersion;
        public int[] timbreMs = Array.Empty<int>(), timbreChannel = Array.Empty<int>(), timbreKind = Array.Empty<int>();
        public int[] timbreValue = Array.Empty<int>(), timbreTrackIndex = Array.Empty<int>(), timbreEventIndex = Array.Empty<int>();
        public MidiImportSettings importSettings = new MidiImportSettings();
        public string[] trackNames = Array.Empty<string>();
        public string[] diagnostics = Array.Empty<string>();
        public string sourceHash;
        public string importedSettingsHash;
        [TextArea] public string lastImportError;
        public bool importFailed;
        public int tempoChangeCount, maxKeyPolyphony, maxActivePolyphony;
        public long estimatedDataBytes;
        public int packedDataBytes;
    }
}
