using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.Udon;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>
    /// One explicitly requested Editor Play Mode smoke test of the real backing Udon programs.
    /// SessionState survives the Play Mode domain reload; no proxy method is used as a test substitute.
    /// </summary>
    [InitializeOnLoad]
    public static class MidiPlayModeValidation
    {
        private const string SessionKey = "MusicMidiPlayer.PlayModeValidation.State";
        private const string ActiveKey = "MusicMidiPlayer.PlayModeValidation.Active";
        private const string DemoScene = "Assets/UdonMidiPlayer/Samples/MidiPlayerDemo.unity";
        private const string ReportFile = "Logs/MusicMidiPlayer-playmode-report.json";

        [Serializable]
        private sealed class Check
        {
            public string name;
            public bool success;
            public string message;
        }

        [Serializable]
        private sealed class PreviousScene
        {
            public string path;
            public bool loaded;
            public bool active;
        }

        [Serializable]
        private sealed class PreservedRoot
        {
            public string globalObjectId;
            public string scenePath;
            public string sceneName;
            public int sceneHandle;
            public string rootName;
            public int rootIndex;
            public bool activeSelf;
        }

        [Serializable]
        private sealed class VoiceMappingEvidence
        {
            public string phase;
            public int slot;
            public string sourceName;
            public int sourceInstanceId;
            public int noteId;
            public int midiPitch;
            public int poolSongPitch;
            public int expectedRegion;
            public int slotRegion;
            public int rootPitch;
            public int regionLowPitch;
            public int regionHighPitch;
            public int slotState;
            public int slotGeneration;
            public int poolGeneration;
            public string actualClip;
            public string expectedClip;
            public int actualClipInstanceId;
            public int expectedClipInstanceId;
            public float actualPitchRatio;
            public float expectedPitchRatio;
            public float tuningCents;
            public bool matches;
            public string mismatch;
        }

        [Serializable]
        private sealed class ProbeState
        {
            public string id;
            public string scope = "Editor native-Unity-button to backing-Udon smoke test; no pointer/raycast, multiplayer or captured-audio validation";
            public string startedUtc;
            public string completedUtc;
            public string stage;
            public string targetScenePath;
            public bool success;
            public bool failed;
            public bool finished;
            public string exception;
            public double startedAt;
            public double phaseStartedAt;
            public double songLoadingStartedAt;
            public bool waitingForSong;
            public string firstSong;
            public string secondSong;
            public string secondSongTitle;
            public int secondSongNotes;
            public int secondSongPackedBytes;
            public double secondSongCommandAt;
            public double secondSongReadySeconds;
            public int navigationFrame;
            public int seekFrame;
            public int seekGeneration;
            public double seekTargetPosition;
            public double playPosition;
            public double pausePosition;
            public int pauseActiveNotes;
            public int[] pauseSamples;
            public bool modelMode;
            public bool salamanderMode;
            public string keyboardMode;
            public int bankClipCount;
            public string[] bankClipNames;
            public int nativeButtonDispatchCount;
            public int backingFallbackDispatchCount;
            public Vector3[] keyRestLocalPositions;
            public Quaternion[] keyRestLocalRotations;
            public Vector3[] keyRestWorldPositions;
            public Vector3[] pauseKeyLocalPositions;
            public Quaternion[] pauseKeyLocalRotations;
            public int[] pauseKeyMaterialIds;
            public int[] pauseKeyDownCounts;
            public int trackedKeyMidi;
            public Vector3 trackedKeyWorldDisplacement;
            public List<string> playedClipNames = new List<string>();
            public List<VoiceMappingEvidence> voiceMappingEvidence = new List<VoiceMappingEvidence>();
            public Quaternion[] pedalRestLocalRotations;
            public Vector3[] pedalRestLocalPositions;
            public Quaternion[] pausePedalRotations;
            public bool sawIntermediateKeyPose;
            public bool sawIntermediatePedalPose;
            public int[] originalPedalMs;
            public int[] originalPedalController;
            public int[] originalPedalValue;
            public int[] originalPedalOffsets;
            public int[] pauseParticleCounts;
            public float[] pauseParticleTimes;
            public float[] pauseLightIntensities;
            public string waterfallPreview;
            public float environmentLightPixelDelta;
            public Color[] firstSongPalette;
            public Color[] secondSongPalette;
            public string firstSongMidiHash;
            public string secondSongMidiHash;
            public int[] paletteMaterialIds;
            public bool sawUpwardDust;
            public bool sawMusicSymbols;
            public PreviousScene[] previousScenes;
            public bool preserveScenes;
            public bool demoOpenedHere;
            public string originalActiveScenePath;
            public string originalActiveSceneName;
            public int originalActiveSceneHandle;
            public PreservedRoot[] preservedRoots;
            public List<Check> checks = new List<Check>();
        }

        private static ProbeState _probe;
        private static UdonBehaviour _facade;
        private static UdonBehaviour _clock;
        private static UdonBehaviour _sequencer;
        private static UdonBehaviour _network;
        private static UdonBehaviour _secondSongButton;
        private static UdonBehaviour _keyboard;
        private static UdonBehaviour _pedals;
        private static UdonBehaviour _ui;
        private static AudioSource[] _voices;
        private static Transform[] _keys;
        private static Renderer[] _keyRenderers;
        private static Material[] _keyRestMaterials;
        private static Vector3[] _keyPressOffsets;
        private static Button _playButton;
        private static Button _pauseButton;
        private static Button _stopButton;
        private static Button _secondNativeButton;
        private static bool _finishing;

        static MidiPlayModeValidation()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
            Application.logMessageReceived += LogMessageReceived;
            if (SessionState.GetBool(ActiveKey, false))
            {
                string json = SessionState.GetString(SessionKey, "");
                if (!string.IsNullOrEmpty(json)) _probe = JsonUtility.FromJson<ProbeState>(json);
                if (_probe == null) SessionState.SetBool(ActiveKey, false);
            }
        }

        public static string StartProbe()
        {
            return StartProbe(Guid.NewGuid().ToString("N"));
        }

        public static string StartProbe(string requestId)
        {
            return StartProbeInternal(requestId, false);
        }

        public static string StartProbePreservingScenes()
        {
            return StartProbeInternal(Guid.NewGuid().ToString("N"), true);
        }

        public static string StartProbePreservingScenes(string requestId)
        {
            return StartProbeInternal(requestId, true);
        }

        public static string StartCurrentSceneProbe()
        {
            Scene current = SceneManager.GetActiveScene();
            if (!current.IsValid() || !current.isLoaded || string.IsNullOrEmpty(current.path))
                throw new InvalidOperationException("A loaded, saved current scene is required; no scene will be opened or created.");
            return StartProbeInternal(Guid.NewGuid().ToString("N"), true, current.path);
        }

        private static string StartProbeInternal(string requestId, bool preserveScenes, string targetScenePath = DemoScene)
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException("A MIDI Play Mode probe is already active. Read GetStatus().");

            _probe = new ProbeState
            {
                id = string.IsNullOrEmpty(requestId) ? Guid.NewGuid().ToString("N") : requestId,
                startedUtc = DateTime.UtcNow.ToString("o"),
                startedAt = EditorApplication.timeSinceStartup,
                phaseStartedAt = EditorApplication.timeSinceStartup,
                preserveScenes = preserveScenes,
                targetScenePath = targetScenePath,
                stage = "Preflight"
            };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Probe requires Edit Mode; the existing user Play Mode session was left unchanged.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new InvalidOperationException("Wait for script compilation and asset imports before starting the probe.");
                if (!File.Exists(ProjectPath(_probe.targetScenePath))) throw new FileNotFoundException("The target scene must already exist.", _probe.targetScenePath);
                SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
                _probe.previousScenes = new PreviousScene[setup.Length];
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    if (!preserveScenes && current.isDirty)
                        throw new InvalidOperationException("Unsaved scene was preserved; probe did not open another scene: " + current.name);
                }
                for (int i = 0; i < setup.Length; i++)
                    _probe.previousScenes[i] = new PreviousScene { path = setup[i].path, loaded = setup[i].isLoaded, active = setup[i].isActive };

                if (preserveScenes) CapturePreservedScenes();
                AddCheck("preflight", true, preserveScenes
                    ? "Existing scenes and unsaved changes are retained in memory. Non-demo root activation and the active scene will be temporarily isolated and restored; no scene is saved or reloaded."
                    : "All currently open scenes are clean; their saved scene setup is retained.");
                SessionState.SetBool(ActiveKey, true);
                SetStage("EnteringPlay");
                if (preserveScenes) PreparePreservedScenes();
                else EditorSceneManager.OpenScene(_probe.targetScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                if (SessionState.GetBool(ActiveKey, false)) Fail(exception);
                else
                {
                    _probe.failed = true;
                    _probe.exception = exception.ToString();
                    AddCheck("preflight", false, exception.Message);
                    Complete(false);
                }
            }
            return _probe.id;
        }

        private static void CapturePreservedScenes()
        {
            Scene active = SceneManager.GetActiveScene();
            _probe.originalActiveScenePath = active.path;
            _probe.originalActiveSceneName = active.name;
            _probe.originalActiveSceneHandle = active.handle;
            var roots = new List<PreservedRoot>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path == _probe.targetScenePath) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    string id = "";
                    try { id = GlobalObjectId.GetGlobalObjectIdSlow(root).ToString(); }
                    catch (Exception) { /* Unsaved objects also have scene/name/index fallback identity. */ }
                    roots.Add(new PreservedRoot
                    {
                        globalObjectId = id,
                        scenePath = scene.path,
                        sceneName = scene.name,
                        sceneHandle = scene.handle,
                        rootName = root.name,
                        rootIndex = root.transform.GetSiblingIndex(),
                        activeSelf = root.activeSelf
                    });
                }
            }
            _probe.preservedRoots = roots.ToArray();
            Save();
        }

        private static void PreparePreservedScenes()
        {
            Scene demo = SceneManager.GetSceneByPath(_probe.targetScenePath);
            _probe.demoOpenedHere = !demo.IsValid() || !demo.isLoaded;
            Save();
            if (_probe.demoOpenedHere) demo = EditorSceneManager.OpenScene(_probe.targetScenePath, OpenSceneMode.Additive);
            foreach (PreservedRoot record in _probe.preservedRoots)
            {
                GameObject root = ResolvePreservedRoot(record);
                if (root == null) throw new InvalidOperationException("Cannot isolate recorded non-demo root: " + record.sceneName + "/" + record.rootName);
                if (root.activeSelf) root.SetActive(false);
            }
            if (SceneManager.GetActiveScene() != demo && !SceneManager.SetActiveScene(demo))
                throw new InvalidOperationException("Cannot activate the existing target scene.");
            AddCheck("preserve-scene-isolation", true, "Temporarily disabled " + _probe.preservedRoots.Length +
                " recorded non-demo roots; demoOpenedHere=" + _probe.demoOpenedHere + ". Existing dirty scenes were not saved, closed or reloaded.");
        }

        private static Scene FindPreservedScene(string path, string name, int handle)
        {
            if (!string.IsNullOrEmpty(path))
            {
                Scene byPath = SceneManager.GetSceneByPath(path);
                if (byPath.IsValid() && byPath.isLoaded) return byPath;
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (scene.handle == handle && (string.IsNullOrEmpty(path) || scene.path == path)) return scene;
                if (string.IsNullOrEmpty(path) && scene.name == name) return scene;
            }
            return default(Scene);
        }

        private static GameObject ResolvePreservedRoot(PreservedRoot record)
        {
            if (!string.IsNullOrEmpty(record.globalObjectId))
            {
                GlobalObjectId id;
                if (GlobalObjectId.TryParse(record.globalObjectId, out id))
                {
                    GameObject byId = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                    if (byId != null && byId.transform.parent == null && byId.scene.isLoaded && byId.name == record.rootName &&
                        (string.IsNullOrEmpty(record.scenePath) ? byId.scene.name == record.sceneName : byId.scene.path == record.scenePath)) return byId;
                }
            }
            Scene scene = FindPreservedScene(record.scenePath, record.sceneName, record.sceneHandle);
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
                if (root.transform.GetSiblingIndex() == record.rootIndex && root.name == record.rootName) return root;
            GameObject named = null;
            foreach (GameObject root in roots)
            {
                if (root.name != record.rootName) continue;
                if (named != null) return null;
                named = root;
            }
            return named;
        }

        private static void RestorePreservedScenes()
        {
            var missing = new List<string>();
            if (_probe.preservedRoots != null)
            {
                foreach (PreservedRoot record in _probe.preservedRoots)
                {
                    GameObject root = ResolvePreservedRoot(record);
                    if (root == null) { missing.Add(record.sceneName + "/" + record.rootName); continue; }
                    if (root.activeSelf != record.activeSelf) root.SetActive(record.activeSelf);
                }
            }
            Scene active = FindPreservedScene(_probe.originalActiveScenePath, _probe.originalActiveSceneName, _probe.originalActiveSceneHandle);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            else missing.Add("original active scene: " + _probe.originalActiveSceneName);
            if (_probe.demoOpenedHere)
            {
                Scene demo = SceneManager.GetSceneByPath(_probe.targetScenePath);
                if (demo.IsValid() && demo.isLoaded && !EditorSceneManager.CloseScene(demo, true))
                    missing.Add("newly additively opened demo could not be closed");
            }
            if (missing.Count > 0)
                throw new InvalidOperationException("Preserving-scene restore could not resolve: " + string.Join(", ", missing.ToArray()) + ". No original scene was reloaded or saved.");
            AddCheck("restore-scenes", true, "Restored every recorded non-demo root activeSelf and the original active scene. Original scene contents/dirty changes were not saved or reloaded; only a demo opened by this probe was closed.");
        }

        public static string GetStatus()
        {
            if (SessionState.GetBool(ActiveKey, false)) return SessionState.GetString(SessionKey, "{}");
            string file = ProjectPath(ReportFile);
            return File.Exists(file) ? File.ReadAllText(file) : "No Play Mode probe report exists.";
        }

        private static void PlayModeStateChanged(PlayModeStateChange change)
        {
            if (!IsActive()) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                _facade = null;
                _clock = null;
                _sequencer = null;
                _network = null;
                _voices = null;
                _secondSongButton = null;
                _keyboard = null;
                _pedals = null;
                _ui = null;
                _keys = null;
                _keyRenderers = null;
                _keyRestMaterials = null;
                _keyPressOffsets = null;
                _playButton = null;
                _pauseButton = null;
                _stopButton = null;
                _secondNativeButton = null;
                if (!_probe.failed) SetStage("WaitingReady");
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (_probe.stage != "ExitingPlay" && !_probe.failed)
                {
                    _probe.failed = true;
                    _probe.exception = "Play Mode exited before the requested probe completed.";
                    AddCheck("play-mode-lifecycle", false, _probe.exception);
                }
                SetStage("RestoringScene");
            }
        }

        private static void Update()
        {
            if (!IsActive() || _finishing) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (_probe.stage == "RestoringScene" || (_probe.stage == "ExitingPlay" && !EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    RestoreAndComplete();
                    return;
                }
                if (_probe.stage == "EnteringPlay")
                {
                    if (EditorApplication.isPlaying) SetStage("WaitingReady");
                    else if (Elapsed() > 30) throw new TimeoutException("Play Mode did not start within 30 seconds.");
                    return;
                }
                if (!EditorApplication.isPlaying)
                {
                    if (EditorApplication.timeSinceStartup - _probe.phaseStartedAt > 15)
                        throw new TimeoutException("Play Mode is unavailable during probe stage " + _probe.stage);
                    return;
                }
                if (EditorApplication.timeSinceStartup - _probe.startedAt > 240)
                    throw new TimeoutException("Overall MIDI Play Mode probe timed out.");

                if (_probe.stage == "WaitingReady")
                {
                    if (!TryResolveFacade() || !_facade.IsInitialized)
                    {
                        if (Elapsed() > 15) throw new TimeoutException("Backing Facade UdonBehaviour did not initialize within 15 seconds.");
                        return;
                    }
                    if (!ReadBool(_facade, "isReady") || ReadBool(_facade, "isSongLoading"))
                    {
                        if (Elapsed() > 90)
                            throw new TimeoutException("Facade did not finish its initial song decode: " + ReadOptionalString(_facade, "lastError"));
                        return;
                    }
                    ResolveRuntimeReferences();
                    if (!ReadBool(_network, "networkReady"))
                    {
                        if (Elapsed() > 15)
                            throw new TimeoutException("Facade isReady is true but backing Network.networkReady remained false: " + ReadOptionalString(_network, "lastError"));
                        return;
                    }
                    _probe.firstSong = ReadString(_facade, "selectedSongId");
                    if (_playButton != null && !_playButton.interactable)
                    {
                        if (Elapsed() > 15) throw new TimeoutException("Runtime is ready but the baked native Play button remained non-interactable.");
                        return;
                    }
                    ValidateBindingsAndCaptureRest();
                    RequireDecodedSong("initial-song-decoded");
                    AddCheck("runtime-ready", true, "isReady was read from the initialized backing Udon heap; selectedSongId=" + _probe.firstSong);
                    AddCheck("backing-programs", true, "Native buttons invoke their baked backing-Udon SendCustomEvent callbacks; state, clock, key bindings and note counts are actual VM heap reads. Legacy missing-button fallback is counted separately.");
                    InvokeControl(_playButton, _facade, "CommandPlay", "play");
                    SetStage("Playing");
                    return;
                }

                ResolveRuntimeReferences();
                if (ReadBool(_facade, "isSongLoading"))
                {
                    if (!_probe.waitingForSong)
                    {
                        _probe.waitingForSong = true;
                        _probe.songLoadingStartedAt = EditorApplication.timeSinceStartup;
                        Save();
                    }
                    if (EditorApplication.timeSinceStartup - _probe.songLoadingStartedAt > 90)
                        throw new TimeoutException("Song decode did not finish during " + _probe.stage + ": " + ReadOptionalString(_facade, "lastError"));
                    return;
                }
                if (!ReadBool(_facade, "isReady"))
                    throw new InvalidOperationException("Facade became unavailable during " + _probe.stage + ": " + ReadOptionalString(_facade, "lastError"));
                if (_probe.waitingForSong)
                {
                    _probe.waitingForSong = false;
                    if (_probe.stage == "SelectingSecondSong")
                        _probe.secondSongReadySeconds = EditorApplication.timeSinceStartup - _probe.secondSongCommandAt;
                    _probe.phaseStartedAt = EditorApplication.timeSinceStartup;
                    RequireDecodedSong("song-decoded-" + _probe.stage);
                    Save();
                }
                ObserveKeyMotion();
                if (_probe.stage == "Playing" && Elapsed() >= 0.6)
                {
                    RequireState(1);
                    if (!HasPressedKey())
                    {
                        if (Elapsed() > 2.5) throw new InvalidOperationException("No physically held key was observed within the initial Play window; keyboard press/pause checks cannot be substantiated.");
                        return;
                    }
                    UdonBehaviour liveEffects = WaterfallEffects();
                    if (ReadInt(liveEffects, "liveParticleCount") == 0 || (ReadInt(liveEffects, "maxActiveLights") > 0 && ReadInt(liveEffects, "activeLightCount") == 0))
                    {
                        if (Elapsed() > 4) throw new InvalidOperationException("No recent native particle hit was observed after the Editor startup frames.");
                        return;
                    }
                    _probe.playPosition = ReadDouble(_clock, "_position");
                    int notes = ReadInt(_sequencer, "activeNoteCount");
                    int playing = PlayingVoices();
                    int samples = MaximumSamplePosition();
                    Require(_probe.playPosition >= 0.25 && notes > 0 && playing > 0 && samples > 0,
                        "play", "position=" + _probe.playPosition + ", activeNotes=" + notes + ", isPlayingVoices=" + playing + ", maxTimeSamples=" + samples);
                    RecordPlayingClips("play-clips");
                    CheckPressedKeys("play-keyboard");
                    CheckPedals("play-pedals", false);
                    CheckPaletteAndWaterfall();
                    CheckWaterfallEffects(false);
                    InvokeControl(_pauseButton, _facade, "CommandPause", "pause");
                    SetStage("PauseSettling");
                }
                else if (_probe.stage == "PauseSettling" && Elapsed() >= 0.3)
                {
                    RequireState(2);
                    _probe.pausePosition = ReadDouble(_clock, "_position");
                    _probe.pauseActiveNotes = ReadInt(_sequencer, "activeNoteCount");
                    _probe.pauseSamples = SamplePositions();
                    Require(PlayingVoices() == 0, "pause-audio", "AudioSource.isPlaying count=" + PlayingVoices() + " after release of old voice slots.");
                    CheckPressedKeys("pause-keyboard");
                    CapturePausedKeyboard();
                    CaptureEffectsSnapshot();
                    SetStage("Paused");
                }
                else if (_probe.stage == "Paused" && Elapsed() >= 0.3)
                {
                    RequireState(2);
                    double current = ReadDouble(_clock, "_position");
                    int notes = ReadInt(_sequencer, "activeNoteCount");
                    Require(Math.Abs(current - _probe.pausePosition) <= 0.002 && notes == _probe.pauseActiveNotes &&
                        PlayingVoices() == 0 && SamplesEqual(_probe.pauseSamples, SamplePositions()),
                        "pause-freeze", "position before/after=" + _probe.pausePosition + "/" + current + ", activeNotes=" + notes + "; paused AudioSource sample positions did not advance.");
                    CheckPausedKeyboardFrozen();
                    CheckEffectsFrozen();
                    InvokeControl(_playButton, _facade, "CommandPlay", "resume");
                    SetStage("Resuming");
                }
                else if (_probe.stage == "Resuming" && Elapsed() >= 0.4)
                {
                    RequireState(1);
                    double current = ReadDouble(_clock, "_position");
                    Require(current > _probe.pausePosition + 0.15 && PlayingVoices() > 0 && MaximumSamplePosition() > 0,
                        "resume", "position=" + current + ", pausedPosition=" + _probe.pausePosition + ", isPlayingVoices=" + PlayingVoices());
                    RecordPlayingClips("resume-clips");
                    InvokeControl(_pauseButton, _facade, "CommandPause", "pause-visual-preview");
                    SetStage("VisualShowcasePaused");
                }
                else if (_probe.stage == "VisualShowcasePaused" && Elapsed() >= 0.15)
                {
                    RequireState(2);
                    CheckWaterfallEffects(false);
                    CaptureWaterfallPreview();
                    OpenAllSongsThroughNativeButton();
                    _probe.navigationFrame = Time.frameCount;
                    SetStage("OpeningAllSongs");
                }
                else if (_probe.stage == "OpeningAllSongs")
                {
                    if (Time.frameCount <= _probe.navigationFrame) return;
                    if (ReadInt(_ui, "_openFolder") != -2 || !_secondNativeButton.gameObject.activeInHierarchy || !_secondNativeButton.interactable)
                    {
                        if (Elapsed() > 5) throw new TimeoutException("The native All Songs navigation did not expose the largest song button.");
                        return;
                    }
                    Require(true, "all-songs-navigation", "The native All Songs Button.onClick opened the full catalog before the later song-button click.");
                    _probe.secondSongCommandAt = EditorApplication.timeSinceStartup;
                    InvokeControl(_secondNativeButton, _secondSongButton, "_OnClick", "select-second-song");
                    SetStage("SelectingSecondSong");
                }
                else if (_probe.stage == "SelectingSecondSong" && Elapsed() >= 0.25)
                {
                    if (_probe.secondSongReadySeconds <= 0)
                        _probe.secondSongReadySeconds = EditorApplication.timeSinceStartup - _probe.secondSongCommandAt;
                    RequireDecodedSong("second-song-decoded");
                    AddCheck("largest-song-load-time", true, "Selected " + _probe.secondSongTitle + "; notes=" + _probe.secondSongNotes +
                        ", packedBytes=" + _probe.secondSongPackedBytes + ", native command to observed Ready seconds=" + _probe.secondSongReadySeconds.ToString("F3") + ".");
                    RequireState(0);
                    string selected = ReadString(_facade, "selectedSongId");
                    Require(selected == _probe.secondSong && selected != _probe.firstSong &&
                        ReadDouble(_clock, "_position") <= 0.002 && ReadInt(_sequencer, "activeNoteCount") == 0 && PlayingVoices() == 0,
                        "select-second-song", "second button selected " + selected + "; Clock is Stopped/0, note count and actual voices are clear.");
                    CheckRestoredKeys("select-keyboard-rest");
                    CheckSongTonePalette();
                    InvokeControl(_playButton, _facade, "CommandPlay", "play-second-song");
                    SetStage("PlayingSecondSong");
                }
                else if (_probe.stage == "PlayingSecondSong" && Elapsed() >= 0.4)
                {
                    RequireState(1);
                    Require(ReadDouble(_clock, "_position") >= 0.15 && PlayingVoices() > 0,
                        "play-second-song", "Second song started through the baked control binding; voices=" + PlayingVoices());
                    RecordPlayingClips("second-song-clips");
                    InvokeControl(_stopButton, _facade, "CommandStop", "stop");
                    SetStage("Stopping");
                }
                else if (_probe.stage == "Stopping" && Elapsed() >= 0.3)
                {
                    RequireState(0);
                    Require(ReadDouble(_clock, "_position") <= 0.002 && ReadInt(_sequencer, "activeNoteCount") == 0 &&
                        PlayingVoices() == 0 && ReadString(_facade, "selectedSongId") == _probe.secondSong,
                        "stop", "Stopped/0 retains the second song, with zero active logical notes and zero playing AudioSources.");
                    CheckRestoredKeys("stop-keyboard-rest");
                    CheckPedals("stop-pedals-rest", true);
                    CheckWaterfallEffects(true);
                    Require(_probe.sawUpwardDust && _probe.sawMusicSymbols, "two-layer-upward-effects",
                        "Observed native dust particles above their emitter height and live musical-symbol mesh particles.");
                    Require(_probe.sawIntermediateKeyPose, "keyboard-smooth-motion", "Observed an actual native key rotation strictly between rest and fully pressed while music time advanced.");
                    if (_probe.modelMode || _probe.salamanderMode)
                        Require(_probe.backingFallbackDispatchCount == 0 && _probe.nativeButtonDispatchCount >= 6,
                            "native-control-path", "Native Button.onClick dispatches=" + _probe.nativeButtonDispatchCount + "; backing-only fallbacks=" + _probe.backingFallbackDispatchCount);
                    BeginNativeSeekCheck();
                    SetStage("SeekingLargestSong");
                }
                else if (_probe.stage == "SeekingLargestSong")
                {
                    if (Time.frameCount <= _probe.seekFrame) return;
                    if (ReadInt(_clock, "_state") != 2 || ReadInt(_facade, "applyGeneration") <= _probe.seekGeneration)
                    {
                        if (Elapsed() > 5) throw new TimeoutException("Native Slider.onValueChanged did not commit the paused seek.");
                        return;
                    }
                    CheckNativeSeekResult();
                    InvokeControl(_stopButton, _facade, "CommandStop", "stop-after-seek");
                    SetStage("SeekStopping");
                }
                else if (_probe.stage == "SeekStopping" && Elapsed() >= 0.3)
                {
                    RequireState(0);
                    Require(ReadDouble(_clock, "_position") <= 0.002 && ReadInt(_sequencer, "activeNoteCount") == 0 && PlayingVoices() == 0,
                        "stop-after-seek", "The native Stop button returned the sought largest song to Stopped/0 with no logical notes or playing voices.");
                    BeginThreePedalCheck();
                    SetStage("ThreePedalsPressed");
                }
                else if (_probe.stage == "ThreePedalsPressed" && Elapsed() >= 0.35)
                {
                    CheckPedals("three-pedals-pressed", false, true);
                    SetStage("ThreePedalsReleasing");
                }
                else if (_probe.stage == "ThreePedalsReleasing")
                {
                    ObservePedalMotion();
                    if (ReadDouble(_clock, "_position") < 1.15) return;
                    Require(_probe.sawIntermediatePedalPose, "three-pedals-smooth-release", "All three CC lanes were injected into runtime-only song arrays; an actual pedal rotation between rest and full press was observed during release. Source MIDI/assets were unchanged.");
                    CheckPedals("three-pedals-released", true);
                    InvokeControl(_stopButton, _facade, "CommandStop", "stop-pedal-probe");
                    RestorePedalTimeline();
                    UdonBehaviour firstButton = FindSongButton(_probe.firstSong);
                    InvokeControl(ReadOptionalButton(firstButton, "button"), firstButton, "_OnClick", "return-first-palette");
                    SetStage("PaletteReturning");
                }
                else if (_probe.stage == "PaletteReturning" && Elapsed() >= 0.15)
                {
                    RequireDecodedSong("return-song-decoded");
                    RequireState(0);
                    Require(ReadString(_facade, "selectedSongId") == _probe.firstSong, "palette-return-song", "Returned to the initial MIDI through its native song button.");
                    CheckSongTonePalette();
                    SetStage("ExitingPlay");
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static bool TryResolveFacade()
        {
            if (_facade != null) return true;
            MidiPlayerFacade[] proxies = UnityEngine.Object.FindObjectsOfType<MidiPlayerFacade>(true);
            foreach (MidiPlayerFacade proxy in proxies)
            {
                if (proxy.gameObject.scene.path != _probe.targetScenePath) continue;
                _facade = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                if (_facade != null) return true;
            }
            return false;
        }

        private static void RequireDecodedSong(string check)
        {
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            int[] starts = ReadRequired(song, "startMs") as int[];
            Require(ReadBool(song, "isDecoded") && starts != null && starts.Length == ReadInt(song, "noteCount")
                && ReadString(song, "songId") == ReadString(_facade, "selectedSongId"), check,
                "The selected song completed its framed decode and is bound to the real Sequencer Udon heap.");
            CheckOnlyCurrentSongDecoded(check + "-single-cache");
        }

        private static void CheckOnlyCurrentSongDecoded(string check)
        {
            Array songs = ReadRequired(ReadBehaviour(_facade, "library"), "songs") as Array;
            if (songs == null) throw new InvalidOperationException("The real Library.songs VM array is unavailable.");
            UdonBehaviour selected = ReadBehaviour(_sequencer, "song");
            int decoded = 0;
            var problems = new List<string>();
            foreach (object item in songs)
            {
                UdonBehaviour song = AsBacking(item);
                int[] starts = ReadRequired(song, "startMs") as int[];
                bool ready = ReadBool(song, "isDecoded");
                if (ready) decoded++;
                if (song == selected)
                {
                    if (!ready || starts == null || starts.Length != ReadInt(song, "noteCount")) problems.Add("selected song is not decoded");
                }
                else if (ready || starts == null || starts.Length != 0)
                    problems.Add(ReadString(song, "songId") + " retains decoded starts");
            }
            Require(decoded == 1 && problems.Count == 0, check,
                "Actual VM descriptors=" + songs.Length + "; decoded songs=" + decoded + "; every non-current startMs length is zero. " + string.Join("; ", problems.ToArray()));
        }

        private static void OpenAllSongsThroughNativeButton()
        {
            if (ReadInt(_ui, "_openFolder") == -2) return;
            RectTransform row = ReadRequired(_ui, "allSongsRow") as RectTransform;
            Button button = row == null ? null : row.GetComponent<Button>();
            if (button == null) throw new InvalidOperationException("The All Songs row has no native Button; no direct Udon navigation bypass was used.");
            ValidateNativeBinding(button, "All songs");
            InvokeControl(button, _ui, "_OnAllSongsPressed", "open-all-songs");
        }

        private static UdonBehaviour FindSongButton(string id)
        {
            Array buttons = ReadRequired(_ui, "songButtons") as Array;
            if (buttons != null)
                foreach (object item in buttons)
                {
                    UdonBehaviour button = AsBacking(item);
                    if (button != null && ReadString(button, "songId") == id) return button;
                }
            throw new InvalidOperationException("No real song-button VM binding exists for " + id);
        }

        private static void BeginNativeSeekCheck()
        {
            Slider slider = ReadRequired(_ui, "progressSlider") as Slider;
            if (slider == null || !slider.gameObject.activeInHierarchy || !slider.enabled || !slider.interactable)
                throw new InvalidOperationException("The native progress Slider is unavailable; no direct seek bypass was used.");
            bool backingListener = false;
            for (int i = 0; i < slider.onValueChanged.GetPersistentEventCount(); i++)
                if (slider.onValueChanged.GetPersistentTarget(i) is UdonBehaviour && slider.onValueChanged.GetPersistentMethodName(i) == "SendCustomEvent" &&
                    slider.onValueChanged.GetPersistentListenerState(i) != UnityEngine.Events.UnityEventCallState.Off) backingListener = true;
            Require(backingListener, "native-progress-binding", "Native Slider.onValueChanged has its active persistent backing-Udon listener.");
            _probe.seekFrame = Time.frameCount;
            _probe.seekGeneration = ReadInt(_facade, "applyGeneration");
            _probe.seekTargetPosition = ReadDouble(_clock, "_end") * 0.5;
            slider.normalizedValue = 0.5f; // Native property dispatches onValueChanged and the existing delayed commit.
            AddCheck("native-progress-change", true, "Changed the native progress Slider to 50%; its existing onValueChanged path must commit on a later frame.");
        }

        private static void CheckNativeSeekResult()
        {
            RequireState(2);
            double position = ReadDouble(_clock, "_position");
            Require(Math.Abs(position - _probe.seekTargetPosition) <= 0.005 && PlayingVoices() == 0,
                "native-progress-seek", "Paused seek position=" + position + "; expected=" + _probe.seekTargetPosition + "; playing voices=" + PlayingVoices());
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            int[] starts = (int[])ReadRequired(song, "startMs"), ups = (int[])ReadRequired(song, "keyUpMs"), releases = (int[])ReadRequired(song, "releaseMs"), pitches = (int[])ReadRequired(song, "pitch");
            int[] actualIds = (int[])ReadRequired(_sequencer, "activeNoteIds"), actualKeys = KeyDownCounts();
            var expectedIds = new HashSet<int>();
            int[] expectedKeys = new int[88];
            int ms = (int)(position * 1000.0);
            for (int i = 0; i < starts.Length; i++)
            {
                if (starts[i] > ms) continue;
                if (releases[i] > ms) expectedIds.Add(i);
                int key = pitches[i] - 21;
                if (ups[i] > ms && key >= 0 && key < 88) expectedKeys[key]++;
            }
            int actualCount = ReadInt(_sequencer, "activeNoteCount");
            bool matches = actualCount == expectedIds.Count;
            for (int i = 0; matches && i < actualCount; i++) matches = expectedIds.Remove(actualIds[i]);
            for (int key = 0; matches && key < 88; key++) matches = expectedKeys[key] == actualKeys[key];
            Require(matches && expectedIds.Count == 0, "seek-note-and-key-state", "Actual VM active-note IDs and all 88 key counts match the decoded start/key-up/release intervals at " + ms + " ms.");
            RequireDecodedSong("seek-single-song");
        }

        private static void ResolveRuntimeReferences()
        {
            if (!TryResolveFacade()) throw new InvalidOperationException("Demo Facade backing UdonBehaviour is missing.");
            if (_clock == null) _clock = ReadBehaviour(_facade, "clock");
            if (_sequencer == null) _sequencer = ReadBehaviour(_facade, "sequencer");
            if (_network == null) _network = ReadBehaviour(_facade, "network");
            if (_keyboard == null) _keyboard = ReadBehaviour(_facade, "keyboard");
            if (_pedals == null) _pedals = ReadBehaviour(_facade, "pedals");
            if (_ui == null) _ui = ReadBehaviour(_facade, "playerUI");
            if (_keys == null) _keys = ReadRequired(_keyboard, "keyTransforms") as Transform[];
            if (_keyRenderers == null) _keyRenderers = ReadRequired(_keyboard, "keyRenderers") as Renderer[];
            if (_keyRestMaterials == null) _keyRestMaterials = ReadRequired(_keyboard, "keyRestMaterials") as Material[];
            if (_keyPressOffsets == null) _keyPressOffsets = ReadRequired(_keyboard, "keyPressOffsets") as Vector3[];
            if (_playButton == null) _playButton = ReadOptionalButton(_ui, "playButton");
            if (_pauseButton == null) _pauseButton = ReadOptionalButton(_ui, "pauseButton");
            if (_stopButton == null) _stopButton = ReadOptionalButton(_ui, "stopButton");
            if (_voices == null)
            {
                UdonBehaviour audio = ReadBehaviour(_facade, "voicePool");
                _voices = ReadRequired(audio, "voiceSources") as AudioSource[];
                if (_voices == null || _voices.Length == 0) throw new InvalidOperationException("Runtime voiceSources is not a nonempty AudioSource[] in the Udon heap.");
            }
            if (_secondSongButton == null)
            {
                Array songs = ReadRequired(ReadBehaviour(_facade, "library"), "songs") as Array;
                if (songs == null || songs.Length < 2) throw new InvalidOperationException("Demo Library must have at least two runtime songs.");
                string current = ReadString(_facade, "selectedSongId");
                foreach (object item in songs)
                {
                    UdonBehaviour song = AsBacking(item);
                    string id = ReadString(song, "songId");
                    int count = ReadInt(song, "noteCount");
                    if (id == current || count <= _probe.secondSongNotes) continue;
                    _probe.secondSong = id;
                    _probe.secondSongTitle = ReadString(song, "title");
                    _probe.secondSongNotes = count;
                    _probe.secondSongPackedBytes = ReadInt(song, "packedDataBytes");
                }
                _secondSongButton = FindSongButton(_probe.secondSong);
            }
            if (_secondNativeButton == null) _secondNativeButton = ReadOptionalButton(_secondSongButton, "button");
        }

        private static void ValidateBindingsAndCaptureRest()
        {
            RequireState(0);
            Require(_keys != null && _keys.Length == 88 && _keyRenderers != null && _keyRenderers.Length == 88 &&
                _keyRestMaterials != null && _keyRestMaterials.Length == 88 && _keyPressOffsets != null && _keyPressOffsets.Length == 88,
                "keyboard-binding-arrays", "Actual Udon keyTransforms/keyRenderers/keyRestMaterials/keyPressOffsets must each contain 88 entries.");

            _probe.modelMode = HasModelAncestor(_keys[0]) || (_keys[0] != null && KeyNameMatches(_keys[0].name, 21, true));
            _probe.keyboardMode = _probe.modelMode ? "model: padded MIDI Key_021 through Key_108" : "standalone: legacy unpadded key names accepted";
            _probe.keyRestLocalPositions = new Vector3[88];
            _probe.keyRestLocalRotations = new Quaternion[88];
            _probe.keyRestWorldPositions = new Vector3[88];
            var unique = new HashSet<Transform>();
            float minimumDepth = float.MaxValue;
            float maximumDepth = 0;
            for (int i = 0; i < 88; i++)
            {
                Transform key = _keys[i];
                if (key == null || !unique.Add(key) || _keyRenderers[i] == null || _keyRestMaterials[i] == null)
                    throw new InvalidOperationException("Missing/duplicate key transform, renderer or rest material at MIDI " + (i + 21));
                if (!KeyNameMatches(key.name, i + 21, _probe.modelMode))
                    throw new InvalidOperationException("Key MIDI mapping/name differs at " + (i + 21) + ": " + key.name + "; mode=" + _probe.keyboardMode);
                if (_keyRenderers[i].sharedMaterial != _keyRestMaterials[i])
                    throw new InvalidOperationException("Stopped key material does not match its baked rest material: " + key.name);
                _probe.keyRestLocalPositions[i] = key.localPosition;
                _probe.keyRestLocalRotations[i] = key.localRotation;
                _probe.keyRestWorldPositions[i] = key.position;
                Vector3 worldOffset = key.parent == null ? _keyPressOffsets[i] : key.parent.TransformVector(_keyPressOffsets[i]);
                float depth = worldOffset.magnitude;
                minimumDepth = Mathf.Min(minimumDepth, depth);
                maximumDepth = Mathf.Max(maximumDepth, depth);
                if (!IsDownwardPress(worldOffset))
                    throw new InvalidOperationException("Key press offset must point down in world space and be approximately 0.0015 m: " + key.name + " offset=" + worldOffset.ToString("F6"));
            }
            AddCheck("keyboard-midi-mapping", true, _probe.keyboardMode + "; 88 distinct ordered key transforms and original rest materials verified.");
            AddCheck("keyboard-world-offsets", true, "All baked press offsets point down; world depth range=" + minimumDepth + ".." + maximumDepth + " m.");
            Transform[] pedalTransforms = ReadRequired(_pedals, "pedalTransforms") as Transform[];
            Require(pedalTransforms != null && pedalTransforms.Length == 3, "pedal-bindings", "Three actual model pedal transforms are bound in left CC67 / middle CC66 / right CC64 order.");
            _probe.pedalRestLocalPositions = new Vector3[3];
            _probe.pedalRestLocalRotations = new Quaternion[3];
            for (int i = 0; i < 3; i++)
            {
                _probe.pedalRestLocalPositions[i] = pedalTransforms[i].localPosition;
                _probe.pedalRestLocalRotations[i] = pedalTransforms[i].localRotation;
            }

            UdonBehaviour library = ReadBehaviour(_facade, "library");
            UdonBehaviour bank = ReadBehaviour(library, "bank");
            AudioClip[] clips = ReadRequired(bank, "clips") as AudioClip[];
            if (clips == null || clips.Length == 0) throw new InvalidOperationException("Runtime bank.clips is missing or empty.");
            _probe.bankClipCount = clips.Length;
            _probe.bankClipNames = new string[clips.Length];
            bool anyV9 = false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) throw new InvalidOperationException("Runtime bank contains a null AudioClip at " + i);
                _probe.bankClipNames[i] = clips[i].name;
                if (clips[i].name.IndexOf("v9", StringComparison.OrdinalIgnoreCase) >= 0) anyV9 = true;
            }
            _probe.salamanderMode = _probe.modelMode || anyV9;
            if (_probe.salamanderMode)
            {
                int layers = (int)ReadRequired(bank, "velocityLayerCount");
                Require((layers == 1 || layers == 4) && clips.Length == 30 * layers, "salamander-bank-count", "Actual runtime bank.clips length=" + clips.Length + "; 30 stereo roots per recorded layer.");
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i].frequency != 48000 || clips[i].channels != 2 || clips[i].name.IndexOf(layers == 1 ? "v9" : "v" + new[] { 3, 7, 11, 15 }[i / 30] + "-q4", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Salamander runtime clip must match its recorded layer, 48 kHz stereo: " + clips[i].name +
                            "; frequency=" + clips[i].frequency + "; channels=" + clips[i].channels);
                AddCheck("salamander-bank-format", true, "All " + clips.Length + " actual AudioClips match the selected layers, 48000 Hz and 2 channels; names retained in bankClipNames.");
            }
            else AddCheck("standalone-bank-format", true, "Legacy standalone bank retained: " + clips.Length + " clips. Salamander-specific count/rate/stereo claims are not made for this fallback.");

            ValidateNativeBinding(_playButton, "Play");
            ValidateNativeBinding(_pauseButton, "Pause");
            ValidateNativeBinding(_stopButton, "Stop");
            ValidateNativeBinding(_secondNativeButton, "Second song");
            Save();
        }

        private static bool HasModelAncestor(Transform key)
        {
            for (Transform item = key; item != null; item = item.parent)
                if (item.name == "PianoModel") return true;
            return false;
        }

        private static bool KeyNameMatches(string name, int pitch, bool padded)
        {
            string prefix = "Key_" + (padded ? pitch.ToString("000") : pitch.ToString());
            if (name.StartsWith(prefix, StringComparison.Ordinal) && (name.Length == prefix.Length || name[prefix.Length] == '_')) return true;
            if (!padded)
            {
                prefix = "Key_" + pitch.ToString("000");
                return name.StartsWith(prefix, StringComparison.Ordinal) && (name.Length == prefix.Length || name[prefix.Length] == '_');
            }
            return false;
        }

        private static bool IsDownwardPress(Vector3 offset)
        {
            float length = offset.magnitude;
            return length > 0 && Mathf.Abs(length - 0.0015f) <= 0.0004f && Vector3.Dot(offset / length, Vector3.down) >= 0.98f;
        }

        private static void ValidateNativeBinding(Button button, string action)
        {
            if (button == null)
            {
                if (_probe.modelMode || _probe.salamanderMode)
                    throw new InvalidOperationException(action + " native Unity Button reference is missing in the model/v9 demo.");
                AddCheck("legacy-button-binding", true, action + " native Button is absent; only this legacy standalone action may use backing SendCustomEvent fallback.");
                return;
            }
            int count = button.onClick.GetPersistentEventCount();
            bool backingListener = false;
            for (int i = 0; i < count; i++)
                if (button.onClick.GetPersistentTarget(i) is UdonBehaviour && button.onClick.GetPersistentMethodName(i) == "SendCustomEvent" &&
                    button.onClick.GetPersistentListenerState(i) != UnityEngine.Events.UnityEventCallState.Off) backingListener = true;
            Require(backingListener, "native-binding-" + action, action + " button=" + button.name + "; persistent listeners=" + count + "; active backing SendCustomEvent binding=" + backingListener);
        }

        private static void InvokeControl(Button button, UdonBehaviour fallback, string eventName, string action)
        {
            if (button != null)
            {
                if (!button.gameObject.activeInHierarchy || !button.enabled || !button.interactable)
                    throw new InvalidOperationException("Native " + action + " button is inactive, disabled or non-interactable; no direct Udon bypass was used.");
                button.onClick.Invoke();
                _probe.nativeButtonDispatchCount++;
                AddCheck("native-click-" + action, true, "Invoked native " + button.name + ".onClick; actual backing-Udon state is checked in the following phase.");
            }
            else
            {
                if (_probe.modelMode || _probe.salamanderMode || fallback == null)
                    throw new InvalidOperationException("Native " + action + " control is required; no fallback is allowed for the model/v9 demo.");
                fallback.SendCustomEvent(eventName);
                _probe.backingFallbackDispatchCount++;
                AddCheck("legacy-dispatch-" + action, true, "Standalone only: missing native Button fell back to actual backing SendCustomEvent(" + eventName + ").");
            }
        }

        private static Button ReadOptionalButton(UdonBehaviour owner, string symbol)
        {
            object value;
            return owner != null && owner.TryGetProgramVariable(symbol, out value) ? value as Button : null;
        }

        private static int[] KeyDownCounts()
        {
            int[] counts = ReadRequired(_sequencer, "keyDownCount") as int[];
            if (counts == null || counts.Length != 88) throw new InvalidOperationException("Actual Sequencer.keyDownCount must contain 88 entries.");
            return counts;
        }

        private static bool HasPressedKey()
        {
            int[] counts = KeyDownCounts();
            for (int i = 0; i < 88; i++) if (counts[i] > 0) return true;
            return false;
        }

        private static void CheckPressedKeys(string checkName)
        {
            int[] counts = KeyDownCounts();
            int pressed = 0;
            int tracked = -1;
            float[] angles = ReadRequired(_keyboard, "keyPressAngles") as float[];
            Vector3[] pivots = ReadRequired(_keyboard, "keyPivotPositions") as Vector3[];
            for (int i = 0; i < 88; i++)
            {
                if (counts[i] <= 0) continue;
                pressed++;
                Vector3 displacement = _keys[i].position - _probe.keyRestWorldPositions[i];
                Quaternion delta = _keys[i].localRotation * Quaternion.Inverse(_probe.keyRestLocalRotations[i]);
                float rotation = Quaternion.Angle(_keys[i].localRotation, _probe.keyRestLocalRotations[i]);
                Vector3 expected = pivots[i] + delta * (_probe.keyRestLocalPositions[i] - pivots[i]);
                if (_keyRenderers[i].sharedMaterial == _keyRestMaterials[i] || rotation > Mathf.Abs(angles[i]) + 0.05f ||
                    Vector3.Distance(_keys[i].localPosition, expected) > 0.00001f || displacement.y > 0.0001f)
                    throw new InvalidOperationException("Held key did not use the note material and its baked rear hinge: " + _keys[i].name);
                if (tracked < 0) tracked = i;
            }
            if (tracked >= 0)
            {
                _probe.trackedKeyMidi = tracked + 21;
                _probe.trackedKeyWorldDisplacement = _keys[tracked].position - _probe.keyRestWorldPositions[tracked];
            }
            Require(pressed > 0, checkName, "Held keys with note materials and bounded rear-hinge rotation=" + pressed +
                "; tracked MIDI=" + _probe.trackedKeyMidi + "; world displacement=" + _probe.trackedKeyWorldDisplacement.ToString("F6"));
        }

        private static void CapturePausedKeyboard()
        {
            _probe.pauseKeyLocalPositions = new Vector3[88];
            _probe.pauseKeyLocalRotations = new Quaternion[88];
            _probe.pauseKeyMaterialIds = new int[88];
            _probe.pauseKeyDownCounts = (int[])KeyDownCounts().Clone();
            for (int i = 0; i < 88; i++)
            {
                _probe.pauseKeyLocalPositions[i] = _keys[i].localPosition;
                _probe.pauseKeyLocalRotations[i] = _keys[i].localRotation;
                Material material = _keyRenderers[i].sharedMaterial;
                _probe.pauseKeyMaterialIds[i] = material == null ? 0 : material.GetInstanceID();
            }
            Transform[] pedals = ReadRequired(_pedals, "pedalTransforms") as Transform[];
            _probe.pausePedalRotations = new Quaternion[3];
            for (int i = 0; i < 3; i++) _probe.pausePedalRotations[i] = pedals[i].localRotation;
            Save();
        }

        private static void CheckPausedKeyboardFrozen()
        {
            int[] counts = KeyDownCounts();
            for (int i = 0; i < 88; i++)
            {
                Material material = _keyRenderers[i].sharedMaterial;
                int materialId = material == null ? 0 : material.GetInstanceID();
                if (counts[i] != _probe.pauseKeyDownCounts[i] || materialId != _probe.pauseKeyMaterialIds[i] ||
                    RotationDifferenceSquared(_keys[i].localRotation, _probe.pauseKeyLocalRotations[i]) > 0.0000000001f ||
                    Vector3.Distance(_keys[i].localPosition, _probe.pauseKeyLocalPositions[i]) > 0.00001f)
                    throw new InvalidOperationException("Paused keyboard material, position or logical key count changed: " + _keys[i].name);
            }
            AddCheck("pause-keyboard-freeze", true, "All 88 actual key transforms, renderer material instances and key-down counts stayed frozen across the second pause interval.");
            Transform[] pedals = ReadRequired(_pedals, "pedalTransforms") as Transform[];
            for (int i = 0; i < 3; i++)
                Require(RotationDifferenceSquared(pedals[i].localRotation, _probe.pausePedalRotations[i]) < 0.0000000001f,
                    "pause-pedal-freeze-" + i, "Native pedal rotation remained frozen at paused music time.");
        }

        private static void CheckRestoredKeys(string checkName)
        {
            int[] counts = KeyDownCounts();
            for (int i = 0; i < 88; i++)
                if (counts[i] != 0 || _keyRenderers[i].sharedMaterial != _keyRestMaterials[i] ||
                    RotationDifferenceSquared(_keys[i].localRotation, _probe.keyRestLocalRotations[i]) > 0.0000000001f ||
                    Vector3.Distance(_keys[i].localPosition, _probe.keyRestLocalPositions[i]) > 0.00001f ||
                    Vector3.Distance(_keys[i].position, _probe.keyRestWorldPositions[i]) > 0.00005f)
                    throw new InvalidOperationException("Stopped key did not restore original material/position or clear key count: " + _keys[i].name);
            AddCheck(checkName, true, "All 88 actual key renderers restored baked rest materials, original local/world positions and zero key-down counts.");
        }

        private static void ObserveKeyMotion()
        {
            if (_probe.sawIntermediateKeyPose || _probe.keyRestLocalRotations == null || _keys == null) return;
            float[] angles = ReadRequired(_keyboard, "keyPressAngles") as float[];
            for (int i = 0; i < 88; i++)
            {
                float amount = Quaternion.Angle(_keys[i].localRotation, _probe.keyRestLocalRotations[i]);
                if (amount > 0.1f && amount < Mathf.Abs(angles[i]) - 0.1f) { _probe.sawIntermediateKeyPose = true; break; }
            }
        }

        private static float RotationDifferenceSquared(Quaternion a, Quaternion b)
        {
            var direct = new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
            var flipped = new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
            return Mathf.Min(direct.sqrMagnitude, flipped.sqrMagnitude);
        }

        private static void CheckPaletteAndWaterfall()
        {
            Material[] palette = ReadRequired(_keyboard, "_noteMaterials") as Material[];
            var colors = new HashSet<Color>();
            Require(palette != null && palette.Length == 88, "midi-hash-palette", "Actual VM owns one cached note material per MIDI key.");
            foreach (Material material in palette)
            {
                if (material == null) throw new InvalidOperationException("Hash note material is missing.");
                colors.Add(material.color);
            }
            Require(colors.Count == 2, "midi-hash-colors", "All 88 keys use only the song's white-key and black-key colors; distinct actual colors=" + colors.Count + ".");
            CheckSongTonePalette();
            UdonBehaviour waterfall = ReadBehaviour(_facade, "waterfall");
            int[] slots = ReadRequired(waterfall, "_slotNote") as int[];
            Renderer[] renderers = ReadRequired(waterfall, "barRenderers") as Renderer[];
            int[] pitch = ReadRequired(ReadBehaviour(_sequencer, "song"), "pitch") as int[];
            Material[] volumePalette = ReadRequired(waterfall, "_barMaterials") as Material[];
            Require(volumePalette != null && volumePalette.Length == 88, "volume-palette", "Waterfall owns a separate cached 88-color volume palette.");
            int matched = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] < 0 || !renderers[i].gameObject.activeInHierarchy) continue;
                int midiPitch = pitch[slots[i]];
                Require(midiPitch >= 0 && midiPitch <= 127, "waterfall-midi-pitch-" + i, "Visible note retains a valid original MIDI pitch.");
                int key = midiPitch - 21;
                while (key < 0) key += 12;
                while (key >= 88) key -= 12;
                Require(renderers[i].sharedMaterial == volumePalette[key] && volumePalette[key] != palette[key]
                    && Vector4.Distance(volumePalette[key].color, palette[key].color) < 0.00001f,
                    "waterfall-key-color-" + i, "Native volume note retains the key's MIDI hue in a separate glowing material.");
                MeshFilter mesh = renderers[i].GetComponent<MeshFilter>();
                Require(mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 24
                    && renderers[i].transform.localScale.z >= 0.006f && renderers[i].sharedMaterial.shader.name == "MusicMidiPlayer/WaterfallVolume",
                    "volume-note-" + i, "Native visible note uses beveled 3D mesh, physical depth and the volume shader.");
                matched++;
            }
            Require(matched > 0, "waterfall-hash-colors", "Visible falling notes using keyed palette=" + matched + ".");
        }

        private static void CheckPedals(string name, bool resting, bool allPressed = false)
        {
            Transform[] pedals = ReadRequired(_pedals, "pedalTransforms") as Transform[];
            float[] angles = ReadRequired(_pedals, "pedalPressAngles") as float[];
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            int[] times = ReadRequired(song, "pedalMs") as int[];
            int[] values = ReadRequired(song, "pedalValue") as int[];
            int[] offsets = ReadRequired(song, "pedalOffsets") as int[];
            int ms = (int)(ReadDouble(_clock, "_position") * 1000);
            for (int i = 0; i < 3; i++)
            {
                int value = 0;
                for (int p = offsets[i]; p < offsets[i + 1] && times[p] <= ms; p++) value = values[p];
                float expected = resting ? 0 : allPressed ? Mathf.Abs(angles[i]) : Mathf.Abs(angles[i]) * value / 127f;
                float actual = Quaternion.Angle(pedals[i].localRotation, _probe.pedalRestLocalRotations[i]);
                Require(Mathf.Abs(actual - expected) < 0.1f, name + "-" + i,
                    "Pedal " + i + ": native angle=" + actual + ", expected=" + expected + ", MIDI CC value=" + value);
                if (resting && Vector3.Distance(pedals[i].localPosition, _probe.pedalRestLocalPositions[i]) > 0.00001f)
                    throw new InvalidOperationException("Pedal did not restore rest position: " + i);
            }
        }

        private static void CheckSongTonePalette()
        {
            Color[] colors = ReadRequired(_keyboard, "songPalette") as Color[];
            Material[] materials = ReadRequired(_keyboard, "_noteMaterials") as Material[];
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            string hash = ReadString(song, "sourceHash");
            string selected = ReadString(_facade, "selectedSongId");
            Require(colors != null && colors.Length == 2 && hash.Length == 64
                && ReadString(_keyboard, "paletteSeedHash") == hash.ToLowerInvariant(), "palette-midi-sha256",
                "Native two-tone palette seed equals the original MIDI SHA256: " + hash);
            Color.RGBToHSV(colors[0], out float whiteHue, out float whiteSaturation, out float whiteValue);
            Color.RGBToHSV(colors[1], out float blackHue, out float blackSaturation, out float blackValue);
            Require(Mathf.Abs(Mathf.DeltaAngle(whiteHue * 360, blackHue * 360)) < 0.02f
                && whiteSaturation < blackSaturation && whiteValue > blackValue,
                "palette-single-song-hue", "White/black colors have the same hue; white is lighter and black is darker/more saturated.");
            bool[] blackKeys = ReadRequired(_keyboard, "keyIsBlack") as bool[];
            for (int key = 0; key < 88; key++)
                if (Vector4.Distance(materials[key].color, colors[blackKeys[key] ? 1 : 0]) > 0.00001f)
                    throw new InvalidOperationException("Key does not use its white/black song color: " + key);
            if (_probe.firstSongPalette == null || _probe.firstSongPalette.Length == 0)
            {
                _probe.firstSongPalette = (Color[])colors.Clone(); _probe.firstSongMidiHash = hash;
                _probe.paletteMaterialIds = new int[88];
                for (int key = 0; key < 88; key++) _probe.paletteMaterialIds[key] = materials[key].GetInstanceID();
            }
            else
            {
                for (int key = 0; key < 88; key++)
                    if (materials[key].GetInstanceID() != _probe.paletteMaterialIds[key])
                        throw new InvalidOperationException("Song change allocated another key material: " + key);
                bool equal = true;
                for (int i = 0; i < 2; i++) if (Vector4.Distance(colors[i], _probe.firstSongPalette[i]) > 0.00001f) equal = false;
                if (selected == _probe.firstSong)
                    Require(hash == _probe.firstSongMidiHash && equal, "palette-roundtrip", "Returning to the same MIDI reproduces both group colors exactly without reallocating materials.");
                else
                {
                    Require(hash != _probe.firstSongMidiHash && !equal, "palette-distinct-songs", "The two actual MIDI SHA256 values produce distinct palettes.");
                    _probe.secondSongPalette = (Color[])colors.Clone(); _probe.secondSongMidiHash = hash;
                }
            }
        }

        private static UdonBehaviour WaterfallEffects()
        {
            return ReadBehaviour(ReadBehaviour(_facade, "waterfall"), "effects");
        }

        private static void CheckWaterfallEffects(bool stopped)
        {
            UdonBehaviour effects = WaterfallEffects();
            ParticleSystem[] pools = ReadRequired(effects, "hitParticles") as ParticleSystem[];
            ParticleSystem[] symbols = ReadRequired(effects, "symbolParticles") as ParticleSystem[];
            Light[] lights = ReadRequired(effects, "hitLights") as Light[];
            Require(pools != null && pools.Length == 12 && lights != null && lights.Length == 8,
                "waterfall-effect-pools", "12 native particle pools and 8 light zones are bound.");
            int particles = 0, activeLights = 0;
            foreach (ParticleSystem pool in pools)
            {
                if (pool == null || !pool.collision.enabled || pool.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    throw new InvalidOperationException("Waterfall particle pool must have world simulation and environment collision.");
                particles += pool.particleCount;
                var live = new ParticleSystem.Particle[64];
                int count = pool.GetParticles(live);
                for (int i = 0; i < count; i++)
                    if (live[i].position.y > pool.transform.position.y + 0.003f) _probe.sawUpwardDust = true;
            }
            Require(symbols != null && symbols.Length == 12, "music-symbol-pools", "12 native musical-symbol pools are bound.");
            int symbolCount = 0;
            var glyphMeshes = new HashSet<Mesh>();
            foreach (ParticleSystem pool in symbols)
            {
                var renderer = pool.GetComponent<ParticleSystemRenderer>();
                if (pool.main.maxParticles != 16 || renderer.renderMode != ParticleSystemRenderMode.Mesh || renderer.meshCount != 4)
                    throw new InvalidOperationException("Musical-symbol pools must use four grouped glyph meshes and 16 particles per pool.");
                var meshes = new Mesh[renderer.meshCount]; renderer.GetMeshes(meshes);
                foreach (var mesh in meshes) glyphMeshes.Add(mesh);
                symbolCount += pool.particleCount;
            }
            Require(glyphMeshes.Count == 6, "music-symbol-variety", "Native renderer pools jointly cover all six music glyph meshes.");
            if (symbolCount > 0) _probe.sawMusicSymbols = true;
            particles += symbolCount;
            Require(symbolCount <= 192, "music-symbol-budget", "Native live symbol particles=" + symbolCount + ".");
            foreach (Light light in lights)
            {
                if (light == null || light.shadows != LightShadows.None) throw new InvalidOperationException("Local hit lights must be shadowless.");
                if (light.enabled && light.intensity > 0) activeLights++;
            }
            if (stopped)
                Require(particles == 0 && activeLights == 0, "waterfall-stop-clear", "Stop clears native particles and disables all local lights.");
            else
                Require(particles > 0 && particles <= 768
                    && (ReadInt(effects, "maxActiveLights") == 0 ? activeLights == 0 : activeLights > 0 && activeLights <= 4)
                    && ReadInt(effects, "processedHitCount") > 0, "waterfall-live-effects",
                    "Native live particles=" + particles + "; enabled light zones=" + activeLights
                    + "; processed hits=" + ReadInt(effects, "processedHitCount") + "; emitted=" + ReadInt(effects, "emittedParticleCount")
                    + "; symbols=" + symbolCount
                    + "; dropped hits=" + ReadInt(effects, "droppedHitCount") + "; all within local budgets.");
        }

        private static void CaptureEffectsSnapshot()
        {
            UdonBehaviour effects = WaterfallEffects();
            ParticleSystem[] pools = AllEffectParticles(effects);
            Light[] lights = ReadRequired(effects, "hitLights") as Light[];
            _probe.pauseParticleCounts = new int[pools.Length];
            _probe.pauseParticleTimes = new float[pools.Length];
            _probe.pauseLightIntensities = new float[lights.Length];
            for (int i = 0; i < pools.Length; i++) { _probe.pauseParticleCounts[i] = pools[i].particleCount; _probe.pauseParticleTimes[i] = pools[i].time; }
            for (int i = 0; i < lights.Length; i++) _probe.pauseLightIntensities[i] = lights[i].intensity;
        }

        private static void CheckEffectsFrozen()
        {
            UdonBehaviour effects = WaterfallEffects();
            ParticleSystem[] pools = AllEffectParticles(effects);
            Light[] lights = ReadRequired(effects, "hitLights") as Light[];
            for (int i = 0; i < pools.Length; i++)
                if (pools[i].particleCount != _probe.pauseParticleCounts[i] || Mathf.Abs(pools[i].time - _probe.pauseParticleTimes[i]) > 0.00001f)
                    throw new InvalidOperationException("Paused particle simulation advanced: pool " + i);
            for (int i = 0; i < lights.Length; i++)
                if (Mathf.Abs(lights[i].intensity - _probe.pauseLightIntensities[i]) > 0.00001f)
                    throw new InvalidOperationException("Paused impact light decayed: zone " + i);
            AddCheck("waterfall-pause-freeze", true, "Native particle counts/simulation times and all local light intensities remained frozen.");
        }

        private static ParticleSystem[] AllEffectParticles(UdonBehaviour effects)
        {
            var dust = (ParticleSystem[])ReadRequired(effects, "hitParticles");
            var symbols = (ParticleSystem[])ReadRequired(effects, "symbolParticles");
            var result = new ParticleSystem[dust.Length + symbols.Length];
            Array.Copy(dust, 0, result, 0, dust.Length);
            Array.Copy(symbols, 0, result, dust.Length, symbols.Length);
            return result;
        }

        private static void CaptureWaterfallPreview()
        {
            const int width = 1600, height = 1100;
            UdonBehaviour effects = WaterfallEffects();
            Light[] lights = ReadRequired(effects, "hitLights") as Light[];
            bool[] enabled = new bool[lights.Length];
            for (int i = 0; i < lights.Length; i++) enabled[i] = lights[i].enabled;
            var host = new GameObject("Waterfall validation camera (temporary)");
            host.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.cullingMask = ~LayerMask.GetMask("InternalUI", "UiMenu", "MirrorReflection", "Player", "PlayerLocal");
            camera.fieldOfView = 42;
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 80;
            Vector3 origin = _facade.transform.root.position;
            host.transform.position = origin + new Vector3(1.65f, 1.75f, -4.15f);
            host.transform.LookAt(origin + new Vector3(-0.05f, 1.35f, 0.15f));
            var previous = RenderTexture.active;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D on = null, off = null, front = null;
            try
            {
                camera.targetTexture = target;
                on = CaptureCamera(camera, target, width, height);
                string folder = ProjectPath("Docs/UdonMidiPlayer/Previews");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "waterfall-volume-playing.png");
                File.WriteAllBytes(file, on.EncodeToPNG());
                _probe.waterfallPreview = file;
                for (int i = 0; i < lights.Length; i++) lights[i].enabled = false;
                off = CaptureCamera(camera, target, width, height);
                File.WriteAllBytes(Path.Combine(folder, "waterfall-volume-lights-off.png"), off.EncodeToPNG());
                Color32[] a = on.GetPixels32(), b = off.GetPixels32();
                float difference = 0;
                // The lower central image contains the floor/piano base, below the falling bars.
                for (int y = 0; y < height * 0.42f; y++)
                    for (int x = width / 4; x < width * 3 / 4; x++)
                    {
                        int p = y * width + x;
                        float delta = (Mathf.Abs(a[p].r - b[p].r) + Mathf.Abs(a[p].g - b[p].g) + Mathf.Abs(a[p].b - b[p].b)) / 765f;
                        difference = Mathf.Max(difference, delta);
                    }
                _probe.environmentLightPixelDelta = difference;
                Require(ReadInt(WaterfallEffects(), "maxActiveLights") == 0 ? difference <= 0.001f : difference > 0.001f, "environment-light-render", "Native floor/piano render respects the configured light budget; maximum RGB mean delta=" + difference);
                AddCheck("waterfall-render-preview", true, "Captured real paused Play Mode frame with the current scene environment: " + file);
                for (int i = 0; i < lights.Length; i++) lights[i].enabled = enabled[i];
                host.transform.position = origin + new Vector3(0, 1.65f, -4.25f);
                host.transform.LookAt(origin + new Vector3(0, 1.35f, 0.15f));
                front = CaptureCamera(camera, target, width, height);
                File.WriteAllBytes(Path.Combine(folder, "waterfall-translucent-front.png"), front.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = enabled[i];
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                if (on != null) UnityEngine.Object.DestroyImmediate(on);
                if (off != null) UnityEngine.Object.DestroyImmediate(off);
                if (front != null) UnityEngine.Object.DestroyImmediate(front);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static Texture2D CaptureCamera(Camera camera, RenderTexture target, int width, int height)
        {
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            return image;
        }

        private static void BeginThreePedalCheck()
        {
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            _probe.originalPedalMs = (int[])ReadRequired(song, "pedalMs");
            _probe.originalPedalController = (int[])ReadRequired(song, "pedalController");
            _probe.originalPedalValue = (int[])ReadRequired(song, "pedalValue");
            _probe.originalPedalOffsets = (int[])ReadRequired(song, "pedalOffsets");
            song.SetProgramVariable("pedalMs", new[] { 0, 900, 0, 900, 0, 900 });
            song.SetProgramVariable("pedalController", new[] { 67, 67, 66, 66, 64, 64 });
            song.SetProgramVariable("pedalValue", new[] { 127, 0, 127, 0, 127, 0 });
            song.SetProgramVariable("pedalOffsets", new[] { 0, 2, 4, 6 });
            InvokeControl(_playButton, _facade, "CommandPlay", "play-pedal-probe");
        }

        private static void ObservePedalMotion()
        {
            Transform[] pedals = ReadRequired(_pedals, "pedalTransforms") as Transform[];
            float[] angles = ReadRequired(_pedals, "pedalPressAngles") as float[];
            for (int i = 0; i < 3; i++)
            {
                float amount = Quaternion.Angle(pedals[i].localRotation, _probe.pedalRestLocalRotations[i]);
                if (amount > 0.1f && amount < Mathf.Abs(angles[i]) - 0.1f) _probe.sawIntermediatePedalPose = true;
            }
        }

        private static void RestorePedalTimeline()
        {
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            song.SetProgramVariable("pedalMs", _probe.originalPedalMs);
            song.SetProgramVariable("pedalController", _probe.originalPedalController);
            song.SetProgramVariable("pedalValue", _probe.originalPedalValue);
            song.SetProgramVariable("pedalOffsets", _probe.originalPedalOffsets);
        }

        private static void RecordPlayingClips(string checkName)
        {
            UdonBehaviour pool = ReadBehaviour(_facade, "voicePool");
            UdonBehaviour song = ReadBehaviour(_sequencer, "song");
            UdonBehaviour poolSong = ReadBehaviour(pool, "_song");
            UdonBehaviour bank = ReadBehaviour(pool, "bank");
            UdonBehaviour libraryBank = ReadBehaviour(ReadBehaviour(_facade, "library"), "bank");
            int[] notes = ReadRequired(pool, "_slotNote") as int[];
            int[] regions = ReadRequired(pool, "_slotRegion") as int[];
            int[] states = ReadRequired(pool, "_slotState") as int[];
            int[] generations = ReadRequired(pool, "_slotGeneration") as int[];
            int poolGeneration = ReadInt(pool, "_generation");
            int[] pitches = ReadRequired(song, "pitch") as int[];
            int[] poolPitches = ReadRequired(poolSong, "pitch") as int[];
            int[] pitchToRegion = ReadRequired(bank, "pitchToRegion") as int[];
            int[] roots = ReadRequired(bank, "rootPitch") as int[];
            int[] low = ReadRequired(bank, "lowPitch") as int[];
            int[] high = ReadRequired(bank, "highPitch") as int[];
            float[] tuning = ReadRequired(bank, "tuningCents") as float[];
            AudioClip[] clips = ReadRequired(bank, "clips") as AudioClip[];
            _voices = ReadRequired(pool, "voiceSources") as AudioSource[];
            if (_voices == null || notes == null || regions == null || states == null || generations == null ||
                pitches == null || poolPitches == null || pitchToRegion == null || pitchToRegion.Length != 88 ||
                roots == null || low == null || high == null || tuning == null || clips == null)
                throw new InvalidOperationException("Voice mapping evidence requires actual VM slot arrays, Sequencer/Pool song pitch arrays, bank mapping, bounds, roots, tuning and native clips.");

            int playing = 0;
            int v9 = 0;
            int mismatches = 0;
            int recorded = 0;
            var names = new List<string>();
            var problems = new List<string>();
            var uniqueSources = new HashSet<AudioSource>();
            if (song != poolSong) problems.Add("VoicePool._song differs from Sequencer.song");
            if (bank != libraryBank) problems.Add("VoicePool.bank differs from Library.bank");
            for (int slot = 0; slot < _voices.Length; slot++)
            {
                AudioSource source = _voices[slot];
                if (source != null && !uniqueSources.Add(source)) problems.Add("voiceSources duplicates the same native AudioSource at slot " + slot);
                if (source == null || !source.isPlaying) continue;
                playing++;
                int note = slot < notes.Length ? notes[slot] : -1;
                int midi = note >= 0 && note < pitches.Length ? pitches[note] : -1;
                int poolMidi = note >= 0 && note < poolPitches.Length ? poolPitches[note] : -1;
                int mappedPitch = Mathf.Clamp(midi, 21, 108);
                int expectedRegion = midi >= 0 && midi <= 127 ? pitchToRegion[mappedPitch - 21] : -1;
                int actualRegion = slot < regions.Length ? regions[slot] : -1;
                bool validRegion = expectedRegion >= 0 && expectedRegion < clips.Length && expectedRegion < roots.Length &&
                    expectedRegion < low.Length && expectedRegion < high.Length && expectedRegion < tuning.Length;
                AudioClip expectedClip = validRegion ? clips[expectedRegion] : null;
                AudioClip actualClip = source.clip;
                int root = validRegion ? roots[expectedRegion] : -1;
                float correction = validRegion ? tuning[expectedRegion] : 0f;
                float ratio = validRegion ? Mathf.Pow(2f, (midi - root) / 12f + correction / 1200f) : 0f;
                int slotGeneration = slot < generations.Length ? generations[slot] : -1;
                var errors = new List<string>();
                if (note < 0 || note >= pitches.Length || poolMidi != midi) errors.Add("invalid note/song pitch association");
                if (!validRegion) errors.Add("expected region out of VM bank bounds");
                if (actualRegion != expectedRegion) errors.Add("slotRegion != VM pitchToRegion[clamp(pitch,21,108)-21]");
                if (actualClip == null || expectedClip == null || actualClip != expectedClip) errors.Add("native AudioSource.clip != expected VM bank clip object");
                if (validRegion && (mappedPitch < low[expectedRegion] || mappedPitch > high[expectedRegion])) errors.Add("Clamped sample-selection pitch lies outside the selected VM region key range");
                if (slotGeneration != poolGeneration) errors.Add("playing voice belongs to another generation");
                if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f || ratio > 3f || Mathf.Abs(source.pitch - ratio) > 0.001f) errors.Add("native AudioSource.pitch != playable original-pitch/root/tuning ratio");
                if (_probe.salamanderMode && expectedClip != null && V9ClipMidiPitch(expectedClip.name) != root)
                    errors.Add("v9 expected clip name pitch != VM bank rootPitch (possible corrupted clips/root array)");
                if (song != poolSong || bank != libraryBank) errors.Add("runtime song/bank backing reference mismatch");
                string name = actualClip == null ? "(null)" : actualClip.name;
                if (!names.Contains(name)) names.Add(name);
                if (_probe.playedClipNames == null) _probe.playedClipNames = new List<string>();
                if (!_probe.playedClipNames.Contains(name)) _probe.playedClipNames.Add(name);
                if (name.IndexOf("v9", StringComparison.OrdinalIgnoreCase) >= 0) v9++;
                string mismatch = string.Join("; ", errors.ToArray());
                if (errors.Count > 0)
                {
                    mismatches++;
                    if (problems.Count < 12) problems.Add("slot " + slot + ", note " + note + ", MIDI " + midi +
                        ", expected/slot region " + expectedRegion + "/" + actualRegion + ", actual/expected clip " + name + "/" +
                        (expectedClip == null ? "(null)" : expectedClip.name) + ": " + mismatch);
                }
                if (recorded < 10)
                {
                    if (_probe.voiceMappingEvidence == null) _probe.voiceMappingEvidence = new List<VoiceMappingEvidence>();
                    _probe.voiceMappingEvidence.Add(new VoiceMappingEvidence
                    {
                        phase = checkName, slot = slot, sourceName = source.name, sourceInstanceId = source.GetInstanceID(),
                        noteId = note, midiPitch = midi, poolSongPitch = poolMidi, expectedRegion = expectedRegion, slotRegion = actualRegion,
                        rootPitch = root, regionLowPitch = validRegion ? low[expectedRegion] : -1, regionHighPitch = validRegion ? high[expectedRegion] : -1,
                        slotState = slot < states.Length ? states[slot] : -1, slotGeneration = slotGeneration, poolGeneration = poolGeneration,
                        actualClip = name, expectedClip = expectedClip == null ? "(null)" : expectedClip.name,
                        actualClipInstanceId = actualClip == null ? 0 : actualClip.GetInstanceID(), expectedClipInstanceId = expectedClip == null ? 0 : expectedClip.GetInstanceID(),
                        actualPitchRatio = source.pitch, expectedPitchRatio = ratio, tuningCents = correction, matches = errors.Count == 0, mismatch = mismatch
                    });
                    recorded++;
                }
            }
            Require(playing > 0 && mismatches == 0 && problems.Count == 0 && (!_probe.salamanderMode || v9 == playing), checkName,
                "Actual isPlaying AudioSource.clip names=" + string.Join(", ", names.ToArray()) + "; playing=" + playing + "; v9 voices=" + v9 +
                "; mapping mismatches=" + mismatches + "; first ten voice mappings for this phase are in voiceMappingEvidence. " +
                string.Join(" | ", problems.ToArray()) + ". This is native playback/mapping evidence, not captured output audio.");
        }

        private static int V9ClipMidiPitch(string name)
        {
            Match match = Regex.Match(name, @"^([A-G])(#?)(-?\d+)v9$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return -1;
            string note = match.Groups[1].Value.ToUpperInvariant();
            int pc = note == "C" ? 0 : note == "D" ? 2 : note == "E" ? 4 : note == "F" ? 5 : note == "G" ? 7 : note == "A" ? 9 : 11;
            if (match.Groups[2].Value == "#") pc++;
            int octave;
            return int.TryParse(match.Groups[3].Value, out octave) ? (octave + 1) * 12 + pc : -1;
        }

        private static UdonBehaviour ReadBehaviour(UdonBehaviour owner, string symbol)
        {
            UdonBehaviour result = AsBacking(ReadRequired(owner, symbol));
            if (result == null) throw new InvalidOperationException("Runtime reference " + symbol + " has no backing UdonBehaviour.");
            return result;
        }

        private static UdonBehaviour AsBacking(object value)
        {
            UdonBehaviour backing = value as UdonBehaviour;
            if (backing != null) return backing;
            UdonSharpBehaviour proxy = value as UdonSharpBehaviour;
            return proxy == null ? null : UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        }

        private static object ReadRequired(UdonBehaviour owner, string symbol)
        {
            if (owner == null || !owner.IsInitialized)
                throw new InvalidOperationException("Backing UdonBehaviour is not initialized when reading " + symbol + ".");
            object value;
            if (!owner.TryGetProgramVariable(symbol, out value))
                throw new MissingMemberException("Udon heap metadata symbol '" + symbol + "' was not found on " + owner.gameObject.name +
                    ". Private field names may differ after Udon compilation; proxy values were not used instead.");
            return owner.GetProgramVariable(symbol);
        }

        private static bool ReadBool(UdonBehaviour owner, string symbol) { return Convert.ToBoolean(ReadRequired(owner, symbol)); }
        private static int ReadInt(UdonBehaviour owner, string symbol) { return Convert.ToInt32(ReadRequired(owner, symbol)); }
        private static double ReadDouble(UdonBehaviour owner, string symbol) { return Convert.ToDouble(ReadRequired(owner, symbol)); }
        private static string ReadString(UdonBehaviour owner, string symbol) { return ReadRequired(owner, symbol) as string ?? ""; }

        private static string ReadOptionalString(UdonBehaviour owner, string symbol)
        {
            object value;
            return owner != null && owner.TryGetProgramVariable(symbol, out value) ? value as string ?? "" : "(metadata unavailable)";
        }

        private static void RequireState(int expected)
        {
            int actual = ReadInt(_clock, "_state");
            if (actual != expected) throw new InvalidOperationException("Backing Clock _state expected " + expected + " but was " + actual +
                "; Facade error=" + ReadOptionalString(_facade, "lastError"));
        }

        private static int PlayingVoices()
        {
            int count = 0;
            foreach (AudioSource source in _voices) if (source != null && source.isPlaying) count++;
            return count;
        }

        private static int MaximumSamplePosition()
        {
            int maximum = 0;
            foreach (AudioSource source in _voices)
                if (source != null && source.isPlaying) maximum = Math.Max(maximum, source.timeSamples);
            return maximum;
        }

        private static int[] SamplePositions()
        {
            var result = new int[_voices.Length];
            for (int i = 0; i < result.Length; i++) result[i] = _voices[i] == null ? -1 : _voices[i].timeSamples;
            return result;
        }

        private static bool SamplesEqual(int[] before, int[] after)
        {
            if (before == null || after == null || before.Length != after.Length) return false;
            for (int i = 0; i < before.Length; i++) if (Math.Abs(before[i] - after[i]) > 2) return false;
            return true;
        }

        private static void Require(bool condition, string name, string message)
        {
            AddCheck(name, condition, message);
            if (!condition) throw new InvalidOperationException("Probe check failed: " + name + "; " + message);
        }

        private static void AddCheck(string name, bool success, string message)
        {
            if (_probe.checks == null) _probe.checks = new List<Check>();
            _probe.checks.Add(new Check { name = name, success = success, message = message });
            Save();
        }

        private static double Elapsed() { return EditorApplication.timeSinceStartup - _probe.phaseStartedAt; }
        private static bool IsActive() { return _probe != null && SessionState.GetBool(ActiveKey, false) && !_probe.finished; }

        private static void SetStage(string stage)
        {
            _probe.stage = stage;
            _probe.phaseStartedAt = EditorApplication.timeSinceStartup;
            Save();
        }

        private static void Save()
        {
            if (_probe != null) SessionState.SetString(SessionKey, JsonUtility.ToJson(_probe));
        }

        private static void Fail(Exception exception)
        {
            if (_probe == null || _probe.finished) return;
            if (!_probe.failed)
            {
                _probe.failed = true;
                _probe.exception = exception.ToString();
                AddCheck(_probe.stage, false, exception.Message);
            }
            SetStage("ExitingPlay");
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else RestoreAndComplete();
        }

        private static void RestoreAndComplete()
        {
            if (_finishing || _probe == null) return;
            _finishing = true;
            try
            {
                if (_probe.preserveScenes)
                {
                    RestorePreservedScenes();
                }
                else
                {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    if (current.isDirty && current.path != DemoScene)
                        throw new InvalidOperationException("A different scene became dirty during the probe; automatic scene restore was skipped to preserve it: " + current.name);
                }
                var restore = new List<SceneSetup>();
                if (_probe.previousScenes != null)
                {
                    foreach (PreviousScene scene in _probe.previousScenes)
                    {
                        if (scene == null || string.IsNullOrEmpty(scene.path) || !File.Exists(ProjectPath(scene.path))) continue;
                        restore.Add(new SceneSetup { path = scene.path, isLoaded = scene.loaded, isActive = scene.active });
                    }
                }
                if (restore.Count > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(restore.ToArray());
                    AddCheck("restore-scenes", true, "Restored the previously saved scene setup without saving Play Mode state.");
                }
                else AddCheck("restore-scenes", true, "No previous valid saved scene path was available; demo left open without saving Play Mode state.");
                }
            }
            catch (Exception exception)
            {
                _probe.failed = true;
                _probe.exception = (_probe.exception ?? "") + "\nScene restore: " + exception;
                AddCheck("restore-scenes", false, exception.Message);
            }
            finally
            {
                Complete(!_probe.failed);
                _finishing = false;
            }
        }

        private static void Complete(bool success)
        {
            _probe.success = success;
            _probe.finished = true;
            _probe.stage = success ? "Complete" : "Failed";
            _probe.completedUtc = DateTime.UtcNow.ToString("o");
            Save();
            SessionState.SetBool(ActiveKey, false);
            string path = ProjectPath(ReportFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(_probe, true));
        }

        private static void LogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!IsActive() || _probe.failed || (type != LogType.Exception && type != LogType.Error)) return;
            if (condition.IndexOf("Udon", StringComparison.OrdinalIgnoreCase) < 0 &&
                condition.IndexOf("MusicMidiPlayer", StringComparison.OrdinalIgnoreCase) < 0 &&
                stackTrace.IndexOf("MusicMidiPlayer", StringComparison.OrdinalIgnoreCase) < 0) return;
            Fail(new InvalidOperationException("Runtime/Editor Udon error during probe: " + condition + "\n" + stackTrace));
        }

        private static string ProjectPath(string relative)
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), relative));
        }
    }
}
