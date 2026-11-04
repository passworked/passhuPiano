using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.SceneArt
{
    public static class ClockworkMusicLinkBuilder
    {
        private const string Folder = "Assets/Scenes/ClockworkMaintenanceStreet/MusicLink";

        [MenuItem("Tools/Music MIDI Player/Scene Art/Install Clockwork Music Constellation")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Open the clockwork second scene outside Play Mode and baking.");
            var roots = scene.GetRootGameObjects();
            var facade = roots.SelectMany(x => x.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            var clock = roots.SelectMany(x => x.GetComponentsInChildren<ClockworkSceneController>(true)).Single();
            if (clock.gears.Length < 8) throw new InvalidOperationException("The existing clockwork mechanism is required.");
            Directory.CreateDirectory("RecoveryBackups/ClockworkMusicLink-20261104");
            EditorSceneManager.SaveScene(scene, "RecoveryBackups/ClockworkMusicLink-20261104/InMemory-" + DateTime.Now.Ticks + ".unity", true);
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            EnsureProgram();
            var host = clock.transform.Find("MusicConstellation");
            if (host == null) { host = new GameObject("MusicConstellation").transform; host.SetParent(clock.transform, false); }
            var link = host.GetComponent<ClockworkMusicLink>();
            if (link == null) link = host.gameObject.AddUdonSharpComponent<ClockworkMusicLink>();
            link.facade = facade; link.mechanism = clock;
            link.sources = new Transform[10];
            for (int i = 0; i < facade.instrumentRoots.Length && i < 9; i++)
            {
                if (facade.instrumentRoots[i] == null) continue;
                link.sources[i] = facade.instrumentRoots[i].transform;
                if (i < facade.instrumentKeyboards.Length && facade.instrumentKeyboards[i] != null)
                    link.sources[i] = facade.instrumentKeyboards[i].transform;
                if (i < facade.instrumentStrings.Length && facade.instrumentStrings[i] != null && facade.instrumentStrings[i].contacts.Length > 0)
                    link.sources[i] = facade.instrumentStrings[i].contacts[0];
            }
            if (facade.drumView != null) link.sources[9] = facade.drumView.transform;
            link.gearRadii = clock.gearTeeth.Select(t => t * .16f * .5f).ToArray();
            link.dialRadius = 5.3f;
            link.sparks = Particles(host, "ConnectingStardust", false);
            link.ripples = Particles(host, "GearAndDialRipples", true);
            facade.clockworkMusicLink = link; facade.sequencer.clockworkMusicLink = link;
            ClockworkMusicalSealBuilder.Configure(link);
            clock.dreamMode = true;
            clock.RefreshFromClientClock(); // Refresh the civil anchor without resetting either driver's phase.
            foreach (var proxy in new UdonSharpBehaviour[] { link, facade, facade.sequencer, clock })
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(proxy);
                EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy));
                PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            }
            UdonSharpEditorUtility.GetBackingUdonBehaviour(link).SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            Debug.Log(Validate());
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureProgram()
        {
            const string programPath = "Assets/UdonMidiPlayer/Runtime/Views/ClockworkMusicLink.asset";
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
            if (program == null)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/UdonMidiPlayer/Runtime/Views/ClockworkMusicLink.cs");
                program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(program, programPath);
            }
            AssetDatabase.ImportAsset(programPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = true });
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("Fix UdonSharp compilation before installing music links.");
        }

        private static ParticleSystem Particles(Transform parent, string name, bool ring)
        {
            var child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            var ps = child.GetComponent<ParticleSystem>();
            if (ps == null) ps = child.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true; main.duration = 5;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 18;
            main.startLifetime = ring ? .65f : 12f;
            main.startSpeed = 0; main.startSize = .12f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.2f, .2f);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var collision = ps.collision; collision.enabled = false;
            var lights = ps.lights; lights.enabled = false;
            var trails = ps.trails; trails.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = ring;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .4f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime; size.enabled = false;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = .25f;
            renderer.minParticleSize = .008f;
            string path = Folder + "/" + (ring ? "ConstellationRing" : "ConstellationSpark") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("MusicMidiPlayer/Clockwork/MusicParticle")); AssetDatabase.CreateAsset(material, path); }
            string texturePath = Folder + "/Textures/" + (ring ? "KenneyTrace" : "KenneyStar") + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.maxTextureSize = 128; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new InvalidOperationException("Particle silhouette did not finish importing: " + texturePath);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_Intensity", 1);
            material.SetFloat("_Butterfly", 1);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            renderer.sharedMaterial = material; EditorUtility.SetDirty(material);
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            return ps;
        }

        public static string Validate()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var lights = roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).ToArray();
            if (lights.Length != 1 || lights[0].type != LightType.Point) throw new InvalidOperationException("Exactly one Point Light including inactive objects is required.");
            var link = roots.SelectMany(x => x.GetComponentsInChildren<ClockworkMusicLink>(true)).Single();
            if (link.facade.clockworkMusicLink != link || link.facade.sequencer.clockworkMusicLink != link || !link.mechanism.dreamMode)
                throw new InvalidOperationException("Incomplete music constellation wiring.");
            foreach (var system in new[] { link.sparks, link.ripples })
            {
                if (system == null || system.lights.enabled || system.collision.enabled || system.emission.enabled)
                    throw new InvalidOperationException("Music particle systems must use manual, unlit, collision-free emission.");
                if (system.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetTexture("_MainTex") == null)
                    throw new InvalidOperationException("A particle silhouette texture is missing.");
                if (system.main.maxParticles != 18 || system.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetFloat("_Butterfly") != 1)
                    throw new InvalidOperationException("Music links require bounded animated butterfly particles.");
            }
            if (link.sparks.colorOverLifetime.enabled || link.sparks.sizeOverLifetime.enabled)
                throw new InvalidOperationException("Travelling butterflies must remain visible for the complete flight.");
            foreach (var go in roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go.gameObject) != 0) throw new InvalidOperationException("Missing script: " + go.name);
            if (link.ritualNodes.Length != 9 || link.ritualNodes.Any(x => x == null)) throw new InvalidOperationException("Nine musical seal nodes are required.");
            return "Music constellation connected: file/live palettes; 18 continuous butterfly flights + 18 arrivals / 2 particle renderers / 9 layered shared-mesh seals; half-radius piano with independent ink width; exactly 1 Point Light.";
        }
    }
}
