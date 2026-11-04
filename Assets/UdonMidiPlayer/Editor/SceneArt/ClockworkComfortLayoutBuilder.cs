using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Logic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Adjust existing recital furniture only. Lighting is baked separately after review.</summary>
    public static class ClockworkComfortLayoutBuilder
    {
        [MenuItem("Tools/Music MIDI Player/Scene Art/Arrange Clockwork Recital Space (No Bake)")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode and baking.");
            var facade = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            var layout = facade.instrumentFanLayout;
            if (layout == null || layout.positions.Length != 5120)
                throw new InvalidOperationException("The existing combination layout is required.");
            Directory.CreateDirectory("RecoveryBackups/ClockworkComfort-20261104");
            EditorSceneManager.SaveScene(scene, "RecoveryBackups/ClockworkComfort-20261104/BeforeApply-" + DateTime.Now.Ticks + ".unity", true);
            Undo.RecordObject(layout, "Bring recital towards the entrance");
            Undo.RecordObjects(layout.roots.Where(t => t != null).Cast<UnityEngine.Object>().ToArray(), "Move existing instruments");
            var center = new Vector3(0, 0, -1.8f);
            var offset = center - layout.center;
            for (int i = 0; i < layout.positions.Length; i++) layout.positions[i] += offset;
            layout.center = center;
            ClockworkInstrumentLayoutBuilder.Validate(layout);
            layout.ApplyLayout();
            UdonSharpEditorUtility.CopyProxyToUdon(layout, ProxySerializationPolicy.All);
            EditorUtility.SetDirty(layout);
            EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(layout));
            var root = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet").transform;
            ApplyCarpet(root.Find("ArtRefinement/RecitalCarpet"));
            ApplyConsole(facade, root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SceneView.RepaintAll();
            Debug.Log("Clockwork recital layout saved. Existing stools/rug moved in source. No light bake started; review layout first.");
        }

        public static void ApplyCarpet(Transform carpet)
        {
            if (carpet == null) return;
            Undo.RecordObject(carpet, "Fit existing woven carpet to recital");
            carpet.localScale = new Vector3(.80f, 1, .84f);
            carpet.localPosition = new Vector3(-.05f * .80f, 0, -1.8f - .40f * .84f);
            carpet.localRotation = Quaternion.identity;
        }

        public static void ApplyConsole(MidiPlayerFacade facade, Transform root)
        {
            var stand = root.Find("PlayerControlConsoleSupport");
            if (stand == null || facade.playerUI == null) throw new InvalidOperationException("Existing console is required.");
            var shift = new Vector3(-3.8f, 0, -5.7f) - stand.position;
            Undo.RecordObject(stand, "Keep console clear of every ensemble combination");
            Undo.RecordObject(facade.playerUI.transform, "Move controls with existing support");
            stand.position += shift;
            facade.playerUI.transform.position += shift;
            PrefabUtility.RecordPrefabInstancePropertyModifications(facade.playerUI.transform);
        }

        public static void CaptureLayoutReview()
        {
            if (SceneManager.GetActiveScene().path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode.");
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var fov = camera.fieldOfView;
            var target = camera.targetTexture;
            var active = RenderTexture.active;
            var rt = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                Directory.CreateDirectory("Captures/ClockworkComfort/After");
                camera.targetTexture = rt;
                camera.fieldOfView = 60;
                var views = new[] { new Vector3(0, 1.65f, -10.5f), new Vector3(-7, 7, -11) };
                for (int i = 0; i < views.Length; i++)
                {
                    camera.transform.position = views[i];
                    camera.transform.LookAt(new Vector3(0, i == 0 ? 1.4f : .5f, -1.8f));
                    camera.Render();
                    RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                    image.Apply();
                    File.WriteAllBytes("Captures/ClockworkComfort/After/" + (i == 0 ? "01-entry-layout.png" : "02-overview-layout.png"), image.EncodeToPNG());
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                camera.targetTexture = target;
                RenderTexture.active = active;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
