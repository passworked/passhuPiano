using System;
using System.Collections.Generic;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Import;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPracticeBuilder
    {
        public const string ScenePath = "Assets/UdonMidiPlayer/Samples/MidiPractice.unity";
        public static string UpgradePlayers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first");
            var songs = new Dictionary<string, MidiSongAsset>();
            int variableSignatures = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:MidiSongAsset"))
            {
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(AssetDatabase.GUIDToAssetPath(guid));
                string path = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
                if (string.IsNullOrEmpty(path)) path = song.sourcePath;
                var parsed = SmfParser.Parse(System.IO.File.ReadAllBytes(path), song.importSettings);
                song.measureStartMs = parsed.measureStartMs;
                EditorUtility.SetDirty(song);
                AssetDatabase.SaveAssetIfDirty(song);
                songs[song.songId] = song;
                if (parsed.tempoChangeCount > 1) variableSignatures++;
            }
            int players = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UdonMidiPlayer/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (UpgradeRoot(root, songs)) { PrefabUtility.SaveAsPrefabAsset(root, path); players++; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string path in new[] { MidiPlayerBuilder.DemoScenePath, "Assets/Scenes/VRCDefaultWorldScene.unity" })
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                bool wasDirty = scene.isDirty, changed = false;
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects()) changed |= UpgradeRoot(root, songs);
                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!wasDirty) EditorSceneManager.SaveScene(scene);
                    }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets();
            return "Upgraded " + songs.Count + " songs and " + players + " prefabs; " + variableSignatures + " songs contain multiple tempo events.";
        }
        private static bool UpgradeRoot(GameObject root, Dictionary<string, MidiSongAsset> songs)
        {
            var players = root.GetComponentsInChildren<MidiPlayerFacade>(true);
            if (players.Length == 0) return false;
            foreach (var player in players)
            {
                foreach (MidiSongData data in player.library.songs)
                {
                    MidiSongAsset source;
                    if (!songs.TryGetValue(data.songId, out source)) throw new InvalidOperationException("Missing source: " + data.title);
                    data.measureStartMs = source.measureStartMs; data.trackNames = source.trackNames;
                    UdonSharpEditorUtility.CopyProxyToUdon(data, ProxySerializationPolicy.All);
                    EditorUtility.SetDirty(data);
                }
                MidiPlayerLocalizationBuilder.Configure(player.playerUI);
                UdonSharpEditorUtility.CopyProxyToUdon(player.playerUI, ProxySerializationPolicy.All);
                EditorUtility.SetDirty(player.playerUI);
            }
            return true;
        }

        public static string CreatePracticeScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) throw new InvalidOperationException("Practice scene already exists");
            MidiPlayerBuilder.EnsurePrograms();
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MidiPlayerBuilder.PrefabPath);
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                root.name = "MIDI Practice";
                var player = root.GetComponentInChildren<MidiPlayerFacade>(true);
                var practice = player.gameObject.AddUdonSharpComponent<MidiPracticeController>();
                practice.facade = player; player.practice = practice;
                player.clock.practiceMode = true;
                player.voicePool.practice = practice; player.voicePool.useDspScheduling = false;
                // Practice is personal; the public player transport stays inactive in this scene.
                player.network.enabled = false;
                UdonSharpEditorUtility.GetBackingUdonBehaviour(player.network).enabled = false;
                player.playerUI.transform.position = new Vector3(-1.85f, 1.6f, -0.82f);
                BuildPanel(practice);
                foreach (var behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
                    UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(-1.3f, 1.8f, -4.8f);
                camera.transform.rotation = Quaternion.Euler(8, 0, 0);
                var light = new GameObject("Directional Light", typeof(Light));
                light.GetComponent<Light>().type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(45, -30, 0);
                var spawn = new GameObject("Practice Spawn");
                spawn.transform.position = new Vector3(-1.2f, 0.05f, -2.5f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Practice Floor";
                floor.transform.position = new Vector3(0, -0.1f, 0);
                floor.transform.localScale = new Vector3(12, 0.2f, 12);
                var descriptor = root.GetComponentInChildren<VRC.SDK3.Components.VRCSceneDescriptor>(true);
                if (descriptor == null) descriptor = root.AddComponent<VRC.SDK3.Components.VRCSceneDescriptor>();
                descriptor.spawns = new[] { spawn.transform };
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
            return ScenePath;
        }

        private static readonly Color Panel = new Color32(55, 66, 79, 255);
        private static readonly Color Tile = new Color32(37, 49, 62, 255);
        private static readonly Color Inset = new Color32(8, 13, 12, 255);
        private static readonly Color Accent = new Color32(135, 226, 229, 255);
        private static readonly Color Muted = new Color32(186, 200, 209, 255);

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = MidiPlayerUISkinAssets.Rounded; image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2; image.color = color; image.raycastTarget = false; return image;
        }
        private static TMP_Text Text(Transform parent, string name, string text, float x, float y, float width, TMP_FontAsset font, int size = 20)
        {
            var label = Rect(parent, name, x, y, width, 32).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size; label.color = Muted;
            label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false;
            label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true; label.fontSizeMin = 14; label.fontSizeMax = size; return label;
        }
        private static TMP_Text Button(Transform parent, MidiPracticeController practice, string name, string method, float x, float y, float width, TMP_FontAsset font)
        {
            Image image = Box(parent, name, x, y, width, 42, Inset); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Inset; colors.highlightedColor = new Color32(52, 72, 82, 255);
            colors.pressedColor = new Color32(27, 36, 44, 255); colors.disabledColor = new Color32(34, 44, 50, 100); colors.fadeDuration = 0.08f; button.colors = colors;
            Navigation nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            UnityAction<string> action = UdonSharpEditorUtility.GetBackingUdonBehaviour(practice).SendCustomEvent; UnityEventTools.AddStringPersistentListener(button.onClick, action, method);
            var text = Text(image.transform, "Label", name, 0, 0, width - 16, font); text.alignment = TextAlignmentOptions.Center; text.color = Accent; return text;
        }
        private static Slider Slider(Transform parent, MidiPracticeController practice, string name, string method, float x, float y, float width)
        {
            var rect = Rect(parent, name, x, y, width, 34); var hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var slider = rect.gameObject.AddComponent<UnityEngine.UI.Slider>();
            Box(rect, "Track", 0, 0, width, 6, Inset);
            var fillArea = Rect(rect, "FillArea", 0, 0, width - 14, 6);
            var fill = Box(fillArea, "Fill", 0, 0, width - 14, 6, Accent).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            var area = Rect(rect, "HandleArea", 0, 0, width - 14, 16);
            var handle = Box(area, "Handle", 0, 0, 14, 0, Accent);
            handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = new Vector2(0, 0.5f);
            slider.fillRect = fill; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            var colors = slider.colors; colors.disabledColor = new Color(1, 1, 1, 0.25f); slider.colors = colors;
            Navigation nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
            slider.onValueChanged = new Slider.SliderEvent(); UnityAction<string> action = UdonSharpEditorUtility.GetBackingUdonBehaviour(practice).SendCustomEvent;
            UnityEventTools.AddStringPersistentListener(slider.onValueChanged, action, method); return slider;
        }
        private static void Localize(MusicMidiPlayer.UI.MidiPlayerUI ui, TMP_Text label, string key)
        {
            var labels = new List<TMP_Text>(ui.localizedLabels); labels.Add(label); ui.localizedLabels = labels.ToArray();
            var keys = new List<string>(ui.localizedKeys); keys.Add(key); ui.localizedKeys = keys.ToArray();
        }
        public static void BuildPanel(MidiPracticeController practice)
        {
            var ui = practice.facade.playerUI; TMP_FontAsset font = ui.titleLabel.font;
            // Replace only the practice-owned controls; preserve transport, library and settings references.
            var labels = new List<TMP_Text>(); var keys = new List<string>();
            for (int i = 0; i < ui.localizedKeys.Length; i++) if (!ui.localizedKeys[i].StartsWith("practice.")) { labels.Add(ui.localizedLabels[i]); keys.Add(ui.localizedKeys[i]); }
            ui.localizedLabels = labels.ToArray(); ui.localizedKeys = keys.ToArray();
            foreach (string name in new[] { "PracticePanel", "PracticeBackground" }) { var old = ui.transform.Find(name); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }
            ui.GetComponent<Image>().color = Color.clear;
            var background = Box(ui.transform, "PracticeBackground", 0, -222, 1000, 924, Panel); background.transform.SetAsFirstSibling();
            var shadow = background.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, 0.24f); shadow.effectDistance = new Vector2(0, -7);
            var panel = Rect(ui.transform, "PracticePanel", 0, -464, 1000, 440);
            var heading = Text(panel, "Title", "MIDI PRACTICE", -295, 195, 360, font, 21); heading.color = Color.white; heading.fontStyle = FontStyles.Bold; heading.characterSpacing = 1.5f;
            Localize(ui, heading, "practice.title");
            practice.summaryLabel = Text(panel, "Status", "", 285, 195, 380, font, 17); practice.summaryLabel.alignment = TextAlignmentOptions.MidlineRight;
            Box(panel, "Divider", 0, 167, 952, 1, new Color(1, 1, 1, 0.09f));
            Box(panel, "ModeCard", -242, 59, 464, 188, Tile);
            Box(panel, "LoopCard", 242, 59, 464, 188, Tile);
            Box(panel, "TrackCard", 0, -132, 952, 164, Tile);
            var blocking = Button(panel, practice, "Blocking", "_UseBlockingMode", -352, 113, 204, font);
            var streaming = Button(panel, practice, "Streaming", "_UseStreamingMode", -132, 113, 204, font);
            Localize(ui, blocking, "practice.blocking"); Localize(ui, streaming, "practice.streaming");
            practice.blockingButton = blocking.transform.parent.GetComponent<UnityEngine.UI.Button>();
            practice.streamingButton = streaming.transform.parent.GetComponent<UnityEngine.UI.Button>(); practice.modeLabel = null;
            var speedTitle = Text(panel, "SpeedTitle", "SPEED", -334, 59, 240, font, 17); Localize(ui, speedTitle, "practice.speed");
            practice.speedLabel = Text(panel, "SpeedValue", "1×", -94, 59, 124, font, 23); practice.speedLabel.alignment = TextAlignmentOptions.MidlineRight; practice.speedLabel.color = Accent;
            practice.speedSlider = Slider(panel, practice, "Speed", "_SpeedChanged", -242, 18, 424);
            practice.speedSlider.minValue = 0.25f; practice.speedSlider.maxValue = 2; practice.speedSlider.SetValueWithoutNotify(practice.streamSpeed);
            Text(panel, "SpeedMinimum", "0.25×", -400, -15, 108, font, 14);
            var maximum = Text(panel, "SpeedMaximum", "2×", -86, -15, 108, font, 14); maximum.alignment = TextAlignmentOptions.MidlineRight;
            practice.loopLabel = Button(panel, practice, "Loop", "_ToggleLoop", 131, 113, 204, font);
            practice.loopUnitLabel = Button(panel, practice, "LoopUnit", "_ToggleLoopUnit", 352, 113, 204, font);
            practice.loopStartLabel = Text(panel, "AValue", "A 00:00.00", 102, 53, 144, font, 18); practice.loopStartLabel.color = Accent;
            practice.loopEndLabel = Text(panel, "BValue", "B 00:00.00", 102, -3, 144, font, 18); practice.loopEndLabel.color = Accent;
            practice.loopStartSlider = Slider(panel, practice, "LoopA", "_LoopStartChanged", 277, 53, 188);
            practice.loopEndSlider = Slider(panel, practice, "LoopB", "_LoopEndChanged", 277, -3, 188); practice.loopEndSlider.SetValueWithoutNotify(1);
            Localize(ui, Button(panel, practice, "SetA", "_SetA", 421, 53, 66, font), "practice.mark_a");
            Localize(ui, Button(panel, practice, "SetB", "_SetB", 421, -3, 66, font), "practice.mark_b");
            Localize(ui, Text(panel, "TrackHeading", "PRACTICE TRACKS", -280, -83, 344, font, 17), "practice.tracks");
            practice.countdownLabel = Button(panel, practice, "Countdown", "_ToggleCountdown", 351, -83, 204, font);
            Button(panel, practice, "PreviousTrack", "_PreviousTrack", -430, -137, 48, font).text = "‹";
            practice.trackLabel = Text(panel, "Track", "", -230, -137, 330, font, 21); practice.trackLabel.color = Color.white;
            Button(panel, practice, "NextTrack", "_NextTrack", -30, -137, 48, font).text = "›";
            var target = Button(panel, practice, "ToggleTrack", "_ToggleTrack", 110, -137, 184, font); Localize(ui, target, "practice.toggle_track"); practice.trackButton = target.transform.parent.GetComponent<UnityEngine.UI.Button>();
            Localize(ui, Button(panel, practice, "AllTracks", "_AllTracks", 279, -137, 130, font), "practice.all_tracks");
            Localize(ui, Button(panel, practice, "NoTracks", "_NoTracks", 405, -137, 98, font), "practice.no_tracks");
            Localize(ui, Text(panel, "TrackHint", "", 0, -186, 904, font, 15), "practice.track_hint");
            var collider = ui.GetComponent<BoxCollider>(); collider.center = new Vector3(0, -222, 0); collider.size = new Vector3(1000, 924, 1);
            var back = ui.transform.Find("Backplate") as RectTransform;
            if (back != null) { back.offsetMin = new Vector2(0, -444); back.offsetMax = Vector2.zero; back.SetAsLastSibling(); }
            ui.ApplyLanguage(); practice.RefreshUI();
        }
    }
}
