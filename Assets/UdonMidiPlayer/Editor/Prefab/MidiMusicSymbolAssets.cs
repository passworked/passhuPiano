using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Original outlined glyphs baked into a shared white glow atlas and tinted by particle color.</summary>
    public static class MidiMusicSymbolAssets
    {
        public const string MeshDirectory = "Assets/UdonMidiPlayer/Meshes/MusicSymbols";
        public const string MaterialPath = "Assets/UdonMidiPlayer/Materials/WaterfallMusicSymbols.mat";
        public const string AtlasPath = "Assets/UdonMidiPlayer/Art/Waterfall/WaterfallMusicSymbolsGlow.png";
        public const string ShaderPath = "Assets/UdonMidiPlayer/Shaders/WaterfallMusicSymbol.shader";
        private static readonly string[] Names = { "QuarterNote", "EighthNote", "BeamedNotes", "TrebleClef", "BassClef", "QuarterRest" };
        private static Mesh[] _meshes;
        private static Material _material;

        /// <summary>Updates only the existing floating-symbol renderer layer and its own assets.</summary>
        public static void UpdateExistingSymbolVisuals()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before updating symbol visuals.");
            string path = MidiPlayerBuilder.PrefabPath;
            if (!File.Exists(path)) throw new FileNotFoundException("Existing MIDI player Prefab is required.", path);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MidiWaterfallEffects[] effects = contents.GetComponentsInChildren<MidiWaterfallEffects>(true);
                if (effects.Length == 0) throw new InvalidOperationException("The existing Prefab has no music-symbol effects layer.");
                _meshes = null;
                _material = null;
                EnsureMeshes();
                EnsureMaterial();
                foreach (MidiWaterfallEffects effect in effects)
                {
                    if (effect.symbolParticles == null || effect.symbolParticles.Length == 0)
                        throw new InvalidOperationException("The existing effects component has no symbol particle pool.");
                    for (int i = 0; i < effect.symbolParticles.Length; i++)
                    {
                        ParticleSystem symbols = effect.symbolParticles[i];
                        if (symbols == null) throw new InvalidOperationException("Missing symbol particle renderer: " + i);
                        ConfigureRenderer(symbols, i);
                        EditorUtility.SetDirty(symbols.GetComponent<ParticleSystemRenderer>());
                    }
                }
                MidiBuildValidator.ValidateRoot(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null)
                    throw new InvalidOperationException("Could not save the existing music-symbol visuals.");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // Only renderer configuration belongs here. The presentation builder owns all simulation,
        // lifetime, size, alpha/size curves, velocity, shape and manual-emission parameters.
        public static void ConfigureRenderer(ParticleSystem particles, int variant = 0)
        {
            if (particles == null) throw new ArgumentNullException(nameof(particles));
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) throw new InvalidOperationException("Music-symbol particles require a ParticleSystemRenderer.");
            Mesh[] meshes = EnsureMeshes();
            // Unity limits an individual particle renderer to four meshes. Rotate the subset
            // across pooled zones so all six glyph assets remain represented in the effect.
            var selected = new Mesh[4];
            int first = ((variant % meshes.Length) + meshes.Length) % meshes.Length;
            for (int j = 0; j < selected.Length; j++) selected[j] = meshes[(first + j) % meshes.Length];
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.SetMeshes(selected, selected.Length);
            renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
            renderer.sharedMaterial = EnsureMaterial();
            renderer.enableGPUInstancing = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV
            });
        }

        public static Mesh[] EnsureMeshes()
        {
            bool ready = _meshes != null && _meshes.Length == Names.Length;
            if (ready) for (int i = 0; i < _meshes.Length; i++) if (_meshes[i] == null) ready = false;
            if (ready) return _meshes;
            Directory.CreateDirectory(MeshDirectory);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            _meshes = new Mesh[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                Mesh generated = BuildAtlasQuad(i);
                string path = MeshDirectory + "/" + Names[i] + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null)
                {
                    existing = generated;
                    AssetDatabase.CreateAsset(existing, path);
                }
                else
                {
                    // Rewrite native mesh buffers as well as serialized data. CopySerialized alone
                    // can leave an existing particle renderer drawing its previous filled buffers.
                    existing.Clear(false);
                    existing.indexFormat = generated.indexFormat;
                    existing.vertices = generated.vertices;
                    existing.normals = generated.normals;
                    existing.colors = generated.colors;
                    existing.uv = generated.uv;
                    existing.triangles = generated.triangles;
                    existing.bounds = generated.bounds;
                    existing.UploadMeshData(false);
                    UnityEngine.Object.DestroyImmediate(generated);
                }
                existing.name = Names[i];
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                _meshes[i] = existing;
            }
            return _meshes;
        }

        public static Material EnsureMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("MusicMidiPlayer/WaterfallMusicSymbol");
            if (shader == null) shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new InvalidOperationException("Import WaterfallMusicSymbol.shader before configuring symbol particles.");
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            _material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (_material == null)
            {
                _material = new Material(shader);
                AssetDatabase.CreateAsset(_material, MaterialPath);
            }
            _material.shader = shader;
            _material.name = Path.GetFileNameWithoutExtension(MaterialPath);
            _material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
            _material.SetColor("_Color", Color.white); // Emit supplies the current song's white/black-key color.
            _material.SetFloat("_Opacity", 1f);
            _material.SetFloat("_Softness", 0.85f);
            _material.SetFloat("_EdgeDarkening", 0f);
            _material.SetFloat("_EmissionStrength", 0.35f);
            _material.enableInstancing = false;
            _material.renderQueue = 3030;
            EditorUtility.SetDirty(_material);
            AssetDatabase.SaveAssetIfDirty(_material);
            return _material;
        }

        private static Mesh BuildAtlasQuad(int kind)
        {
            // The baked glyph occupies the original unit size; the extra margin contains its halo.
            float left = (kind % 4) * 0.25f, bottom = 1f - (kind / 4 + 1) * 0.5f;
            var mesh = new Mesh { name = Names[kind] };
            mesh.vertices = new[] { new Vector3(-0.65f, -0.65f, 0), new Vector3(0.65f, -0.65f, 0),
                new Vector3(0.65f, 0.65f, 0), new Vector3(-0.65f, 0.65f, 0) };
            mesh.uv = new[] { new Vector2(left, bottom), new Vector2(left + 0.25f, bottom),
                new Vector2(left + 0.25f, bottom + 0.5f), new Vector2(left, bottom + 0.5f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.Expand(new Vector3(0, 0, 0.01f)); mesh.bounds = bounds;
            return mesh;
        }

        private static Mesh BuildGlyph(int kind)
        {
            var geometry = new GlyphGeometry();
            if (kind == 0 || kind == 1)
            {
                geometry.Ellipse(new Vector2(-0.13f, -0.29f), 0.19f, 0.115f, -18f);
                geometry.Stroke(new[] { new Vector2(0.04f, -0.26f), new Vector2(0.04f, 0.49f) }, 0.055f);
                if (kind == 1)
                {
                    var flag = new List<Vector2> { new Vector2(0.045f, 0.49f) };
                    Cubic(flag, new Vector2(0.07f, 0.33f), new Vector2(0.36f, 0.34f), new Vector2(0.27f, 0.14f));
                    Cubic(flag, new Vector2(0.23f, 0.06f), new Vector2(0.15f, 0.04f), new Vector2(0.12f, 0.01f));
                    geometry.Stroke(flag, 0.067f);
                }
            }
            else if (kind == 2)
            {
                geometry.Ellipse(new Vector2(-0.25f, -0.31f), 0.15f, 0.095f, -18f);
                geometry.Ellipse(new Vector2(0.22f, -0.19f), 0.15f, 0.095f, -18f);
                geometry.Stroke(new[] { new Vector2(-0.12f, -0.28f), new Vector2(-0.12f, 0.36f) }, 0.048f);
                geometry.Stroke(new[] { new Vector2(0.35f, -0.16f), new Vector2(0.35f, 0.48f) }, 0.048f);
                geometry.Stroke(new[] { new Vector2(-0.12f, 0.36f), new Vector2(0.35f, 0.48f) }, 0.105f);
            }
            else if (kind == 3)
            {
                // The crossing ascending loop, central G spiral and lower curled stem form one stroke.
                var clef = new List<Vector2> { new Vector2(-0.05f, -0.52f) };
                Cubic(clef, new Vector2(0.16f, -0.56f), new Vector2(0.19f, -0.35f), new Vector2(0.10f, -0.13f));
                Cubic(clef, new Vector2(-0.01f, 0.11f), new Vector2(-0.14f, 0.36f), new Vector2(-0.07f, 0.55f));
                Cubic(clef, new Vector2(-0.02f, 0.69f), new Vector2(0.15f, 0.70f), new Vector2(0.17f, 0.54f));
                Cubic(clef, new Vector2(0.21f, 0.34f), new Vector2(-0.18f, 0.14f), new Vector2(-0.20f, -0.01f));
                Cubic(clef, new Vector2(-0.25f, -0.22f), new Vector2(-0.08f, -0.36f), new Vector2(0.10f, -0.31f));
                Cubic(clef, new Vector2(0.28f, -0.25f), new Vector2(0.29f, -0.04f), new Vector2(0.15f, 0.05f));
                Cubic(clef, new Vector2(0.00f, 0.15f), new Vector2(-0.16f, 0.04f), new Vector2(-0.13f, -0.10f));
                Cubic(clef, new Vector2(-0.11f, -0.18f), new Vector2(0.02f, -0.21f), new Vector2(0.08f, -0.11f));
                geometry.Stroke(clef, 0.064f);
                geometry.Ellipse(new Vector2(-0.05f, -0.52f), 0.052f, 0.043f, -20f);
            }
            else if (kind == 4)
            {
                var clef = new List<Vector2> { new Vector2(-0.18f, 0.20f) };
                Cubic(clef, new Vector2(-0.12f, 0.38f), new Vector2(0.15f, 0.37f), new Vector2(0.16f, 0.13f));
                Cubic(clef, new Vector2(0.19f, -0.15f), new Vector2(-0.02f, -0.35f), new Vector2(-0.24f, -0.42f));
                geometry.Stroke(clef, 0.080f);
                geometry.Ellipse(new Vector2(-0.18f, 0.20f), 0.10f, 0.09f, 0f);
                geometry.Ellipse(new Vector2(0.33f, 0.18f), 0.047f, 0.047f, 0f);
                geometry.Ellipse(new Vector2(0.33f, -0.065f), 0.047f, 0.047f, 0f);
            }
            else
            {
                var rest = new List<Vector2>
                {
                    new Vector2(-0.15f, 0.50f), new Vector2(0.12f, 0.27f),
                    new Vector2(-0.12f, 0.045f), new Vector2(0.12f, -0.17f)
                };
                Cubic(rest, new Vector2(-0.13f, -0.15f), new Vector2(-0.25f, -0.31f), new Vector2(-0.13f, -0.43f));
                Cubic(rest, new Vector2(-0.08f, -0.48f), new Vector2(0.00f, -0.47f), new Vector2(0.035f, -0.40f));
                geometry.Stroke(rest, 0.082f);
            }
            return geometry.ToMesh("MusicSymbol_" + Names[kind]);
        }

        private static void Cubic(List<Vector2> path, Vector2 c1, Vector2 c2, Vector2 end)
        {
            Vector2 start = path[path.Count - 1];
            const int segments = 12;
            for (int step = 1; step <= segments; step++)
            {
                float t = step / (float)segments;
                float u = 1f - t;
                path.Add(u * u * u * start + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * end);
            }
        }

        private sealed class GlyphGeometry
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector2> _coverage = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();
            private const float Feather = 0.004f;

            private int Vertex(Vector2 point, float coverage)
            {
                int index = _vertices.Count;
                _vertices.Add(new Vector3(point.x, point.y, 0));
                _coverage.Add(new Vector2(coverage, 0.5f));
                return index;
            }

            private void Quad(int a, int b, int c, int d)
            {
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(c);
                _triangles.Add(a); _triangles.Add(c); _triangles.Add(d);
            }

            public void Stroke(IList<Vector2> points, float width)
            {
                if (points == null || points.Count < 2) return;
                // Wider rails survive the small on-screen particle size; retain an empty center.
                width *= 1.65f;
                float half = width * 0.5f;
                float outline = Mathf.Min(half * 0.84f, Mathf.Clamp(width * 0.38f, 0.028f, 0.052f));
                float inside = half - outline;
                float feather = Mathf.Min(Feather, outline * 0.22f);
                int begin = _vertices.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 previous = points[Mathf.Max(0, i - 1)];
                    Vector2 next = points[Mathf.Min(points.Count - 1, i + 1)];
                    Vector2 tangent = (next - previous).normalized;
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    Vector2 direction = i + 1 < points.Count ? (next - points[i]).normalized : (points[i] - previous).normalized;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    float miter = 1f / Mathf.Max(0.67f, Mathf.Abs(Vector2.Dot(normal, side)));
                    // Two separate boundary rails. Nothing bridges the empty center of the stroke.
                    Vertex(points[i] + normal * (half * miter), 0f);
                    Vertex(points[i] + normal * ((half - feather) * miter), 1f);
                    Vertex(points[i] + normal * ((inside + feather) * miter), 1f);
                    Vertex(points[i] + normal * (inside * miter), 0f);
                    Vertex(points[i] - normal * (inside * miter), 0f);
                    Vertex(points[i] - normal * ((inside + feather) * miter), 1f);
                    Vertex(points[i] - normal * ((half - feather) * miter), 1f);
                    Vertex(points[i] - normal * (half * miter), 0f);
                    if (i == 0) continue;
                    int current = begin + i * 8;
                    for (int band = 0; band < 3; band++)
                    {
                        Quad(current - 8 + band, current + band, current + band + 1, current - 8 + band + 1);
                        Quad(current - 4 + band, current + 4 + band, current + 5 + band, current - 3 + band);
                    }
                }
                Vector2 startDirection = (points[0] - points[1]).normalized;
                Vector2 endDirection = (points[points.Count - 1] - points[points.Count - 2]).normalized;
                float startAngle = Mathf.Atan2(startDirection.y, startDirection.x);
                float endAngle = Mathf.Atan2(endDirection.y, endDirection.x);
                ArcBand(points[0], half, half, 0f, startAngle - Mathf.PI * 0.5f, startAngle + Mathf.PI * 0.5f, outline, 16);
                ArcBand(points[points.Count - 1], half, half, 0f, endAngle - Mathf.PI * 0.5f, endAngle + Mathf.PI * 0.5f, outline, 16);
            }

            public void Ellipse(Vector2 center, float radiusX, float radiusY, float degrees, int segments = 32)
            {
                float radius = Mathf.Min(radiusX, radiusY);
                float outline = Mathf.Min(radius * 0.70f, Mathf.Clamp(radius * 0.52f, 0.030f, 0.055f));
                ArcBand(center, radiusX, radiusY, degrees, 0f, Mathf.PI * 2f, outline, segments);
            }

            private void ArcBand(Vector2 center, float radiusX, float radiusY, float degrees,
                float startPhase, float endPhase, float outline, int segments)
            {
                float angle = degrees * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
                float innerX = Mathf.Max(0.002f, radiusX - outline);
                float innerY = Mathf.Max(0.002f, radiusY - outline);
                float feather = Mathf.Min(Feather, outline * 0.22f);
                int ring = _vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float phase = Mathf.Lerp(startPhase, endPhase, i / (float)segments);
                    float x = Mathf.Cos(phase), y = Mathf.Sin(phase);
                    for (int edge = 0; edge < 4; edge++)
                    {
                        float rx = edge == 0 ? radiusX : edge == 1 ? radiusX - feather : edge == 2 ? innerX + feather : innerX;
                        float ry = edge == 0 ? radiusY : edge == 1 ? radiusY - feather : edge == 2 ? innerY + feather : innerY;
                        Vector2 point = new Vector2(x * rx, y * ry);
                        Vector2 rotated = new Vector2(point.x * cosine - point.y * sine, point.x * sine + point.y * cosine);
                        Vertex(center + rotated, edge == 0 || edge == 3 ? 0f : 1f);
                    }
                    if (i == 0) continue;
                    int current = ring + i * 4;
                    for (int band = 0; band < 3; band++)
                        Quad(current - 4 + band, current + band, current + band + 1, current - 4 + band + 1);
                }
            }

            public Mesh ToMesh(string name)
            {
                if (_vertices.Count == 0) throw new InvalidOperationException("Empty musical glyph.");
                Vector3 min = _vertices[0], max = _vertices[0];
                foreach (Vector3 vertex in _vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
                Vector3 center = (min + max) * 0.5f;
                float scale = 1f / Mathf.Max(max.x - min.x, max.y - min.y);
                for (int i = 0; i < _vertices.Count; i++) _vertices[i] = (_vertices[i] - center) * scale;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetUVs(0, _coverage);
                mesh.SetTriangles(_triangles, 0);
                var colors = new Color[_vertices.Count];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                mesh.colors = colors;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Bounds bounds = mesh.bounds;
                bounds.Expand(new Vector3(0, 0, 0.01f));
                mesh.bounds = bounds; // Preserve XY aspect; only add conservative culling thickness.
                mesh.UploadMeshData(false);
                return mesh;
            }
        }
    }
}
