using System;
using System.Collections.Generic;
using MusicMidiPlayer.Editor.Assets;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Player-local fonts; never changes the world's TMP Settings or other UI fonts.</summary>
    public static class MidiPlayerFontAssets
    {
        public const string Folder = "Assets/UdonMidiPlayer/Fonts";
        public const string FontPath = Folder + "/MidiPlayer Latin SDF.asset";
        private const string LatinSource = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private static readonly string[] FallbackNames =
        {
            "Comfortaa", "NotoSansCJKsc", "NotoSansHebrew", "NotoSansThai"
        };

        public static TMP_FontAsset EnsureFont()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/UdonMidiPlayer", "Fonts");
            TMP_FontAsset latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (latin == null)
            {
                if (!AssetDatabase.CopyAsset(LatinSource, FontPath))
                    throw new InvalidOperationException("无法创建播放器独立的拉丁字体，请先准备TMP Essential Resources");
                latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                latin.name = "MidiPlayer Latin SDF";
            }
            var fallbacks = new List<TMP_FontAsset>
            {
                DynamicFont("Comfortaa", "Comfortaa-Regular.ttf", 1024, "ÀÉÖßΩαЖя—…"),
                DynamicFont("NotoSansCJKsc", "NotoSansCJKsc-Regular.otf", 2048,
                    "中文简体繁體上海紅茶館東方紅魔郷美鈴音楽日本語あいうえおカタカナ한글한국어「」～（）"),
                DynamicFont("NotoSansHebrew", "NotoSansHebrew-Regular.ttf", 1024, "אבגדהוזחטיכלמנסעפצקרשת"),
                DynamicFont("NotoSansThai", "NotoSansThai-Regular.ttf", 1024, "ภาษาไทยเพลงดนตรี")
            };
            bool changed = latin.fallbackFontAssetTable == null || latin.fallbackFontAssetTable.Count != fallbacks.Count;
            if (!changed)
                for (int i = 0; i < fallbacks.Count; i++) changed |= latin.fallbackFontAssetTable[i] != fallbacks[i];
            if (changed)
            {
                latin.fallbackFontAssetTable = fallbacks;
                EditorUtility.SetDirty(latin);
                AssetDatabase.SaveAssetIfDirty(latin);
            }
            return latin;
        }

        private static TMP_FontAsset DynamicFont(string name, string sourceName, int atlasSize, string seed)
        {
            string path = Folder + "/" + name + " TMP.asset";
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
            Font source = AssetDatabase.LoadAssetAtPath<Font>(Folder + "/" + sourceName);
            if (source == null) throw new InvalidOperationException("播放器字体源文件缺失: " + sourceName);
            font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            if (font == null) throw new InvalidOperationException("无法读取播放器字体: " + sourceName);
            font.name = name + " TMP";
            font.material.name = name + " SDF Material";
            font.TryAddCharacters(seed, out string missing);
            if (!string.IsNullOrEmpty(missing))
                throw new InvalidOperationException("字体基础字符缺失: " + name + " / " + missing);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                font.atlasTextures[i].name = name + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(font.atlasTextures[i], font);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            return font;
        }

        public static void ValidateUI(Transform uiRoot, MidiLibraryAsset library)
        {
            TMP_FontAsset latin = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (latin == null || latin.material == null || latin.atlasTexture == null)
                throw new InvalidOperationException("播放器多语言字体尚未准备，请更新播放器UI");
            var fallbacks = latin.fallbackFontAssetTable;
            if (fallbacks == null || fallbacks.Count != FallbackNames.Length)
                throw new InvalidOperationException("播放器多语言字体回退链不完整");
            for (int i = 0; i < FallbackNames.Length; i++)
            {
                TMP_FontAsset font = fallbacks[i];
                if (font == null || AssetDatabase.GetAssetPath(font) != Folder + "/" + FallbackNames[i] + " TMP.asset"
                    || font.atlasPopulationMode != AtlasPopulationMode.Dynamic || font.sourceFontFile == null
                    || !font.isMultiAtlasTexturesEnabled || font.material == null || font.atlasTexture == null)
                    throw new InvalidOperationException("播放器动态回退字体配置无效: " + FallbackNames[i]);
            }
            foreach (TMP_Text text in uiRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font != latin) throw new InvalidOperationException("播放器文字未使用多语言字体: " + text.name);
                ValidateText(latin, text.text, text.name);
            }
            ValidateText(latin, library.title, "曲库名称");
            foreach (MidiSongAsset song in library.songs) ValidateText(latin, song.title, "曲目 " + song.name);
            foreach (MidiLibraryFolder folder in library.folders ?? Array.Empty<MidiLibraryFolder>())
                if (folder != null) ValidateText(latin, folder.path, "收藏夹目录 " + folder.path);
            var ui = uiRoot.GetComponent<MusicMidiPlayer.UI.MidiPlayerUI>();
            if (ui != null && ui.languageFiles != null)
                foreach (TextAsset language in ui.languageFiles)
                    if (language != null) ValidateText(latin, language.text, "Language " + language.name);
        }

        private static void ValidateText(TMP_FontAsset font, string text, string context)
        {
            if (string.IsNullOrEmpty(text)) return;
            var checkedCharacters = new HashSet<uint>();
            for (int i = 0; i < text.Length; i++)
            {
                uint character = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    character = (uint)char.ConvertToUtf32(text[i], text[++i]);
                if (character <= 0xFFFF && char.IsControl((char)character)) continue;
                if (checkedCharacters.Add(character) && !CanRender(font, character, new HashSet<TMP_FontAsset>()))
                    throw new InvalidOperationException(context + " 缺少字形 U+" + character.ToString("X4"));
            }
        }

        private static bool CanRender(TMP_FontAsset font, uint character, HashSet<TMP_FontAsset> visited)
        {
            if (font == null || !visited.Add(font)) return false;
            if (font.characterLookupTable.ContainsKey(character)) return true;
            // The native Font.HasCharacter API accepts UTF-16 char only in Unity 2022.
            // Query the source face by Unicode scalar so supplementary CJK is checked correctly.
            if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic && font.sourceFontFile != null
                && FontEngine.LoadFontFace(font.sourceFontFile, font.faceInfo.pointSize) == FontEngineError.Success
                && FontEngine.TryGetGlyphIndex(character, out uint glyphIndex) && glyphIndex != 0) return true;
            if (font.fallbackFontAssetTable != null)
                foreach (TMP_FontAsset fallback in font.fallbackFontAssetTable)
                    if (CanRender(fallback, character, visited)) return true;
            return false;
        }
    }
}
