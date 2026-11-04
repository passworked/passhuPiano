using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Explicit, bounded backing-Udon probe; never runs on import/build unless Begin was called.
    [InitializeOnLoad]
    public static class MidiPianoImpactPlayModeValidation
    {
        const string Key = "MMP.PianoImpacts.Probe";
        [Serializable] class Report { public string environment = "Unity Editor Play Mode, backing Udon VM"; public bool passed; public string error; public List<string> checks = new List<string>(); }
        static Report report = new Report();
        static int stage;
        static double started, next;
        static MidiPlayerFacade proxy;
        static MidiWaterfallView view;
        static UdonBehaviour facade, effects;
        static float pausedTime, particleTime;
        static Material impact;
        static MidiPianoImpactPlayModeValidation()
        { if (SessionState.GetBool(Key, false)) { started = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; } }
        public static string Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Start from the saved scene outside Play mode.");
            SessionState.SetBool(Key, true); stage = 0; report = new Report(); started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick; EditorApplication.update += Tick; EditorApplication.isPlaying = true;
            return "Started bounded piano-impact backing-Udon probe.";
        }
        static void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); report.checks.Add(text); }
        static UdonBehaviour Back(UdonSharpBehaviour value) { return UdonSharpEditorUtility.GetBackingUdonBehaviour(value); }
        static void Call(UdonBehaviour b, string method, params object[] args)
        {
            var program = ((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram();
            var symbols = program.SymbolTable.GetSymbols();
            for (int i = 0; i < args.Length; i += 2)
            {
                var names = symbols.Where(s => Regex.IsMatch(s, "^__\\d+_" + args[i] + "__param$")).ToArray();
                if (names.Length == 0) throw new MissingMemberException(method + "." + args[i]);
                foreach (string name in names) b.SetProgramVariable(name, args[i + 1]);
            }
            b.SendCustomEvent(program.EntryPoints.GetExportedSymbols().Contains(method) ? method : "__0_" + method);
        }
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 45) throw new TimeoutException("Piano impact VM probe timed out.");
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0)
                {
                    proxy = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>(); if (proxy == null) return;
                    facade = Back(proxy);
                    if (!(bool)facade.GetProgramVariable("isReady") || (bool)facade.GetProgramVariable("isSongLoading")
                        || !(bool)Back(proxy.network).GetProgramVariable("networkReady")) return;
                    Call(facade, "CommandStop");
                    view = proxy.instrumentWaterfalls.First(v => v != null && v.instrumentTarget == 0 && v.gameObject.activeInHierarchy);
                    effects = Back(view.effects);
                    Need((bool)Back(view).GetProgramVariable("pianoImpactPresentation") && (bool)effects.GetProgramVariable("compactParticles"), "Scene backing Udon enables the piano presentation");
                    Need(proxy.voicePool.voiceSources.Length == 64, "Physical AudioSource budget remains 64");
                    Call(facade, "_LiveNoteOn", "channel", 0, "number", 60, "velocity", 24);
                    Call(facade, "_LiveNoteOn", "channel", 0, "number", 61, "velocity", 127);
                    stage = 1; next = EditorApplication.timeSinceStartup + 0.06; return;
                }
                if (stage == 1)
                {
                    impact = (Material)effects.GetProgramVariable("_impactMaterial");
                    var state = (Vector4[])effects.GetProgramVariable("_impactData");
                    Need(state[60].y > 0 && state[60].y < state[61].y && state[60].z < 0 && state[61].z < 0, "Real live MIDI creates independent velocity-sensitive held impacts");
                    Need(view.effects.keyImpactRenderer.enabled || view.effects.keyImpactRootRenderer.enabled, "One combined impact renderer draws the live chord");
                    Need((int)effects.GetProgramVariable("liveParticleCount") <= 192, "Real Udon particle count stays within 192");
                    var slotIds = (int[])Back(view).GetProgramVariable("_liveId");
                    int slot = Array.IndexOf(slotIds, 61); var block = new MaterialPropertyBlock(); view.barRenderers[slot].GetPropertyBlock(block);
                    Need(block.GetVector("_NoteTiming").w == 1f, "Real live waterfall forwards full velocity to its crystal shader");
                    Call(facade, "_LiveControlChange", "channel", 0, "number", 64, "value", 127);
                    Call(facade, "_LiveNoteOff", "channel", 0, "number", 60, "velocity", 0);
                    stage = 2; next = EditorApplication.timeSinceStartup + 0.08; return;
                }
                if (stage == 2)
                {
                    var state = (Vector4[])effects.GetProgramVariable("_impactData");
                    Need(state[60].z >= 0 && state[61].z < 0, "Sustain pedal does not keep the released physical key lit");
                    Call(facade, "_LiveNoteOff", "channel", 0, "number", 61, "velocity", 0);
                    Call(facade, "_LiveControlChange", "channel", 0, "number", 64, "value", 0);
                    Call(facade, "CommandPlay"); Call(facade, "CommandSeek", "progress", 0.15f);
                    stage = 3; next = EditorApplication.timeSinceStartup + 0.4; return;
                }
                if (stage == 3)
                {
                    if ((bool)facade.GetProgramVariable("isSongLoading")) return;
                    Call(facade, "CommandPause"); stage = 4; next = EditorApplication.timeSinceStartup + 0.08; return;
                }
                if (stage == 4)
                {
                    pausedTime = impact.GetFloat("_VisualTime"); particleTime = view.effects.hitParticles[0].time;
                    stage = 5; next = EditorApplication.timeSinceStartup + 0.12; return;
                }
                if (stage == 5)
                {
                    Need((int)Back(proxy.clock).GetProgramVariable("_state") == 2
                        && impact.GetFloat("_VisualTime") == pausedTime && view.effects.hitParticles[0].time == particleTime, "Backing Udon pause freezes musical-time impact and particle animation");
                    Call(facade, "CommandStop");
                    Need(!view.effects.keyImpactRenderer.enabled && !view.effects.keyImpactRootRenderer.enabled
                        && view.effects.hitParticles[0].particleCount == 0, "Backing Udon Stop clears the piano effects");
                    var scene = view.gameObject.scene;
                    Need(Resources.FindObjectsOfTypeAll<Light>().Count(l => l.gameObject.scene == scene) == 1, "Clockwork scene retains exactly one Light including inactive objects");
                    Finish(null);
                }
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }
        static void Finish(string error)
        {
            report.passed = error == null; report.error = error ?? "";
            Directory.CreateDirectory("Logs/PianoImpacts"); File.WriteAllText("Logs/PianoImpacts/playmode.json", JsonUtility.ToJson(report, true));
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick; EditorApplication.isPlaying = false;
        }
    }
}
