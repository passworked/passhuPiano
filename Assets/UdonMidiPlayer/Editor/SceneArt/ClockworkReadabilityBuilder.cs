using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Only the second scene uses these emissive material copies; no lighting components.</summary>
    public static class ClockworkReadabilityBuilder
    {
        private const string Folder = ClockworkMaintenanceSceneBuilder.Folder + "/Materials/Readability";

        public static void Configure(ClockworkSceneController c)
        {
            if (c.gameObject.scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the clockwork second scene outside Play Mode.");
            Directory.CreateDirectory(Folder);
            var materials = new HashSet<Material>();
            foreach (string name in new[] {"CW_Old_brushed_brass", "CW_Polished_brass_wear", "CW_Aged_petrol_green_cast_iron", "CW_Dark_gunmetal_fastenings"})
            {
                var metal=AssetDatabase.LoadAssetAtPath<Material>(ClockworkMaintenanceSceneBuilder.Folder + "/Materials/" + name + ".mat");
                if (metal.shader.name != "MusicMidiPlayer/Clockwork/HeritageSurface")
                    metal.shader=Shader.Find("MusicMidiPlayer/Clockwork/ReadableMetal");
                materials.Add(metal);
            }
            var f = c.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            var renderers = f.instrumentRoots.SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Concat(f.drumView.GetComponentsInChildren<Renderer>(true)).Distinct();
            var copies = new Dictionary<Material,Material>();
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials; bool changed = false;
                for (int i=0; i<slots.Length; i++)
                {
                    var source = slots[i];
                    if (source == null || source.shader.name != "Standard" || source.GetFloat("_Mode") != 0) continue;
                    string n = source.name;
                    if (n.IndexOf("Key", StringComparison.OrdinalIgnoreCase)>=0 || n.Contains("Ebony") || n.Contains("Rubber") || n.Contains("Felt") || n.Contains("Caster") || n.Contains("Seat")) continue;
                    Material copy;
                    if (AssetDatabase.GetAssetPath(source).StartsWith(Folder + "/",StringComparison.Ordinal)) copy=source;
                    else if (!copies.TryGetValue(source,out copy))
                    {
                        string guid; long localId; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out guid,out localId);
                        string path = Folder + "/" + n + "_" + guid.Substring(0,8) + "_" + localId + ".mat";
                        copy=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (copy==null) { copy=new Material(source); copy.name=n; AssetDatabase.CreateAsset(copy,path); }
                        copies.Add(source,copy);
                    }
                    copy.EnableKeyword("_EMISSION");
                    materials.Add(copy); slots[i]=copy; changed=true;
                }
                if (changed) { renderer.sharedMaterials=slots; EditorUtility.SetDirty(renderer); }
            }
            c.readabilityMaterials=materials.Where(m=>m!=null).OrderBy(m=>AssetDatabase.GetAssetPath(m)).ToArray();
            c.readabilityBaseColors=c.readabilityMaterials.Select(m=>
            {
                Color color=m.color.linear;
                if (m.name.StartsWith("CW_",StringComparison.Ordinal)) return Color.white;
                float strength=color.grayscale>.55f?.35f:.95f;
                return new Color(Mathf.Sqrt(color.r)*strength,Mathf.Sqrt(color.g)*strength,Mathf.Sqrt(color.b)*strength,1);
            }).ToArray();
            foreach(var material in c.readabilityMaterials)
                material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            ClockworkArtRefinementBuilder.RefreshInstalledMaterials();
        }

        [MenuItem("Tools/Music MIDI Player/Scene Art/Refresh Clockwork Surface Readability")]
        public static void Refresh()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var c=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ClockworkSceneController>(true)).Single();
            Configure(c); c.RefreshFromClientClock();
            UdonSharpEditorUtility.CopyProxyToUdon(c,ProxySerializationPolicy.All);
            EditorUtility.SetDirty(c); EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(c));
            foreach(var m in c.readabilityMaterials) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
            EditorUtility.SetDirty(c.skyMaterial); AssetDatabase.SaveAssetIfDirty(c.skyMaterial);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
    }
}
