using System;
using MusicMidiPlayer.Editor.Import;
using MusicMidiPlayer.Views;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPianoModelBinder
    {
        public static void Bind(Transform presentation, Transform viewRoot, PianoKeyboardView keyboard,
            MidiWaterfallView waterfall, GameObject source)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (source == null || string.IsNullOrEmpty(sourcePath) || !EditorUtility.IsPersistent(source))
                throw new InvalidOperationException("钢琴模型必须是已保存的FBX/Prefab资源");
            Transform host = presentation.Find("PianoModel");
            if (host == null)
            {
                GameObject created = new GameObject("PianoModel"); Undo.RegisterCreatedObjectUndo(created, "Add Piano Model");
                created.transform.SetParent(presentation, false); host = created.transform;
            }
            host.gameObject.SetActive(true);
            GameObject instance = null;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                GameObject candidate = host.GetChild(i).gameObject;
                if (instance == null && HasModelSource(candidate, sourcePath)) instance = candidate;
                else Undo.DestroyObjectImmediate(candidate);
            }
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(source, host);
                if (instance == null) throw new InvalidOperationException("无法实例化钢琴模型: " + sourcePath);
                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Piano Model");
            }
            Transform[] keys = new Transform[88];
            foreach (Transform item in instance.GetComponentsInChildren<Transform>(true))
            {
                int pitch;
                if (!TryPitch(item.name, out pitch)) continue;
                int index = pitch - 21;
                if (keys[index] != null)
                {
                    if (item.IsChildOf(keys[index])) continue;
                    if (!keys[index].IsChildOf(item)) throw new InvalidOperationException("模型含重复独立琴键: " + pitch);
                }
                keys[index] = item;
            }
            Renderer[] renderers = new Renderer[88];
            Bounds[] bounds = new Bounds[88];
            Material[] rest = new Material[88];
            Vector3[] offsets = new Vector3[88];
            Vector3[] pivots = new Vector3[88];
            Vector3[] axes = new Vector3[88];
            float[] angles = new float[88];
            bool[] black = new bool[88];
            Bounds whiteBounds = new Bounds(); bool hasWhiteBounds = false;
            Material whiteMaterial = null, blackMaterial = null;
            for (int i = 0; i < 88; i++)
            {
                if (keys[i] == null) throw new InvalidOperationException("模型缺少独立琴键 Key_" + (i + 21).ToString("000"));
                Renderer[] meshes = keys[i].GetComponentsInChildren<Renderer>(true);
                if (meshes.Length != 1 || meshes[0].sharedMaterials.Length != 1 || meshes[0].sharedMaterial == null)
                    throw new InvalidOperationException("琴键须有一个Renderer和一个材质槽: " + keys[i].name);
                renderers[i] = meshes[0]; rest[i] = meshes[0].sharedMaterial;
                bounds[i] = BoundsIn(meshes[0], presentation);
                int pc = (i + 21) % 12; black[i] = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                if (black[i]) { if (blackMaterial == null) blackMaterial = rest[i]; }
                else
                {
                    if (whiteMaterial == null) whiteMaterial = rest[i];
                    if (!hasWhiteBounds) { whiteBounds = bounds[i]; hasWhiteBounds = true; } else whiteBounds.Encapsulate(bounds[i]);
                }
                offsets[i] = keys[i].parent.InverseTransformVector(presentation.TransformVector(Vector3.down * 0.0015f));
                Vector3 pivot = new Vector3(bounds[i].center.x, bounds[i].max.y, bounds[i].max.z);
                pivots[i] = keys[i].parent.InverseTransformPoint(presentation.TransformPoint(pivot));
                axes[i] = keys[i].parent.InverseTransformVector(presentation.TransformVector(Vector3.right)).normalized;
                angles[i] = PressAngle(black[i] ? 0.006f : 0.008f, bounds[i].size.z);
                if (i > 0 && bounds[i].center.x <= bounds[i - 1].center.x)
                    throw new InvalidOperationException("模型琴键X方向须由低音向高音递增: " + keys[i].name);
            }
            viewRoot.localRotation = Quaternion.identity; viewRoot.localScale = Vector3.one;
            viewRoot.localPosition = new Vector3(whiteBounds.center.x, whiteBounds.max.y + 0.002f, whiteBounds.min.z - 0.006f);
            keyboard.keyTransforms = keys; keyboard.keyRenderers = renderers; keyboard.keyIsBlack = black;
            keyboard.keyRestMaterials = rest; keyboard.keyPressOffsets = offsets;
            keyboard.keyPivotPositions = pivots; keyboard.keyRotationAxes = axes; keyboard.keyPressAngles = angles;
            waterfall.keyboard = keyboard;
            keyboard.whiteMaterial = whiteMaterial; keyboard.blackMaterial = blackMaterial;
            keyboard.whitePressedMaterial = PressedMaterial(source, whiteMaterial, false);
            keyboard.blackPressedMaterial = PressedMaterial(source, blackMaterial, true);
            waterfall.keyX = new float[88]; waterfall.keyWidth = new float[88]; waterfall.keyIsBlack = (bool[])black.Clone();
            for (int i = 0; i < 88; i++)
            {
                Bounds local = BoundsIn(renderers[i], waterfall.transform);
                waterfall.keyX[i] = local.center.x; waterfall.keyWidth[i] = local.size.x;
                if (local.size.x <= 0) throw new InvalidOperationException("模型琴键几何宽度无效: " + keys[i].name);
            }
            for (int i = 0; i < keyboard.transform.childCount; i++)
                if (keyboard.transform.GetChild(i).name.StartsWith("Key_", StringComparison.Ordinal)) keyboard.transform.GetChild(i).gameObject.SetActive(false);
        }

        public static void BindPedals(Transform presentation, PianoPedalView pedalView)
        {
            if (presentation == null || pedalView == null) throw new ArgumentNullException("踏板绑定缺少Presentation或PianoPedalView");
            Transform host = presentation.Find("PianoModel");
            if (host == null || !host.gameObject.activeSelf)
            {
                pedalView.pedalTransforms = Array.Empty<Transform>();
                pedalView.pedalPivotPositions = Array.Empty<Vector3>();
                pedalView.pedalRotationAxes = Array.Empty<Vector3>();
                pedalView.pedalPressAngles = Array.Empty<float>();
                EditorUtility.SetDirty(pedalView);
                return;
            }
            var pedals = new System.Collections.Generic.List<Transform>();
            var pedalNames = new System.Collections.Generic.HashSet<string>();
            foreach (Transform item in host.GetComponentsInChildren<Transform>(true))
            {
                if (item.name == "Pedal_UnaCorda" || item.name == "Pedal_Sostenuto" || item.name == "Pedal_Sustain")
                { pedals.Add(item); pedalNames.Add(item.name); }
            }
            if (pedals.Count == 0)
            {
                pedalView.pedalTransforms = Array.Empty<Transform>();
                pedalView.pedalPivotPositions = Array.Empty<Vector3>();
                pedalView.pedalRotationAxes = Array.Empty<Vector3>();
                pedalView.pedalPressAngles = Array.Empty<float>();
                EditorUtility.SetDirty(pedalView);
                return;
            }
            if (pedals.Count != 3 || pedalNames.Count != 3)
                throw new InvalidOperationException("钢琴模型踏板须为空，或含独立的Pedal_UnaCorda、Pedal_Sostenuto、Pedal_Sustain各一个，实际: " + pedals.Count);
            // Use the piano's X direction, so rotating the entire scene instance preserves physical left/right.
            pedals.Sort((a, b) => presentation.InverseTransformPoint(a.position).x.CompareTo(presentation.InverseTransformPoint(b.position).x));
            pedalView.pedalTransforms = pedals.ToArray();
            pedalView.pedalPivotPositions = new Vector3[3];
            pedalView.pedalRotationAxes = new Vector3[3];
            pedalView.pedalPressAngles = new float[3];
            for (int i = 0; i < 3; i++)
            {
                Transform pedal = pedals[i];
                Renderer[] meshes = pedal.GetComponentsInChildren<Renderer>(true);
                if (meshes.Length != 1) throw new InvalidOperationException("踏板需要单一Renderer: " + pedal.name);
                Bounds bounds = BoundsIn(meshes[0], presentation);
                Vector3 pivot = new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);
                pedalView.pedalPivotPositions[i] = pedal.parent.InverseTransformPoint(presentation.TransformPoint(pivot));
                pedalView.pedalRotationAxes[i] = pedal.parent.InverseTransformVector(presentation.TransformVector(Vector3.right)).normalized;
                pedalView.pedalPressAngles[i] = PressAngle(0.012f, bounds.size.z);
            }
            pedalView.smoothingMs = 80;
            EditorUtility.SetDirty(pedalView);
        }

        private static float PressAngle(float depth, float leverLength)
        {
            if (leverLength <= 0.0001f) throw new InvalidOperationException("铰链运动的几何长度无效");
            return -Mathf.Asin(Mathf.Clamp(depth / leverLength, 0f, 0.9f)) * Mathf.Rad2Deg;
        }

        public static Bounds BoundsIn(Renderer renderer, Transform frame)
        {
            Bounds local = renderer.localBounds;
            Bounds result = new Bounds(); bool hasPoint = false;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3((mask & 1) == 0 ? local.min.x : local.max.x,
                    (mask & 2) == 0 ? local.min.y : local.max.y, (mask & 4) == 0 ? local.min.z : local.max.z);
                Vector3 point = frame.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                if (!hasPoint) { result = new Bounds(point, Vector3.zero); hasPoint = true; } else result.Encapsulate(point);
            }
            return result;
        }

        internal static bool HasModelSource(GameObject instance, string sourcePath)
        {
            // A model nested in a scene Prefab first resolves to the outer Prefab.
            // Follow every source level so reapplying configuration preserves it.
            UnityEngine.Object current = instance;
            var visited = new System.Collections.Generic.HashSet<UnityEngine.Object>();
            while (current != null && visited.Add(current))
            {
                if (AssetDatabase.GetAssetPath(current) == sourcePath) return true;
                current = PrefabUtility.GetCorrespondingObjectFromSource(current);
            }
            return false;
        }

        public static Bounds ModelBounds(Transform presentation)
        {
            Transform host = presentation.Find("PianoModel");
            Bounds result = new Bounds(); bool found = false;
            if (host != null)
                foreach (Renderer renderer in host.GetComponentsInChildren<Renderer>())
                {
                    Bounds next = BoundsIn(renderer, presentation);
                    if (!found) { result = next; found = true; } else result.Encapsulate(next);
                }
            return result;
        }

        private static bool TryPitch(string name, out int pitch)
        {
            pitch = 0;
            return name.Length >= 7 && name.StartsWith("Key_", StringComparison.Ordinal)
                && (name.Length == 7 || name[7] == '_') && int.TryParse(name.Substring(4, 3), out pitch) && pitch >= 21 && pitch <= 108;
        }

        private static Material PressedMaterial(GameObject model, Material source, bool black)
        {
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Materials");
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(model));
            string path = "Assets/UdonMidiPlayer/Materials/ModelPressed" + (black ? "Black_" : "White_") + guid + ".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(source); AssetDatabase.CreateAsset(result, path); }
            else { result.shader = source.shader; result.CopyPropertiesFromMaterial(source); }
            result.name = System.IO.Path.GetFileNameWithoutExtension(path);
            Color tint = black ? new Color(0.08f, 0.4f, 0.78f) : new Color(0.25f, 0.82f, 0.95f);
            if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", Color.Lerp(source.GetColor("_BaseColor"), tint, 0.55f));
            if (result.HasProperty("_Color")) result.SetColor("_Color", Color.Lerp(source.GetColor("_Color"), tint, 0.55f));
            if (result.HasProperty("_EmissionColor")) { result.SetColor("_EmissionColor", tint * 0.3f); result.EnableKeyword("_EMISSION"); }
            EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result); return result;
        }
    }
}
