using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.CommunityBoards;
using MusicMidiPlayer.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Scene overrides only; does not apply the imported community prefab.</summary>
    public static class ClockworkCommunityBoardsBuilder
    {
        [MenuItem("Tools/Music MIDI Player/Scene Art/Localize and Style Clockwork Community Boards")]
        public static void Install()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode and compilation.");
            var root = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet").transform.Find("AuthorSponsorCommunityBoards");
            if (!root) throw new InvalidOperationException("Install the community boards before configuring them.");
            var ui = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerUI>(true)).Single();
            // Read the installed scene theme: the general UI builder keeps other scenes' defaults.
            Color panel = ui.GetComponent<Image>().color;
            Color tile = ui.transform.Find("LibraryPanel").GetComponent<Image>().color;
            Color inset = ui.transform.Find("NowPlayingPanel").GetComponent<Image>().color;
            Color accent = ui.transform.Find("BrandIcon").GetComponent<Image>().color;
            Color muted = ui.transform.Find("Status").GetComponent<TMP_Text>().color;
            Color ink = ui.titleLabel.color;
            var linkColors = ui.transform.Find("LibraryBack").GetComponent<Button>().colors;
            Color hover = ui.transform.Find("Pause").GetComponent<Button>().colors.highlightedColor;
            var localization = root.GetComponentInChildren<LocalizationManager>(true);
            Directory.CreateDirectory("RecoveryBackups/CommunityBoardsLocalization-20261104");
            if (!EditorSceneManager.SaveScene(scene, "RecoveryBackups/CommunityBoardsLocalization-20261104/Before-" + DateTime.Now.Ticks + ".unity", true))
                throw new IOException("Could not preserve the current scene.");

            Undo.RecordObject(ui, "Bind community board translations");
            Undo.RecordObject(localization, "Follow player language");
            localization.playerUI = ui;
            ui.languageListeners = (ui.languageListeners ?? new UdonSharpBehaviour[0]).Where(b => b != null && b != localization).Concat(new UdonSharpBehaviour[] { localization }).ToArray();

            string promo = "Author wall/Author card/08 · 宣传栏/";
            string[] paths = { "Eyebrow", "Title", "Subtitle", "QQGroupCard/Title", "QQGroupCard/Description", "QQGroupCard/ReservedValue", "QQGroupCard/ReservedNote", "QQGroupCard/Status", "VRChatGroupCard_RESERVED/Title", "VRChatGroupCard_RESERVED/Description", "DiscordCommunityCard/Title", "DiscordCommunityCard/Description", "DiscordCommunityCard/ReservedNote", "DiscordCommunityCard/Status", "Footer" };
            string[] keys = { "hub", "join", "platforms", "qq_title", "qq_description", "qq_value", "qq_note", "open", "vrchat_title", "vrchat_description", "discord_title", "discord_description", "discord_note", "open", "platforms" };
            for (int i = 0; i < paths.Length; i++)
            {
                var label = root.Find(promo + paths[i]).GetComponent<TMP_Text>();
                var localized = label.GetComponent<LocalizedText>();
                Undo.RecordObject(localized, "Bind community caption");
                localized.key = "community." + keys[i];
                localized.localization = localization;
                localized.target = label;
                Changed(localized);
            }
            // AuthorWallDisplay owns these captions, including the immutable group numbers.
            // Do not let a second LocalizedText writer replace their event-driven translations.
            foreach (string path in new[] { "QQ shortcut/Label", "Discord shortcut/Label", "VRChat shortcut/Label" })
            {
                var localized = root.Find(promo + path).GetComponent<LocalizedText>();
                Undo.RecordObject(localized, "Keep one caption writer");
                localized.key = "";
                Changed(localized);
            }

            string materialPath = ClockworkMaintenanceSceneBuilder.Folder + "/Materials/CommunitySocialPalette.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                var shader = Shader.Find("MusicMidiPlayer/UI Community Palette");
                if (!shader) throw new InvalidOperationException("Community palette shader is missing.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_Background", inset); material.SetColor("_Ink", accent); material.SetColor("_Rule", hover);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                Undo.RecordObject(image, "Unify community palette");
                string n = image.name;
                Color color;
                if (image.sprite != null) { color = Color.white; image.material = material; }
                else if (image.color.a == 0) continue;
                else if (n.Contains("rule") || n.Contains("Divider") || n.Contains("Accent") || n.StartsWith("Corner")) color = accent;
                else if (n == "Paper face" || n == "Backdrop" || n == "Compact community surface") color = panel;
                else if (n == "Green heading") color = tile;
                else color = inset;
                color.a = image.color.a;
                image.color = color;
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(text, "Unify community text");
                string n = text.name;
                text.color = n == "Heading" || n == "Author name" || n == "Title" || n == "BadgeText" || n == "Index" || n == "Status" ? accent
                    : n == "Project name" || n == "Page" || n == "Description" || n == "ReservedNote" || n == "Footer" || n == "Placeholder" ? muted : ink;
                if (n == "Heading" || n == "Label" || n == "Placeholder" || n == "Title")
                {
                    float maximum = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
                    text.enableAutoSizing = true; text.fontSizeMax = maximum; text.fontSizeMin = maximum * .65f;
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                Undo.RecordObject(selectable, "Unify community interaction colors");
                selectable.colors = linkColors;
                PrefabUtility.RecordPrefabInstancePropertyModifications(selectable);
            }
            Changed(localization); Changed(ui);
            ui.ApplyLanguage();
            // Explicit refresh also works for edit-mode previews before listeners have enabled.
            root.GetComponentInChildren<AuthorWallDisplay>(true).RefreshLanguage();
            root.GetComponentInChildren<SponsorWallDisplay>(true).RefreshLanguage();
            foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true)) localized.RefreshLanguage();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the updated scene.");
        }

        static void Changed(UdonSharpBehaviour behaviour)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
            EditorUtility.SetDirty(behaviour);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            EditorUtility.SetDirty(backing);
            PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
        }
    }
}
