using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Logic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Scene-only placement; preserves player wiring and the imported prefab.</summary>
    public static class ClockworkUILayoutBuilder
    {
        const string BoardPath = "Assets/UdonMidiPlayer/CommunityBoards/Prefabs/AuthorSponsorCommunityBoards.prefab";
        const string Folder = "Assets/Scenes/ClockworkMaintenanceStreet";
        static readonly Vector3 ConsolePosition = new Vector3(-3.8f, 1.38f, -4.85f);
        static readonly Vector3 ConsoleAngles = new Vector3(12, 330, 0);

        [MenuItem("Tools/Music MIDI Player/Scene Art/Arrange Clockwork UI and Community Boards")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode and compilation.");
            var f = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            var art = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet").transform;
            var credits = f.transform.root.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "Credits");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPath);
            if (!prefab || !f.playerUI) throw new InvalidOperationException("Required existing UI or imported prefab is missing.");
            var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
            if (lights.Length != 1 || lights[0].type != LightType.Point) throw new InvalidOperationException("Expected exactly one Point Light.");

            Directory.CreateDirectory("RecoveryBackups/ClockworkUI-20261104");
            string backup = "RecoveryBackups/ClockworkUI-20261104/Before-" + DateTime.Now.Ticks + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not preserve the current in-memory scene.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Arrange clockwork scene UI");

            SetPose(f.playerUI.transform, ConsolePosition, ConsoleAngles);
            SetPose(credits.transform, new Vector3(-4.6f, 1.7f, -12.5f), new Vector3(0, 180, 0));

            var boards = art.Find("AuthorSponsorCommunityBoards");
            if (!boards)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, art);
                instance.name = "AuthorSponsorCommunityBoards";
                Undo.RegisterCreatedObjectUndo(instance, "Place author sponsor community prefab");
                boards = instance.transform;
            }
            SetPose(boards, new Vector3(4.7f, 1.7f, -12.5f), new Vector3(0, 180, 0));

            // Player panels use triggers; imported canvases must not create physical walls.
            foreach (var canvas in new[] { f.playerUI.GetComponent<Canvas>(), credits }.Concat(boards.GetComponentsInChildren<Canvas>(true)))
                foreach (var collider in canvas.GetComponents<BoxCollider>())
                {
                    Undo.RecordObject(collider, "Keep UI collider non-blocking");
                    collider.isTrigger = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                }
            EnsureConsoleSupport(art);
            if (f.instrumentFanLayout != null && Mathf.Abs(f.instrumentFanLayout.center.z + 1.8f) < .001f)
                ClockworkComfortLayoutBuilder.ApplyConsole(f, art);
            // Popup order and all existing event targets stay inside their original UI roots.
            EditorUtility.SetDirty(f.playerUI.transform);
            EditorUtility.SetDirty(credits.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the UI placement.");
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            Debug.Log("Clockwork UI: console at southwest approach; credits and author/community/supporters flank entrance; prefab and instrument layout retained.");
        }

        static void SetPose(Transform t, Vector3 position, Vector3 angles)
        {
            Undo.RecordObject(t, "Place scene UI");
            t.position = position;
            t.rotation = Quaternion.Euler(angles);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        static void EnsureConsoleSupport(Transform art)
        {
            var stand = art.Find("PlayerControlConsoleSupport");
            if (!stand)
            {
                stand = new GameObject("PlayerControlConsoleSupport").transform;
                stand.SetParent(art, false);
                Undo.RegisterCreatedObjectUndo(stand.gameObject, "Support player console");
            }
            SetPose(stand, new Vector3(ConsolePosition.x, 0, ConsolePosition.z), new Vector3(0, ConsoleAngles.y, 0));
            const string meshPath = Folder + "/PlayerControlConsoleSupport.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                var parts = new List<GameObject>();
                try
                {
                    parts.Add(Part(stand, "Left foot", new Vector3(-.62f, .025f, .06f), new Vector3(.24f, .05f, .56f), Vector3.zero));
                    parts.Add(Part(stand, "Right foot", new Vector3(.62f, .025f, .06f), new Vector3(.24f, .05f, .56f), Vector3.zero));
                    parts.Add(Part(stand, "Left upright", new Vector3(-.62f, .63f, .1f), new Vector3(.065f, 1.22f, .065f), Vector3.zero));
                    parts.Add(Part(stand, "Right upright", new Vector3(.62f, .63f, .1f), new Vector3(.065f, 1.22f, .065f), Vector3.zero));
                    parts.Add(Part(stand, "Back frame", new Vector3(0, ConsolePosition.y, .035f), new Vector3(1.86f, .92f, .045f), new Vector3(ConsoleAngles.x, 0, 0)));
                    mesh = new Mesh { name = "ClockworkPlayerConsoleSupport" };
                    mesh.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p.GetComponent<MeshFilter>().sharedMesh, transform = stand.worldToLocalMatrix * p.transform.localToWorldMatrix }).ToArray(), true, true);
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }
                finally { foreach (var part in parts) Object.DestroyImmediate(part); }
            }
            var filter = stand.GetComponent<MeshFilter>();
            if (!filter) filter = stand.gameObject.AddComponent<MeshFilter>();
            var renderer = stand.GetComponent<MeshRenderer>();
            if (!renderer) renderer = stand.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/CW_Aged_petrol_green_cast_iron.mat");
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static GameObject Part(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 angles)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = angles;
            return go;
        }
    }
}
