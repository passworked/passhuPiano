using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Second-scene ceiling emission bake, with no additional Light components.</summary>
    public static class ClockworkIndirectLightingBuilder
    {
        private const string Folder = ClockworkMaintenanceSceneBuilder.Folder;

        [MenuItem("Tools/Music MIDI Player/Scene Art/Bake Clockwork Indirect Fill (No Added Lights)")]
        public static void Bake()
        {
            Configure();
            if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Could not start indirect bake.");
        }

        public static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the clockwork second scene outside Play Mode.");
            var root = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet");
            var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
            if (lights.Length != 1 || lights[0].type != LightType.Point || lights[0].lightmapBakeType != LightmapBakeType.Realtime)
                throw new InvalidOperationException("Keep the sole realtime celestial Point Light.");
            Directory.CreateDirectory("RecoveryBackups/ClockworkStructureBake-20261104");
            EditorSceneManager.SaveScene(scene,"RecoveryBackups/ClockworkStructureBake-20261104/BeforeBake-"+DateTime.Now.Ticks+".unity",true);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ClockworkMaintenanceSceneBuilder.ModelPath);
            if (!importer.generateSecondaryUV || importer.secondaryUVMinLightmapResolution!=6)
            {
                importer.generateSecondaryUV=true;
                importer.secondaryUVMarginMethod=ModelImporterSecondaryUVMarginMethod.Calculate;
                importer.secondaryUVMinLightmapResolution=6;importer.secondaryUVMinObjectScale=1;
                importer.SaveAndReimport();
            }
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool moving = false;
                for (var t=renderer.transform;t!=root.transform;t=t.parent)
                    if (t.name.StartsWith("CW_Moving_",StringComparison.Ordinal)) moving=true;
                if (moving || renderer.sharedMaterials.Any(m=>m==null || m.renderQueue>=2500)) continue;
                var filter=renderer.GetComponent<MeshFilter>();
                if (filter==null || filter.sharedMesh==null) continue;
                var mesh=filter.sharedMesh;
                bool architecture = renderer.name.StartsWith("Clockwork_01__",StringComparison.Ordinal)
                    || renderer.name.StartsWith("Clockwork_02__",StringComparison.Ordinal)
                    || renderer.name.StartsWith("Clockwork_03__",StringComparison.Ordinal)
                    || renderer.name.StartsWith("Clockwork_11__",StringComparison.Ordinal)
                    || renderer.name.StartsWith("Clockwork_15__",StringComparison.Ordinal);
                bool structuralSurface=renderer.sharedMaterials.All(m=>new[]{"walnut","oak","limestone","plaster","mortar"}
                    .Any(n=>m.name.IndexOf(n,StringComparison.OrdinalIgnoreCase)>=0));
                // This batch's oak pieces are thin cabinet shelves/window trim, rather than wall structure.
                if (renderer.name.StartsWith("Clockwork_11__",StringComparison.Ordinal)
                    && renderer.sharedMaterials.Any(m=>m.name.IndexOf("oak",StringComparison.OrdinalIgnoreCase)>=0)) structuralSurface=false;
                if (renderer.name.StartsWith("Clockwork_",StringComparison.Ordinal) && !(architecture && structuralSurface))
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&~StaticEditorFlags.ContributeGI);
                    renderer.receiveGI=ReceiveGI.LightProbes;renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
                    continue;
                }
                if (!AssetDatabase.GetAssetPath(mesh).StartsWith(Folder+"/",StringComparison.Ordinal))
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&~StaticEditorFlags.ContributeGI);
                    renderer.receiveGI=ReceiveGI.LightProbes;renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
                    continue;
                }
                // Tiny dressing details use probes; avoid packing dozens of sub-texel faces.
                bool dressing=AssetDatabase.GetAssetPath(mesh).EndsWith(".asset",StringComparison.OrdinalIgnoreCase);
                if (dressing && renderer.bounds.size.y<10)
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&~StaticEditorFlags.ContributeGI);
                    renderer.receiveGI=ReceiveGI.LightProbes;renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
                    continue;
                }
                if (dressing && mesh.uv2.Length==0)
                { Unwrapping.GenerateSecondaryUVSet(mesh); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)|StaticEditorFlags.ContributeGI);
                renderer.receiveGI=ReceiveGI.Lightmaps;
                renderer.scaleInLightmap=1;
            }
            foreach (var mat in root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct())
            {
                if (!mat.HasProperty("_BakedCeilingFill") || mat.GetFloat("_Metallic")>.1f) continue;
                bool wood=mat.name.IndexOf("walnut",StringComparison.OrdinalIgnoreCase)>=0 || mat.name.IndexOf("dark",StringComparison.OrdinalIgnoreCase)>=0;
                if (!wood) continue;
                mat.SetFloat("_BakedCeilingFill",1.6f);
                mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                EditorUtility.SetDirty(mat);AssetDatabase.SaveAssetIfDirty(mat);
            }
            var settings=AssetDatabase.LoadAssetAtPath<LightingSettings>(Folder+"/ClockworkLighting.lighting");
            settings.autoGenerate=false;settings.realtimeGI=false;settings.bakedGI=true;
            settings.lightmapper=LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution=12;settings.lightmapMaxSize=1024;settings.lightmapPadding=4;
            settings.directionalityMode=LightmapsMode.NonDirectional;
            settings.directSampleCount=32;settings.indirectSampleCount=128;settings.environmentSampleCount=64;
            settings.maxBounces=3;settings.ao=true;settings.aoMaxDistance=.65f;settings.aoExponentIndirect=.65f;
            settings.filteringMode=LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);Lightmapping.lightingSettings=settings;
            var probeHost=root.transform.Find("BakedIndirectProbes");
            if (probeHost==null) { probeHost=new GameObject("BakedIndirectProbes").transform;probeHost.SetParent(root.transform,false); }
            var probes=probeHost.GetComponent<LightProbeGroup>();
            if (probes==null) probes=probeHost.gameObject.AddComponent<LightProbeGroup>();
            var positions=new System.Collections.Generic.List<Vector3>();
            foreach (float y in new[]{.5f,2f,4.8f,7.5f,11f})
                foreach (float x in new[]{-11.6f,-8f,-4f,0f,4f,8f,11.6f})
                    foreach (float z in new[]{-10f,-5f,0f,5f,10f}) positions.Add(new Vector3(x,y,z));
            probes.probePositions=positions.ToArray();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject,StaticEditorFlags.ContributeGI)) r.lightProbeUsage=LightProbeUsage.BlendProbes;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
    }
}
