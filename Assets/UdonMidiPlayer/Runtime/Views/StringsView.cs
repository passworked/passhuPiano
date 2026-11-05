using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StringsView : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public StringsVoicePool voices;
        public Transform[] bodies = new Transform[0], bows = new Transform[0], contacts = new Transform[0];
        public Renderer[] strings = new Renderer[0];
        public ParticleSystem[] stringParticles = new ParticleSystem[0];
        public ParticleSystem[] symbolParticles = new ParticleSystem[0];
        public int[] openNotes = new int[0];
        public float[] stringLengths = new float[0], nutPositions = new float[0];
        public int routedTarget = -1;
        [HideInInspector] public int activeStringCount, liveParticleCount, liveSymbolParticleCount;
        [HideInInspector] public Color[] songPalette;
        [HideInInspector] public string paletteSeedHash;
        private Color[] _channelPalette = new Color[32];
        private int[] _sparkChannel, _poseChannel;
        private Material[] _materials;
        private Material[] _contactMaterials;
        private Material _symbolMaterial;
        private Vector3[] _bowRest;
        private Quaternion[] _bowRotation;
        private float[] _energy, _glow;
        private int[] _sparkPitch, _posePitch, _poseVelocity, _poseStyle;
        private int[] _hitCount;
        private double[] _onset, _emittedOnset, _nextSpark, _poseAge;
        private bool _ready, _wasMusic;
        private MidiSongData _song;
        private string _performancePlayerName = "";
        private double _lastTime = -1;
        private int _generation = -1;

        private void Initialize()
        {
            if (_ready) return;
            int count = strings.Length;
            _materials = new Material[count];
            songPalette = new Color[2]; _hitCount = new int[count];
            _contactMaterials = new Material[contacts.Length];
            _energy = new float[count]; _glow = new float[count]; _sparkPitch = new int[count];
            _sparkChannel = new int[count]; _poseChannel = new int[bodies.Length];
            _onset = new double[count]; _emittedOnset = new double[count]; _nextSpark = new double[count];
            _posePitch = new int[bodies.Length]; _poseVelocity = new int[bodies.Length];
            _poseStyle = new int[bodies.Length]; _poseAge = new double[bodies.Length];
            _bowRest = new Vector3[bows.Length]; _bowRotation = new Quaternion[bows.Length];
            for (int i = 0; i < count; i++)
            {
                _materials[i] = strings[i].material;
                _emittedOnset[i] = -1e20;
                if (i < stringParticles.Length && stringParticles[i] != null)
                {
                    ResetParticles(stringParticles[i]);
                }
                if (i < symbolParticles.Length && symbolParticles[i] != null)
                {
                    var renderer = symbolParticles[i].GetComponent<ParticleSystemRenderer>();
                    if (_symbolMaterial == null) _symbolMaterial = renderer.material;
                    renderer.sharedMaterial = _symbolMaterial;
                    ResetParticles(symbolParticles[i]);
                }
            }
            for (int i = 0; i < bows.Length; i++)
            {
                _bowRest[i] = bows[i].localPosition; _bowRotation[i] = bows[i].localRotation;
                var renderer = contacts[i].GetComponent<Renderer>();
                _contactMaterials[i] = renderer.material;
            }
            _ready = true;
        }

        public void BindSong(MidiSongData song)
        {
            Initialize();
            string text = PaletteSeed(song);
            if (_song == song && paletteSeedHash == text) return;
            Clear(); _song = song; paletteSeedHash = text;
            ApplyPalette(text);
        }

        public void SetPerformancePalette(string displayName)
        {
            Initialize();
            displayName = displayName ?? "";
            if (_performancePlayerName == displayName) return;
            _performancePlayerName = displayName;
            paletteSeedHash = PaletteSeed(_song);
            ApplyPalette(paletteSeedHash);
        }

        private string PaletteSeed(MidiSongData song)
        {
            if (!string.IsNullOrEmpty(_performancePlayerName)) return _performancePlayerName;
            return song == null || string.IsNullOrEmpty(song.sourceHash) ? "MusicMidiPlayer" : song.sourceHash.ToLowerInvariant();
        }

        private void ApplyPalette(string text)
        {
            uint seed = 2166136261u;
            for (int i = 0; i < text.Length; i++) seed = (seed ^ (uint)text[i]) * 16777619u;
            uint tone = seed * 1664525u + 1013904223u;
            // Match PianoKeyboardView's source-hash palette, including its hue rounding.
            float hue = ((seed & 0x00ffffffu) * (360f / 16777216f)) / 360f;
            float saturation = .70f + .18f * ((tone & 65535u) / 65535f), value = .90f + .10f * ((tone >> 16) / 65535f);
            songPalette[0] = Color.HSVToRGB(hue, saturation * .82f, value);
            songPalette[1] = Color.HSVToRGB(hue, saturation * .94f, value * .80f);
            for (int channel = 0; channel < 16; channel++)
            {
                // Match the keyboard's two channel hues and white/black shades.
                float channelHue = Mathf.Repeat(hue + (channel % 2) * .14f, 1f);
                _channelPalette[channel * 2] = Color.HSVToRGB(channelHue, saturation * .82f, value);
                _channelPalette[channel * 2 + 1] = Color.HSVToRGB(channelHue, saturation * .94f, value * .80f);
            }
            foreach (Material material in _materials) material.SetColor("_GlowColor", songPalette[0]);
            foreach (Material material in _contactMaterials) material.SetColor("_Color", songPalette[0]);
            // The shared symbol shader takes RGB from its private material, like the piano.
            if (_symbolMaterial != null) _symbolMaterial.SetColor("_Color", Color.white);
        }

        public Color FileChannelColor(int pitch, int channel)
        { Initialize(); return PitchColor(pitch, channel); }

        private Color PitchColor(int pitch, int channel)
        {
            int key = pitch % 12;
            int shade = key == 1 || key == 3 || key == 6 || key == 8 || key == 10 ? 1 : 0;
            return channel < 0 ? songPalette[shade] : _channelPalette[Mathf.Clamp(channel, 0, 15) * 2 + shade];
        }

        private int NoteSection(int channel, int track)
        {
            if (bodies.Length == 1) return 0;
            if (track >= 0 && track < voices.trackSections.Length && voices.trackSections[track] >= 0)
                return Mathf.Clamp(voices.trackSections[track], 0, bodies.Length - 1);
            return channel >= 0 && channel < voices.channelSections.Length ? Mathf.Clamp(voices.channelSections[channel], 0, bodies.Length - 1) : 0;
        }

        public bool HasPhysicalPitch(int section, int pitch)
        { return pitch >= openNotes[section * 4] && pitch <= openNotes[section * 4 + 3] + 24; }

        private int PitchString(int section, int pitch)
        {
            int best = 0;
            for (int s = 1; s < 4; s++) if (pitch >= openNotes[section * 4 + s]) best = s;
            return best;
        }

        private Vector3 StringPoint(int section, int pitch)
        {
            int s = PitchString(section, pitch), semitones = pitch - openNotes[section * 4 + s];
            float y = semitones == 0 ? nutPositions[section] - stringLengths[section]
                : nutPositions[section] - stringLengths[section] * (1f - Mathf.Pow(2, -semitones / 12f));
            float t = Mathf.Clamp01((y + .185f) / .535f);
            return new Vector3((s - 1.5f) * .009f * Mathf.Lerp(1, .55f, t), y, Mathf.Lerp(.05f, .065f, t) + .029f * Mathf.Sin(t * Mathf.PI));
        }

        private void ResetParticles(ParticleSystem system)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.loop = true;
            system.Simulate(0f, false, true, false); system.Pause(false);
        }

        public void Clear()
        {
            Initialize();
            for (int i = 0; i < bows.Length; i++)
            { bows[i].localPosition = _bowRest[i]; bows[i].localRotation = _bowRotation[i]; contacts[i].gameObject.SetActive(false); }
            for (int i = 0; i < strings.Length; i++)
            {
                _glow[i] = 0; _emittedOnset[i] = -1e20;
                _nextSpark[i] = -1e20; _hitCount[i] = 0;
                _materials[i].SetFloat("_Amount", 0); _materials[i].SetFloat("_GlowStrength", 0);
                if (i < stringParticles.Length && stringParticles[i] != null) ResetParticles(stringParticles[i]);
                if (i < symbolParticles.Length && symbolParticles[i] != null) ResetParticles(symbolParticles[i]);
            }
            _lastTime = -1; _generation = -1; activeStringCount = 0; liveParticleCount = 0; liveSymbolParticleCount = 0;
        }

        private void Gather(int section, int pitch, int velocity, double age, double onset, float level, int style, int channel)
        {
            if (!HasPhysicalPitch(section, pitch) || level <= .001f) return;
            int index = section * 4 + PitchString(section, pitch);
            float energy = Mathf.Clamp01(level) * (style == 0 ? 1f : Mathf.Exp(-(float)age * 5f));
            _energy[index] = Mathf.Max(_energy[index], energy);
            if (onset > _onset[index]) { _onset[index] = onset; _sparkPitch[index] = pitch; _sparkChannel[index] = channel; }
            if (_posePitch[section] < 0 || velocity >= _poseVelocity[section])
            { _posePitch[section] = pitch; _poseVelocity[section] = velocity; _poseAge[section] = age; _poseStyle[section] = style; _poseChannel[section] = channel; }
        }

        public void Refresh(int ms)
        {
            Initialize();
            bool live = facade.CanReceiveLiveMidi(), music = facade.GetState() != 0 && _song != null && _song.isDecoded;
            double time = music ? ms * .001 : facade.clock.GetFrameDspSeconds();
            bool rebuild = _lastTime < 0 || music != _wasMusic || facade.applyGeneration != _generation || time < _lastTime || time - _lastTime > .25;
            float delta = rebuild ? 0 : (float)System.Math.Max(0, time - _lastTime);
            for (int i = 0; i < strings.Length; i++) { _energy[i] = 0; _onset[i] = -1e20; }
            for (int i = 0; i < bodies.Length; i++) { _posePitch[i] = -1; _poseVelocity[i] = 0; }
            if (music)
                for (int i = 0; i < facade.sequencer.activeNoteCount; i++)
                {
                    int n = facade.sequencer.activeNoteIds[i];
                    if (facade.instrumentRouter == null ? _song.channel[n] == 9 : facade.instrumentRouter.TargetForNote(n) != routedTarget) continue;
                    int style = facade.instrumentRouter == null ? voices.articulation : facade.instrumentRouter.ArticulationForNote(routedTarget, n);
                    Gather(NoteSection(_song.channel[n], _song.trackIndex[n]), facade.instrumentRouter == null ? _song.pitch[n] : facade.instrumentRouter.PitchForNote(n), _song.velocity[n], System.Math.Max(0, (ms - _song.startMs[n]) * .001),
                        _song.startMs[n] * .001, _song.velocity[n] / 127f * _song.ExpressionAt(_song.channel[n], ms) / 127f, style, _song.ColorChannelForNote(n));
                }
            else if (live)
            {
                var pool = facade.voicePool.ensemble;
                if (facade.instrumentRouter != null && pool != null)
                {
                    for (int i = 0; i < 64; i++) if (pool.active[i] && pool.live[i] && pool.targets[i] == routedTarget)
                        Gather(NoteSection(pool.identities[i] / 128, -1), pool.pitches[i], pool.velocities[i], System.Math.Max(0, time - pool.starts[i]), pool.starts[i], pool.levels[i], pool.articulations[i], -1);
                }
                else
                    for (int i = 0; i < 32; i++) if (voices.sounding[i])
                        Gather(voices.sections[i], voices.pitches[i], voices.velocities[i], System.Math.Max(0, time - voices.starts[i]), voices.starts[i], voices.levels[i], voices.articulation, -1);
            }
            for (int s = 0; s < bodies.Length; s++)
            {
                int pitch = _posePitch[s], style = _poseStyle[s]; float phase = (float)_poseAge[s];
                contacts[s].gameObject.SetActive(pitch >= 0);
                if (pitch >= 0)
                {
                    contacts[s].localPosition = StringPoint(s, pitch);
                    _contactMaterials[s].SetColor("_Color", PitchColor(pitch, _poseChannel[s]));
                }
                int str = pitch < 0 ? 1 : PitchString(s, pitch);
                float travel = style == 0 ? .10f * Mathf.Sin(phase * 3.4f) : .028f * Mathf.Sin(phase * 17f);
                float hop = style == 1 ? .012f * Mathf.Abs(Mathf.Sin(phase * 17f)) : 0;
                bool pluck = style == 2;
                bows[s].localPosition = _bowRest[s] + new Vector3(pitch < 0 || pluck ? 0 : travel, 0, pluck || pitch < 0 ? .045f : hop);
                bows[s].localRotation = _bowRotation[s] * Quaternion.Euler(0, (str - 1.5f) * 7f, pluck ? -12f : 0);
            }
            activeStringCount = 0; liveParticleCount = 0; liveSymbolParticleCount = 0;
            for (int i = 0; i < strings.Length; i++)
            {
                _glow[i] = rebuild ? _energy[i] : Mathf.MoveTowards(_glow[i], _energy[i], delta * 7f);
                if (_energy[i] > .001f) activeStringCount++;
                _materials[i].SetFloat("_MusicTime", (float)time);
                _materials[i].SetFloat("_Amount", .00065f * _glow[i]);
                _materials[i].SetFloat("_GlowStrength", _glow[i]);
                if (_energy[i] > .001f) _materials[i].SetColor("_GlowColor", PitchColor(_sparkPitch[i], _sparkChannel[i]));
                ParticleSystem symbols = i < symbolParticles.Length ? symbolParticles[i] : null;
                if (symbols != null)
                {
                    if (rebuild) { ResetParticles(symbols); _hitCount[i] = 0; }
                    else if (delta > 0 && symbols.particleCount > 0) symbols.Simulate(delta, false, false, false);
                }
                if (i >= stringParticles.Length || stringParticles[i] == null)
                {
                    if (symbols != null) { symbols.Pause(false); liveSymbolParticleCount += symbols.particleCount; }
                    continue;
                }
                var system = stringParticles[i];
                if (rebuild) { ResetParticles(system); _emittedOnset[i] = -1e20; _nextSpark[i] = -1e20; }
                else if (delta > 0 && system.particleCount > 0) system.Simulate(delta, false, false, false);
                if ((rebuild || delta > 0) && _energy[i] > .03f && (_onset[i] > _emittedOnset[i] || time >= _nextSpark[i]))
                {
                    bool attack = _onset[i] > _emittedOnset[i];
                    system.transform.localPosition = StringPoint(i / 4, _sparkPitch[i]);
                    var main = system.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(PitchColor(_sparkPitch[i], _sparkChannel[i]));
                    system.Emit(attack ? Mathf.Clamp(Mathf.RoundToInt(2 + _energy[i] * 5), 1, 8) : 1);
                    // New attacks only: seeking/late joining never replays old symbol bursts.
                    if (attack && !rebuild)
                    {
                        if (symbols != null && _hitCount[i] % 3 == 0)
                        {
                            symbols.transform.position = system.transform.position;
                            var symbolMain = symbols.main;
                            symbolMain.startColor = new ParticleSystem.MinMaxGradient(PitchColor(_sparkPitch[i], _sparkChannel[i]));
                            symbols.Emit(_energy[i] >= .85f && _hitCount[i] % 6 == 0 ? 2 : 1);
                        }
                        _hitCount[i]++;
                    }
                    _emittedOnset[i] = _onset[i]; _nextSpark[i] = time + .14;
                    if (rebuild) system.Simulate(Mathf.Min(.16f, (float)System.Math.Max(0, time - _onset[i])), false, false, false);
                }
                system.Pause(false); liveParticleCount += system.particleCount;
                if (symbols != null) { symbols.Pause(false); liveSymbolParticleCount += symbols.particleCount; }
            }
            _lastTime = time; _wasMusic = music; _generation = facade.applyGeneration;
        }
    }
}
