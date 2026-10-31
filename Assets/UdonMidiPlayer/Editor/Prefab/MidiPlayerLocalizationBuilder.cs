using System;
using MusicMidiPlayer.UI;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Upgrades localization in place, without rebuilding playback data or user content.</summary>
    public static class MidiPlayerLocalizationBuilder
    {
        public const string LanguageFolder = "Assets/UdonMidiPlayer/Localization/World/";
        public static readonly string[] LanguageCodes = { "en", "zh-Hans", "zh-Hant", "ja" };
        public static readonly string[] SelectionEvents =
        {
            "_SelectLanguageEnglish", "_SelectLanguageChinese", "_SelectLanguageJapanese"
        };
        private static readonly string[] LabelPaths =
        {
            "Brand", "NowPlayingCaption", "Play/Label", "Pause/Label", "Stop/Label",
            "VolumeLabel", "Mute/Label", "LibraryCaption", "SettingsPanel/Content/Title",
            "SettingsPanel/Content/StickScrollSensitivityLabel", "SettingsPanel/Content/NoteLandingLabel", "SettingsPanel/Content/CurrentLanding/Label", "SettingsPanel/Content/KeyRootLanding/Label", "SettingsPanel/Content/AudioSourceLabel", "SettingsPanel/Content/WorldAudio/Label", "SettingsPanel/Content/TwoDAudio/Label", "SettingsPanel/Content/MeasureSnap/Label", "SettingsPanel/Content/PeakProtection/Label"
        };
        private static readonly string[] LabelKeys =
        {
            "player.title", "player.now_playing", "transport.play", "transport.pause", "transport.stop",
            "audio.volume", "audio.mute", "library.caption", "settings.title",
            "settings.stick_scroll_sensitivity", "settings.note_landing", "settings.landing_current", "settings.landing_root", "settings.audio_source", "settings.audio_world", "settings.audio_2d", "settings.measure_snap", "settings.peak_protection"
        };

        public static void Configure(MidiPlayerUI ui)
        {
            BuildSelector(ui);
            MidiPlayerUIBuilder.ConfigureSettings(ui);
            TMP_FontAsset font = ui.titleLabel.font;
            ui.languageFiles = new TextAsset[LanguageCodes.Length];
            for (int i = 0; i < LanguageCodes.Length; i++)
            {
                ui.languageFiles[i] = AssetDatabase.LoadAssetAtPath<TextAsset>(LanguageFolder + LanguageCodes[i] + ".json");
                if (ui.languageFiles[i] == null) throw new InvalidOperationException("Missing MIDI language file: " + LanguageCodes[i]);
            }
            ui.localizedLabels = new TMP_Text[LabelPaths.Length];
            ui.localizedKeys = (string[])LabelKeys.Clone();
            for (int i = 0; i < LabelPaths.Length; i++)
            {
                var label = ui.transform.Find(LabelPaths[i]);
                ui.localizedLabels[i] = label == null ? null : label.GetComponent<TMP_Text>();
                if (ui.localizedLabels[i] == null) throw new InvalidOperationException("Missing MIDI label: " + LabelPaths[i]);
                ui.localizedLabels[i].enableAutoSizing = true;
                ui.localizedLabels[i].fontSizeMin = 12;
                ui.localizedLabels[i].fontSizeMax = ui.localizedLabels[i].fontSize;
            }
            ui.allSongsTitleLabel = ui.allSongsRow.Find("Label").GetComponent<TMP_Text>();
            ui.allSongsSummaryLabel = ui.allSongsRow.Find("Count").GetComponent<TMP_Text>();
            ui.folderSummaryLabels = new TMP_Text[ui.folderButtons.Length];
            ui.folderChildCounts = new int[ui.folderButtons.Length];
            for (int i = 0; i < ui.folderButtons.Length; i++)
            {
                ui.folderSummaryLabels[i] = ui.folderButtons[i].transform.Find("Count").GetComponent<TMP_Text>();
                for (int j = 0; j < ui.folderButtons.Length; j++)
                    if (ui.folderButtons[j].parentFolderIndex == i) ui.folderChildCounts[i]++;
            }
            MidiPerformanceBuilder.RegisterLabels(ui);
            ui.ApplyLanguage();
        }

        public static void Validate(MidiPlayerUI ui)
        {
            if (ui.languageFiles == null || ui.languageFiles.Length != LanguageCodes.Length
                || ui.languageButton == null || ui.languagePanel == null
                || ui.languageOptionButtons == null || ui.languageOptionButtons.Length != SelectionEvents.Length
                || ui.languageSelectionMarks == null || ui.languageSelectionMarks.Length != SelectionEvents.Length)
                throw new InvalidOperationException("MIDI localization is incomplete; update the player UI.");
            for (int i = 0; i < SelectionEvents.Length; i++)
                if (ui.languageOptionButtons[i] == null || ui.languageSelectionMarks[i] == null
                    || !ui.languageOptionButtons[i].transform.IsChildOf(ui.languagePanel.transform))
                    throw new InvalidOperationException("Missing MIDI language icon button: " + i);
            if (ui.localizedLabels == null || ui.localizedKeys == null || ui.localizedLabels.Length < LabelKeys.Length
                || ui.localizedKeys.Length != ui.localizedLabels.Length)
                throw new InvalidOperationException("MIDI localized labels are incomplete.");
            DataList englishKeys = null;
            for (int i = 0; i < LanguageCodes.Length; i++)
            {
                TextAsset file = ui.languageFiles[i];
                if (file == null || AssetDatabase.GetAssetPath(file) != LanguageFolder + LanguageCodes[i] + ".json"
                    || !VRCJson.TryDeserializeFromJson(file.text, out DataToken parsed) || parsed.TokenType != TokenType.DataDictionary)
                    throw new InvalidOperationException("Invalid MIDI language file: " + LanguageCodes[i]);
                DataDictionary dictionary = parsed.DataDictionary;
                if (i == 0) englishKeys = dictionary.GetKeys();
                for (int j = 0; j < englishKeys.Count; j++)
                    if (!dictionary.TryGetValue(englishKeys[j], TokenType.String, out DataToken translation)
                        || string.IsNullOrEmpty(translation.String))
                        throw new InvalidOperationException("Missing MIDI translation: " + LanguageCodes[i] + " / " + englishKeys[j]);
                for (int j = 0; j < LabelKeys.Length; j++)
                    if (ui.localizedLabels[j] == null || ui.localizedKeys[j] != LabelKeys[j]
                        || !dictionary.ContainsKey(LabelKeys[j]))
                        throw new InvalidOperationException("Invalid MIDI localized label: " + LabelKeys[j]);
            }
            if (ui.allSongsTitleLabel == null || ui.allSongsSummaryLabel == null || ui.folderSummaryLabels == null
                || ui.folderChildCounts == null || ui.folderSummaryLabels.Length != ui.folderButtons.Length
                || ui.folderChildCounts.Length != ui.folderButtons.Length)
                throw new InvalidOperationException("MIDI localized library labels are incomplete.");
            for (int i = 0; i < ui.folderButtons.Length; i++)
            {
                int children = 0;
                for (int j = 0; j < ui.folderButtons.Length; j++) if (ui.folderButtons[j].parentFolderIndex == i) children++;
                if (ui.folderSummaryLabels[i] == null || ui.folderChildCounts[i] != children)
                    throw new InvalidOperationException("Invalid MIDI localized folder summary: " + i);
            }
        }

        private static void BuildSelector(MidiPlayerUI ui)
        {
            Transform parent = ui.transform;
            RectTransform entry = Rect(parent, "Language", new Vector2(424, 210), new Vector2(52, 44));
            // Replace the old dropdown in place; its root keeps the same identity.
            var dropdown = entry.GetComponent<TMP_Dropdown>();
            if (dropdown != null) UnityEngine.Object.DestroyImmediate(dropdown);
            foreach (string oldName in new[] { "Label", "Arrow", "Template" })
            {
                Transform old = entry.Find(oldName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            ui.languageButton = Button(entry, ui, "_ToggleLanguagePanel");
            DrawIcon(entry, "Icon", "Language", new Vector2(-18, 0), new Vector2(36, 36), true);

            // A transparent full-panel button closes the palette when clicking outside it.
            RectTransform overlay = Rect(parent, "LanguagePanel", new Vector2(-500, 0), new Vector2(1000, 480));
            var dismiss = Get<Button>(overlay);
            Image hit = PaintImage(overlay, Color.clear);
            hit.sprite = null;
            dismiss.targetGraphic = hit;
            dismiss.transition = Selectable.Transition.None;
            Listen(dismiss, ui, "_CloseLanguagePanel");
            RectTransform panel = Rect(overlay, "Choices", new Vector2(246, 141), new Vector2(230, 74));
            PaintImage(panel, new Color32(37, 49, 62, 255));
            ui.languagePanel = overlay.gameObject;
            ui.languageOptionButtons = new Button[SelectionEvents.Length];
            ui.languageSelectionMarks = new GameObject[SelectionEvents.Length];
            string[] icons = { "en", "zh-Hans", "ja" };
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                Transform child = panel.GetChild(i);
                if (child.name.StartsWith("Option_", StringComparison.Ordinal)
                    && Array.IndexOf(icons, child.name.Substring("Option_".Length)) < 0)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            for (int i = 0; i < icons.Length; i++)
            {
                RectTransform tile = Rect(panel, "Option_" + icons[i], new Vector2(-104 + i * 72, 0), new Vector2(64, 52));
                ui.languageOptionButtons[i] = Button(tile, ui, SelectionEvents[i]);
                DrawIcon(tile, "Icon", icons[i], new Vector2(-25, 2), new Vector2(50, 32), false);
                RectTransform mark = Rect(tile, "Selected", new Vector2(-18, -20), new Vector2(36, 3));
                PaintImage(mark, new Color32(135, 226, 229, 255)).raycastTarget = false;
                ui.languageSelectionMarks[i] = mark.gameObject;
            }
            overlay.gameObject.SetActive(false);
            MidiPlayerUIBuilder.ConfigureHierarchy((RectTransform)parent);
        }

        private static Button Button(RectTransform rect, MidiPlayerUI ui, string method)
        {
            var button = Get<Button>(rect);
            button.targetGraphic = PaintImage(rect, new Color32(8, 13, 12, 255));
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock { normalColor = Color.white, highlightedColor = new Color(2, 3, 3),
                selectedColor = Color.white, pressedColor = new Color(1.5f, 2, 2), disabledColor = Color.gray,
                colorMultiplier = 1, fadeDuration = 0.1f };
            Listen(button, ui, method);
            return button;
        }

        private static void Listen(Button button, MidiPlayerUI ui, string method)
        {
            while (button.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(button.onClick, 0);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
            if (backing == null) throw new InvalidOperationException("Missing MIDI UI backing behavior.");
            UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, method);
        }

        private static void DrawIcon(Transform parent, string name, string icon, Vector2 position, Vector2 size, bool tinted)
        {
            Image image = Get<Image>(Rect(parent, name, position, size));
            image.sprite = MidiLanguageIconAssets.Icon(icon);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = tinted ? new Color32(135, 226, 229, 255) : Color.white;
            image.raycastTarget = false;
        }

        private static Image PaintImage(RectTransform rect, Color color)
        {
            var image = Get<Image>(rect);
            image.sprite = MidiPlayerUISkinAssets.Rounded;
            image.type = Image.Type.Sliced; image.color = color;
            image.pixelsPerUnitMultiplier = 2;
            image.raycastTarget = true;
            return image;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform child = parent.Find(name);
            if (child == null) child = new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(parent, false);
            var rect = (RectTransform)child;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
            return rect;
        }

        private static T Get<T>(Transform transform) where T : Component
        {
            var component = transform.GetComponent<T>();
            return component != null ? component : transform.gameObject.AddComponent<T>();
        }
    }
}
