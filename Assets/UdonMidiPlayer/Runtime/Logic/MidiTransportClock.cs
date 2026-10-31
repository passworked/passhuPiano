using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Logic
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Logic/Transport Clock")]
    public class MidiTransportClock : UdonSharpBehaviour
    {
        public MusicMidiPlayer.Audio.PercussionVoicePool drums;
        public bool practiceMode;
        public MusicMidiPlayer.Audio.StringsVoicePool strings;
        public MidiInstrumentRouter instrumentRouter;
        public void SetPracticeFrame(int state, double seconds, double dsp)
        {
            _state = state; _position = ClampPosition(seconds); _anchorPosition = _position;
            _frameDsp = dsp;
        }
        private int _state;
        private double _position;
        private double _anchorPosition;
        private double _anchorServer;
        private double _frameDsp;
        private double _end;
        private double _dspAnchor;
        private double _localAnchor;
        private double _lastCalibration;

        public void BindSong(MidiSongData song, PianoBankData bank)
        {
            int tail = strings != null && strings.activeStrings ? (strings.articulation == 0 ? 700 : 180) : (bank == null ? 0 : bank.maxReleaseMs);
            if (instrumentRouter != null) tail = instrumentRouter.MaxReleaseMs();
            _end = song == null ? 0 : ((double)song.durationMs + tail) / 1000.0;
            if (song != null && drums != null) _end = System.Math.Max(_end, drums.playbackEndMs * .001);
        }

        public void ApplyAnchor(int state, double positionSeconds, double serverSeconds)
        {
            _state = state;
            _anchorPosition = ClampPosition(positionSeconds);
            _anchorServer = serverSeconds;
            if (state == 0) _anchorPosition = 0;
            _position = GetPositionAtServerTime(GetServerSeconds());
            _localAnchor = _position;
            _dspAnchor = AudioSettings.dspTime;
            _lastCalibration = _dspAnchor;
        }

        public double GetServerSeconds()
        {
            return Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer) ? VRC.SDKBase.Networking.GetServerTimeInSeconds() : Time.realtimeSinceStartupAsDouble;
        }

        public double GetPositionAtServerTime(double serverSeconds)
        {
            if (_state == 0) return 0;
            if (_state == 2) return _anchorPosition;
            double delta = Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer)
                ? VRC.SDKBase.Networking.CalculateServerDeltaTime(serverSeconds, _anchorServer)
                : serverSeconds - _anchorServer;
            return ClampPosition(_anchorPosition + delta);
        }

        public void SampleFrame(double serverNowSeconds, double dspNowSeconds)
        {
            _frameDsp = dspNowSeconds;
            if (practiceMode) return;
            if (_state != 1) { _position = _state == 0 ? 0 : _anchorPosition; return; }
            double local = _localAnchor + dspNowSeconds - _dspAnchor;
            if (dspNowSeconds - _lastCalibration >= 0.5 || dspNowSeconds < _dspAnchor)
            {
                _lastCalibration = dspNowSeconds;
                double error = GetPositionAtServerTime(serverNowSeconds) - local;
                if (System.Math.Abs(error) > 0.02)
                {
                    double change = System.Math.Max(-0.005, System.Math.Min(0.005, error));
                    _localAnchor += change;
                    local += change;
                }
            }
            _position = ClampPosition(System.Math.Max(_position, local));
        }

        public void UpdateTargetAnchor(double positionSeconds, double serverSeconds)
        {
            _anchorPosition = ClampPosition(positionSeconds);
            _anchorServer = serverSeconds;
        }

        public void ResyncLocalPhase(double serverSeconds, double dspSeconds)
        {
            _localAnchor = GetPositionAtServerTime(serverSeconds);
            _position = _localAnchor;
            _dspAnchor = dspSeconds;
            _frameDsp = dspSeconds;
            _lastCalibration = dspSeconds;
        }

        public double GetPositionSeconds() { return _position; }
        public double GetFrameDspSeconds() { return _frameDsp; }
        public int GetState() { return _state; }
        public double GetPlaybackEndSeconds() { return _end; }

        private double ClampPosition(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            // Negative song time is the synchronized three-second opening.
            if (value < -3) return -3;
            return value > _end ? _end : value;
        }
    }
}
