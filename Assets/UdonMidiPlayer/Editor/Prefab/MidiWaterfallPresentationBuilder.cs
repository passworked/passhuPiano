using System;
using System.Collections.Generic;
using MusicMidiPlayer.Editor.Import;
using MusicMidiPlayer.Views;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Idempotent, local presentation setup. Does not create a scene, change a piano model, or bake lighting.</summary>
    public static class MidiWaterfallPresentationBuilder
    {
        public const string EffectsObjectName = "WaterfallEffects";
        public const string ReflectionProbeObjectName = "WaterfallEnvironment";
        public const string EnvironmentTexturePath = "Assets/UdonMidiPlayer/Art/Waterfall/WaterfallEnvironment.exr";
        public const string NoteMeshPath = "Assets/UdonMidiPlayer/Meshes/WaterfallCrystal.asset";
        public const string NoteMaterialPath = "Assets/UdonMidiPlayer/Materials/WaterfallVolume.mat";
        private const string GlowMeshPath = "Assets/UdonMidiPlayer/Meshes/WaterfallImpactQuad.asset";
        private const string SparkMaterialPath = "Assets/UdonMidiPlayer/Materials/WaterfallSoftSpark.mat";
        private const string GlowMaterialPath = "Assets/UdonMidiPlayer/Materials/WaterfallImpactGlow.mat";

        public static void Build(MidiWaterfallView view)
        {
            if (view == null || view.barTransforms == null || view.barRenderers == null || view.barTransforms.Length != view.barRenderers.Length)
                throw new InvalidOperationException("体积瀑布流需要现有的条带Transform/Renderer数组");
            if (view.keyX == null || view.keyX.Length != 88 || view.keyWidth == null || view.keyWidth.Length != 88)
                throw new InvalidOperationException("请先烘焙88键真实布局，再配置瀑布流表现");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式配置瀑布流表现");
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Meshes");
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Materials");
            Mesh noteMesh = CrystalMesh();
            Mesh glowMesh = GlowQuad();
            Material volume = MaterialAsset(NoteMaterialPath, "MusicMidiPlayer/WaterfallVolume");
            Material spark = MaterialAsset(SparkMaterialPath, "MusicMidiPlayer/WaterfallSoftSpark");
            Material glow = MaterialAsset(GlowMaterialPath, "MusicMidiPlayer/WaterfallImpactGlow");
            view.volumeNoteMaterial = volume;
            view.noteEmissionIntensity = 3f;
            volume.renderQueue = (int)RenderQueue.Geometry;
            volume.enableInstancing = true;
            volume.SetFloat("_Saturation", 1.25f);
            volume.SetFloat("_Metallic", 0.12f);
            volume.SetFloat("_Smoothness", 0.91f);
            volume.SetFloat("_EdgeStrength", 0.23f);
            volume.SetFloat("_BodyGlow", 0.09f);
            volume.SetFloat("_FacetStrength", 0.38f);
            volume.SetFloat("_EndBevelRatio", 0.12f);
            EditorUtility.SetDirty(volume); AssetDatabase.SaveAssetIfDirty(volume);
            view.barDepth = 0.008f;
            view.blackBarZ = -0.009f;
            BakeKeyHitPositions(view);
            for (int i = 0; i < view.barTransforms.Length; i++)
            {
                Transform bar = view.barTransforms[i];
                MeshRenderer renderer = view.barRenderers[i] as MeshRenderer;
                if (bar == null || renderer == null || renderer.transform != bar)
                    throw new InvalidOperationException("条带槽需要同对象MeshRenderer: " + i);
                MeshFilter filter = bar.GetComponent<MeshFilter>();
                if (filter == null) filter = Undo.AddComponent<MeshFilter>(bar.gameObject);
                filter.sharedMesh = noteMesh;
                renderer.sharedMaterial = volume;
                bar.gameObject.layer = LayerMask.NameToLayer("MidiNotes");
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;
                EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer);
            }
            Transform host = Child(view.transform, EffectsObjectName);
            host.localPosition = Vector3.zero; host.localRotation = Quaternion.identity; host.localScale = Vector3.one;
            MidiWaterfallEffects effects = host.GetComponent<MidiWaterfallEffects>();
            if (effects == null)
            {
                if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(MidiWaterfallEffects)) == null)
                    throw new InvalidOperationException("请先通过EnsurePrograms编译MidiWaterfallEffects程序资产");
                effects = host.gameObject.AddUdonSharpComponent<MidiWaterfallEffects>();
            }
            view.effects = effects; effects.waterfall = view; effects.keyboard = view.keyboard;
            effects.effectsEnabled = true;
            effects.hitParticles = new ParticleSystem[12];
            effects.symbolParticles = new ParticleSystem[12];
            effects.symbolLayersEnabled = true; effects.totalSymbolBudget = 192;
            effects.hitLights = new Light[8];
            effects.glowTransforms = new Transform[12];
            effects.glowRenderers = new Renderer[12];
            effects.ringParticles = new ParticleSystem[0];
            effects.maxParticlesPerFrame = 64; effects.totalParticleBudget = 768; effects.maxHitsPerFrame = 24;
            effects.maxActiveLights = 0; effects.maxLightIntensity = 1.1f;
            effects.lightRange = 1.25f; effects.lightDecaySeconds = 0.28f;
            float left = view.keyX[0] - view.keyWidth[0] * 0.5f;
            float right = view.keyX[87] + view.keyWidth[87] * 0.5f;
            float width = Mathf.Max(0.1f, right - left);
            for (int i = 0; i < 12; i++)
            {
                float x = Mathf.Lerp(left, right, (i + 0.5f) / 12f);
                Transform particleHost = Child(host, "HitDust_" + i.ToString("00"));
                particleHost.localPosition = new Vector3(x, 0.015f, -0.012f);
                particleHost.localRotation = Quaternion.Euler(-90f, 0, 0);
                ParticleSystem particles = particleHost.GetComponent<ParticleSystem>();
                if (particles == null) particles = Undo.AddComponent<ParticleSystem>(particleHost.gameObject);
                ConfigureParticles(particles, spark, width / 12f);
                effects.hitParticles[i] = particles;
                Transform symbolHost = Child(host, "MusicSymbols_" + i.ToString("00"));
                symbolHost.localPosition = new Vector3(x, 0.02f, -0.018f);
                symbolHost.localRotation = Quaternion.Euler(-90f, 0, 0);
                ParticleSystem symbols = symbolHost.GetComponent<ParticleSystem>();
                if (symbols == null) symbols = Undo.AddComponent<ParticleSystem>(symbolHost.gameObject);
                ConfigureSymbols(symbols, width / 12f, i);
                effects.symbolParticles[i] = symbols;
                Transform glowHost = Child(host, "KeyGlow_" + i.ToString("00"));
                glowHost.gameObject.SetActive(true);
                glowHost.localPosition = new Vector3(x, -0.0005f, 0.065f);
                glowHost.localRotation = Quaternion.Euler(90f, 0, 0);
                glowHost.localScale = new Vector3(0.12f, 0.14f, 1f);
                MeshFilter glowFilter = Component<MeshFilter>(glowHost.gameObject); glowFilter.sharedMesh = glowMesh;
                MeshRenderer glowRenderer = Component<MeshRenderer>(glowHost.gameObject);
                glowRenderer.sharedMaterial = glow; glowRenderer.enabled = false;
                glowRenderer.shadowCastingMode = ShadowCastingMode.Off; glowRenderer.receiveShadows = false;
                glowRenderer.lightProbeUsage = LightProbeUsage.Off; glowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                effects.glowTransforms[i] = glowHost; effects.glowRenderers[i] = glowRenderer;
            }
            for (int i = 0; i < 8; i++)
            {
                Transform lightHost = Child(host, "ImpactLight_" + i.ToString("00"));
                lightHost.localPosition = new Vector3(Mathf.Lerp(left, right, (i + 0.5f) / 8f), 0.16f, -0.10f);
                Light light = Component<Light>(lightHost.gameObject);
                light.type = LightType.Point; light.range = effects.lightRange;
                light.intensity = 0; light.enabled = false; light.color = new Color(0.2f, 0.65f, 1);
                light.shadows = LightShadows.None; light.renderMode = LightRenderMode.Auto;
                light.bounceIntensity = 0.25f;
                light.cullingMask = ~LayerMask.GetMask("UI", "UiMenu", "InternalUI", "MidiNotes");
                effects.hitLights[i] = light;
            }
            ConfigureReflection(host, width, view.viewHeight);
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(effects);
            UdonSharpEditorUtility.CopyProxyToUdon(effects, ProxySerializationPolicy.All);
            UdonSharpEditorUtility.CopyProxyToUdon(view, ProxySerializationPolicy.RootOnly);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(effects);
            if (backing != null) { backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(backing); }
        }

        public static void BakeKeyHitPositions(MidiWaterfallView view)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式烘焙音符落点");
            if (view == null || view.keyX == null || view.keyX.Length != 88
                || view.keyIsBlack == null || view.keyIsBlack.Length != 88
                || view.keyboard == null || view.keyboard.keyRenderers == null || view.keyboard.keyRenderers.Length != 88)
                throw new InvalidOperationException("音符落点需要已绑定的88键布局和Renderer");
            Vector3[] positions = new Vector3[88];
            Vector3[] roots = new Vector3[88];
            Transform model = null;
            bool marimba = false;
            for (Transform parent = view.transform.parent; parent != null && model == null; parent = parent.parent)
            {
                model = parent.Find("MarimbaModel");
                marimba = model != null;
                if (model == null) model = parent.Find("PianoModel");
            }
            float rearZ = float.NegativeInfinity;
            foreach (Renderer renderer in view.keyboard.keyRenderers)
                if (renderer != null)
                    rearZ = Mathf.Max(rearZ, MidiPianoModelBinder.BoundsIn(renderer, view.transform).max.z);
            if (float.IsNegativeInfinity(rearZ)) throw new InvalidOperationException("键盘后沿落点缺少琴键Renderer");
            float inset = Mathf.Max(0.001f, view.barDepth) * 0.5f + 0.002f;
            // Root means the common rear edge of the entire keyboard/bar layout, in waterfall space.
            // Keep each pitch's X and landing height; every pitch shares this rear Z.
            rearZ -= inset;
            for (int key = 0; key < 88; key++)
            {
                positions[key] = new Vector3(view.keyX[key], 0f, 0f);
                Renderer renderer = view.keyboard.keyRenderers[key];
                if (renderer == null && marimba) continue;
                if (renderer == null) throw new InvalidOperationException("音符落点缺少琴键Renderer: " + (key + 21));
                Bounds local = MidiPianoModelBinder.BoundsIn(renderer, view.transform);
                roots[key] = new Vector3(local.center.x, local.max.y + 0.002f, rearZ);
                if (marimba)
                {
                    positions[key] = new Vector3(local.center.x, local.max.y + 0.002f,
                        Mathf.Min(local.max.z, local.min.z + 0.018f));
                    continue;
                }
                // Land just above the raised black key, with the full bar depth inside its front edge.
                if (!view.keyIsBlack[key]) continue;
                positions[key] = new Vector3(local.center.x, local.max.y + 0.002f,
                    Mathf.Min(local.max.z, local.min.z + inset));
            }
            if (marimba)
                for (int key = 0; key < 88; key++)
                {
                    if (view.keyboard.keyRenderers[key] != null) continue;
                    // Extend the nearest physical bar of the same row without a Z/height jump.
                    int nearest = -1;
                    for (int candidate = 0; candidate < 88; candidate++)
                        if (view.keyboard.keyRenderers[candidate] != null
                            && view.keyIsBlack[candidate] == view.keyIsBlack[key]
                            && (nearest < 0 || Mathf.Abs(candidate - key) < Mathf.Abs(nearest - key))) nearest = candidate;
                    if (nearest < 0) throw new InvalidOperationException("马林巴落点缺少对应排琴条");
                    positions[key] = new Vector3(view.keyX[key], positions[nearest].y, positions[nearest].z);
                    roots[key] = new Vector3(view.keyX[key], roots[nearest].y, rearZ);
                }
            view.keyHitPositions = positions;
            view.keyRootHitPositions = roots;
            EditorUtility.SetDirty(view);
        }

        internal static void ConfigureParticles(ParticleSystem particles, Material material, float zoneWidth)
        {
            if (material.HasProperty("_Intensity") && !Mathf.Approximately(material.GetFloat("_Intensity"), 1.8f))
            {
                material.SetFloat("_Intensity", 1.8f);
                EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            }
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.prewarm = false;
            main.duration = 2f; main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.simulationSpeed = 1; main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.2f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2f);
            main.startColor = Color.white; main.gravityModifier = 0;
            main.stopAction = ParticleSystemStopAction.None;
            var emission = particles.emission; emission.enabled = false; emission.rateOverTime = 0; emission.SetBursts(new ParticleSystem.Burst[0]);
            var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22; shape.radius = Mathf.Clamp(zoneWidth * 0.20f, 0.008f, 0.03f); shape.radiusThickness = 0.9f;
            ConfigureLifeCurves(particles, false);
            ConfigureUpwardVelocity(particles, false);
            var rotation = particles.rotationOverLifetime; rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
            ConfigureStaticCollision(particles);
            var particleLights = particles.lights; particleLights.enabled = false;
            var trails = particles.trails; trails.enabled = false;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material; renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(particles); EditorUtility.SetDirty(renderer);
        }

        internal static void ConfigureSymbols(ParticleSystem particles, float zoneWidth, int variant)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = false; main.prewarm = false;
            main.duration = 2f; main.maxParticles = 16;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.simulationSpeed = 1;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.085f);
            main.startSpeed = 0; main.gravityModifier = 0; main.startColor = Color.white;
            main.startRotation3D = false; main.startRotation = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);
            main.stopAction = ParticleSystemStopAction.None;
            var emission = particles.emission; emission.enabled = false; emission.rateOverTime = 0; emission.SetBursts(new ParticleSystem.Burst[0]);
            var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Clamp(zoneWidth * 0.13f, 0.006f, 0.02f); shape.radiusThickness = 1;
            ConfigureLifeCurves(particles, true);
            ConfigureUpwardVelocity(particles, true);
            var rotation = particles.rotationOverLifetime; rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.16f, 0.16f);
            ConfigureStaticCollision(particles);
            var particleLights = particles.lights; particleLights.enabled = false;
            var trails = particles.trails; trails.enabled = false;
            MidiMusicSymbolAssets.ConfigureRenderer(particles, variant);
            EditorUtility.SetDirty(particles);
        }

        private static void ConfigureLifeCurves(ParticleSystem particles, bool symbols)
        {
            var color = particles.colorOverLifetime; color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, symbols ? 0.14f : 0.10f),
                    new GradientAlphaKey(0.75f, 0.55f), new GradientAlphaKey(0.25f, 0.82f), new GradientAlphaKey(0, 1) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = particles.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, symbols
                ? new AnimationCurve(new Keyframe(0, 0.55f), new Keyframe(0.18f, 1), new Keyframe(0.75f, 0.9f), new Keyframe(1, 0.5f))
                : new AnimationCurve(new Keyframe(0, 0.2f), new Keyframe(0.18f, 1), new Keyframe(0.65f, 0.65f), new Keyframe(1, 0.05f)));
        }

        private static void ConfigureUpwardVelocity(ParticleSystem particles, bool symbols)
        {
            // Host local Z is rotated toward piano/world up. All vertical curve keys remain positive.
            var velocity = particles.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            float spreadX = symbols ? 0.035f : 0.09f;
            float spreadY = symbols ? 0.025f : 0.055f;
            velocity.x = SymmetricSpread(spreadX); velocity.y = SymmetricSpread(spreadY);
            AnimationCurve upCurve = symbols
                ? new AnimationCurve(new Keyframe(0, 0.27f), new Keyframe(0.15f, 0.24f), new Keyframe(0.55f, 0.15f), new Keyframe(1, 0.07f))
                : new AnimationCurve(new Keyframe(0, 0.62f), new Keyframe(0.12f, 0.50f), new Keyframe(0.40f, 0.22f), new Keyframe(0.78f, 0.10f), new Keyframe(1, 0.06f));
            velocity.z = new ParticleSystem.MinMaxCurve(1, upCurve, upCurve);
            velocity.radial = 0; velocity.orbitalX = 0; velocity.orbitalY = 0; velocity.orbitalZ = 0; velocity.speedModifier = 1;
            var limit = particles.limitVelocityOverLifetime; limit.enabled = true;
            limit.space = ParticleSystemSimulationSpace.Local; limit.separateAxes = false; limit.dampen = 0.75f;
            limit.limit = new ParticleSystem.MinMaxCurve(1, symbols
                ? new AnimationCurve(new Keyframe(0, 0.34f), new Keyframe(0.55f, 0.24f), new Keyframe(1, 0.12f))
                : new AnimationCurve(new Keyframe(0, 0.75f), new Keyframe(0.18f, 0.65f), new Keyframe(0.65f, 0.30f), new Keyframe(1, 0.15f)));
            limit.drag = 0; limit.multiplyDragByParticleSize = false; limit.multiplyDragByParticleVelocity = false;
            var force = particles.forceOverLifetime; force.enabled = false;
            var noise = particles.noise; noise.enabled = false;
            var inherit = particles.inheritVelocity; inherit.enabled = false;
        }

        private static ParticleSystem.MinMaxCurve SymmetricSpread(float peak)
        {
            AnimationCurve minimum = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.14f, -peak), new Keyframe(0.55f, -peak * 0.65f), new Keyframe(1, -peak * 0.17f));
            AnimationCurve maximum = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.14f, peak), new Keyframe(0.55f, peak * 0.65f), new Keyframe(1, peak * 0.17f));
            return new ParticleSystem.MinMaxCurve(1, minimum, maximum);
        }

        private static void ConfigureStaticCollision(ParticleSystem particles)
        {
            var collision = particles.collision; collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World; collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium; collision.enableDynamicColliders = false;
            collision.sendCollisionMessages = false; collision.collidesWith = ~LayerMask.GetMask("Player", "PlayerLocal", "UI", "UiMenu", "InternalUI");
            collision.dampen = 0.28f; collision.bounce = 0.22f; collision.lifetimeLoss = 0.22f;
            collision.radiusScale = 0.4f; collision.colliderForce = 0;
        }

        private static void ConfigureReflection(Transform host, float keyboardWidth, float viewHeight)
        {
            Transform holder = Child(host, ReflectionProbeObjectName);
            holder.localPosition = new Vector3(0, Mathf.Max(0.6f, viewHeight * 0.36f), 0.40f);
            ReflectionProbe probe = Component<ReflectionProbe>(holder.gameObject);
            Texture environment = AssetDatabase.LoadAssetAtPath<Texture>(EnvironmentTexturePath);
            if (environment != null && environment.dimension == TextureDimension.Cube) probe.customBakedTexture = environment;
            probe.mode = probe.customBakedTexture != null ? ReflectionProbeMode.Custom : ReflectionProbeMode.Baked;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128; probe.intensity = 0.75f; probe.boxProjection = true; probe.blendDistance = 0.35f;
            probe.size = new Vector3(Mathf.Max(3.4f, keyboardWidth + 2), Mathf.Max(3.2f, viewHeight + 1.2f), 3.6f);
            probe.center = Vector3.zero; probe.nearClipPlane = 0.05f; probe.farClipPlane = 25f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = ~LayerMask.GetMask("UI", "UiMenu", "InternalUI", "MidiNotes");
            EditorUtility.SetDirty(probe);
        }

        private static Mesh CrystalMesh()
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var tangents = new List<Vector4>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector3 inset = new Vector3(0.38f, 0.465f, 0.38f);
            for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int a = (axis + 1) % 3, b = (axis + 2) % 3;
                    Vector3[] face = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        face[i][axis] = sign * 0.5f;
                        face[i][a] = (i == 0 || i == 3 ? -1 : 1) * inset[a];
                        face[i][b] = (i < 2 ? -1 : 1) * inset[b];
                    }
                    Vector3 outward = Vector3.zero; outward[axis] = sign;
                    AddFace(face, outward, vertices, normals, tangents, uv, triangles);
                }
            for (int along = 0; along < 3; along++)
                for (int sa = -1; sa <= 1; sa += 2)
                    for (int sb = -1; sb <= 1; sb += 2)
                    {
                        int a = (along + 1) % 3, b = (along + 2) % 3;
                        Vector3[] face = new Vector3[4];
                        for (int i = 0; i < 4; i++)
                        {
                            face[i][along] = (i == 0 || i == 3 ? -1 : 1) * inset[along];
                            face[i][a] = sa * (i < 2 ? 0.5f : inset[a]);
                            face[i][b] = sb * (i < 2 ? inset[b] : 0.5f);
                        }
                        Vector3 outward = Vector3.zero; outward[a] = sa; outward[b] = sb;
                        AddFace(face, outward, vertices, normals, tangents, uv, triangles);
                    }
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3[] face = { new Vector3(x * 0.5f, y * inset.y, z * inset.z), new Vector3(x * inset.x, y * 0.5f, z * inset.z), new Vector3(x * inset.x, y * inset.y, z * 0.5f) };
                        AddFace(face, new Vector3(x,y,z), vertices, normals, tangents, uv, triangles);
                    }
            Mesh mesh = MeshAsset(NoteMeshPath);
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTangents(tangents); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if ((mesh.bounds.size - Vector3.one).sqrMagnitude > 0.000001f) throw new InvalidOperationException("音符晶体Mesh须保持unit bounds=1");
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); return mesh;
        }

        private static void AddFace(Vector3[] face, Vector3 outward, List<Vector3> vertices, List<Vector3> normals,
            List<Vector4> tangents, List<Vector2> uv, List<int> triangles)
        {
            Vector3 normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]).normalized;
            if (Vector3.Dot(normal, outward) < 0) { Array.Reverse(face); normal = -normal; }
            Vector3 tangent = (face[1] - face[0]).normalized;
            int start = vertices.Count;
            foreach (Vector3 point in face)
            {
                vertices.Add(point); normals.Add(normal); tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1));
                uv.Add(new Vector2(point.x + 0.5f, point.y + 0.5f));
            }
            for (int i = 1; i < face.Length - 1; i++) { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }

        private static Mesh GlowQuad()
        {
            Mesh mesh = MeshAsset(GlowMeshPath); mesh.Clear();
            mesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0), new Vector3(0.5f,0.5f,0), new Vector3(-0.5f,0.5f,0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0,2,1,0,3,2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); return mesh;
        }

        private static Mesh MeshAsset(string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(mesh, path); }
            return mesh;
        }
        private static Material MaterialAsset(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("找不到瀑布Shader，请先完成资源导入: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) }; material.enableInstancing = true; AssetDatabase.CreateAsset(material, path); }
            else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            AssetDatabase.SaveAssetIfDirty(material); return material;
        }
        private static Transform Child(Transform parent, string name)
        {
            Transform found = parent.Find(name); if (found != null) return found;
            GameObject created = new GameObject(name); Undo.RegisterCreatedObjectUndo(created, "Create MIDI Waterfall Effects"); created.transform.SetParent(parent, false); return created.transform;
        }
        private static T Component<T>(GameObject host) where T : UnityEngine.Component
        { T component = host.GetComponent<T>(); return component != null ? component : Undo.AddComponent<T>(host); }
    }
}
