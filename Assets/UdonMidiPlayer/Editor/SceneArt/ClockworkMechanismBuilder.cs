using System;
using System.IO;
using System.Linq;
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
    public static class ClockworkMechanismBuilder
    {
        private const string Folder = ClockworkMaintenanceSceneBuilder.Folder;
        [Serializable] private class Mechanism
        {
            public Gear[] gears;
            public Motion[] motions;
            public float pendulumLength;
            public float crutchRatio;
            public int escapeTeeth;
            public int cycleBeats;
            public float[] dialCenter;
        }
        [Serializable] private class Motion { public string name; public float[] pivot; public float phase; }
        [Serializable] private class Gear : Motion
        {
            public int teeth;
            public int parent;
            public int parentContactTeeth;
            public int ratioNumerator;
            public int ratioDenominator;
            public float plane;
        }

        [MenuItem("Tools/Music MIDI Player/Scene Art/Upgrade Clockwork Mechanism and Daylight")]
        private static void UpgradeMenu() { Debug.Log(Upgrade()); }

        public static string Upgrade()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the clockwork second scene outside Play Mode.");
            Directory.CreateDirectory("RecoveryBackups/ClockworkMechanism-20261104");
            if (scene.isDirty)
                EditorSceneManager.SaveScene(scene, "RecoveryBackups/ClockworkMechanism-20261104/InMemory-" + DateTime.Now.Ticks + ".unity", true);
            EnsureProgram();
            ClockworkMaintenanceSceneBuilder.RefreshEnvironmentMaterials();
            var source = JsonUtility.FromJson<Mechanism>(File.ReadAllText(Folder + "/mechanism-manifest.json"));
            var art = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet");
            var model = art.transform.Find("BlenderEnvironment");
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var host = art.transform.Find("ClockworkMechanism");
            if (host == null)
            {
                host = new GameObject("ClockworkMechanism").transform;
                host.SetParent(art.transform, false);
            }
            var c = host.GetComponent<ClockworkSceneController>() ?? host.gameObject.AddUdonSharpComponent<ClockworkSceneController>();
            c.gears = source.gears.Select(g => Find(transforms, g.name)).ToArray();
            c.gearTeeth = source.gears.Select(g => g.teeth).ToArray();
            c.gearRatioNumerators = source.gears.Select(g => g.ratioNumerator).ToArray();
            c.gearRatioDenominators = source.gears.Select(g => g.ratioDenominator).ToArray();
            c.gearMeshPhases = source.gears.Select(g => g.phase).ToArray();
            c.gearRestRotations = c.gears.Select(Rest).ToArray();
            c.rotationAxis = c.gears[0].parent.InverseTransformDirection(Vector3.forward).normalized;
            c.hourHand = Find(transforms, "HourHand"); c.hourRest = Rest(c.hourHand);
            c.minuteHand = Find(transforms, "MinuteHand"); c.minuteRest = Rest(c.minuteHand);
            c.secondHand = Find(transforms, "SecondHand"); c.secondRest = Rest(c.secondHand);
            c.pendulum = Find(transforms, "Pendulum"); c.pendulumRest = Rest(c.pendulum);
            c.anchor = Find(transforms, "Anchor"); c.anchorRest = Rest(c.anchor);
            c.pendulumLength = source.pendulumLength; c.crutchRatio = source.crutchRatio;
            c.escapeTeeth = source.escapeTeeth; c.cycleBeats = source.cycleBeats;
            c.dialCenter = new Vector3(source.dialCenter[0],source.dialCenter[1],source.dialCenter[2]);
            c.celestialDistance = 38; c.glassTransmission = .68f;
            foreach (var t in c.gears.Concat(new[] {c.hourHand,c.minuteHand,c.secondHand,c.pendulum,c.anchor}))
            {
                if (t.parent != c.gears[0].parent) throw new InvalidOperationException("Moving FBX pivots must share the same parent space.");
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
            c.dialGlass = model.GetComponentsInChildren<Renderer>(true).Single(r => r.name.StartsWith("Clockwork_DialGlass__"));
            c.dialGlass.shadowCastingMode = ShadowCastingMode.Off;
            c.dialGlass.receiveShadows = false;
            c.dialGlass.sharedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
            if (lights.Length != 1 || lights[0].type != LightType.Point) throw new InvalidOperationException("Exactly one Point Light is required.");
            c.celestialLight = lights[0];
            c.celestialLight.range = 140;
            c.celestialLight.intensity = 4;
            c.celestialLight.lightmapBakeType = LightmapBakeType.Realtime;
            c.celestialLight.shadowBias = .02f;
            c.celestialLight.shadowNormalBias = .07f;
            c.celestialLight.shadowNearPlane = .4f;
            c.celestialLight.shadowStrength = 1;
            c.celestialLight.shadowResolution = LightShadowResolution.VeryHigh;
            string skyPath = Folder + "/Materials/Clockwork_LocalTimeSky.mat";
            c.skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (c.skyMaterial == null)
            {
                c.skyMaterial = new Material(Shader.Find("MusicMidiPlayer/Clockwork/Sky"));
                AssetDatabase.CreateAsset(c.skyMaterial, skyPath);
            }
            RenderSettings.skybox = c.skyMaterial;
            RenderSettings.sun = null; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            var cam = scene.GetRootGameObjects().Single(g => g.name == "Main Camera").GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(10.2f,4.4f,-11.4f);
            cam.transform.LookAt(new Vector3(0,6.9f,5));
            cam.fieldOfView = 54;
            ClockworkReadabilityBuilder.Configure(c);
            c.RefreshFromClientClock();
            foreach (var material in c.readabilityMaterials)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(c, ProxySerializationPolicy.All);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            EditorUtility.SetDirty(c); EditorUtility.SetDirty(backing);
            EditorUtility.SetDirty(c.skyMaterial); AssetDatabase.SaveAssetIfDirty(c.skyMaterial);
            ClockworkMaintenanceSceneBuilder.UpdateEnvironmentColliders();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Clockwork scene upgraded: 8 moving axles / 9 involute spur wheels + 30-tooth escapement; local client clock; supported pendulum; enclosed roof/walls; one celestial Point Light and single-pass translucent dial.";
        }

        private static Transform Find(Transform[] objects, string name)
        {
            return objects.Single(t => t.name == "CW_Moving_" + name);
        }
        private static Quaternion Rest(Transform t)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(t);
            return source == null ? t.localRotation : source.localRotation;
        }
        private static void EnsureProgram()
        {
            const string scriptPath = "Assets/UdonMidiPlayer/Runtime/Views/ClockworkSceneController.cs";
            const string programPath = "Assets/UdonMidiPlayer/Runtime/Views/ClockworkSceneController.asset";
            var program = UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(ClockworkSceneController));
            if (program == null)
            {
                program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
                if (program == null)
                {
                    program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                    program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                    AssetDatabase.CreateAsset(program, programPath);
                }
                AssetDatabase.ImportAsset(programPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions {IsEditorBuild=true});
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("Fix UdonSharp compilation before installing the mechanism.");
        }
    }
}
