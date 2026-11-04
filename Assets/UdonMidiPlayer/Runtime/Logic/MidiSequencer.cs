using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Audio;

namespace MusicMidiPlayer.Logic
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Logic/Sequencer")]
    public class MidiSequencer : UdonSharpBehaviour
    {
        public PianoVoicePool voicePool;
        public MusicMidiPlayer.Views.ClockworkMusicLink clockworkMusicLink;
        [HideInInspector] public MidiSongData song;
        [HideInInspector] public int[] keyDownCount = new int[88];
        [HideInInspector] public int[] activeNoteIds = new int[0];
        [HideInInspector] public int activeNoteCount;
        [HideInInspector] public int eventCursor;
        [HideInInspector] public int generation;
        [HideInInspector] public bool rebuildRequested;
        private int[] _activeSlot = new int[0];
        private bool[] _held = new bool[0];
        private int _lastPosition;

        public void BindSong(MidiSongData data)
        {
            Clear(generation);
            song = data;
            int count = data == null ? 0 : data.startMs.Length;
            activeNoteIds = new int[count];
            _activeSlot = new int[count];
            _held = new bool[count];
        }

        public void Clear(int nextGeneration)
        {
            for (int i = 0; i < activeNoteCount; i++)
            {
                int id = activeNoteIds[i];
                _activeSlot[id] = 0;
                _held[id] = false;
            }
            for (int i = 0; i < 88; i++) keyDownCount[i] = 0;
            activeNoteCount = 0;
            eventCursor = 0;
            _lastPosition = 0;
            generation = nextGeneration;
            rebuildRequested = false;
        }

        public void RebuildAt(int positionMs, int nextGeneration)
        {
            Clear(nextGeneration);
            if (song == null) return;
            if (positionMs < 0) { _lastPosition = positionMs; return; }
            int t = Mathf.Clamp(positionMs, 0, song.durationMs);
            int checkpoint = t / 1000;
            int checkpointTime = checkpoint * 1000;
            bool hasCheckpoint = checkpoint < song.checkpointEventIndex.Length && checkpoint + 1 < song.checkpointActiveOffsets.Length;
            if (hasCheckpoint)
            {
                int from = song.checkpointActiveOffsets[checkpoint];
                int to = song.checkpointActiveOffsets[checkpoint + 1];
                for (int i = from; i < to; i++)
                {
                    int id = song.checkpointActiveNoteIds[i];
                    RestoreNote(id, t);
                }
            }
            // Restore the final intervals directly. Replaying every on/up/release
            // between the checkpoint and target is expensive in dense MIDI files.
            int first = hasCheckpoint ? LowerBoundStart(checkpointTime) : 0;
            for (int id = first; id < song.startMs.Length && song.startMs[id] <= t; id++)
                RestoreNote(id, t);
            eventCursor = song.UpperBoundEvent(t);
            _lastPosition = positionMs;
        }

        private int LowerBoundStart(int ms)
        {
            int low = 0, high = song.startMs.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (song.startMs[mid] < ms) low = mid + 1; else high = mid;
            }
            return low;
        }

        private void RestoreNote(int id, int ms)
        {
            if (song.releaseMs[id] <= ms || _activeSlot[id] != 0) return;
            AddActive(id);
            if (song.keyUpMs[id] > ms)
            {
                _held[id] = true;
                int key = IsDrum(id) ? -1 : song.pitch[id] - 21;
                if (key >= 0 && key < 88) keyDownCount[key]++;
            }
        }

        public void AdvanceTo(int positionMs)
        {
            if (song == null) return;
            int end = song.UpperBoundEvent(positionMs);
            if (positionMs < _lastPosition)
            {
                rebuildRequested = true;
                return;
            }
            // Consume every due event, including after slow frames and dense batches.
            while (eventCursor < end)
            {
                ApplyEvent(eventCursor, positionMs, true);
                eventCursor++;
            }
            _lastPosition = positionMs;
        }

        private bool IsDrum(int note)
        { return voicePool != null && voicePool.ensemble != null ? voicePool.ensemble.router.IsFileDrum(note) : song.channel[note] == 9; }

        private void ApplyEvent(int index, int positionMs, bool output)
        {
            int id = song.eventNoteId[index];
            int kind = song.eventKind[index];
            int key = IsDrum(id) ? -1 : song.pitch[id] - 21;
            if (kind == 0)
            {
                AddActive(id);
                if (!_held[id])
                {
                    _held[id] = true;
                    if (key >= 0 && key < 88) keyDownCount[key]++;
                }
                if (output && voicePool != null) voicePool.NoteDown(id, positionMs, generation);
                if (output && clockworkMusicLink != null) clockworkMusicLink.FileNote(id, positionMs);
            }
            else if (kind == 1)
            {
                if (_held[id])
                {
                    _held[id] = false;
                    if (key >= 0 && key < 88) keyDownCount[key] = Mathf.Max(0, keyDownCount[key] - 1);
                }
            }
            else
            {
                RemoveActive(id);
                if (output && voicePool != null) voicePool.NoteRelease(id, positionMs, generation);
            }
        }

        private void AddActive(int id)
        {
            if (_activeSlot[id] != 0) return;
            activeNoteIds[activeNoteCount] = id;
            _activeSlot[id] = ++activeNoteCount;
        }

        private void RemoveActive(int id)
        {
            int slot = _activeSlot[id] - 1;
            if (slot < 0) return;
            int lastId = activeNoteIds[--activeNoteCount];
            activeNoteIds[slot] = lastId;
            _activeSlot[lastId] = slot + 1;
            _activeSlot[id] = 0;
        }
    }
}
