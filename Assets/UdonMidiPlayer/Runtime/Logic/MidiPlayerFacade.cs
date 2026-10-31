using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Networking;
using MusicMidiPlayer.Views;
using MusicMidiPlayer.UI;

namespace MusicMidiPlayer.Logic
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Logic/Player")]
    public class MidiPlayerFacade : UdonSharpBehaviour
    {
        public MidiLibraryData library;
        public MidiControlGate gate;
        public MidiNetworkTransport network;
        public MidiTransportClock clock;
        public MidiSequencer sequencer;
        public PianoVoicePool voicePool;
        public PianoKeyboardView keyboard;
        public PianoPedalView pedals;
        public MidiWaterfallView waterfall;
        public MidiPlayerUI playerUI;
        public MidiPracticeController practice;
        public MidiPerformanceInput performanceInput;
        public MidiDrumKitView drumView;
        public MidiInstrumentRouter instrumentRouter;
        [Header("Hardware MIDI channel targets")]
        [Tooltip("Index 0..15 = MIDI channel 1..16. -1: automatic; 0: piano; 1: marimba; 2: violin; 3: viola; 4: cello; 5: contrabass; 6: strings ensemble. Channel 10 always plays drums. Explicit targets must be enabled.")]
        // New players follow the enabled instrument set on every melodic channel.
        // Explicit mappings and restored PlayerData remain authoritative for that player.
        public int[] hardwareMidiChannelTargets = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
        public string midiPreferencesKey = "MusicMidiPlayer.MidiPreferences.v1";
        [HideInInspector] public int preferredMidiChannel = -1;
        private bool _midiPreferencesRestored, _midiPreferenceWriteQueued, _midiPreferredDirty;
        private bool[] _midiPreferenceDirtyChannels = new bool[16];
        private string _midiLastSaved = "";
        private int _routedViewGeneration = -1;
        public bool IsLiveKeyHeld(int identity) { return identity >= 0 && identity < 2048 && _liveHeld[identity]; }
        public int LiveExpression(int channel) { return _liveExpression[channel]; }
        // Zero follows MIDI Program routing; 1..7 pins a physical performance target.
        private int[] _liveTargetOverrides = new int[2048];
        public int LiveTargetOverride(int identity) { return _liveTargetOverrides[identity]; }
        public int InstrumentMask() { return instrumentRouter == null ? 1 << (selectedInstrument < 2 ? 0 : selectedInstrument - 1) : instrumentRouter.enabledMask; }
        public bool HasSelectedStrings() { return instrumentRouter != null ? (instrumentRouter.enabledMask & 60) != 0 : GetStringsView(selectedInstrument) != null; }
        public bool IsInstrumentChecked(int index) { return instrumentRouter == null ? index == selectedInstrument : instrumentRouter.IsViewEnabled(index); }
        [HideInInspector] public int liveMidiEventSequence;
        // One controller/input/voice pool; presentation variants share it.
        public GameObject[] instrumentRoots = new GameObject[0];
        public PianoKeyboardView[] instrumentKeyboards = new PianoKeyboardView[0];
        public PianoPedalView[] instrumentPedals = new PianoPedalView[0];
        public MidiWaterfallView[] instrumentWaterfalls = new MidiWaterfallView[0];
        public PianoBankData[] instrumentBanks = new PianoBankData[0];
        public StringsView[] instrumentStrings = new StringsView[0];
        public int selectedInstrument;
        [HideInInspector] public string selectedSongId;
        [HideInInspector] public string lastError;
        [HideInInspector] public bool isReady;
        [HideInInspector] public bool isSongLoading;
        public float decodeFrameBudgetMs = 5f;
        [HideInInspector] public int applyGeneration = 1;
        private int[] _liveVelocity = new int[2048];
        private bool[] _liveHeld = new bool[2048];
        private bool[] _liveSustained = new bool[2048];
        private bool[] _livePedal = new bool[16];
        private int[] _liveExpression = {127,127,127,127,127,127,127,127,127,127,127,127,127,127,127,127};
        private MidiSongData _song;
        private bool _naturalEnded;
        private int _lastFrameMs;
        private bool _networkBlocked;

        private void Start()
        {
            if (library == null || clock == null || sequencer == null || voicePool == null || network == null || gate == null)
            { SetNetworkError("播放器配置不完整"); return; }
            if (library.songs == null || library.songs.Length == 0 || library.bank == null || !library.bank.IsValid())
            { SetNetworkError("暂无可播放曲目或有效音源"); return; }
            if (practice != null) { clock.practiceMode = true; voicePool.practice = practice; voicePool.useDspScheduling = false; }
            voicePool.BindBank(GetInstrumentBank(selectedInstrument));
            if (instrumentRouter != null) { instrumentRouter.ResolveSelection(); voicePool.ensemble.Initialize(); ApplyRoutedRoots(); }
            if (voicePool.strings != null) voicePool.SetStringsMode(GetStringsMode(selectedInstrument));
            if (GetStringsView(selectedInstrument) != null) voicePool.strings.soundOrigins = GetStringsView(selectedInstrument).bodies;
            sequencer.voicePool = voicePool;
            if (keyboard != null) keyboard.BindSequencer(sequencer);
            string initial = library.defaultSongId;
            if (library.FindSong(initial) == null) initial = library.songs[0].songId;
            selectedSongId = initial;
            if (!BindSong(initial)) return;
            clock.ApplyAnchor(0, 0, clock.GetServerSeconds());
            clock.SampleFrame(clock.GetServerSeconds(), AudioSettings.dspTime);
            ClearOutput();
            isReady = true;
            if (practice == null) network._Initialize();
            if (playerUI != null) playerUI.RefreshState();
        }

        private void Update()
        {
            if (!isReady) return;
            double now = clock.GetServerSeconds();
            if (practice != null) practice.AdvanceFrame(AudioSettings.dspTime);
            clock.SampleFrame(now, AudioSettings.dspTime);
            if (isSongLoading)
            {
                // Finish the short retirement fades from the previous generation while loading.
                voicePool.UpdateVoices((int)(clock.GetPositionSeconds() * 1000.0), applyGeneration);
                if (!_networkBlocked) AdvanceSongLoad();
                if (playerUI != null) playerUI.RefreshState();
                return;
            }
            if (_networkBlocked)
            {
                voicePool.UpdateVoices((int)(clock.GetPositionSeconds() * 1000.0), applyGeneration);
                if (playerUI != null) playerUI.RefreshState();
                return;
            }
            int positionMs = (int)(clock.GetPositionSeconds() * 1000.0);
            int state = clock.GetState();
            if (instrumentRouter != null) instrumentRouter.RefreshKeyCounts(positionMs, CanReceiveLiveMidi());
            if (state == 1 && !_naturalEnded)
            {
                if (clock.GetPositionSeconds() >= clock.GetPlaybackEndSeconds())
                {
                    _naturalEnded = true;
                    ClearOutput();
                    if (practice == null) network._NotifyNaturalEnd();
                }
                else
                {
                    // Reclaim ended samples before allocating this frame's new attacks.
                    voicePool.UpdateVoices(positionMs, applyGeneration);
                    sequencer.AdvanceTo(positionMs);
                    if (sequencer.rebuildRequested) RebuildCurrent(positionMs);
                    if (instrumentRouter != null) instrumentRouter.RefreshKeyCounts(positionMs, false);
                    voicePool.UpdateVoices(positionMs, applyGeneration);
                    voicePool.PrepareWindow(positionMs, positionMs + 150, applyGeneration, clock.GetPositionSeconds(), clock.GetFrameDspSeconds());
                    if (keyboard != null) keyboard.Refresh(positionMs, applyGeneration, false);
                    if (pedals != null) pedals.Refresh(positionMs, applyGeneration, false);
                    if (waterfall != null) waterfall.Refresh(positionMs, applyGeneration);
                }
            }
            else
            {
                voicePool.UpdateVoices(positionMs, applyGeneration);
                if (state == 2)
                {
                    if (keyboard != null) keyboard.Refresh(positionMs, applyGeneration, false);
                    if (pedals != null) pedals.Refresh(positionMs, applyGeneration, false);
                    if (waterfall != null) waterfall.Refresh(positionMs, applyGeneration);
                }
            }
            if (CanReceiveLiveMidi())
            {
                if (drumView != null) drumView.RefreshLive();
                if (keyboard != null) keyboard.RefreshLive((int)(Time.realtimeSinceStartup * 1000f), applyGeneration);
                if (waterfall != null) waterfall.RefreshLive();
                if (waterfall != null && waterfall.effects != null) waterfall.effects.RefreshLive(applyGeneration);
            }
            StringsView stringsView = GetStringsView(selectedInstrument);
            if (stringsView != null) stringsView.Refresh(positionMs);
            if (drumView != null && (state == 1 || state == 2)) drumView.Refresh(positionMs, applyGeneration);
            if (instrumentRouter != null) RefreshRoutedViews(positionMs);
            _lastFrameMs = positionMs;
            if (playerUI != null) playerUI.RefreshState();
        }

        public bool CanReceiveLiveMidi()
        {
            return isReady && !isSongLoading && !_networkBlocked && (practice != null || clock.GetState() == 0);
        }

        public override void MidiNoteOn(int channel, int number, int velocity)
        {
            if (playerUI != null && playerUI.TryLearnMidiInput(channel, number, velocity)) return;
            // Resolve only hardware input here. PC/VR already identify their physical instrument.
            // NoteOff must keep working after a mapping edit or the target being unchecked.
            if (velocity == 0) { PerformNoteOff(channel, number, 0); return; }
            if (channel < 0 || channel >= 16) return;
            int target = hardwareMidiChannelTargets != null && channel < hardwareMidiChannelTargets.Length
                ? hardwareMidiChannelTargets[channel] : -1;
            if (instrumentRouter == null || channel == 9 || target == -1)
            { PerformNoteOn(channel, number, velocity); return; }
            if (target < 0 || target > 6) return;
            int view = target == 0 ? instrumentRouter.pianoAppearance : target + 1;
            PerformInstrumentNoteOn(channel, number, velocity, view);
        }

        // Local device preference. Active notes retain the target sent with their NoteOn.
        public void SetHardwareMidiChannelTarget(int channel, int target)
        {
            if (channel < 0 || channel >= 16 || channel == 9 || target < -1 || target > 6
                || hardwareMidiChannelTargets == null || hardwareMidiChannelTargets.Length != 16) return;
            if (hardwareMidiChannelTargets[channel] == target) return;
            hardwareMidiChannelTargets[channel] = target;
            _midiPreferenceDirtyChannels[channel] = true;
            QueueMidiPreferenceSave();
        }

        public void RememberMidiChannel(int channel)
        {
            if (channel < 0 || channel > 15 || channel == 9 || preferredMidiChannel == channel) return;
            preferredMidiChannel = channel; _midiPreferredDirty = true; QueueMidiPreferenceSave();
        }
        private void QueueMidiPreferenceSave()
        {
            if (!_midiPreferencesRestored || _midiPreferenceWriteQueued || string.IsNullOrEmpty(midiPreferencesKey)) return;
            _midiPreferenceWriteQueued = true;
            SendCustomEventDelayedSeconds(nameof(_SaveMidiPreferences), .5f);
        }
        public string EncodeMidiPreferences()
        {
            if (hardwareMidiChannelTargets == null || hardwareMidiChannelTargets.Length != 16) return "";
            string value = "1", digits = "01234567";
            for (int i = 0; i < 16; i++)
            {
                int target = i == 9 ? -1 : hardwareMidiChannelTargets[i];
                if (target < -1 || target > 6) return "";
                value += digits.Substring(target + 1, 1);
            }
            return value + (preferredMidiChannel >= 0 && preferredMidiChannel < 16 && preferredMidiChannel != 9
                ? "0123456789ABCDEF".Substring(preferredMidiChannel, 1) : "x");
        }
        public bool RestoreMidiPreferences(string encodedPreferences)
        {
            if (string.IsNullOrEmpty(encodedPreferences) || encodedPreferences.Length != 18 || encodedPreferences.Substring(0, 1) != "1"
                || hardwareMidiChannelTargets == null || hardwareMidiChannelTargets.Length != 16) return false;
            int[] targets = new int[16];
            for (int i = 0; i < 16; i++)
            {
                int digit = "01234567".IndexOf(encodedPreferences.Substring(i + 1, 1));
                if (digit < 0) return false;
                targets[i] = digit - 1;
            }
            string last = encodedPreferences.Substring(17, 1);
            int channel = "0123456789ABCDEF".IndexOf(last);
            if ((channel < 0 && last != "x") || channel == 9) return false;
            for (int i = 0; i < 16; i++)
                if (i != 9 && !_midiPreferenceDirtyChannels[i]) hardwareMidiChannelTargets[i] = targets[i];
            if (!_midiPreferredDirty) preferredMidiChannel = channel;
            return true;
        }
        public override void OnPlayerRestored(VRC.SDKBase.VRCPlayerApi player)
        {
            if (!VRC.SDKBase.Utilities.IsValid(player) || !player.isLocal || _midiPreferencesRestored) return;
            string value;
            if (!string.IsNullOrEmpty(midiPreferencesKey) && VRC.SDK3.Persistence.PlayerData.TryGetString(player, midiPreferencesKey, out value))
            { if (RestoreMidiPreferences(value)) _midiLastSaved = value; }
            _midiPreferencesRestored = true;
            bool dirty = _midiPreferredDirty;
            for (int i = 0; i < 16; i++) if (_midiPreferenceDirtyChannels[i]) dirty = true;
            if (dirty) QueueMidiPreferenceSave();
            if (playerUI != null) playerUI._OnMidiPreferencesRestored();
        }
        public void _SaveMidiPreferences()
        {
            _midiPreferenceWriteQueued = false;
            if (!_midiPreferencesRestored || string.IsNullOrEmpty(midiPreferencesKey)
                || !VRC.SDKBase.Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer)) return;
            string value = EncodeMidiPreferences();
            if (value == "" || value == _midiLastSaved) return;
            VRC.SDK3.Persistence.PlayerData.SetString(midiPreferencesKey, value); _midiLastSaved = value;
        }

        // Hardware MIDI, desktop keys and VR fingers enter the same performance pipeline.
        public void PerformNoteOn(int channel, int number, int velocity)
        {
            if (!CanReceiveLiveMidi() || channel < 0 || channel >= 16 || number < 0 || number > 127 || velocity < 0 || velocity > 127) return;
            if (practice != null)
            {
                if (velocity == 0) _LiveNoteOff(channel, number, 0);
                else { _LiveNoteOn(channel, number, velocity); if (!isSongLoading && channel != 9) practice.AcceptNote(number); }
                return;
            }
            if (network != null) network._SubmitLiveMidi(velocity == 0 ? 1 : 0, channel, number, velocity);
        }

        public void PerformInstrumentNoteOn(int channel, int number, int velocity, int view)
        {
            if (instrumentRouter == null) { PerformNoteOn(channel, number, velocity); return; }
            if (!CanReceiveLiveMidi() || view < 0 || view >= instrumentRoots.Length || !IsInstrumentChecked(view)) return;
            int target = instrumentRouter.TargetForView(view);
            if (network != null) network._SubmitLiveMidi(3 + target, channel, number, velocity);
        }

        public override void MidiNoteOff(int channel, int number, int velocity)
        {
            PerformNoteOff(channel, number, velocity);
        }

        public void PerformNoteOff(int channel, int number, int velocity)
        {
            if (practice != null) { _LiveNoteOff(channel, number, velocity); return; }
            if (network != null) network._SubmitLiveMidi(1, channel, number, velocity);
        }

        public override void MidiControlChange(int channel, int number, int value)
        {
            PerformControlChange(channel, number, value);
        }

        public void PerformControlChange(int channel, int number, int value)
        {
            if (practice != null) { _LiveControlChange(channel, number, value); return; }
            if (network != null) network._SubmitLiveMidi(2, channel, number, value);
        }

        // Local entries, deliberately not NetworkCallable. The transport authenticates remote events.
        public void _ApplyLiveMidi(int packed)
        {
            int kind = packed >> 18;
            int channel = (packed >> 14) & 15;
            int number = (packed >> 7) & 127;
            int value = packed & 127;
            if (kind == 0) _LiveNoteOn(channel, number, value);
            else if (kind == 1) _LiveNoteOff(channel, number, value);
            else if (kind == 2) _LiveControlChange(channel, number, value);
            else if (kind >= 3 && kind <= 9) StartLiveNote(channel, number, value, kind - 2);
        }

        public void _ApplyLiveMidiEvent(int packed, int sequence)
        { liveMidiEventSequence = sequence; _ApplyLiveMidi(packed); }

        public bool _HasLiveMidiState()
        {
            if (voicePool != null && voicePool.drums != null && voicePool.drums.activeVoiceCount > 0) return true;
            for (int i = 0; i < 2048; i++) if (_liveHeld[i] || _liveSustained[i]) return true;
            for (int i = 0; i < 16; i++) if (_livePedal[i]) return true;
            return false;
        }

        // Sparse snapshot: velocity/pedal in bits 0..7, identity in 8..19,
        // explicit performance target + 1 in 20..22 (zero keeps MIDI routing).
        public int[] _CaptureLiveMidi()
        {
            int count = 0;
            for (int i = 0; i < 2048; i++) if (_liveHeld[i] || _liveSustained[i]) count++;
            for (int i = 0; i < 16; i++) if (_livePedal[i]) count++;
            for (int i = 0; i < 16; i++) if (_liveExpression[i] != 127) count++;
            int[] data = new int[count];
            int cursor = 0;
            for (int i = 0; i < 2048; i++)
                if (_liveHeld[i] || _liveSustained[i]) data[cursor++] = (_liveTargetOverrides[i] << 20) | (i << 8) | _liveVelocity[i] | (_liveHeld[i] ? 0 : 128);
            for (int i = 0; i < 16; i++) if (_livePedal[i]) data[cursor++] = ((2048 + i) << 8) | 127;
            for (int i = 0; i < 16; i++) if (_liveExpression[i] != 127) data[cursor++] = ((2064 + i) << 8) | (_liveExpression[i] + 1);
            return data;
        }

        public void _RestoreLiveMidi(int[] data)
        {
            if (!CanReceiveLiveMidi() || data == null || data.Length > 2080) return;
            int[] target = new int[2080];
            int[] overrides = new int[2048];
            for (int i = 0; i < data.Length; i++)
            {
                int id = (data[i] >> 8) & 4095;
                int direct = data[i] >> 20;
                int value = data[i] & 255;
                if (id < 0 || id >= 2080 || target[id] != 0
                    || direct < 0 || direct > 7 || (id >= 2048 && direct != 0)
                    || (id < 2064 ? (value & 127) == 0 : value < 1 || value > 128)) return;
                target[id] = value;
                if (id < 2048) overrides[id] = direct;
            }
            for (int channel = 0; channel < 16; channel++)
            {
                _liveExpression[channel] = target[2064 + channel] == 0 ? 127 : target[2064 + channel] - 1;
                if (voicePool.strings != null) voicePool.strings.SetExpression(channel, _liveExpression[channel]);
            }
            for (int id = 0; id < 2048; id++)
            {
                if (id / 128 == 9) continue;
                int value = target[id];
                if (overrides[id] > 0 && (instrumentRouter == null || !instrumentRouter.IsEnabled(overrides[id] - 1))) value = 0;
                bool held = value > 0 && value < 128;
                bool sustained = value > 128;
                bool wasSounding = _liveHeld[id] || _liveSustained[id];
                bool changedTarget = _liveTargetOverrides[id] != overrides[id];
                if (wasSounding && changedTarget)
                {
                    voicePool.LiveNoteRelease(id);
                    if (instrumentRouter != null) RouteLiveVisual(id, false, 0, false);
                    wasSounding = false;
                }
                _liveTargetOverrides[id] = overrides[id];
                if ((held || sustained) && !wasSounding)
                    voicePool.LiveNoteDown(id, id % 128, value & 127, applyGeneration);
                else if (!held && !sustained && wasSounding) voicePool.LiveNoteRelease(id);
                if (held != _liveHeld[id] || (held && changedTarget))
                {
                    int key = id % 128 - 21;
                    if (key >= 0 && key < 88 && held != _liveHeld[id]) sequencer.keyDownCount[key] = Mathf.Max(0, sequencer.keyDownCount[key] + (held ? 1 : -1));
                    if (instrumentRouter != null) RouteLiveVisual(id, held, value & 127, false);
                    else if (waterfall != null)
                    {
                        if (held) waterfall.LiveNoteDown(id, id % 128);
                        else waterfall.LiveNoteUp(id);
                    }
                }
                _liveHeld[id] = held;
                _liveSustained[id] = sustained;
                _liveVelocity[id] = value & 127;
            }
            for (int i = 0; i < 16; i++) _livePedal[i] = target[2048 + i] != 0;
        }

        public void _ResetLiveMidiOutput()
        {
            ClearLiveMidi();
            if (drumView != null) drumView.Clear(applyGeneration);
            if (clock == null || clock.GetState() != 0) return;
            if (sequencer != null) sequencer.Clear(applyGeneration);
            if (keyboard != null) keyboard.Clear(applyGeneration);
            if (waterfall != null) waterfall.Clear(applyGeneration);
        }

        public void _LiveNoteOn(int channel, int number, int velocity)
        { StartLiveNote(channel, number, velocity, 0); }

        private void StartLiveNote(int channel, int number, int velocity, int direct)
        {
            if (velocity == 0) { _LiveNoteOff(channel, number, 0); return; }
            if (!CanReceiveLiveMidi() || channel < 0 || channel >= 16 || number < 0 || number > 127 || velocity < 0 || velocity > 127) return;
            if (direct > 0 && (channel == 9 || instrumentRouter == null || !instrumentRouter.IsEnabled(direct - 1))) return;
            if (channel == 9)
            {
                if (voicePool.drums != null) voicePool.drums.LiveNoteDown(number, velocity, liveMidiEventSequence++);
                if (drumView != null) drumView.LiveHit(number, velocity, applyGeneration);
                return;
            }
            int id = channel * 128 + number;
            if (_liveHeld[id]) _LiveNoteOff(channel, number, 0);
            voicePool.LiveNoteRelease(id);
            _liveSustained[id] = false;
            _liveHeld[id] = true;
            _liveVelocity[id] = velocity;
            _liveTargetOverrides[id] = direct;
            int key = number - 21;
            if (key >= 0 && key < 88) sequencer.keyDownCount[key]++;
            voicePool.LiveNoteDown(id, number, velocity, applyGeneration);
            if (instrumentRouter != null) RouteLiveVisual(id, true, velocity, true);
            else
            {
                if (waterfall != null) waterfall.LiveNoteDown(id, number);
                if (waterfall != null && waterfall.effects != null) waterfall.effects.LiveHit(number, velocity, applyGeneration);
            }
        }

        public void _LiveNoteOff(int channel, int number, int velocity)
        {
            if (channel == 9) return;
            if (!CanReceiveLiveMidi() || channel < 0 || channel >= 16 || number < 0 || number > 127) return;
            int id = channel * 128 + number;
            if (!_liveHeld[id]) return;
            _liveHeld[id] = false;
            int key = number - 21;
            if (key >= 0 && key < 88) sequencer.keyDownCount[key] = Mathf.Max(0, sequencer.keyDownCount[key] - 1);
            if (instrumentRouter != null) RouteLiveVisual(id, false, 0, false);
            else if (waterfall != null) waterfall.LiveNoteUp(id);
            _liveSustained[id] = _livePedal[channel];
            if (!_liveSustained[id]) voicePool.LiveNoteRelease(id);
        }

        public void _LiveControlChange(int channel, int number, int value)
        {
            if (!CanReceiveLiveMidi() || channel < 0 || channel >= 16) return;
            if (number == 11 || number == 121)
            {
                _liveExpression[channel] = number == 121 ? 127 : Mathf.Clamp(value, 0, 127);
                if (voicePool.strings != null) voicePool.strings.SetExpression(channel, _liveExpression[channel]);
            }
            if (channel == 9)
            {
                if (number == 120 && voicePool.drums != null) voicePool.drums.StopLive();
                return;
            }
            if (number == 64 || number == 121)
            {
                _livePedal[channel] = number == 64 && value >= 64;
                if (!_livePedal[channel])
                    for (int pitch = 0; pitch < 128; pitch++)
                    {
                        int id = channel * 128 + pitch;
                        if (_liveSustained[id]) { voicePool.LiveNoteRelease(id); _liveSustained[id] = false; }
                    }
            }
            if (number == 120 || number == 123)
            {
                if (number == 120) { _livePedal[channel] = false; voicePool.LiveAllSoundOff(channel); }
                for (int pitch = 0; pitch < 128; pitch++)
                {
                    _LiveNoteOff(channel, pitch, 0);
                    int id = channel * 128 + pitch;
                    if (number == 120) { voicePool.LiveNoteRelease(id); _liveSustained[id] = false; }
                }
            }
        }

        private void ClearLiveMidi()
        {
            if (instrumentRouter != null) ClearRoutedViews();
            if (voicePool != null) voicePool.LiveAllSoundOff(-1);
            for (int i = 0; i < 2048; i++) { _liveHeld[i] = false; _liveSustained[i] = false; }
            for (int i = 0; i < 16; i++) _livePedal[i] = false;
        }

        private void ClearMelodicLiveMidi()
        {
            if (voicePool != null)
                for (int channel = 0; channel < 16; channel++) if (channel != 9) voicePool.LiveAllSoundOff(channel);
            for (int i = 0; i < 2048; i++) { _liveHeld[i] = false; _liveSustained[i] = false; }
            for (int i = 0; i < 16; i++) _livePedal[i] = false;
        }

        private void OnDisable()
        {
            if (!isReady || clock == null || clock.GetState() != 0) return;
            if (network != null) network._InvalidateLiveMidi(clock.GetServerSeconds());
            ClearLiveMidi();
            sequencer.Clear(applyGeneration);
            if (keyboard != null) keyboard.Clear(applyGeneration);
            if (waterfall != null) waterfall.Clear(applyGeneration);
        }

        public void CommandPlay()
        {
            if (practice != null) { practice.Play(); return; }
            if (!CanCommand() || isSongLoading || clock.GetState() == 1) return;
            double position = clock.GetState() == 2 ? clock.GetPositionSeconds() : -3;
            if (ApplyState(selectedSongId, 1, position, clock.GetServerSeconds(), false)) network._CommitLocalState();
        }

        public void CommandPause()
        {
            if (practice != null) { practice.Pause(); return; }
            if (!CanCommand() || clock.GetState() != 1) return;
            double now = clock.GetServerSeconds();
            clock.SampleFrame(now, AudioSettings.dspTime);
            if (ApplyState(selectedSongId, 2, clock.GetPositionSeconds(), now, false)) network._CommitLocalState();
        }

        public void CommandStop()
        {
            if (practice != null) { practice.Stop(); return; }
            if (!CanCommand()) return;
            if (ApplyState(selectedSongId, 0, 0, clock.GetServerSeconds(), false)) network._CommitLocalState();
        }

        public float SnapProgressToMeasure(float progress)
        {
            if (_song == null || clock == null || _song.measureStartMs == null || _song.measureStartMs.Length == 0) return progress;
            double duration = clock.GetPlaybackEndSeconds();
            if (duration <= 0) return progress;
            if (progress <= 0 || progress >= 1) return Mathf.Clamp01(progress);
            int time = (int)(progress * duration * 1000.0);
            int[] grid = _song.measureStartMs;
            int low = 0, high = grid.Length - 1;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (grid[mid] < time) low = mid + 1; else high = mid;
            }
            int index = low;
            if (index > 0 && time - grid[index - 1] <= grid[index] - time) index--;
            return (float)(grid[index] / (duration * 1000.0));
        }

        public void CommandSeek(float progress)
        {
            if (practice != null) { practice.Seek(progress); return; }
            if (!CanCommand() || float.IsNaN(progress) || float.IsInfinity(progress)) return;
            double duration = clock.GetPlaybackEndSeconds();
            if (duration <= 0) return;
            int state = clock.GetState() == 1 ? 1 : 2;
            if (ApplyState(selectedSongId, state, Mathf.Clamp01(progress) * duration, clock.GetServerSeconds(), true))
                network._CommitSeek();
        }

        public void ApplyPracticePosition(int state, double seconds, bool rebuild)
        {
            if (practice != null && isReady) ApplyState(selectedSongId, state, seconds, clock.GetServerSeconds(), rebuild);
        }

        public void CommandSelectSong(string id)
        {
            if (practice != null)
            {
                if (isReady && id != selectedSongId && library.FindSong(id) != null) ApplyState(id, 0, 0, clock.GetServerSeconds(), true);
                return;
            }
            if (!CanCommand() || id == selectedSongId || library.FindSong(id) == null) return;
            if (ApplyState(id, 0, 0, clock.GetServerSeconds(), false)) network._CommitLocalState();
        }

        public void ApplyRemoteSnapshot(string hash, string id, int state, double position, double server, int writer, int sequence, bool forceRebuild)
        {
            if (!isReady || practice != null) return;
            if (hash != library.catalogHash) { SetNetworkError("播放器配置不一致，请联系世界作者"); return; }
            if (library.FindSong(id) == null || state < 0 || state > 2 || double.IsNaN(position) || double.IsNaN(server) || double.IsInfinity(position) || double.IsInfinity(server))
            { SetNetworkError("播放器同步数据无效"); return; }
            if (!isSongLoading && !forceRebuild && id == selectedSongId && state == clock.GetState())
            {
                if (!_networkBlocked && (state == 1 || System.Math.Abs(position - clock.GetPositionSeconds()) < 0.000001))
                {
                    clock.UpdateTargetAnchor(position, server);
                    return;
                }
            }
            ApplyState(id, state, position, server, forceRebuild);
        }

        public void SetNetworkError(string message)
        {
            lastError = message;
            if (string.IsNullOrEmpty(message)) return;
            _networkBlocked = true;
            Debug.LogWarning("[MusicMidiPlayer] " + message);
            if (isReady) ClearOutput();
        }

        public int GetState() { return clock == null ? 0 : clock.GetState(); }
        public string GetSongTitle() { return _song == null ? "暂无可播放曲目" : _song.title; }
        public string GetSongAuthor() { return _song == null || string.IsNullOrEmpty(_song.author) ? "" : _song.author; }

        private bool CanCommand()
        {
            return !VRC.SDK3.UdonNetworkCalling.NetworkCalling.InNetworkCall && isReady && gate.CanControl() && network.CanAcceptCommands();
        }

        public bool HasInstrument(int index)
        {
            if (instrumentRouter != null && index == 7) return false;
            if (instrumentRoots == null || instrumentRoots.Length == 0) return index == 0;
            return index >= 0 && index < instrumentRoots.Length && instrumentRoots[index] != null
                && index < instrumentBanks.Length && instrumentBanks[index] != null
                && ((GetStringsView(index) != null && voicePool.strings != null)
                    || (index < instrumentKeyboards.Length && instrumentKeyboards[index] != null
                        && index < instrumentWaterfalls.Length && instrumentWaterfalls[index] != null));
        }

        public StringsView GetStringsView(int index)
        {
            return instrumentStrings != null && index >= 0 && index < instrumentStrings.Length ? instrumentStrings[index] : null;
        }

        public int GetStringsMode(int index) { return GetStringsView(index) != null ? index - 3 : -1; }

        public void CommandStringsArticulation(int style)
        {
            if (!isReady || isSongLoading || !HasSelectedStrings() || (practice == null && !CanCommand())) return;
            _ApplyStringsArticulation(style);
            if (practice == null) network._CommitInstrumentState();
        }

        public int StringArticulationOverrides() { return instrumentRouter == null ? 0 : instrumentRouter.stringArticulationOverrides; }
        public void CommandStringArticulation(int target, int choice)
        {
            if (!isReady || isSongLoading || instrumentRouter == null || target < 2 || target > 5 || choice < 0 || choice > 3
                || !instrumentRouter.IsEnabled(target) || (practice == null && !CanCommand())) return;
            int shift = (target - 2) * 2;
            _ApplyStringArticulationOverrides((StringArticulationOverrides() & ~(3 << shift)) | (choice << shift));
            if (practice == null) network._CommitInstrumentState();
        }
        public void _ApplyStringArticulationOverrides(int choices)
        {
            if (instrumentRouter == null || choices < 0 || choices > 255 || choices == StringArticulationOverrides()) return;
            int previous = StringArticulationOverrides();
            instrumentRouter.stringArticulationOverrides = choices;
            for (int target = 2; target < 6; target++) if (((previous ^ choices) & (3 << ((target - 2) * 2))) != 0)
            {
                if (voicePool.ensemble != null) voicePool.ensemble.RefreshStringArticulation(target);
                var view = GetStringsView(target + 1);
                if (view != null)
                {
                    view.Clear();
                    if (!isSongLoading) view.Refresh((int)(clock.GetPositionSeconds() * 1000));
                }
            }
            if (playerUI != null) playerUI.RefreshState();
        }

        public void _ApplyStringsArticulation(int style)
        {
            if (voicePool.strings == null || style < 0 || style > 2 || voicePool.strings.articulation == style) return;
            if (instrumentRouter != null)
            {
                voicePool.strings.articulation = style;
                int[] held = _CaptureLiveMidi(); ClearLiveMidi(); ClearRoutedViews();
                if (!isSongLoading && clock.GetState() != 0) RebuildCurrent((int)(clock.GetPositionSeconds() * 1000));
                if (CanReceiveLiveMidi()) _RestoreLiveMidi(held);
                return;
            }
            if (!voicePool.strings.activeStrings) { voicePool.strings.articulation = style; return; }
            int[] live = _CaptureLiveMidi();
            ClearMelodicLiveMidi();
            voicePool.CancelGeneration(applyGeneration++);
            sequencer.Clear(applyGeneration);
            voicePool.strings.articulation = style;
            clock.BindSong(_song, voicePool.bank);
            if (!isSongLoading && clock.GetState() != 0) RebuildCurrent((int)(clock.GetPositionSeconds() * 1000));
            if (CanReceiveLiveMidi()) _RestoreLiveMidi(live);
        }

        public PianoBankData GetInstrumentBank(int index)
        {
            return instrumentBanks != null && index >= 0 && index < instrumentBanks.Length
                ? instrumentBanks[index] : library.bank;
        }

        public void CommandSelectInstrument(int index)
        {
            if (instrumentRouter != null)
            {
                if (!isReady || isSongLoading || !HasInstrument(index) || !CanCommand()) return;
                int bit = 1 << instrumentRouter.TargetForView(index);
                int mask = instrumentRouter.enabledMask;
                int appearance = instrumentRouter.pianoAppearance;
                if (index < 2 && index != appearance) { appearance = index; mask |= bit; }
                else mask ^= bit;
                _ApplyInstrumentSelection(mask, appearance);
                network._CommitInstrumentState(); return;
            }
            if (!isReady || isSongLoading || !HasInstrument(index) || index == selectedInstrument) return;
            if (practice == null && !CanCommand()) return;
            _ApplyInstrument(index);
            if (practice == null) network._CommitInstrumentState();
        }

        public void _ApplyInstrument(int index)
        {
            if (instrumentRouter != null) { _ApplyInstrumentSelection(instrumentRouter.enabledMask, index < 2 ? index : instrumentRouter.pianoAppearance); return; }
            if (!isReady || !HasInstrument(index) || index == selectedInstrument) return;
            PianoBankData nextBank = GetInstrumentBank(index);
            if (!nextBank.IsValid()) { SetNetworkError("暂无可播放曲目或有效音源"); return; }
            bool bankChanged = voicePool.bank != nextBank || GetStringsMode(index) != GetStringsMode(selectedInstrument);
            bool rootLanding = waterfall != null && waterfall.landAtKeyRoot;
            int[] live = _CaptureLiveMidi();
            if (keyboard != null) keyboard.Clear(applyGeneration);
            if (pedals != null) pedals.Clear(applyGeneration);
            if (waterfall != null) waterfall.Clear(applyGeneration);
            StringsView oldStrings = GetStringsView(selectedInstrument);
            if (oldStrings != null) oldStrings.Clear();
            if (bankChanged)
            {
                ClearMelodicLiveMidi();
                voicePool.CancelGeneration(applyGeneration++);
                sequencer.Clear(applyGeneration);
            }
            selectedInstrument = index;
            keyboard = instrumentKeyboards[index];
            pedals = index < instrumentPedals.Length ? instrumentPedals[index] : null;
            waterfall = instrumentWaterfalls[index];
            if (waterfall != null) waterfall.SetLandingAtKeyRoot(rootLanding);
            if (keyboard != null) keyboard.BindSequencer(sequencer);
            StringsView nextStrings = GetStringsView(index);
            if (nextStrings != null) voicePool.strings.soundOrigins = nextStrings.bodies;
            if (nextStrings != null) nextStrings.BindSong(_song);
            if (_song != null && !isSongLoading)
            {
                if (keyboard != null) keyboard.BindSong(_song);
                if (pedals != null) pedals.BindSong(_song);
                if (waterfall != null) waterfall.BindSong(_song, sequencer);
            }
            if (bankChanged)
            {
                if (voicePool.strings != null) voicePool.SetStringsMode(GetStringsMode(index));
                voicePool.BindBank(nextBank);
                clock.BindSong(_song, nextBank);
                // Keep the same musical position and shared input state.
                if (!isSongLoading && clock.GetState() != 0)
                    RebuildCurrent((int)(clock.GetPositionSeconds() * 1000.0));
                if (CanReceiveLiveMidi()) _RestoreLiveMidi(live);
            }
            for (int i = 0; i < instrumentRoots.Length; i++)
                if (instrumentRoots[i] != null) instrumentRoots[i].SetActive(i == index);
            int ms = (int)(clock.GetPositionSeconds() * 1000.0);
            if (CanReceiveLiveMidi())
            {
                // Same-bank model changes must never restart sounding voices.
                for (int i = 0; i < 2048; i++)
                    if (_liveHeld[i] && waterfall != null) waterfall.LiveNoteDown(i, i % 128);
                if (keyboard != null) keyboard.RefreshLive((int)(Time.realtimeSinceStartup * 1000f), applyGeneration);
                if (waterfall != null) waterfall.RefreshLive();
            }
            else if (!isSongLoading)
            {
                if (keyboard != null) keyboard.Refresh(ms, applyGeneration, true);
                if (pedals != null) pedals.Refresh(ms, applyGeneration, true);
                if (waterfall != null) waterfall.RebuildAt(ms, applyGeneration);
            }
            if (nextStrings != null) nextStrings.Refresh(ms);
            if (playerUI != null)
            {
                playerUI.RefreshState();
            }
        }

        private bool BindSong(string id)
        {
            MidiSongData next = library.FindSong(id);
            if (next == null || !next.HasPackedData()) { SetNetworkError("曲目数据无效，请重新导入并配置曲库"); return false; }
            // No delayed decoder callbacks are scheduled: Update advances only the latest descriptor.
            // Clearing output before switching also makes it safe to discard the previous arrays.
            if (_song != null && _song != next) _song.ReleaseDecodedData();
            _song = next;
            selectedSongId = id;
            clock.BindSong(next, voicePool.bank);
            isSongLoading = !next.isDecoded || (instrumentRouter != null && !instrumentRouter.IsSongReady(next));
            if (isSongLoading && !next.isDecoded && !next.BeginDecode())
            {
                isSongLoading = false;
                SetNetworkError("曲目加载失败：" + next.decodeError);
                return false;
            }
            if (!isSongLoading) BindDecodedSong();
            return true;
        }

        private void BindDecodedSong()
        {
            MidiSongData next = _song;
            sequencer.BindSong(next);
            voicePool.BindSong(next, sequencer);
            clock.BindSong(next, voicePool.bank);
            if (drumView != null) drumView.BindSong(next);
            if (keyboard != null) keyboard.BindSong(next);
            if (pedals != null) pedals.BindSong(next);
            if (waterfall != null) waterfall.BindSong(next, sequencer);
            StringsView stringsView = GetStringsView(selectedInstrument);
            if (stringsView != null) stringsView.BindSong(next);
            if (practice != null) practice.BindSong(next);
            if (instrumentRouter != null)
                for (int i = 0; i < instrumentRoots.Length; i++)
                {
                    if (instrumentKeyboards[i] != null) { instrumentKeyboards[i].BindSequencer(sequencer); instrumentKeyboards[i].BindSong(next); }
                    if (instrumentPedals[i] != null) instrumentPedals[i].BindSong(next);
                    if (instrumentWaterfalls[i] != null) instrumentWaterfalls[i].BindSong(next, sequencer);
                    if (GetStringsView(i) != null) GetStringsView(i).BindSong(next);
                }
        }

        private void AdvanceSongLoad()
        {
            if (_song == null) return;
            double started = Time.realtimeSinceStartupAsDouble;
            double budget = Mathf.Clamp(decodeFrameBudgetMs, 0.25f, 8f) * 0.001;
            // Decode until completion or the elapsed-time budget is reached.
            while (true)
            {
                bool complete = _song.DecodeStep(16);
                if (!string.IsNullOrEmpty(_song.decodeError))
                {
                    isSongLoading = false;
                    SetNetworkError("曲目加载失败：" + _song.decodeError);
                    return;
                }
                if (complete)
                {
                    if ((instrumentRouter == null || !instrumentRouter.IsBoundSong(_song)) && !_song.IsValid())
                    {
                        isSongLoading = false;
                        SetNetworkError("曲目解码数据无效");
                        return;
                    }
                    if (instrumentRouter != null && !instrumentRouter.IsSongReady(_song))
                    {
                        if (!instrumentRouter.IsBoundSong(_song)) instrumentRouter.BeginBindSong(_song);
                        if (!instrumentRouter.BindStep(32))
                        {
                            if (Time.realtimeSinceStartupAsDouble - started >= budget) return;
                            continue;
                        }
                    }
                    BindDecodedSong();
                    isSongLoading = false;
                    // The transport kept accepting the latest remote/local anchor during loading.
                    // Resume at server time now, including elapsed loading time for playing songs.
                    double now = clock.GetServerSeconds();
                    clock.ResyncLocalPhase(now, AudioSettings.dspTime);
                    if (clock.GetState() != 0) RebuildCurrent((int)(clock.GetPositionSeconds() * 1000.0));
                    return;
                }
                if (Time.realtimeSinceStartupAsDouble - started >= budget) return;
            }
        }

        private bool ApplyState(string id, int state, double position, double server, bool forceRebuild)
        {
            bool preservePose = !isSongLoading && !forceRebuild && !_networkBlocked && !_naturalEnded
                && _song != null && id == selectedSongId
                && System.Math.Abs(position - clock.GetPositionSeconds()) <= 0.002
                && ((clock.GetState() == 1 && state == 2) || (clock.GetState() == 2 && state == 1));
            ClearLiveMidi();
            int old = applyGeneration++;
            voicePool.CancelGeneration(old);
            sequencer.Clear(applyGeneration);
            if (!preservePose && keyboard != null) keyboard.Clear(applyGeneration);
            if (!preservePose && pedals != null) pedals.Clear(applyGeneration);
            if (!preservePose && waterfall != null) waterfall.Clear(applyGeneration);
            if (!preservePose && drumView != null) drumView.Clear(applyGeneration);
            if (_song == null || id != selectedSongId || (!isSongLoading && !_song.isDecoded))
                if (!BindSong(id)) return false;
            lastError = "";
            _networkBlocked = false;
            _naturalEnded = false;
            clock.ApplyAnchor(state, position, server);
            clock.SampleFrame(clock.GetServerSeconds(), AudioSettings.dspTime);
            int ms = (int)(clock.GetPositionSeconds() * 1000.0);
            if (state != 0 && !isSongLoading)
            {
                sequencer.RebuildAt(ms, applyGeneration);
                if (instrumentRouter != null) instrumentRouter.RefreshKeyCounts(ms, false);
                voicePool.RebuildAt(ms, applyGeneration);
                if (drumView != null) drumView.RebuildAt(ms, applyGeneration);
                if (keyboard != null) keyboard.Refresh(ms, applyGeneration, !preservePose);
                if (pedals != null) pedals.Refresh(ms, applyGeneration, !preservePose);
                if (waterfall != null)
                {
                    if (preservePose) waterfall.RebuildGeometryAt(ms, applyGeneration);
                    else waterfall.RebuildAt(ms, applyGeneration);
                }
            }
            _lastFrameMs = ms;
            if (playerUI != null) playerUI.RefreshState();
            return true;
        }

        private void RebuildCurrent(int ms)
        {
            int old = applyGeneration++;
            voicePool.CancelGeneration(old);
            sequencer.RebuildAt(ms, applyGeneration);
            if (instrumentRouter != null) instrumentRouter.RefreshKeyCounts(ms, false);
            voicePool.RebuildAt(ms, applyGeneration);
            if (drumView != null) drumView.RebuildAt(ms, applyGeneration);
            if (keyboard != null) keyboard.Refresh(ms, applyGeneration, true);
            if (pedals != null) pedals.Refresh(ms, applyGeneration, true);
            if (waterfall != null) waterfall.RebuildAt(ms, applyGeneration);
        }

        private void ClearOutput()
        {
            if (instrumentRouter != null) ClearRoutedViews();
            if (drumView != null) drumView.Clear(applyGeneration + 1);
            StringsView stringsView = GetStringsView(selectedInstrument);
            if (stringsView != null) stringsView.Clear();
            ClearLiveMidi();
            int old = applyGeneration++;
            voicePool.CancelGeneration(old);
            sequencer.Clear(applyGeneration);
            if (keyboard != null) keyboard.Clear(applyGeneration);
            if (pedals != null) pedals.Clear(applyGeneration);
            if (waterfall != null) waterfall.Clear(applyGeneration);
        }

        public void _ApplyInstrumentSelection(int mask, int appearance)
        {
            if (instrumentRouter == null || mask < 0 || mask > 63 || appearance < 0 || appearance > 1) return;
            if (mask == instrumentRouter.enabledMask && appearance == instrumentRouter.pianoAppearance) return;
            int[] held = _CaptureLiveMidi();
            bool changedAudio = mask != instrumentRouter.enabledMask;
            if (changedAudio) { ClearMelodicLiveMidi(); voicePool.CancelGeneration(applyGeneration++); sequencer.Clear(applyGeneration); }
            ClearRoutedViews();
            instrumentRouter.enabledMask = mask; instrumentRouter.pianoAppearance = appearance;
            instrumentRouter.ResolveSelection();
            // Retain a stable existing menu index for legacy integrations and the shared opening.
            selectedInstrument = appearance;
            for (int i = 2; i < instrumentRoots.Length && !instrumentRouter.IsEnabled(0); i++)
                if (instrumentRouter.IsViewEnabled(i)) { selectedInstrument = i; break; }
            keyboard = instrumentKeyboards[selectedInstrument]; pedals = instrumentPedals[selectedInstrument];
            waterfall = instrumentWaterfalls[selectedInstrument];
            ApplyRoutedRoots();
            if (changedAudio && !isSongLoading && clock.GetState() != 0) RebuildCurrent((int)(clock.GetPositionSeconds() * 1000));
            if (changedAudio && CanReceiveLiveMidi()) _RestoreLiveMidi(held);
            if (!changedAudio && CanReceiveLiveMidi())
                for (int id = 0; id < 2048; id++) if (_liveHeld[id]) RouteLiveVisual(id, true, _liveVelocity[id], false);
            if (instrumentRouter != null) instrumentRouter.RefreshKeyCounts((int)(clock.GetPositionSeconds() * 1000), CanReceiveLiveMidi());
            if (playerUI != null) playerUI.RefreshState();
        }
        public string FileProgramMap() { return instrumentRouter == null ? "" : instrumentRouter.fileProgramMap; }
        public void _ApplyRoutingState(int mask, int appearance, string map)
        {
            if (instrumentRouter == null || mask < 0 || mask > 63 || appearance < 0 || appearance > 1
                || !instrumentRouter.IsValidFileProgramMap(map)) return;
            if (mask != instrumentRouter.enabledMask)
            {
                instrumentRouter.fileProgramMap = map;
                _ApplyInstrumentSelection(mask, appearance);
            }
            else
            {
                _ApplyInstrumentSelection(mask, appearance); _ApplyFileProgramMap(map);
            }
        }
        public void CommandFileProgramTarget(int program, int target)
        {
            if (instrumentRouter == null || !CanCommand() || isSongLoading || program < 0 || program > 127
                || target < -1 || target > 5) return;
            string map = FileProgramMap();
            if (map == "") map = new string('0', 128);
            map = map.Substring(0, program) + (target + 1).ToString() + map.Substring(program + 1);
            if (target >= 0 && !instrumentRouter.IsEnabled(target))
            {
                instrumentRouter.fileProgramMap = map;
                _ApplyInstrumentSelection(instrumentRouter.enabledMask | (1 << target), instrumentRouter.pianoAppearance);
            }
            else _ApplyFileProgramMap(map);
            network._CommitInstrumentState();
        }
        public void CommandResetFileProgramMap()
        {
            if (instrumentRouter == null || !CanCommand() || isSongLoading) return;
            _ApplyFileProgramMap(""); network._CommitInstrumentState();
        }
        public void _ApplyFileProgramMap(string map)
        {
            if (instrumentRouter == null || !instrumentRouter.IsValidFileProgramMap(map) || map == FileProgramMap()) return;
            instrumentRouter.fileProgramMap = map; instrumentRouter.ResolveSelection();
            if (isReady && !isSongLoading && clock.GetState() != 0)
            {
                ClearRoutedViews(); RebuildCurrent((int)(clock.GetPositionSeconds() * 1000));
            }
            if (playerUI != null) playerUI.RefreshState();
        }
        private void ApplyRoutedRoots()
        {
            int count = 0;
            for (int t = 0; t < 7; t++) if (instrumentRouter.IsEnabled(t)) count++;
            for (int i = 0; i < instrumentRoots.Length; i++) if (instrumentRoots[i] != null)
            {
                if (i < instrumentRouter.viewRestPositions.Length)
                    instrumentRoots[i].transform.localPosition = instrumentRouter.viewRestPositions[i]
                        + (count > 1 ? instrumentRouter.stageOffsets[instrumentRouter.TargetForView(i)] : Vector3.zero);
                instrumentRoots[i].SetActive(instrumentRouter.IsViewEnabled(i));
            }
            voicePool.ensemble.soundOrigins[0] = instrumentKeyboards[instrumentRouter.pianoAppearance].transform;
            voicePool.ensemble.RelocateSources();
        }
        private void ClearRoutedViews()
        {
            for (int i = 0; i < instrumentRoots.Length; i++)
            {
                if (instrumentKeyboards[i] != null) instrumentKeyboards[i].Clear(applyGeneration);
                if (instrumentPedals[i] != null) instrumentPedals[i].Clear(applyGeneration);
                if (instrumentWaterfalls[i] != null) instrumentWaterfalls[i].Clear(applyGeneration);
                if (GetStringsView(i) != null) GetStringsView(i).Clear();
            }
        }
        private void RefreshRoutedViews(int ms)
        {
            bool rebuild = _routedViewGeneration != applyGeneration;
            bool live = CanReceiveLiveMidi();
            for (int i = 0; i < instrumentRoots.Length; i++)
            {
                if (!instrumentRouter.IsViewEnabled(i)) continue;
                var keys = instrumentKeyboards[i]; var fall = instrumentWaterfalls[i]; var pedal = instrumentPedals[i];
                if (live)
                {
                    if (keys != null) keys.RefreshLive((int)(Time.realtimeSinceStartup * 1000), applyGeneration);
                    if (fall != null) { fall.RefreshLive(); if (fall.effects != null) fall.effects.RefreshLive(applyGeneration); }
                }
                else if (clock.GetState() != 0)
                {
                    if (keys != null) keys.Refresh(ms, applyGeneration, rebuild);
                    if (pedal != null) pedal.Refresh(ms, applyGeneration, rebuild);
                    if (fall != null) { if (rebuild) fall.RebuildAt(ms, applyGeneration); else fall.Refresh(ms, applyGeneration); }
                }
                if (GetStringsView(i) != null) GetStringsView(i).Refresh(ms);
            }
            _routedViewGeneration = applyGeneration;
        }
        private void RouteLiveVisual(int id, bool down, int velocity, bool hit)
        {
            int target = instrumentRouter.liveTargets[id];
            for (int i = 0; i < instrumentRoots.Length; i++)
            {
                var fall = instrumentWaterfalls[i];
                if (fall == null || !instrumentRouter.IsViewEnabled(i)) continue;
                if (!down) fall.LiveNoteUp(id);
                else if (instrumentRouter.TargetForView(i) == target)
                {
                    fall.LiveNoteDown(id, id % 128);
                    if (hit && fall.effects != null) fall.effects.LiveHit(id % 128, velocity, applyGeneration);
                }
            }
        }
    }
}
