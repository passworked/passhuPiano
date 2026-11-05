using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Views
{
    /// <summary>Local, bounded hit effects. All lifetime and light decay follows musical time.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Waterfall Hit Effects")]
    public class MidiWaterfallEffects : UdonSharpBehaviour
    {
        public MidiWaterfallView waterfall;
        public PianoKeyboardView keyboard;
        public bool effectsEnabled = true;
        public ParticleSystem[] hitParticles;
        public ParticleSystem[] symbolParticles;
        public bool symbolLayersEnabled = true;
        public ParticleSystem[] ringParticles;
        public Light[] hitLights;
        public Transform[] glowTransforms;
        public Renderer[] glowRenderers;
        // Optional piano-only path: 128 independent pitch envelopes in one static mesh.
        public Renderer keyImpactRenderer;
        public Renderer keyImpactRootRenderer;
        public bool compactParticles;

        [Header("Bounded local budgets")]
        public int maxParticlesPerFrame = 64;
        public int totalParticleBudget = 768;
        public int totalSymbolBudget = 192;
        public int maxHitsPerFrame = 24;
        public int maxActiveLights = 0;
        public int maxCatchUpMs = 150;
        public int maxHitAgeMs = 80;
        public bool useRings;

        [Header("Light and impact glow")]
        public float maxLightIntensity = 1.1f;
        public float lightRange = 1.25f;
        public float lightDecaySeconds = 0.28f;
        public float glowDecaySeconds = 0.18f;
        public float glowMaxAlpha = 0.4f;
        public float particleLift = 0.015f;
        public float particleDepthOffset = -0.012f;
        public float glowLift = -0.0005f;
        public float glowDepthOffset = 0.065f;

        [HideInInspector] public int liveParticleCount;
        [HideInInspector] public int emittedParticleCount;
        [HideInInspector] public int liveSymbolParticleCount;
        [HideInInspector] public int emittedSymbolParticleCount;
        [HideInInspector] public int droppedParticleCount;
        [HideInInspector] public int processedHitCount;
        [HideInInspector] public int droppedHitCount;
        [HideInInspector] public int activeLightCount;

        private bool _liveMode;
        private int _liveFrame = -1;
        private int _liveFrameHits;
        private double _liveLastSeconds;
        private MidiSongData _song;
        private string _songHash = "";
        private bool _initialized;
        private bool _hasPosition;
        private bool _disabledCleared;
        private int _generation = -1;
        private int _lastPositionMs;
        private int _noteCursor;
        private int _hitPoolCount;
        private int _symbolPoolCount;
        private int _ringPoolCount;
        private int _lightCount;
        private int _glowCount;
        private int _frameParticleRemaining;
        private float[] _lightEnergy;
        private Color[] _lightColors;
        private bool[] _lightSelected;
        private float[] _glowEnergy;
        private Color[] _glowColors;
        private Vector3[] _glowRestScales;
        private Material[] _glowMaterials;
        private Material _symbolMaterial;
        private Material _impactMaterial;
        private Vector4[] _impactData, _impactColors;
        private int[] _impactPitches;
        private bool[] _impactActive;
        private int _impactCount;
        private bool _impactDirty;
        private bool _impactsShown, _impactRootShown;
        private float _impactTime, _attackTime, _nextSymbolTime;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _hitPoolCount = hitParticles == null ? 0 : Mathf.Min(12, hitParticles.Length);
            _symbolPoolCount = symbolParticles == null ? 0 : Mathf.Min(12, symbolParticles.Length);
            _ringPoolCount = ringParticles == null ? 0 : Mathf.Min(12, ringParticles.Length);
            _lightCount = hitLights == null ? 0 : Mathf.Min(8, hitLights.Length);
            _glowCount = glowTransforms == null || glowRenderers == null ? 0 : Mathf.Min(12, Mathf.Min(glowTransforms.Length, glowRenderers.Length));
            _lightEnergy = new float[_lightCount];
            _lightColors = new Color[_lightCount];
            _lightSelected = new bool[_lightCount];
            _glowEnergy = new float[_glowCount];
            _glowColors = new Color[_glowCount];
            _glowRestScales = new Vector3[_glowCount];
            _glowMaterials = new Material[_glowCount];
            if (keyImpactRenderer != null)
            {
                _impactMaterial = keyImpactRenderer.material;
                if (keyImpactRootRenderer != null) keyImpactRootRenderer.sharedMaterial = _impactMaterial;
                _impactData = new Vector4[128];
                _impactColors = new Vector4[128];
                _impactPitches = new int[128];
                _impactActive = new bool[128];
                _impactMaterial.SetVectorArray("_ImpactData", _impactData);
                keyImpactRenderer.enabled = false;
                if (keyImpactRootRenderer != null) keyImpactRootRenderer.enabled = false;
                _impactsShown = false; _impactRootShown = false;
            }
            for (int i = 0; i < _hitPoolCount; i++) ResetParticlePool(hitParticles[i], compactParticles ? 160 : 64);
            for (int i = 0; i < _symbolPoolCount; i++)
            {
                ResetParticlePool(symbolParticles[i], compactParticles ? 32 : 16);
                if (symbolParticles[i] == null) continue;
                Renderer renderer = symbolParticles[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;
                // One private material per player; all zones share the current song's theme.
                if (_symbolMaterial == null) _symbolMaterial = renderer.material;
                else renderer.sharedMaterial = _symbolMaterial;
            }
            for (int i = 0; i < _ringPoolCount; i++) ResetParticlePool(ringParticles[i], 64);
            for (int i = 0; i < _lightCount; i++)
            {
                Light light = hitLights[i];
                if (light == null) continue;
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
                light.range = Mathf.Clamp(lightRange, 0.1f, 2f);
                light.intensity = 0f;
                light.enabled = false;
            }
            for (int i = 0; i < _glowCount; i++)
            {
                if (glowTransforms[i] != null) _glowRestScales[i] = glowTransforms[i].localScale;
                Renderer renderer = glowRenderers[i];
                if (renderer == null) continue;
                // Once per configured glow only. Never instantiate a material in Refresh or per note.
                _glowMaterials[i] = renderer.material;
                renderer.enabled = false;
            }
        }

        public void BindSong(MidiSongData song)
        {
            Initialize();
            string hash = song == null ? "" : song.contentHash;
            if (_song == song && _songHash == hash) return;
            Clear(_generation);
            _song = song;
            RefreshPalette();
            _songHash = hash;
        }

        public void RefreshPalette()
        {
            Initialize();
            if (_impactMaterial != null && waterfall != null)
            {
                _impactMaterial.SetColor("_LightColor", waterfall.NoteColor(21));
                _impactMaterial.SetColor("_DarkColor", waterfall.NoteColor(22));
            }
            if (_symbolMaterial != null && keyboard != null)
            {
                Material theme = keyboard.GetNoteMaterial(21);
                if (theme != null) _symbolMaterial.SetColor("_Color", Color.white);
            }
        }

        public void RebuildAt(int positionMs, int generation)
        {
            Initialize();
            if (generation < _generation) return;
            Clear(generation);
            _lastPositionMs = positionMs;
            _noteCursor = UpperBoundStart(_lastPositionMs);
            _hasPosition = true;
            _impactTime = positionMs * 0.001f;
            RecoverHeldImpacts();
            UpdateImpacts();
            // A seek, correction or late join never emits old attacks or reconstructs old light pulses.
        }

        public void Refresh(int positionMs, int generation)
        {
            Initialize();
            if (generation < _generation || _song == null) return;
            if (_liveMode) { RebuildAt(positionMs, generation); return; }
            _generation = generation;
            if (positionMs < 0)
            {
                _lastPositionMs = positionMs;
                _noteCursor = 0;
                _hasPosition = true;
                if (!compactParticles) PauseParticles();
                return;
            }
            if (!effectsEnabled)
            {
                if (!_disabledCleared) Clear(generation);
                _disabledCleared = true;
                _lastPositionMs = positionMs;
                _noteCursor = UpperBoundStart(positionMs);
                _hasPosition = true;
                return;
            }
            if (_disabledCleared)
            {
                _disabledCleared = false;
                RebuildAt(positionMs, generation);
                return;
            }
            if (!_hasPosition || positionMs < _lastPositionMs || positionMs - _lastPositionMs > Mathf.Max(1, maxCatchUpMs))
            {
                RebuildAt(positionMs, generation);
                return;
            }

            int elapsedMs = positionMs - _lastPositionMs;
            if (elapsedMs == 0)
            {
                // Pause is explicitly a pause, not a Clear. No wall-clock lifetime or light decay runs.
                PauseParticles();
                return;
            }
            float elapsed = elapsedMs * 0.001f;
            _impactTime = positionMs * 0.001f;
            AdvanceParticles(elapsed);
            DecayEnergy(elapsed);
            liveParticleCount = CountParticles();
            _frameParticleRemaining = Mathf.Clamp(maxParticlesPerFrame, 0, 128);

            int end = UpperBoundStart(positionMs);
            int first = Mathf.Max(_noteCursor, end - Mathf.Clamp(maxHitsPerFrame, 0, compactParticles ? 128 : 64));
            droppedHitCount += first - _noteCursor;
            for (int note = first; note < end; note++)
            {
                if (positionMs - _song.startMs[note] > Mathf.Max(0, maxHitAgeMs))
                {
                    droppedHitCount++;
                    continue;
                }
                Hit(note);
            }
            // Even budget-rejected attacks are consumed: they must not burst out on a later frame.
            _noteCursor = end;
            _lastPositionMs = positionMs;
            UpdateLights();
            UpdateGlows();
            UpdateImpacts();
        }

        public void Clear(int generation)
        {
            Initialize();
            if (generation < _generation) return;
            _generation = generation;
            _liveMode = false;
            _liveFrame = -1;
            _hasPosition = false;
            _lastPositionMs = 0;
            _noteCursor = 0;
            _nextSymbolTime = -1f;
            if (_impactData != null)
            {
                for (int i = 0; i < 128; i++) { _impactData[i] = Vector4.zero; _impactActive[i] = false; }
                _impactCount = 0;
                _impactMaterial.SetVectorArray("_ImpactData", _impactData);
                keyImpactRenderer.enabled = false;
                if (keyImpactRootRenderer != null) keyImpactRootRenderer.enabled = false;
                _impactsShown = false; _impactRootShown = false;
                _impactDirty = false;
            }
            for (int i = 0; i < _hitPoolCount; i++) ResetParticlePool(hitParticles[i], compactParticles ? 160 : 64);
            for (int i = 0; i < _symbolPoolCount; i++)
            {
                ResetParticlePool(symbolParticles[i], compactParticles ? 32 : 16);
                if (symbolParticles[i] == null) continue;
                Renderer renderer = symbolParticles[i].GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;
                // One private material per player; all zones share the current song's theme.
                if (_symbolMaterial == null) _symbolMaterial = renderer.material;
                else renderer.sharedMaterial = _symbolMaterial;
            }
            for (int i = 0; i < _ringPoolCount; i++) ResetParticlePool(ringParticles[i], 64);
            for (int i = 0; i < _lightCount; i++)
            {
                _lightEnergy[i] = 0f;
                _lightColors[i] = Color.black;
                if (hitLights[i] == null) continue;
                hitLights[i].intensity = 0f;
                hitLights[i].enabled = false;
            }
            for (int i = 0; i < _glowCount; i++)
            {
                _glowEnergy[i] = 0f;
                _glowColors[i] = Color.black;
                if (glowRenderers[i] != null) glowRenderers[i].enabled = false;
                if (glowTransforms[i] != null) glowTransforms[i].localScale = _glowRestScales[i];
            }
            liveParticleCount = 0;
            emittedParticleCount = 0;
            liveSymbolParticleCount = 0;
            emittedSymbolParticleCount = 0;
            droppedParticleCount = 0;
            processedHitCount = 0;
            droppedHitCount = 0;
            activeLightCount = 0;
        }

        // Live MIDI follows local wall time even though the song transport remains stopped.
        // Budgets reset once per rendered frame, including when several network batches arrive.
        public void RefreshLive(int generation)
        {
            Initialize();
            if (generation < _generation) return;
            if (!effectsEnabled)
            {
                if (!_disabledCleared) Clear(generation);
                _disabledCleared = true;
                return;
            }
            _disabledCleared = false;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_liveMode || generation != _generation)
            {
                Clear(generation);
                _liveMode = true;
                _liveLastSeconds = now;
                _impactTime = (float)now;
                RecoverHeldImpacts();
            }
            if (_liveFrame == Time.frameCount) return;
            _liveFrame = Time.frameCount;
            float elapsed = (float)System.Math.Max(0.0, now - _liveLastSeconds);
            _liveLastSeconds = now;
            _impactTime = (float)now;
            if (elapsed > 0f)
            {
                AdvanceParticles(elapsed);
                DecayEnergy(elapsed);
            }
            liveParticleCount = CountParticles();
            _frameParticleRemaining = Mathf.Clamp(maxParticlesPerFrame, 0, 128);
            _liveFrameHits = 0;
            UpdateLights();
            UpdateGlows();
            UpdateImpacts();
        }

        public void LiveHit(int pitch, int velocity, int generation)
        {
            if (pitch < 0 || pitch > 127 || velocity <= 0 || velocity > 127 || generation < _generation) return;
            RefreshLive(generation);
            if (!effectsEnabled) return;
            if (_liveFrameHits >= Mathf.Clamp(maxHitsPerFrame, 0, compactParticles ? 128 : 64))
            {
                droppedHitCount++;
                return;
            }
            _liveFrameHits++;
            _attackTime = _impactTime;
            HitPitch(pitch, velocity, waterfall == null ? Color.white : waterfall.NoteColor(pitch));
            UpdateLights();
            UpdateGlows();
            UpdateImpacts();
        }

        private void Hit(int note)
        {
            if (waterfall.instrumentRouter == null && _song.channel[note] == 9) return;
            if (waterfall.instrumentRouter != null && waterfall.instrumentRouter.TargetForNote(note) != waterfall.instrumentTarget) return;
            _attackTime = _song.startMs[note] * 0.001f;
            HitPitch(FilePitch(note), _song.velocity[note], waterfall.FileNoteColor(note));
        }

        private void HitPitch(int pitch, int midiVelocity, Color color)
        {
            int key = pitch - 21;
            if (pitch < 0 || pitch > 127) return;
            float velocity = Mathf.Clamp01(midiVelocity / 127f);
            float strength = Mathf.Pow(velocity, 1.35f);
            RecordImpact(pitch, strength, _attackTime, color);
            color.a = 1f;
            Vector3 point = HitPoint(key);
            // Particle pools stay bounded; their emission point follows the virtual key.
            key = Mathf.Clamp(key, 0, 87);
            Vector3 up = waterfall == null ? Vector3.up : waterfall.transform.up;
            Vector3 forward = waterfall == null ? Vector3.forward : waterfall.transform.forward;
            Vector3 particlePoint = point + up * particleLift + forward * particleDepthOffset;
            int count = Mathf.Clamp(Mathf.RoundToInt(2f + velocity * 5f), 1, 8);
            if (compactParticles)
            {
                // The precise impact stays visible even when decoration is throttled.
                float density = Mathf.Clamp01(liveParticleCount / 192f);
                count = Mathf.Max(1, Mathf.RoundToInt((1f + strength * 3f) * (1f - density * 0.75f)));
            }
            // Cadence follows hits actually rendered locally, so skipped attacks cannot starve this layer.
            // The first visible hit and each third one emit a symbol; rare strong accents get two.
            // Emit it first so a dense dust burst cannot monopolize the shared frame allowance.
            if (symbolLayersEnabled && _symbolPoolCount > 0 && (compactParticles ? _impactTime >= _nextSymbolTime : processedHitCount % 3 == 0))
            {
                int desired = !compactParticles && velocity >= 0.85f && processedHitCount % 6 == 0 ? 2 : 1;
                if (compactParticles) _nextSymbolTime = _impactTime + 0.14f;
                int available = Mathf.Max(0, Mathf.Clamp(totalSymbolBudget, 0, 192) - liveSymbolParticleCount);
                int requested = Mathf.Min(desired, available);
                droppedParticleCount += desired - requested;
                int emitted = Emit(symbolParticles[Mathf.Min(_symbolPoolCount - 1, key * _symbolPoolCount / 88)], particlePoint, color, requested);
                liveSymbolParticleCount += emitted;
                emittedSymbolParticleCount += emitted;
            }
            if (_hitPoolCount > 0)
                Emit(hitParticles[Mathf.Min(_hitPoolCount - 1, key * _hitPoolCount / 88)], particlePoint, color, count);
            if (useRings && _ringPoolCount > 0 && velocity >= 0.6f)
                Emit(ringParticles[Mathf.Min(_ringPoolCount - 1, key * _ringPoolCount / 88)], point, color, 1);

            if (_lightCount > 0)
            {
                int zone = Mathf.Min(_lightCount - 1, key * _lightCount / 88);
                float added = strength * 0.72f;
                float total = _lightEnergy[zone] + added;
                _lightColors[zone] = Color.Lerp(_lightColors[zone], color, added / Mathf.Max(0.0001f, total));
                _lightEnergy[zone] = Mathf.Min(1.5f, total);
            }
            if (_glowCount > 0)
            {
                int zone = Mathf.Min(_glowCount - 1, key * _glowCount / 88);
                float added = strength * 0.9f;
                float total = _glowEnergy[zone] + added;
                float weight = added / Mathf.Max(0.0001f, total);
                _glowColors[zone] = Color.Lerp(_glowColors[zone], color, weight);
                _glowEnergy[zone] = Mathf.Min(1.5f, total);
                Transform glow = glowTransforms[zone];
                if (glow != null)
                {
                    Vector3 target = point + up * glowLift + forward * glowDepthOffset;
                    glow.position = _glowEnergy[zone] <= added + 0.0001f ? target : Vector3.Lerp(glow.position, target, weight);
                }
            }
            processedHitCount++;
        }

        private Vector3 HitPoint(int key)
        {
            if (waterfall != null && waterfall.keyX != null && waterfall.keyX.Length == 88)
            {
                return waterfall.transform.TransformPoint(waterfall.GetKeyHitPosition(key));
            }
            if (keyboard != null && keyboard.keyTransforms != null && key >= 0 && key < keyboard.keyTransforms.Length && keyboard.keyTransforms[key] != null)
                return keyboard.keyTransforms[key].position;
            return transform.position;
        }

        private int Emit(ParticleSystem system, Vector3 point, Color color, int requested)
        {
            if (system == null || requested <= 0) return 0;
            ParticleSystem.MainModule main = system.main;
            int before = system.particleCount;
            int remaining = Mathf.Min(_frameParticleRemaining, Mathf.Clamp(totalParticleBudget, 0, 2048) - liveParticleCount);
            remaining = Mathf.Min(remaining, Mathf.Max(0, main.maxParticles - before));
            int count = Mathf.Max(0, Mathf.Min(requested, remaining));
            if (count == 0) { droppedParticleCount += requested; return 0; }
            system.transform.position = point;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
            system.Emit(count);
            int emitted = Mathf.Max(0, system.particleCount - before);
            // All systems remain paused between manual music-time simulation steps.
            system.Pause(false);
            _frameParticleRemaining -= count;
            liveParticleCount += emitted;
            emittedParticleCount += emitted;
            droppedParticleCount += requested - emitted;
            return emitted;
        }

        private void ResetParticlePool(ParticleSystem system, int limit)
        {
            if (system == null) return;
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Clamp(limit, 1, 192);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Simulate(0f, false, true, false);
            system.Pause(false);
        }

        private void PauseParticles()
        {
            for (int i = 0; i < _hitPoolCount; i++) if (hitParticles[i] != null) hitParticles[i].Pause(false);
            for (int i = 0; i < _symbolPoolCount; i++) if (symbolParticles[i] != null) symbolParticles[i].Pause(false);
            for (int i = 0; i < _ringPoolCount; i++) if (ringParticles[i] != null) ringParticles[i].Pause(false);
        }

        private void AdvanceParticles(float seconds)
        {
            for (int i = 0; i < _hitPoolCount; i++)
            {
                ParticleSystem system = hitParticles[i];
                if (system == null || system.particleCount == 0) continue;
                system.Simulate(seconds, false, false, false);
                system.Pause(false);
            }
            for (int i = 0; i < _ringPoolCount; i++)
            {
                ParticleSystem system = ringParticles[i];
                if (system == null || system.particleCount == 0) continue;
                system.Simulate(seconds, false, false, false);
                system.Pause(false);
            }
            for (int i = 0; i < _symbolPoolCount; i++)
            {
                ParticleSystem system = symbolParticles[i];
                if (system == null || system.particleCount == 0) continue;
                system.Simulate(seconds, false, false, false);
                system.Pause(false);
            }
        }

        private int CountParticles()
        {
            int count = 0;
            for (int i = 0; i < _hitPoolCount; i++) if (hitParticles[i] != null) count += hitParticles[i].particleCount;
            for (int i = 0; i < _ringPoolCount; i++) if (ringParticles[i] != null) count += ringParticles[i].particleCount;
            liveSymbolParticleCount = 0;
            for (int i = 0; i < _symbolPoolCount; i++) if (symbolParticles[i] != null) liveSymbolParticleCount += symbolParticles[i].particleCount;
            count += liveSymbolParticleCount;
            return count;
        }

        private void DecayEnergy(float seconds)
        {
            if (_lightCount == 0 && _glowCount == 0) return;
            float lightDecay = Mathf.Exp(-seconds / Mathf.Max(0.02f, lightDecaySeconds));
            float glowDecay = Mathf.Exp(-seconds / Mathf.Max(0.02f, glowDecaySeconds));
            for (int i = 0; i < _lightCount; i++) _lightEnergy[i] *= lightDecay;
            for (int i = 0; i < _glowCount; i++) _glowEnergy[i] *= glowDecay;
        }

        private void UpdateLights()
        {
            for (int i = 0; i < _lightCount; i++) _lightSelected[i] = false;
            int limit = Mathf.Clamp(maxActiveLights, 0, Mathf.Min(4, _lightCount));
            for (int selected = 0; selected < limit; selected++)
            {
                int best = -1;
                for (int i = 0; i < _lightCount; i++)
                    if (!_lightSelected[i] && hitLights[i] != null && _lightEnergy[i] >= 0.015f && (best < 0 || _lightEnergy[i] > _lightEnergy[best])) best = i;
                if (best < 0) break;
                _lightSelected[best] = true;
            }
            activeLightCount = 0;
            for (int i = 0; i < _lightCount; i++)
            {
                Light light = hitLights[i];
                if (light == null) continue;
                bool enabled = _lightSelected[i];
                light.intensity = enabled ? Mathf.Clamp01(_lightEnergy[i]) * Mathf.Clamp(maxLightIntensity, 0f, 2f) : 0f;
                light.color = _lightColors[i];
                light.enabled = enabled;
                if (enabled) activeLightCount++;
            }
        }

        private void UpdateGlows()
        {
            for (int i = 0; i < _glowCount; i++)
            {
                Renderer renderer = glowRenderers[i];
                Material material = _glowMaterials[i];
                if (renderer == null || material == null) continue;
                float strength = Mathf.Clamp01(_glowEnergy[i]);
                renderer.enabled = strength >= 0.02f;
                Color color = _glowColors[i];
                color.a = strength * Mathf.Clamp01(glowMaxAlpha);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", _glowColors[i] * strength * 0.65f);
                if (glowTransforms[i] != null) glowTransforms[i].localScale = _glowRestScales[i] * (0.8f + strength * 0.4f);
            }
        }

        private int PhysicalCount(int pitch)
        {
            if (waterfall != null && waterfall.instrumentRouter != null)
                return waterfall.instrumentRouter.keyCounts[waterfall.instrumentTarget * 128 + pitch];
            int key = pitch - 21;
            if (keyboard != null && keyboard.sequencer != null && key >= 0 && key < 88)
                return keyboard.sequencer.keyDownCount[key];
            return 0;
        }

        private void RecordImpact(int pitch, float strength, float attack, Color color)
        {
            if (_impactData == null) return;
            if (!_impactActive[pitch])
            {
                _impactActive[pitch] = true;
                _impactPitches[_impactCount++] = pitch;
            }
            _impactColors[pitch] = color;
            _impactData[pitch] = new Vector4(attack, Mathf.Max(0.06f, strength), -1f, 0f);
            _impactDirty = true;
        }

        private void RecoverHeldImpacts()
        {
            if (_impactData == null) return;
            // Restore only the quiet held layer, never replay an attack on seek/late join.
            for (int pitch = 0; pitch < 128; pitch++)
                if (PhysicalCount(pitch) > 0)
                {
                    Color color = waterfall == null ? Color.white : waterfall.NoteColor(pitch);
                    int latest = -1;
                    if (!_liveMode && _song != null && keyboard != null && keyboard.sequencer != null)
                        for (int i = 0; i < keyboard.sequencer.activeNoteCount; i++)
                        {
                            int n = keyboard.sequencer.activeNoteIds[i];
                            if (n <= latest || FilePitch(n) != pitch || _song.keyUpMs[n] <= _impactTime * 1000f) continue;
                            if (waterfall.instrumentRouter == null ? _song.channel[n] == 9 : waterfall.instrumentRouter.TargetForNote(n) != waterfall.instrumentTarget) continue;
                            latest = n; color = waterfall.FileNoteColor(n);
                        }
                    RecordImpact(pitch, .45f, _impactTime - 10f, color);
                }
        }

        private void UpdateImpacts()
        {
            if (_impactMaterial == null) return;
            // Only live pitches are inspected; no per-key Transform/material animation.
            for (int i = _impactCount - 1; i >= 0; i--)
            {
                int pitch = _impactPitches[i];
                Vector4 data = _impactData[pitch];
                if (data.z < 0f && _impactTime > data.x + 0.0001f && PhysicalCount(pitch) == 0)
                {
                    data.z = _impactTime;
                    _impactData[pitch] = data;
                    _impactDirty = true;
                }
                if (data.z >= 0f && _impactTime - Mathf.Max(data.x, data.z) > 1.4f)
                {
                    _impactActive[pitch] = false;
                    _impactPitches[i] = _impactPitches[--_impactCount];
                }
            }
            if (_impactDirty)
            {
                _impactMaterial.SetVectorArray("_ImpactData", _impactData);
                _impactMaterial.SetVectorArray("_ImpactColors", _impactColors);
                _impactDirty = false;
            }
            if (_impactCount > 0) _impactMaterial.SetFloat("_VisualTime", _impactTime);
            bool show = effectsEnabled && _impactCount > 0;
            bool root = waterfall != null && waterfall.landAtKeyRoot && keyImpactRootRenderer != null;
            bool frontShown = show && !root, rearShown = show && root;
            if (_impactsShown != frontShown) { keyImpactRenderer.enabled = frontShown; _impactsShown = frontShown; }
            if (keyImpactRootRenderer != null && _impactRootShown != rearShown)
            { keyImpactRootRenderer.enabled = rearShown; _impactRootShown = rearShown; }
        }

        private int UpperBoundStart(int timeMs)
        {
            if (_song == null || _song.startMs == null) return 0;
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
        { return waterfall.instrumentRouter == null ? _song.pitch[note] : waterfall.instrumentRouter.PitchForNote(note); }
    }
}
