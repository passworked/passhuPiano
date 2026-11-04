using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Audio
{
    // One allocator owns the existing 64 sources. Sample voices use one source;
    // strings use a dynamically allocated attack/body pair, drums retain their partition.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiEnsembleVoicePool : UdonSharpBehaviour
    {
        public PianoVoicePool output;
        public MidiInstrumentRouter router;
        public Transform[] soundOrigins = new Transform[0];
        [HideInInspector] public bool[] active = new bool[64];
        [HideInInspector] public bool[] live = new bool[64];
        [HideInInspector] public int[] identities = new int[64];
        [HideInInspector] public int[] targets = new int[64];
        [HideInInspector] public int[] pitches = new int[64];
        [HideInInspector] public int[] velocities = new int[64];
        [HideInInspector] public int[] articulations = new int[64];
        [HideInInspector] public int[] sections = new int[64];
        [HideInInspector] public double[] starts = new double[64];
        [HideInInspector] public float[] levels = new float[64];
        [HideInInspector] public int activeVoiceCount, stolenVoiceCount;
        private int[] _a = new int[64], _b = new int[64], _region = new int[64];
        private int[] _seen = new int[0];
        private int[] _channel = new int[64];
        private float[] _gain = new float[64], _peak = new float[64];
        private double[] _release = new double[64], _end = new double[64], _scheduled = new double[64];
        private float[] _releaseSeconds = new float[64];
        private PianoBankData[] _sampleBank = new PianoBankData[64];
        private StringsBankData[] _stringsBank = new StringsBankData[64];
        private int[] _owners = new int[64];
        private Vector3[] _rest = new Vector3[64];
        private MidiSongData _song;
        private int _generation = -1;
        private bool _ready;
        private float _normalization = 1f;
        private int _outputBatchDepth;
        private bool _outputDirty;
        private int[] _expressionFrame = new int[16], _expressionMs = new int[16];
        private float[] _expressionValue = new float[16];

        public void BeginOutputBatch() { _outputBatchDepth++; }
        public void EndOutputBatch()
        {
            _outputBatchDepth = Mathf.Max(0, _outputBatchDepth - 1);
            if (_outputBatchDepth == 0 && _outputDirty) UpdateOutput();
        }
        private void RequestOutputUpdate()
        {
            if (_outputBatchDepth > 0) _outputDirty = true;
            else UpdateOutput();
        }

        public void Initialize()
        {
            if (_ready) return;
            for (int i = 0; i < 64; i++)
            {
                _owners[i] = -1; _a[i] = -1; _b[i] = -1;
                if (i < output.voiceSources.Length && output.voiceSources[i] != null)
                    _rest[i] = output.voiceSources[i].transform.localPosition;
            }
            router.ResolveSelection(); _ready = true;
        }
        private int Limit()
        {
            int reserved = output.ReservedPercussionSources();
            return Mathf.Max(0, Mathf.Min(output.voiceLimit - reserved,
                output.voiceSources.Length - (reserved > 0 ? 16 : 0), 64));
        }
        public void BindSong(MidiSongData song)
        {
            BeginBindSong(song);
            if (output.drums != null) output.drums.BindSong(song);
        }
        public void BeginBindSong(MidiSongData song)
        {
            Initialize(); StopAll(); _song = song; _generation = -1;
            for (int i = 0; i < 16; i++) _expressionFrame[i] = -1;
            _seen = new int[song == null ? 0 : song.pitch.Length];
            // Facade generations start above zero; fresh arrays are already unseen.
            router.BindSong(song);
        }
        public void CancelGeneration()
        {
            for (int i = 0; i < 64; i++) if (active[i] && !live[i]) StopSlot(i);
            if (output.drums != null) output.drums.CancelSong();
            _generation = -1;
        }
        public void StopAll()
        {
            for (int i = 0; i < 64; i++) StopSlot(i);
        }
        public void ClaimPercussionSources()
        {
            Initialize();
            int first = Mathf.Max(0, output.voiceSources.Length - 16);
            for (int i = 0; i < 64; i++)
                if (active[i] && (_a[i] >= first || _b[i] >= first)) StopSlot(i);
        }
        public void RebuildAt(int ms, int generation)
        {
            CancelGeneration(); _generation = generation;
            if (output.drums != null) output.drums.RebuildAt(ms, generation);
            if (output.clock.GetState() == 1) RecoverAt(ms, generation);
        }
        public void RecoverAt(int ms, int generation)
        {
            if (_song == null || generation != _generation || output.clock.GetState() != 1) return;
            BeginOutputBatch();
            // Include long held notes, then recent released tails; never replay history.
            for (int i = 0; i < router.facade.sequencer.activeNoteCount; i++)
                StartNote(router.facade.sequencer.activeNoteIds[i], ms, false);
            int e = _song.UpperBoundEvent(Mathf.Max(-1, ms - router.MaxReleaseMs() - 1));
            for (; e < _song.eventMs.Length && _song.eventMs[e] <= ms; e++)
                if (_song.eventKind[e] == 2) StartNote(_song.eventNoteId[e], ms, false);
            if (output.drums != null) output.drums.RecoverAt(ms, generation);
            EndOutputBatch();
        }
        public void PrepareWindow(int from, int to, int generation)
        {
            if (_song == null || generation != _generation || output.clock.GetState() != 1) return;
            BeginOutputBatch();
            if (output.drums != null) output.drums.PrepareWindow(from, to, generation);
            for (int n = LowerStart(from + 1); n < _song.startMs.Length && _song.startMs[n] <= to; n++) StartNote(n, from, true);
            EndOutputBatch();
        }
        public void CatchUpRecentNotes(int from, int ms, int generation)
        {
            if (_song == null || generation != _generation) return;
            BeginOutputBatch();
            for (int n = LowerStart(from + 1); n < _song.startMs.Length && _song.startMs[n] <= ms; n++) StartNote(n, ms, true);
            if (output.drums != null) output.drums.CatchUpRecentNotes(from, ms, generation);
            EndOutputBatch();
        }
        public void NoteDown(int note, int ms, int generation)
        {
            if (_song == null || generation != _generation) return;
            if (router.IsFileDrum(note)) { if (output.drums != null) output.drums.NoteDown(note, ms, generation); return; }
            StartNote(note, ms, true);
        }
        private int LowerStart(int ms)
        {
            int lo = 0, hi = _song.startMs.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_song.startMs[mid] < ms) lo = mid + 1; else hi = mid; }
            return lo;
        }
        private PianoBankData SampleBank(int target)
        {
            return router.facade.GetInstrumentBank(target == 0 ? router.pianoAppearance : target == 7 ? 8 : 2);
        }
        public int StringsBankIndex(int target) { return target == 2 ? 0 : target == 6 ? 1 : target - 1; }
        public int EnsembleSection(int channel, int track)
        {
            var voices = output.strings;
            if (track >= 0 && track < voices.trackSections.Length && voices.trackSections[track] >= 0)
                return Mathf.Clamp(voices.trackSections[track], 0, 4);
            return channel >= 0 && channel < voices.channelSections.Length ? Mathf.Clamp(voices.channelSections[channel], 0, 4) : 0;
        }
        private void StartNote(int note, int ms, bool attack)
        {
            if (note < 0 || note >= _seen.Length || _seen[note] == _generation) return;
            int target = router.TargetForNote(note);
            if (target < 0) return;
            double age = System.Math.Max(0, (ms - _song.startMs[note]) * .001);
            int style = router.ArticulationForNote(target, note);
            int slot = StartVoice(target, router.PitchForNote(note), _song.velocity[note], _song.channel[note], style,
                note, _song.trackIndex[note], false, age, (ms - _song.releaseMs[note]) * .001, (_song.startMs[note] - ms) * .001,
                attack);
            if (slot >= 0) _seen[note] = _generation;
        }
        public void LiveNoteDown(int id, int pitch, int velocity)
        {
            Initialize();
            int direct = router.facade.LiveTargetOverride(id);
            int target = direct == 0 ? router.ResolveProgramPitch(router.livePrograms[id / 128], pitch)
                : router.IsEnabled(direct - 1) ? direct - 1 : -1;
            router.liveTargets[id] = target;
            if (target < 0) return;
            int style = router.ArticulationForTarget(target, direct == 0 ? router.livePrograms[id / 128] : 40);
            StartVoice(target, pitch, velocity, id / 128, style, id, -1, true, 0, -1000000, 0, true);
        }
        public void RefreshStringArticulation(int target)
        {
            Initialize();
            if (output.clock.GetState() == 2) return;
            double dsp = output.clock.GetFrameDspSeconds();
            BeginOutputBatch();
            for (int i = 0; i < 64; i++) if (active[i] && targets[i] == target)
            {
                int id = identities[i], channel = _channel[i], pitch = pitches[i], velocity = velocities[i];
                bool isLive = live[i];
                int program = isLive ? (router.facade.LiveTargetOverride(id) == 0 ? router.livePrograms[channel] : 40) : router.notePrograms[id];
                int style = isLive ? router.ArticulationForTarget(target, program) : router.ArticulationForNote(target, id);
                if (style == articulations[i]) continue;
                double age = System.Math.Max(0, dsp - starts[i]), ahead = System.Math.Max(0, _scheduled[i] - dsp), release = _release[i];
                int track = isLive ? -1 : _song.trackIndex[id];
                StopSlot(i);
                int slot = StartVoice(target, pitch, velocity, channel, style, id, track, isLive, age, dsp - release, ahead, false);
                if (slot >= 0) { _release[slot] = release; ApplyLevel(slot, dsp); }
            }
            EndOutputBatch();
        }
        public void LiveNoteRelease(int id)
        {
            double dsp = output.clock.GetFrameDspSeconds();
            for (int i = 0; i < 64; i++) if (active[i] && live[i] && identities[i] == id && _release[i] > dsp) _release[i] = dsp;
        }
        public void LiveAllSoundOff(int channel)
        {
            for (int i = 0; i < 64; i++) if (active[i] && live[i] && (channel < 0 || _channel[i] == channel)) StopSlot(i);
            if (output.drums != null && (channel < 0 || channel == 9)) output.drums.StopLive();
        }
        private int StartVoice(int target, int pitch, int velocity, int channel, int style, int identity, int track,
            bool isLive, double age, double releasedAge, double ahead, bool attack)
        {
            double dsp = output.clock.GetFrameDspSeconds();
            PianoBankData sample = null;
            StringsBankData str = null;
            int region;
            int section = target == 6 ? EnsembleSection(channel, track) : 0;
            if ((target < 2 || target == 7))
            {
                sample = SampleBank(target); if (sample == null) return -1;
                region = sample.SelectRegion(pitch, velocity);
                if (region < 0 || region >= sample.clips.Length || sample.clips[region] == null) return -1;
            }
            else
            {
                if (output.strings == null) return -1;
                int bank = StringsBankIndex(target);
                if (target == 6) bank = section < 2 ? 1 : section;
                if (bank < 0 || bank >= output.strings.banks.Length) return -1;
                str = output.strings.banks[bank]; if (str == null) return -1;
                region = str.SelectRegion(pitch, style);
                if (region < 0 || str.attacks[region] == null) return -1;
            }
            // Newly due notes always play their attack, even after a stall. Move
            // their audio lifetime with it; recovery still uses the true song age.
            if (attack) { releasedAge -= age; age = 0; }
            float release = (sample != null ? sample.releaseDurationMs[region] : str.releaseMs[region]) * .001f;
            if (!isLive && releasedAge >= release) return -1;
            float root = sample != null ? sample.rootPitch[region] + sample.tuningCents[region] * .01f : str.roots[region];
            // Sample tuning adds to playback pitch, rather than moving the root upward.
            float ratio = sample != null ? Mathf.Pow(2, (pitch - sample.rootPitch[region]) / 12f + sample.tuningCents[region] / 1200f) : Mathf.Pow(2, (pitch - root) / 12f);
            AudioClip clip = sample != null ? sample.clips[region] : str.attacks[region];
            AudioClip loop = str == null ? null : str.loops[region];
            if (loop == null && age * ratio >= clip.length) return -1;
            int needed = (target < 2 || target == 7) ? 1 : 2;
            int slot = Allocate(needed, ahead > .001); if (slot < 0) return -1;
            int first = FreeSource(); _owners[first] = slot;
            int second = -1;
            if (needed == 2) { second = FreeSource(); _owners[second] = slot; }
            _a[slot] = first; _b[slot] = second; targets[slot] = target; _region[slot] = region;
            identities[slot] = identity; pitches[slot] = pitch; velocities[slot] = velocity;
            articulations[slot] = style; _channel[slot] = channel; live[slot] = isLive; _sampleBank[slot] = sample;
            sections[slot] = section; _stringsBank[slot] = str;
            _gain[slot] = sample != null ? (sample.velocityLayerCount == 4 ? 1 : Mathf.Pow(velocity / 127f, 1.6f)) * sample.regionGain[region]
                : Mathf.Pow(velocity / 127f, 1.2f) * str.gains[region];
            _peak[slot] = sample != null ? sample.sourcePeak[region] : str.peaks[region];
            _releaseSeconds[slot] = Mathf.Max(.001f, release);
            _release[slot] = isLive ? double.MaxValue : dsp - releasedAge;
            double sampleAge = attack ? 0 : age;
            double start = dsp + System.Math.Max(0, ahead);
            _scheduled[slot] = start; starts[slot] = start - age;
            _end[slot] = loop == null ? start + clip.length / ratio - sampleAge : double.MaxValue;
            active[slot] = true;
            ConfigureSource(first, slot, clip, ratio, false);
            AudioSource a = output.voiceSources[first];
            if (sampleAge * ratio < clip.length)
            {
                a.timeSamples = Mathf.Clamp((int)(sampleAge * ratio * clip.frequency), 0, clip.samples - 1);
                if (start > dsp + .001) a.PlayScheduled(start); else a.Play();
            }
            if (second >= 0)
            {
                ConfigureSource(second, slot, loop, ratio, true);
                if (loop != null)
                {
                    AudioSource b = output.voiceSources[second];
                    double offset = System.Math.Max(0, sampleAge * ratio - clip.length);
                    b.timeSamples = Mathf.Clamp((int)((offset % loop.length) * loop.frequency), 0, loop.samples - 1);
                    b.PlayScheduled(System.Math.Max(start, start + clip.length / ratio - sampleAge));
                }
            }
            ApplyLevel(slot, dsp);
            RequestOutputUpdate(); return slot;
        }
        private void ConfigureSource(int index, int slot, AudioClip clip, float ratio, bool loop)
        {
            AudioSource source = output.voiceSources[index]; source.Stop(); source.volume = 0f; source.clip = clip;
            source.pitch = ratio; source.loop = loop; source.spatialBlend = output.worldSpaceAudio ? 1 : 0; source.dopplerLevel = 0;
            Transform origin = soundOrigins[targets[slot]];
            if (targets[slot] == 6) origin = router.facade.GetStringsView(7).bodies[sections[slot]];
            if (origin != null) source.transform.position = origin.position;
        }
        private int FreeSource()
        {
            for (int i = 0; i < Limit(); i++) if (_owners[i] < 0 && output.voiceSources[i] != null) return i;
            return -1;
        }
        private int Allocate(int needed, bool future)
        {
            int limit = Limit(); if (limit < needed) return -1;
            for (int pass = 0; pass < 65; pass++)
            {
                int free = 0, slot = -1;
                for (int i = 0; i < limit; i++) if (_owners[i] < 0 && output.voiceSources[i] != null) free++;
                for (int i = 0; i < 64; i++) if (!active[i]) { slot = i; break; }
                if (free >= needed && slot >= 0) return slot;
                if (future) return -1;
                int victim = -1;
                for (int i = 0; i < 64; i++)
                    if (active[i] && (victim < 0 || Worse(i, victim))) victim = i;
                if (victim < 0) return -1;
                // A future reservation stolen before it starts remains eligible at its due time.
                if (!live[victim] && _scheduled[victim] > output.clock.GetFrameDspSeconds()) _seen[identities[victim]] = -1;
                StopSlot(victim); stolenVoiceCount++;
            }
            return -1;
        }
        private bool Worse(int a, int b)
        {
            double dsp = output.clock.GetFrameDspSeconds();
            bool ar = _release[a] <= dsp, br = _release[b] <= dsp;
            if (ar != br) return ar;
            bool aa = dsp - starts[a] < .12, ba = dsp - starts[b] < .12;
            if (aa != ba) return !aa;
            if (System.Math.Abs(starts[a] - starts[b]) > .12) return starts[a] < starts[b];
            // Output is batched, but stealing still compares the current envelope.
            return EvaluateLevel(a, dsp) < EvaluateLevel(b, dsp);
        }
        // The pre-attack pass only frees expired sources. Envelopes and output are
        // evaluated once after all due events and look-ahead reservations are applied.
        public void ReclaimEndedVoices(int ms, int generation)
        {
            Initialize();
            if (output.drums != null) output.drums.ReclaimEndedVoices();
            double dsp = output.clock.GetFrameDspSeconds();
            int limit = Limit();
            for (int i = 0; i < 64; i++) if (active[i])
                if ((!live[i] && (generation != _generation || output.clock.GetState() != 1)) || _a[i] >= limit || _b[i] >= limit
                    || dsp >= _end[i] || dsp >= _release[i] + _releaseSeconds[i]) StopSlot(i);
        }
        public void UpdateVoices(int ms, int generation)
        {
            Initialize(); if (output.drums != null) output.drums.UpdateVoices(ms, generation);
            double dsp = output.clock.GetFrameDspSeconds(); activeVoiceCount = 0;
            for (int i = 0; i < 64; i++)
            {
                if (!active[i]) continue;
                if ((!live[i] && (generation != _generation || output.clock.GetState() != 1)) || _a[i] >= Limit() || _b[i] >= Limit()
                    || dsp >= _end[i] || dsp >= _release[i] + _releaseSeconds[i]) { StopSlot(i); continue; }
                ApplyLevel(i, dsp); activeVoiceCount++;
            }
            RequestOutputUpdate();
            output.activeVoiceCount = activeVoiceCount + (output.drums == null ? 0 : output.drums.activeVoiceCount);
            output.stolenVoiceCount = stolenVoiceCount; output.effectiveVoiceLimit = Limit(); output.pendingStartCount = 0;
        }
        private void ApplyLevel(int slot, double dsp)
        {
            float phase = Mathf.Clamp01((float)((dsp - _release[slot]) / _releaseSeconds[slot]));
            levels[slot] = EvaluateLevel(slot, dsp);
            int priority = dsp - starts[slot] < .12 ? 32 + (127 - velocities[slot]) / 4 : phase > 0 ? 200 : 96 + Mathf.Clamp((int)((dsp - starts[slot]) * 4), 0, 63);
            output.voiceSources[_a[slot]].priority = priority;
            if (_b[slot] >= 0) output.voiceSources[_b[slot]].priority = priority;
        }
        private float EvaluateLevel(int slot, double dsp)
        {
            float phase = Mathf.Clamp01((float)((dsp - _release[slot]) / _releaseSeconds[slot]));
            float expression = live[slot] ? router.facade.LiveExpression(_channel[slot]) / 127f
                : SongExpression(_channel[slot], (int)(output.clock.GetPositionSeconds() * 1000));
            float onset = targets[slot] >= 2 && targets[slot] <= 5 ? Mathf.Clamp01((float)((dsp - starts[slot]) / .008)) : 1;
            if (dsp < _scheduled[slot]) onset = 1;
            return _gain[slot] * (1 - phase) * (1 - phase) * expression * onset;
        }
        public void UpdateOutput()
        {
            _outputDirty = false;
            double dsp = output.clock.GetFrameDspSeconds();
            float power = 0f;
            float peakSum = output.drums == null ? 0f : output.drums.PeakSum();
            float maxLevel = output.drums == null ? 0f : output.drums.MaxLevel();
            for (int i = 0; i < 64; i++) if (active[i])
            {
                peakSum += levels[i] * _peak[i];
                maxLevel = Mathf.Max(maxLevel, levels[i]);
            }
            if (output.peakProtectionEnabled)
            {
                power = output.drums == null ? 0f : output.drums.EnergySum(dsp);
                for (int i = 0; i < 64; i++) if (active[i] && dsp >= _scheduled[i])
                {
                    float sourceSeconds = (float)(dsp - starts[i]) * output.voiceSources[_a[i]].pitch;
                    float energy = _sampleBank[i] != null ? _sampleBank[i].EnergyAt(_region[i], sourceSeconds)
                        : _stringsBank[i].EnergyAt(_region[i], sourceSeconds);
                    power += levels[i] * levels[i] * energy;
                }
            }
            _normalization = output.LimitOutputGain(output.CalculateCompressionGain(power, dsp), peakSum, maxLevel, dsp);
            for (int i = 0; i < 64; i++) if (active[i])
                ApplySlotVolume(i);
            if (output.drums != null) output.drums.ApplyOutputGains(_normalization);
        }
        private void ApplySlotVolume(int slot)
        {
            float volume = output.muted ? 0 : Mathf.Clamp01(output.localVolume * levels[slot] * _normalization);
            output.voiceSources[_a[slot]].volume = volume;
            if (_b[slot] >= 0) output.voiceSources[_b[slot]].volume = volume;
        }
        private float SongExpression(int channel, int ms)
        {
            if (_expressionFrame[channel] != Time.frameCount || _expressionMs[channel] != ms)
            {
                _expressionValue[channel] = _song.ExpressionAt(channel, ms) / 127f;
                _expressionFrame[channel] = Time.frameCount; _expressionMs[channel] = ms;
            }
            return _expressionValue[channel];
        }
        private void StopSlot(int slot)
        {
            if (!active[slot]) return;
            ReleaseSource(_a[slot]); ReleaseSource(_b[slot]);
            active[slot] = false; levels[slot] = 0; _a[slot] = _b[slot] = -1;
        }
        public void RelocateSources()
        {
            for (int i = 0; i < 64; i++) if (active[i] && soundOrigins[targets[i]] != null)
            {
                Transform origin = targets[i] == 6 ? router.facade.GetStringsView(7).bodies[sections[i]] : soundOrigins[targets[i]];
                output.voiceSources[_a[i]].transform.position = origin.position;
                if (_b[i] >= 0) output.voiceSources[_b[i]].transform.position = origin.position;
            }
        }
        private void ReleaseSource(int index)
        {
            if (index < 0 || index >= output.voiceSources.Length) return;
            AudioSource source = output.voiceSources[index]; source.Stop(); source.volume = 0;
            source.transform.localPosition = _rest[index]; _owners[index] = -1;
        }
    }
}
