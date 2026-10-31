using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Original vector paths, rasterized through the same sprite pipeline as the existing UI icons.
    public static class MidiPerformanceHandAssets
    {
        private const string Path = "Assets/UdonMidiPlayer/UI/Skin/PerformanceHand.png";
        private const int Width = 368, Height = 640;
        public static Sprite Get()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Path);
            if (sprite != null) return sprite;
            var outline = new List<Vector2> { new Vector2(-42, -130) };
            Curve(outline, -42, -92, -65, -76, -72, -35);
            Curve(outline, -77, -8, -86, 36, -89, 57);
            Curve(outline, -91, 76, -68, 80, -66, 62);
            Curve(outline, -62, 42, -59, 20, -56, 10);
            Curve(outline, -52, -2, -53, 69, -56, 112);
            Curve(outline, -57, 133, -31, 136, -30, 115);
            Curve(outline, -29, 82, -30, 46, -28, 30);
            Curve(outline, -24, 17, -20, 33, -20, 145);
            Curve(outline, -20, 166, 9, 169, 9, 146);
            Curve(outline, 10, 102, 9, 53, 11, 36);
            Curve(outline, 14, 20, 19, 32, 20, 107);
            Curve(outline, 20, 127, 47, 130, 47, 108);
            Curve(outline, 47, 73, 43, 31, 45, 7);
            Curve(outline, 45, -8, 49, -10, 59, 6);
            Curve(outline, 71, 26, 92, 10, 80, -8);
            Curve(outline, 72, -24, 59, -44, 48, -62);
            Curve(outline, 34, -84, 35, -106, 35, -130);
            outline.Add(outline[0]);
            var pixels = new Color32[Width * Height];
            var crossings = new List<float>();
            for (int y = 0; y < Height; y++)
            {
                crossings.Clear(); float py = (y + 0.5f) * 0.5f - 140;
                for (int i = 1; i < outline.Count; i++)
                {
                    Vector2 a = outline[i - 1], b = outline[i];
                    if ((a.y > py) == (b.y > py)) continue;
                    crossings.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                crossings.Sort();
                for (int i = 1; i < crossings.Count; i += 2)
                    for (int x = Mathf.Max(0, Mathf.CeilToInt((crossings[i - 1] + 92) * 2)); x < Mathf.Min(Width, (crossings[i] + 92) * 2); x++)
                        pixels[y * Width + x] = new Color32(255, 255, 255, 20);
            }
            Stroke(pixels, outline, 1.3f, 230);
            var crease = new List<Vector2> { new Vector2(-49, -7) };
            Curve(crease, -26, 5, 10, -4, 28, -25); Stroke(pixels, crease, 0.8f, 105);
            crease = new List<Vector2> { new Vector2(32, -19) };
            Curve(crease, 10, -42, 9, -67, 18, -88); Stroke(pixels, crease, 0.8f, 105);
            crease = new List<Vector2> { new Vector2(-35, -109) };
            Curve(crease, -16, -105, 7, -106, 28, -109); Stroke(pixels, crease, 0.8f, 105);
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(Path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(Path);
        }

        private static void Curve(List<Vector2> path, float ax, float ay, float bx, float by, float cx, float cy)
        {
            Vector2 start = path[path.Count - 1], a = new Vector2(ax, ay), b = new Vector2(bx, by), c = new Vector2(cx, cy);
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, s = 1 - t;
                path.Add(s * s * s * start + 3 * s * s * t * a + 3 * s * t * t * b + t * t * t * c);
            }
        }

        private static void Stroke(Color32[] pixels, List<Vector2> path, float radius, byte opacity)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = (path[i - 1] + new Vector2(92, 140)) * 2, b = (path[i] + new Vector2(92, 140)) * 2;
                Vector2 delta = b - a; float length = delta.sqrMagnitude, r = radius * 2;
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 1));
                int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 1));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 1));
                int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 1));
                for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = length == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, delta) / length);
                    byte alpha = (byte)Mathf.RoundToInt(opacity * Mathf.Clamp01(r + 0.5f - Vector2.Distance(p, a + delta * t)));
                    int index = y * Width + x;
                    if (alpha > pixels[index].a) pixels[index] = new Color32(255, 255, 255, alpha);
                }
            }
        }
    }
}
