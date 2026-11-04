using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Editor.Prefab;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MusicMidiPlayer.Editor.SceneArt
{
    // Only the explicitly opted-in clockwork scene receives a layout reference and baked poses.
    public static class ClockworkInstrumentLayoutBuilder
    {
        const string Folder = "Assets/Scenes/ClockworkMaintenanceStreet";
        static readonly int[] Order = { 0, 2, 3, 4, 5, 6, 8, 9 };

        [MenuItem("Tools/Music MIDI Player/Scene Art/Arrange Clockwork Instruments in Fan")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode.");
            var f = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            if (f.instrumentRoots.Length != 9 || f.instrumentRouter == null || f.drumView == null)
                throw new InvalidOperationException("The existing nine view bindings and drums are required.");
            Directory.CreateDirectory("RecoveryBackups/FanLayout-20261104");
            const string backup = "RecoveryBackups/FanLayout-20261104/Clockwork-Before-Fan.unity";
            if (!File.Exists(backup) && !EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not preserve the in-memory scene.");
            EnsureProgram();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Arrange clockwork instrument combinations");
            Undo.RecordObject(f, "Connect scene fan layout");
            var host = f.transform.root.Find("Data/InstrumentFanLayout");
            if (host == null)
            {
                var go = new GameObject("InstrumentFanLayout");
                go.transform.SetParent(f.instrumentRouter.transform.parent, false);
                Undo.RegisterCreatedObjectUndo(go, "Create scene fan layout");
                host = go.transform;
            }
            var layout = host.GetComponent<MidiInstrumentFanLayout>() ?? host.gameObject.AddUdonSharpComponent<MidiInstrumentFanLayout>();
            Undo.RecordObject(layout, "Bake fixed fan poses");
            layout.facade = f;
            layout.roots = f.instrumentRoots.Select(g => g.transform).Concat(new[] { f.drumView.transform }).ToArray();
            f.instrumentFanLayout = layout;
            if (layout.roots.Any(t => t.parent != layout.roots[0].parent || t.localScale != Vector3.one))
                throw new InvalidOperationException("Instrument roots must share an unscaled presentation frame.");

            // The previous clockwork setup moved this child in world space while it was hidden.
            Undo.RecordObject(f.musicBoxTable.transform, "Restore music box table under its feet");
            f.musicBoxTable.transform.localPosition = Vector3.zero;
            f.musicBoxTable.transform.localRotation = Quaternion.identity;
            f.musicBoxTable.transform.localScale = Vector3.one;
            f.musicBoxTable.SetActive(true);
            f.musicBoxSeat.SetActive(true);
            AddStringStand(f, 3);
            AddStringStand(f, 4);
            for (int i = 0; i < 3; i++) AlignSeatWithModelFloor(f.instrumentRoots[i].transform, i == 2 ? "MarimbaModel" : "PianoModel");
            layout.footprintMinimum = new Vector3[10];
            layout.footprintMaximum = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                if (i == 7) continue;
                Bounds b = Measure(layout.roots[i]);
                layout.footprintMinimum[i] = b.min;
                layout.footprintMaximum[i] = b.max;
            }
            Bake(layout);
            Validate(layout);
            layout.ApplyLayout();
            for (int i = 0; i < 9; i++) f.instrumentRoots[i].SetActive(f.instrumentRouter.IsViewEnabled(i));
            foreach (var b in new UdonSharpBehaviour[] { f, layout })
            {
                UdonSharpEditorUtility.CopyProxyToUdon(b, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(b);
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the clockwork layout.");
            AssetDatabase.SaveAssets();
            Debug.Log("Clockwork fan: 512 selection/appearance poses baked and checked; 0.65 m footprint clearance. Main scene and distribution prefabs unchanged.");
        }

        static void EnsureProgram()
        {
            const string script = "Assets/UdonMidiPlayer/Runtime/Views/MidiInstrumentFanLayout.cs";
            string path = Path.ChangeExtension(script, ".asset").Replace('\\', '/');
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            if (program == null)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(script);
                program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(program, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = true });
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("Fix UdonSharp compilation before changing the scene.");
        }

        static void AddStringStand(MidiPlayerFacade f, int index)
        {
            var root = f.instrumentRoots[index].transform;
            if (root.Find("DisplayStand") != null) return;
            var plate = root.GetComponentsInChildren<MeshFilter>(true).Single(m => m.name == "Ribs");
            Vector3 contact = new Vector3(0, float.MaxValue, 0);
            foreach (var vertex in plate.sharedMesh.vertices)
            {
                Vector3 p = root.InverseTransformPoint(plate.transform.TransformPoint(vertex));
                if (p.y < contact.y) contact = p;
            }
            var stand = new GameObject("DisplayStand");
            stand.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(stand, "Support suspended string instrument");
            Material material = f.musicBoxTable.GetComponent<Renderer>().sharedMaterial;
            var parts = new List<GameObject>();
            parts.Add(Cylinder(stand.transform, "Base", new Vector3(contact.x, .02f, contact.z), new Vector3(.38f, .02f, .38f), material));
            float top = contact.y - .028f;
            parts.Add(Cylinder(stand.transform, "Post", new Vector3(contact.x, (.04f + top) * .5f, contact.z), new Vector3(.035f, (top - .04f) * .5f, .035f), material));
            parts.Add(Cylinder(stand.transform, "Cradle", contact - Vector3.up * .014f, new Vector3(.16f, .014f, .15f), material));
            var combine = parts.Select(g => new CombineInstance
            {
                mesh = g.GetComponent<MeshFilter>().sharedMesh,
                transform = stand.transform.worldToLocalMatrix * g.transform.localToWorldMatrix
            }).ToArray();
            var mesh = new Mesh { name = root.name + "DisplayStand" };
            mesh.CombineMeshes(combine, true, true);
            string path = Folder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            stand.AddComponent<MeshFilter>().sharedMesh = mesh;
            stand.AddComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var part in parts) Object.DestroyImmediate(part);
        }

        static GameObject Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void AlignSeatWithModelFloor(Transform root, string modelName)
        {
            var model = root.Find(modelName);
            var seat = root.Find("PerformanceSeat");
            float modelFloor = model.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeSelf).Min(r => MidiPianoModelBinder.BoundsIn(r, root).min.y);
            float seatFloor = seat.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeSelf).Min(r => MidiPianoModelBinder.BoundsIn(r, root).min.y);
            // Imported piano feet / marimba wheels can start a few millimeters above their root.
            // Share their support plane before grounding the whole root, so the seat stays on the floor too.
            Undo.RecordObject(seat, "Align instrument and seat support planes");
            seat.localPosition += Vector3.up * (modelFloor - seatFloor);
        }

        public static Bounds Measure(Transform root)
        {
            bool hasBounds = false;
            Bounds result = new Bounds();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform child = r.transform;
                bool visible = r.enabled;
                while (child != root)
                {
                    if (!child.gameObject.activeSelf || child.name == "ViewRoot" || child.name == "PercussionLanes" || child.name.StartsWith("Impact_")) visible = false;
                    child = child.parent;
                }
                if (!visible) continue;
                var b = MidiPianoModelBinder.BoundsIn(r, root);
                if (!hasBounds) { result = b; hasBounds = true; } else result.Encapsulate(b);
            }
            if (!hasBounds) throw new InvalidOperationException("No physical geometry for " + root.name);
            return result;
        }

        static int[] Views(int selection, int appearance)
        {
            var views = new List<int>();
            for (int bit = 0; bit < 8; bit++)
                if ((selection & (1 << bit)) != 0) views.Add(bit == 0 ? appearance : Order[bit]);
            return views.ToArray();
        }

        static void Bake(MidiInstrumentFanLayout layout)
        {
            layout.positions = new Vector3[5120];
            layout.yawAngles = new float[5120];
            for (int selection = 0; selection < 256; selection++)
            for (int appearance = 0; appearance < 2; appearance++)
            {
                int[] views = Views(selection, appearance);
                if (views.Length == 0) continue;
                float lo = 4.5f, hi = 30f;
                for (int iteration = 0; iteration < 24; iteration++)
                {
                    float mid = (lo + hi) * .5f;
                    if (Arc(layout, views, mid).Sum() > layout.maximumArc * Mathf.Deg2Rad) lo = mid;
                    else hi = mid;
                }
                float radius = hi;
                float[] steps = Arc(layout, views, radius);
                float angle = -steps.Sum() * .5f;
                int start = (selection * 2 + appearance) * 10;
                Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue), maximum = -minimum;
                for (int n = 0; n < views.Length; n++)
                {
                    int view = views[n];
                    Vector2 p = OnArc(radius, angle);
                    Vector3 min = layout.footprintMinimum[view], max = layout.footprintMaximum[view];
                    Vector2 c = new Vector2((min.x + max.x) * .5f, (min.z + max.z) * .5f);
                    Vector2 root = p - Rotate(c, angle);
                    layout.positions[start + view] = new Vector3(root.x, layout.center.y - min.y, root.y);
                    layout.yawAngles[start + view] = angle * Mathf.Rad2Deg;
                    foreach (var point in Rectangle(layout, view, radius, angle))
                    { minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point); }
                    if (n < steps.Length) angle += steps[n];
                }
                Vector2 shift = new Vector2(layout.center.x, layout.center.z) - (minimum + maximum) * .5f;
                foreach (int view in views) layout.positions[start + view] += new Vector3(shift.x, 0, shift.y);
            }
        }

        static float[] Arc(MidiInstrumentFanLayout layout, int[] views, float radius)
        {
            var result = new float[views.Length - 1];
            for (int i = 0; i < result.Length; i++)
            {
                var a = Rectangle(layout, views[i], radius, 0);
                float lo = 0, hi = Mathf.PI * .5f;
                for (int k = 0; k < 24; k++)
                {
                    float angle = (lo + hi) * .5f;
                    if (Distance(a, Rectangle(layout, views[i + 1], radius, angle)) < layout.edgeGap) lo = angle;
                    else hi = angle;
                }
                result[i] = hi;
            }
            return result;
        }

        static Vector2 OnArc(float radius, float angle) => new Vector2(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius);
        static Vector2 Rotate(Vector2 p, float angle) => new Vector2(p.x * Mathf.Cos(angle) + p.y * Mathf.Sin(angle), -p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle));
        static Vector2[] Rectangle(MidiInstrumentFanLayout layout, int view, float radius, float angle)
        {
            Vector3 size = (layout.footprintMaximum[view] - layout.footprintMinimum[view]) * .5f;
            return Corners(OnArc(radius, angle), new Vector2(size.x, size.z), angle);
        }
        static Vector2[] Corners(Vector2 center, Vector2 extents, float angle) => new[]
        {
            center + Rotate(new Vector2(-extents.x, -extents.y), angle),
            center + Rotate(new Vector2(extents.x, -extents.y), angle),
            center + Rotate(new Vector2(extents.x, extents.y), angle),
            center + Rotate(new Vector2(-extents.x, extents.y), angle)
        };

        // Convex footprint distance: overlap is zero, otherwise a closest vertex/edge pair.
        public static float Distance(Vector2[] a, Vector2[] b)
        {
            bool separated = false;
            foreach (var shape in new[] { a, b })
            for (int i = 0; i < 4; i++)
            {
                Vector2 edge = shape[(i + 1) % 4] - shape[i], axis = new Vector2(-edge.y, edge.x).normalized;
                float minA = a.Min(p => Vector2.Dot(p, axis)), maxA = a.Max(p => Vector2.Dot(p, axis));
                float minB = b.Min(p => Vector2.Dot(p, axis)), maxB = b.Max(p => Vector2.Dot(p, axis));
                if (maxA < minB || maxB < minA) separated = true;
            }
            if (!separated) return 0;
            float result = float.MaxValue;
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                result = Mathf.Min(result, PointSegment(a[i], b[j], b[(j + 1) % 4]));
                result = Mathf.Min(result, PointSegment(b[j], a[i], a[(i + 1) % 4]));
            }
            return result;
        }
        static float PointSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return Vector2.Distance(point, a + edge * Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude));
        }

        public static string Validate(MidiInstrumentFanLayout layout)
        {
            int pairs = 0;
            float maximumGapError = 0, maximumCenterError = 0, minimumClearance = float.MaxValue;
            var extentMin = new Vector2(float.MaxValue, float.MaxValue);
            var extentMax = -extentMin;
            for (int selection = 0; selection < 256; selection++)
            for (int appearance = 0; appearance < 2; appearance++)
            {
                int[] views = Views(selection, appearance);
                int start = (selection * 2 + appearance) * 10;
                var rectangles = new List<Vector2[]>();
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
                foreach (int view in views)
                {
                    Vector3 low = layout.footprintMinimum[view], high = layout.footprintMaximum[view], p = layout.positions[start + view];
                    if (Mathf.Abs(p.y + low.y - layout.center.y) > .0001f) throw new InvalidOperationException("Ungrounded selection " + selection);
                    float yaw = layout.yawAngles[start + view] * Mathf.Deg2Rad;
                    Vector2 center = new Vector2(p.x, p.z) + Rotate(new Vector2((low.x + high.x) * .5f, (low.z + high.z) * .5f), yaw);
                    var corners = Corners(center, new Vector2((high.x - low.x) * .5f, (high.z - low.z) * .5f), yaw);
                    rectangles.Add(corners);
                    foreach (var corner in corners) { min = Vector2.Min(min, corner); max = Vector2.Max(max, corner); }
                }
                if (views.Length == 0) continue;
                maximumCenterError = Mathf.Max(maximumCenterError, Vector2.Distance((min + max) * .5f, new Vector2(layout.center.x, layout.center.z)));
                extentMin = Vector2.Min(extentMin, min); extentMax = Vector2.Max(extentMax, max);
                for (int i = 0; i < views.Length; i++)
                for (int j = i + 1; j < views.Length; j++)
                {
                    float gap = Distance(rectangles[i], rectangles[j]); pairs++;
                    minimumClearance = Mathf.Min(minimumClearance, gap);
                    if (j == i + 1) maximumGapError = Mathf.Max(maximumGapError, Mathf.Abs(gap - layout.edgeGap));
                    if (gap < layout.edgeGap - .0001f) throw new InvalidOperationException("Overlapping footprint in selection " + selection);
                }
            }
            if (maximumGapError > .0001f || maximumCenterError > .0001f) throw new InvalidOperationException("Fan spacing or centering is inconsistent.");
            var report = Newtonsoft.Json.JsonConvert.SerializeObject(new { configurations = 512, pairs, layout.edgeGap, layout.maximumArc, maximumGapError, maximumCenterError, minimumClearance, extentMin = extentMin.ToString("F4"), extentMax = extentMax.ToString("F4") }, Newtonsoft.Json.Formatting.Indented);
            Directory.CreateDirectory("Logs/ClockworkFanLayout");
            File.WriteAllText("Logs/ClockworkFanLayout/geometry.json", report);
            return report;
        }
    }
}
