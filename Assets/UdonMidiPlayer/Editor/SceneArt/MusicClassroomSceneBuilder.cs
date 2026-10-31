using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>
    /// Imports the Blender classroom around the existing main-scene player.
    /// No player builder, Udon bake, library migration or prefab apply is invoked.
    /// </summary>
    public static class MusicClassroomSceneBuilder
    {
        public const string Folder = "Assets/Scenes/DemoMusicClassroom";
        public const string ModelPath = Folder + "/Models/JapaneseMusicClassroom.fbx";
        public const string RootName = "DemoMusicClassroom";
        private const string MainScene = "Assets/Scenes/VRCDefaultWorldScene.unity";

        [Serializable] private sealed class Manifest
        {
            public MaterialSpec[] materials;
            public ColliderSpec[] colliders;
            public int meshes;
            public int triangles;
        }
        [Serializable] private sealed class MaterialSpec
        {
            public string name;
            public float[] color;
            public float smoothness;
            public float metallic;
            public string texture;
            public float emission;
            public bool glass;
        }
        [Serializable] private sealed class ColliderSpec
        {
            public string name;
            public float[] center;
            public float[] size;
            public float yaw;
        }

        [MenuItem("Tools/Music MIDI Player/Scene Art/Install Blender Music Classroom")]
        private static void InstallFromMenu() { Debug.Log(Install()); }

        public static string PlayerFingerprint()
        {
            var scene = SceneManager.GetActiveScene();
            var player = scene.GetRootGameObjects().Single(g => g.name == "UdonMidiPlayer");
            var text = new StringBuilder();
            foreach (var component in player.GetComponentsInChildren<Component>(true)
                .Where(c => c != null).OrderBy(c => c.GetInstanceID()))
            {
                text.Append(component.GetInstanceID()).Append('|')
                    .Append(EditorJsonUtility.ToJson(component)).Append('\n');
            }
            // Include GameObject activation, names, tags and layers as well as components.
            foreach (var t in player.GetComponentsInChildren<Transform>(true)
                .OrderBy(t => t.GetInstanceID()))
                text.Append(t.gameObject.GetInstanceID()).Append('|')
                    .Append(EditorJsonUtility.ToJson(t.gameObject)).Append('\n');
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))
                    .Replace("-", "");
        }

        public static string Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing classroom art.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScene)
                throw new InvalidOperationException("Open the existing VRCDefaultWorldScene first.");
            if (scene.GetRootGameObjects().Any(g => g.name == RootName))
                throw new InvalidOperationException("Classroom already installed. Reimport its FBX to update geometry.");
            var before = PlayerFingerprint();
            var data = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/classroom-manifest.json"));
            Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var surface = Shader.Find("MusicMidiPlayer/Demo/ClassroomSurface");
            if (surface == null) throw new InvalidOperationException("Classroom surface shader is missing.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.generateSecondaryUV = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var spec in data.materials)
            {
                var path = Folder + "/Materials/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(spec.glass ? Shader.Find("Standard") : surface);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = spec.glass ? Shader.Find("Standard") : surface;
                material.color = new Color(spec.color[0], spec.color[1], spec.color[2],
                    spec.glass ? .10f : 1);
                material.SetFloat("_Metallic", spec.metallic);
                material.SetFloat(spec.glass ? "_Glossiness" : "_Smoothness", spec.smoothness);
                if (!string.IsNullOrEmpty(spec.texture))
                {
                    var texturePath = Folder + "/Textures/" + spec.texture;
                    var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    textureImporter.wrapMode = TextureWrapMode.Repeat;
                    textureImporter.anisoLevel = 4;
                    textureImporter.mipmapEnabled = true;
                    textureImporter.maxTextureSize = 1024;
                    textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                    textureImporter.SaveAndReimport();
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                }
                material.SetColor("_EmissionColor", spec.emission > 0
                    ? new Color(spec.color[0], spec.color[1], spec.color[2]) * spec.emission
                    : Color.black);
                material.globalIlluminationFlags = spec.emission > 0
                    ? MaterialGlobalIlluminationFlags.BakedEmissive
                    : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (spec.glass)
                {
                    material.SetFloat("_Mode", 3);
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = 3000;
                }
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name), material);
            }
            // Texture imports can invalidate the cached model importer. Commit all
            // remaps together only after dependent texture imports have finished.
            importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            foreach (var spec in data.materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name),
                    AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + spec.name + ".mat"));
            importer.SaveAndReimport();
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Install music classroom environment");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
            model.name = "BlenderClassroom";
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                var flags =
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                bool glass = renderer.sharedMaterials.Any(m => m != null && m.name == "Classroom_Glass");
                if (!glass && !renderer.name.StartsWith("Exterior"))
                    flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
                renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            var colliders = new GameObject("ArchitectureAndFurnitureColliders");
            colliders.transform.SetParent(root.transform, false);
            foreach (var spec in data.colliders)
            {
                var obj = new GameObject(spec.name);
                obj.transform.SetParent(colliders.transform, false);
                obj.transform.localPosition = V(spec.center);
                obj.transform.localEulerAngles = new Vector3(0, spec.yaw, 0);
                obj.AddComponent<BoxCollider>().size = V(spec.size);
            }
            // Preserve the original floor and its world collider, suppress only its art.
            var oldFloor = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "Floor");
            if (oldFloor != null && oldFloor.GetComponent<Renderer>() != null)
            {
                Undo.RecordObject(oldFloor.GetComponent<Renderer>(), "Show classroom parquet");
                oldFloor.GetComponent<Renderer>().enabled = false;
            }
            SetupLighting(root);
            PrepareBakedLighting(root);
            var cameraObject = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "Main Camera");
            if (cameraObject != null)
            {
                Undo.RecordObject(cameraObject.transform, "Frame classroom demo");
                cameraObject.transform.position = new Vector3(4.35f, 2.05f, -4.65f);
                cameraObject.transform.LookAt(new Vector3(-.45f, 1.60f, .65f));
                var camera = cameraObject.GetComponent<Camera>();
                Undo.RecordObject(camera, "Set classroom preview lens");
                camera.fieldOfView = 65;
                camera.nearClipPlane = .03f;
                camera.farClipPlane = 150;
            }
            var after = PlayerFingerprint();
            if (after != before)
                throw new InvalidOperationException("Player fingerprint changed during art installation.");
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/MusicClassroom-install-report.txt",
                "Scene: " + scene.path + "\nSource: Blender 5.2.3 LTS\n" +
                "Player serialized fingerprint before: " + before +
                "\nPlayer serialized fingerprint after: " + after +
                "\nPlayer unchanged: " + (before == after) +
                "\nArt mesh batches: " + data.meshes + "\nArt triangles: " + data.triangles +
                "\nEnvironment colliders: " + data.colliders.Length +
                "\nNo runtime scripts, player prefab, song data or Udon wiring changed by installer.\n");
            SceneView.RepaintAll();
            return "Installed Blender classroom in existing main scene. Player unchanged. " +
                data.meshes + " mesh batches; " + data.triangles + " triangles.";
        }

        private static Vector3 V(float[] a) { return new Vector3(a[0], a[1], a[2]); }

        private static void SetupLighting(GameObject root)
        {
            var sunObject = SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(g => g.name == "Directional Light");
            Undo.RecordObject(sunObject.transform, "Set afternoon sun direction");
            sunObject.transform.rotation = Quaternion.LookRotation(new Vector3(1, -.65f, -.32f));
            var sun = sunObject.GetComponent<Light>();
            Undo.RecordObject(sun, "Set classroom afternoon lighting");
            sun.enabled = true;
            sun.color = new Color(1, .84f, .65f);
            sun.intensity = 1.25f;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.bounceIntensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .85f;
            sun.shadowBias = .025f;
            sun.shadowNormalBias = .10f;
            sun.shadowResolution = LightShadowResolution.VeryHigh;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.83f, .82f, .77f);
            RenderSettings.ambientEquatorColor = new Color(.69f, .65f, .56f);
            RenderSettings.ambientGroundColor = new Color(.43f, .36f, .26f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .65f;
            var skyPath = Folder + "/Materials/Classroom_AfternoonSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", .025f);
            sky.SetFloat("_AtmosphereThickness", .9f);
            sky.SetColor("_SkyTint", new Color(.61f, .70f, .82f));
            sky.SetColor("_GroundColor", new Color(.43f, .39f, .30f));
            sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssetIfDirty(sky);
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
            var probeObject = new GameObject("ClassroomReflection");
            probeObject.transform.SetParent(root.transform, false);
            probeObject.transform.position = new Vector3(0, 1.6f, -.8f);
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 128;
            probe.size = new Vector3(10.4f, 3.8f, 8.7f);
            probe.center = new Vector3(0, .3f, 0);
            probe.boxProjection = true;
            probe.intensity = .65f;
            probe.nearClipPlane = .1f;
            probe.farClipPlane = 80;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        }

        private static void PrepareBakedLighting(GameObject root)
        {
            var path = Folder + "/ClassroomLighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings();
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.autoGenerate = false;
            settings.realtimeGI = false;
            settings.bakedGI = true;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 12;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 3;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 3;
            settings.ao = true;
            settings.aoMaxDistance = .6f;
            settings.aoExponentIndirect = .7f;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Lightmapping.lightingSettings = settings;
            var holder = new GameObject("ClassroomLightProbes");
            holder.transform.SetParent(root.transform, false);
            var positions = new System.Collections.Generic.List<Vector3>();
            foreach (float x in new[] { -4.5f, -2.5f, 0f, 2.5f, 4.5f })
            foreach (float z in new[] { -4.5f, -2.5f, -.5f, 2.4f })
            foreach (float y in new[] { .3f, 1.5f, 3.2f })
                positions.Add(new Vector3(x, y, z));
            holder.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
        }

        [MenuItem("Tools/Music MIDI Player/Scene Art/Bake Classroom Indirect Lighting")]
        private static void BakeLightingFromMenu() { Debug.Log(BakeClassroomLighting()); }

        public static string BakeClassroomLighting()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before baking classroom lighting.");
            if (SceneManager.GetActiveScene().path != MainScene || GameObject.Find(RootName) == null)
                throw new InvalidOperationException("The main classroom scene is not open.");
            return "Classroom indirect lighting bake started: " + Lightmapping.BakeAsync();
        }

        public static string BakeClassroomReflection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before baking reflection.");
            var root = GameObject.Find(RootName);
            var probe = root.GetComponentInChildren<ReflectionProbe>();
            var path = Folder + "/Textures/ClassroomReflection.exr";
            if (!Lightmapping.BakeReflectionProbe(probe, path))
                throw new InvalidOperationException("Classroom reflection capture failed.");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return "Captured classroom-only 128px reflection; no world GI or player prefab changed.";
        }
    }
}
