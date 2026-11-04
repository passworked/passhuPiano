// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.UI;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Installs only the scene's independent credits board; never applies a prefab.</summary>
    public static class MidiCreditsBuilder
    {
        public const string Repository = "https://github.com/passworked/passhuPiano";
        public const float PanelHeight = 880f;
        private static readonly Color Panel = new Color32(37, 49, 62, 255);
        private static readonly Color Tile = new Color32(8, 13, 12, 255);
        private static readonly Color Accent = new Color32(135, 226, 229, 255);
        private static readonly Color Muted = new Color32(186, 200, 209, 255);
        private static TMP_FontAsset font;

        [Serializable] private class Entry
        {
            public int category;
            public string name, author, license, detailKey, source, licenseUrl;
        }
        [Serializable] private class Catalog { public Entry[] entries; }
        [Serializable] private class Provenance
        {
            public string sha256, author, source, license, licenseUrl, attribution;
        }
        [Serializable] private class Policy { public Provenance[] music; }
        [Serializable] private class Selection { public Selected[] items; }
        [Serializable] private class Selected { public string sha256; public Metadata metadata; }
        [Serializable] private class Metadata
        {
            public string source_page, license, license_url, attribution, maintainer;
        }

        [MenuItem("Tools/Music MIDI Player/Scene/Install or Update Credits Board")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before installing credits.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/VRCDefaultWorldScene.unity") throw new InvalidOperationException("Open the main world scene before installing credits.");
            MidiPlayerUI ui = UnityEngine.Object.FindObjectOfType<MidiPlayerUI>();
            if (ui == null || ui.facade == null || ui.facade.library == null) throw new InvalidOperationException("Missing scene player UI/library.");
            EnsureProgram();
            font = ui.titleLabel.font;
            Transform presentation = ui.transform.parent;
            bool exists = presentation.Find("Credits") != null;
            var priorRoot = exists ? (RectTransform)presentation.Find("Credits") : null;
            Vector3 retainedTop = exists ? priorRoot.TransformPoint(new Vector3(0, priorRoot.rect.yMax, 0)) : Vector3.zero;
            RectTransform root = Rect(presentation, "Credits", 0, 0, 1180, PanelHeight);
            root.pivot = new Vector2(.5f, .5f);
            if (exists) root.position = retainedTop - root.TransformVector(new Vector3(0, PanelHeight * .5f, 0));
            if (!exists)
            {
                root.position = new Vector3(0, 2.57f - PanelHeight * .001f, -5.04f);
                root.rotation = Quaternion.Euler(0, 180, 0);
                root.localScale = Vector3.one * .002f;
            }
            Get<Canvas>(root).renderMode = RenderMode.WorldSpace;
            Get<CanvasScaler>(root); Get<GraphicRaycaster>(root); Get<VRCUiShape>(root);
            var collider = Get<BoxCollider>(root); collider.isTrigger = true; collider.size = new Vector3(1180, PanelHeight, 1);
            Paint(root, Panel, true);
            var board = root.GetComponent<MidiCreditsPanel>();
            if (board == null) board = root.gameObject.AddUdonSharpComponent<MidiCreditsPanel>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(board);
            if (backing.programSource == null)
            {
                var program = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>("Assets/UdonMidiPlayer/Runtime/UI/MidiCreditsPanel.asset");
                backing.programSource = program;
                var serialized = new SerializedObject(backing);
                serialized.FindProperty("serializedProgramAsset").objectReferenceValue = program.SerializedProgramAsset;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            board.playerUI = ui; ui.creditsPanel = board;
            board.panelRect = root; board.panelCollider = collider;
            board.title = Label(root, "Title", -546, 355, 690, 44, 32, Color.white);
            board.title.fontStyle = FontStyles.Bold;
            ConfigureBranding(root);
            board.subtitle = Label(root, "Subtitle", -546, 315, 1092, 30, 17, Muted);
            board.subtitle.text = "";
            board.subtitle.gameObject.SetActive(false);
            Url(root, "Repository", -546, 306, 1092, 30, Repository, 20);
            board.tabLabels = new TMP_Text[4]; board.tabMarks = new GameObject[4];
            string[] events = { "_Project", "_Samples", "_Fonts", "_Music" };
            for (int i = 0; i < 4; i++)
            {
                Button tab = Button(root, "Tab" + i, -546 + i * 276, 250, 264, 44, board, events[i]);
                board.tabLabels[i] = tab.transform.Find("Label").GetComponent<TMP_Text>();
                RectTransform mark = Rect(tab.transform, "Selected", -120, -18, 240, 3);
                Paint(mark, Accent, false); board.tabMarks[i] = mark.gameObject;
            }
            board.hint = Label(root, "Hint", -546, 177, 1092, 28, 15, Muted);
            board.hint.text = "";
            board.hint.gameObject.SetActive(false);
            const int capacity = 8;
            board.rows = new GameObject[capacity]; board.rowNames = new TMP_Text[capacity]; board.rowAuthors = new TMP_Text[capacity];
            board.rowLicenses = new TMP_Text[capacity]; board.rowDetails = new TMP_Text[capacity]; board.rowSources = new TMP_InputField[capacity];
            board.rowRects = new RectTransform[capacity]; board.rowLicenseRects = new RectTransform[capacity];
            board.rowDetailRects = new RectTransform[capacity]; board.rowSourceRects = new RectTransform[capacity];
            board.rowNameRects = new RectTransform[capacity]; board.rowAuthorRects = new RectTransform[capacity];
            board.rowLicenseSources = new TMP_InputField[capacity]; board.rowLicenseSourceRects = new RectTransform[capacity];
            for (int i = 0; i < capacity; i++)
            {
                RectTransform row = Rect(root, "Row" + i, i % 2 == 0 ? -558 : 8, 0, 550, 152);
                row.pivot = new Vector2(0, 1); board.rowRects[i] = row;
                Paint(row, Tile, false); board.rows[i] = row.gameObject;
                board.rowNames[i] = CardLabel(row, "Name", -6, 36, 18, Color.white);
                board.rowNames[i].fontStyle = FontStyles.Bold;
                board.rowAuthors[i] = CardLabel(row, "Author", -42, 28, 14, Muted);
                board.rowLicenses[i] = CardLabel(row, "License", -70, 18, 14, Accent);
                board.rowDetails[i] = CardLabel(row, "Details", -88, 28, 13, Muted);
                board.rowSources[i] = Url(row, "Sources", 0, 0, 526, 16, "", 12);
                board.rowSources[i].lineType = TMP_InputField.LineType.SingleLine;
                board.rowLicenseSources[i] = Url(row, "LicenseSource", 0, 0, 526, 16, "", 12);
                board.rowLicenseSources[i].lineType = TMP_InputField.LineType.SingleLine;
                var licenseSourceRect = (RectTransform)board.rowLicenseSources[i].transform;
                licenseSourceRect.anchorMin = licenseSourceRect.anchorMax = new Vector2(0, 1);
                licenseSourceRect.pivot = new Vector2(0, 1);
                board.rowLicenseSourceRects[i] = licenseSourceRect;
                var sourceRect = (RectTransform)board.rowSources[i].transform;
                sourceRect.anchorMin = sourceRect.anchorMax = new Vector2(0, 1);
                sourceRect.pivot = new Vector2(0, 1);
                board.rowNameRects[i] = (RectTransform)board.rowNames[i].transform;
                board.rowAuthorRects[i] = (RectTransform)board.rowAuthors[i].transform;
                board.rowLicenseRects[i] = (RectTransform)board.rowLicenses[i].transform;
                board.rowDetailRects[i] = (RectTransform)board.rowDetails[i].transform;
                board.rowSourceRects[i] = sourceRect;
            }
            board.previous = Button(root, "Previous", -546, -390, 174, 32, board, "_Previous");
            board.next = Button(root, "Next", 372, -390, 174, 32, board, "_Next");
            board.previousLabel = board.previous.transform.Find("Label").GetComponent<TMP_Text>();
            board.nextLabel = board.next.transform.Find("Label").GetComponent<TMP_Text>();
            board.pageLabel = Label(root, "Page", -352, -390, 704, 30, 17, Muted);
            board.pageLabel.alignment = TextAlignmentOptions.Center;
            board.previousRect = (RectTransform)board.previous.transform;
            board.nextRect = (RectTransform)board.next.transform;
            board.pageRect = (RectTransform)board.pageLabel.transform;
            foreach (RectTransform child in root)
                if (child.name != "Backplate") AnchorTop(child);
            board.previousRect.anchoredPosition = new Vector2(-546, -PanelHeight + 22);
            board.nextRect.anchoredPosition = new Vector2(372, -PanelHeight + 22);
            board.pageRect.anchoredPosition = new Vector2(-352, -PanelHeight + 22);
            Populate(board, ui);
            MidiPlayerUIBuilder.ConfigureBackplate(root);
            board.Refresh();
            Save(board); Save(ui);
            foreach (Component component in root.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Credits board updated: " + board.categories.Length + " resources, including " + board.categories.Count(c => c == 3) + " scene songs.");
        }

        public static void ConfigureBranding(RectTransform root)
        {
            const string logoPath = "Assets/UdonMidiPlayer/UI/passhuPiano-logo.png";
            var importer = AssetImporter.GetAtPath(logoPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing project logo: " + logoPath);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
                || !importer.alphaIsTransparency || importer.mipmapEnabled || importer.maxTextureSize != 512)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
            font = root.Find("Title").GetComponent<TMP_Text>().font;
            var brand = Label(root, "Brand", 190, 355, 216, 36, 24, Accent);
            brand.text = "passhuPiano"; AnchorTop((RectTransform)brand.transform);
            var logo = Get<Image>(Rect(root, "Logo", 414, 364, 132, 112));
            logo.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(logoPath);
            logo.type = Image.Type.Simple;
            logo.color = Color.white;
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            AnchorTop((RectTransform)logo.transform);
            MidiPlayerUIBuilder.ConfigureHierarchy(root);
            EditorUtility.SetDirty(logo);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        private static void AnchorTop(RectTransform rect)
        {
            if (rect.anchorMin.y == 1f && rect.anchorMax.y == 1f) return;
            Vector2 position = rect.anchoredPosition;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(position.x, position.y - 410);
        }

        private static TMP_Text CardLabel(Transform parent, string name, float y, float height, float size, Color color)
        {
            var label = Label(parent, name, 0, 0, 526, height, size, color);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, y);
            label.enableWordWrapping = true;
            label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        private static void EnsureProgram()
        {
            const string path = "Assets/UdonMidiPlayer/Runtime/UI/MidiCreditsPanel.asset";
            var program = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(path);
            if (program != null) return;
            program = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
            program.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/UdonMidiPlayer/Runtime/UI/MidiCreditsPanel.cs");
            program.ScriptVersion = UdonSharp.UdonSharpProgramVersion.CurrentVersion;
            AssetDatabase.CreateAsset(program, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions
            { IsEditorBuild = true, CurrentBuildTarget = EditorUserBuildSettings.activeBuildTarget });
            if (UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("Resolve UdonSharp compilation errors before installing credits.");
        }

        private static void Populate(MidiCreditsPanel board, MidiPlayerUI ui)
        {
            var entries = new List<Entry>(JsonUtility.FromJson<Catalog>(File.ReadAllText("Assets/UdonMidiPlayer/Editor/Prefab/MidiCreditsCatalog.json")).entries);
            var policy = JsonUtility.FromJson<Policy>(File.ReadAllText(MidiReleasePolicy.PathInProject));
            string selectionPath = "Assets/music/Classical100/SELECTION.json";
            Selected[] selected = File.Exists(selectionPath) ? JsonUtility.FromJson<Selection>("{\"items\":" + File.ReadAllText(selectionPath) + "}").items : new Selected[0];
            foreach (var song in ui.facade.library.songs)
            {
                if (song == null) continue;
                var provenance = policy.music.FirstOrDefault(p => p.sha256 == song.sourceHash);
                var known = selected.FirstOrDefault(s => s.sha256 == song.sourceHash);
                string author = song.author ?? "";
                var entry = new Entry { category = 3, name = song.title, author = author,
                    license = "credits.unrecorded_license", detailKey = "", source = "", licenseUrl = "" };
                if (provenance != null)
                {
                    entry.license = provenance.license; entry.source = provenance.source; entry.licenseUrl = provenance.licenseUrl;
                    if (!string.IsNullOrEmpty(provenance.author)) entry.author += " · MIDI: " + provenance.author;
                    entry.detailKey = "credits.music_derived";
                }
                else if (known != null && known.metadata != null)
                {
                    entry.license = known.metadata.license; entry.source = known.metadata.source_page; entry.licenseUrl = known.metadata.license_url;
                    if (!string.IsNullOrEmpty(known.metadata.maintainer)) entry.author += " · MIDI: " + known.metadata.maintainer;
                    entry.detailKey = "credits.music_derived";
                }
                else if (author.IndexOf("uaaaak", StringComparison.OrdinalIgnoreCase) >= 0 || author.IndexOf("uaaaaak", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    entry.license = "credits.arranger_terms"; entry.detailKey = "credits.uaaaak_terms";
                    entry.source = "https://drive.google.com/drive/folders/1IbM67amQHYXjATVSi2LjBKkiIYK_Ac-O";
                    entry.licenseUrl = "https://drive.google.com/file/d/1-6mxIUTIjQxg1VMLXlxyr0IrMntK2T1U/view";
                }
                entries.Add(entry);
            }
            board.categories = entries.Select(e => e.category).ToArray(); board.names = entries.Select(e => e.name ?? "").ToArray();
            board.authors = entries.Select(e => e.author ?? "").ToArray(); board.licenses = entries.Select(e => e.license ?? "credits.unrecorded_license").ToArray();
            board.detailKeys = entries.Select(e => e.detailKey).ToArray(); board.sources = entries.Select(e => e.source ?? "").ToArray();
            board.licenseUrls = entries.Select(e => e.licenseUrl ?? "").ToArray();
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            Transform child = parent.Find(name);
            if (child == null) { child = new GameObject(name, typeof(RectTransform)).transform; child.SetParent(parent, false); }
            var rect = (RectTransform)child; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(0, .5f); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }
        private static T Get<T>(Transform t) where T : Component
        {
            T component = t.GetComponent<T>();
            return component == null ? t.gameObject.AddComponent<T>() : component;
        }
        private static Image Paint(Transform t, Color color, bool raycast)
        {
            Image image = Get<Image>(t); image.sprite = MidiPlayerUISkinAssets.Rounded;
            image.pixelsPerUnitMultiplier = 2;
            image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = raycast; return image;
        }
        private static TMP_Text Label(Transform parent, string name, float x, float y, float w, float h, float size, Color color)
        {
            var rect = Rect(parent, name, x, y, w, h); var label = Get<TextMeshProUGUI>(rect);
            label.font = font; label.fontSize = size; label.fontSizeMax = size; label.fontSizeMin = size - 3;
            label.enableAutoSizing = true; label.color = color; label.raycastTarget = false;
            label.richText = false; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.MidlineLeft; return label;
        }
        private static Button Button(Transform parent, string name, float x, float y, float w, float h, MidiCreditsPanel board, string method)
        {
            var rect = Rect(parent, name, x, y, w, h); var button = Get<Button>(rect);
            button.targetGraphic = Paint(rect, Tile, true);
            var colors = button.colors; colors.highlightedColor = new Color(1.5f, 1.8f, 1.8f); colors.pressedColor = Accent; button.colors = colors;
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(board).SendCustomEvent, method);
            Label(rect, "Label", -w / 2 + 12, 0, w - 24, h - 6, 19, Accent).alignment = TextAlignmentOptions.Center;
            return button;
        }
        private static TMP_InputField Url(Transform parent, string name, float x, float y, float w, float h, string value, float size)
        {
            var rect = Rect(parent, name, x, y, w, h); var image = Paint(rect, Tile, true);
            var area = Rect(rect, "Viewport", -w / 2 + 5, 0, w - 10, h); Get<RectMask2D>(area);
            var label = Label(area, "Text", -(w - 10) / 2, 0, w - 10, h, size, Accent);
            label.enableAutoSizing = false; label.overflowMode = TextOverflowModes.Overflow;
            var field = Get<TMP_InputField>(rect); field.textViewport = area; field.textComponent = label;
            field.targetGraphic = image; field.readOnly = true; field.richText = false; field.fontAsset = font;
            field.customCaretColor = true; field.caretColor = Accent; field.selectionColor = new Color(.53f, .89f, .9f, .35f);
            var nav = field.navigation; nav.mode = Navigation.Mode.None; field.navigation = nav;
            field.SetTextWithoutNotify(value); return field;
        }
        private static void Save(UdonSharp.UdonSharpBehaviour behaviour)
        {
            EditorUtility.SetDirty(behaviour);
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour); EditorUtility.SetDirty(backing);
            if (PrefabUtility.IsPartOfPrefabInstance(behaviour))
            { PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour); PrefabUtility.RecordPrefabInstancePropertyModifications(backing); }
        }
    }
}
