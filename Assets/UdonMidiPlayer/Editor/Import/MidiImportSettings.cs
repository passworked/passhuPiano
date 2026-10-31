using System;

namespace MusicMidiPlayer.Editor.Import
{
    [Serializable]
    public sealed class MidiImportSettings
    {
        public int[] selectedTracks = Array.Empty<int>();
        public bool excludePercussion = true;
        public int maxNotes = 250000;
        public int maxCheckpointEntries = 2000000;
        public long maxDataBytes = 32L * 1024 * 1024;

        public bool IncludesTrack(int track)
        {
            if (selectedTracks == null || selectedTracks.Length == 0) return true;
            return Array.IndexOf(selectedTracks, track) >= 0;
        }
    }

    public sealed class MidiImportResult
    {
        public int timbreDataVersion = 1;
        public int[] timbreMs = Array.Empty<int>(), timbreChannel = Array.Empty<int>(), timbreKind = Array.Empty<int>();
        public int[] timbreValue = Array.Empty<int>(), timbreTrackIndex = Array.Empty<int>(), timbreEventIndex = Array.Empty<int>();
        public int[] expressionMs = Array.Empty<int>(), expressionValue = Array.Empty<int>();
        public int[] expressionOffsets = new int[17];
        public int format;
        public int ppq;
        public int[] measureStartMs = new int[0];
        public int durationMs;
        public int tempoChangeCount;
        public int maxKeyPolyphony;
        public int maxActivePolyphony;
        public int skippedPercussion;
        // Kept for callers of older import results. All valid MIDI pitches (0..127) are now retained.
        public int skippedOutOfRange;
        public int repairedKeyUps;
        public int repairedReleases;
        public long estimatedDataBytes;
        public string[] trackNames;
        public string[] diagnostics;
        public int[] startMs, keyUpMs, releaseMs, pitch, velocity, channel, trackIndex;
        public int[] eventMs, eventKind, eventNoteId;
        public int[] checkpointEventIndex, checkpointActiveOffsets, checkpointActiveNoteIds;
        // Physical left to right: CC67 una corda, CC66 sostenuto, CC64 sustain.
        public int[] pedalMs, pedalController, pedalValue, pedalOffsets;
    }

    public sealed class MidiImportException : Exception
    {
        public MidiImportException(string message) : base(message) { }
    }
}
