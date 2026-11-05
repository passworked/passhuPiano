using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Piano Keyboard View")]
    public class PianoKeyboardView : UdonSharpBehaviour
    {
        public MidiSequencer sequencer;
        public MidiInstrumentRouter instrumentRouter;
        public int instrumentTarget;
        public Transform[] keyTransforms;
        public Renderer[] keyRenderers;
        public bool[] keyIsBlack;
        public Material[] keyRestMaterials;
        public Vector3[] keyPressOffsets;
        public Vector3[] keyPivotPositions;
        public Vector3[] keyRotationAxes;
        public float[] keyPressAngles;
        public Material whiteMaterial;
        public Material blackMaterial;
        public Material whitePressedMaterial;
        public Material blackPressedMaterial;
        public float keyPressDepth = 0.0015f;
        public float pressDurationMs = 35f;
        public float releaseDurationMs = 90f;
        public bool pianoImpactPresentation;
        // Optional mechanical presentation, used by the music box only.
        public Transform rotatingMechanism;
        public float mechanismDegreesPerSecond = 24f;
        public bool pluckedTines;
        private Quaternion _mechanismRest;
        private int _lastLiveMs, _liveMechanismMs;
        private int[] _lastPlucks;
        [HideInInspector] public Color[] songPalette;
        [HideInInspector] public float paletteBaseHueDegrees;
        [HideInInspector] public string paletteSeedHash;
        private MidiSongData _song;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;
        private bool[] _hasHinge;
        private bool[] _pressed;
        private bool[] _materialPressed;
        private int[] _scanCounts;
        private float[] _fromPress;
        private float[] _targetPress;
        private float[] _appliedPress;
        private int[] _transitionStartMs;
        private Material[] _noteMaterials;
        private Material[] _blackPressedMaterials;
        private Color[] _channelPalette = new Color[32], _channelPressedPalette = new Color[16];
        private int[] _keyChannels = new int[88], _shownChannels = new int[88];
        private MaterialPropertyBlock _channelProperties;
        private int[] _glowStartMs = new int[88], _glowChannel = new int[88];
        private float[] _glowVelocity = new float[88];
        private bool _paletteReady;
        private string _paletteHash = "";
        private string _performancePlayerName = "";
        private string _observedSourceHash = "";
        private int _generation = -1;
        private int _lastPositionMs;
        private bool _liveSettled;
        private int _liveKeyRevision;

        public void Initialize()
        {
            if (_restPositions != null) return;
            if (rotatingMechanism != null) _mechanismRest = rotatingMechanism.localRotation;
            _restPositions = new Vector3[88];
            _restRotations = new Quaternion[88];
            _hasHinge = new bool[88];
            _pressed = new bool[88];
            _materialPressed = new bool[88];
            _scanCounts = new int[88];
            _fromPress = new float[88];
            _targetPress = new float[88];
            _appliedPress = new float[88];
            _transitionStartMs = new int[88];
            _lastPlucks = new int[88];
            _noteMaterials = new Material[88];
            _blackPressedMaterials = new Material[88];
            songPalette = new Color[2];
            _channelProperties = new MaterialPropertyBlock();
            for (int i = 0; i < 88; i++) _shownChannels[i] = -2;
            if (keyRestMaterials == null || keyRestMaterials.Length != 88)
            {
                keyRestMaterials = new Material[88];
                for (int i = 0; i < 88; i++)
                    if (keyRenderers != null && i < keyRenderers.Length && keyRenderers[i] != null)
                        keyRestMaterials[i] = keyRenderers[i].sharedMaterial;
            }
            if (keyPressOffsets == null || keyPressOffsets.Length != 88)
            {
                keyPressOffsets = new Vector3[88];
                for (int i = 0; i < 88; i++) keyPressOffsets[i] = new Vector3(0f, -keyPressDepth, 0f);
            }
            if (keyIsBlack == null || keyIsBlack.Length != 88)
            {
                keyIsBlack = new bool[88];
                for (int i = 0; i < 88; i++)
                {
                    int pc = (i + 21) % 12;
                    keyIsBlack[i] = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                }
            }
            bool hinges = keyPivotPositions != null && keyPivotPositions.Length == 88
                && keyRotationAxes != null && keyRotationAxes.Length == 88
                && keyPressAngles != null && keyPressAngles.Length == 88;
            for (int i = 0; i < 88; i++)
            {
                if (keyTransforms == null || i >= keyTransforms.Length || keyTransforms[i] == null) continue;
                _restPositions[i] = keyTransforms[i].localPosition;
                _restRotations[i] = keyTransforms[i].localRotation;
                _hasHinge[i] = hinges && keyRotationAxes[i].sqrMagnitude > 0.000001f;
                _appliedPress[i] = -1f;
            }
        }

        public void BindSequencer(MidiSequencer value)
        {
            Initialize();
            sequencer = value;
            _liveSettled = false;
        }

        public void BindSong(MidiSongData song)
        {
            Initialize();
            string hash = SongHash(song);
            _observedSourceHash = song == null ? "" : song.sourceHash;
            if (_song == song && _paletteReady && hash == _paletteHash) return;
            _song = song;
            _paletteHash = hash;
            BuildPalette();
            Clear(_generation);
        }

        public void SetPerformancePalette(string displayName)
        {
            Initialize();
            displayName = displayName ?? "";
            if (_performancePlayerName == displayName) return;
            _performancePlayerName = displayName;
            BuildPalette();
        }

        public Color FileChannelColor(int pitch, int channel)
        {
            Initialize();
            if (!_paletteReady) BuildPalette();
            int pc = pitch % 12;
            bool dark = !pluckedTines && (pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10);
            return _channelPalette[Mathf.Clamp(channel, 0, 15) * 2 + (dark ? 1 : 0)];
        }

        private void ShowKeyChannel(int key, int channel)
        {
            if (_shownChannels[key] == channel) return;
            _shownChannels[key] = channel;
            if (keyRenderers == null || key >= keyRenderers.Length || keyRenderers[key] == null) return;
            _channelProperties.Clear();
            if (channel >= 0)
            {
                // Physical black keys stay bright while falling notes keep their dark variant.
                Color color = keyIsBlack[key] && !pluckedTines ? _channelPressedPalette[channel] : _channelPalette[channel * 2];
                _channelProperties.SetColor("_Color", color);
                _channelProperties.SetColor("_BaseColor", color);
                _channelProperties.SetColor("_EmissionColor", color * (keyIsBlack[key] ? .6f : .3f));
            }
            keyRenderers[key].SetPropertyBlock(_channelProperties);
        }

        // MIDI pitch selects a cached key material. Bars/effects copy its palette color.
        public Material GetNoteMaterial(int midiPitch)
        {
            Initialize();
            if (midiPitch < 0 || midiPitch > 127) return null;
            int key = midiPitch - 21;
            // Virtual keys share the material of a physical key with the same pitch class.
            while (key < 0) key += 12;
            while (key >= 88) key -= 12;
            if (!_paletteReady)
            {
                _song = sequencer == null ? null : sequencer.song;
                _paletteHash = SongHash(_song);
                BuildPalette();
            }
            if (_noteMaterials[key] != null) return _noteMaterials[key];
            // A shorter physical keyboard still colors virtual notes by pitch class.
            // This only chooses a material; the audio/visual pitch remains unchanged.
            for (int distance = 12; distance < 88; distance += 12)
            {
                if (key - distance >= 0 && _noteMaterials[key - distance] != null) return _noteMaterials[key - distance];
                if (key + distance < 88 && _noteMaterials[key + distance] != null) return _noteMaterials[key + distance];
            }
            return null;
        }

        public void Refresh(int positionMs, int generation, bool force)
        {
            Initialize();
            if (generation < _generation) return;
            _liveSettled = false;
            MidiSongData currentSong = sequencer == null ? null : sequencer.song;
            string sourceHash = currentSong == null ? "" : currentSong.sourceHash;
            if (currentSong != _song || sourceHash != _observedSourceHash) BindSong(currentSong);
            // Pause/resume can change generation without changing the musical instant.
            // Only explicit force snaps the pose; a generation change alone does not.
            _generation = generation;
            if (force && positionMs != _lastPositionMs)
                for (int key = 0; key < 88; key++)
                { _glowVelocity[key] = 0; _glowStartMs[key] = -10000; _glowChannel[key] = -1; }
            if (force && pluckedTines) RecoverPlucks(positionMs);
            if (!force && positionMs >= _lastPositionMs) ScanPhysicalEdges(positionMs);
            for (int key = 0; key < 88; key++) _keyChannels[key] = -1;
            if (_song != null && sequencer != null)
                for (int i = 0; i < sequencer.activeNoteCount; i++)
                {
                    int n = sequencer.activeNoteIds[i], key = FilePitch(n) - 21;
                    if (key < 0 || key >= 88 || _song.keyUpMs[n] <= positionMs) continue;
                    if (instrumentRouter == null ? _song.channel[n] == 9 : instrumentRouter.TargetForNote(n) != instrumentTarget) continue;
                    // Source note IDs are start-time ordered; active slots may be swap-removed.
                    if (n > _keyChannels[key]) _keyChannels[key] = n;
                }
            for (int key = 0; key < 88; key++)
            {
                int note = _keyChannels[key];
                if (note >= 0 && (force || _song.startMs[note] > _glowStartMs[key]))
                    RecordGlow(key, _song.startMs[note], _song.velocity[note], _song.ColorChannelForNote(note));
                ShowKeyChannel(key, note < 0 ? (positionMs - _glowStartMs[key] < 1400 ? _glowChannel[key] : -1) : _song.ColorChannelForNote(note));
                int count = CurrentCount(key);
                bool down = count > 0;
                if (force)
                {
                    _pressed[key] = down;
                    _fromPress[key] = down ? 1f : 0f;
                    _targetPress[key] = _fromPress[key];
                    _transitionStartMs[key] = positionMs;
                    if (pluckedTines)
                    {
                        // Recover the last real pluck; seek/pause/late join never invent an attack.
                        _targetPress[key] = _lastPlucks[key] >= 0 ? 1f : 0f;
                        _transitionStartMs[key] = _lastPlucks[key];
                    }
                }
                else if (down != _pressed[key]) BeginTransition(key, down, positionMs);
                _scanCounts[key] = count;
                if (force) _appliedPress[key] = -1f;
                ApplyPose(key, EvaluatePress(key, positionMs));
                UpdateKeyGlow(key, positionMs, down);
            }
            _lastPositionMs = positionMs;
            RotateMechanism(positionMs);
        }

        public void RefreshLive(int ms, int generation)
        {
            Initialize();
            int revision = instrumentRouter != null ? instrumentRouter.keyCountRevision
                : sequencer == null ? 0 : sequencer.keyCountRevision;
            if (_liveSettled && _generation == generation && _liveKeyRevision == revision
                && (instrumentRouter != null || sequencer != null))
            {
                // Keep the mechanism's next input frame anchored without scanning resting keys.
                _lastLiveMs = ms;
                return;
            }
            _generation = generation;
            _liveKeyRevision = revision;
            bool moving = false, animating = false;
            for (int key = 0; key < 88; key++)
            {
                ShowKeyChannel(key, -1);
                bool down = CurrentCount(key) > 0;
                if (down != _pressed[key]) BeginTransition(key, down, ms);
                ApplyPose(key, EvaluatePress(key, ms));
                UpdateKeyGlow(key, ms, down);
                if (ms - _glowStartMs[key] < 1400) animating = true;
                if (down || (_targetPress[key] > .5f && ms - _transitionStartMs[key] < 1800)) moving = true;
                // Finish the exact existing release/pluck envelope before allowing idle sleep.
                int age = ms - _transitionStartMs[key];
                if (pluckedTines)
                {
                    if (_targetPress[key] > .5f && age <= 900) animating = true;
                }
                else if (_fromPress[key] != _targetPress[key]
                    && age < Mathf.Max(1f, _targetPress[key] > .5f ? pressDurationMs : releaseDurationMs)) animating = true;
            }
            if (moving) _liveMechanismMs += _lastLiveMs == 0 ? 0 : Mathf.Clamp(ms - _lastLiveMs, 0, 250);
            else _liveMechanismMs = 0;
            _lastLiveMs = ms;
            RotateMechanism(_liveMechanismMs);
            _liveSettled = !moving && !animating;
        }

        public void PluckLive(int midiPitch, int ms)
        {
            if (!pluckedTines || midiPitch < 21 || midiPitch > 108) return;
            Initialize(); BeginTransition(midiPitch - 21, true, ms);
        }

        public void GlowLive(int midiPitch, int velocity)
        {
            if (midiPitch < 21 || midiPitch > 108) return;
            Initialize();
            RecordGlow(midiPitch - 21, (int)(Time.realtimeSinceStartup * 1000f), velocity, -1);
            _liveSettled = false;
        }

        private void RecordGlow(int key, int ms, int velocity, int channel)
        {
            _glowStartMs[key] = ms; _glowVelocity[key] = Mathf.Clamp01(velocity / 127f); _glowChannel[key] = channel;
        }

        private void UpdateKeyGlow(int key, int ms, bool down)
        {
            if (keyRenderers == null || key >= keyRenderers.Length || keyRenderers[key] == null) return;
            bool glowing = _glowVelocity[key] > 0f && ms - _glowStartMs[key] < 1400;
            if (_materialPressed[key] != (down || glowing)) SetKeyMaterial(key, down || glowing);
            if (!glowing && !down) { ShowKeyChannel(key, -1); return; }
            keyRenderers[key].GetPropertyBlock(_channelProperties);
            _channelProperties.SetVector("_GlowTiming", new Vector4(_glowStartMs[key] * .001f, _glowVelocity[key], 0, 0));
            _channelProperties.SetFloat("_VisualTime", ms * .001f);
            Material rest = RestMaterial(key);
            _channelProperties.SetColor("_RestColor", rest != null && rest.HasProperty("_Color") ? rest.color : (keyIsBlack[key] ? Color.black : Color.white));
            keyRenderers[key].SetPropertyBlock(_channelProperties);
        }

        public void Clear(int generation)
        {
            Initialize();
            _liveSettled = false;
            _generation = generation;
            _lastPositionMs = 0;
            _lastLiveMs = 0; _liveMechanismMs = 0;
            if (rotatingMechanism != null) rotatingMechanism.localRotation = _mechanismRest;
            for (int key = 0; key < 88; key++)
            {
                _glowVelocity[key] = 0; _glowStartMs[key] = -10000; _glowChannel[key] = -1;
                ShowKeyChannel(key, -1);
                _pressed[key] = false;
                _scanCounts[key] = 0;
                _fromPress[key] = 0f;
                _targetPress[key] = 0f;
                _transitionStartMs[key] = 0;
                SetKeyMaterial(key, false);
                _appliedPress[key] = -1f;
                ApplyPose(key, 0f);
            }
        }

        private void ScanPhysicalEdges(int positionMs)
        {
            if (_song == null || _song.eventMs == null || positionMs == _lastPositionMs) return;
            int first = _song.UpperBoundEvent(_lastPositionMs);
            int end = _song.UpperBoundEvent(positionMs);
            for (int e = first; e < end; e++)
            {
                int kind = _song.eventKind[e];
                if (kind == 2) continue;
                int note = _song.eventNoteId[e];
                if (instrumentRouter == null && _song.channel[note] == 9) continue;
                if (instrumentRouter != null && instrumentRouter.TargetForNote(note) != instrumentTarget) continue;
                int key = FilePitch(note) - 21;
                if (key < 0 || key >= 88) continue;
                if (kind == 0) RecordGlow(key, _song.eventMs[e], _song.velocity[note], _song.ColorChannelForNote(note));
                bool wasDown = _scanCounts[key] > 0;
                _scanCounts[key] = Mathf.Max(0, _scanCounts[key] + (kind == 0 ? 1 : -1));
                bool down = _scanCounts[key] > 0;
                if (wasDown != down || (pluckedTines && kind == 0)) BeginTransition(key, down, _song.eventMs[e]);
            }
        }

        private void BeginTransition(int key, bool down, int timeMs)
        {
            _liveSettled = false;
            // Releasing a metal comb does not cause another mechanical pluck.
            if (pluckedTines && !down) { _pressed[key] = false; return; }
            _fromPress[key] = EvaluatePress(key, timeMs);
            _targetPress[key] = down ? 1f : 0f;
            _transitionStartMs[key] = timeMs;
            _pressed[key] = down;
        }

        private float EvaluatePress(int key, int positionMs)
        {
            if (pluckedTines)
            {
                if (_targetPress[key] < .5f) return 0f;
                float age = Mathf.Max(0, positionMs - _transitionStartMs[key]) * .001f;
                if (age > .9f) return 0f;
                return Mathf.Cos(age * (105f + key * .42f)) * Mathf.Exp(-age * 7f);
            }
            float duration = _targetPress[key] > 0.5f ? pressDurationMs : releaseDurationMs;
            float u = Mathf.Clamp01((positionMs - _transitionStartMs[key]) / Mathf.Max(1f, duration));
            u = u * u * (3f - 2f * u);
            return Mathf.Lerp(_fromPress[key], _targetPress[key], u);
        }

        private void RotateMechanism(int ms)
        {
            if (rotatingMechanism != null)
                rotatingMechanism.localRotation = _mechanismRest * Quaternion.AngleAxis((Mathf.Max(0, ms) * .001f * mechanismDegreesPerSecond) % 360f, Vector3.right);
        }

        private void RecoverPlucks(int ms)
        {
            for (int key = 0; key < 88; key++) _lastPlucks[key] = -1;
            if (_song == null) return;
            int lo = 0, hi = _song.startMs.Length, from = Mathf.Max(0, ms - 900);
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_song.startMs[mid] < from) lo = mid + 1; else hi = mid; }
            for (int note = lo; note < _song.startMs.Length && _song.startMs[note] <= ms; note++)
            {
                int key = FilePitch(note) - 21;
                if (key >= 0 && key < 88 && (instrumentRouter == null || instrumentRouter.TargetForNote(note) == instrumentTarget))
                    _lastPlucks[key] = _song.startMs[note];
            }
        }

        private void ApplyPose(int key, float press)
        {
            if (keyTransforms == null || key >= keyTransforms.Length || keyTransforms[key] == null) return;
            if (Mathf.Abs(_appliedPress[key] - press) < 0.00001f) return;
            _appliedPress[key] = press;
            Transform target = keyTransforms[key];
            if (_hasHinge[key])
            {
                Quaternion rotation = Quaternion.AngleAxis(keyPressAngles[key] * press, keyRotationAxes[key].normalized);
                Vector3 pivot = keyPivotPositions[key];
                target.localPosition = pivot + rotation * (_restPositions[key] - pivot);
                target.localRotation = rotation * _restRotations[key];
            }
            else
            {
                target.localPosition = _restPositions[key] + keyPressOffsets[key] * press;
                target.localRotation = _restRotations[key];
            }
        }

        private int CurrentCount(int key)
        {
            if (instrumentRouter != null) return instrumentRouter.keyCounts[instrumentTarget * 128 + key + 21];
            return sequencer != null && sequencer.keyDownCount != null && key < sequencer.keyDownCount.Length
                ? sequencer.keyDownCount[key] : 0;
        }

        private void SetKeyMaterial(int key, bool down)
        {
            _materialPressed[key] = down;
            if (keyRenderers == null || key >= keyRenderers.Length || keyRenderers[key] == null) return;
            Material rest = RestMaterial(key);
            Material material = down && _noteMaterials[key] != null ? _noteMaterials[key] : rest;
            if (down && keyIsBlack[key] && _blackPressedMaterials[key] != null)
                material = _blackPressedMaterials[key];
            if (material != null) keyRenderers[key].sharedMaterial = material;
        }

        private Material RestMaterial(int key)
        {
            return keyRestMaterials[key] != null ? keyRestMaterials[key] : (keyIsBlack[key] ? blackMaterial : whiteMaterial);
        }

        private string SongHash(MidiSongData song)
        {
            if (song == null || string.IsNullOrEmpty(song.sourceHash)) return "MusicMidiPlayer";
            return song.sourceHash.ToLowerInvariant();
        }

        private void BuildPalette()
        {
            _liveSettled = false;
            bool performance = !string.IsNullOrEmpty(_performancePlayerName);
            string text = performance ? _performancePlayerName : (string.IsNullOrEmpty(_paletteHash) ? "MusicMidiPlayer" : _paletteHash);
            uint seed = 2166136261u;
            for (int c = 0; c < text.Length; c++)
            {
                uint symbol = (uint)text[c];
                if (!performance && symbol >= 65u && symbol <= 70u) symbol += 32u; // Canonicalize SHA hex A–F only.
                seed = (seed ^ symbol) * 16777619u;
            }
            paletteSeedHash = text;
            paletteBaseHueDegrees = (seed & 0x00ffffffu) * (360f / 16777216f);
            // One hue per song or performer; white/black differ only in saturation and value.
            uint toneSeed = seed * 1664525u + 1013904223u;
            float saturation = 0.70f + 0.18f * ((toneSeed & 0x0000ffffu) / 65535f);
            float value = 0.90f + 0.10f * ((toneSeed >> 16) / 65535f);
            if (songPalette == null || songPalette.Length != 2) songPalette = new Color[2];
            float hue = paletteBaseHueDegrees / 360f;
            songPalette[0] = Color.HSVToRGB(hue, saturation * 0.82f, value);
            songPalette[1] = Color.HSVToRGB(hue, saturation * .94f, value * .80f);
            for (int channel = 0; channel < 16; channel++)
            {
                // Two analogous channel hues, 50.4 degrees apart, with white/black shades.
                float channelHue = Mathf.Repeat(hue + (channel % 2) * .14f, 1f);
                _channelPalette[channel * 2] = Color.HSVToRGB(channelHue, saturation * .82f, value);
                _channelPalette[channel * 2 + 1] = Color.HSVToRGB(channelHue, saturation * .94f, value * .80f);
                _channelPressedPalette[channel] = Color.HSVToRGB(channelHue, saturation, value);
            }
            for (int key = 0; key < 88; key++) _shownChannels[key] = -2;
            for (int key = 0; key < 88; key++)
            {
                // The comb shares one palette material to keep the complete new instrument
                // within its eight-material budget, including the waterfall and particles.
                if (pluckedTines && _noteMaterials[key] == null)
                    for (int prior = 0; prior < key; prior++)
                        if (_noteMaterials[prior] != null) { _noteMaterials[key] = _noteMaterials[prior]; break; }
                if (_noteMaterials[key] == null && keyRenderers != null && key < keyRenderers.Length && keyRenderers[key] != null)
                {
                    Renderer renderer = keyRenderers[key];
                    Material rest = RestMaterial(key);
                    Material template = keyIsBlack[key] ? blackPressedMaterial : whitePressedMaterial;
                    if (template == null) template = rest;
                    if (template != null)
                    {
                        renderer.sharedMaterial = template;
                        // Once per key only: use the exposed material getter to create a private
                        // cached instance. Never move this getter into a per-frame render loop.
                        _noteMaterials[key] = renderer.material;
                        renderer.sharedMaterial = rest;
                    }
                }
                Material material = _noteMaterials[key];
                if (material == null) continue;
                if (material.HasProperty("_QuietHeld")) material.SetFloat("_QuietHeld", pianoImpactPresentation ? 1f : 0f);
                Color color = songPalette[!pluckedTines && keyIsBlack[key] ? 1 : 0];
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 0.3f);
                // Keep the darker palette source for falling notes; brighten only the physical key.
                if (!keyIsBlack[key] || pluckedTines) continue;
                if (_blackPressedMaterials[key] == null && keyRenderers != null
                    && key < keyRenderers.Length && keyRenderers[key] != null)
                {
                    Renderer renderer = keyRenderers[key];
                    Material previous = renderer.sharedMaterial;
                    renderer.sharedMaterial = material;
                    _blackPressedMaterials[key] = renderer.material;
                    renderer.sharedMaterial = previous;
                }
                Material pressedMaterial = _blackPressedMaterials[key];
                if (pressedMaterial == null) continue;
                Color pressedColor = Color.HSVToRGB(hue, saturation, value);
                if (pressedMaterial.HasProperty("_Color")) pressedMaterial.SetColor("_Color", pressedColor);
                if (pressedMaterial.HasProperty("_BaseColor")) pressedMaterial.SetColor("_BaseColor", pressedColor);
                if (pressedMaterial.HasProperty("_EmissionColor")) pressedMaterial.SetColor("_EmissionColor", pressedColor * 0.6f);
            }
            _paletteReady = true;
        }
        private int FilePitch(int note)
        { return instrumentRouter == null ? _song.pitch[note] : instrumentRouter.PitchForNote(note); }
    }
}
