using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Logic
{
    // Target IDs are independent of the legacy menu/model indices:
    // piano, marimba, solo violin, viola, cello, contrabass. ID 6 is retired.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiInstrumentRouter : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public int enabledMask = 1;
        public int pianoAppearance = 1;
        public int[] programGroup = new int[0];
        public int[] priorityOrder = new int[0];
        public string rulesHash;
        // Empty means automatic. Otherwise 128 digits: 0 automatic, 1..6 target+1.
        public string fileProgramMap = "";
        // Two bits per target 2..5: automatic, sustain, spiccato, pizzicato.
        public int stringArticulationOverrides;
        public int[] stringRangeLow = { 55, 48, 36, 24 };
        public int[] stringRangeHigh = { 96, 86, 77, 60 };
        [HideInInspector] public bool[] usedFilePrograms = new bool[128];
        private int[] _fileTargets = new int[128];
        private int[] _pitchTargets = new int[128];
        public Vector3[] viewRestPositions = new Vector3[0];
        public Vector3[] stageOffsets = new Vector3[0];
        [HideInInspector] public int[] notePrograms = new int[0];
        [HideInInspector] public int[] resolvedPrograms = new int[128];
        [HideInInspector] public int[] keyCounts = new int[896];
        [HideInInspector] public int[] livePrograms = new int[16];
        [HideInInspector] public int[] liveTargets = new int[2048];
        private MidiSongData _song;
        private int[] _filePrograms = new int[16];
        private int _bindEvent, _bindTimbre;
        private bool _bound;
        public bool IsBoundSong(MidiSongData song) { return _song == song; }
        public bool IsSongReady(MidiSongData song) { return _song == song && _bound; }

        public int TargetForView(int index) { return index < 2 ? 0 : index - 1; }
        public bool IsEnabled(int target) { return target >= 0 && target < 6 && (enabledMask & (1 << target)) != 0; }
        public bool IsViewEnabled(int index)
        {
            return IsEnabled(TargetForView(index)) && (index > 1 || index == pianoAppearance);
        }

        public void ResolveSelection()
        {
            for (int p = 0; p < 128; p++)
            {
                int target = -1;
                if (programGroup.Length == 128)
                {
                    int start = programGroup[p] * 6;
                    for (int rank = 0; rank < 6 && start + rank < priorityOrder.Length; rank++)
                    {
                        int candidate = priorityOrder[start + rank];
                        if (IsEnabled(candidate)) { target = candidate; break; }
                    }
                }
                resolvedPrograms[p] = target;
                _fileTargets[p] = fileProgramMap.Length == 128 ? int.Parse(fileProgramMap.Substring(p, 1)) - 1 : -1;
            }
            for (int pitch = 0; pitch < 128; pitch++)
            {
                int target = -1;
                // Adjacent members of the actual selection determine each boundary.
                for (int t = 2; t < 6; t++) if (IsEnabled(t))
                {
                    if (target < 0) target = t;
                    else
                    {
                        int boundaryTwice = stringRangeLow[target - 2] + stringRangeHigh[t - 2];
                        if (pitch * 2 > boundaryTwice) break;
                        target = t; // Exact midpoint belongs to the lower instrument.
                    }
                }
                _pitchTargets[pitch] = target;
            }
        }

        public int ResolveProgram(int program) { return resolvedPrograms[Mathf.Clamp(program, 0, 127)]; }
        public int ResolveProgramPitch(int program, int pitch)
        {
            int p = Mathf.Clamp(program, 0, 127), target = ResolveProgram(p);
            // Preserve an available, explicitly authored solo string timbre.
            if (p >= 40 && p <= 43 && IsEnabled(p - 38)) return p - 38;
            return target >= 2 ? _pitchTargets[Mathf.Clamp(pitch, 0, 127)] : target;
        }
        public int FileProgramTarget(int program) { return _fileTargets[Mathf.Clamp(program, 0, 127)]; }
        public bool IsValidFileProgramMap(string map)
        {
            if (map == "") return true;
            if (map == null || map.Length != 128) return false;
            for (int i = 0; i < 128; i++) if (!"0123456".Contains(map.Substring(i, 1))) return false;
            return true;
        }
        public int TargetForNote(int note)
        {
            if (_song == null || note < 0 || note >= notePrograms.Length || _song.channel[note] == 9) return -1;
            int program = notePrograms[note], target = FileProgramTarget(program);
            return target < 0 ? ResolveProgramPitch(program, _song.pitch[note]) : IsEnabled(target) ? target : -1;
        }
        public int ArticulationForProgram(int program)
        {
            if ((program >= 32 && program <= 34) || program == 36 || program == 37 || program == 45) return 2;
            if (program == 55) return 1;
            return facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation;
        }
        public int StringArticulationChoice(int target)
        { return target < 2 || target > 5 ? 0 : (stringArticulationOverrides >> ((target - 2) * 2)) & 3; }
        public int ArticulationForTarget(int target, int program)
        {
            int choice = StringArticulationChoice(target);
            return choice == 0 ? ArticulationForProgram(program) : choice - 1;
        }

        public void BindSong(MidiSongData song)
        {
            if (IsSongReady(song)) { ResolveSelection(); return; }
            BeginBindSong(song);
            while (!BindStep(512)) { }
        }
        public void BeginBindSong(MidiSongData song)
        {
            _song = song;
            notePrograms = new int[song == null ? 0 : song.pitch.Length];
            usedFilePrograms = new bool[128];
            _filePrograms = new int[16]; _bindEvent = 0; _bindTimbre = 0; _bound = song == null;
        }
        public bool BindStep(int budget)
        {
            if (_bound || _song == null) return true;
            // eventIndex preserves original same-tick ordering, including control-only tracks.
            // Timbre records also consume budget, even when many share one event boundary.
            while (budget-- > 0 && _bindEvent < _song.eventMs.Length)
            {
                if (_bindTimbre < _song.timbreEventIndex.Length && _song.timbreEventIndex[_bindTimbre] <= _bindEvent)
                {
                    if (_song.timbreKind[_bindTimbre] == 0)
                        _filePrograms[_song.timbreChannel[_bindTimbre]] = _song.timbreValue[_bindTimbre];
                    _bindTimbre++; continue;
                }
                if (_song.eventKind[_bindEvent] == 0)
                {
                    int n = _song.eventNoteId[_bindEvent];
                    notePrograms[n] = _filePrograms[_song.channel[n]];
                    if (_song.channel[n] != 9) usedFilePrograms[notePrograms[n]] = true;
                }
                _bindEvent++;
            }
            _bound = _bindEvent >= _song.eventMs.Length;
            if (_bound) ResolveSelection();
            return _bound;
        }

        public int MaxReleaseMs()
        {
            if (_maxReleaseMs >= 0) return _maxReleaseMs;
            int tail = 0;
            if (facade == null) return 0;
            for (int target = 0; target < 2; target++)
            {
                var bank = facade.GetInstrumentBank(target == 0 ? pianoAppearance : 2);
                if (bank != null) tail = Mathf.Max(tail, bank.maxReleaseMs);
            }
            if (facade.voicePool.strings != null)
                foreach (var bank in facade.voicePool.strings.banks)
                    if (bank != null) foreach (int release in bank.releaseMs) tail = Mathf.Max(tail, release);
            // Stable across selections, so a checkbox cannot clamp or rewind the transport.
            _maxReleaseMs = tail; return tail;
        }
        private int _maxReleaseMs = -1;

        public void RefreshKeyCounts(int ms, bool live)
        {
            for (int i = 0; i < keyCounts.Length; i++) keyCounts[i] = 0;
            if (live)
            {
                for (int id = 0; id < 2048; id++)
                    if (id / 128 != 9 && facade.IsLiveKeyHeld(id))
                    {
                        int target = liveTargets[id];
                        if (IsEnabled(target)) keyCounts[target * 128 + id % 128]++;
                    }
                return;
            }
            if (_song == null) return;
            for (int i = 0; i < facade.sequencer.activeNoteCount; i++)
            {
                int n = facade.sequencer.activeNoteIds[i], target = TargetForNote(n);
                if (target >= 0 && _song.keyUpMs[n] > ms) keyCounts[target * 128 + _song.pitch[n]]++;
            }
        }
    }
}
