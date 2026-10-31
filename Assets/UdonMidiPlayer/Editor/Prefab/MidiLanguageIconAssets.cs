using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Original language glyphs and flags, generated through the player's sprite asset pipeline.</summary>
    public static class MidiLanguageIconAssets
    {
        private const string Folder = "Assets/UdonMidiPlayer/UI/Skin/Language";
        private const int SamplesPerAxis = 4;
        private const float StrokeRadius = 1.5f;
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static readonly Color32 Clear = new Color32(255, 255, 255, 0);
        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 FlagRed = new Color32(222, 41, 16, 255);
        private static readonly Color32 FlagBlue = new Color32(1, 33, 105, 255);
        private static readonly Color32 FlagGold = new Color32(255, 222, 0, 255);

        /// <summary>Language / Auto are white alpha masks; keep Image tint white for the full-color flags.</summary>
        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A language icon name is required.", "name");
            string canonical;
            switch (name.ToLowerInvariant())
            {
                case "language": canonical = "Language"; break;
                case "auto": canonical = "Auto"; break;
                case "en": canonical = "en"; break;
                case "zh-hans": canonical = "zh-Hans"; break;
                case "ja": canonical = "ja"; break;
                default: throw new ArgumentException("Unknown MIDI player language icon: " + name, "name");
            }

            Sprite sprite;
            if (!Icons.TryGetValue(canonical, out sprite) || sprite == null)
            {
                sprite = LoadOrCreate(canonical);
                Icons[canonical] = sprite;
            }
            return sprite;
        }

        private static Sprite LoadOrCreate(string name)
        {
            string assetPath = Folder + "/" + name + ".png";
            string diskPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            if (!File.Exists(diskPath))
            {
                EnsureFolder();
                bool flag = name != "Language" && name != "Auto";
                int width = flag ? 96 : 64;
                const int height = 64;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color32[width * height];
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int r = 0, g = 0, b = 0, covered = 0;
                            for (int sy = 0; sy < SamplesPerAxis; sy++)
                            {
                                for (int sx = 0; sx < SamplesPerAxis; sx++)
                                {
                                    Color32 sample = Sample(name, x + (sx + 0.5f) / SamplesPerAxis,
                                        y + (sy + 0.5f) / SamplesPerAxis);
                                    if (sample.a == 0) continue;
                                    r += sample.r;
                                    g += sample.g;
                                    b += sample.b;
                                    covered++;
                                }
                            }
                            // Average visible RGB separately so transparent borders do not darken the artwork.
                            pixels[y * width + x] = covered == 0 ? Clear : new Color32(
                                (byte)(r / covered), (byte)(g / covered), (byte)(b / covered),
                                (byte)Mathf.RoundToInt(255f * covered / (SamplesPerAxis * SamplesPerAxis)));
                        }
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(diskPath, texture.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Could not import language icon: " + assetPath);
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
                importer.spriteBorder = Vector4.zero;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            // Reuse existing textures and their .meta files so prefab references retain their GUIDs.
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null) throw new InvalidOperationException("Could not load language icon: " + assetPath);
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

        private static Color32 Sample(string name, float x, float y)
        {
            if (name == "Language") return InsideLanguage(x, y) ? White : Clear;
            if (name == "Auto") return InsideGlobe(x, y) ? White : Clear;
            // A 90 x 60 flag occupies a 96 x 64 canvas, keeping its 3:2 ratio and a transparent rim.
            if (!InsideFlag(x, y)) return Clear;
            float u = (x - 3f) / 90f;
            float v = (y - 2f) / 60f;
            switch (name)
            {
                case "en": return UnionJack(u, v);
                case "zh-Hans": return China(u, v);
                case "ja":
                    float jx = (u - 0.5f) * 1.5f;
                    float jy = v - 0.5f;
                    return jx * jx + jy * jy <= 0.3f * 0.3f ? new Color32(188, 0, 45, 255) : White;
                default: return Clear;
            }
        }

        private static bool InsideLanguage(float x, float y)
        {
            // The two familiar translation glyphs: 文 above-left, A below-right.
            return Line(x, y, 21, 54, 23, 50)
                || Line(x, y, 9, 45, 35, 45)
                || Line(x, y, 29, 45, 26, 35)
                || Line(x, y, 26, 35, 20, 27)
                || Line(x, y, 20, 27, 9, 21)
                || Line(x, y, 15, 40, 20, 32)
                || Line(x, y, 20, 32, 27, 26)
                || Line(x, y, 27, 26, 34, 23)
                || Line(x, y, 35, 10, 46, 36)
                || Line(x, y, 46, 36, 57, 10)
                || Line(x, y, 39, 19, 53, 19);
        }

        private static bool InsideGlobe(float x, float y)
        {
            float dx = x - 32f;
            float dy = y - 32f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            if (Mathf.Abs(radius - 21f) <= StrokeRadius) return true;
            if (radius > 21f) return false;
            if (Mathf.Abs(dy) <= StrokeRadius) return true;
            if (Mathf.Abs(Mathf.Abs(dy) - 10f) <= StrokeRadius) return true;
            // Projected meridians, matching the rounded 3 px strokes used by the player controls.
            float longitude = Mathf.Sqrt(dx * dx / (10f * 10f) + dy * dy / (21f * 21f));
            return Mathf.Abs(longitude - 1f) <= 0.095f;
        }

        private static bool InsideFlag(float x, float y)
        {
            float dx = Mathf.Max(Mathf.Abs(x - 48f) - 42f, 0f);
            float dy = Mathf.Max(Mathf.Abs(y - 32f) - 27f, 0f);
            return dx * dx + dy * dy <= 3f * 3f;
        }

        private static Color32 UnionJack(float u, float v)
        {
            // Draw the white saltires, the narrow offset red saltires, then the central cross.
            float x = u * 90f;
            float y = v * 60f;
            float rising = (60f * x - 90f * y) / 108.1665f;
            float falling = (60f * x + 90f * y - 5400f) / 108.1665f;
            Color32 color = Mathf.Abs(rising) <= 7f || Mathf.Abs(falling) <= 7f ? White : FlagBlue;
            // Opposite halves of each diagonal keep the distinctive counterchanged red placement.
            float redRising = rising + (u < 0.5f ? 2.1f : -2.1f);
            float redFalling = falling + (u < 0.5f ? -2.1f : 2.1f);
            if (Mathf.Abs(redRising) <= 2.3f || Mathf.Abs(redFalling) <= 2.3f)
                color = new Color32(200, 16, 46, 255);
            if (Mathf.Abs(x - 45f) <= 9f || Mathf.Abs(y - 30f) <= 9f) color = White;
            if (Mathf.Abs(x - 45f) <= 5.5f || Mathf.Abs(y - 30f) <= 5.5f)
                color = new Color32(200, 16, 46, 255);
            return color;
        }

        private static Color32 China(float u, float v)
        {
            float x = u * 30f;
            float y = (1f - v) * 20f;
            if (InsideStar(x, y, 5f, 5f, 3f, 0f)
                || InsideStar(x, y, 10f, 2f, 1f, Mathf.Atan2(-5f, -3f))
                || InsideStar(x, y, 12f, 4f, 1f, Mathf.Atan2(-7f, -1f))
                || InsideStar(x, y, 12f, 7f, 1f, Mathf.Atan2(-7f, 2f))
                || InsideStar(x, y, 10f, 9f, 1f, Mathf.Atan2(-5f, 4f))) return FlagGold;
            return FlagRed;
        }

        private static bool InsideStar(float x, float y, float cx, float cy, float radius, float rotation)
        {
            // A ten-point concave polygon, avoiding thin-line artifacts on the small flag stars.
            const float innerRatio = 0.381966f;
            bool inside = false;
            float previousAngle = rotation + 9f * Mathf.PI / 5f;
            float previousRadius = radius * innerRatio;
            float px = cx + Mathf.Sin(previousAngle) * previousRadius;
            float py = cy - Mathf.Cos(previousAngle) * previousRadius;
            for (int i = 0; i < 10; i++)
            {
                float angle = rotation + i * Mathf.PI / 5f;
                float vertexRadius = (i & 1) == 0 ? radius : radius * innerRatio;
                float nx = cx + Mathf.Sin(angle) * vertexRadius;
                float ny = cy - Mathf.Cos(angle) * vertexRadius;
                if ((ny > y) != (py > y) && x < (px - nx) * (y - ny) / (py - ny) + nx)
                    inside = !inside;
                px = nx;
                py = ny;
            }
            return inside;
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
    }
}
