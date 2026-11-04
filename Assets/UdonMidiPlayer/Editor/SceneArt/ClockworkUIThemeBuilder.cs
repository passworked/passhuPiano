using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Logic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Serialized scene styling only; no runtime recoloring or shared asset changes.</summary>
    public static class ClockworkUIThemeBuilder
    {
        static readonly Color Panel = Hex(0x143E32);
        static readonly Color Tile = Hex(0x1C4A3D);
        static readonly Color Inset = Hex(0x09261E);
        static readonly Color Gold = Hex(0xD8B96A);
        static readonly Color Ivory = Hex(0xF6EDD5);
        static readonly Color Muted = Hex(0xC5C9A8);

        [MenuItem("Tools/Music MIDI Player/Scene Art/Apply Clockwork Green Gold UI Theme")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClockworkMaintenanceSceneBuilder.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Open the clockwork scene outside Play Mode and compilation.");
            var f = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MidiPlayerFacade>(true)).Single();
            var credits = f.transform.root.GetComponentsInChildren<Canvas>(true).Single(c => c.name == "Credits");
            Directory.CreateDirectory("RecoveryBackups/ClockworkUITheme-20261104");
            if (!EditorSceneManager.SaveScene(scene, "RecoveryBackups/ClockworkUITheme-20261104/Before-" + DateTime.Now.Ticks + ".unity", true))
                throw new IOException("Could not preserve the in-memory scene.");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Unify clockwork green gold UI theme");
            foreach (var root in new[] { f.playerUI.transform, credits.transform }) Apply(root);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the green gold theme.");
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }

        static void Apply(Transform root)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                // Preserve original logo/flag artwork. TMP fallback submeshes follow their text.
                if (graphic.name == "Logo" || graphic is TMP_SubMeshUI) continue;
                Undo.RecordObject(graphic, "Apply green gold palette");
                graphic.color = Remap(graphic.color, graphic is TMP_Text || graphic is Text);
                if (graphic.name == "HeaderDivider" || graphic.name == "LibraryDivider")
                    graphic.color = WithAlpha(Gold, .35f);
                if (graphic.name == "Handle") graphic.color = Ivory;
                EditorUtility.SetDirty(graphic);
                PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
            }
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                Undo.RecordObject(selectable, "Theme all selectable states");
                var c = selectable.colors;
                c.normalColor = Remap(c.normalColor, false);
                c.highlightedColor = Tint(c.highlightedColor);
                c.pressedColor = Tint(c.pressedColor);
                c.selectedColor = Tint(c.selectedColor);
                c.disabledColor = Tint(c.disabledColor);
                selectable.colors = c;
                EditorUtility.SetDirty(selectable);
                PrefabUtility.RecordPrefabInstancePropertyModifications(selectable);
            }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                Undo.RecordObject(input, "Theme text selection and caret");
                input.customCaretColor = true;
                input.caretColor = Gold;
                input.selectionColor = WithAlpha(Gold, .35f);
                EditorUtility.SetDirty(input);
                PrefabUtility.RecordPrefabInstancePropertyModifications(input);
            }
            var background = root.GetComponent<Image>();
            background.color = Panel;
            var backplate = root.Find("Backplate").GetComponent<Image>();
            Undo.RecordObject(backplate, "Match backplate to panel");
            backplate.color = Panel;
            var outline = root.GetComponent<Outline>();
            if (!outline) outline = Undo.AddComponent<Outline>(root.gameObject);
            Undo.RecordObject(outline, "Gold panel edge");
            outline.effectColor = WithAlpha(Gold, .8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        static Color Tint(Color c)
        {
            // Original hover tints include HDR RGB multipliers, so do not clamp them.
            if (c.maxColorComponent > 1.01f && (c.g > c.r || c.b > c.r))
                return new Color(c.maxColorComponent, c.maxColorComponent * .94f, c.maxColorComponent * .7f, c.a);
            if (Near(c, Hex(0xF5F5F5))) return WithAlpha(new Color(1.08f, 1.04f, .9f), c.a);
            if (Near(c, Hex(0xC8C8C8))) return WithAlpha(Hex(0xA7AC8E), c.a);
            if (Near(c, Hex(0x808080))) return WithAlpha(Hex(0x718072), c.a);
            return Remap(c, false);
        }

        static Color Remap(Color c, bool text)
        {
            if (c.a < .001f) return c;
            Color mapped;
            if (Near(c, Hex(0x37424F))) mapped = Panel;
            else if (Near(c, Hex(0x25313E))) mapped = Tile;
            else if (Near(c, Hex(0x080D0C))) mapped = Inset;
            else if (Near(c, Hex(0x87E2E5))) mapped = Gold;
            else if (Near(c, Hex(0xBAC8D1))) mapped = Muted;
            else if (Near(c, Hex(0x46586E))) mapped = Hex(0x365B45);
            else if (Near(c, Hex(0xB5F3F4))) mapped = Hex(0xF0D99A);
            else if (Near(c, Hex(0x62B9BD))) mapped = Hex(0xAD8B43);
            else if (Near(c, Hex(0x9AB8C4))) mapped = Hex(0xD7CE9F);
            else if (Near(c, Hex(0x658C91))) mapped = Hex(0xAA9B63);
            else if (Near(c, Hex(0x78818B)) || Near(c, Hex(0x414B58))) mapped = Hex(0x647465);
            else if (Near(c, Hex(0xB2CCCC)) || Near(c, Hex(0xA4BFC2))) mapped = Hex(0xC6BA8D);
            else if (Near(c, Hex(0xD2E5EB))) mapped = Hex(0xE9DEB6);
            else if (text && Near(c, Color.white)) mapped = Ivory;
            else return c;
            return WithAlpha(mapped, c.a);
        }

        static bool Near(Color a, Color b) { return Mathf.Abs(a.r-b.r)<.006f && Mathf.Abs(a.g-b.g)<.006f && Mathf.Abs(a.b-b.b)<.006f; }
        static Color WithAlpha(Color c, float alpha) { c.a = alpha; return c; }
        static Color Hex(int value) { return new Color32((byte)(value>>16),(byte)(value>>8),(byte)value,255); }
    }
}
