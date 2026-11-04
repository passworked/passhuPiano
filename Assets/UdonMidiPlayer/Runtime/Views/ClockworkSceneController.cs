using System;
using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Views
{
    /// <summary>Continuous civil/music drivers sharing one pose, coupled gears and one celestial Point Light.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Clockwork Scene")]
    public class ClockworkSceneController : UdonSharpBehaviour
    {
        public Transform[] gears;
        public int[] gearTeeth, gearRatioNumerators, gearRatioDenominators;
        public float[] gearMeshPhases;
        public Quaternion[] gearRestRotations;
        public Vector3 rotationAxis = Vector3.forward;
        public Transform hourHand, minuteHand, secondHand, pendulum, anchor;
        public Quaternion hourRest = Quaternion.identity, minuteRest = Quaternion.identity, secondRest = Quaternion.identity;
        public Quaternion pendulumRest = Quaternion.identity, anchorRest = Quaternion.identity;
        public float pendulumLength = 9, pendulumAmplitude = 3.2f, crutchRatio = 1.125f;
        public int escapeTeeth = 30, cycleBeats = 7800;
        public Light celestialLight;
        public Renderer dialGlass;
        public Material skyMaterial;
        public Material[] readabilityMaterials;
        public Color[] readabilityBaseColors;
        public Material[] atmosphereMaterials;
        public Vector3 dialCenter = new Vector3(1.65f, 10.8f, 12.1f);
        public float celestialDistance = 38, glassTransmission = .68f;
        public bool dreamMode;
        [HideInInspector] public bool musicActive, moonlight;
        [HideInInspector] public float musicDrive, musicBlend;
        // 0 civil, 1 entering music, 2 music, 3 returning to civil. Both drivers share phases and velocities.
        [HideInInspector] public int transitionState;
        [HideInInspector] public float displayedHourAngle, displayedMinuteAngle, displayedSecondAngle, displayedPendulumAngle, displayedSkyPhase;
        private double _clockAnchor, _dayAnchor, _realtimeAnchor, _nextResync;
        private double[] _phase = new double[4], _returnTarget = new double[4];
        private float[] _velocity = new float[4];
        private double _mechanicalPhase, _pendulumPhase;
        private float _mechanicalSpeed = 1, _dreamDrive, _lightingAge;
        private bool _motionReady, _wasMusic;
        private MaterialPropertyBlock _dialBlock;

        private void Start() { RefreshFromClientClock(); }
        private void Update()
        {
            if (!_motionReady || Time.realtimeSinceStartupAsDouble >= _nextResync) RefreshFromClientClock();
            double age = Time.realtimeSinceStartupAsDouble - _realtimeAnchor;
            AdvancePresentation(Time.unscaledDeltaTime, _dayAnchor + age);
        }

        public void RefreshFromClientClock()
        {
            DateTime local = DateTime.Now;
            _clockAnchor = local.Ticks * .0000001d;
            _dayAnchor = local.TimeOfDay.TotalSeconds;
            _realtimeAnchor = Time.realtimeSinceStartupAsDouble;
            _nextResync = _realtimeAnchor + 2d;
            if (!_motionReady)
            {
                for (int i = 0; i < 4; i++)
                {
                    _phase[i] = CivilAngle(i, _dayAnchor);
                    _returnTarget[i] = _phase[i]; _velocity[i] = CivilSpeed(i);
                }
                _mechanicalPhase = _clockAnchor % (cycleBeats * PendulumPeriod() * .5d);
                _pendulumPhase = (_mechanicalPhase / PendulumPeriod()) * Math.PI * 2d;
                _motionReady = true;
            }
            ApplyCurrentPose();
        }

        public float PendulumPeriod() { return 2f * Mathf.PI * Mathf.Sqrt(Mathf.Max(.1f, pendulumLength) / 9.80665f); }
        private double CivilAngle(int index, double day)
        {
            day = ((day % 86400d) + 86400d) % 86400d;
            if (index == 0) return day / 120d % 360d;
            if (index == 1) return day / 10d % 360d;
            if (index == 2) return day * 6d % 360d;
            return day / 240d - 180d;
        }
        private float CivilSpeed(int index) { return index == 0 ? 1f / 120f : index == 1 ? .1f : index == 2 ? 6f : 1f / 240f; }

        // Explicit civil time makes continuity/return checks deterministic without changing the system clock.
        public void AdvancePresentation(float delta, double civilDaySeconds)
        {
            if (!_motionReady) RefreshFromClientClock();
            delta = Mathf.Clamp(delta, 0f, .1f);
            bool musical = dreamMode && musicActive;
            float blendResponse = 1f - Mathf.Exp(-delta * (musical ? .8f : .35f));
            musicBlend = Mathf.Lerp(musicBlend, musical ? 1f : 0f, blendResponse);
            _dreamDrive = Mathf.Lerp(_dreamDrive, Mathf.Clamp01(musicDrive), 1f - Mathf.Exp(-delta * 1.5f));
            float chase = Mathf.Sin((float)(_mechanicalPhase * .13d));
            float worstError = 0;
            for (int i = 0; i < 4; i++)
            {
                double target = CivilAngle(i, civilDaySeconds);
                if (musical)
                {
                    float desired = i == 0 ? .9f + _dreamDrive * (3.1f + chase * .45f)
                        : i == 1 ? 3.5f + _dreamDrive * (8f - chase * 1.1f)
                        : i == 2 ? 11f + _dreamDrive * (20f + chase * 2.5f) : 1.2f + _dreamDrive * 1.4f;
                    float before = _velocity[i];
                    _velocity[i] = Mathf.Lerp(before, desired, 1f - Mathf.Exp(-delta * .8f));
                    _phase[i] += (before + _velocity[i]) * .5d * delta;
                }
                else
                {
                    // Unwrap the moving civil target once on exit, preserving the incoming angular velocity.
                    if (_wasMusic) _returnTarget[i] = _phase[i] + Mathf.DeltaAngle((float)(_phase[i] % 360d), (float)target);
                    else _returnTarget[i] += Mathf.DeltaAngle((float)(_returnTarget[i] % 360d), (float)target);
                    double error = _phase[i] - _returnTarget[i];
                    float realSpeed = CivilSpeed(i);
                    double relativeVelocity = _velocity[i] - realSpeed;
                    double omega = i == 3 ? .16d : .5d;
                    double decay = Math.Exp(-omega * delta);
                    double spring = relativeVelocity + omega * error;
                    // Critically damped moving-target solution: no angle snap and no velocity reset.
                    _phase[i] = _returnTarget[i] + (error + spring * delta) * decay + realSpeed * delta;
                    _velocity[i] = realSpeed + (float)((relativeVelocity - omega * spring * delta) * decay);
                    _returnTarget[i] += realSpeed * delta;
                    worstError = Mathf.Max(worstError, (float)Math.Abs(error));
                }
                // Reduce both unwrapped values together to retain precision for long sessions.
                if (Math.Abs(_phase[i]) > 7200d)
                {
                    double turns = Math.Floor(_phase[i] / 360d) * 360d;
                    _phase[i] -= turns; _returnTarget[i] -= turns;
                }
            }
            float targetSpeed = musical ? 2.2f + _dreamDrive * 3f : 1f;
            float previousSpeed = _mechanicalSpeed;
            _mechanicalSpeed = Mathf.Lerp(_mechanicalSpeed, targetSpeed, 1f - Mathf.Exp(-delta * (musical ? .7f : .3f)));
            double step = (previousSpeed + _mechanicalSpeed) * .5d * delta;
            _mechanicalPhase += step;
            _pendulumPhase = (_pendulumPhase + step * Math.PI * 2d / PendulumPeriod()) % (Math.PI * 2d);
            _wasMusic = musical;
            transitionState = musical ? (musicBlend < .97f ? 1 : 2) : (musicBlend > .01f || worstError > .05f ? 3 : 0);
            ApplyPose(_phase[0], _phase[1], _phase[2], _mechanicalPhase, _pendulumPhase);
            _lightingAge += delta;
            if (_lightingAge >= 1f / 30f)
            {
                _lightingAge %= 1f / 30f;
                ApplyOrbitLighting((float)(_phase[3] % 360d) / 360f, musicBlend);
            }
        }

        private void ApplyCurrentPose()
        {
            ApplyPose(_phase[0], _phase[1], _phase[2], _mechanicalPhase, _pendulumPhase);
            ApplyOrbitLighting((float)(_phase[3] % 360d) / 360f, musicBlend);
        }

        private void ApplyPose(double hour, double minute, double second, double mechanicalSeconds, double pendulumPhase)
        {
            displayedHourAngle = (float)((hour % 360d + 360d) % 360d);
            displayedMinuteAngle = (float)((minute % 360d + 360d) % 360d);
            displayedSecondAngle = (float)((second % 360d + 360d) % 360d);
            if (hourHand != null) hourHand.localRotation = Quaternion.AngleAxis(-displayedHourAngle, rotationAxis) * hourRest;
            if (minuteHand != null) minuteHand.localRotation = Quaternion.AngleAxis(-displayedMinuteAngle, rotationAxis) * minuteRest;
            if (secondHand != null) secondHand.localRotation = Quaternion.AngleAxis(-displayedSecondAngle, rotationAxis) * secondRest;
            // Continuous common gear phase; every ratio and tooth offset remains constrained.
            double escapeDegrees = -mechanicalSeconds * (360d / (escapeTeeth * PendulumPeriod()));
            if (gears != null)
                for (int i = 0; i < gears.Length; i++)
                    if (gears[i] != null)
                        gears[i].localRotation = Quaternion.AngleAxis(gearMeshPhases[i]
                            + (float)((escapeDegrees * gearRatioNumerators[i] / gearRatioDenominators[i]) % 360d), rotationAxis) * gearRestRotations[i];
            displayedPendulumAngle = Mathf.Sin((float)pendulumPhase) * pendulumAmplitude;
            if (pendulum != null) pendulum.localRotation = Quaternion.AngleAxis(displayedPendulumAngle, rotationAxis) * pendulumRest;
            if (anchor != null)
            {
                float angle = Mathf.Asin(Mathf.Clamp(Mathf.Sin(displayedPendulumAngle * Mathf.Deg2Rad) * crutchRatio, -.95f, .95f)) * Mathf.Rad2Deg;
                anchor.localRotation = Quaternion.AngleAxis(angle, rotationAxis) * anchorRest;
            }
        }

        // Maintenance-only preview; leaves the runtime phases and velocities intact.
        public void EvaluateAt(double mechanicalSeconds, double localDaySeconds)
        {
            ApplyPose(CivilAngle(0, localDaySeconds), CivilAngle(1, localDaySeconds), CivilAngle(2, localDaySeconds),
                mechanicalSeconds, (mechanicalSeconds / PendulumPeriod() % 1d) * Math.PI * 2d);
            ApplyOrbitLighting((float)CivilAngle(3, localDaySeconds) / 360f, 0);
        }

        private void ApplyOrbitLighting(float phase, float dream)
        {
            if (celestialLight == null) return;
            phase = (phase % 1f + 1f) % 1f;
            displayedSkyPhase = phase;
            float orbit = phase * Mathf.PI * 2f;
            float daylight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(.5f + Mathf.Cos(orbit) * 3f));
            float dusk = .5f + .5f * Mathf.Sin(orbit);
            float twilight = 1f - Mathf.Abs(daylight * 2f - 1f);
            float altitude = .2f + .8f * Mathf.Abs(Mathf.Cos(orbit));
            moonlight = daylight < .5f; // Diagnostic only. No visual branch or light relocation here.
            Vector3 direction = new Vector3(Mathf.Sin(orbit) * .8f, .38f + Mathf.Cos(orbit) * .58f, 1f).normalized;
            celestialLight.transform.position = dialCenter + direction * celestialDistance;
            celestialLight.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            Color twilightColor = Color.Lerp(new Color(.36f,.68f,.86f), new Color(1f,.55f,.29f), dusk);
            Color body = Color.Lerp(new Color(.57f,.64f,1f), new Color(1f,.91f,.73f), daylight);
            celestialLight.color = Color.Lerp(body, twilightColor, twilight * .65f);
            celestialLight.intensity = glassTransmission * Mathf.Lerp(2.2f, 6.9f, daylight);
            RenderSettings.fog = atmosphereMaterials != null && atmosphereMaterials.Length > 0;
            if (RenderSettings.fog)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = .011f;
                RenderSettings.fogColor = Color.Lerp(new Color(.10f,.09f,.21f), new Color(.32f,.28f,.20f), daylight);
            }
            if (atmosphereMaterials != null)
                foreach (var material in atmosphereMaterials)
                    if (material != null)
                    {
                        material.SetFloat("_ClockworkDaylight", daylight);
                        material.SetFloat("_ClockworkAltitude", altitude);
                        material.SetVector("_ClockworkSourceDirection", new Vector4(direction.x,direction.y,direction.z,0));
                    }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.ambientLight = Color.Lerp(new Color(.44f,.46f,.65f),new Color(.64f,.59f,.50f),daylight) * .9f;
            Color fill = Color.Lerp(new Color(.15f,.15f,.24f),new Color(.13f,.105f,.075f),daylight);
            if (readabilityMaterials != null)
                for (int i = 0; i < readabilityMaterials.Length; i++)
                    if (readabilityMaterials[i] != null) readabilityMaterials[i].SetColor("_EmissionColor", readabilityBaseColors[i] * fill);
            if (dialGlass != null)
            {
                if (_dialBlock == null) _dialBlock = new MaterialPropertyBlock();
                dialGlass.GetPropertyBlock(_dialBlock);
                _dialBlock.SetColor("_DaylightTint", Color.Lerp(new Color(.28f,.23f,.51f), Color.Lerp(new Color(.70f,.80f,.83f),new Color(.88f,.69f,.45f),dusk), daylight));
                dialGlass.SetPropertyBlock(_dialBlock);
            }
            if (skyMaterial != null)
            {
                Color night = Color.Lerp(new Color(.006f,.012f,.035f),new Color(.025f,.014f,.09f),dream);
                skyMaterial.SetFloat("_NightAmount", 1f - daylight);
                skyMaterial.SetColor("_TopColor", Color.Lerp(night,new Color(.23f,.43f,.65f),daylight));
                skyMaterial.SetColor("_HorizonColor", Color.Lerp(Color.Lerp(new Color(.07f,.05f,.16f),new Color(.70f,.78f,.82f),daylight),twilightColor,twilight*.7f));
                skyMaterial.SetVector("_BodyDirection", new Vector4(direction.x,direction.y,direction.z,0));
                skyMaterial.SetColor("_BodyColor", body);
                skyMaterial.SetFloat("_BodyVisibility", .7f + .3f * altitude);
            }
        }
    }
}
