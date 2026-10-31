using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Creates the player's original UI artwork once, then reuses the imported sprites.</summary>
    public static class MidiPlayerUISkinAssets
    {
        private const string Folder = "Assets/UdonMidiPlayer/UI/Skin";
        private const int Size = 64;
        private const int SamplesPerAxis = 4;
        private const float StrokeRadius = 1.5f;
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static Sprite rounded;

        public static Sprite Rounded
        {
            get
            {
                if (rounded == null) rounded = LoadOrCreate("Rounded", true, InsideRounded);
                return rounded;
            }
        }

        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("An icon name is required.", "name");
            string canonical;
            Func<float, float, bool> shape;
            switch (name.ToLowerInvariant())
            {
                case "dot": canonical = "Dot"; shape = (x, y) => (x - 32f) * (x - 32f) + (y - 32f) * (y - 32f) <= 900f; break;
                case "play": canonical = "Play"; shape = InsidePlay; break;
                case "pause": canonical = "Pause"; shape = InsidePause; break;
                case "stop": canonical = "Stop"; shape = InsideStop; break;
                case "sound": canonical = "Sound"; shape = InsideSound; break;
                case "note": canonical = "Note"; shape = InsideNote; break;
                case "folder": canonical = "Folder"; shape = InsideFolder; break;
                case "back": canonical = "Back"; shape = InsideBack; break;
                case "chevron": canonical = "Chevron"; shape = InsideChevron; break;
                case "settings": canonical = "Settings"; shape = InsideSettings; break;
                default: throw new ArgumentException("Unknown MIDI player UI icon: " + name, "name");
            }

            Sprite sprite;
            if (!Icons.TryGetValue(canonical, out sprite) || sprite == null)
            {
                sprite = LoadOrCreate(canonical, false, shape);
                Icons[canonical] = sprite;
            }
            return sprite;
        }

        private static Sprite LoadOrCreate(string name, bool sliced, Func<float, float, bool> shape)
        {
            string assetPath = Folder + "/" + name + ".png";
            string diskPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            if (!File.Exists(diskPath))
            {
                EnsureFolder();
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color32[Size * Size];
                    for (int y = 0; y < Size; y++)
                    {
                        for (int x = 0; x < Size; x++)
                        {
                            int covered = 0;
                            for (int sy = 0; sy < SamplesPerAxis; sy++)
                                for (int sx = 0; sx < SamplesPerAxis; sx++)
                                    if (shape(x + (sx + 0.5f) / SamplesPerAxis,
                                              y + (sy + 0.5f) / SamplesPerAxis)) covered++;
                            byte alpha = (byte)Mathf.RoundToInt(255f * covered / (SamplesPerAxis * SamplesPerAxis));
                            pixels[y * Size + x] = new Color32(255, 255, 255, alpha);
                        }
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(diskPath, texture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Could not import UI skin texture: " + assetPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = sliced ? new Vector4(16, 16, 16, 16) : Vector4.zero;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null) throw new InvalidOperationException("Could not load UI skin sprite: " + assetPath);
            return sprite;
        }

        private static void EnsureFolder()
        {
            string current = "Assets";
            string[] parts = Folder.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static bool InsideRounded(float x, float y)
        {
            float dx = Mathf.Max(Mathf.Abs(x - 32f) - 16f, 0f);
            float dy = Mathf.Max(Mathf.Abs(y - 32f) - 16f, 0f);
            return dx * dx + dy * dy <= 16f * 16f;
        }

        private static bool InsidePlay(float x, float y)
        {
            return Line(x, y, 21, 14, 21, 50) || Line(x, y, 21, 50, 49, 32)
                || Line(x, y, 49, 32, 21, 14);
        }

        private static bool InsidePause(float x, float y)
        {
            return Line(x, y, 24, 16, 24, 48) || Line(x, y, 40, 16, 40, 48);
        }

        private static bool InsideStop(float x, float y)
        {
            return Line(x, y, 18, 18, 46, 18) || Line(x, y, 46, 18, 46, 46)
                || Line(x, y, 46, 46, 18, 46) || Line(x, y, 18, 46, 18, 18);
        }

        private static bool InsideSound(float x, float y)
        {
            return Line(x, y, 12, 25, 21, 25) || Line(x, y, 21, 25, 31, 17)
                || Line(x, y, 31, 17, 31, 47) || Line(x, y, 31, 47, 21, 39)
                || Line(x, y, 21, 39, 12, 39) || Line(x, y, 12, 39, 12, 25)
                || Arc(x, y, 27, 32, 14) || Arc(x, y, 27, 32, 22);
        }

        private static bool InsideNote(float x, float y)
        {
            float dx = (x - 23f) / 8f;
            float dy = (y - 18f) / 5f;
            float ellipse = Mathf.Sqrt(dx * dx + dy * dy);
            bool head = Mathf.Abs(ellipse - 1f) <= 0.24f;
            return head || Line(x, y, 31, 18, 31, 48) || Line(x, y, 31, 48, 43, 41)
                || Line(x, y, 43, 41, 43, 34);
        }

        private static bool InsideFolder(float x, float y)
        {
            return Line(x, y, 10, 16, 54, 16) || Line(x, y, 54, 16, 54, 43)
                || Line(x, y, 54, 43, 30, 43) || Line(x, y, 30, 43, 25, 49)
                || Line(x, y, 25, 49, 10, 49) || Line(x, y, 10, 49, 10, 16)
                || Line(x, y, 10, 37, 54, 37);
        }

        private static bool InsideBack(float x, float y)
        {
            return Line(x, y, 29, 17, 14, 32) || Line(x, y, 14, 32, 29, 47)
                || Line(x, y, 14, 32, 51, 32);
        }

        private static bool InsideChevron(float x, float y)
        {
            return Line(x, y, 26, 19, 39, 32) || Line(x, y, 39, 32, 26, 45);
        }

        private static bool InsideSettings(float x, float y)
        {
            float dx = x - 32f, dy = y - 32f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dy, dx);
            float outer = Mathf.Cos(angle * 8f) > 0.25f ? 24f : 19f;
            return radius >= 9f && radius <= outer;
        }

        private static bool Line(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float px = x - ax - t * dx;
            float py = y - ay - t * dy;
            return px * px + py * py <= StrokeRadius * StrokeRadius;
        }

        private static bool Arc(float x, float y, float cx, float cy, float radius)
        {
            float dx = x - cx;
            float dy = y - cy;
            return dx > 0 && Mathf.Abs(dy) <= dx
                && Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius) <= StrokeRadius;
        }
    }
}
