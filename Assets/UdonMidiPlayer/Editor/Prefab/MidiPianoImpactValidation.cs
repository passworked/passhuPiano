using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Explicit isolated preview: musical-time, velocity, dense chords and asset wiring.</summary>
    public static class MidiPianoImpactValidation
    {
        [Serializable] public class Report { public string environment = "Unity Editor C# proxy preview"; public List<string> checks = new List<string>(); public List<string> captures = new List<string>(); }
        static void Need(Report r, bool ok, string label) { if (!ok) throw new InvalidOperationException(label); r.checks.Add(label); }
        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }

        public static string RunEditorPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before previewing.");
            var source = Resources.FindObjectsOfTypeAll<MidiWaterfallView>().First(v => v.gameObject.scene.IsValid()
                && !EditorSceneManager.IsPreviewScene(v.gameObject.scene) && v.instrumentTarget == 0 && v.gameObject.activeInHierarchy);
            var report = new Report();
            var existingMaterials = new HashSet<int>(Resources.FindObjectsOfTypeAll<Material>().Select(m => m.GetInstanceID()));
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(source.transform.parent.parent.gameObject);
                SceneManager.MoveGameObjectToScene(clone, preview); clone.SetActive(true);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.transform.localScale = Vector3.one;
                var view = clone.GetComponentInChildren<MidiWaterfallView>(true);
                var keys = view.keyboard; var fx = view.effects;
                view.instrumentRouter = null; keys.instrumentRouter = null;
                GameObject data = new GameObject("Isolated presentation data"); SceneManager.MoveGameObjectToScene(data, preview);
                var seq = data.AddUdonSharpComponent<MidiSequencer>();
                var song = data.AddUdonSharpComponent<MidiSongData>();
                keys.sequencer = seq; seq.song = song;
                song.sourceHash = new string('3', 64); song.contentHash = "piano-impact-preview";
                song.startMs = new[] { 100, 100, 100, 500, 900, 1150 };
                song.keyUpMs = new[] { 800, 800, 800, 950, 1400, 1600 };
                song.releaseMs = song.keyUpMs.ToArray();
                song.pitch = new[] { 60, 61, 64, 67, 72, 55 };
                song.velocity = new[] { 32, 80, 127, 95, 100, 65 };
                song.channel = new int[6]; song.trackIndex = new int[6];
                seq.activeNoteIds = new int[128]; seq.keyDownCount = new int[88];
                view.BindSong(song, seq); view.RebuildAt(0, 1);
                Need(report, fx.hitParticles.Length == 1 && fx.symbolParticles.Length == 1, "Two particle systems per piano");
                Need(report, !fx.hitParticles[0].collision.enabled && !fx.symbolParticles[0].collision.enabled, "No particle world collision");
                Need(report, fx.keyImpactRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount == 1024, "128 pitches share one 1024-vertex impact mesh");
                for (int i = 0; i < 3; i++) { seq.keyDownCount[song.pitch[i] - 21] = 1; seq.activeNoteIds[i] = i; }
                seq.activeNoteCount = 3;
                view.Refresh(100, 1); view.Refresh(130, 1); keys.Refresh(130, 1, true);
                var impact = Field<Material>(fx, "_impactMaterial");
                var states = impact.GetVectorArray("_ImpactData");
                Need(report, states[60].y < states[61].y && states[61].y < states[64].y, "Independent soft/medium/strong velocity envelopes on adjacent chord keys");
                Need(report, states[60].x == states[61].x && states[61].x == states[64].x && Field<int>(fx, "_impactCount") == 3, "Simultaneous chord retains three independent impacts");
                var bars = Field<int[]>(view, "_slotNote");
                var block = new MaterialPropertyBlock();
                for (int note = 0; note < 3; note++)
                {
                    int slot = Array.IndexOf(bars, note); view.barRenderers[slot].GetPropertyBlock(block);
                    Need(report, Mathf.Abs(block.GetVector("_NoteTiming").w - song.velocity[note] / 127f) < 0.0001f, "File waterfall velocity " + note);
                }
                float time = impact.GetFloat("_VisualTime"); float particleTime = fx.hitParticles[0].time;
                view.RebuildGeometryAt(130, 2);
                Need(report, impact.GetFloat("_VisualTime") == time && fx.hitParticles[0].time == particleTime, "Pause with generation change freezes impacts and particles");
                Camera camera = PreviewCamera(preview, view);
                CheckSymbolAtlas(report, preview, camera, fx.symbolParticles[0]);
                Capture(report, camera, "01-chord-velocity");
                view.Refresh(240, 2); keys.Refresh(240, 2, true); Capture(report, camera, "02-crystal-and-decay");
                seq.keyDownCount[60 - 21] = 0; view.Refresh(270, 2);
                Need(report, impact.GetVectorArray("_ImpactData")[60].z >= 0 && impact.GetVectorArray("_ImpactData")[61].z < 0, "Releasing one chord key preserves its held neighbor");
                int emitted = fx.emittedParticleCount;
                view.Refresh(260, 1);
                Need(report, impact.GetFloat("_VisualTime") == 0.270f && fx.emittedParticleCount == emitted, "Stale generation cannot alter the current effect");
                view.RebuildAt(400, 3);
                Need(report, fx.emittedParticleCount == 0 && Field<int>(fx, "_impactCount") == 2
                    && impact.GetVectorArray("_ImpactData")[61].x < -1f, "Seek restores quiet held keys without replaying attacks");
                view.SetLandingAtKeyRoot(!view.landAtKeyRoot);
                view.effects.Refresh(400, 3);
                Need(report, fx.keyImpactRenderer.enabled != fx.keyImpactRootRenderer.enabled, "Front/rear landing uses exactly one impact renderer");
                view.Clear(4);
                Need(report, !fx.keyImpactRenderer.enabled && !fx.keyImpactRootRenderer.enabled && fx.liveParticleCount == 0, "Stop clears both impact layouts and particles");
                seq.keyDownCount[39] = 1;
                view.LiveNoteDownVelocity(60, 60, 24); fx.LiveHit(60, 24, 5);
                float weak = impact.GetVectorArray("_ImpactData")[60].y;
                view.LiveNoteDownVelocity(64, 64, 127); fx.LiveHit(64, 127, 5);
                Need(report, impact.GetVectorArray("_ImpactData")[64].y > weak, "Live MIDI retains velocity contrast");
                view.Clear(6);
                song.startMs = Enumerable.Repeat(100, 128).ToArray(); song.keyUpMs = Enumerable.Repeat(500, 128).ToArray();
                song.pitch = Enumerable.Range(0, 128).ToArray(); song.velocity = Enumerable.Repeat(127, 128).ToArray(); song.channel = new int[128];
                view.BindSong(song, seq); view.RebuildAt(0, 7); view.Refresh(100, 7);
                Need(report, Field<int>(fx, "_impactCount") == 128, "128-pitch chord keeps exact impacts even when decoration is throttled");
                Need(report, fx.liveParticleCount <= 192 && fx.emittedParticleCount <= 24, "Dense chord respects total and per-frame particle budgets");
                view.Clear(8);
                Need(report, Field<int>(view, "_activeCount") == 0 && Field<int>(view, "_liveCount") == 0, "Clear retires active file/live slot lists");
                foreach (string shader in new[] { "MusicMidiPlayer/WaterfallVolume", "MusicMidiPlayer/PianoKeyImpact", "MusicMidiPlayer/PianoKeyBreathing" })
                    Need(report, !ShaderUtil.GetShaderMessages(Shader.Find(shader)).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), "Compiled shader: " + shader);
                Directory.CreateDirectory("Logs/PianoImpacts");
                File.WriteAllText("Logs/PianoImpacts/editor-preview.json", JsonUtility.ToJson(report, true));
                return JsonUtility.ToJson(report, true);
            }
            finally
            {
                // Explicit preview-created material instances are native objects too.
                var materials = new HashSet<Material>();
                foreach (GameObject root in preview.GetRootGameObjects())
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        foreach (Material material in renderer.sharedMaterials) if (material != null) materials.Add(material);
                    foreach (PianoKeyboardView keys in root.GetComponentsInChildren<PianoKeyboardView>(true))
                        foreach (string field in new[] { "_noteMaterials", "_blackPressedMaterials" })
                        { var cached = Field<Material[]>(keys, field); if (cached != null) foreach (Material material in cached) if (material != null) materials.Add(material); }
                }
                EditorSceneManager.ClosePreviewScene(preview);
                foreach (Material material in materials)
                    if (material != null && !existingMaterials.Contains(material.GetInstanceID()) && !EditorUtility.IsPersistent(material))
                        UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static Camera PreviewCamera(Scene scene, MidiWaterfallView view)
        {
            var go = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(go, scene);
            Camera camera = go.AddComponent<Camera>(); camera.scene = scene;
            Vector3 target = view.transform.TransformPoint(new Vector3(0.06f, 0.20f, 0.02f));
            camera.transform.position = target + view.transform.TransformVector(new Vector3(0.08f, 0.47f, -0.95f));
            camera.transform.LookAt(target); camera.fieldOfView = 48; camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f,0.045f,0.055f);
            camera.allowHDR = true; camera.allowMSAA = true;
            GameObject lamp = new GameObject("Preview-only studio light"); SceneManager.MoveGameObjectToScene(lamp, scene);
            var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
            lamp.transform.rotation = Quaternion.Euler(45, -25, 0);
            return camera;
        }

        static void CheckSymbolAtlas(Report report, Scene scene, Camera camera, ParticleSystem source)
        {
            var copy = UnityEngine.Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(copy, scene);
            var mesh = new Mesh();
            try
            {
                var particles = copy.GetComponent<ParticleSystem>();
                particles.useAutoRandomSeed = false; particles.randomSeed = 113;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Emit(32); particles.Simulate(0.15f, false, false, false); particles.Pause();
#pragma warning disable 618
                copy.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh, camera, false);
#pragma warning restore 618
                var uv = mesh.uv; var frames = new HashSet<int>();
                for (int i = 0; i + 3 < uv.Length; i += 4)
                {
                    Vector2 center = (uv[i] + uv[i+1] + uv[i+2] + uv[i+3]) * 0.25f;
                    frames.Add(Mathf.FloorToInt(center.x * 4) + (1 - Mathf.FloorToInt(center.y * 2)) * 4);
                }
                Need(report, frames.SetEquals(Enumerable.Range(0,6)), "Rendered particle UVs cover exactly six glyphs without the two empty atlas tiles");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(copy); }
        }

        static void Capture(Report r, Camera camera, string name)
        {
            string folder = "Captures/PianoImpacts"; Directory.CreateDirectory(folder);
            var texture = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                string path = folder + "/" + name + ".png"; File.WriteAllBytes(path, image.EncodeToPNG()); r.captures.Add(path);
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
