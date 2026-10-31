using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Piano Pedal View")]
    public class PianoPedalView : UdonSharpBehaviour
    {
        // Physical order: left CC67 (soft), middle CC66 (sostenuto), right CC64 (sustain).
        public Transform[] pedalTransforms;
        public Vector3[] pedalPivotPositions;
        public Vector3[] pedalRotationAxes;
        public float[] pedalPressAngles;
        public int smoothingMs = 80;

        private MidiSongData _song;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;
        private float[] _fromValues;
        private float[] _targetValues;
        private int[] _transitionStartMs;
        private int _generation = -1;
        private int _lastPositionMs = -1;
        private int _count;

        public void Initialize()
        {
            if (_restPositions != null) return;
            _count = pedalTransforms == null ? 0 : Mathf.Min(3, pedalTransforms.Length);
            if (pedalPivotPositions == null || pedalPivotPositions.Length < _count
                || pedalRotationAxes == null || pedalRotationAxes.Length < _count
                || pedalPressAngles == null || pedalPressAngles.Length < _count) _count = 0;
            _restPositions = new Vector3[_count];
            _restRotations = new Quaternion[_count];
            _fromValues = new float[_count]; _targetValues = new float[_count];
            _transitionStartMs = new int[_count];
            for (int i = 0; i < _count; i++)
            {
                if (pedalTransforms[i] == null) continue;
                _restPositions[i] = pedalTransforms[i].localPosition;
                _restRotations[i] = pedalTransforms[i].localRotation;
            }
        }

        public void BindSong(MidiSongData song)
        {
            Initialize();
            if (_song == song) return;
            _song = song;
            Clear(_generation);
        }

        public void Refresh(int positionMs, int generation, bool force)
        {
            Initialize();
            if (generation < _generation) return;
            _generation = generation;
            if (positionMs < 0) { Clear(generation); return; }
            positionMs = Mathf.Max(0, positionMs);
            if (!force && positionMs == _lastPositionMs) return;
            // A new generation alone does not reset an in-progress press on pause/resume.
            if (_lastPositionMs >= 0 && positionMs < _lastPositionMs) force = true;
            for (int i = 0; i < _count; i++)
            {
                float target = _song == null ? 0f : Mathf.Clamp01(_song.QueryPedalValue(i, positionMs) / 127f);
                if (force || smoothingMs <= 0)
                {
                    _fromValues[i] = target; _targetValues[i] = target; _transitionStartMs[i] = positionMs;
                    SetPose(i, target);
                    continue;
                }
                float current = EvaluateValue(i, positionMs);
                if (!Mathf.Approximately(target, _targetValues[i]))
                {
                    _fromValues[i] = current; _targetValues[i] = target; _transitionStartMs[i] = positionMs;
                }
                SetPose(i, EvaluateValue(i, positionMs));
            }
            _lastPositionMs = positionMs;
        }

        public void Clear(int generation)
        {
            Initialize();
            if (generation < _generation) return;
            _generation = generation; _lastPositionMs = -1;
            for (int i = 0; i < _count; i++)
            {
                _fromValues[i] = 0f; _targetValues[i] = 0f; _transitionStartMs[i] = 0;
                if (pedalTransforms[i] == null) continue;
                pedalTransforms[i].localPosition = _restPositions[i];
                pedalTransforms[i].localRotation = _restRotations[i];
            }
        }

        private float EvaluateValue(int index, int positionMs)
        {
            float t = smoothingMs <= 0 ? 1f : Mathf.Clamp01((positionMs - _transitionStartMs[index]) / (float)smoothingMs);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(_fromValues[index], _targetValues[index], t);
        }

        private void SetPose(int index, float amount)
        {
            Transform pedal = pedalTransforms[index];
            if (pedal == null) return;
            Quaternion rotation = Quaternion.AngleAxis(pedalPressAngles[index] * amount, pedalRotationAxes[index]);
            Vector3 pivot = pedalPivotPositions[index];
            pedal.localPosition = pivot + rotation * (_restPositions[index] - pivot);
            pedal.localRotation = rotation * _restRotations[index];
        }
    }
}
