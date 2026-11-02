using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MusicMidiPlayer.Logic
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiPerformanceInput : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        // Virtual instruments use distinct channels descending from this channel, skipping drums.
        [Range(0, 15)] public int midiChannel = 15;
        [Range(1, 127)] public int velocity = 100;
        public int baseNote = 60;
        public bool[] enabledFingers = { false, true, false, false, false, false, true, false, false, false };
        public Toggle[] fingerToggles = new Toggle[10];
        public GameObject vrSettings;
        public GameObject desktopSettings;
        public GameObject[] vrOnlySettings = new GameObject[0];
        public RectTransform[] commonSettingsRows = new RectTransform[0];
        public Vector2[] commonSettingsPositions = new Vector2[0];
        public Transform[] instrumentFrames;
        public Vector3[] instrumentMinimum;
        public Vector3[] instrumentMaximum;
        // Rest-pose key bounds in instrument coordinates, flattened instrument * 88 + key.
        public Vector3[] keyMinimum;
        public Vector3[] keyMaximum;
        public float touchHeight = 0.008f;
        public float releaseHeight = 0.025f;
        public float penetrationDepth = 0.06f;
        [HideInInspector] public MidiPerformanceSeat activeSeat;
        private int[] _keys = { (int)KeyCode.A, (int)KeyCode.W, (int)KeyCode.S, (int)KeyCode.E, (int)KeyCode.D,
            (int)KeyCode.F, (int)KeyCode.T, (int)KeyCode.G, (int)KeyCode.Y, (int)KeyCode.H, (int)KeyCode.U, (int)KeyCode.J,
            (int)KeyCode.K, (int)KeyCode.O, (int)KeyCode.L, (int)KeyCode.P, (int)KeyCode.Semicolon, (int)KeyCode.Quote };
        private int[] _distal = { (int)HumanBodyBones.LeftThumbDistal, (int)HumanBodyBones.LeftIndexDistal,
            (int)HumanBodyBones.LeftMiddleDistal, (int)HumanBodyBones.LeftRingDistal, (int)HumanBodyBones.LeftLittleDistal,
            (int)HumanBodyBones.RightThumbDistal, (int)HumanBodyBones.RightIndexDistal, (int)HumanBodyBones.RightMiddleDistal,
            (int)HumanBodyBones.RightRingDistal, (int)HumanBodyBones.RightLittleDistal };
        private int[] _middle = { (int)HumanBodyBones.LeftThumbIntermediate, (int)HumanBodyBones.LeftIndexIntermediate,
            (int)HumanBodyBones.LeftMiddleIntermediate, (int)HumanBodyBones.LeftRingIntermediate, (int)HumanBodyBones.LeftLittleIntermediate,
            (int)HumanBodyBones.RightThumbIntermediate, (int)HumanBodyBones.RightIndexIntermediate, (int)HumanBodyBones.RightMiddleIntermediate,
            (int)HumanBodyBones.RightRingIntermediate, (int)HumanBodyBones.RightLittleIntermediate };
        private int[] _desktopNotes = new int[18]; // note + 1, zero means released
        private int[] _fingerNotes = new int[10];
        private int[] _noteUsers = new int[1024]; // view * 128 + pitch
        private Vector3[] _previous = new Vector3[10];
        private bool[] _hasPrevious = new bool[10];
        private bool _sustain;
        private int _sustainChannel;
        private int _instrument = -1;
        private int _generation = -1;
        private bool _wasVr;
        private bool _platformUiReady;
        private bool _platformUiVr;

        private void Start() { _OnFingersChanged(); _RefreshPlatformUI(); }

        public void _SelectAllFingers() { SetAllFingers(true); }
        public void _ClearAllFingers() { SetAllFingers(false); }

        private void SetAllFingers(bool selected)
        {
            for (int i = 0; i < 10; i++)
            {
                enabledFingers[i] = selected;
                if (fingerToggles != null && i < fingerToggles.Length && fingerToggles[i] != null)
                    fingerToggles[i].SetIsOnWithoutNotify(selected);
            }
            _OnFingersChanged();
        }

        public void _RefreshPlatformUI()
        {
            VRCPlayerApi player = VRC.SDKBase.Networking.LocalPlayer;
            if (Utilities.IsValid(player)) ApplyPlatformUI(player.IsUserInVR());
        }

        private void ApplyPlatformUI(bool vr)
        {
            if (_platformUiReady && _platformUiVr == vr) return;
            if (vrSettings != null) vrSettings.SetActive(vr);
            if (desktopSettings != null) desktopSettings.SetActive(!vr);
            for (int i = 0; i < vrOnlySettings.Length; i++)
                if (vrOnlySettings[i] != null) vrOnlySettings[i].SetActive(vr);
            for (int i = 0; i < commonSettingsRows.Length && i < commonSettingsPositions.Length; i++)
                if (commonSettingsRows[i] != null)
                    commonSettingsRows[i].anchoredPosition = commonSettingsPositions[i] + new Vector2(0, vr ? 0 : 137);
            _platformUiReady = true; _platformUiVr = vr;
        }

        public void EnterSeat(MidiPerformanceSeat seat)
        {
            if (activeSeat != null && activeSeat != seat) activeSeat.ExitLocal();
            ReleaseAll();
            activeSeat = seat;
        }

        public bool CanPlayView(int view)
        {
            return facade != null && view >= 0 && instrumentFrames != null && view < instrumentFrames.Length
                && instrumentFrames[view] != null && instrumentFrames[view].gameObject.activeInHierarchy
                && facade.IsInstrumentChecked(view);
        }

        public int PerformanceChannel(int view)
        {
            if (facade.instrumentRouter == null) return midiChannel;
            int target = facade.instrumentRouter.TargetForView(view);
            int channel = midiChannel == 9 ? 8 : midiChannel;
            for (int i = 0; i < target; i++)
            {
                channel = (channel + 15) % 16;
                if (channel == 9) channel = 8;
            }
            return channel;
        }

        public void LeaveSeat(MidiPerformanceSeat seat)
        {
            if (activeSeat != seat) return;
            ReleaseAll();
            activeSeat = null;
        }

        public void _OnFingersChanged()
        {
            for (int i = 0; i < 10; i++)
            {
                if (fingerToggles != null && i < fingerToggles.Length && fingerToggles[i] != null)
                    enabledFingers[i] = fingerToggles[i].isOn;
                if (!enabledFingers[i]) { SetFinger(i, 0); _hasPrevious[i] = false; }
            }
        }

        public override void PostLateUpdate()
        {
            VRCPlayerApi player = VRC.SDKBase.Networking.LocalPlayer;
            if (facade == null || !Utilities.IsValid(player)) { ReleaseAll(); return; }
            bool vr = player.IsUserInVR();
            ApplyPlatformUI(vr);
            if (_instrument != facade.selectedInstrument || _generation != facade.applyGeneration || vr != _wasVr)
            {
                ReleaseAll();
                _instrument = facade.selectedInstrument; _generation = facade.applyGeneration; _wasVr = vr;
            }
            if (activeSeat != null && !CanPlayView(activeSeat.instrumentIndex)) activeSeat.ExitLocal();
            // Leaving must remain possible while stopped, loading, locked or in a menu.
            if (activeSeat != null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            { activeSeat.ExitLocal(); return; }
            if (!facade.CanReceiveLiveMidi() || (facade.practice == null && (facade.gate == null || !facade.gate.CanControl())))
            { ReleaseAll(); return; }
            var ensemble = facade.voicePool.ensemble;
            if (ensemble != null) ensemble.BeginOutputBatch();
            if (vr) ReadFingers(player);
            else if (activeSeat != null && activeSeat.gameObject.activeInHierarchy) ReadDesktop();
            else ReleaseAll();
            if (ensemble != null) ensemble.EndOutputBatch();
        }

        private void ReadDesktop()
        {
            if (Input.GetKeyDown(KeyCode.Z)) baseNote = Mathf.Max(24, baseNote - 12);
            if (Input.GetKeyDown(KeyCode.X)) baseNote = Mathf.Min(84, baseNote + 12);
            for (int i = 0; i < _keys.Length; i++)
            {
                if (_desktopNotes[i] != 0 && !Input.GetKey((KeyCode)_keys[i]))
                    _DesktopKeyUp(i);
                if (_desktopNotes[i] == 0 && Input.GetKeyDown((KeyCode)_keys[i]))
                    _DesktopKeyDown(i);
            }
            bool pedal = Input.GetKey(KeyCode.Space);
            if (pedal != _sustain)
            {
                _sustain = pedal;
                if (pedal) _sustainChannel = PerformanceChannel(activeSeat.instrumentIndex);
                facade.PerformControlChange(_sustainChannel, 64, pedal ? 127 : 0);
            }
        }

        public void _DesktopKeyDown(int key)
        {
            if (key < 0 || key >= _desktopNotes.Length || _desktopNotes[key] != 0 || activeSeat == null
                || !CanPlayView(activeSeat.instrumentIndex) || !facade.CanReceiveLiveMidi()
                || (facade.practice == null && (facade.gate == null || !facade.gate.CanControl()))) return;
            int pitch = baseNote + key;
            if (pitch < 0 || pitch > 127) return;
            int note = activeSeat.instrumentIndex * 128 + pitch;
            _desktopNotes[key] = note + 1; PressNote(note);
        }

        public void _DesktopKeyUp(int key)
        {
            if (key < 0 || key >= _desktopNotes.Length || _desktopNotes[key] == 0) return;
            ReleaseNote(_desktopNotes[key] - 1); _desktopNotes[key] = 0;
        }

        private void ReadFingers(VRCPlayerApi player)
        {
            for (int f = 0; f < 10; f++)
            {
                if (!enabledFingers[f]) continue;
                Vector3 distal = player.GetBonePosition((HumanBodyBones)_distal[f]);
                Vector3 middle = player.GetBonePosition((HumanBodyBones)_middle[f]);
                Vector3 segment = distal - middle;
                if (distal == Vector3.zero || middle == Vector3.zero || segment.sqrMagnitude < 0.000001f || segment.sqrMagnitude > 0.01f)
                { SetFinger(f, 0); _hasPrevious[f] = false; continue; }
                // Humanoid bones stop at the last knuckle. Extend by half its last segment to the fingertip.
                _ReadFingerPoint(f, distal + segment * 0.5f);
            }
        }

        // World-space history allows one hand on each enabled keyboard without changing selection.
        public void _ReadFingerPoint(int finger, Vector3 worldPoint)
        {
            if (finger < 0 || finger >= 10 || !enabledFingers[finger] || instrumentFrames == null
                || !facade.CanReceiveLiveMidi() || (facade.practice == null && (facade.gate == null || !facade.gate.CanControl()))) return;
            int held = _fingerNotes[finger], candidate = 0;
            for (int view = 0; view < instrumentFrames.Length; view++)
            {
                if (!CanPlayView(view) || facade.GetStringsView(view) != null || keyMinimum == null || keyMaximum == null
                    || keyMinimum.Length < (view + 1) * 88 || keyMaximum.Length < (view + 1) * 88
                    || view >= instrumentMinimum.Length || view >= instrumentMaximum.Length) continue;
                Transform frame = instrumentFrames[view];
                Vector3 point = frame.InverseTransformPoint(worldPoint);
                Vector3 regionMin = instrumentMinimum[view], regionMax = instrumentMaximum[view];
                bool nearby = point.x >= regionMin.x - 0.002f && point.x <= regionMax.x + 0.002f
                    && point.z >= regionMin.z - 0.002f && point.z <= regionMax.z + 0.002f
                    && point.y >= regionMin.y - penetrationDepth && point.y <= regionMax.y + releaseHeight;
                if (!nearby) continue;
                int key = FindKey(view, point, held != 0 && (held - 1) / 128 == view ? (held - 1) % 128 + 1 : 0);
                if (key == 0) continue;
                int identity = view * 128 + key;
                if (identity == held) { candidate = held; break; }
                if (_hasPrevious[finger])
                {
                    float top = keyMaximum[view * 88 + key - 22].y;
                    Vector3 previous = frame.InverseTransformPoint(_previous[finger]);
                    if (previous.y > top + touchHeight && point.y <= top + touchHeight
                        && Vector3.Distance(point, previous) < 0.25f) { candidate = identity; break; }
                }
            }
            SetFinger(finger, candidate);
            _previous[finger] = worldPoint; _hasPrevious[finger] = true;
        }

        private int FindKey(int view, Vector3 point, int held)
        {
            int offset = view * 88;
            int result = 0;
            float highest = -10000f;
            for (int k = 0; k < 88; k++)
            {
                Vector3 min = keyMinimum[offset + k], max = keyMaximum[offset + k];
                if (max.x <= min.x || max.z <= min.z) continue;
                bool down = held == k + 22;
                float margin = down ? 0.002f : 0f;
                if (point.x < min.x - margin || point.x > max.x + margin || point.z < min.z - margin || point.z > max.z + margin
                    || point.y < max.y - penetrationDepth || point.y > max.y + (down ? releaseHeight : touchHeight)) continue;
                if (max.y > highest) { highest = max.y; result = k + 22; }
            }
            return result;
        }

        private void SetFinger(int finger, int note)
        {
            int previous = _fingerNotes[finger];
            if (previous == note) return;
            if (previous != 0) ReleaseNote(previous - 1);
            _fingerNotes[finger] = note;
            if (note != 0) PressNote(note - 1);
        }

        private void PressNote(int note)
        {
            if (note < 0 || note >= _noteUsers.Length || facade == null || !CanPlayView(note / 128)) return;
            _noteUsers[note]++;
            if (_noteUsers[note] == 1) facade.PerformInstrumentNoteOn(PerformanceChannel(note / 128), note % 128, velocity, note / 128);
        }

        private void ReleaseNote(int note)
        {
            if (note < 0 || note >= _noteUsers.Length || _noteUsers[note] == 0) return;
            _noteUsers[note]--;
            if (_noteUsers[note] == 0 && facade != null) facade.PerformNoteOff(PerformanceChannel(note / 128), note % 128, 0);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _desktopNotes.Length; i++)
            { if (_desktopNotes[i] != 0) ReleaseNote(_desktopNotes[i] - 1); _desktopNotes[i] = 0; }
            for (int i = 0; i < 10; i++) { SetFinger(i, 0); _hasPrevious[i] = false; }
            if (_sustain && facade != null) facade.PerformControlChange(_sustainChannel, 64, 0);
            _sustain = false;
        }

        private void OnDisable() { ReleaseAll(); if (activeSeat != null) activeSeat.ExitLocal(); }
        public override void OnAvatarChanged(VRCPlayerApi player)
        { if (Utilities.IsValid(player) && player.isLocal) ReleaseAll(); }
        public override void OnPlayerRespawn(VRCPlayerApi player)
        { if (Utilities.IsValid(player) && player.isLocal) { ReleaseAll(); if (activeSeat != null) activeSeat.ExitLocal(); } }
    }
}
