using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Logic
{
    // Personal practice state: no score and no network authority or traffic.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiPracticeController : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public bool blockingMode = true;
        public bool loopEnabled;
        public bool countdownEnabled = true;
        public float streamSpeed = 1f;
        public Slider speedSlider;
        public Slider loopStartSlider;
        public Slider loopEndSlider;
        public Button blockingButton;
        public Button streamingButton;
        public Button trackButton;
        public TMP_Text loopUnitLabel;
        public TMP_Text loopStartLabel;
        public TMP_Text loopEndLabel;
        public TMP_Text summaryLabel;
        public TMP_Text trackLabel;
        public TMP_Text speedLabel;
        public TMP_Text modeLabel;
        public TMP_Text loopLabel;
        public TMP_Text countdownLabel;
        public bool running;
        public bool waiting;
        public double positionSeconds;
        public double loopStartSeconds;
        public double loopEndSeconds;
        private MidiSongData _song;
        private double _lastDsp;
        private double _countdown;
        private int _cursor;
        private int _remaining;
        private bool[] _needed = new bool[128];
        private bool[] _targets = new bool[0];
        private int _track;
        private bool _measureLoop = true;
        private float _nextUi;

        public bool IsTargetTrack(int track)
        {
            return track >= 0 && track < _targets.Length && _targets[track];
        }

        public void BindSong(MidiSongData song)
        {
            _song = song;
            running = false; waiting = false; positionSeconds = 0; _countdown = 0;
            loopStartSeconds = 0; loopEndSeconds = song.durationMs * 0.001;
            int tracks = song.trackNames == null ? 0 : song.trackNames.Length;
            for (int i = 0; i < song.trackIndex.Length; i++) tracks = Mathf.Max(tracks, song.trackIndex[i] + 1);
            _targets = new bool[tracks];
            for (int i = 0; i < tracks; i++) _targets[i] = true;
            _track = 0; _cursor = 0;
            if (loopStartSlider != null) loopStartSlider.SetValueWithoutNotify(0);
            if (loopEndSlider != null) loopEndSlider.SetValueWithoutNotify(1);
            RefreshUI();
        }

        private void ResetCursor()
        {
            waiting = false; _remaining = 0;
            for (int i = 0; i < 128; i++) _needed[i] = false;
            int low = 0, high = _song == null ? 0 : _song.startMs.Length;
            int ms = (int)System.Math.Round(positionSeconds * 1000);
            while (low < high) { int mid = (low + high) / 2; if (_song.startMs[mid] < ms) low = mid + 1; else high = mid; }
            _cursor = low;
        }

        private void Jump(double seconds, bool countIn)
        {
            positionSeconds = seconds;
            _lastDsp = AudioSettings.dspTime;
            _countdown = countIn && countdownEnabled ? 3 : 0;
            ResetCursor();
            facade.ApplyPracticePosition(running && _countdown <= 0 ? 1 : 2, positionSeconds, true);
            RefreshUI();
        }

        public void Play()
        {
            if (_song == null || facade.isSongLoading || running) return;
            running = true;
            bool restart = facade.GetState() == 0 || positionSeconds >= (loopEnabled ? loopEndSeconds : _song.durationMs * 0.001);
            if (restart) Jump(loopEnabled ? loopStartSeconds : 0, true);
            else { _lastDsp = AudioSettings.dspTime; facade.ApplyPracticePosition(waiting ? 2 : 1, positionSeconds, true); }
        }
        public void Pause() { if (!running) return; running = false; _countdown = 0; facade.ApplyPracticePosition(2, positionSeconds, true); RefreshUI(); }
        public void Stop() { running = false; waiting = false; _countdown = 0; positionSeconds = 0; ResetCursor(); facade.ApplyPracticePosition(0, 0, true); RefreshUI(); }
        public void Seek(float normalized)
        {
            if (_song == null) return;
            double target = Mathf.Clamp01(normalized) * facade.clock.GetPlaybackEndSeconds();
            if (loopEnabled) target = System.Math.Max(loopStartSeconds, System.Math.Min(loopEndSeconds, target));
            Jump(target, false);
        }

        // Called before the facade samples its clock, so a frame cannot cross an unanswered chord.
        public void AdvanceFrame(double dsp)
        {
            double delta = _lastDsp <= 0 ? 0 : System.Math.Max(0, dsp - _lastDsp);
            _lastDsp = dsp;
            if (_song == null || facade.isSongLoading || !running) { if (Time.unscaledTime >= _nextUi) RefreshUI(); return; }
            if (_countdown > 0)
            {
                _countdown = System.Math.Max(0, _countdown - delta);
                facade.clock.SetPracticeFrame(2, positionSeconds, dsp);
                if (_countdown > 0) { RefreshUI(); return; }
                delta = 0;
            }
            if (!waiting)
            {
                double end = loopEnabled ? loopEndSeconds : _song.durationMs * 0.001;
                double next = positionSeconds + delta * (blockingMode ? 1 : streamSpeed);
                if (blockingMode)
                {
                    while (_cursor < _song.startMs.Length && (_song.channel[_cursor] == 9 || !IsTargetTrack(_song.trackIndex[_cursor]))) _cursor++;
                    if (_cursor < _song.startMs.Length && _song.startMs[_cursor] * 0.001 < end && _song.startMs[_cursor] * 0.001 <= next)
                    {
                        int groupMs = _song.startMs[_cursor];
                        positionSeconds = groupMs * 0.001; _remaining = 0;
                        while (_cursor < _song.startMs.Length && _song.startMs[_cursor] == groupMs)
                        {
                            int pitch = _song.pitch[_cursor];
                            if (_song.channel[_cursor] != 9 && IsTargetTrack(_song.trackIndex[_cursor]) && !_needed[pitch]) { _needed[pitch] = true; _remaining++; }
                            _cursor++;
                        }
                        waiting = _remaining > 0;
                    }
                    else positionSeconds = next;
                }
                else positionSeconds = next;
                if (positionSeconds >= end)
                {
                    if (loopEnabled) { Jump(loopStartSeconds, true); return; }
                    positionSeconds = end; running = false;
                    facade.ApplyPracticePosition(2, positionSeconds, true);
                }
            }
            facade.clock.SetPracticeFrame(running && !waiting ? 1 : 2, positionSeconds, dsp);
            if (Time.unscaledTime >= _nextUi) RefreshUI();
        }

        public void AcceptNote(int pitch)
        {
            if (!running || !blockingMode || !waiting || pitch < 0 || pitch > 127 || !_needed[pitch]) return;
            _needed[pitch] = false; _remaining--;
            if (_remaining == 0) { waiting = false; _lastDsp = AudioSettings.dspTime; }
            RefreshUI();
        }

        public void _UseBlockingMode() { if (!blockingMode) _ToggleMode(); }
        public void _UseStreamingMode() { if (blockingMode) _ToggleMode(); }
        public void _ToggleMode() { blockingMode = !blockingMode; Jump(positionSeconds, false); }
        public void _SpeedChanged() { if (speedSlider != null) streamSpeed = Mathf.Clamp(speedSlider.value, 0.25f, 2f); RefreshUI(); }
        public void _ToggleLoop() { loopEnabled = !loopEnabled; if (loopEnabled && (positionSeconds < loopStartSeconds || positionSeconds >= loopEndSeconds)) Jump(loopStartSeconds, running); RefreshUI(); }
        public void _ToggleCountdown() { countdownEnabled = !countdownEnabled; RefreshUI(); }
        public void _ToggleLoopUnit() { _measureLoop = !_measureLoop; RefreshUI(); }
        public void _SetA() { SetBoundary(true, positionSeconds); }
        public void _SetB() { SetBoundary(false, positionSeconds); }
        public void _LoopStartChanged() { if (_song != null && loopStartSlider != null) SetBoundary(true, loopStartSlider.value * _song.durationMs * 0.001); }
        public void _LoopEndChanged() { if (_song != null && loopEndSlider != null) SetBoundary(false, loopEndSlider.value * _song.durationMs * 0.001); }
        private void SetBoundary(bool start, double seconds)
        {
            if (_song == null) return;
            if (_measureLoop && _song.measureStartMs.Length > 0)
            {
                int best = 0; double error = double.MaxValue;
                for (int i = 0; i < _song.measureStartMs.Length; i++)
                { double d = System.Math.Abs(_song.measureStartMs[i] * 0.001 - seconds); if (d < error) { error = d; best = i; } }
                seconds = _song.measureStartMs[best] * 0.001;
            }
            seconds = System.Math.Max(0, System.Math.Min(_song.durationMs * 0.001, seconds));
            if (start && seconds < loopEndSeconds) loopStartSeconds = seconds;
            if (!start && seconds > loopStartSeconds) loopEndSeconds = seconds;
            if (loopStartSlider != null) loopStartSlider.SetValueWithoutNotify((float)(loopStartSeconds * 1000 / _song.durationMs));
            if (loopEndSlider != null) loopEndSlider.SetValueWithoutNotify((float)(loopEndSeconds * 1000 / _song.durationMs));
            if (loopEnabled && (positionSeconds < loopStartSeconds || positionSeconds >= loopEndSeconds)) Jump(loopStartSeconds, running);
            RefreshUI();
        }
        public void _PreviousTrack() { if (_targets.Length > 0) _track = (_track + _targets.Length - 1) % _targets.Length; RefreshUI(); }
        public void _NextTrack() { if (_targets.Length > 0) _track = (_track + 1) % _targets.Length; RefreshUI(); }
        public void _ToggleTrack() { if (_targets.Length > 0) { _targets[_track] = !_targets[_track]; Jump(positionSeconds, false); } RefreshUI(); }
        public void _AllTracks() { for (int i = 0; i < _targets.Length; i++) _targets[i] = true; Jump(positionSeconds, false); }
        public void _NoTracks() { for (int i = 0; i < _targets.Length; i++) _targets[i] = false; Jump(positionSeconds, false); }
        private string TimeText(double seconds)
        {
            int centiseconds = (int)System.Math.Round(System.Math.Max(0, seconds) * 100);
            return (centiseconds / 6000).ToString("00") + ":" + (centiseconds / 100 % 60).ToString("00") + "." + (centiseconds % 100).ToString("00");
        }
        private void SelectButton(Button button, bool selected)
        {
            if (button == null) return;
            Color background = selected ? new Color(0.529f, 0.886f, 0.898f) : new Color(0.031f, 0.051f, 0.047f);
            ColorBlock colors = button.colors;
            if (colors.normalColor != background)
            {
                colors.normalColor = background; colors.highlightedColor = Color.Lerp(background, Color.white, 0.12f);
                colors.pressedColor = Color.Lerp(background, Color.black, 0.15f); button.colors = colors;
            }
            TMP_Text text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.color = selected ? new Color(0.031f, 0.051f, 0.047f) : new Color(0.529f, 0.886f, 0.898f);
        }
        private string Tr(string key) { return facade.playerUI.Tr(key); }
        public void RefreshUI()
        {
            _nextUi = Time.unscaledTime + 0.1f;
            SelectButton(blockingButton, blockingMode);
            SelectButton(streamingButton, !blockingMode);
            SelectButton(trackButton, _targets.Length > 0 && _track < _targets.Length && _targets[_track]);
            if (loopUnitLabel != null) loopUnitLabel.text = Tr(_measureLoop ? "practice.measure_loop" : "practice.free_loop");
            if (loopStartLabel != null) loopStartLabel.text = "A  " + TimeText(loopStartSeconds);
            if (loopEndLabel != null) loopEndLabel.text = "B  " + TimeText(loopEndSeconds);
            if (modeLabel != null) modeLabel.text = Tr(blockingMode ? "practice.blocking" : "practice.streaming");
            if (speedSlider != null) speedSlider.interactable = !blockingMode;
            if (speedLabel != null) speedLabel.text = (blockingMode ? "—" : streamSpeed.ToString("0.##") + "×");
            if (loopLabel != null) loopLabel.text = Tr(loopEnabled ? "practice.loop_on" : "practice.loop_off");
            if (countdownLabel != null) countdownLabel.text = Tr(countdownEnabled ? "practice.countdown_on" : "practice.countdown_off");
            if (trackLabel != null && _targets.Length > 0)
            {
                string name = _song.trackNames != null && _track < _song.trackNames.Length ? _song.trackNames[_track] : "Track " + (_track + 1);
                trackLabel.text = (_targets[_track] ? "● " : "○ ") + name;
            }
            if (summaryLabel != null)
            {
                string state = _countdown > 0 ? Tr("practice.countdown") + " " + Mathf.CeilToInt((float)_countdown) : waiting ? Tr("practice.waiting") + " " + _remaining : Tr("practice.no_score");
                summaryLabel.text = state;
            }
        }
    }
}
