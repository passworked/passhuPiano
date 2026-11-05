using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/MIDI Waterfall View")]
    public class MidiWaterfallView : UdonSharpBehaviour
    {
        public PianoKeyboardView keyboard;
        public MidiInstrumentRouter instrumentRouter;
        public int instrumentTarget;
        public MidiWaterfallEffects effects;
        public Transform[] barTransforms;
        public Renderer[] barRenderers;
        public float[] keyX;
        public float[] keyWidth;
        public bool[] keyIsBlack;
        public Vector3[] keyHitPositions;
        // Legacy serialized names retained to preserve scene/prefab and Udon bindings.
        [Tooltip("Landing positions along the common rear edge of the keyboard/bar layout.")]
        public Vector3[] keyRootHitPositions;
        [Tooltip("Land at the rear edge of the keyboard/bar layout.")]
        public bool landAtKeyRoot;
        public bool rollOverDrum;
        public float drumNoteRadius = .235f;
        public float drumContactAngle = -1.02f;
        public float drumArcSweep = 2.04f;
        public Material whiteNoteMaterial;
        public Material blackNoteMaterial;
        public Material whiteHitMaterial;
        public Material blackHitMaterial;
        public Material volumeNoteMaterial;
        public float noteEmissionIntensity = 3f;
        public bool pianoImpactPresentation;
        public int windowMs = 3000;
        public float viewHeight = 6f;
        public float barGapRatio = 0.08f;
        public float minimumBarHeight = 0.03f;
        public float barDepth = 0.008f;
        public float blackBarZ = -0.012f;
        [HideInInspector] public int visibleCandidateCount;
        [HideInInspector] public int visibleRenderedCount;
        [HideInInspector] public int droppedVisualCount;

        private int[] _liveId;
        private int[] _livePitch;
        private float[] _liveStart;
        private float[] _liveEnd;
        private float[] _liveVelocity;
        private MidiSongData _song;
        private MidiSequencer _sequencer;
        private string _boundContentHash = "";
        private Material[] _barMaterials;
        private Material[] _barHitMaterials;
        private bool _barPaletteReady;
        private int[] _pitchKeys;
        private Vector3[] _pitchHitPositions;
        private int[] _slotNote;

        private int[] _noteSlot;
        private bool[] _slotHit;
        private int[] _freeSlots;
        private int _freeCount;
        private int _capacity;
        private int _generation = -1;
        private int _lastPositionMs;
        private int _worstSlot = -1;
        private int[] _activeSlots, _activeIndex, _liveSlots;
        private int _activeCount, _liveCount;
        private Material _gpuMaterial;
        private MaterialPropertyBlock _noteProperties;

        public void Initialize()
        {
            if (_barMaterials == null) _barMaterials = new Material[88];
            if (_barHitMaterials == null) _barHitMaterials = new Material[88];
            int count = barTransforms == null ? 0 : Mathf.Min(512, barTransforms.Length);
            if (_slotNote != null && _capacity == count) return;
            _capacity = count;
            if (_noteProperties == null) _noteProperties = new MaterialPropertyBlock();
            if (_gpuMaterial == null && volumeNoteMaterial != null && volumeNoteMaterial.HasProperty("_GpuAnimation"))
                for (int i = 0; i < count; i++)
                    if (barRenderers != null && i < barRenderers.Length && barRenderers[i] != null)
                    {
                        barRenderers[i].sharedMaterial = volumeNoteMaterial;
                        _gpuMaterial = barRenderers[i].material;
                        _gpuMaterial.SetFloat("_GpuAnimation", 1f);
                        _gpuMaterial.SetFloat("_PianoImpact", pianoImpactPresentation ? 1f : 0f);
                        _gpuMaterial.SetFloat("_RollOverDrum", rollOverDrum ? 1f : 0f);
                        if (rollOverDrum)
                        {
                            _gpuMaterial.SetFloat("_DrumContactAngle", drumContactAngle);
                            _gpuMaterial.SetFloat("_DrumArcSweep", drumArcSweep);
                            _gpuMaterial.SetFloat("_DrumNoteThicknessRatio", barDepth / Mathf.Max(.001f, drumNoteRadius));
                        }
                        break;
                    }
            _liveId = new int[count];
            _livePitch = new int[count];
            _liveStart = new float[count];
            _liveEnd = new float[count];
            _liveVelocity = new float[count];
            for (int i = 0; i < count; i++) _liveId[i] = -1;
            _slotNote = new int[count];
            _activeSlots = new int[count]; _activeIndex = new int[count]; _liveSlots = new int[count];

            _slotHit = new bool[count];
            _freeSlots = new int[count];
            _freeCount = count;
            MaterialPropertyBlock noteProperties = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                if (barRenderers != null && i < barRenderers.Length && barRenderers[i] != null)
                {
                    noteProperties.Clear();
                    barRenderers[i].GetPropertyBlock(noteProperties);
                    noteProperties.SetFloat("_NoteOrder", (i + 1f) / Mathf.Max(1, count));
                    barRenderers[i].SetPropertyBlock(noteProperties);
                    if (_gpuMaterial != null) barRenderers[i].sharedMaterial = _gpuMaterial;
                }
                _slotNote[i] = -1;
                _freeSlots[i] = count - 1 - i;
                if (barTransforms[i] != null) barTransforms[i].gameObject.SetActive(false);
            }
            if (keyX == null || keyX.Length != 88 || keyWidth == null || keyWidth.Length != 88
                || keyIsBlack == null || keyIsBlack.Length != 88)
            {
                keyX = new float[88];
                keyWidth = new float[88];
                keyIsBlack = new bool[88];
                int whites = 0;
                for (int key = 0; key < 88; key++)
                {
                    int pc = (key + 21) % 12;
                    bool black = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                    keyIsBlack[key] = black;
                    keyWidth[key] = black ? 0.62f : 1f;
                    keyX[key] = (black ? whites : whites + 0.5f) - 26f;
                    if (!black) whites++;
                }
            }
            BuildPitchLayout();
        }

        private void BuildPitchLayout()
        {
            _pitchKeys = new int[128];
            _pitchHitPositions = new Vector3[128];
            Vector3[] positions = landAtKeyRoot && keyRootHitPositions != null && keyRootHitPositions.Length == 88
                ? keyRootHitPositions : keyHitPositions;
            bool hasHits = positions != null && positions.Length == 88;
            for (int pitch = 0; pitch < 128; pitch++)
            {
                int key = pitch - 21;
                while (key < 0) key += 12;
                while (key >= 88) key -= 12;
                _pitchKeys[pitch] = key;
                if (rollOverDrum)
                {
                    _pitchHitPositions[pitch] = new Vector3(DrumPitchX(pitch), drumNoteRadius * Mathf.Cos(drumContactAngle), drumNoteRadius * Mathf.Sin(drumContactAngle));
                    continue;
                }
                Vector3 point = hasHits ? positions[key]
                    : new Vector3(keyX[key], 0f, keyIsBlack[key] ? blackBarZ : 0f);
                int octaves = (pitch - 21 - key) / 12;
                if (octaves != 0)
                {
                    int neighbor = octaves < 0 ? key + 12 : key - 12;
                    float neighborX = hasHits ? positions[neighbor].x : keyX[neighbor];
                    float octaveWidth = octaves < 0 ? neighborX - point.x : point.x - neighborX;
                    point.x += octaves * octaveWidth;
                }
                _pitchHitPositions[pitch] = point;
            }
        }

        public void BindSong(MidiSongData data, MidiSequencer sequencer)
        {
            Initialize();
            string hash = data == null ? "" : data.contentHash;
            bool changed = _song != data || _boundContentHash != hash;
            ClearGeometry(_generation);
            _song = data;
            _sequencer = sequencer;
            _boundContentHash = hash;
            if (keyboard != null) keyboard.BindSong(data);
            BuildBarPalette();
            int count = data == null || data.startMs == null ? 0 : data.startMs.Length;
            _noteSlot = new int[count];
            // Zero is unassigned; encoded slot+1 avoids an O(song notes) Udon fill.
            if (changed && effects != null) effects.BindSong(data);
        }

        public void RebuildAt(int positionMs, int generation)
        {
            if (generation < _generation) return;
            RebuildGeometry(positionMs, generation);
            if (effects != null) effects.RebuildAt(positionMs, generation);
        }

        // Local pause/resume changes audio generation but must retain transient particles/lights.
        public void RebuildGeometryAt(int positionMs, int generation)
        {
            if (generation < _generation) return;
            RebuildGeometry(positionMs, generation);
            if (effects != null) effects.Refresh(positionMs, generation);
        }

        private void RebuildGeometry(int positionMs, int generation)
        {
            Initialize();
            ClearGeometry(generation);
            if (_song == null || _sequencer == null || _noteSlot == null) return;
            _lastPositionMs = positionMs;
            FillCandidates(positionMs);
            UpdateGeometry(positionMs);
        }

        public void Refresh(int positionMs, int generation)
        {
            Initialize();
            if (_song == null || _sequencer == null || generation < _generation) return;
            if (_generation != generation || positionMs < _lastPositionMs)
            {
                RebuildAt(positionMs, generation);
                return;
            }
            if (_gpuMaterial != null && positionMs == _lastPositionMs)
            {
                if (effects != null) effects.Refresh(positionMs, generation);
                return;
            }
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                int slot = _activeSlots[i];
                if (!Candidate(_slotNote[slot], positionMs)) ReleaseSlot(slot);
            }
            _worstSlot = -1;
            FillCandidates(positionMs);
            UpdateGeometry(positionMs);
            _lastPositionMs = positionMs;
            if (effects != null) effects.Refresh(positionMs, generation);
        }

        public void Clear(int generation)
        {
            ClearGeometry(generation);
            if (effects != null) effects.Clear(generation);
        }

        private void ClearGeometry(int generation)
        {
            Initialize();
            _generation = generation;
            _freeCount = _capacity;
            _activeCount = 0; _liveCount = 0;
            _worstSlot = -1;
            _lastPositionMs = 0;
            for (int i = 0; i < _capacity; i++)
            {
                int note = _slotNote[i];
                if (_noteSlot != null && note >= 0 && note < _noteSlot.Length) _noteSlot[note] = 0;
                _liveId[i] = -1;
                _slotNote[i] = -1;
                _slotHit[i] = false;
                _freeSlots[i] = _capacity - 1 - i;
                if (barTransforms[i] != null) barTransforms[i].gameObject.SetActive(false);
            }
            visibleRenderedCount = 0;
            visibleCandidateCount = 0;
            droppedVisualCount = 0;
        }

        private void FillCandidates(int positionMs)
        {
            visibleCandidateCount = 0;
            int count = Mathf.Min(_sequencer.activeNoteCount, _sequencer.activeNoteIds.Length);
            for (int i = 0; i < count; i++)
            {
                int note = _sequencer.activeNoteIds[i];
                if (!Candidate(note, positionMs) || _song.startMs[note] > positionMs) continue;
                visibleCandidateCount++;
                OfferNote(note, positionMs);
            }
            int endMs = positionMs > int.MaxValue - Mathf.Max(1, windowMs) ? int.MaxValue : positionMs + Mathf.Max(1, windowMs);
            int first = UpperBoundStart(positionMs);
            int end = UpperBoundStart(endMs);
            for (int note = first; note < end; note++)
            {
                if (!Candidate(note, positionMs)) continue;
                visibleCandidateCount++;
                // Future notes are already ordered by BetterVisual (start, ID).
                // Once the pool's worst note wins, no later note can enter it.
                if (_freeCount == 0)
                {
                    if (_worstSlot < 0) FindWorst(positionMs);
                    if (_worstSlot < 0 || !BetterVisual(note, _slotNote[_worstSlot], positionMs))
                    {
                        visibleCandidateCount += end - note - 1;
                        break;
                    }
                }
                OfferNote(note, positionMs);
            }
            visibleRenderedCount = _capacity - _freeCount;
            droppedVisualCount = Mathf.Max(0, visibleCandidateCount - visibleRenderedCount);
        }

        private bool Candidate(int note, int positionMs)
        {
            if (_song == null || note < 0 || note >= _song.startMs.Length) return false;
            if (instrumentRouter == null && _song.channel[note] == 9) return false;
            if (instrumentRouter != null && instrumentRouter.TargetForNote(note) != instrumentTarget) return false;
            if (FilePitch(note) < 0 || FilePitch(note) > 127) return false;
            if (_song.keyUpMs[note] <= positionMs) return false;
            return (long)_song.startMs[note] <= (long)positionMs + Mathf.Max(1, windowMs);
        }

        private void OfferNote(int note, int positionMs)
        {
            if (_capacity == 0 || _noteSlot[note] != 0) return;
            int slot;
            if (_freeCount > 0)
            {
                _freeCount--;
                slot = _freeSlots[_freeCount];
                _activeIndex[slot] = _activeCount;
                _activeSlots[_activeCount++] = slot;
            }
            else
            {
                if (_worstSlot < 0) FindWorst(positionMs);
                slot = _worstSlot;
                if (slot < 0 || !BetterVisual(note, _slotNote[slot], positionMs)) return;
                _noteSlot[_slotNote[slot]] = 0;
            }
            _slotNote[slot] = note;
            _noteSlot[note] = slot + 1;
            _slotHit[slot] = false;
            if (barTransforms[slot] != null) barTransforms[slot].gameObject.SetActive(true);
            SetMaterial(slot, note, false);
            _worstSlot = -1;
        }

        private void ReleaseSlot(int slot)
        {
            int note = _slotNote[slot];
            if (note < 0) return;
            _noteSlot[note] = 0;
            _slotNote[slot] = -1;
            int index = _activeIndex[slot];
            int moved = _activeSlots[--_activeCount];
            _activeSlots[index] = moved;
            _activeIndex[moved] = index;
            _freeSlots[_freeCount] = slot;
            _freeCount++;
            if (barTransforms[slot] != null) barTransforms[slot].gameObject.SetActive(false);
            _worstSlot = -1;
        }

        private void FindWorst(int positionMs)
        {
            _worstSlot = -1;
            for (int a = 0; a < _activeCount; a++)
            {
                int i = _activeSlots[a];
                if (_worstSlot < 0 || BetterVisual(_slotNote[_worstSlot], _slotNote[i], positionMs)) _worstSlot = i;
            }
        }

        private bool BetterVisual(int a, int b, int positionMs)
        {
            bool aHeld = _song.startMs[a] <= positionMs;
            bool bHeld = _song.startMs[b] <= positionMs;
            if (aHeld != bHeld) return aHeld;
            if (_song.startMs[a] != _song.startMs[b]) return _song.startMs[a] < _song.startMs[b];
            return a < b;
        }

        private void UpdateGeometry(int positionMs)
        {
            if (_gpuMaterial != null) { UpdateGpuClock(positionMs * 0.001f); return; }
            float height = Mathf.Max(0.001f, viewHeight);
            float unitsPerMs = height / Mathf.Max(1, windowMs);
            for (int i = 0; i < _capacity; i++)
            {
                int note = _slotNote[i];
                Transform bar = barTransforms[i];
                if (note < 0 || bar == null) continue;
                int pitch = FilePitch(note);
                int key = _pitchKeys[pitch];
                float low = Mathf.Clamp((_song.startMs[note] - positionMs) * unitsPerMs, 0f, height);
                float high = Mathf.Clamp((_song.keyUpMs[note] - positionMs) * unitsPerMs, 0f, height);
                float length = Mathf.Min(height, Mathf.Max(high - low, Mathf.Max(0.001f, minimumBarHeight)));
                float center = Mathf.Clamp((low + high) * 0.5f, length * 0.5f, height - length * 0.5f);
                Vector3 landing = _pitchHitPositions[pitch];
                float width = Mathf.Max(0.001f, keyWidth[key] * (1f - Mathf.Clamp01(barGapRatio)));
                bar.localPosition = landing + Vector3.up * center;
                bar.localScale = new Vector3(width, length, Mathf.Max(0.001f, barDepth));
                bool hit = _song.startMs[note] <= positionMs;
                if (hit != _slotHit[i]) { _slotHit[i] = hit; SetMaterial(i, note, hit); }
            }
        }

        public void LiveNoteDown(int identity, int pitch)
        { LiveNoteDownVelocity(identity, pitch, 100); }

        public void LiveNoteDownVelocity(int identity, int pitch, int velocity)
        {
            Initialize();
            if (pitch < 0 || pitch > 127 || _capacity == 0) return;
            if (keyboard != null) keyboard.GlowLive(pitch, velocity);
            int slot = -1;
            for (int i = 0; i < _capacity; i++)
                if (_liveId[i] < 0) { slot = i; break; }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < _capacity; i++)
                    if (_liveStart[i] < _liveStart[slot]) slot = i;
            }
            if (_liveId[slot] < 0) _liveSlots[_liveCount++] = slot;
            _liveId[slot] = identity;
            _livePitch[slot] = pitch;
            _liveVelocity[slot] = Mathf.Clamp01(velocity / 127f);
            _liveStart[slot] = Time.realtimeSinceStartup;
            _liveEnd[slot] = -1f;
            if (_gpuMaterial != null)
            {
                ConfigureGpuSlot(slot, pitch, _liveStart[slot], -1f, true);
                if (barTransforms[slot] != null) barTransforms[slot].gameObject.SetActive(true);
                UpdateGpuClock(_liveStart[slot]);
                return;
            }
            if (barRenderers != null && slot < barRenderers.Length && barRenderers[slot] != null)
            {
                barRenderers[slot].SetPropertyBlock(null);
                barRenderers[slot].sharedMaterial = GetBarMaterial(pitch);
            }
            RefreshLive();
        }

        public void LiveNoteUp(int identity)
        {
            Initialize();
            for (int a = 0; a < _liveCount; a++)
            {
                int i = _liveSlots[a];
                if (_liveId[i] == identity && _liveEnd[i] < 0f)
                {
                    _liveEnd[i] = Time.realtimeSinceStartup;
                    if (_gpuMaterial != null) ConfigureGpuSlot(i, _livePitch[i], _liveStart[i], _liveEnd[i], true);
                }
            }
        }

        public void RefreshLive()
        {
            Initialize();
            float now = Time.realtimeSinceStartup;
            if (_gpuMaterial != null) UpdateGpuClock(now);
            float height = Mathf.Max(0.001f, viewHeight);
            float speed = height * 1000f / Mathf.Max(1, windowMs);
            visibleRenderedCount = 0;
            for (int a = _liveCount - 1; a >= 0; a--)
            {
                int i = _liveSlots[a];
                if (_liveId[i] < 0 || barTransforms[i] == null) continue;
                Transform bar = barTransforms[i];
                float low = _liveEnd[i] < 0f ? 0f : (now - _liveEnd[i]) * speed;
                if (low >= height)
                {
                    _liveId[i] = -1;
                    _liveSlots[a] = _liveSlots[--_liveCount];
                    bar.gameObject.SetActive(false);
                    continue;
                }
                if (_gpuMaterial != null) { visibleRenderedCount++; continue; }
                float high = Mathf.Min(height, (now - _liveStart[i]) * speed);
                float length = Mathf.Min(height - low, Mathf.Max(minimumBarHeight, high - low));
                int pitch = _livePitch[i];
                int key = _pitchKeys[pitch];
                Vector3 landing = _pitchHitPositions[pitch];
                float width = Mathf.Max(0.001f, keyWidth[key] * (1f - Mathf.Clamp01(barGapRatio)));
                bar.localPosition = landing + Vector3.up * (low + length * 0.5f);
                bar.localScale = new Vector3(width, length, Mathf.Max(0.001f, barDepth));
                bar.gameObject.SetActive(true);
                visibleRenderedCount++;
            }
            visibleCandidateCount = visibleRenderedCount;
        }

        public Vector3 GetKeyHitPosition(int key)
        {
            Initialize();
            int pitch = key + 21;
            if (pitch < 0 || pitch > 127) return Vector3.zero;
            return _pitchHitPositions[pitch];
        }

        public void SetLandingAtKeyRoot(bool enabled)
        {
            if (landAtKeyRoot == enabled) return;
            Initialize();
            landAtKeyRoot = enabled;
            BuildPitchLayout();
            if (_gpuMaterial != null)
                for (int i = 0; i < _capacity; i++) if (_liveId[i] >= 0)
                    ConfigureGpuSlot(i, _livePitch[i], _liveStart[i], _liveEnd[i], true);
            for (int i = 0; i < _capacity; i++)
                if (_liveId[i] >= 0) { RefreshLive(); return; }
            // Reposition visible notes and retire effects at the old landing line locally.
            RebuildAt(_lastPositionMs, _generation);
        }

        private void SetMaterial(int slot, int note, bool hit)
        {
            if (barRenderers == null || slot >= barRenderers.Length || barRenderers[slot] == null) return;
            if (_gpuMaterial != null)
            {
                ConfigureGpuSlot(slot, FilePitch(note), _song.startMs[note] * 0.001f, _song.keyUpMs[note] * 0.001f, false);
                return;
            }
            Color channelColor = FileNoteColor(note);
            if (hit) channelColor = Color.Lerp(channelColor, Color.white, .25f);
            _noteProperties.Clear();
            _noteProperties.SetFloat("_NoteOrder", (slot + 1f) / Mathf.Max(1, _capacity));
            _noteProperties.SetColor("_Color", channelColor);
            _noteProperties.SetColor("_BaseColor", channelColor);
            _noteProperties.SetColor("_EmissionColor", channelColor * (hit ? Mathf.Max(1f, noteEmissionIntensity * 2.5f) : Mathf.Max(0f, noteEmissionIntensity)));
            barRenderers[slot].SetPropertyBlock(_noteProperties);
            Material noteMaterial = GetBarMaterial(FilePitch(note));
            int key = _pitchKeys[FilePitch(note)];
            if (hit && _barHitMaterials[key] != null) noteMaterial = _barHitMaterials[key];
            if (noteMaterial != null)
            {
                barRenderers[slot].sharedMaterial = noteMaterial;
                return;
            }
            bool black = keyIsBlack[_pitchKeys[FilePitch(note)]];
            Material material = black
                ? (hit && blackHitMaterial != null ? blackHitMaterial : blackNoteMaterial)
                : (hit && whiteHitMaterial != null ? whiteHitMaterial : whiteNoteMaterial);
            if (material != null) barRenderers[slot].sharedMaterial = material;
        }

        public Material GetBarMaterial(int midiPitch)
        {
            Initialize();
            if (midiPitch < 0 || midiPitch > 127) return null;
            if (_gpuMaterial != null) return keyboard == null ? null : keyboard.GetNoteMaterial(midiPitch);
            int key = _pitchKeys[midiPitch];
            if (!_barPaletteReady) BuildBarPalette();
            return _barMaterials[key];
        }

        public void RefreshPalette()
        {
            Initialize();
            BuildBarPalette();
            if (_gpuMaterial != null)
                for (int i = 0; i < _capacity; i++)
                    if (_liveId[i] >= 0) ConfigureGpuSlot(i, _livePitch[i], _liveStart[i], _liveEnd[i], true);
            if (effects != null) effects.RefreshPalette();
        }

        private void BuildBarPalette()
        {
            if (_gpuMaterial != null) { _barPaletteReady = true; return; }
            for (int key = 0; key < 88; key++)
            {
                if (_barMaterials[key] == null && barRenderers != null && barRenderers.Length > 0)
                {
                    Renderer renderer = null;
                    int preferred = key % barRenderers.Length;
                    if (barRenderers[preferred] != null) renderer = barRenderers[preferred];
                    else
                        for (int i = 0; i < barRenderers.Length; i++)
                            if (barRenderers[i] != null) { renderer = barRenderers[i]; break; }
                    Material template = volumeNoteMaterial;
                    if (template == null) template = keyIsBlack[key] ? blackNoteMaterial : whiteNoteMaterial;
                    if (renderer != null && template != null)
                    {
                        Material previous = renderer.sharedMaterial;
                        renderer.sharedMaterial = template;
                        // Exactly once per pitch. This clone belongs to the waterfall, never the key.
                        _barMaterials[key] = renderer.material;
                        renderer.sharedMaterial = previous;
                    }
                }
                Material material = _barMaterials[key];
                if (material == null) continue;
                Material keyMaterial = keyboard == null ? null : keyboard.GetNoteMaterial(key + 21);
                Color color = keyMaterial == null ? Color.white : keyMaterial.color;
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * Mathf.Max(0f, noteEmissionIntensity));
                if (material.HasProperty("_BreathAmount")) material.SetFloat("_BreathAmount", 0f);
                // Cache a separate hit material so future notes of this pitch stay dim.
                if (_barHitMaterials[key] == null && barRenderers != null)
                {
                    for (int i = 0; i < barRenderers.Length; i++)
                    {
                        Renderer renderer = barRenderers[i];
                        if (renderer == null) continue;
                        Material previous = renderer.sharedMaterial;
                        renderer.sharedMaterial = material;
                        _barHitMaterials[key] = renderer.material;
                        renderer.sharedMaterial = previous;
                        break;
                    }
                }
                Material hitMaterial = _barHitMaterials[key];
                if (hitMaterial == null) continue;
                if (hitMaterial.HasProperty("_BreathAmount")) hitMaterial.SetFloat("_BreathAmount", 1f);
                Color hitColor = Color.Lerp(color, Color.white, 0.25f);
                if (hitMaterial.HasProperty("_Color")) hitMaterial.SetColor("_Color", hitColor);
                if (hitMaterial.HasProperty("_BaseColor")) hitMaterial.SetColor("_BaseColor", hitColor);
                if (hitMaterial.HasProperty("_EmissionColor")) hitMaterial.SetColor("_EmissionColor", hitColor * Mathf.Max(1f, noteEmissionIntensity * 2.5f));
            }
            _barPaletteReady = true;
        }

        private void UpdateGpuClock(float seconds)
        {
            _gpuMaterial.SetFloat("_VisualTime", seconds);
            _gpuMaterial.SetFloat("_WindowSeconds", Mathf.Max(1, windowMs) * 0.001f);
            _gpuMaterial.SetFloat("_MinimumFraction", Mathf.Clamp01(Mathf.Max(0.001f, minimumBarHeight) / Mathf.Max(0.001f, viewHeight)));
            _gpuMaterial.SetFloat("_NoteEmission", noteEmissionIntensity);
        }

        private void ConfigureGpuSlot(int slot, int pitch, float start, float end, bool live)
        {
            if (barTransforms[slot] == null || barRenderers[slot] == null) return;
            int key = _pitchKeys[pitch];
            float height = Mathf.Max(0.001f, viewHeight);
            float width = Mathf.Max(0.001f, keyWidth[key] * (1f - Mathf.Clamp01(barGapRatio)));
            // Assign once per note, never per frame. The full-height Transform
            // provides conservative CPU culling without a runtime bounds override.
            barTransforms[slot].localPosition = rollOverDrum ? new Vector3(DrumPitchX(pitch), 0, 0) : _pitchHitPositions[pitch] + Vector3.up * (height * 0.5f);
            barTransforms[slot].localScale = rollOverDrum ? new Vector3(width, drumNoteRadius, drumNoteRadius) : new Vector3(width, height, Mathf.Max(0.001f, barDepth));
            _noteProperties.Clear();
            _noteProperties.SetColor("_Color", live ? NoteColor(pitch) : FileNoteColor(_slotNote[slot]));
            float velocity = live ? _liveVelocity[slot] : Mathf.Clamp01(_song.velocity[_slotNote[slot]] / 127f);
            _noteProperties.SetVector("_NoteTiming", new Vector4(start, end, live ? 1f : 0f, velocity));
            _noteProperties.SetFloat("_NoteOrder", (slot + 1f) / Mathf.Max(1, _capacity));
            barRenderers[slot].sharedMaterial = _gpuMaterial;
            barRenderers[slot].SetPropertyBlock(_noteProperties);
        }

        private float DrumPitchX(int pitch) { return Mathf.Lerp(-1.55f, 1.55f, pitch / 127f); }

        public Color FileNoteColor(int note)
        {
            return keyboard == null ? Color.white : keyboard.FileChannelColor(FilePitch(note), _song.ColorChannelForNote(note));
        }

        public Color NoteColor(int pitch)
        {
            Material palette = keyboard == null ? null : keyboard.GetNoteMaterial(pitch);
            if (palette != null) return palette.color;
            if (rollOverDrum && keyboard != null && keyboard.songPalette != null && keyboard.songPalette.Length > 0)
                return keyboard.songPalette[0];
            return Color.white;
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
        private int FilePitch(int note)
        { return instrumentRouter == null ? _song.pitch[note] : instrumentRouter.PitchForNote(note); }
    }
}
