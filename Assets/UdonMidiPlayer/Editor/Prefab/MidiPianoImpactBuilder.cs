using System;
using System.Collections.Generic;
using MusicMidiPlayer.Views;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Incremental piano-only presentation upgrade. No library/prefab apply or Light creation.</summary>
    public static class MidiPianoImpactBuilder
    {
        const string MaterialPath = "Assets/UdonMidiPlayer/Materials/PianoKeyImpact.mat";

        [MenuItem("Tools/Music MIDI Player/Presentation/Upgrade Piano Impacts in Current Scene")]
        public static void InstallLoadedScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("Use a saved scene outside Play mode.");
            string folder = "RecoveryBackups/PianoImpacts-20261104";
            System.IO.Directory.CreateDirectory(folder);
            if (!EditorSceneManager.SaveScene(scene, folder + "/" + scene.name + "-" + DateTime.Now.ToString("HHmmssfff") + ".unity", true))
                throw new InvalidOperationException("Could not preserve the current in-memory scene.");
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MidiWaterfallView view in root.GetComponentsInChildren<MidiWaterfallView>(true))
                    if (view.instrumentTarget == 0) { Configure(view); count++; }
            if (count == 0) throw new InvalidOperationException("No wired piano waterfall in this scene.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save piano presentation wiring.");
            Debug.Log("Updated " + count + " piano presentations in " + scene.path);
        }

        public static void Configure(MidiWaterfallView view)
        {
            if (view == null || view.instrumentTarget != 0 || view.keyboard == null || view.effects == null)
                throw new InvalidOperationException("A wired piano view is required.");
            MidiWaterfallEffects effects = view.effects;
            if (effects.hitParticles == null || effects.hitParticles.Length == 0 || effects.hitParticles[0] == null
                || effects.symbolParticles == null || effects.symbolParticles.Length == 0 || effects.symbolParticles[0] == null)
                throw new InvalidOperationException("Preserve the existing dust and symbol pools before upgrading.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader shader = Shader.Find("MusicMidiPlayer/PianoKeyImpact");
            if (shader == null) throw new InvalidOperationException("Import PianoKeyImpact.shader first.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;
            material.enableInstancing = false;
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);

            view.pianoImpactPresentation = true;
            view.keyboard.pianoImpactPresentation = true;
            effects.keyImpactRenderer = ImpactMesh(view, effects.transform, false, material);
            effects.keyImpactRootRenderer = ImpactMesh(view, effects.transform, true, material);

            ParticleSystem dust = effects.hitParticles[0], symbols = effects.symbolParticles[0];
            RemoveOtherPools(effects.hitParticles, dust);
            RemoveOtherPools(effects.symbolParticles, symbols);
            if (effects.ringParticles != null) RemoveOtherPools(effects.ringParticles, null);
            if (effects.glowTransforms != null)
                foreach (Transform old in effects.glowTransforms) if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            if (effects.hitLights != null)
                foreach (Light old in effects.hitLights) if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            effects.hitParticles = new[] { dust }; effects.symbolParticles = new[] { symbols };
            effects.ringParticles = new ParticleSystem[0]; effects.hitLights = new Light[0];
            effects.glowTransforms = new Transform[0]; effects.glowRenderers = new Renderer[0];
            effects.compactParticles = true; effects.symbolLayersEnabled = true; effects.effectsEnabled = true;
            effects.totalParticleBudget = 192; effects.totalSymbolBudget = 32;
            effects.maxParticlesPerFrame = 24; effects.maxHitsPerFrame = 128; effects.maxActiveLights = 0;
            effects.useRings = false;
            ConfigureCompactParticles(dust, false);
            ConfigureCompactParticles(symbols, true);
            EditorUtility.SetDirty(effects); EditorUtility.SetDirty(view); EditorUtility.SetDirty(view.keyboard);
            UdonSharpEditorUtility.CopyProxyToUdon(view.keyboard, ProxySerializationPolicy.RootOnly);
            UdonSharpEditorUtility.CopyProxyToUdon(effects, ProxySerializationPolicy.RootOnly);
            UdonSharpEditorUtility.CopyProxyToUdon(view, ProxySerializationPolicy.RootOnly);
        }

        static void RemoveOtherPools(ParticleSystem[] pools, ParticleSystem keep)
        {
            foreach (ParticleSystem pool in pools)
                if (pool != null && pool != keep) Undo.DestroyObjectImmediate(pool.gameObject);
        }

        static void ConfigureCompactParticles(ParticleSystem particles, bool symbols)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.maxParticles = symbols ? 32 : 160;
            main.startLifetime = symbols ? new ParticleSystem.MinMaxCurve(0.9f, 1.5f) : new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
            var collision = particles.collision; collision.enabled = false;
            var emission = particles.emission; emission.enabled = false;
            var trails = particles.trails; trails.enabled = false;
            var lights = particles.lights; lights.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.None;
            // One billboard system covers all six existing atlas glyphs, including clefs/rest.
            if (symbols)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sharedMaterial = MidiMusicSymbolAssets.EnsureMaterial();
                var sheet = particles.textureSheetAnimation;
                sheet.enabled = true; sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = 4; sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                // Start-frame curves use normalized sheet coordinates: six of eight tiles.
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.7499f);
                sheet.cycleCount = 1;
                sheet.uvChannelMask = UVChannelFlags.UV0;
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            }
            EditorUtility.SetDirty(particles); EditorUtility.SetDirty(renderer);
        }

        static Renderer ImpactMesh(MidiWaterfallView view, Transform host, bool root, Material material)
        {
            string name = root ? "PerKeyRearImpacts" : "PerKeyImpacts";
            Transform child = host.Find(name);
            if (child == null) { var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Piano impact mesh"); child = go.transform; child.SetParent(host, false); }
            child.localPosition = Vector3.zero; child.localRotation = Quaternion.identity; child.localScale = Vector3.one;
            child.gameObject.layer = view.gameObject.layer;
            MeshFilter filter = child.GetComponent<MeshFilter>(); if (filter == null) filter = Undo.AddComponent<MeshFilter>(child.gameObject);
            MeshRenderer renderer = child.GetComponent<MeshRenderer>(); if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
            // Each existing model has its own baked landing heights and widths. Preserve GUIDs on reruns.
            string meshPath = "Assets/UdonMidiPlayer/Meshes/PianoImpacts_" + view.transform.parent.parent.name + (root ? "_Rear" : "_Front") + ".asset";
            Mesh generated = BuildMesh(view, root);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = generated; AssetDatabase.CreateAsset(mesh, meshPath); }
            else { EditorUtility.CopySerialized(generated, mesh); UnityEngine.Object.DestroyImmediate(generated); }
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            filter.sharedMesh = mesh; renderer.sharedMaterial = material; renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(filter); EditorUtility.SetDirty(renderer);
            return renderer;
        }

        static Mesh BuildMesh(MidiWaterfallView view, bool root)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
            var pitchData = new List<Vector3>(); var centers = new List<Vector3>();
            Vector3[] positions = root ? view.keyRootHitPositions : view.keyHitPositions;
            if (positions == null || positions.Length != 88) throw new InvalidOperationException("Piano landing positions are missing.");
            for (int pitch = 0; pitch < 128; pitch++)
            {
                int key = pitch - 21; while (key < 0) key += 12; while (key >= 88) key -= 12;
                Vector3 landing = positions[key]; int octaves = (pitch - 21 - key) / 12;
                if (octaves != 0)
                {
                    int neighbor = octaves < 0 ? key + 12 : key - 12;
                    float octave = octaves < 0 ? positions[neighbor].x - landing.x : landing.x - positions[neighbor].x;
                    landing.x += octave * octaves;
                }
                float width = view.keyWidth[key] * 0.96f;
                for (int layer = 0; layer < 2; layer++)
                {
                    int start = vertices.Count;
                    Vector3 center = landing + new Vector3(0, 0.001f, root ? -0.018f : 0.006f);
                    for (int j = 0; j < 4; j++)
                    {
                        float x = j == 0 || j == 3 ? -0.5f : 0.5f;
                        float y = j < 2 ? 0f : 1f;
                        Vector3 offset = layer == 0 ? new Vector3(x * width, 0, (y - 0.5f) * 0.075f)
                            : new Vector3(x * width * 0.92f, y * 0.075f, -0.002f);
                        vertices.Add(center + offset); centers.Add(center);
                        uv.Add(new Vector2(x + 0.5f, y));
                        pitchData.Add(new Vector3(pitch, view.keyIsBlack[key] ? 1f : 0f, layer));
                    }
                    indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
            }
            var mesh = new Mesh { name = "Piano independent pitch impacts" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1, pitchData); mesh.SetUVs(2, centers);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
