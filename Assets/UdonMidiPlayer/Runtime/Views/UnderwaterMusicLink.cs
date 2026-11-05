using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Views
{
    /// <summary>Local mesh marine life and floor seals, sampled on the transport clock.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UnderwaterMusicLink : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public Transform[] sources = new Transform[0];
        public Renderer[] seals = new Renderer[0];
        public ParticleSystem[] fishSystems = new ParticleSystem[0];
        [HideInInspector] public int activeFish, acceptedPulses, mergedPulses, droppedPulses;
        [HideInInspector] public float visualTime;
        private const int Capacity = 72;
        private bool[] _active = new bool[Capacity];
        private int[] _kind = new int[Capacity], _counts = new int[8];
        private int[] _limits = { 24, 16, 14, 6, 5, 3, 1, 10 };
        private float[] _size = new float[Capacity];
        private float[] _azimuth = new float[Capacity], _orbitSign = new float[Capacity];
        private bool[] _inbound = new bool[Capacity];
        private float[] _start = new float[Capacity], _duration = new float[Capacity], _phase = new float[Capacity];
        private Vector3[] _origin = new Vector3[Capacity], _destination = new Vector3[Capacity];
        private Color[] _color = new Color[Capacity], _ink = new Color[10];
        private Color[] _fishPalette = new Color[2];
        private int[] _colorIndex = new int[Capacity];
        private float[] _elevation = new float[Capacity];
        private float[] _nextSource = new float[10], _until = new float[10], _visibility = new float[10], _flash = new float[10];
        private float[] _sealStart = new float[10];
        private ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[Capacity];
        private MaterialPropertyBlock _properties;
        private bool _ready;
        private int _mode = -1, _serial;
        private float _previous, _nextFrame, _nextPoll, _nextGlobal;

        private void Initialize()
        {
            if (_ready) return;
            _properties = new MaterialPropertyBlock(); _ready = true;
            for (int i = 0; i < 10; i++) { _nextSource[i] = -1000; _until[i] = -1000; _ink[i] = Color.white; }
            foreach (var ps in fishSystems) if (ps != null) { ps.Play(false); ps.Pause(false); ps.Clear(false); }
        }
        private float MusicalTime()
        { return facade != null && facade.clock != null && facade.clock.GetState() != 0 ? (float)facade.clock.GetPositionSeconds() : Time.realtimeSinceStartup; }
        private void ObserveMode()
        {
            int mode = facade != null && facade.clock != null && facade.clock.GetState() != 0 ? 1 : 0;
            if (_mode == mode) return;
            ClearPerformance(); _mode = mode;
        }
        public void ClearPerformance()
        {
            Initialize();
            for (int i = 0; i < Capacity; i++) _active[i] = false;
            for (int i = 0; i < 8; i++) _counts[i] = 0;
            for (int i = 0; i < 10; i++) { _nextSource[i] = -1000; _until[i] = -1000; _visibility[i] = 0; _flash[i] = 0; _sealStart[i] = -1000; }
            foreach (var ps in fishSystems) if (ps != null) ps.Clear(false);
            foreach (var seal in seals) if (seal != null) seal.enabled = false;
            activeFish = 0; _previous = MusicalTime(); _nextFrame = 0; _nextPoll = -1000; _nextGlobal = -1000;
        }
        private void OnDisable() { ClearPerformance(); }
        public void FileNote(int note, int positionMs)
        {
            if (facade == null || facade.sequencer == null || facade.isSongLoading) return;
            var song = facade.sequencer.song;
            if (song == null || note < 0 || note >= song.pitch.Length || positionMs - song.startMs[note] > 80) return;
            var router = facade.instrumentRouter;
            int target = router == null ? 0 : router.IsFileDrum(note) ? 8 : router.TargetForNote(note);
            int pitch = router == null ? song.pitch[note] : target == 8 ? router.FileDrumPitch(note) : router.PitchForNote(note);
            PulseChannel(target, pitch, song.velocity[note], song.ColorChannelForNote(note), song.releaseMs[note] * .001f);
        }
        public void Pulse(int target, int pitch, int velocity)
        { PulseChannel(target, pitch, velocity, -1, MusicalTime() + .65f); }
        private int View(int target)
        { return target == 0 ? (facade.instrumentRouter == null ? facade.selectedInstrument : facade.instrumentRouter.pianoAppearance) : target + 1; }
        private int SealView(int view)
        {
            if (view == 8 && facade.instrumentFanLayout != null && facade.instrumentFanLayout.musicBoxSharesPiano
                && facade.instrumentRouter != null && facade.instrumentRouter.IsEnabled(0)) return View(0);
            return view;
        }
        private void PulseChannel(int target, int pitch, int velocity, int channel, float end)
        {
            Initialize(); ObserveMode();
            if (facade == null || target < 0 || target == 6 || target > 8 || velocity <= 0 || pitch < 0 || pitch > 127) return;
            var router = facade.instrumentRouter;
            if (router != null && !(target == 8 ? router.IsDrumsEnabled() : router.IsEnabled(target))) return;
            int view = View(target);
            if (view >= sources.Length || sources[view] == null || !sources[view].gameObject.activeInHierarchy) return;
            float now = MusicalTime(); int seal = SealView(view);
            // Use a canonical white-key pitch so black notes cannot darken the seal or add fish colors.
            _ink[seal] = PaletteColor(view, 60, 0);
            _fishPalette[0] = PaletteColor(view, 60, 0);
            _fishPalette[1] = PaletteColor(view, 60, 1);
            if (_visibility[seal] <= .001f && now >= _until[seal]) _sealStart[seal] = now;
            // A short note can complete the unfolding; dense notes extend the hold without restarting it.
            _until[seal] = Mathf.Max(_until[seal], Mathf.Max(Mathf.Max(end, now + .4f), _sealStart[seal] + 3.4f)); _flash[seal] = 1;
            if (now < _nextSource[view] || now < _nextGlobal) { mergedPulses++; return; }
            _nextSource[view] = now + .14f; _nextGlobal = now + .09f;
            int hash = (pitch * 37 + velocity * 13 + _serial++ * 53 + target * 19) % 100;
            int kind = hash < 30 ? 0 : hash < 50 ? 1 : hash < 67 ? 2 : hash < 77 ? 3 : hash < 85 ? 4 : hash < 90 ? 5 : hash < 92 ? 6 : 7;
            while (kind > 0 && _counts[kind] >= _limits[kind]) kind--;
            if (_counts[kind] >= _limits[kind]) { droppedPulses++; return; }
            int slot = -1;
            for (int i = 0; i < Capacity; i++) if (!_active[i]) { slot = i; break; }
            if (slot < 0) { droppedPulses++; return; }
            Vector3 origin = SourcePoint(view, pitch);
            _active[slot] = true; _kind[slot] = kind; _counts[kind]++; _start[slot] = now;
            _duration[slot] = 48 + kind * 3 + hash * .1f; _phase[slot] = hash * .17f;
            _size[slot] = .8f + .4f * Mathf.Repeat(_serial * .61803398875f, 1f);
            // A golden-angle sequence distributes new arrivals around the complete water volume.
            _azimuth[slot] = _serial * 2.399963f;
            _inbound[slot] = _serial % 2 == 0; _orbitSign[slot] = _serial % 4 < 2 ? 1 : -1;
            _origin[slot] = origin;
            float radius = 16f + Mathf.Repeat(_serial * .754877666f, 1f) * 8f;
            // Uniform solid-angle coverage, independent of species, pitch and the alternating route.
            float height = .04f + .92f * Mathf.Repeat(_serial * .41421356237f, 1f);
            _elevation[slot] = height;
            float horizontal = radius * Mathf.Sqrt(1f - height * height);
            _destination[slot] = new Vector3(Mathf.Sin(_azimuth[slot]) * horizontal, .75f + radius * .8f * height, Mathf.Cos(_azimuth[slot]) * horizontal);
            _colorIndex[slot] = channel < 0 ? _serial % 2 : channel % 2;
            _color[slot] = _fishPalette[_colorIndex[slot]]; activeFish++; acceptedPulses++;
        }
        private void Update()
        {
            Initialize(); ObserveMode();
            if (facade == null) return;
            if (Time.realtimeSinceStartup < _nextFrame) return;
            _nextFrame = Time.realtimeSinceStartup + 1f / 30f;
            float now = MusicalTime(), delta = Mathf.Max(0, now - _previous);
            if (now < _previous - .02f) ClearPerformance();
            _previous = now; visualTime = now;
            if (_mode == 0 && now >= _nextPoll)
            {
                _nextPoll = now + .1f;
                var pool = facade.voicePool == null ? null : facade.voicePool.ensemble;
                if (pool != null)
                    for (int i = 0; i < pool.active.Length; i++)
                        if (pool.active[i] && pool.live[i] && pool.targets[i] >= 0 && pool.targets[i] != 6 && pool.targets[i] <= 8)
                        { int view = SealView(View(pool.targets[i])); _until[view] = Mathf.Max(_until[view], now + .2f); }
            }
            AdvanceSeals(now, Mathf.Min(delta, .2f));
            for (int i = 0; i < Capacity; i++) if (_active[i] && now - _start[i] >= _duration[i])
            { _active[i] = false; _counts[_kind[i]]--; activeFish--; }
            for (int kind = 0; kind < fishSystems.Length && kind < 8; kind++)
            {
                var ps = fishSystems[kind]; if (ps == null) continue;
                int count = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (!_active[i] || _kind[i] != kind) continue;
                    float age = now - _start[i];
                    Vector3 point = FlightPoint(i, age);
                    Vector3 tangent = FlightPoint(i, age + .04f) - point;
                    _color[i] = _fishPalette[_colorIndex[i]];
                    Color color = _color[i]; color.a *= Mathf.SmoothStep(0, 1, age / (_inbound[i] ? 3.2f : .6f)) * Mathf.Clamp01((_duration[i] - age) / 3f);
                    var p = new ParticleSystem.Particle(); p.position = point; p.velocity = Vector3.zero;
                    p.startColor = color; p.startSize = _size[i];
                    // Jellyfish propel along their upright bell axis, with a modest lean into the route.
                    p.rotation3D = tangent.sqrMagnitude > .000001f
                        ? (kind == 7 ? Quaternion.FromToRotation(Vector3.up, Vector3.up + tangent.normalized * .4f)
                            : Quaternion.LookRotation(tangent)).eulerAngles : Vector3.zero;
                    p.randomSeed = (uint)(i + 1); p.startLifetime = _duration[i]; p.remainingLifetime = Mathf.Max(.001f, _duration[i] - age);
                    _particles[count++] = p;
                }
                ps.SetParticles(_particles, count);
                _properties.Clear(); _properties.SetFloat("_MusicTime", now);
                ps.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(_properties);
            }
        }
        private Vector3 FlightPoint(int slot, float age)
        {
            if (_kind[slot] == 7) age = JellyTravelAge(slot, age);
            float angle = _azimuth[slot], sign = _orbitSign[slot];
            float travel = 8f + _kind[slot] * .35f;
            if (!_inbound[slot] && age < travel)
                return Vector3.Lerp(_origin[slot], _destination[slot], Mathf.SmoothStep(0, 1, age / travel));
            // Legacy _inbound flag now denotes an exterior spawn. It never approaches the room.
            float elapsed = _inbound[slot] ? age : age - travel;
            float radius = new Vector2(_destination[slot].x, _destination[slot].z).magnitude;
            float elevation = _elevation[slot];
            float baseRadius = radius / Mathf.Sqrt(1f - elevation * elevation);
            float outward = baseRadius + elapsed * .22f;
            angle += sign * elapsed * .055f;
            float horizontal = outward * Mathf.Sqrt(1f - elevation * elevation);
            return new Vector3(Mathf.Sin(angle) * horizontal, .75f + outward * .8f * elevation, Mathf.Cos(angle) * horizontal);
        }
        private float JellyTravelAge(int slot, float age)
        {
            // The shader derives the same stable phase from uniform instance scale: no extra GPU streams.
            // Integrating a short power stroke and long recovery keeps forward motion continuous.
            float phase = Mathf.Repeat(_size[slot] * 17f, 1f);
            float initial = Mathf.Repeat(_start[slot] / 2.6f + phase, 1f);
            float current = Mathf.Repeat((_start[slot] + age) / 2.6f + phase, 1f);
            float initialStroke = initial < .28f ? .55f * Mathf.SmoothStep(0, 1, initial / .28f)
                : .55f + .45f * Mathf.SmoothStep(0, 1, (initial - .28f) / .72f);
            float currentStroke = current < .28f ? .55f * Mathf.SmoothStep(0, 1, current / .28f)
                : .55f + .45f * Mathf.SmoothStep(0, 1, (current - .28f) / .72f);
            return age + 2.6f * .7f * (currentStroke - current - initialStroke + initial);
        }
        private void AdvanceSeals(float now, float delta)
        {
            for (int i = 0; i < seals.Length && i < 10; i++)
            {
                var seal = seals[i]; if (seal == null || i >= sources.Length || sources[i] == null) continue;
                bool enabledSource = sources[i].gameObject.activeInHierarchy && SealView(i) == i;
                _visibility[i] = Mathf.MoveTowards(_visibility[i], enabledSource && now < _until[i] ? 1 : 0, delta * 2);
                _flash[i] = Mathf.Max(0, _flash[i] - delta * 2);
                seal.enabled = enabledSource && _visibility[i] > .001f; if (!seal.enabled) continue;
                Vector3 p = sources[i].position; float yaw = sources[i].eulerAngles.y;
                var layout = facade.instrumentFanLayout;
                if (layout != null && i < layout.roots.Length && layout.roots[i] != null
                    && i < layout.footprintMinimum.Length && i < layout.footprintMaximum.Length)
                {
                    p = layout.roots[i].TransformPoint((layout.footprintMinimum[i] + layout.footprintMaximum[i]) * .5f);
                    yaw = layout.roots[i].eulerAngles.y;
                }
                float floorY = layout != null && layout.roots.Length > 1 && layout.roots[1] != null
                    ? layout.roots[1].parent.TransformPoint(layout.center).y : 0;
                seal.transform.position = new Vector3(p.x, floorY + .028f + i * .0002f, p.z);
                seal.transform.rotation = Quaternion.Euler(90, yaw, 0);
                _properties.Clear(); _properties.SetColor("_Color", _ink[i]); _properties.SetFloat("_MusicTime", now);
                float unfold = Mathf.Clamp01((now - _sealStart[i]) / 2.8f);
                _properties.SetVector("_Activation", new Vector4(_visibility[i], unfold, .45f, _flash[i]));
                seal.SetPropertyBlock(_properties);
            }
        }
        private Color PaletteColor(int view, int pitch, int channel)
        {
            if (view < facade.instrumentKeyboards.Length && facade.instrumentKeyboards[view] != null)
            {
                if (channel >= 0) return facade.instrumentKeyboards[view].FileChannelColor(pitch, channel);
                Material material = facade.instrumentKeyboards[view].GetNoteMaterial(pitch); if (material != null) return material.color;
            }
            if (view < facade.instrumentStrings.Length && facade.instrumentStrings[view] != null)
            {
                if (channel >= 0) return facade.instrumentStrings[view].FileChannelColor(pitch, channel);
                Color[] palette = facade.instrumentStrings[view].songPalette;
                if (palette != null && palette.Length > 0) return palette[pitch % palette.Length];
            }
            if (view == 9 && facade.drumView != null) return channel >= 0 ? facade.drumView.FileChannelColor(channel) : facade.drumView.CurrentPaletteColor();
            return Color.white;
        }
        private Vector3 SourcePoint(int view, int pitch)
        {
            if (view < facade.instrumentWaterfalls.Length && facade.instrumentWaterfalls[view] != null)
            {
                var fall = facade.instrumentWaterfalls[view];
                return fall.transform.TransformPoint(fall.GetKeyHitPosition(pitch - 21)) + Vector3.up * .12f;
            }
            if (view == 9 && facade.drumView != null && facade.drumView.bank != null)
            {
                int lane = facade.drumView.bank.Lane(pitch);
                if (lane >= 0 && lane < facade.drumView.hitPoints.Length && facade.drumView.hitPoints[lane] != null) return facade.drumView.hitPoints[lane].position + Vector3.up * .12f;
            }
            return sources[view].position + Vector3.up * .35f;
        }
    }
}
