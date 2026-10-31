using System;
using System.Collections.Generic;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.UI;
using MusicMidiPlayer.Views;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPerformanceBuilder
    {
        public static void Configure(MidiPlayerFacade facade)
        {
            Transform host = Child(facade.transform.parent, "PerformanceInput");
            var input = host.GetComponent<MidiPerformanceInput>();
            if (input == null) input = host.gameObject.AddUdonSharpComponent<MidiPerformanceInput>();
            input.facade = facade; facade.performanceInput = input;
            var keyboards = facade.instrumentKeyboards;
            if (keyboards == null || keyboards.Length == 0) keyboards = new[] { facade.keyboard };
            input.instrumentFrames = new Transform[keyboards.Length];
            input.instrumentMinimum = new Vector3[keyboards.Length];
            input.instrumentMaximum = new Vector3[keyboards.Length];
            input.keyMinimum = new Vector3[keyboards.Length * 88];
            input.keyMaximum = new Vector3[keyboards.Length * 88];
            for (int n = 0; n < keyboards.Length; n++)
            {
                PianoKeyboardView keyboard = keyboards[n];
                if (keyboard == null && facade.GetStringsView(n) != null)
                {
                    Transform stringsFrame = facade.instrumentRoots[n].transform;
                    input.instrumentFrames[n] = stringsFrame;
                    // A seat keeps desktop MIDI input available; strings have no keyboard touch geometry.
                    ConfigureSeat(stringsFrame, input, n, new Bounds(new Vector3(0, .85f, -.2f), new Vector3(.6f, .1f, .1f)));
                    continue;
                }
                if (keyboard == null) throw new InvalidOperationException("Missing performance keyboard " + n);
                Transform frame = facade.instrumentRoots != null && n < facade.instrumentRoots.Length
                    ? facade.instrumentRoots[n].transform : keyboard.transform.parent.name == "ViewRoot"
                        ? keyboard.transform.parent.parent : keyboard.transform.parent;
                input.instrumentFrames[n] = frame;
                Bounds all = new Bounds();
                bool hasKeys = false;
                for (int k = 0; k < 88; k++)
                {
                    Renderer renderer = keyboard.keyRenderers[k];
                    // Marimba has 61 physical bars; absent MIDI pitches have no touch surface.
                    if (renderer == null) continue;
                    Bounds bounds = KeyBounds(renderer, frame);
                    input.keyMinimum[n * 88 + k] = bounds.min;
                    input.keyMaximum[n * 88 + k] = bounds.max;
                    if (!hasKeys) { all = bounds; hasKeys = true; } else all.Encapsulate(bounds);
                }
                if (!hasKeys) throw new InvalidOperationException("No playable key geometry: " + keyboard.name);
                input.instrumentMinimum[n] = all.min; input.instrumentMaximum[n] = all.max;
                Transform priorSeat = keyboard.transform.parent.Find("PerformanceSeat");
                if (priorSeat != null && priorSeat.parent != frame && frame.Find("PerformanceSeat") == null)
                    priorSeat.SetParent(frame, false);
                ConfigureSeat(frame, input, n, all);
            }
            if (facade.playerUI != null)
            {
                ConfigureSettings(facade.playerUI);
                RegisterLabels(facade.playerUI);
                facade.playerUI.ApplyLanguage();
                Save(facade.playerUI);
            }
            Save(input); Save(facade);
        }

        private static Bounds KeyBounds(Renderer renderer, Transform frame)
        {
            // Some imported key renderers are containers. Include their actual mesh children.
            bool found = false;
            Bounds result = new Bounds();
            foreach (MeshFilter filter in renderer.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 point = frame.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; } else result.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidOperationException("No key geometry: " + renderer.name);
            return result;
        }

        private static void ConfigureSeat(Transform frame, MidiPerformanceInput input, int instrument, Bounds keys)
        {
            Transform chair = Child(frame, "PerformanceSeat");
            float height = Mathf.Clamp(keys.max.y - 0.31f, 0.43f, 0.66f);
            chair.localPosition = new Vector3(keys.center.x, 0, keys.min.z - 0.43f);
            chair.localRotation = Quaternion.identity;
            var seat = chair.GetComponent<MidiPerformanceSeat>();
            if (seat == null) seat = chair.gameObject.AddUdonSharpComponent<MidiPerformanceSeat>();
            var station = chair.GetComponent<VRC.SDK3.Components.VRCStation>();
            if (station == null) station = chair.gameObject.AddComponent<VRC.SDK3.Components.VRCStation>();
            station.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.Immobilize;
            station.seated = true; station.disableStationExit = false;
            Transform enter = Child(chair, "Enter"); enter.localPosition = new Vector3(0, height, 0);
            Transform exit = Child(chair, "Exit"); exit.localPosition = new Vector3(0.65f, 0.05f, -0.25f);
            station.stationEnterPlayerLocation = enter; station.stationExitPlayerLocation = exit;
            var collider = chair.GetComponent<BoxCollider>();
            if (collider == null) collider = chair.gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true; collider.center = new Vector3(0, height - 0.04f, 0); collider.size = new Vector3(0.62f, 0.15f, 0.36f);
            seat.input = input; seat.station = station; seat.instrumentIndex = instrument;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(seat);
            backing.interactText = "Sit / Play · 坐下演奏"; backing.proximity = 1.5f;
            Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/UdonMidiPlayer/Materials/PerformanceSeat.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Standard")); material.color = new Color(0.055f, 0.065f, 0.075f);
                material.SetFloat("_Glossiness", 0.25f);
                AssetDatabase.CreateAsset(material, "Assets/UdonMidiPlayer/Materials/PerformanceSeat.mat");
            }
            Cube(chair, "Cushion", new Vector3(0, height - 0.05f, 0), new Vector3(0.6f, 0.1f, 0.34f), material);
            for (int l = 0; l < 4; l++) Cube(chair, "Leg" + l,
                new Vector3((l & 1) == 0 ? -0.24f : 0.24f, (height - 0.1f) * 0.5f, (l & 2) == 0 ? -0.12f : 0.12f),
                new Vector3(0.045f, height - 0.1f, 0.045f), material);
            Save(seat);
        }

        private static void Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); t = cube.transform; t.SetParent(parent, false); t.name = name;
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }
            t.localPosition = position; t.localScale = scale; t.GetComponent<Renderer>().sharedMaterial = material;
        }

        public static void ConfigureSettings(MidiPlayerUI ui)
        {
            if (ui.settingsPanel == null || ui.facade == null || ui.facade.performanceInput == null) return;
            var input = ui.facade.performanceInput;
            // Settings stays inside the existing 1000 x 480 canvas, with a second column for input.
            var existing = ui.settingsPanel.transform.Find("Content") as RectTransform;
            existing.anchoredPosition = new Vector2(-490, 0);
            string[] vrRows = { "StickScrollSensitivityLabel", "StickScrollSensitivityValue", "StickScrollSensitivity", "StickScrollMinimum", "StickScrollMaximum" };
            input.vrOnlySettings = new GameObject[vrRows.Length];
            for (int i = 0; i < vrRows.Length; i++) input.vrOnlySettings[i] = existing.Find(vrRows[i]).gameObject;
            string[] commonRows = { "AudioSourceLabel", "WorldAudio", "TwoDAudio", "MeasureSnap", "PeakProtection" };
            input.commonSettingsRows = new RectTransform[commonRows.Length]; input.commonSettingsPositions = new Vector2[commonRows.Length];
            for (int i = 0; i < commonRows.Length; i++)
            {
                input.commonSettingsRows[i] = (RectTransform)existing.Find(commonRows[i]);
                if (input.commonSettingsRows[i] != null) input.commonSettingsPositions[i] = input.commonSettingsRows[i].anchoredPosition;
            }
            Image overlayImage = ui.settingsPanel.GetComponent<Image>();
            MatchImage(overlayImage, ui.GetComponent<Image>());
            overlayImage.raycastTarget = true;
            EditorUtility.SetDirty(overlayImage);
            if (PrefabUtility.IsPartOfPrefabInstance(overlayImage)) PrefabUtility.RecordPrefabInstancePropertyModifications(overlayImage);
            Image panelStyle = existing.GetComponent<Image>();
            Image optionStyle = existing.Find("CurrentLanding").GetComponent<Image>();
            Button optionButton = existing.Find("CurrentLanding").GetComponent<Button>();
            TMP_Text titleStyle = existing.Find("Title").GetComponent<TMP_Text>();
            TMP_Text sectionStyle = existing.Find("NoteLandingLabel").GetComponent<TMP_Text>();
            TMP_Text optionLabelStyle = existing.Find("CurrentLanding/Label").GetComponent<TMP_Text>();
            Image selectionStyle = ui.currentLandingSelectionMark.GetComponent<Image>();
            TMP_Text hintStyle = existing.Find("StickScrollMinimum").GetComponent<TMP_Text>();
            RectTransform panel = Rect(ui.settingsPanel.transform, "Performance", 260, 0, 460, 480);
            Image bg = Get<Image>(panel); MatchImage(bg, panelStyle); bg.raycastTarget = true;
            var blocker = Get<Button>(panel); blocker.transition = Selectable.Transition.None; blocker.targetGraphic = bg;
            RectTransform vr = Rect(panel, "VR", 0, 0, 460, 480);
            RectTransform desktop = Rect(panel, "Desktop", 0, 0, 460, 480);
            foreach (string name in new[] { "Title", "Left", "Right" }) MoveLegacy(panel, vr, name);
            foreach (string name in new[] { "DesktopTitle", "Hint" }) MoveLegacy(panel, desktop, name);
            MatchLabel(Label(vr, "Title", "VR FINGERS", 0, 214, 412, 30, ui.titleLabel.font, 22), titleStyle);
            var leftLabel = Label(vr, "Left", "LEFT HAND", -106, 177, 184, 24, ui.titleLabel.font, 18);
            MatchLabel(leftLabel, sectionStyle); leftLabel.alignment = TextAlignmentOptions.Center;
            var rightLabel = Label(vr, "Right", "RIGHT HAND", 106, 177, 184, 24, ui.titleLabel.font, 18);
            MatchLabel(rightLabel, sectionStyle); rightLabel.alignment = TextAlignmentOptions.Center;
            Sprite handSprite = MidiPerformanceHandAssets.Get();
            for (int hand = 0; hand < 2; hand++)
            {
                var rect = Rect(vr, "Hand" + hand, hand == 0 ? -106 : 106, 20, 184, 320);
                rect.localScale = new Vector3(hand == 0 ? 1 : -1, 1, 1);
                Image diagram = Get<Image>(rect); diagram.sprite = handSprite; diagram.type = Image.Type.Simple;
                diagram.color = hintStyle.color; diagram.raycastTarget = false; rect.SetAsFirstSibling();
            }
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            Vector2[] tips = { new Vector2(70, 5), new Vector2(33, 112), new Vector2(-5, 152), new Vector2(-42, 120), new Vector2(-77, 66) };
            input.fingerToggles = new Toggle[10];
            for (int f = 0; f < 10; f++)
            {
                MoveLegacy(panel, vr, "Finger" + f);
                Vector2 tip = tips[f % 5];
                float x = f < 5 ? -106 + tip.x : 106 - tip.x;
                RectTransform row = Rect(vr, "Finger" + f, x, tip.y, 44, 30);
                Image background = Get<Image>(row); MatchImage(background, optionStyle); background.raycastTarget = true;
                Toggle toggle = Get<Toggle>(row); toggle.targetGraphic = background;
                toggle.transition = optionButton.transition; toggle.colors = optionButton.colors;
                RectTransform check = Rect(row, "Check", 0, -11, 30, 3);
                Image mark = Get<Image>(check); MatchImage(mark, selectionStyle); mark.raycastTarget = false;
                toggle.graphic = mark; toggle.SetIsOnWithoutNotify(input.enabledFingers[f]);
                Navigation nav = toggle.navigation; nav.mode = Navigation.Mode.None; toggle.navigation = nav;
                toggle.onValueChanged = new Toggle.ToggleEvent();
                UnityEventTools.AddStringPersistentListener(toggle.onValueChanged, UdonSharpEditorUtility.GetBackingUdonBehaviour(input).SendCustomEvent, "_OnFingersChanged");
                var fingerLabel = Label(row, "Label", fingers[f % 5], 0, 2, 40, 22, ui.titleLabel.font, 13);
                MatchLabel(fingerLabel, optionLabelStyle); fingerLabel.fontSize = fingerLabel.fontSizeMax = 13; fingerLabel.fontSizeMin = 10;
                input.fingerToggles[f] = toggle;
            }
            var fingerHint = Label(vr, "FingerHint", "Tap fingertips to choose your fingers", 0, -154, 412, 26, ui.titleLabel.font, 15);
            MatchLabel(fingerHint, hintStyle); fingerHint.alignment = TextAlignmentOptions.Center;
            ActionButton(vr, input, "SelectAll", "Select all", "_SelectAllFingers", -108, -200, optionStyle, optionButton, optionLabelStyle);
            ActionButton(vr, input, "ClearAll", "Clear all", "_ClearAllFingers", 108, -200, optionStyle, optionButton, optionLabelStyle);
            MatchLabel(Label(desktop, "DesktopTitle", "PC · SIT TO PLAY", 0, 214, 412, 30, ui.titleLabel.font, 22), titleStyle);
            var help = Label(desktop, "Help", "Sit on the instrument's chair to play with your keyboard.", 0, 151, 412, 52, ui.titleLabel.font, 18);
            MatchLabel(help, sectionStyle); help.enableWordWrapping = true;
            // Fixed key columns keep the two rows aligned with proportional localized UI fonts.
            var oldMap = panel.Find("KeyMap"); if (oldMap != null) oldMap.gameObject.SetActive(false);
            string[] whiteKeys = { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" };
            string[] blackKeys = { "W", "E", "T", "Y", "U", "O", "P" };
            float[] blackColumns = { 0.5f, 1.5f, 3.5f, 4.5f, 5.5f, 7.5f, 8.5f };
            for (int i = 0; i < whiteKeys.Length; i++)
            {
                MoveLegacy(panel, desktop, "WhiteKey" + i);
                KeyCap(desktop, "WhiteKey" + i, whiteKeys[i], -186 + i * 37.2f, 51, optionStyle, optionLabelStyle);
            }
            for (int i = 0; i < blackKeys.Length; i++)
            {
                MoveLegacy(panel, desktop, "BlackKey" + i);
                KeyCap(desktop, "BlackKey" + i, blackKeys[i], -186 + blackColumns[i] * 37.2f, 88, optionStyle, optionLabelStyle);
            }
            var hint = Label(desktop, "Hint", "Z / X: octave · Space: sustain\nEnter: leave seat", 0, -62, 412, 82, ui.titleLabel.font, 18);
            MatchLabel(hint, hintStyle); hint.enableWordWrapping = true; hint.fontSize = hint.fontSizeMax = 18; hint.lineSpacing = 18;
            input.vrSettings = vr.gameObject; input.desktopSettings = desktop.gameObject;
            // No wrong-device flash before the local player API is ready.
            vr.gameObject.SetActive(false); desktop.gameObject.SetActive(false);
            foreach (Component component in panel.GetComponentsInChildren<Component>(true))
            {
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            Save(input);
        }

        private static void MoveLegacy(Transform oldParent, Transform newParent, string name)
        {
            Transform child = oldParent.Find(name);
            if (child != null && newParent.Find(name) == null) child.SetParent(newParent, false);
        }

        private static void ActionButton(Transform parent, MidiPerformanceInput input, string name, string text,
            string method, float x, float y, Image imageStyle, Button buttonStyle, TMP_Text labelStyle)
        {
            var rect = Rect(parent, name, x, y, 196, 38); var image = Get<Image>(rect); MatchImage(image, imageStyle); image.raycastTarget = true;
            var button = Get<Button>(rect); button.targetGraphic = image; button.colors = buttonStyle.colors; button.transition = buttonStyle.transition;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(input).SendCustomEvent, method);
            MatchLabel(Label(rect, "Label", text, 0, 0, 180, 30, labelStyle.font, 17), labelStyle);
        }

        private static void MatchImage(Image target, Image source)
        {
            target.sprite = source.sprite; target.type = source.type; target.color = source.color;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }

        private static void MatchLabel(TMP_Text target, TMP_Text source)
        {
            target.font = source.font; target.fontSharedMaterial = source.fontSharedMaterial;
            target.color = source.color; target.fontSize = source.fontSize; target.fontStyle = source.fontStyle;
            target.alignment = source.alignment; target.characterSpacing = source.characterSpacing;
            target.enableWordWrapping = source.enableWordWrapping; target.overflowMode = source.overflowMode;
            target.enableAutoSizing = true; target.fontSizeMin = 12; target.fontSizeMax = source.fontSize;
        }

        private static void KeyCap(Transform parent, string name, string text, float x, float y, Image backgroundStyle, TMP_Text labelStyle)
        {
            RectTransform key = Rect(parent, name, x, y, 32, 30);
            Image image = Get<Image>(key); MatchImage(image, backgroundStyle); image.raycastTarget = false;
            MatchLabel(Label(key, "Label", text, 0, 0, 30, 22, labelStyle.font, 17), labelStyle);
        }

        public static void RegisterLabels(MidiPlayerUI ui)
        {
            Transform panel = ui.transform.Find("SettingsPanel/Performance");
            if (panel == null) return;
            var labels = new List<TMP_Text>(ui.localizedLabels); var keys = new List<string>(ui.localizedKeys);
            string[] paths = { "VR/Title", "VR/Left", "VR/Right", "Desktop/DesktopTitle", "Desktop/Hint",
                "VR/FingerHint", "VR/SelectAll/Label", "VR/ClearAll/Label", "Desktop/Help" };
            string[] ids = { "input.vr_fingers", "input.left", "input.right", "input.desktop", "input.hint",
                "input.finger_hint", "input.select_all", "input.clear_all", "input.pc_help" };
            for (int i = 0; i < paths.Length; i++) AddLabel(labels, keys, panel.Find(paths[i]).GetComponent<TMP_Text>(), ids[i]);
            string[] fingers = { "thumb", "index", "middle", "ring", "little" };
            for (int i = 0; i < 10; i++) AddLabel(labels, keys, panel.Find("VR/Finger" + i + "/Label").GetComponent<TMP_Text>(), "input." + fingers[i % 5]);
            ui.localizedLabels = labels.ToArray(); ui.localizedKeys = keys.ToArray();
        }

        private static void AddLabel(List<TMP_Text> labels, List<string> keys, TMP_Text label, string key)
        { if (!labels.Contains(label)) { labels.Add(label); keys.Add(key); } }
        private static Transform Child(Transform parent, string name)
        { Transform t = parent.Find(name); if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); } return t; }
        private static T Get<T>(Component parent) where T : Component
        { T c = parent.GetComponent<T>(); return c != null ? c : parent.gameObject.AddComponent<T>(); }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            Transform t = parent.Find(name);
            if (t == null) { t = new GameObject(name, typeof(RectTransform)).transform; t.SetParent(parent, false); }
            var r = (RectTransform)t; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); return r;
        }
        private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, TMP_FontAsset font, int size)
        {
            var label = Get<TextMeshProUGUI>(Rect(parent, name, x, y, w, h));
            label.font = font; label.text = text; label.fontSize = size; label.color = new Color32(210, 229, 235, 255);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.enableWordWrapping = true; label.raycastTarget = false;
            label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = size;
            return label;
        }
        private static void Save(UdonSharpBehaviour behaviour)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All); EditorUtility.SetDirty(behaviour);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour); EditorUtility.SetDirty(backing);
            if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            if (PrefabUtility.IsPartOfPrefabInstance(backing)) PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
        }

        public static string UpgradeAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            MidiPlayerBuilder.EnsurePrograms();
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UdonMidiPlayer/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid); GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var player in root.GetComponentsInChildren<MidiPlayerFacade>(true)) { Configure(player); count++; }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string path in new[] { "Assets/Scenes/VRCDefaultWorldScene.unity", MidiPlayerBuilder.DemoScenePath, MidiPracticeBuilder.ScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                // Newly opened scenes may be dirtied by Udon proxy hydration. Only protect
                // unsaved edits in scenes that were already open before this migration.
                bool dirty = !opened && scene.isDirty;
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (var player in root.GetComponentsInChildren<MidiPlayerFacade>(true)) { Configure(player); count++; }
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!dirty) EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets(); return "Configured VR/PC input on " + count + " players.";
        }
    }
}
