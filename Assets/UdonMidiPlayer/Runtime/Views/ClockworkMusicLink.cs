using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Views
{
    /// <summary>Local, bounded butterfly flights and layered, non-emissive musical seals.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ClockworkMusicLink : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public ClockworkSceneController mechanism;
        public ParticleSystem sparks, ripples;
        public Transform[] sources = new Transform[0];
        public float[] gearRadii = new float[0];
        public float dialRadius = 5.3f;
        // Piano floor, room floor, dial, left/right wheels, pendulum, crown, left/right gallery.
        public Renderer[] ritualNodes = new Renderer[0];
        public Transform[] ritualAnchors = new Transform[0];
        public float[] ritualDelays = new float[0];
        [HideInInspector] public int activeLinks, acceptedPulses, mergedPulses, droppedPulses, gearArrivals, dialArrivals;
        [HideInInspector] public Color lastPulseColor;
        [HideInInspector] public Vector3 lastSourcePoint;
        [HideInInspector] public float musicEnergy, ritualVisibility, expansionAge;
        // 0 quiet, 1 excitation, 2 overcharge, 3 decay; diagnostic local state only.
        [HideInInspector] public int[] nodeStates = new int[9];
        [HideInInspector] public float[] nodeCharges = new float[9];
        private const int Capacity = 18;
        private bool[] _active = new bool[Capacity];
        private float[] _start = new float[Capacity], _duration = new float[Capacity], _strength = new float[Capacity], _angle = new float[Capacity];
        private Vector3[] _origin = new Vector3[Capacity], _destination = new Vector3[Capacity];
        private ParticleSystem.Particle[] _butterflies = new ParticleSystem.Particle[Capacity];
        private Color[] _color = new Color[Capacity];
        private float[] _nextSource = new float[10], _nodeFlash = new float[9], _nodeReveal = new float[9];
        private Color[] _nodeColor = new Color[9];
        private int _generation = -1, _resonanceCursor;
        private float _nextSample, _lastVisualTime, _lastLivePulse = -1000f, _nextSoundPoll, _nodeAge;
        private bool _ready, _liveSounding;
        private Color _roomColor = Color.white;
        private MaterialPropertyBlock _properties, _glassProperties;

        private void Initialize()
        {
            if (_ready) return;
            for (int i = 0; i < 10; i++) _nextSource[i] = -1000f;
            for (int i = 0; i < 9; i++) _nodeColor[i] = Color.white;
            _properties = new MaterialPropertyBlock(); _glassProperties = new MaterialPropertyBlock();
            _lastVisualTime = Time.realtimeSinceStartup; _ready = true;
        }

        public void ClearLinks()
        {
            Initialize();
            for (int i = 0; i < Capacity; i++) _active[i] = false;
            for (int i = 0; i < 10; i++) _nextSource[i] = -1000f;
            activeLinks = 0; _nextSample = 0;
            if (sparks != null) sparks.Clear(false);
            if (ripples != null) ripples.Clear(false);
            _generation = facade == null ? -1 : facade.applyGeneration;
        }
        private void OnDisable()
        {
            ClearLinks(); musicEnergy = 0; ritualVisibility = 0; expansionAge = 0;
            foreach (var node in ritualNodes) if (node != null) node.enabled = false;
            if (mechanism != null) { mechanism.musicDrive = 0; mechanism.musicActive = false; ClearGlass(); }
        }
        private void Update()
        {
            Initialize(); float now = Time.realtimeSinceStartup;
            AdvanceVisuals(now, Mathf.Clamp(now - _lastVisualTime, 0, .1f)); _lastVisualTime = now;
        }

        public void FileNote(int note, int positionMs)
        {
            if (facade == null || facade.sequencer == null || facade.isSongLoading) return;
            var song = facade.sequencer.song;
            if (song == null || note < 0 || note >= song.pitch.Length || positionMs - song.startMs[note] > 80) return;
            var router = facade.instrumentRouter;
            int target = router == null ? (facade.selectedInstrument < 2 ? 0 : facade.selectedInstrument - 1)
                : router.IsFileDrum(note) ? 8 : router.TargetForNote(note);
            int pitch = router == null ? song.pitch[note] : target == 8 ? router.FileDrumPitch(note) : router.PitchForNote(note);
            PulseChannel(target, pitch, song.velocity[note], song.channel[note]);
        }
        public void Pulse(int target, int pitch, int velocity)
        { PulseChannel(target, pitch, velocity, -1); }

        private void PulseChannel(int target, int pitch, int velocity, int channel)
        {
            Initialize();
            if (facade == null || mechanism == null || sparks == null || target < 0 || target == 6 || target > 8
                || pitch < 0 || pitch > 127 || velocity <= 0) return;
            var router = facade.instrumentRouter;
            if (router != null && !(target == 8 ? router.IsDrumsEnabled() : router.IsEnabled(target))) return;
            int view = target == 0 ? (router == null ? facade.selectedInstrument : router.pianoAppearance) : target + 1;
            if (view < 0 || view >= sources.Length || sources[view] == null || !sources[view].gameObject.activeInHierarchy) return;
            ObserveGeneration(); float now = Time.realtimeSinceStartup;
            if (facade.CanReceiveLiveMidi()) _lastLivePulse = now;
            mechanism.musicActive = true;
            float strength = Mathf.Pow(Mathf.Clamp01(velocity / 127f), 1.1f);
            musicEnergy = Mathf.Min(1f, musicEnergy + strength * (now < _nextSource[view] ? .012f : .10f));
            if (now < _nextSource[view]) { mergedPulses++; return; }
            int slot = -1;
            for (int i = 0; i < Capacity; i++) if (!_active[i]) { slot = i; break; }
            if (slot < 0) { droppedPulses++; return; }
            _nextSource[view] = now + .125f;
            Color color = PaletteColor(view, pitch, channel); Vector3 origin = SourcePoint(view, pitch);
            _active[slot] = true; _start[slot] = now; _strength[slot] = strength; _color[slot] = color;
            _origin[slot] = origin; _angle[slot] = (pitch % 12) * Mathf.PI / 6f;
            _destination[slot] = mechanism.dialCenter + new Vector3(Mathf.Sin(_angle[slot]) * dialRadius * .88f,
                Mathf.Cos(_angle[slot]) * dialRadius * .88f, -.27f);
            // Travel at a readable speed, with lifetime derived from the entire source-to-dial trip.
            _duration[slot] = Mathf.Max(1.65f, Vector3.Distance(origin, _destination[slot]) / 3.4f);
            _roomColor = color; lastPulseColor = color; lastSourcePoint = origin; acceptedPulses++; activeLinks++;
            ExciteNode(0, color, strength * .45f);
        }

        private Color PaletteColor(int view, int pitch, int channel)
        {
            if (view < facade.instrumentKeyboards.Length && facade.instrumentKeyboards[view] != null)
            {
                if (channel >= 0) return facade.instrumentKeyboards[view].FileChannelColor(pitch, channel);
                Material material = facade.instrumentKeyboards[view].GetNoteMaterial(pitch);
                if (material != null) return material.color;
            }
            if (view < facade.instrumentStrings.Length && facade.instrumentStrings[view] != null)
            {
                if (channel >= 0) return facade.instrumentStrings[view].FileChannelColor(pitch, channel);
                Color[] palette = facade.instrumentStrings[view].songPalette; int pc = pitch % 12;
                if (palette != null && palette.Length == 2) return palette[pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10 ? 1 : 0];
            }
            if (view == 9 && facade.drumView != null) return channel >= 0 ? facade.drumView.FileChannelColor(channel) : facade.drumView.CurrentPaletteColor();
            return Color.white;
        }
        private Vector3 SourcePoint(int view, int pitch)
        {
            if (view < facade.instrumentWaterfalls.Length && facade.instrumentWaterfalls[view] != null)
            {
                var fall = facade.instrumentWaterfalls[view];
                return fall.transform.TransformPoint(fall.GetKeyHitPosition(pitch - 21)) + Vector3.up * .10f;
            }
            if (view == 9 && facade.drumView != null && facade.drumView.bank != null)
            {
                int lane = facade.drumView.bank.Lane(pitch);
                if (lane >= 0 && lane < facade.drumView.hitPoints.Length && facade.drumView.hitPoints[lane] != null)
                    return facade.drumView.hitPoints[lane].position + Vector3.up * .12f;
            }
            return sources[view].position + Vector3.up * .12f;
        }

        public void AdvanceVisuals(float now, float delta)
        {
            Initialize(); ObserveGeneration();
            musicEnergy = Mathf.Max(0, musicEnergy - delta * .24f);
            if (mechanism == null) return;
            bool live = facade != null && facade.CanReceiveLiveMidi();
            if (now >= _nextSoundPoll)
            {
                _nextSoundPoll = now + .1f; _liveSounding = false;
                if (live && facade.voicePool != null)
                {
                    var ensemble = facade.voicePool.ensemble;
                    if (ensemble != null) for (int i = 0; i < ensemble.active.Length; i++)
                        if (ensemble.active[i] && ensemble.live[i]) { _liveSounding = true; break; }
                    if (facade.voicePool.drums != null && facade.voicePool.drums.activeVoiceCount > 0) _liveSounding = true;
                }
            }
            bool musical = (facade != null && facade.isReady && !facade.isSongLoading && facade.clock != null && facade.clock.GetState() == 1)
                || (live && (_liveSounding || now - _lastLivePulse < 1.2f));
            mechanism.musicActive = musical;
            mechanism.musicDrive = Mathf.MoveTowards(mechanism.musicDrive, musicEnergy, delta * 1.5f);
            ritualVisibility = Mathf.MoveTowards(ritualVisibility, musical ? 1f : 0f, delta * (musical ? .9f : .38f));
            if (musical) expansionAge = Mathf.Min(8, expansionAge + delta);
            if (ritualVisibility <= 0 && !musical) expansionAge = 0;
            _nodeAge += delta;
            if (_nodeAge >= 1f / 15f) { AdvanceRituals(_nodeAge, musical); _nodeAge = 0; }
            if (now < _nextSample) return;
            _nextSample = now + 1f / 30f;
            int count = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (!_active[i]) continue;
                float age = now - _start[i];
                float t = age / _duration[i];
                if (t < 0) continue;
                if (t >= 1)
                {
                    dialArrivals++; ExciteNode(2, _color[i], .32f + _strength[i] * .24f);
                    int receiver = 3 + _resonanceCursor % 6; _resonanceCursor++;
                    ExciteNode(receiver, _color[i], .35f + _strength[i] * .24f); gearArrivals++;
                    ExciteNode(1, _color[i], .08f + _strength[i] * .10f);
                    // An arriving butterfly lingers at the receiver before fading; never fade it in transit.
                    Emit(ripples, _destination[i], _color[i], .24f + _strength[i] * .14f, 1);
                    musicEnergy = Mathf.Min(1, musicEnergy + .025f * _strength[i]);
                    _active[i] = false; activeLinks--; continue;
                }
                // A continuous, gently weaving butterfly flight. The envelope reaches exactly zero at both ends.
                Vector3 line = Vector3.Lerp(_origin[i], _destination[i], t);
                Vector3 direction = (_destination[i] - _origin[i]).normalized;
                Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, direction);
                float curl = age * 4.6f + _angle[i]; float radius = Mathf.Sin(t * Mathf.PI) * .30f;
                Vector3 point = line + (side * Mathf.Cos(curl) + up * Mathf.Sin(curl)) * radius;
                Color color = _color[i]; color.a = .95f;
                var butterfly = new ParticleSystem.Particle();
                butterfly.position = point; butterfly.velocity = Vector3.zero;
                butterfly.startColor = color; butterfly.startSize = .24f + _strength[i] * .14f;
                butterfly.rotation = Mathf.Sin(curl) * 18f;
                butterfly.randomSeed = (uint)(i + 1);
                butterfly.startLifetime = _duration[i] + .75f;
                butterfly.remainingLifetime = Mathf.Max(.75f, _duration[i] - age + .75f);
                _butterflies[count] = butterfly; count++;
            }
            // Rewrite the same bounded flights, rather than emitting a trail of short-lived replacement dots.
            if (sparks != null) sparks.SetParticles(_butterflies, count);
        }
        private void ExciteNode(int node, Color color, float strength)
        {
            if (node < 0 || node >= 9) return;
            _nodeColor[node] = color;
            nodeCharges[node] = Mathf.Min(1f, nodeCharges[node] + strength);
            _nodeFlash[node] = Mathf.Min(1f, _nodeFlash[node] + .55f + strength * .3f);
        }
        private void AdvanceRituals(float delta, bool musical)
        {
            for (int i = 0; i < ritualNodes.Length && i < 9; i++)
            {
                var node = ritualNodes[i]; if (node == null) continue;
                float previous = nodeCharges[i];
                nodeCharges[i] = Mathf.Max(0, nodeCharges[i] - delta * (musical ? .16f : .5f));
                _nodeFlash[i] = Mathf.Max(0, _nodeFlash[i] - delta * 1.9f);
                float reveal = i == 1 ? expansionAge / 6.5f : (expansionAge - ritualDelays[i]) * .85f;
                reveal = Mathf.Clamp01(reveal);
                if (reveal > 0 && _nodeReveal[i] <= 0 && musical) ExciteNode(i, _roomColor, .25f);
                _nodeReveal[i] = reveal;
                nodeStates[i] = ritualVisibility <= 0 ? 0 : nodeCharges[i] > .72f ? 2 : _nodeFlash[i] > .12f ? 1 : previous > .01f ? 3 : 0;
                bool visible = ritualVisibility > .001f && reveal > .001f;
                node.enabled = visible; if (!visible) continue;
                if (i < ritualAnchors.Length && ritualAnchors[i] != null)
                {
                    node.transform.position = (i == 5 ? ritualAnchors[i].TransformPoint(Vector3.down * mechanism.pendulumLength)
                        : ritualAnchors[i].position) + new Vector3(0, 0, -.65f);
                    // Pendulum medallion follows its real assembly, other seals counter-rotate in the shader.
                }
                if (i <= 1 && facade.instrumentFanLayout != null && facade.instrumentFanLayout.roots.Length > 0
                    && facade.instrumentFanLayout.roots[0] != null)
                {
                    var layout = facade.instrumentFanLayout;
                    Vector3 center = layout.roots[0].parent.TransformPoint(layout.center);
                    node.transform.position = new Vector3(center.x, .075f + i * .008f, center.z);
                }
                Color ink = Color.Lerp(_roomColor, _nodeColor[i], Mathf.Clamp01(nodeCharges[i] * 2));
                _properties.SetColor("_Color", ink);
                _properties.SetFloat("_StrokeScale", i == 0 ? 2f : i >= 2 ? 1.8f : 1f);
                _properties.SetFloat("_MinPixels", i >= 2 ? 2.4f : 1.6f);
                _properties.SetFloat("_Contrast", i >= 2 ? .88f : .65f);
                _properties.SetVector("_Activation", new Vector4(ritualVisibility, reveal, nodeCharges[i], _nodeFlash[i]));
                node.SetPropertyBlock(_properties);
            }
            if (mechanism.dialGlass != null)
            {
                mechanism.dialGlass.GetPropertyBlock(_glassProperties);
                _glassProperties.SetColor("_MusicTint", _nodeColor[2]);
                _glassProperties.SetFloat("_MusicCharge", nodeCharges[2] * ritualVisibility);
                mechanism.dialGlass.SetPropertyBlock(_glassProperties);
            }
        }
        private void ClearGlass()
        {
            if (mechanism.dialGlass == null) return;
            mechanism.dialGlass.GetPropertyBlock(_glassProperties); _glassProperties.SetFloat("_MusicCharge", 0);
            mechanism.dialGlass.SetPropertyBlock(_glassProperties);
        }
        private void ObserveGeneration()
        {
            if (facade == null || _generation == facade.applyGeneration) return;
            _generation = facade.applyGeneration;
            for (int i = 0; i < 10; i++) _nextSource[i] = -1000f;
            _lastLivePulse = -1000f;
        }
        private void Emit(ParticleSystem system, Vector3 point, Color color, float size, int count)
        {
            if (system == null) return;
            system.transform.position = point;
            var main = system.main; main.startColor = new ParticleSystem.MinMaxGradient(color); main.startSize = size;
            system.Emit(count);
        }
    }
}
