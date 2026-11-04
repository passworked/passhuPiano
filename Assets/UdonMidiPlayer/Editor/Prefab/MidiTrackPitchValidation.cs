using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Explicit and bounded; exercises native UI and the compiled backing Udon.
    [InitializeOnLoad]
    public static class MidiTrackPitchValidation
    {
        const string Key = "MMP.TrackPitch.Probe";
        static readonly List<string> Checks = new List<string>();
        static readonly Dictionary<int, string[]> Symbols = new Dictionary<int, string[]>();
        static readonly Dictionary<int, string[]> Exports = new Dictionary<int, string[]>();
        static readonly Dictionary<string, string[]> Parameters = new Dictionary<string, string[]>();
        static readonly Dictionary<string, string> Returns = new Dictionary<string, string>();
        static MidiPlayerFacade f;
        static UdonBehaviour facade, router, ui, network, clock, song;
        static int stage, track;
        static int[] noteTracks, pitches;
        static double started;
        static MidiTrackPitchValidation()
        { if (SessionState.GetBool(Key, false)) { started = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; } }
        [MenuItem("Tools/Music MIDI Player/Validate Track Pitch (Play Mode)")]
        public static void Begin()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Start from the saved main scene outside Play mode.");
            Checks.Clear(); Symbols.Clear(); Exports.Clear(); Parameters.Clear(); Returns.Clear();
            stage = 0; started = EditorApplication.timeSinceStartup; SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick; EditorApplication.isPlaying = true;
        }
        static UdonBehaviour Back(UdonSharpBehaviour b) { return UdonSharpEditorUtility.GetBackingUdonBehaviour(b); }
        static void Need(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); Checks.Add(label); }
        static void Prepare(UdonBehaviour b)
        {
            if (Symbols.ContainsKey(b.GetInstanceID())) return;
            var p = ((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram();
            Symbols[b.GetInstanceID()] = p.SymbolTable.GetSymbols().ToArray(); Exports[b.GetInstanceID()] = p.EntryPoints.GetExportedSymbols().ToArray();
        }
        static void Call(UdonBehaviour b, string method, params object[] args)
        {
            Prepare(b); int id = b.GetInstanceID();
            for (int i = 0; i < args.Length; i += 2)
            {
                string key = id + "." + args[i];
                if (!Parameters.TryGetValue(key, out var names))
                    Parameters[key] = names = Symbols[id].Where(s => Regex.IsMatch(s, "^__\\d+_" + args[i] + "__param$")).ToArray();
                if (names.Length == 0) throw new MissingMemberException(method + "." + args[i]);
                foreach (string name in names) b.SetProgramVariable(name, args[i + 1]);
            }
            b.SendCustomEvent(Exports[id].Contains(method) ? method : "__0_" + method);
        }
        static T Result<T>(UdonBehaviour b, string method)
        {
            Prepare(b); string key = b.GetInstanceID() + "." + method;
            if (!Returns.TryGetValue(key, out string name))
                Returns[key] = name = Symbols[b.GetInstanceID()].Where(s => s.EndsWith("_" + method + "__ret", StringComparison.Ordinal)).OrderBy(s => s.Length).First();
            return (T)b.GetProgramVariable(name);
        }
        static int State() { Call(clock, "GetState"); return Result<int>(clock, "GetState"); }
        static double Position() { Call(clock, "GetPositionSeconds"); return Result<double>(clock, "GetPositionSeconds"); }
        static int Octave(int source) { Call(router, "FileTrackOctave", "track", source); return Result<int>(router, "FileTrackOctave"); }
        static int Pitch(int note) { Call(router, "PitchForNote", "note", note); return Result<int>(router, "PitchForNote"); }
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 55) throw new TimeoutException("Track pitch probe timed out.");
                if (stage == 0)
                {
                    f = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>(); if (f == null) return;
                    facade = Back(f); if (!(facade.GetProgramVariable("isReady") is bool ready) || !ready || (bool)facade.GetProgramVariable("isSongLoading")) return;
                    router = Back(f.instrumentRouter); ui = Back(f.playerUI); network = Back(f.network); clock = Back(f.clock);
                    if (!(bool)network.GetProgramVariable("networkReady")) return;
                    Call(facade, "CommandSelectSong", "id", "408c3f56422358049b5f154a35ad871c"); stage++; return;
                }
                if ((bool)facade.GetProgramVariable("isSongLoading")) return;
                if (stage == 1)
                {
                    song = Back(f.library.songs.First(s => s.songId == (string)facade.GetProgramVariable("selectedSongId")));
                    noteTracks = (int[])song.GetProgramVariable("trackIndex"); pitches = ((int[])song.GetProgramVariable("pitch")).ToArray();
                    var used = (bool[])router.GetProgramVariable("usedFileTracks"); track = Array.FindIndex(used, x => x);
                    MathAndCodec();
                    Call(ui, "_OpenFileMapping"); Call(ui, "_FileByTrack");
                    Need(!f.playerUI.fileAutoRangeToggle.isOn, "Auto-range defaults off");
                    f.playerUI.fileOctaveUpButtons[0].onClick.Invoke(); f.playerUI.fileOctaveUpButtons[0].onClick.Invoke();
                    Need(State() == 0 && Octave(track) == 2, "Native plus button adjusts twice while stopped");
                    Need(!(bool)facade.GetProgramVariable("isSongLoading"), "Pitch edits avoid rebuilding drum indices");
                    f.playerUI.fileOctaveDownButtons[0].onClick.Invoke(); Need(Octave(track) == 1, "Native minus lowers one octave");
                    int n = Array.IndexOf(noteTracks, track); Need(Pitch(n) == pitches[n] + 12, "File note pitch includes the selected octave");
                    f.playerUI.fileAutoRangeToggle.isOn = true;
                    Need(((string)router.GetProgramVariable("fileTrackMap")).StartsWith("P1"), "Native auto-range toggle reaches shared settings");
                    Need((string)network.GetProgramVariable("_pendingFileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Pending snapshot includes octave and range settings");
                    network.SendCustomEvent("_onPreSerialization");
                    Need((string)network.GetProgramVariable("fileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Public snapshot includes octave and range settings");
                    Need((string)network.GetProgramVariable("_flightFileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Flight snapshot includes octave and range settings");
                    Pagination();
                    Call(facade, "CommandSeek", "progress", .3f); Call(facade, "CommandPlay"); stage++; return;
                }
                if (stage == 2)
                {
                    double position = Position(); int generation = (int)facade.GetProgramVariable("applyGeneration");
                    f.playerUI.fileOctaveUpButtons[0].onClick.Invoke();
                    Need(State() == 1 && Math.Abs(Position() - position) < .05, "Octave edit keeps playing at the current position");
                    Need((int)facade.GetProgramVariable("applyGeneration") > generation && !(bool)facade.GetProgramVariable("isSongLoading"), "Octave edit immediately rebuilds current voices");
                    var pool = Back(f.voicePool.ensemble); var active = (bool[])pool.GetProgramVariable("active");
                    var live = (bool[])pool.GetProgramVariable("live"); var ids = (int[])pool.GetProgramVariable("identities"); var sounding = (int[])pool.GetProgramVariable("pitches");
                    int count = 0; bool match = true;
                    for (int i = 0; i < active.Length; i++) if (active[i] && !live[i]) { count++; if (sounding[i] != Pitch(ids[i])) match = false; }
                    Need(count > 0 && match, "Actual playing voices use the effective file pitches");
                    Call(facade, "CommandPause"); position = Position(); f.playerUI.fileOctaveDownButtons[0].onClick.Invoke();
                    Need(State() == 2 && Math.Abs(Position() - position) < .001, "Paused octave edit retains state and exact position");
                    CheckViews();
                    Call(facade, "CommandFileTrackTarget", "track", track, "target", 7); stage++; return;
                }
                if (stage == 3)
                {
                    int[] low = (int[])router.GetProgramVariable("nativeRangeLow"), high = (int[])router.GetProgramVariable("nativeRangeHigh");
                    bool match = true;
                    for (int i = 0; i < pitches.Length; i++) if (noteTracks[i] == track && (Pitch(i) < low[7] || Pitch(i) > high[7])) match = false;
                    Need(match, "Changing mapping makes auto-range use music-box recorded bounds");
                    Need(Octave(track) == 1, "Target change preserves octave setting");
                    f.playerUI.fileTrackEnabledToggles[0].isOn = false; stage++; return;
                }
                if (stage == 4)
                {
                    f.playerUI.fileOctaveUpButtons[0].onClick.Invoke(); Need(Octave(track) == 2, "Disabled track can be tuned in advance");
                    Call(router, "IsFileTrackEnabled", "track", track); Need(!Result<bool>(router, "IsFileTrackEnabled"), "Octave edit preserves disabled track");
                    f.playerUI.fileTrackEnabledToggles[0].isOn = true; stage++; return;
                }
                if (stage == 5)
                {
                    Call(facade, "CommandFileTrackTarget", "track", track, "target", 8); stage++; return;
                }
                if (stage == 6)
                {
                    int n = Array.IndexOf(noteTracks, track); Call(router, "FileDrumPitch", "note", n);
                    Need(Result<int>(router, "FileDrumPitch") == pitches[n], "Octaves and auto-range preserve explicit drum keys");
                    Need(!f.playerUI.fileOctaveUpButtons[0].interactable && !f.playerUI.fileOctaveDownButtons[0].interactable, "Drum mapping disables octave buttons");
                    Call(facade, "CommandStop"); Call(facade, "CommandFileTrackTarget", "track", track, "target", 0); stage++; return;
                }
                if (stage == 7)
                {
                    Call(facade, "PerformInstrumentNoteOn", "channel", 0, "number", 60, "velocity", 100, "view", 1);
                    // Inspect local live path directly, independently of ClientSim's network timing.
                    Call(facade, "_LiveNoteOn", "channel", 0, "number", 60, "velocity", 100);
                    var pool = Back(f.voicePool.ensemble); var active = (bool[])pool.GetProgramVariable("active"); var live = (bool[])pool.GetProgramVariable("live");
                    Need(Enumerable.Range(0, active.Length).Any(i => active[i] && live[i]), "Stopped player has a held live note");
                    f.playerUI.fileOctaveDownButtons[0].onClick.Invoke(); f.playerUI.fileAutoRangeToggle.isOn = false;
                    Need(Enumerable.Range(0, active.Length).Any(i => active[i] && live[i]), "File pitch settings preserve held live voices while stopped");
                    Call(facade, "_LiveNoteOff", "channel", 0, "number", 60, "velocity", 0);
                    Capture();
                    string map = (string)router.GetProgramVariable("fileTrackMap"); Call(Back(f.gate), "_Lock");
                    Call(facade, "CommandFileTrackOctave", "track", track, "octave", -4); Call(facade, "CommandFileAutoRange", "enabled", true);
                    Need((string)router.GetProgramVariable("fileTrackMap") == map, "Locked gate rejects octave and range edits"); Call(Back(f.gate), "_Unlock");
                    Call(ui, "_FileByProgram");
                    Need(!f.playerUI.fileAutoRangeToggle.gameObject.activeSelf && f.playerUI.fileOctaveUpButtons.All(b => !b.gameObject.activeSelf), "Program mapping hides track pitch controls");
                    Call(ui, "_FileByTrack"); f.playerUI.fileAutoRangeToggle.isOn = true; Call(ui, "_ResetFileMapping"); stage++; return;
                }
                Need((string)router.GetProgramVariable("fileTrackMap") == "P1" && Octave(track) == 0, "Track reset clears octaves and targets while preserving auto-range");
                Need(((int[])song.GetProgramVariable("pitch")).SequenceEqual(pitches), "Source MIDI pitches remain unchanged"); Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void MathAndCodec()
        {
            string saved = (string)router.GetProgramVariable("fileTrackMap");
            try
            {
                router.SetProgramVariable("fileTrackMap", "P1"); Call(router, "ResolveSelection");
                int[] low = (int[])router.GetProgramVariable("nativeRangeLow"), high = (int[])router.GetProgramVariable("nativeRangeHigh");
                foreach (int target in new[] { 0, 1, 2, 3, 4, 5, 7 }) foreach (int octave in new[] { -10, -1, 0, 1, 10 })
                {
                    bool ok = true;
                    for (int pitch = 0; pitch < 128; pitch++)
                    {
                        Call(router, "AdjustFilePitch", "pitch", pitch, "target", target, "octave", octave); int result = Result<int>(router, "AdjustFilePitch");
                        int shifted = pitch + octave * 12;
                        if (result < low[target] || result > high[target] || (result - pitch) % 12 != 0 || (shifted >= low[target] && shifted <= high[target] && result != shifted)) ok = false;
                    }
                    Need(ok, "All 128 pitches preserve class and recorded range: target " + target + ", octave " + octave);
                }
                foreach (int target in new[] { -1, 0, 1, 2, 3, 4, 5, 7, 8 })
                {
                    string original = target < 0 ? "00003a" : "00003" + "abcdefghij"[target + 1];
                    router.SetProgramVariable("fileTrackMap", original); Call(router, "ResolveSelection");
                    Call(router, "WithFileTrackOctave", "track", 3, "octave", 2); string adjusted = Result<string>(router, "WithFileTrackOctave");
                    Call(router, "IsValidFileTrackMap", "map", adjusted); Need(Result<bool>(router, "IsValidFileTrackMap"), "Extended disabled mapping validates: target " + target);
                    router.SetProgramVariable("fileTrackMap", adjusted); Call(router, "ResolveSelection");
                    Need(Octave(3) == 2, "Octave survives extended decode: target " + target);
                    Call(router, "FileRoutingMap", "map", adjusted); Need(Result<string>(router, "FileRoutingMap") == original, "Pitch extension preserves route/mute: target " + target);
                    Call(router, "WithFileTrackOctave", "track", 3, "octave", 0); Need(Result<string>(router, "WithFileTrackOctave") == original, "Zero octave compacts to original legacy mapping: target " + target);
                }
                foreach (string invalid in new[] { "P", "P2", "P100003hA", "P1000037A", "P1000030Z", "P1000030", "P1655350A" })
                { Call(router, "IsValidFileTrackMap", "map", invalid); Need(!Result<bool>(router, "IsValidFileTrackMap"), "Reject malformed extended mapping " + invalid); }
                router.SetProgramVariable("fileTrackMap", ""); Call(router, "ResolveSelection");
                foreach (int octave in new[] { -10, 0, 10 })
                {
                    bool ok=true;
                    for(int pitch=0;pitch<128;pitch++)
                    {
                        Call(router,"AdjustFilePitch","pitch",pitch,"target",0,"octave",octave);int result=Result<int>(router,"AdjustFilePitch");
                        if(result<0 || result>127 || (result-pitch)%12!=0 || (octave==0 && result!=pitch))ok=false;
                    }
                    Need(ok,"Range-off preserves MIDI bounds and pitch class at octave "+octave);
                }
            }
            finally { router.SetProgramVariable("fileTrackMap", saved); Call(router, "ResolveSelection"); }
        }
        static void Pagination()
        {
            var used = (bool[])router.GetProgramVariable("usedFileTracks"); string map = (string)router.GetProgramVariable("fileTrackMap");
            try
            {
                router.SetProgramVariable("usedFileTracks", Enumerable.Repeat(true, 40).ToArray()); Call(ui, "_FileByTrack");
                f.playerUI.fileOctaveUpButtons[15].onClick.Invoke(); Need(Octave(15) == 1, "Last row of first page targets source track 15");
                Call(ui, "_FileNext"); f.playerUI.fileOctaveDownButtons[0].onClick.Invoke(); Need(Octave(16) == -1, "First row of next page targets source track 16");
                Call(ui, "_FileNext"); Need(f.playerUI.fileOctaveUpButtons.Count(b => b.gameObject.activeSelf) == 8, "Partial last page hides unused octave controls");
            }
            finally { router.SetProgramVariable("usedFileTracks", used); Call(facade, "_ApplyFileTrackMap", "map", map); Call(ui, "_FileByTrack"); }
        }
        static void CheckViews()
        {
            int ms = (int)(Position() * 1000); var seq = Back(f.sequencer); var ids = (int[])seq.GetProgramVariable("activeNoteIds");
            int count = (int)seq.GetProgramVariable("activeNoteCount"); int[] up = (int[])song.GetProgramVariable("keyUpMs"); var expected = new int[1024];
            for (int i = 0; i < count; i++) if (up[ids[i]] > ms)
            { Call(router, "TargetForNote", "note", ids[i]); int target = Result<int>(router, "TargetForNote"); if (target >= 0) expected[target * 128 + Pitch(ids[i])]++; }
            Need(((int[])router.GetProgramVariable("keyCounts")).SequenceEqual(expected), "Keyboard counts use transformed pitch without changing note identity");
            var fall = f.instrumentWaterfalls[1]; var backing = Back(fall); var notes = (int[])backing.GetProgramVariable("_slotNote"); var hits = (Vector3[])backing.GetProgramVariable("_pitchHitPositions");
            int tested = 0; bool match = true;
            for (int i = 0; i < notes.Length; i++) if (notes[i] >= 0)
            { tested++; if (Math.Abs(fall.barTransforms[i].localPosition.x - hits[Pitch(notes[i])].x) > .0001f) match = false; }
            Need(tested > 0 && match, "Actual waterfall bars land on effective pitch positions");
        }
        static void Capture()
        {
            var canvas = f.playerUI.GetComponent<Canvas>(); var oldCamera = canvas.worldCamera;
            var host = new GameObject("Track pitch verification camera"); host.hideFlags = HideFlags.HideAndDontSave;
            var camera = host.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 300 * f.playerUI.transform.lossyScale.y;
            camera.nearClipPlane = .01f; camera.farClipPlane = 10; camera.transform.position = f.playerUI.fileMappingPanel.transform.position - f.playerUI.transform.forward * 2;
            camera.transform.rotation = f.playerUI.transform.rotation; camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(1600, 900, 24); var oldTarget = RenderTexture.active; Texture2D image = null;
            try
            {
                camera.targetTexture = target; canvas.worldCamera = camera; Canvas.ForceUpdateCanvases();
                var raycaster = f.playerUI.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                foreach (var selectable in new UnityEngine.UI.Selectable[] { f.playerUI.fileTrackEnabledToggles[0], f.playerUI.fileProgramButtons[0], f.playerUI.fileOctaveDownButtons[0], f.playerUI.fileOctaveUpButtons[0], f.playerUI.fileAutoRangeToggle })
                {
                    var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current); pointer.position = camera.WorldToScreenPoint(selectable.transform.position);
                    var hits = new List<UnityEngine.EventSystems.RaycastResult>(); raycaster.Raycast(pointer, hits);
                    Need(hits.Count > 0 && hits[0].gameObject == selectable.gameObject, "Independent raycast hits " + selectable.name);
                }
                Directory.CreateDirectory("Captures/TrackPitch");
                foreach (var language in new[] { new[] { 1, 0 }, new[] { 2, 1 }, new[] { 2, 2 }, new[] { 3, 3 } })
                {
                    ui.SetProgramVariable("languageSelection", language[0]); ui.SetProgramVariable("_clientLanguage", language[1] == 2 ? "zh-TW" : "en"); Call(ui, "ApplyLanguage");
                    Canvas.ForceUpdateCanvases();
                    foreach(var text in f.playerUI.fileProgramLabels.Concat(f.playerUI.fileProgramValues).Where(t=>t.gameObject.activeInHierarchy))
                    {
                        text.ForceMeshUpdate();Need(text.textInfo.characterInfo.Take(text.textInfo.characterCount).Any(c=>c.isVisible),"Source/target glyphs visible: "+language[1]+"/"+text.transform.parent.name+"/"+text.name);
                    }
                    camera.Render(); RenderTexture.active = target; image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply(); File.WriteAllBytes("Captures/TrackPitch/" + new[] { "en", "zh-Hans", "zh-Hant", "ja" }[language[1]] + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image); image = null;
                    Need(!f.playerUI.fileAutoRangeLabel.text.Contains("file."), "Range label localizes in language " + language[1]);
                }
            }
            finally { canvas.worldCamera = oldCamera; camera.targetTexture = null; RenderTexture.active = oldTarget; target.Release(); UnityEngine.Object.DestroyImmediate(target); if (image != null) UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(host); }
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Directory.CreateDirectory("Logs/TrackPitch");
            File.WriteAllText("Logs/TrackPitch/verification.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { success = error == null, checks = Checks, error = error?.ToString(), scope = "Editor/ClientSim native UI and backing Udon; no real VRChat, remote late join/ownership or listening acceptance", completedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented));
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
            if (error != null) Debug.LogError("Track pitch validation failed: " + error); else Debug.Log("Track pitch validation passed: " + Checks.Count);
        }
    }
}
