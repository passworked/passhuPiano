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
    // Explicit, bounded ClientSim/backing-Udon check; never runs on import or build.
    [InitializeOnLoad]
    public static class MidiTrackSwitchValidation
    {
        const string Key = "MMP.TrackSwitch.Probe";
        static readonly List<string> Checks = new List<string>();
        static MidiPlayerFacade f;
        static UdonBehaviour facade, router, ui, network, clock, song;
        static int stage, track, drumRow;
        static int[] tracks, noteTracks;
        static double started, pausedPosition;
        static MidiTrackSwitchValidation()
        {
            if (!SessionState.GetBool(Key, false)) return;
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        [MenuItem("Tools/Music MIDI Player/Validate Track Switches (Play Mode)")]
        public static void Begin()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Start from the saved main scene outside Play mode.");
            Checks.Clear(); stage = 0; started = EditorApplication.timeSinceStartup;
            SessionState.SetBool(Key, true);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }
        static UdonBehaviour Back(UdonSharpBehaviour b) { return UdonSharpEditorUtility.GetBackingUdonBehaviour(b); }
        static void Need(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); Checks.Add(label); }
        static void Call(UdonBehaviour b, string method, params object[] args)
        {
            var p = ((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram();
            var symbols = p.SymbolTable.GetSymbols();
            for (int i = 0; i < args.Length; i += 2)
            {
                var names = symbols.Where(s => Regex.IsMatch(s, "^__\\d+_" + args[i] + "__param$")).ToArray();
                if (names.Length == 0) throw new MissingMemberException(method + "." + args[i]);
                foreach (string name in names) b.SetProgramVariable(name, args[i + 1]);
            }
            b.SendCustomEvent(p.EntryPoints.GetExportedSymbols().Contains(method) ? method : "__0_" + method);
        }
        static T Result<T>(UdonBehaviour b, string method)
        {
            var symbols = ((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram().SymbolTable.GetSymbols();
            string name = symbols.Where(s => s.EndsWith("_" + method + "__ret", StringComparison.Ordinal)).OrderBy(s => s.Length).First();
            return (T)b.GetProgramVariable(name);
        }
        static bool Loading() { return (bool)facade.GetProgramVariable("isSongLoading"); }
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 55) throw new TimeoutException("Track-switch probe timed out.");
                if (stage == 0)
                {
                    f = UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>(); if (f == null) return;
                    facade = Back(f); if (!(facade.GetProgramVariable("isReady") is bool ready) || !ready || Loading()) return;
                    router = Back(f.instrumentRouter); ui = Back(f.playerUI); network = Back(f.network); clock = Back(f.clock);
                    if (!(bool)network.GetProgramVariable("networkReady")) return;
                    Call(facade, "CommandSelectSong", "id", "408c3f56422358049b5f154a35ad871c");
                    stage++; return;
                }
                if (Loading()) return;
                if (stage == 1)
                {
                    song = Back(f.library.songs.First(s => s.songId == (string)facade.GetProgramVariable("selectedSongId")));
                    noteTracks = (int[])song.GetProgramVariable("trackIndex");
                    var used = (bool[])router.GetProgramVariable("usedFileTracks");
                    tracks = Enumerable.Range(0, used.Length).Where(i => used[i]).ToArray(); track = tracks[0];
                    Need(tracks.Length > 2, "Real song has multiple source tracks");
                    Encoding();
                    Call(ui, "_OpenFileMapping"); Call(ui, "_FileByTrack");
                    Need(f.playerUI.fileTrackEnabledToggles.Count(t => t.gameObject.activeInHierarchy) == tracks.Length, "Only used track rows show switches");
                    int mask = (int)router.GetProgramVariable("enabledMask");
                    f.playerUI.fileTrackEnabledToggles[0].isOn = false;
                    Need((string)router.GetProgramVariable("fileTrackMap") == track.ToString("00000") + "a", "Native toggle disables automatic track");
                    Need((int)router.GetProgramVariable("enabledMask") == mask, "Mute preserves selected instruments");
                    CheckNote(Array.IndexOf(noteTracks, track), -1, "Muted melodic track has no target");
                    Call(router, "TargetForNote", "note", Array.FindIndex(noteTracks, t => t != track));
                    Need(Result<int>(router, "TargetForNote") >= 0, "Another track sharing the instrument stays routed");
                    stage++; return;
                }
                if (stage == 2)
                {
                    Need((string)network.GetProgramVariable("_pendingFileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Pending snapshot retains switch");
                    network.SendCustomEvent("_onPreSerialization");
                    Need((string)network.GetProgramVariable("fileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Public snapshot serializes switch");
                    Need((string)network.GetProgramVariable("_flightFileTrackMap") == (string)router.GetProgramVariable("fileTrackMap"), "Flight snapshot retains switch");
                    f.playerUI.fileProgramButtons[0].onClick.Invoke(); f.playerUI.fileTargetButtons[8].onClick.Invoke();
                    Need((string)router.GetProgramVariable("fileTrackMap") == track.ToString("00000") + "i", "Native picker changes disabled track to music box without enabling it");
                    stage++; return;
                }
                if (stage == 3)
                {
                    f.playerUI.fileTrackEnabledToggles[0].isOn = true;
                    Need((string)router.GetProgramVariable("fileTrackMap") == track.ToString("00000") + "8", "Re-enable retains edited music-box mapping");
                    stage++; return;
                }
                if (stage == 4)
                {
                    Call(facade, "CommandAutoTrackRouting", "enabled", true); stage++; return;
                }
                if (stage == 5)
                {
                    var names = f.library.songs.First(s => s.songId == (string)facade.GetProgramVariable("selectedSongId")).trackNames;
                    int drumTrack = Array.FindIndex(names, n => n == "Drum"); drumRow = Array.IndexOf(tracks, drumTrack);
                    Need(drumRow >= 0, "Real song exposes a drum track");
                    Call(router, "IsFileDrum", "note", Array.IndexOf(noteTracks, drumTrack));
                    Need(Result<bool>(router, "IsFileDrum"), "Automatic drum track enters percussion route");
                    f.playerUI.fileTrackEnabledToggles[drumRow].isOn = false; stage++; return;
                }
                if (stage == 6)
                {
                    int drumTrack = tracks[drumRow], note = Array.IndexOf(noteTracks, drumTrack);
                    Call(router, "IsFileDrum", "note", note); Need(!Result<bool>(router, "IsFileDrum"), "Muted drum track leaves percussion route");
                    CheckNote(note, -1, "Muted drum track cannot fall back to melody");
                    Need(((int[])Back(f.voicePool.drums).GetProgramVariable("_ids")).All(n => noteTracks[n] != drumTrack), "Muted drum track removed from audio index");
                    Need(((int[])Back(f.drumView.lanes).GetProgramVariable("noteIds")).All(n => noteTracks[n] != drumTrack), "Muted drum track removed from waterfall index");
                    Call(facade, "CommandSeek", "progress", .3f); Call(clock, "GetPositionSeconds"); pausedPosition = Result<double>(clock, "GetPositionSeconds");
                    f.playerUI.fileTrackEnabledToggles[0].isOn = false; stage++; return;
                }
                if (stage == 7)
                {
                    Call(clock, "GetState"); Need(Result<int>(clock, "GetState") == 2, "Track change retains paused state");
                    Call(clock, "GetPositionSeconds"); Need(Math.Abs(Result<double>(clock, "GetPositionSeconds") - pausedPosition) < .001, "Track change retains paused position");
                    Call(facade, "CommandPlay"); stage++; return;
                }
                if (stage == 8)
                {
                    var pool = Back(f.voicePool.ensemble); var active = (bool[])pool.GetProgramVariable("active");
                    var live = (bool[])pool.GetProgramVariable("live"); var ids = (int[])pool.GetProgramVariable("identities");
                    Need(Enumerable.Range(0, active.Length).All(i => !active[i] || live[i] || noteTracks[ids[i]] != track), "Playing voices exclude muted track");
                    Call(clock, "GetState"); Need(Result<int>(clock, "GetState") == 1, "Playback continues after paused mute");
                    string map = (string)router.GetProgramVariable("fileTrackMap");
                    Call(Back(f.gate), "_Lock"); Call(facade, "CommandFileTrackEnabled", "track", track, "enabled", true);
                    Need((string)router.GetProgramVariable("fileTrackMap") == map, "Locked gate rejects switch command"); Call(Back(f.gate), "_Unlock");
                    Capture();
                    Call(ui, "_FileByProgram"); Need(f.playerUI.fileTrackEnabledToggles.All(t => !t.gameObject.activeSelf), "Program page hides track switches");
                    Need(f.playerUI.fileProgramButtons.Where(b => b.gameObject.activeSelf).All(b => ((RectTransform)b.transform).sizeDelta.x == 456), "Program page restores visible row widths");
                    Call(ui, "_FileByTrack"); Call(ui, "_ResetFileMapping"); stage++; return;
                }
                Need((string)router.GetProgramVariable("fileTrackMap") == "", "Track reset clears mappings and disabled flags");
                Need((bool)router.GetProgramVariable("autoTrackRouting"), "Track reset preserves automatic routing option");
                Need(f.playerUI.fileTrackEnabledToggles.Where(t => t.gameObject.activeInHierarchy).All(t => t.isOn), "Track reset restores native switches");
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void CheckNote(int note, int target, string label)
        { Call(router, "TargetForNote", "note", note); Need(Result<int>(router, "TargetForNote") == target, label); }
        static void Encoding()
        {
            string saved = (string)router.GetProgramVariable("fileTrackMap");
            try
            {
                foreach (int target in new[] { -1, 0, 1, 2, 3, 4, 5, 7, 8 })
                {
                    string enabled = target < 0 ? "" : "00003" + (target + 1);
                    router.SetProgramVariable("fileTrackMap", enabled); Call(router, "ResolveSelection");
                    Call(router, "WithFileTrackEnabled", "track", 3, "enabled", false);
                    string disabled = Result<string>(router, "WithFileTrackEnabled");
                    Call(router, "IsValidFileTrackMap", "map", disabled); Need(Result<bool>(router, "IsValidFileTrackMap"), "Valid disabled encoding for target " + target);
                    router.SetProgramVariable("fileTrackMap", disabled); Call(router, "ResolveSelection");
                    Call(router, "FileTrackTarget", "track", 3); Need(Result<int>(router, "FileTrackTarget") == target, "Disabled mapping retains target " + target);
                    Call(router, "WithFileTrackEnabled", "track", 3, "enabled", true); Need(Result<string>(router, "WithFileTrackEnabled") == enabled, "Enable roundtrip for target " + target);
                }
                foreach (string invalid in new[] { "000037", "00003h", "000030", "65535a", "00003a00003b", "00004a00003b", "00003z", "0000a", "-0001a" })
                {
                    Call(router, "IsValidFileTrackMap", "map", invalid); Need(!Result<bool>(router, "IsValidFileTrackMap"), "Reject malformed/retired mapping " + invalid);
                }
            }
            finally { router.SetProgramVariable("fileTrackMap", saved); Call(router, "ResolveSelection"); }
        }
        static void Capture()
        {
            var canvas = f.playerUI.GetComponent<Canvas>(); var previousCamera = canvas.worldCamera;
            var host = new GameObject("Track switch verification camera"); host.hideFlags = HideFlags.HideAndDontSave;
            var camera = host.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = 300 * f.playerUI.transform.lossyScale.y;
            camera.nearClipPlane = .01f; camera.farClipPlane = 10;
            camera.transform.position = f.playerUI.fileMappingPanel.transform.position - f.playerUI.transform.forward * 2;
            camera.transform.rotation = f.playerUI.transform.rotation;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.02f, .025f, .04f);
            var target = new RenderTexture(1600, 900, 24); var previous = RenderTexture.active; Texture2D image = null;
            try
            {
                camera.targetTexture = target; canvas.worldCamera = camera; Canvas.ForceUpdateCanvases(); camera.Render();
                var raycaster = f.playerUI.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                for (int row = 0; row < tracks.Length; row++)
                {
                    foreach (var selectable in new UnityEngine.UI.Selectable[] { f.playerUI.fileTrackEnabledToggles[row], f.playerUI.fileProgramButtons[row] })
                    {
                        var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                        pointer.position = camera.WorldToScreenPoint(selectable.transform.position);
                        var hits = new List<UnityEngine.EventSystems.RaycastResult>(); raycaster.Raycast(pointer, hits);
                        Need(hits.Count > 0 && hits[0].gameObject == selectable.gameObject, "Independent raycast hits " + selectable.name);
                    }
                }
                RenderTexture.active = target; image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                Directory.CreateDirectory("Captures/TrackSwitches"); File.WriteAllBytes("Captures/TrackSwitches/DisabledTracks.png", image.EncodeToPNG());
            }
            finally
            {
                canvas.worldCamera = previousCamera; camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); if (image != null) UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(host);
            }
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
            Directory.CreateDirectory("Logs/TrackSwitches");
            File.WriteAllText("Logs/TrackSwitches/verification.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { success = error == null, checks = Checks, error = error?.ToString(), scope = "Editor/ClientSim native UI and backing Udon; no real VRChat, remote ownership/late-join or listening acceptance", completedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented));
            EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
            if (error != null) Debug.LogError("Track-switch validation failed: " + error); else Debug.Log("Track-switch validation passed: " + Checks.Count);
        }
    }
}
