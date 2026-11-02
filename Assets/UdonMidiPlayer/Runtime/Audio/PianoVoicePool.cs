using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Audio
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Piano Voice Pool")]
    public class PianoVoicePool : UdonSharpBehaviour
    {
        public MusicMidiPlayer.Logic.MidiPracticeController practice;
        public PianoBankData bank;
        public StringsVoicePool strings;
        public MidiEnsembleVoicePool ensemble;
        public PercussionVoicePool drums;
        public MidiTransportClock clock;
        public AudioSource[] voiceSources;
        public const int VoiceCapacity = 64;
        [Range(1, VoiceCapacity)] public int voiceLimit = VoiceCapacity;
        [Range(0f, 2f)] public float localVolume = 0.15f;
        public bool muted;
        public bool worldSpaceAudio = true;
        [Range(0.05f, 1f)] public float peakBudget = 0.8f;
        public bool peakProtectionEnabled;
        public float retireFadeSeconds = 0.005f;
        public bool useDspScheduling = true;
        [HideInInspector] public int activeVoiceCount;
        [HideInInspector] public int pendingStartCount;
        [HideInInspector] public int suppressedNoteCount;
        [HideInInspector] public int stolenVoiceCount;
        [HideInInspector] public int lateStartCount;
        [HideInInspector] public int virtualVoiceCount;
        [HideInInspector] public int effectiveVoiceLimit;

        // The same pool capacity is used on PC and mobile. AudioSource priority
        // still determines which notes survive shared client audio contention.
        private const int AttackProtectionMs = 120;

        // Audio-note state is independent of a reusable AudioSource slot.
        private const int NoteScheduled = 1;
        private const int NoteSounding = 2;
        private const int NotePending = 3;
        private const int NoteSuppressed = 4;
        private const int NoteFinished = 5;
        private const int SlotFree = 0;
        private const int SlotScheduled = 1;
        private const int SlotSounding = 2;
        private const int SlotReleasing = 3;
        private const int SlotRetiring = 4;

        private bool[] _live = new bool[VoiceCapacity];
        private double[] _liveRelease = new double[VoiceCapacity];
        private float[] _liveGain = new float[VoiceCapacity];
        private MidiSongData _song;
        private MidiSequencer _sequencer;
        private int _generation = -1;
        private int _capacity;
        private int[] _noteGeneration;
        private int[] _noteState;
        private int[] _slotState;
        private int[] _slotNote;
        private int[] _slotGeneration;
        private int[] _slotToken;
        private int[] _slotRegion;
        private double[] _slotStartDsp;
        private float[] _slotRawGain;
        private float[] _slotBaseGain;
        private int[] _slotReleaseMs;
        private double[] _slotEndMs;
        private double[] _slotAttackUntilDsp;
        private int[] _slotVelocity;
        private float[] _slotPeak;
        private double[] _retireStart;
        private double[] _retireEnd;
        private float[] _retireGain;
        private int[] _retireToken;
        private int[] _retireGeneration;
        private int[] _pendingNotes;
        private int[] _pendingGeneration;
        private bool[] _pendingAttack;
        private int _pendingCount;
        private float _normalization = 1f;
        private double _lastGainDsp;
        private bool[] _worldSpatialize;
        private bool[] _worldBypassReverbZones;

        public void Initialize()
        {
            int count = voiceSources == null ? 0 : Mathf.Min(VoiceCapacity, voiceSources.Length);
            if (_slotState != null && _capacity == count) return;
            _capacity = count;
            _slotState = new int[count];
            _slotNote = new int[count];
            _slotGeneration = new int[count];
            _slotToken = new int[count];
            _slotRegion = new int[count];
            _slotStartDsp = new double[count];
            _slotRawGain = new float[count];
            _slotBaseGain = new float[count];
            _slotReleaseMs = new int[count];
            _slotEndMs = new double[count];
            _slotAttackUntilDsp = new double[count];
            _slotVelocity = new int[count];
            _slotPeak = new float[count];
            _retireStart = new double[count];
            _retireEnd = new double[count];
            _retireGain = new float[count];
            _retireToken = new int[count];
            _retireGeneration = new int[count];
            _pendingNotes = new int[VoiceCapacity];
            _pendingGeneration = new int[VoiceCapacity];
            _pendingAttack = new bool[VoiceCapacity];
            effectiveVoiceLimit = EffectiveLimit();
            for (int i = 0; i < count; i++)
            {
                _slotNote[i] = -1;
                AudioSource source = voiceSources[i];
                if (source == null) continue;
                source.Stop();
                source.playOnAwake = false;
                source.loop = false;
                source.volume = 0f;
                source.priority = 192;
            }
            SetWorldSpaceAudio(worldSpaceAudio);
        }

        public void SetWorldSpaceAudio(bool enabled)
        {
            worldSpaceAudio = enabled;
            if (voiceSources == null) return;
            if (_worldSpatialize == null || _worldSpatialize.Length != voiceSources.Length)
            {
                _worldSpatialize = new bool[voiceSources.Length];
                _worldBypassReverbZones = new bool[voiceSources.Length];
                for (int i = 0; i < voiceSources.Length; i++)
                {
                    AudioSource source = voiceSources[i];
                    if (source == null) continue;
                    _worldSpatialize[i] = source.spatialize;
                    _worldBypassReverbZones[i] = source.bypassReverbZones;
                }
            }
            for (int i = 0; i < voiceSources.Length; i++)
            {
                AudioSource source = voiceSources[i];
                if (source == null) continue;
                source.spatialBlend = enabled ? 1f : 0f;
                // A zero 3D blend alone leaves the spatializer and position-dependent
                // reverb enabled. Restore each source's world settings on return to 3D.
                source.spatialize = enabled && _worldSpatialize[i];
                source.bypassReverbZones = !enabled || _worldBypassReverbZones[i];
                source.dopplerLevel = 0f;
            }
        }

        public void BindBank(PianoBankData data)
        {
            Initialize();
            bank = data;
        }

        public void BindSong(MidiSongData data, MidiSequencer sequencer)
        {
            BeginBindSong(data, sequencer);
            if (drums != null) drums.BindSong(data);
        }

        // Loading drives the drum index separately under the Facade's time budget.
        public void BeginBindSong(MidiSongData data, MidiSequencer sequencer)
        {
            if (ensemble != null) { ensemble.BeginBindSong(data); return; }
            if (strings != null) strings.BindSong(data, sequencer);
            Initialize();
            if (_generation >= 0) CancelGeneration(_generation);
            _song = data;
            _sequencer = sequencer;
            _generation = -1;
            int count = data == null || data.startMs == null ? 0 : data.startMs.Length;
            _noteGeneration = new int[count];
            _noteState = new int[count];
            // Facade generations are positive, so zero-initialized entries are unseen.
            _pendingCount = 0;
            pendingStartCount = 0;
        }

        public void SetPeakProtection(bool enabled)
        {
            peakProtectionEnabled = enabled;
            if (ensemble != null) { ensemble.UpdateOutput(); return; }
            Initialize();
            ApplyOutputGains(FrameDsp());
        }

        public void SetLocalOutput(float volume, bool mute)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume)) volume = 0f;
            localVolume = Mathf.Clamp(volume, 0f, 2f);
            muted = mute;
            if (ensemble != null) { ensemble.UpdateOutput(); return; }
            Initialize();
            ApplyOutputGains(FrameDsp());
        }

        public void PrepareWindow(int fromExclusiveMs, int toInclusiveMs, int generation,
            double songPositionSeconds, double dspNowSeconds)
        {
            if (ensemble != null) { ensemble.PrepareWindow(fromExclusiveMs, toInclusiveMs, generation); return; }
            if (drums != null) drums.PrepareWindow(fromExclusiveMs, toInclusiveMs, generation);
            if (strings != null && strings.activeStrings) { strings.PrepareWindow(fromExclusiveMs, toInclusiveMs, generation); return; }
            // Explicit fallback schedules no future notes; NoteDown will trigger at logical due time.
            if (!Ready(generation) || clock.GetState() != 1 || !useDspScheduling) return;
            int first = UpperBoundStart(fromExclusiveMs);
            int end = UpperBoundStart(toInclusiveMs);
            int positionMs = (int)System.Math.Floor(songPositionSeconds * 1000.0);
            for (int note = first; note < end; note++)
            {
                if (HasRecord(note)) continue;
                RequestNote(note, positionMs, songPositionSeconds, dspNowSeconds, true);
            }
        }

        public void NoteDown(int noteId, int positionMs, int generation)
        {
            if (ensemble != null) { ensemble.NoteDown(noteId, positionMs, generation); return; }
            if (_song != null && noteId >= 0 && noteId < _song.channel.Length && _song.channel[noteId] == 9)
            { if (drums != null) drums.NoteDown(noteId, positionMs, generation); return; }
            if (strings != null && strings.activeStrings) { strings.NoteDown(noteId, positionMs, generation); return; }
            if (!Ready(generation) || clock.GetState() != 1 || !ValidNote(noteId)) return;
            if (HasRecord(noteId)) return;
            RequestNote(noteId, positionMs, clock.GetPositionSeconds(), FrameDsp(), true);
        }

        public void CatchUpRecentNotes(int fromExclusiveMs, int positionMs, int generation)
        {
            if (ensemble != null) { ensemble.CatchUpRecentNotes(fromExclusiveMs, positionMs, generation); return; }
            if (drums != null) drums.CatchUpRecentNotes(fromExclusiveMs, positionMs, generation);
            if (strings != null && strings.activeStrings) { strings.RecoverAt(positionMs, generation); return; }
            if (!Ready(generation) || clock.GetState() != 1) return;
            int first = UpperBoundStart(fromExclusiveMs);
            int end = UpperBoundStart(positionMs);
            double seconds = clock.GetPositionSeconds();
            double dsp = FrameDsp();
            for (int note = first; note < end; note++)
                if (!HasRecord(note)) RequestNote(note, positionMs, seconds, dsp, true);
        }

        public void NoteRelease(int noteId, int positionMs, int generation)
        {
            if (ensemble != null) return; // Per-slot immutable release time includes the actual source identity.
            if (_song != null && noteId >= 0 && noteId < _song.channel.Length && _song.channel[noteId] == 9) return;
            if (strings != null && strings.activeStrings) return; // Immutable MIDI release drives strings too.
            if (!Ready(generation) || !ValidNote(noteId)) return;
            // The immutable releaseMs, rather than callback arrival time, drives the envelope.
            for (int i = 0; i < _capacity; i++)
            {
                if (_slotGeneration[i] == generation && _slotNote[i] == noteId
                    && _slotState[i] != SlotFree && _slotState[i] != SlotRetiring)
                    _slotState[i] = SlotReleasing;
            }
        }

        public void RebuildAt(int positionMs, int generation)
        {
            if (ensemble != null) { ensemble.RebuildAt(positionMs, generation); return; }
            if (strings != null && strings.activeStrings)
            { if (drums != null) drums.RebuildAt(positionMs, generation); strings.RebuildAt(positionMs, generation); return; }
            Initialize();
            if (_song == null || bank == null || clock == null || _sequencer == null) return;
            if (_generation == generation) return;
            if (_generation >= 0) CancelGeneration(_generation);
            _generation = generation;
            _pendingCount = 0;
            suppressedNoteCount = 0;
            stolenVoiceCount = 0;
            lateStartCount = 0;
            if (drums != null) drums.RebuildAt(positionMs, generation);
            RecoverAt(positionMs, generation);
        }

        public void RecoverAt(int positionMs, int generation)
        {
            if (ensemble != null) { ensemble.RecoverAt(positionMs, generation); return; }
            if (drums != null) drums.RecoverAt(positionMs, generation);
            if (strings != null && strings.activeStrings) { strings.RecoverAt(positionMs, generation); return; }
            if (_song == null || bank == null || clock == null || _sequencer == null || _generation != generation) return;
            int activeCount = Mathf.Min(_sequencer.activeNoteCount, _sequencer.activeNoteIds.Length);
            for (int i = 0; i < activeCount; i++) CollectRecovery(_sequencer.activeNoteIds[i], positionMs);
            int tailStart = Mathf.Max(0, positionMs - bank.maxReleaseMs);
            int cursor = LowerBoundEvent(tailStart);
            while (cursor < _song.eventMs.Length && _song.eventMs[cursor] <= positionMs)
            {
                if (_song.eventKind[cursor] == 2) CollectRecovery(_song.eventNoteId[cursor], positionMs);
                cursor++;
            }
            pendingStartCount = _pendingCount;
            if (clock.GetState() == 1) UpdateVoices(positionMs, generation);
        }

        public void CancelGeneration(int generation)
        {
            if (ensemble != null) { ensemble.CancelGeneration(); return; }
            if (drums != null) drums.CancelSong();
            if (strings != null && strings.activeStrings) { strings.StopAll(); return; }
            Initialize();
            double dsp = FrameDsp();
            for (int i = 0; i < _capacity; i++)
            {
                if (_slotGeneration[i] != generation || _slotState[i] == SlotFree
                    || _slotState[i] == SlotRetiring) continue;
                if (_slotState[i] == SlotScheduled && _slotStartDsp[i] > dsp) StopSlot(i);
                else BeginRetire(i, dsp);
            }
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                if (_pendingGeneration[i] == generation) RemovePending(i);
            }
            pendingStartCount = _pendingCount;
        }

        public void UpdateVoices(int positionMs, int generation)
        {
            if (ensemble != null) { ensemble.UpdateVoices(positionMs, generation); return; }
            if (drums != null) drums.UpdateVoices(positionMs, generation);
            if (strings != null && strings.activeStrings)
            {
                strings.UpdateVoices(positionMs, generation);
                activeVoiceCount = strings.activeVoiceCount + (drums == null ? 0 : drums.activeVoiceCount);
                effectiveVoiceLimit = strings.GetVoiceLimit();
                stolenVoiceCount = strings.stolenVoiceCount;
                pendingStartCount = 0;
                return;
            }
            Initialize();
            double dsp = FrameDsp();
            // Cleanup deliberately survives a global generation change; slot identity must match.
            for (int i = 0; i < _capacity; i++)
            {
                if (_slotState[i] != SlotRetiring) continue;
                if (_retireToken[i] != _slotToken[i] || _retireGeneration[i] != _slotGeneration[i]) continue;
                if (dsp >= _retireEnd[i]) StopSlot(i);
                else
                {
                    double span = _retireEnd[i] - _retireStart[i];
                    float phase = span <= 0.0 ? 1f : (float)((dsp - _retireStart[i]) / span);
                    _slotRawGain[i] = _retireGain[i] * (1f - Mathf.Clamp01(phase));
                }
            }
            bool playing = clock != null && clock.GetState() == 1 && generation == _generation;
            for (int i = 0; i < _capacity; i++)
            {
                int state = _slotState[i];
                if (state == SlotFree || state == SlotRetiring) continue;
                if (_live[i])
                {
                    float phase = _liveRelease[i] < 0 ? 0f : Mathf.Clamp01((float)((dsp - _liveRelease[i]) * 1000.0) / bank.releaseDurationMs[_slotRegion[i]]);
                    _slotRawGain[i] = _liveGain[i] * (1f - phase) * (1f - phase);
                    if (phase >= 1f || voiceSources[i] == null || !voiceSources[i].isPlaying) StopSlot(i);
                    else UpdatePriority(i, positionMs, dsp);
                    continue;
                }
                if (!playing || _slotGeneration[i] != _generation)
                {
                    if (state == SlotScheduled && _slotStartDsp[i] > dsp) StopSlot(i);
                    else BeginRetire(i, dsp);
                    continue;
                }
                int note = _slotNote[i];
                if (positionMs >= _slotEndMs[i])
                {
                    MarkFinished(note);
                    StopSlot(i);
                    continue;
                }
                if (state == SlotScheduled && dsp < _slotStartDsp[i]) continue;
                _slotState[i] = positionMs >= _slotReleaseMs[i] ? SlotReleasing : SlotSounding;
                _noteState[note] = NoteSounding;
                _slotRawGain[i] = SlotGain(i, positionMs);
                UpdatePriority(i, positionMs, dsp);
            }
            if (playing) DrainPending(positionMs, clock.GetPositionSeconds(), dsp);
            ApplyOutputGains(dsp);
            activeVoiceCount = drums == null ? 0 : drums.activeVoiceCount;
            virtualVoiceCount = 0;
            effectiveVoiceLimit = EffectiveLimit();
            for (int i = 0; i < _capacity; i++)
            {
                if (_slotState[i] == SlotFree) continue;
                activeVoiceCount++;
                if (voiceSources[i] != null && dsp >= _slotStartDsp[i] && voiceSources[i].isVirtual) virtualVoiceCount++;
            }
            pendingStartCount = _pendingCount;
        }

        public void LiveNoteDown(int identity, int pitch, int velocity, int generation)
        {
            if (ensemble != null) { if (identity / 128 != 9) ensemble.LiveNoteDown(identity, pitch, velocity); return; }
            // Percussion uses event identities rather than held-key identities; never restore old hits from a key snapshot.
            if (identity / 128 == 9) return;
            if (strings != null && strings.activeStrings) { strings.LiveNoteDown(identity, pitch, velocity); return; }
            Initialize();
            if (bank == null || pitch < 0 || pitch > 127) return;
            int region = bank.SelectRegion(pitch, velocity);
            if (region < 0 || region >= bank.clips.Length || bank.clips[region] == null) return;
            int slot = FindFreeSlot();
            if (slot < 0)
                for (int i = 0; i < Mathf.Min(_capacity, EffectiveLimit()); i++)
                    if (voiceSources[i] != null && (slot < 0
                        || voiceSources[i].priority > voiceSources[slot].priority
                        || (voiceSources[i].priority == voiceSources[slot].priority && _slotStartDsp[i] < _slotStartDsp[slot]))) slot = i;
            if (slot < 0) return;
            StopSlot(slot);
            AudioSource source = voiceSources[slot];
            source.clip = bank.clips[region];
            source.pitch = Mathf.Pow(2f, (pitch - bank.rootPitch[region]) / 12f + bank.tuningCents[region] / 1200f);
            source.loop = false;
            source.timeSamples = 0;
            _live[slot] = true;
            _liveRelease[slot] = -1;
            _liveGain[slot] = VelocityGain(velocity) * bank.regionGain[region];
            _slotRawGain[slot] = _liveGain[slot];
            _slotPeak[slot] = Mathf.Max(0.0001f, bank.sourcePeak[region]);
            _slotRegion[slot] = region;
            _slotNote[slot] = identity;
            _slotGeneration[slot] = generation;
            _slotToken[slot]++;
            _slotStartDsp[slot] = AudioSettings.dspTime;
            _slotAttackUntilDsp[slot] = _slotStartDsp[slot] + AttackProtectionMs * 0.001;
            _slotVelocity[slot] = velocity;
            _slotState[slot] = SlotSounding;
            UpdatePriority(slot, 0, AudioSettings.dspTime);
            ApplyStartedGain(slot, AudioSettings.dspTime);
            source.Play();
        }

        public void LiveAllSoundOff(int channel)
        {
            if (ensemble != null) { ensemble.LiveAllSoundOff(channel); return; }
            if (drums != null && (channel < 0 || channel == 9)) drums.StopLive();
            if (strings != null && strings.activeStrings) { strings.LiveAllSoundOff(channel); return; }
            for (int i = 0; i < _capacity; i++)
                if (_live[i] && (channel < 0 || _slotNote[i] / 128 == channel)) StopSlot(i);
        }

        public void LiveNoteRelease(int identity)
        {
            if (ensemble != null) { if (identity / 128 != 9) ensemble.LiveNoteRelease(identity); return; }
            if (identity / 128 == 9) return;
            if (strings != null && strings.activeStrings) { strings.LiveNoteRelease(identity); return; }
            for (int i = 0; i < _capacity; i++)
                if (_live[i] && _slotNote[i] == identity && _liveRelease[i] < 0)
                    _liveRelease[i] = AudioSettings.dspTime;
        }

        private bool Ready(int generation)
        {
            return _song != null && bank != null && clock != null && _noteState != null
                && generation == _generation;
        }

        public void SetStringsMode(int mode)
        {
            if (ensemble != null) { if (strings != null) strings.activeStrings = false; return; }
            Initialize();
            for (int i = 0; i < _capacity; i++) if (_slotState[i] != SlotFree) StopSlot(i);
            _pendingCount = 0;
            if (strings != null) strings.Configure(mode);
        }

        private bool ValidNote(int note)
        {
            return _song != null && _noteState != null && note >= 0 && note < _noteState.Length;
        }

        private bool HasRecord(int note)
        {
            return _noteGeneration[note] == _generation && _noteState[note] != 0;
        }

        private double FrameDsp()
        {
            return clock == null ? AudioSettings.dspTime : clock.GetFrameDspSeconds();
        }

        private int EffectiveLimit()
        {
            int reserved=drums==null?0:drums.ReservedCount();
            return Mathf.Clamp(voiceLimit-reserved, 0, Mathf.Max(0, _capacity-reserved));
        }

        public int ReservedPercussionSources() { return drums==null?0:drums.ReservedCount(); }

        public void ClaimPercussionSources()
        {
            if (ensemble != null) { ensemble.ClaimPercussionSources(); return; }
            Initialize();
            int first=Mathf.Max(0,_capacity-16);
            for(int i=first;i<_capacity;i++) if(_slotState[i]!=SlotFree) StealSlot(i);
            if(strings!=null && strings.activeStrings) strings.ReleasePercussionSources();
        }

        private int RegionFor(int note)
        {
            if (_song.channel[note] == 9) return -1;
            int pitch = _song.pitch[note];
            if (pitch < 0 || pitch > 127) return -1;
            // Only sample selection stops at the piano edges; playback keeps the original pitch.
            int key = Mathf.Clamp(pitch, 21, 108) - 21;
            if (bank.pitchToRegion == null || key >= bank.pitchToRegion.Length) return -1;
            int region = bank.SelectRegion(pitch, _song.velocity[note]);
            if (bank.clips == null || region < 0 || region >= bank.clips.Length || bank.clips[region] == null) return -1;
            return region;
        }

        private bool IsAudibleAt(int note, int positionMs)
        {
            if (!ValidNote(note)) return false;
            int region = RegionFor(note);
            if (region < 0 || bank.sourceRate[region] <= 0 || bank.releaseDurationMs[region] <= 0) return false;
            float ratio = Mathf.Pow(2f, (_song.pitch[note] - bank.rootPitch[region]) / 12f + bank.tuningCents[region] / 1200f);
            double age = System.Math.Max(0.0, (positionMs - _song.startMs[note]) * 0.001);
            if (age * ratio * bank.sourceRate[region] >= bank.sourceFrameCount[region]) return false;
            return positionMs < _song.releaseMs[note] + bank.releaseDurationMs[region];
        }

        private float VelocityGain(int velocity)
        {
            // The selected four-layer Salamander bank retains recorded dynamics.
            // Velocity selects its sample layer without attenuating quiet recordings again.
            // Single-layer and legacy three-layer banks retain their existing response.
            if (bank.velocityLayerCount == 4) return 1f;
            return Mathf.Pow(velocity / 127f, 1.6f);
        }

        private float RawGain(int note, int positionMs)
        {
            int region = RegionFor(note);
            if (region < 0) return 0f;
            float phase = Mathf.Clamp01((positionMs - _song.releaseMs[note]) / (float)bank.releaseDurationMs[region]);
            float release = (1f - phase) * (1f - phase);
            return VelocityGain(_song.velocity[note]) * bank.regionGain[region] * release;
        }

        private float SlotGain(int slot, int positionMs)
        {
            float phase = Mathf.Clamp01((positionMs - _slotReleaseMs[slot]) / (float)bank.releaseDurationMs[_slotRegion[slot]]);
            return _slotBaseGain[slot] * (1f - phase) * (1f - phase);
        }

        private void UpdatePriority(int slot, int positionMs, double dsp)
        {
            AudioSource source = voiceSources[slot];
            if (source == null) return;
            int priority;
            if (dsp < _slotAttackUntilDsp[slot])
                priority = 32 + (127 - _slotVelocity[slot]) / 4;
            else if (_live[slot] ? _liveRelease[slot] >= 0 : positionMs >= _slotReleaseMs[slot])
                priority = 192 + Mathf.Clamp((int)(31f * (1f - _slotRawGain[slot] / Mathf.Max(0.0001f, _live[slot] ? _liveGain[slot] : _slotBaseGain[slot]))), 0, 31);
            else
            {
                int ageMs = _live[slot] ? (int)((dsp - _slotStartDsp[slot]) * 1000.0) : positionMs - _song.startMs[_slotNote[slot]];
                priority = 96 + Mathf.Clamp(ageMs / 250, 0, 63);
            }
            // Leave higher-priority voice chat/client audio alone. Among piano
            // voices, attacks win real voices ahead of old sustained/release tails.
            if (source.priority != priority) source.priority = priority;
        }

        private void ApplyStartedGain(int slot, double dsp)
        {
            // A new slot only needs its own volume when peak protection is off.
            if (peakProtectionEnabled) ApplyOutputGains(dsp);
            else voiceSources[slot].volume = muted ? 0f : Mathf.Clamp01(localVolume * _slotRawGain[slot]);
        }

        private void RequestNote(int note, int positionMs, double songSeconds, double dsp, bool preserveAttack)
        {
            if (_song.channel[note] == 9) return;
            if (EffectiveLimit() == 0) { MarkSuppressed(note); return; }
            if (practice != null && practice.IsTargetTrack(_song.trackIndex[note])) return;
            if (!preserveAttack && !IsAudibleAt(note, positionMs)) { MarkFinished(note); return; }
            int slot = FindFreeSlot();
            if (slot >= 0) { StartSlot(slot, note, positionMs, songSeconds, dsp, preserveAttack); return; }
            // Look-ahead must never steal a sounding note to reserve a future
            // attack, or permanently suppress a note before its actual due time.
            // The overlapping window/NoteDown retries it when a slot is available.
            if (_song.startMs[note] > positionMs) return;
            int victim = FindVictim(positionMs);
            if (victim < 0 || (_slotState[victim] != SlotRetiring && !BetterNote(note, _slotNote[victim], positionMs)))
            {
                QueuePending(note, positionMs, preserveAttack);
                return;
            }
            StealSlot(victim);
            // Under pressure reuse the least important slot now. Waiting for a
            // frame-driven retirement fade loses an entire attack at low FPS.
            StartSlot(victim, note, positionMs, songSeconds, dsp, preserveAttack);
        }

        private void CollectRecovery(int note, int positionMs)
        {
            if (_song.channel[note] == 9) return;
            if (practice != null && practice.IsTargetTrack(_song.trackIndex[note])) return;
            if (!ValidNote(note) || HasRecord(note)) return;
            if (!IsAudibleAt(note, positionMs)) { MarkFinished(note); return; }
            QueuePending(note, positionMs, false);
        }

        private void QueuePending(int note, int positionMs, bool preserveAttack)
        {
            _noteGeneration[note] = _generation;
            int limit = EffectiveLimit();
            if (_pendingCount < limit)
            {
                _pendingNotes[_pendingCount] = note;
                _pendingGeneration[_pendingCount] = _generation;
                _pendingAttack[_pendingCount] = preserveAttack;
                _pendingCount++;
                _noteState[note] = NotePending;
                return;
            }
            int worst = 0;
            for (int i = 1; i < _pendingCount; i++)
                if (BetterNote(_pendingNotes[worst], _pendingNotes[i], positionMs)) worst = i;
            if (BetterNote(note, _pendingNotes[worst], positionMs))
            {
                MarkSuppressed(_pendingNotes[worst]);
                _pendingNotes[worst] = note;
                _pendingGeneration[worst] = _generation;
                _pendingAttack[worst] = preserveAttack;
                _noteState[note] = NotePending;
            }
            else MarkSuppressed(note);
        }

        private void DrainPending(int positionMs, double songSeconds, double dsp)
        {
            if (EffectiveLimit() == 0)
            {
                for(int i=0;i<_pendingCount;i++) MarkSuppressed(_pendingNotes[i]);
                _pendingCount=0; return;
            }
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                int note = _pendingNotes[i];
                if (_pendingGeneration[i] != _generation) { RemovePending(i); continue; }
                if (!_pendingAttack[i] && !IsAudibleAt(note, positionMs)) { MarkFinished(note); RemovePending(i); continue; }
            }
            int free = FindFreeSlot();
            while (_pendingCount > 0)
            {
                int best = 0;
                for (int i = 1; i < _pendingCount; i++)
                    if (BetterNote(_pendingNotes[i], _pendingNotes[best], positionMs)) best = i;
                int note = _pendingNotes[best];
                bool attack = _pendingAttack[best];
                if (free < 0)
                {
                    free = FindVictim(positionMs);
                    if (free < 0 || (_slotState[free] != SlotRetiring && !BetterNote(note, _slotNote[free], positionMs))) break;
                    // Seek/join recovery may wait for its short cancellation
                    // fade. Only a newly due attack is allowed to bypass it.
                    if (_slotState[free] == SlotRetiring && !attack) break;
                    StealSlot(free);
                }
                RemovePending(best);
                StartSlot(free, note, positionMs, songSeconds, dsp, attack);
                free = FindFreeSlot();
            }
        }

        private void RemovePending(int index)
        {
            _pendingCount--;
            _pendingNotes[index] = _pendingNotes[_pendingCount];
            _pendingGeneration[index] = _pendingGeneration[_pendingCount];
            _pendingAttack[index] = _pendingAttack[_pendingCount];
        }

        private bool BetterNote(int a, int b, int positionMs)
        {
            bool aFuture = _song.startMs[a] > positionMs;
            bool bFuture = _song.startMs[b] > positionMs;
            if (aFuture != bFuture) return !aFuture;
            int aAge = Mathf.Max(0, positionMs - _song.startMs[a]);
            int bAge = Mathf.Max(0, positionMs - _song.startMs[b]);
            bool aAttack = aAge < AttackProtectionMs;
            bool bAttack = bAge < AttackProtectionMs;
            if (aAttack != bAttack) return aAttack;
            bool aRelease = positionMs >= _song.releaseMs[a];
            bool bRelease = positionMs >= _song.releaseMs[b];
            if (aRelease != bRelease) return !aRelease;
            // The sample's natural decay is already recorded in the clip;
            // RawGain alone treats a very old sustained sample as a fresh one.
            if (aAge / AttackProtectionMs != bAge / AttackProtectionMs)
                return aAge < bAge;
            float ga = RawGain(a, positionMs);
            float gb = RawGain(b, positionMs);
            if (Mathf.Abs(ga - gb) > 0.00001f) return ga > gb;
            if (_song.velocity[a] != _song.velocity[b]) return _song.velocity[a] > _song.velocity[b];
            return a > b;
        }

        private int FindFreeSlot()
        {
            int limit = Mathf.Min(_capacity, EffectiveLimit());
            for (int i = 0; i < limit; i++)
                if (_slotState[i] == SlotFree && voiceSources[i] != null) return i;
            return -1;
        }

        private int FindVictim(int positionMs)
        {
            int result = -1;
            int limit = Mathf.Min(_capacity, EffectiveLimit());
            for (int i = 0; i < limit; i++)
            {
                if (_slotState[i] == SlotFree || _live[i]) continue;
                if (_slotState[i] == SlotRetiring) return i;
                if (_slotGeneration[i] != _generation) continue;
                if (result < 0 || BetterNote(_slotNote[result], _slotNote[i], positionMs)) result = i;
            }
            return result;
        }

        private void StealSlot(int slot)
        {
            if (_slotGeneration[slot] == _generation && !_live[slot] && _slotState[slot] != SlotRetiring)
            {
                int note = _slotNote[slot];
                // An unheard reservation may retry at its due time. A voice
                // which already sounded must never be resurrected by recovery.
                if (_slotState[slot] == SlotScheduled && _slotStartDsp[slot] > FrameDsp())
                    _noteState[note] = 0;
                else MarkSuppressed(note);
                stolenVoiceCount++;
            }
            StopSlot(slot);
        }

        private void StartSlot(int slot, int note, int positionMs, double songSeconds, double dsp, bool preserveAttack)
        {
            int region = RegionFor(note);
            if (region < 0 || (!preserveAttack && !IsAudibleAt(note, positionMs))) { MarkFinished(note); return; }
            AudioSource source = voiceSources[slot];
            float ratio = Mathf.Pow(2f, (_song.pitch[note] - bank.rootPitch[region]) / 12f + bank.tuningCents[region] / 1200f);
            double age = System.Math.Max(0.0, songSeconds - _song.startMs[note] * 0.001);
            int delayMs = preserveAttack ? Mathf.Max(0, positionMs - _song.startMs[note]) : 0;
            // Shift a newly due note's release/end with its attack. Seeking,
            // joining and recovering old tails retain their original lifetime.
            if (preserveAttack) age = 0.0;
            int sourceFrame = (int)System.Math.Floor(age * ratio * bank.sourceRate[region]);
            sourceFrame = Mathf.Clamp(sourceFrame, 0, bank.sourceFrameCount[region] - 1);
            source.Stop();
            source.clip = bank.clips[region];
            source.pitch = ratio;
            source.loop = false;
            source.timeSamples = sourceFrame;
            _slotToken[slot]++;
            _slotNote[slot] = note;
            _slotGeneration[slot] = _generation;
            _slotRegion[slot] = region;
            _slotBaseGain[slot] = VelocityGain(_song.velocity[note]) * bank.regionGain[region];
            _slotReleaseMs[slot] = _song.releaseMs[note] + delayMs;
            _slotEndMs[slot] = System.Math.Min((double)_slotReleaseMs[slot] + bank.releaseDurationMs[region],
                _song.startMs[note] + delayMs + bank.sourceFrameCount[region] * 1000.0 / (ratio * bank.sourceRate[region]));
            _slotVelocity[slot] = _song.velocity[note];
            _slotRawGain[slot] = SlotGain(slot, positionMs);
            _slotPeak[slot] = Mathf.Max(0.0001f, bank.sourcePeak[region]);
            double targetDsp = dsp + _song.startMs[note] * 0.001 - songSeconds;
            _slotStartDsp[slot] = System.Math.Max(dsp, targetDsp);
            _slotAttackUntilDsp[slot] = _slotStartDsp[slot] + AttackProtectionMs * 0.001 - age;
            bool scheduled = useDspScheduling && targetDsp > dsp + 0.001;
            _slotState[slot] = scheduled ? SlotScheduled : SlotSounding;
            _noteGeneration[note] = _generation;
            _noteState[note] = scheduled ? NoteScheduled : NoteSounding;
            UpdatePriority(slot, positionMs, dsp);
            ApplyStartedGain(slot, dsp);
            if (scheduled) source.PlayScheduled(targetDsp);
            else
            {
                if (positionMs > _song.startMs[note] + 2) lateStartCount++;
                source.Play();
            }
        }

        private void BeginRetire(int slot, double dsp)
        {
            if (_slotState[slot] == SlotFree || _slotState[slot] == SlotRetiring) return;
            _retireToken[slot] = _slotToken[slot];
            _retireGeneration[slot] = _slotGeneration[slot];
            _retireStart[slot] = dsp;
            _retireEnd[slot] = dsp + Mathf.Max(0f, retireFadeSeconds);
            _retireGain[slot] = _slotRawGain[slot];
            _slotState[slot] = SlotRetiring;
            if (voiceSources[slot] != null) voiceSources[slot].priority = 240;
            if (retireFadeSeconds <= 0f) StopSlot(slot);
        }

        private void StopSlot(int slot)
        {
            if (voiceSources[slot] != null)
            {
                voiceSources[slot].Stop();
                voiceSources[slot].volume = 0f;
            }
            _live[slot] = false;
            _slotState[slot] = SlotFree;
            _slotNote[slot] = -1;
            _slotRawGain[slot] = 0f;
        }

        private void MarkSuppressed(int note)
        {
            if (!ValidNote(note)) return;
            if (_noteGeneration[note] != _generation || _noteState[note] != NoteSuppressed) suppressedNoteCount++;
            _noteGeneration[note] = _generation;
            _noteState[note] = NoteSuppressed;
        }

        private void MarkFinished(int note)
        {
            if (!ValidNote(note)) return;
            _noteGeneration[note] = _generation;
            _noteState[note] = NoteFinished;
        }

        private void ApplyOutputGains(double dsp)
        {
            float target = 1f;
            if (peakProtectionEnabled)
            {
                float sum = drums == null ? 0f : drums.PeakSum();
                for (int i = 0; i < _capacity; i++)
                    if (_slotState[i] != SlotFree) sum += _slotRawGain[i] * _slotPeak[i];
                target = Mathf.Min(1f, Mathf.Max(0.01f, peakBudget) / Mathf.Max(0.0001f, sum));
            }
            if (!peakProtectionEnabled) _normalization = 1f;
            else if (target < _normalization) _normalization = target;
            else
            {
                float elapsed = (float)System.Math.Max(0.0, dsp - _lastGainDsp);
                _normalization = Mathf.Lerp(_normalization, target, 1f - Mathf.Exp(-elapsed / 0.08f));
            }
            _lastGainDsp = dsp;
            float master = muted ? 0f : Mathf.Clamp(localVolume, 0f, 2f);
            for (int i = 0; i < _capacity; i++)
                if (_slotState[i] != SlotFree && voiceSources[i] != null)
                    voiceSources[i].volume = Mathf.Clamp01(master * _normalization * _slotRawGain[i]);
            if (drums != null) drums.ApplyOutputGains(_normalization);
        }

        private int UpperBoundStart(int timeMs)
        {
            int lo = 0;
            int hi = _song.startMs.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_song.startMs[mid] <= timeMs) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private int LowerBoundEvent(int timeMs)
        {
            int lo = 0;
            int hi = _song.eventMs.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (_song.eventMs[mid] < timeMs) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
