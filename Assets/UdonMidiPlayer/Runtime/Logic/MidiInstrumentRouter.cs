using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Logic
{
    // Target IDs are independent of the legacy menu/model indices:
    // piano, marimba, solo violin, viola, cello, contrabass. ID 6 is retired; music box is target 7 (view 8).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiInstrumentRouter : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        // Bits 0..5/7 select melodic targets; bit 8 enables the independent drum kit.
        public int enabledMask = 257;
        public int pianoAppearance = 1;
        public int[] programGroup = new int[0];
        public int[] priorityOrder = new int[0];
        public string rulesHash;
        // Empty means automatic. Otherwise 128 digits: 0 automatic, 1..6/8 melodic target+1, 9 drums.
        public string fileProgramMap = "";
        // Legacy: sorted six-character track/target records. Pitch settings use P0/P1
        // (auto-range off/on), then seven-character track/target/octave records.
        // Digits retain enabled targets; a..j encode disabled target+1 (a = automatic).
        // Empty means automatic. Manual track and Program choices also override channel 10.
        public string fileTrackMap = "";
        public bool autoTrackRouting; // Opt in, shared with the playback snapshot.
        public string[] autoTrackKeywords = new string[0];
        public int[] autoTrackPrograms = new int[0];
        private int[] _trackPrograms = new int[0];
        private int[] _trackPitchTotal = new int[0], _trackNoteCount = new int[0];
        private int _bindTrack;
        // Two bits per target 2..5: automatic, sustain, spiccato, pizzicato.
        public int stringArticulationOverrides;
        public int[] stringRangeLow = { 55, 48, 36, 24 };
        public int[] stringRangeHigh = { 96, 86, 77, 60 };
        public int[] nativeRangeLow = { 21, 36, 55, 48, 36, 28, 0, 74 };
        public int[] nativeRangeHigh = { 108, 96, 96, 86, 77, 59, 127, 103 };
        [HideInInspector] public bool[] usedFilePrograms = new bool[128];
        [HideInInspector] public bool[] usedFileTracks = new bool[0];
        private int[] _fileTargets = new int[128];
        private int[] _mappedTracks = new int[0], _fileTrackTargets = new int[0];
        private bool[] _fileTrackEnabled = new bool[0];
        private int[] _fileTrackOctaves = new int[0];
        private bool _fileAutoRange;
        private bool _filePitchAdjusted;
        private int[] _pitchTargets = new int[128];
        public Vector3[] viewRestPositions = new Vector3[0];
        public Vector3[] stageOffsets = new Vector3[0];
        [HideInInspector] public int[] notePrograms = new int[0];
        [HideInInspector] public int[] resolvedPrograms = new int[128];
        [HideInInspector] public int[] keyCounts = new int[1024];
        [HideInInspector] public int[] livePrograms = new int[16];
        [HideInInspector] public int[] liveTargets = new int[2048];
        private MidiSongData _song;
        private int[] _filePrograms = new int[16];
        private int _bindEvent, _bindTimbre;
        private bool _bound;
        public bool IsBoundSong(MidiSongData song) { return _song == song; }
        public bool IsSongReady(MidiSongData song) { return _song == song && _bound; }

        public int TargetForView(int index) { return index < 2 ? 0 : index - 1; }
        public bool IsEnabled(int target) { return target >= 0 && target < 8 && target != 6 && (enabledMask & (1 << target)) != 0; }
        public bool IsDrumsEnabled() { return (enabledMask & 256) != 0; }
        public bool IsViewEnabled(int index)
        {
            return IsEnabled(TargetForView(index)) && (index > 1 || index == pianoAppearance);
        }

        public void ResolveSelection()
        {
            bool extended = fileTrackMap.StartsWith("P");
            int header = extended ? 2 : 0, size = extended ? 7 : 6;
            int mappedCount = (fileTrackMap.Length - header) / size;
            _mappedTracks = new int[mappedCount]; _fileTrackTargets = new int[mappedCount];
            _fileTrackEnabled = new bool[mappedCount];
            _fileTrackOctaves = new int[mappedCount]; _fileAutoRange = FileAutoRange();
            _filePitchAdjusted = _fileAutoRange;
            for (int i = 0; i < mappedCount; i++)
            {
                int offset = header + i * size;
                _mappedTracks[i] = int.Parse(fileTrackMap.Substring(offset, 5));
                string value = fileTrackMap.Substring(offset + 5, 1);
                int digit = "0123456789".IndexOf(value);
                _fileTrackEnabled[i] = digit >= 0;
                _fileTrackTargets[i] = (digit >= 0 ? digit : "abcdefghij".IndexOf(value)) - 1;
                _fileTrackOctaves[i] = extended ? "0123456789ABCDEFGHIJK".IndexOf(fileTrackMap.Substring(offset + 6, 1)) - 10 : 0;
                if (_fileTrackOctaves[i] != 0) _filePitchAdjusted = true;
            }
            for (int p = 0; p < 128; p++)
            {
                int target = -1;
                if (programGroup.Length == 128)
                {
                    int start = programGroup[p] * 7;
                    for (int rank = 0; rank < 7 && start + rank < priorityOrder.Length; rank++)
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
            return target >= 2 && target <= 5 ? _pitchTargets[Mathf.Clamp(pitch, 0, 127)] : target;
        }
        public int FileProgramTarget(int program) { return _fileTargets[Mathf.Clamp(program, 0, 127)]; }
        public int FileTrackTarget(int track)
        {
            int index = FileTrackIndex(track);
            return index < 0 ? -1 : _fileTrackTargets[index];
        }
        public bool IsFileTrackEnabled(int track)
        {
            int index = FileTrackIndex(track);
            return index < 0 || _fileTrackEnabled[index];
        }
        public int FileTrackOctave(int track)
        { int index = FileTrackIndex(track); return index < 0 ? 0 : _fileTrackOctaves[index]; }
        public bool FileAutoRange() { return fileTrackMap.StartsWith("P1"); }
        private int FileTrackIndex(int track)
        {
            int lo = 0, hi = _mappedTracks.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_mappedTracks[mid] < track) lo = mid + 1; else hi = mid;
            }
            return lo < _mappedTracks.Length && _mappedTracks[lo] == track ? lo : -1;
        }
        public bool IsValidFileTrackMap(string map)
        {
            if (map == null) return false;
            bool extended = map.StartsWith("P");
            int header = extended ? 2 : 0, size = extended ? 7 : 6;
            if (map.Length < header || (extended && !"01".Contains(map.Substring(1, 1)))
                || (map.Length - header) % size != 0 || map.Length > header + 65535 * size) return false;
            int previous = -1;
            for (int offset = header; offset < map.Length; offset += size)
            {
                for (int i = 0; i < 5; i++) if (!"0123456789".Contains(map.Substring(offset + i, 1))) return false;
                int track = int.Parse(map.Substring(offset, 5));
                if (track <= previous || track >= 65535 || !(extended ? "012345689abcdefgij" : "12345689abcdefgij").Contains(map.Substring(offset + 5, 1))) return false;
                if (extended && !"0123456789ABCDEFGHIJK".Contains(map.Substring(offset + 6, 1))) return false;
                previous = track;
            }
            return true;
        }
        public string WithFileTrackTarget(int track, int target)
        { return WithFileTrackState(track, target, IsFileTrackEnabled(track), FileTrackOctave(track)); }
        public string WithFileTrackEnabled(int track, bool enabled)
        { return WithFileTrackState(track, FileTrackTarget(track), enabled, FileTrackOctave(track)); }
        public string WithFileTrackOctave(int track, int octave)
        { return WithFileTrackState(track, FileTrackTarget(track), IsFileTrackEnabled(track), Mathf.Clamp(octave, -10, 10)); }
        private string ExtendedTrackMap()
        {
            if (fileTrackMap.StartsWith("P")) return fileTrackMap;
            string map = "P0";
            for (int offset = 0; offset < fileTrackMap.Length; offset += 6) map += fileTrackMap.Substring(offset, 6) + "A";
            return map;
        }
        private string CompactTrackMap(string map)
        {
            if (map.StartsWith("P1")) return map;
            for (int offset = 2; offset < map.Length; offset += 7) if (map.Substring(offset + 6, 1) != "A") return map;
            string legacy = "";
            for (int offset = 2; offset < map.Length; offset += 7)
                if (map.Substring(offset + 5, 1) != "0") legacy += map.Substring(offset, 6);
            return legacy;
        }
        public string WithFileAutoRange(bool enabled)
        { return CompactTrackMap((enabled ? "P1" : "P0") + ExtendedTrackMap().Substring(2)); }
        public string FileRoutingMap(string map)
        {
            if (!map.StartsWith("P")) return map;
            string routing = "";
            for (int offset = 2; offset < map.Length; offset += 7)
                if (map.Substring(offset + 5, 1) != "0") routing += map.Substring(offset, 6);
            return routing;
        }
        private string WithFileTrackState(int track, int target, bool enabled, int octave)
        {
            string map = ExtendedTrackMap();
            int offset = 2;
            while (offset < map.Length && int.Parse(map.Substring(offset, 5)) < track) offset += 7;
            bool exists = offset < map.Length && int.Parse(map.Substring(offset, 5)) == track;
            string entry = target < 0 && enabled && octave == 0 ? "" : track.ToString("00000")
                + (enabled ? (target + 1).ToString() : "abcdefghij".Substring(target + 1, 1))
                + "0123456789ABCDEFGHIJK".Substring(octave + 10, 1);
            return CompactTrackMap(map.Substring(0, offset) + entry + map.Substring(offset + (exists ? 7 : 0)));
        }
        // Choose the destination using original metadata first; pitch adjustment never reroutes it.
        public int PitchForNote(int note)
        {
            // Keep the original hot path cheap when both pitch features are unused.
            if (!_filePitchAdjusted) return _song.pitch[note];
            int target = TargetForNote(note);
            if (target < 0) return _song.pitch[note];
            return AdjustFilePitch(_song.pitch[note], target, FileTrackOctave(_song.trackIndex[note]));
        }
        public int AdjustFilePitch(int pitch, int target, int octave)
        {
            int low = 0, high = 127;
            if (_fileAutoRange && target >= 0 && target < nativeRangeLow.Length && target < nativeRangeHigh.Length)
            { low = nativeRangeLow[target]; high = nativeRangeHigh[target]; }
            int shifted = pitch + Mathf.Clamp(octave, -10, 10) * 12;
            if (shifted < low) shifted += ((low - shifted + 11) / 12) * 12;
            if (shifted > high) shifted -= ((shifted - high + 11) / 12) * 12;
            return shifted;
        }
        public string FileTrackName(int track)
        {
            if (_song == null || track < 0 || track >= _song.trackNames.Length) return "";
            string name = _song.trackNames[track];
            return name == "Track " + track.ToString() ? "" : name;
        }
        public bool IsValidFileProgramMap(string map)
        {
            if (map == "") return true;
            if (map == null || map.Length != 128) return false;
            for (int i = 0; i < 128; i++) if (!"012345689".Contains(map.Substring(i, 1))) return false;
            return true;
        }
        public int TargetForNote(int note)
        {
            if (_song == null || note < 0 || note >= notePrograms.Length
                || !IsFileTrackEnabled(_song.trackIndex[note]) || IsFileDrum(note)) return -1;
            int program = notePrograms[note], target = FileTrackTarget(_song.trackIndex[note]);
            if (target < 0) target = FileProgramTarget(program);
            if (target >= 0) return IsEnabled(target) ? target : -1;
            int automatic = AutoTrackProgram(_song.trackIndex[note]);
            if (automatic >= 0) program = automatic;
            // Bass remains a bass part, even when its written notes jump an octave.
            return automatic == 32 ? ResolveProgram(program) : ResolveProgramPitch(program, _song.pitch[note]);
        }
        public int ClassifyTrack(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            name = name.ToLowerInvariant();
            int earliest = int.MaxValue, result = -1;
            for (int rule = 0; rule < autoTrackKeywords.Length && rule < autoTrackPrograms.Length; rule++)
            {
                string[] words = autoTrackKeywords[rule].Split('|');
                for (int i = 0; i < words.Length; i++)
                {
                    int at = name.IndexOf(words[i]);
                    // Match authored words, not fragments inside unrelated names.
                    if (at < 0 || at >= earliest || (at > 0 && IsAsciiLetter(name.Substring(at - 1, 1)))) continue;
                    int end = at + words[i].Length;
                    if (end < name.Length && IsAsciiLetter(name.Substring(end, 1))) continue;
                    earliest = at; result = autoTrackPrograms[rule];
                }
            }
            return result;
        }
        private bool IsAsciiLetter(string letter) { return "abcdefghijklmnopqrstuvwxyz".Contains(letter); }
        public int AutoTrackProgram(int track)
        { return autoTrackRouting && track >= 0 && track < _trackPrograms.Length ? _trackPrograms[track] : -1; }
        public int AutoTrackTarget(int track)
        {
            int program = AutoTrackProgram(track);
            if (program < 0 || program >= 128) return -1;
            int pitch = _trackNoteCount[track] == 0 ? 60 : _trackPitchTotal[track] / _trackNoteCount[track];
            return program == 32 ? ResolveProgram(program) : ResolveProgramPitch(program, pitch);
        }
        public bool IsFileDrum(int note)
        {
            if (_song == null || note < 0 || note >= notePrograms.Length) return false;
            if (!IsFileTrackEnabled(_song.trackIndex[note])) return false;
            int target = FileTrackTarget(_song.trackIndex[note]);
            if (target < 0) target = FileProgramTarget(notePrograms[note]);
            if (target >= 0) return target == 8; // Classification stays intact while the kit is disabled.
            int program = AutoTrackProgram(_song.trackIndex[note]);
            return program >= 128 || (program < 0 && _song.channel[note] == 9);
        }
        public int FileDrumPitch(int note)
        {
            int pitch = _song.pitch[note], role = AutoTrackProgram(_song.trackIndex[note]);
            if (FileTrackTarget(_song.trackIndex[note]) >= 0 || FileProgramTarget(notePrograms[note]) >= 0) return pitch;
            if (role < 128) return pitch; // Ordinary GM drums retain their authored keys.
            if (role == 129) return 36;
            if (role == 130) return 38;
            if (role == 131) return 42;
            if (role == 132) return 49;
            if (role == 133) return 51;
            if (role == 134) return 45;
            // UAAK's generic Drum parts are pitched piano reductions, not GM kits.
            // Preserve timing/velocity; approximate low-to-high percussion registers.
            return pitch < 36 ? 36 : pitch < 48 ? 38 : pitch < 60 ? 45 : pitch < 72 ? 47 : pitch < 84 ? 42 : 49;
        }
        public int ProgramForNote(int note)
        {
            int automatic = AutoTrackProgram(_song.trackIndex[note]);
            bool manual = FileTrackTarget(_song.trackIndex[note]) >= 0 || FileProgramTarget(notePrograms[note]) >= 0;
            return !manual && automatic >= 0 && automatic < 128 ? automatic : notePrograms[note];
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

        public int ArticulationForNote(int target, int note)
        {
            int program = ProgramForNote(note);
            // Only automatic solo violin uses the Editor's contextual plan. Explicit GM styles win.
            if (target == 2 && StringArticulationChoice(target) == 0 && _song != null
                && (note >> 2) < _song.violinArticulations.Length
                && !((program >= 32 && program <= 34) || program == 36 || program == 37 || program == 45 || program == 55))
            {
                int style = (_song.violinArticulations[note >> 2] >> ((note & 3) * 2)) & 3;
                if (style != 0) return style - 1;
            }
            return ArticulationForTarget(target, program);
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
            usedFileTracks = new bool[song == null ? 0 : song.trackNames.Length];
            _trackPrograms = new int[usedFileTracks.Length];
            _trackPitchTotal = new int[usedFileTracks.Length]; _trackNoteCount = new int[usedFileTracks.Length];
            _bindTrack = 0;
            _filePrograms = new int[16]; _bindEvent = 0; _bindTimbre = 0; _bound = song == null;
        }
        public bool BindStep(int budget)
        {
            if (_bound || _song == null) return true;
            while (budget > 0 && _bindTrack < _trackPrograms.Length)
            { _trackPrograms[_bindTrack] = ClassifyTrack(_song.trackNames[_bindTrack]); _bindTrack++; budget--; }
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
                    int track = _song.trackIndex[n];
                    if (track >= 0 && track < usedFileTracks.Length)
                    { usedFileTracks[track] = true; _trackPitchTotal[track] += _song.pitch[n]; _trackNoteCount[track]++; }
                    usedFilePrograms[notePrograms[n]] = true;
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
            for (int target = 0; target < 3; target++)
            {
                var bank = facade.GetInstrumentBank(target == 0 ? pianoAppearance : target == 1 ? 2 : 8);
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
                if (target >= 0 && _song.keyUpMs[n] > ms) keyCounts[target * 128 + PitchForNote(n)]++;
            }
        }
    }
}
