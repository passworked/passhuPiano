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
using Object = UnityEngine.Object;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Independent second scene, explicitly requested 2026-11-04. One Light total.</summary>
    public static class ClockworkMaintenanceSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/ClockworkMaintenanceStreet.unity";
        public const string Folder = "Assets/Scenes/ClockworkMaintenanceStreet";
        public const string ModelPath = Folder + "/Models/ClockworkMaintenanceStreet.fbx";
        private const string MainScene = "Assets/Scenes/VRCDefaultWorldScene.unity";

        [Serializable] private class Manifest
        {
            public MaterialSpec[] materials;
            public ColliderSpec[] colliders;
            public int meshes;
            public int triangles;
        }
        [Serializable] private class MaterialSpec
        {
            public string name;
            public float[] color;
            public float smoothness;
            public float metallic;
            public string texture;
        }
        [Serializable] private class ColliderSpec
        {
            public string name;
            public float[] center;
            public float[] size;
            public float[] rotation;
        }

        [MenuItem("Tools/Music MIDI Player/Scene Art/Create Clockwork Second Scene (One Point Light)")]
        private static void CreateFromMenu() { Debug.Log(Create()); }

        public static string Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and wait for compilation.");
            var current = SceneManager.GetActiveScene();
            if (current.path != MainScene || current.isDirty)
                throw new InvalidOperationException("The saved main scene must be active; unsaved work is never discarded or overwritten.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Another open scene has unsaved work.");
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("The second scene already exists; update it incrementally.");
            var source = current.GetRootGameObjects().Single(g => g.name == "UdonMidiPlayer");
            var sourceFacade = source.GetComponentInChildren<MidiPlayerFacade>(true);
            string catalogHash = sourceFacade.library.catalogHash;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/clockwork-manifest.json"));
            PrepareMaterials(manifest);
            if (!AssetDatabase.CopyAsset(MainScene, ScenePath))
                throw new InvalidOperationException("Could not create the independent scene copy.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "DemoMusicClassroom" || root.name == "Floor" || root.name == "Directional Light")
                    Object.DestroyImmediate(root);

            var art = new GameObject("ClockworkMaintenanceStreet");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), art.transform);
            model.name = "BlenderEnvironment";
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var collisions = new GameObject("ArchitectureAndFurnitureColliders");
            collisions.transform.SetParent(art.transform, false);
            foreach (var c in manifest.colliders)
            {
                var go = new GameObject(c.name);
                go.transform.SetParent(collisions.transform, false);
                go.transform.localPosition = V(c.center);
                if (c.rotation != null && c.rotation.Length == 3) go.transform.localEulerAngles = V(c.rotation);
                go.AddComponent<BoxCollider>().size = V(c.size);
            }
            var player = scene.GetRootGameObjects().Single(g => g.name == "UdonMidiPlayer");
            var f = player.GetComponentInChildren<MidiPlayerFacade>(true);
            ConfigurePlayer(f);

            // Remove even dormant hit lights, and clear their proxy/backing references.
            foreach (var effects in player.GetComponentsInChildren<MidiWaterfallEffects>(true))
            {
                effects.hitLights = new Light[0];
                effects.maxActiveLights = 0;
                UdonSharpEditorUtility.CopyProxyToUdon(effects, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(effects);
            }
            foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray())
                Object.DestroyImmediate(light);

            var lightObject = new GameObject("Clockwork_SolePointLight");
            lightObject.transform.SetParent(art.transform, false);
            lightObject.transform.position = new Vector3(0, 7.2f, -1.5f);
            var point = lightObject.AddComponent<Light>();
            point.type = LightType.Point;
            point.lightmapBakeType = LightmapBakeType.Realtime;
            point.color = new Color(1, .83f, .63f);
            point.intensity = 3.2f;
            point.range = 42;
            point.shadows = LightShadows.Soft;
            point.shadowStrength = .8f;
            point.shadowBias = .045f;
            point.shadowNormalBias = .16f;
            point.shadowNearPlane = .3f;
            point.shadowResolution = LightShadowResolution.High;
            point.renderMode = LightRenderMode.ForcePixel;

            // No inherited classroom sky, ambient illumination, GI or baked lights.
            RenderSettings.sun = null;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.customReflection = null;
            RenderSettings.fog = false;
            LightmapSettings.lightmaps = new LightmapData[0];
            Lightmapping.lightingDataAsset = null;
            var settings = new LightingSettings { name = "Clockwork_SinglePoint_NoGI", autoGenerate = false, realtimeGI = false, bakedGI = false };
            AssetDatabase.CreateAsset(settings, Folder + "/ClockworkLighting.lighting");
            Lightmapping.lightingSettings = settings;
            foreach (var probe in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(true)))
                probe.enabled = false;

            var world = scene.GetRootGameObjects().Single(g => g.name == "VRCWorld");
            world.transform.position = new Vector3(0, .1f, -7.6f);
            world.transform.rotation = Quaternion.identity;
            foreach (var component in world.GetComponents<Component>())
                if (component != null && component.GetType().Name == "PipelineManager")
                {
                    var so = new SerializedObject(component);
                    var id = so.FindProperty("blueprintId");
                    if (id != null) { id.stringValue = ""; so.ApplyModifiedPropertiesWithoutUndo(); }
                }
            var camera = scene.GetRootGameObjects().Single(g => g.name == "Main Camera").GetComponent<Camera>();
            camera.transform.position = new Vector3(9.5f, 4.7f, -13);
            camera.transform.LookAt(new Vector3(-.2f, 6.35f, 4.5f));
            camera.fieldOfView = 46;
            camera.nearClipPlane = .06f;
            camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .021f, .018f);
            camera.allowHDR = true;
            if (f.library.catalogHash != catalogHash)
                throw new InvalidOperationException("Scene creation changed repertoire identity.");
            var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
            if (lights.Length != 1 || lights[0].type != LightType.Point)
                throw new InvalidOperationException("The second scene must contain exactly one Point Light, including inactive objects.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Logs/ClockworkScene");
            string report = "Scene: " + ScenePath + "\nEnvironment meshes: " + manifest.meshes +
                "\nEnvironment triangles: " + manifest.triangles + "\nEnvironment colliders: " + manifest.colliders.Length +
                "\nLight components including inactive: 1\nType: Point, Realtime\nAmbient / sky / baked GI / emissive GI: off\n" +
                "Waterfall hit lights: empty in proxy and backing\nCatalog hash retained: " + catalogHash +
                "\nMain scene / distribution prefabs: not saved or applied\nPlay Mode / build / VRChat: not run\n";
            File.WriteAllText("Logs/ClockworkScene/installation.txt", report);
            SceneView.RepaintAll();
            return report;
        }

        private static void PrepareMaterials(Manifest manifest)
        {
            Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = Shader.Find("MusicMidiPlayer/Demo/ClassroomSurface");
            if (shader == null) throw new InvalidOperationException("Shared vertex-color surface shader is missing.");
            foreach (var spec in manifest.materials)
            {
                string path = Folder + "/Materials/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                if (spec.name == "CW_Clockwork_lightweight_dial_glass")
                {
                    material.shader = Shader.Find("MusicMidiPlayer/Clockwork/DialGlass");
                    material.SetColor("_Tint", new Color(.92f,.88f,.76f,.32f));
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                    continue;
                }
                material.shader = shader;
                material.color = new Color(spec.color[0], spec.color[1], spec.color[2]);
                material.SetFloat("_Smoothness", spec.smoothness);
                material.SetFloat("_Metallic", spec.metallic);
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (!string.IsNullOrEmpty(spec.texture))
                {
                    string texturePath = Folder + "/Textures/" + spec.texture;
                    var ti = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true; ti.anisoLevel = 4;
                    ti.maxTextureSize = 512; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                    material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                }
                EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.globalScale = 1; importer.useFileScale = true;
            importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false; importer.importLights = false;
            importer.addCollider = false; importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            var clockworkLighting = AssetDatabase.LoadAssetAtPath<LightingSettings>(Folder + "/ClockworkLighting.lighting");
            importer.generateSecondaryUV = clockworkLighting != null && clockworkLighting.bakedGI;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var spec in manifest.materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name),
                    AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/" + spec.name + ".mat"));
            importer.SaveAndReimport();
        }

        private static void ConfigurePlayer(MidiPlayerFacade f)
        {
            Vector3[] positions = {
                new Vector3(-3.2f,0,-.5f), new Vector3(-3.2f,0,-.5f), new Vector3(3.7f,0,-.8f),
                new Vector3(-1.4f,0,1.4f), new Vector3(-.25f,0,1.65f), new Vector3(1,0,1.65f),
                new Vector3(2.3f,0,1.5f), new Vector3(0,0,4.8f), new Vector3(4.8f,0,2.8f)
            };
            f.instrumentRouter.viewRestPositions = positions;
            f.instrumentRouter.stageOffsets = new Vector3[8];
            f.instrumentRouter.enabledMask = 1;
            f.instrumentRouter.pianoAppearance = 1;
            f.selectedInstrument = 1;
            f.network.enabledInstrumentMask = 1; f.network.pianoAppearance = 1;
            for (int i = 0; i < f.instrumentRoots.Length; i++)
            {
                f.instrumentRoots[i].transform.localPosition = positions[i];
                f.instrumentRoots[i].SetActive(f.instrumentRouter.IsViewEnabled(i));
            }
            if (f.instrumentRoots.Length > 8)
                f.instrumentRoots[8].transform.position = f.instrumentRoots[1].transform.TransformPoint(f.musicBoxGrandPianoOffset);
            if (f.musicBoxTable != null) { f.musicBoxTable.transform.position = positions[8]; f.musicBoxTable.SetActive(false); }
            if (f.musicBoxSeat != null) f.musicBoxSeat.SetActive(false);
            f.drumView.transform.localPosition = new Vector3(.3f, 0, 3.4f);
            f.drumView.gameObject.SetActive(f.instrumentRouter.IsDrumsEnabled() && f.drumView.presentationEnabled);
            f.playerUI.transform.position = new Vector3(-6.5f, 1.35f, -3.2f);
            f.playerUI.transform.rotation = Quaternion.Euler(0, 270, 0);
            var credits = f.transform.root.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c => c.name == "Credits");
            if (credits != null)
            {
                credits.transform.position = new Vector3(-6.5f, 1.69f, -6.6f);
                credits.transform.rotation = Quaternion.Euler(0, 270, 0);
            }
            foreach (var b in new UdonSharpBehaviour[] {f, f.instrumentRouter, f.network})
            {
                UdonSharpEditorUtility.CopyProxyToUdon(b, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(b);
            }
        }
        public static int UpdateEnvironmentColliders()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/clockwork-manifest.json"));
            var holder = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet")
                .transform.Find("ArchitectureAndFurnitureColliders");
            for (int i = holder.childCount - 1; i >= 0; i--) Object.DestroyImmediate(holder.GetChild(i).gameObject);
            foreach (var c in manifest.colliders)
            {
                var go = new GameObject(c.name);
                go.transform.SetParent(holder, false);
                go.transform.localPosition = V(c.center);
                if (c.rotation != null && c.rotation.Length == 3) go.transform.localEulerAngles = V(c.rotation);
                go.AddComponent<BoxCollider>().size = V(c.size);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return manifest.colliders.Length;
        }
        public static void RefreshEnvironmentMaterials()
        {
            PrepareMaterials(JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/clockwork-manifest.json")));
        }
        private static Vector3 V(float[] v) { return new Vector3(v[0], v[1], v[2]); }
    }
}
