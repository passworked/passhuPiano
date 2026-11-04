using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Import;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Networking;
using MusicMidiPlayer.UI;
using MusicMidiPlayer.Views;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPlayerBuilder
    {
        public static void ConfigureVoicePool(PianoVoicePool voices, int limit)
        {
            voices.voiceLimit = Mathf.Clamp(limit, 1, PianoVoicePool.VoiceCapacity);
            var sources = new AudioSource[PianoVoicePool.VoiceCapacity];
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = voices.voiceSources != null && i < voices.voiceSources.Length ? voices.voiceSources[i] : null;
                if (source == null)
                {
                    Transform item = Child(voices.transform, "Voice_" + i.ToString("000"));
                    source = Component<AudioSource>(item.gameObject);
                    if (voices.voiceSources != null && voices.voiceSources.Length > 0 && voices.voiceSources[0] != null)
                        EditorUtility.CopySerialized(voices.voiceSources[0], source);
                    source.playOnAwake = false; source.loop = false; source.clip = null;
                    source.volume = 0; source.dopplerLevel = 0;
                    source.spatialBlend = voices.worldSpaceAudio ? 1 : 0;
                }
                // Keep the listening position inside the full-volume area.
                // Near=0 makes the SDK distance curve very quiet just metres away.
                var spatial = Component<VRCSpatialAudioSource>(source.gameObject);
                spatial.Near = 5f;
                spatial.Far = 80f;
                spatial.UseAudioSourceVolumeCurve = false;
                source.minDistance = 5f;
                source.maxDistance = 80f;
                sources[i] = source;
            }
            // Preserve the retained sources and their file IDs. Remove only
            // surplus audio-only children generated for this pool.
            foreach (AudioSource source in voices.GetComponentsInChildren<AudioSource>(true))
            {
                if (Array.IndexOf(sources, source) >= 0) continue;
                int index;
                if (source.transform.parent != voices.transform || source.transform.childCount != 0
                    || !source.name.StartsWith("Voice_", StringComparison.Ordinal)
                    || !int.TryParse(source.name.Substring(6), out index) || index < PianoVoicePool.VoiceCapacity)
                    throw new InvalidOperationException("声音池存在非预期的额外AudioSource，请检查引用: " + source.name);
                foreach (UnityEngine.Component component in source.GetComponents<UnityEngine.Component>())
                    if (!(component is Transform) && !(component is AudioSource) && !(component is VRC.SDKBase.VRC_SpatialAudioSource))
                        throw new InvalidOperationException("多余声音槽含其他组件，停止清理: " + source.name);
                UnityEngine.Object.DestroyImmediate(source.gameObject);
            }
            voices.voiceSources = sources;
            EditorUtility.SetDirty(voices);
            UdonSharpEditorUtility.CopyProxyToUdon(voices, ProxySerializationPolicy.All);
        }

        public const string PrefabPath = "Assets/UdonMidiPlayer/Prefabs/UdonMidiPlayer.prefab";
        public const string DemoScenePath = "Assets/UdonMidiPlayer/Samples/MidiPlayerDemo.unity";
        public const string DefaultLibraryPath = "Assets/UdonMidiPlayer/Editor/Generated/DemoLibrary.asset";
        public const string DefaultConfigurationPath = "Assets/UdonMidiPlayer/Editor/Generated/DemoConfiguration.asset";
        private static TMP_FontAsset font;
        private static readonly List<Action> resourceReadyCallbacks = new List<Action>();
        private static readonly List<Action<Exception>> resourceErrorCallbacks = new List<Action<Exception>>();
        private static bool resourceImportPending;
        private static bool resourceCheckQueued;
        private static double resourceImportDeadline;

        [MenuItem("Tools/Music MIDI Player/Build Demo Prefab and Scene")]
        public static void BuildDemoFromMenu()
        {
            BuildDemoAsync(null, Debug.LogException);
        }

        public static void BuildDemoAsync(Action completed, Action<Exception> failed)
        {
            PrepareResources(() => { BuildDemo(); if (completed != null) completed(); }, failed);
        }

        public static void BuildDemo()
        {
            BuildArtifacts(true);
        }

        public static void BuildPrefabOnly()
        {
            BuildArtifacts(false);
        }

        /// <summary>Updates the existing Prefab directly, without opening or creating a scene asset.</summary>
        public static void UpdateExistingPrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式后更新Prefab");
            if (!File.Exists(PrefabPath)) throw new FileNotFoundException("找不到现有播放器Prefab", PrefabPath);
            var configuration = ExistingPrefabConfiguration()
                ?? AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(DefaultConfigurationPath);
            if (configuration == null) throw new InvalidOperationException("找不到现有播放器默认配置");
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Configure(contents, configuration);
                MidiBuildValidator.ValidateRoot(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) == null)
                    throw new InvalidOperationException("播放器Prefab保存失败");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        [MenuItem("Tools/Music MIDI Player/Update Player UI")]
        public static void UpdateExistingUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式后更新UI");
            if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(MidiLibraryFolderButton)) == null) EnsurePrograms();
            EnsureFont();
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                MidiPlayerFacade facade = contents.GetComponentInChildren<MidiPlayerFacade>(true);
                string configPath = AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid);
                var configuration = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(configPath);
                if (configuration == null) throw new InvalidOperationException("找不到播放器配置");
                MidiDataBaker.ValidateFolders(configuration.library);
                BuildUI(contents.transform, facade, configuration, true);
                foreach (UdonSharpBehaviour behaviour in facade.playerUI.GetComponentsInChildren<UdonSharpBehaviour>(true))
                {
                    UdonSharpEditorUtility.CopyProxyToUdon(behaviour, ProxySerializationPolicy.All);
                    var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                    if (backing != null) backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                }
                MidiBuildValidator.ValidateRoot(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) == null)
                    throw new InvalidOperationException("播放器UI保存失败");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public static MidiLibraryAsset FindSceneLibrary()
        {
            MidiLibraryAsset onlyLibrary = null;
            bool ambiguous = false;
            foreach (MidiPlayerFacade facade in UnityEngine.Resources.FindObjectsOfTypeAll<MidiPlayerFacade>())
            {
                if (!IsScenePlayer(facade)) continue;
                MidiPlayerConfigurationAsset configuration = ConfigurationOf(facade);
                if (configuration == null || configuration.library == null) continue;
                Transform root = facade.transform.parent.parent;
                Transform selected = Selection.activeTransform;
                if (selected != null && (selected == root || selected.IsChildOf(root))) return configuration.library;
                if (onlyLibrary != null && onlyLibrary != configuration.library) ambiguous = true;
                onlyLibrary = configuration.library;
            }
            return ambiguous ? null : onlyLibrary;
        }

        public static int SynchronizeLibraryPlayers(MidiLibraryAsset library, Action<string> report, bool failOnError = false, bool useLibraryDefault = false)
        {
            if (library == null) return 0;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后同步曲库");
            int updated = 0, matched = 0, failed = 0;
            foreach (MidiPlayerFacade facade in UnityEngine.Resources.FindObjectsOfTypeAll<MidiPlayerFacade>())
            {
                if (!IsScenePlayer(facade)) continue;
                MidiPlayerConfigurationAsset configuration = ConfigurationOf(facade);
                if (configuration == null || configuration.library != library) continue;
                matched++;
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Synchronize MIDI Library");
                try
                {
                    SynchronizeLibrary(facade, configuration, useLibraryDefault ? library.defaultSongId : null);
                    if (useLibraryDefault && !string.IsNullOrEmpty(configuration.defaultSongId))
                    {
                        Undo.RecordObject(configuration, "Use MIDI library default");
                        configuration.defaultSongId = "";
                        EditorUtility.SetDirty(configuration);
                        AssetDatabase.SaveAssetIfDirty(configuration);
                    }
                    Undo.CollapseUndoOperations(undoGroup);
                    updated++;
                    report?.Invoke(facade.gameObject.scene.name + "：已自动配置 " + library.songs.Length + " 首曲目，请保存场景。");
                }
                catch (Exception exception)
                {
                    failed++;
                    Undo.RevertAllDownToGroup(undoGroup);
                    report?.Invoke(facade.gameObject.scene.name + "：曲库已保存，自动配置失败：" + exception.Message);
                    Debug.LogException(exception);
                }
                finally { Undo.IncrementCurrentGroup(); }
            }
            if (matched == 0) report?.Invoke("曲库已保存；当前打开的场景中没有使用此曲库的播放器。");
            if (failOnError && failed > 0) throw new InvalidOperationException("Scene application failed: " + failed + "; applied: " + updated);
            return updated;
        }

        /// <summary>Rebakes only library data and navigation, preserving the existing presentation.</summary>
        public static void SynchronizePlayerLibrary(MidiPlayerFacade facade)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出播放模式后同步曲库");
            if (facade == null) throw new ArgumentNullException(nameof(facade));
            MidiPlayerConfigurationAsset configuration = ConfigurationOf(facade);
            if (configuration == null) throw new InvalidOperationException("找不到播放器配置");
            SynchronizeLibrary(facade, configuration);
        }

        /// <summary>Filters a release copy without changing the authoring configuration asset.</summary>
        public static void SynchronizePlayerLibrary(MidiPlayerFacade facade, MidiPlayerConfigurationAsset configuration)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("播放模式下不能同步曲库");
            if (facade == null || configuration == null) throw new ArgumentNullException("facade/configuration");
            SynchronizeLibrary(facade, configuration);
        }

        private static bool IsScenePlayer(MidiPlayerFacade facade)
        {
            return facade != null && !EditorUtility.IsPersistent(facade) && facade.gameObject.scene.IsValid()
                && facade.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(facade.gameObject.scene)
                && PrefabStageUtility.GetPrefabStage(facade.gameObject) == null
                && facade.transform.parent != null && facade.transform.parent.parent != null
                && facade.transform.parent.parent.Find("Runtime/Facade") == facade.transform;
        }

        private static MidiPlayerConfigurationAsset ConfigurationOf(MidiPlayerFacade facade)
        {
            if (facade.library == null || string.IsNullOrEmpty(facade.library.configurationAssetGuid)) return null;
            return AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(
                AssetDatabase.GUIDToAssetPath(facade.library.configurationAssetGuid));
        }

        private static MidiPlayerConfigurationAsset ExistingPrefabConfiguration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            MidiPlayerFacade facade = prefab == null ? null : prefab.GetComponentInChildren<MidiPlayerFacade>(true);
            return facade == null ? null : ConfigurationOf(facade);
        }

        // Updating songs must not rebuild the piano, effects, audio pool or custom UI styling.
        private static void SynchronizeLibrary(MidiPlayerFacade facade, MidiPlayerConfigurationAsset configuration, string libraryDefaultOverride = null)
        {
            MidiDataBaker.ValidateLibrary(configuration.library);
            MidiDataBaker.ValidateBank(configuration.bank);
            if (facade.library.bank == null || facade.library.bank.bankHash != configuration.bank.bankHash)
                throw new InvalidOperationException("音源配置已变化，请先在 Configure Player 应用配置");
            if (facade.playerUI == null || facade.network == null)
                throw new InvalidOperationException("播放器接线不完整，请先在 Configure Player 应用配置");
            string defaultId = libraryDefaultOverride ?? (string.IsNullOrEmpty(configuration.defaultSongId)
                ? configuration.library.defaultSongId : configuration.defaultSongId);
            if (string.IsNullOrEmpty(defaultId)) defaultId = configuration.library.songs[0].songId;
            bool foundDefault = false;
            foreach (MidiSongAsset song in configuration.library.songs)
            {
                if (song.songId == defaultId) foundDefault = true;
                if ((long)song.durationMs + facade.library.bank.maxReleaseMs > int.MaxValue)
                    throw new InvalidOperationException("曲目加尾音时长溢出: " + song.title);
            }
            if (!foundDefault) throw new InvalidOperationException("配置默认曲目不在曲库内");
            GameObject root = facade.transform.parent.parent.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(root, "Synchronize MIDI Library");
            Transform dataRoot = facade.library.transform.parent;
            var songData = new MidiSongData[configuration.library.songs.Length];
            var wantedSongs = new HashSet<string>();
            for (int i = 0; i < songData.Length; i++)
            {
                MidiSongAsset song = configuration.library.songs[i];
                string childName = "Song_" + song.songId;
                wantedSongs.Add(childName);
                songData[i] = Behaviour<MidiSongData>(dataRoot, childName);
                MidiDataBaker.CopySongToRuntime(song, songData[i]);
            }
            RemoveStaleSongObjects(dataRoot, wantedSongs);
            facade.library.songs = songData;
            facade.library.defaultSongId = defaultId;
            facade.library.catalogHash = MidiInstrumentMigration.InstrumentCatalogHash(facade, MidiContentHash.Catalog(configuration.library, configuration.bank));
            facade.network.catalogHash = facade.library.catalogHash;
            if (facade.library.FindSong(facade.network.songId) == null) facade.network.songId = defaultId;
            MidiPlayerUIBuilder.SynchronizeLibrary(facade.playerUI, facade, configuration);
            UdonSharpEditorUtility.CopyProxyToUdon(facade, ProxySerializationPolicy.All);
            foreach (UnityEngine.Component component in facade.playerUI.GetComponentsInChildren<UnityEngine.Component>(true))
            {
                if (component == null) continue;
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                if (component is Transform && PrefabUtility.IsPartOfPrefabInstance(component.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component.gameObject);
            }
            foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                EditorUtility.SetDirty(behaviour);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                if (backing != null)
                {
                    backing.SyncMethod = behaviour == facade.network ? VRC.SDKBase.Networking.SyncType.Manual : VRC.SDKBase.Networking.SyncType.None;
                    EditorUtility.SetDirty(backing);
                    if (PrefabUtility.IsPartOfPrefabInstance(backing)) PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
                }
                if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static void BuildArtifacts(bool includeDemoScene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式后生成播放器");
            EnsureFont();
            Scene existingScene = SceneManager.GetSceneByPath(DemoScenePath);
            if (includeDemoScene && existingScene.IsValid() && existingScene.isLoaded && existingScene.isDirty)
                throw new InvalidOperationException("示例场景包含未保存修改，请先保存；工具不会覆盖未保存场景");
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Editor/Generated");
            MidiPlayerConfigurationAsset configuration = ExistingPrefabConfiguration()
                ?? LoadOrCreate<MidiPlayerConfigurationAsset>(DefaultConfigurationPath);
            PianoBankAsset bank = configuration.bank != null ? configuration.bank : ProceduralPianoGenerator.CreateDefaultBank();
            EnsureBankPrepared(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
            MidiLibraryAsset library = configuration.library != null ? configuration.library : LoadOrCreate<MidiLibraryAsset>(DefaultLibraryPath);
            if (library.songs == null || library.songs.Length == 0)
            {
                library.title = "MIDI Player Demo"; library.songs = DemoMidiGenerator.EnsureDemoSongs();
                library.defaultSongId = library.songs.Length == 0 ? "" : library.songs[0].songId;
                EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
            }
            configuration.library = library; configuration.bank = bank;
            if (string.IsNullOrEmpty(configuration.defaultSongId)) configuration.defaultSongId = library.defaultSongId;
            EditorUtility.SetDirty(configuration); AssetDatabase.SaveAssetIfDirty(configuration);
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Prefabs");
            GameObject prefabRoot = null;
            bool loaded = File.Exists(PrefabPath);
            Scene originalScene = SceneManager.GetActiveScene();
            Scene buildScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(buildScene);
            try
            {
                prefabRoot = loaded ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("UdonMidiPlayer");
                Configure(prefabRoot, configuration);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                if (saved == null) throw new InvalidOperationException("播放器 Prefab 保存失败");
            }
            finally
            {
                if (prefabRoot != null)
                {
                    if (loaded) PrefabUtility.UnloadPrefabContents(prefabRoot);
                    else UnityEngine.Object.DestroyImmediate(prefabRoot);
                }
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (buildScene.IsValid() && buildScene.isLoaded) EditorSceneManager.CloseScene(buildScene, true);
            }
            if (includeDemoScene) BuildDemoScene();
            Debug.Log(includeDemoScene
                ? "Music MIDI Player：已生成 Prefab、默认配置与示例场景。尚需当前 SDK 编译及客户端验证。"
                : "Music MIDI Player：已更新主 Prefab 与默认配置；未保存、关闭或重建现有场景。");
        }

        public static void Configure(GameObject target, MidiLibraryAsset library, PianoBankAsset bank)
        {
            Configure(target, ConfigurationFor(library, bank));
        }

        public static void Configure(GameObject target, MidiLibraryAsset library, PianoBankAsset bank, GameObject pianoModel)
        {
            MidiPlayerConfigurationAsset configuration = ConfigurationFor(library, bank);
            configuration.pianoModel = pianoModel;
            EditorUtility.SetDirty(configuration); AssetDatabase.SaveAssetIfDirty(configuration);
            Configure(target, configuration);
        }

        private static MidiPlayerConfigurationAsset ConfigurationFor(MidiLibraryAsset library, PianoBankAsset bank)
        {
            if (library == null || bank == null) throw new ArgumentException("请选择曲库与钢琴音源");
            string libraryGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(library));
            string bankGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(bank));
            if (string.IsNullOrEmpty(libraryGuid) || string.IsNullOrEmpty(bankGuid)) throw new InvalidOperationException("曲库与音源必须先保存为资产");
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Editor/Generated/Configurations");
            string configPath = "Assets/UdonMidiPlayer/Editor/Generated/Configurations/" + libraryGuid + "_" + bankGuid + ".asset";
            MidiPlayerConfigurationAsset configuration = LoadOrCreate<MidiPlayerConfigurationAsset>(configPath);
            configuration.library = library; configuration.bank = bank;
            EditorUtility.SetDirty(configuration); AssetDatabase.SaveAssetIfDirty(configuration);
            return configuration;
        }

        public static void Configure(GameObject target, MidiPlayerConfigurationAsset configuration)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("播放模式下不能重烘焙播放器");
            if (target == null || configuration == null) throw new ArgumentNullException("请选择播放器根对象和配置");
            if (EditorUtility.IsPersistent(target)) throw new InvalidOperationException("请配置场景中的实例或使用 Build Demo 更新标准 Prefab");
            MidiPlayerFacade priorFacade = target.GetComponentInChildren<MidiPlayerFacade>(true);
            if (priorFacade == null && (target.name != "UdonMidiPlayer" || target.transform.childCount != 0))
                throw new InvalidOperationException("目标不是播放器根对象；请使用空的 UdonMidiPlayer 对象或既有播放器实例");
            if (priorFacade != null && priorFacade.transform != target.transform.Find("Runtime/Facade"))
                throw new InvalidOperationException("请选择完整播放器根对象，不能覆盖包含其他播放器的上层对象");
            MidiDataBaker.ValidateLibrary(configuration.library);
            string requestedDefault = string.IsNullOrEmpty(configuration.defaultSongId) ? configuration.library.defaultSongId : configuration.defaultSongId;
            if (!string.IsNullOrEmpty(requestedDefault))
            {
                bool foundDefault = false;
                foreach (MidiSongAsset source in configuration.library.songs) if (source.songId == requestedDefault) foundDefault = true;
                if (!foundDefault) throw new InvalidOperationException("配置默认曲目不在曲库内");
            }
            EnsureBankPrepared(configuration.bank);
            if (configuration.voiceLimit < 1 || configuration.voiceLimit > PianoVoicePool.VoiceCapacity || configuration.visibleNoteLimit < 1 || configuration.visibleNoteLimit > 512
                || configuration.windowMs < 1 || float.IsNaN(configuration.viewHeight) || float.IsInfinity(configuration.viewHeight) || configuration.viewHeight <= 0)
                throw new InvalidOperationException("配置超出 MVP 池/显示范围：voices 1～64，notes 1～512，窗口与高度须为正");
            foreach (MidiSongAsset song in configuration.library.songs)
                if ((long)song.durationMs + configuration.bank.maxReleaseMs > int.MaxValue) throw new InvalidOperationException("曲目加尾音时长溢出: " + song.title);
            EnsureFont();
            EnsurePrograms();
            Undo.RegisterFullObjectHierarchyUndo(target, "Configure MIDI Player");
            if (priorFacade != null && priorFacade.instrumentRoots != null && priorFacade.instrumentRoots.Length >= 3)
            {
                foreach (var view in priorFacade.instrumentWaterfalls)
                {
                    if (view.barTransforms.Length != configuration.visibleNoteLimit)
                        throw new InvalidOperationException("合并乐器的音符池请在各视图中配置；本次保留既有模型与资源");
                    view.windowMs = configuration.windowMs; view.viewHeight = configuration.viewHeight;
                    UdonSharpEditorUtility.CopyProxyToUdon(view, ProxySerializationPolicy.All);
                }
                ConfigureVoicePool(priorFacade.voicePool, configuration.voiceLimit);
                priorFacade.library.configurationAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(configuration));
                MidiInstrumentMigration.ApplyToneChangesTo(priorFacade);
                SynchronizeLibrary(priorFacade, configuration);
                EditorSceneManager.MarkSceneDirty(target.scene);
                return;
            }
            Transform dataRoot = Child(target.transform, "Data");
            Transform runtimeRoot = Child(target.transform, "Runtime");
            Transform networkRoot = Child(target.transform, "Network");
            MidiLibraryData libraryData = Behaviour<MidiLibraryData>(dataRoot, "Library");
            PianoBankData bankData = Behaviour<PianoBankData>(dataRoot, "PianoBank");
            MidiDataBaker.CopyBankToRuntime(configuration.bank, bankData);
            var songData = new MidiSongData[configuration.library.songs.Length];
            var wantedSongs = new HashSet<string>();
            for (int i = 0; i < songData.Length; i++)
            {
                MidiSongAsset song = configuration.library.songs[i];
                string childName = "Song_" + song.songId;
                wantedSongs.Add(childName);
                songData[i] = Behaviour<MidiSongData>(dataRoot, childName);
                MidiDataBaker.CopySongToRuntime(song, songData[i]);
            }
            RemoveStaleSongObjects(dataRoot, wantedSongs);
            libraryData.songs = songData; libraryData.bank = bankData;
            libraryData.catalogHash = MidiDataBaker.ComputeCatalogHash(configuration.library, configuration.bank);
            libraryData.configurationAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(configuration));
            string defaultId = string.IsNullOrEmpty(configuration.defaultSongId) ? configuration.library.defaultSongId : configuration.defaultSongId;
            bool found = false;
            foreach (MidiSongData song in songData) if (song.songId == defaultId) found = true;
            if (!found && !string.IsNullOrEmpty(defaultId)) throw new InvalidOperationException("配置默认曲目不在曲库内");
            libraryData.defaultSongId = string.IsNullOrEmpty(defaultId) ? songData[0].songId : defaultId;

            MidiPlayerFacade facade = Behaviour<MidiPlayerFacade>(runtimeRoot, "Facade");
            MidiControlGate gate = Behaviour<MidiControlGate>(runtimeRoot, "ControlGate");
            MidiTransportClock clock = Behaviour<MidiTransportClock>(runtimeRoot, "Clock");
            MidiSequencer sequencer = Behaviour<MidiSequencer>(runtimeRoot, "Sequencer");
            PianoVoicePool voices = Behaviour<PianoVoicePool>(runtimeRoot, "Audio");
            MidiNetworkTransport network = Behaviour<MidiNetworkTransport>(networkRoot, "Transport");
            facade.library = libraryData; facade.gate = gate; facade.clock = clock; facade.sequencer = sequencer;
            facade.voicePool = voices; facade.network = network; gate.facade = facade;
            VRC.SDK3.Midi.VRCMidiListener midiListener = Component<VRC.SDK3.Midi.VRCMidiListener>(facade.gameObject);
            midiListener.activeEvents = VRC.SDK3.Midi.VRCMidiListener.MidiEvents.NoteOn
                | VRC.SDK3.Midi.VRCMidiListener.MidiEvents.NoteOff | VRC.SDK3.Midi.VRCMidiListener.MidiEvents.CC;
            var midiSerialized = new SerializedObject(midiListener);
            midiSerialized.FindProperty("behaviour").objectReferenceValue = UdonSharpEditorUtility.GetBackingUdonBehaviour(facade);
            midiSerialized.ApplyModifiedPropertiesWithoutUndo();
            network.facade = facade; network.clock = clock; network.library = libraryData; network.gate = gate;
            network.catalogHash = libraryData.catalogHash; network.songId = libraryData.defaultSongId; network.state = 0;
            network.anchorPositionSeconds = 0; network.anchorServerSeconds = 0; network.writerPlayerId = 0; network.snapshotSequence = 0;
            voices.bank = bankData; voices.clock = clock; voices.voiceLimit = configuration.voiceLimit;
            voices.localVolume = 0.65f; voices.muted = false; voices.worldSpaceAudio = true;
            ConfigureVoicePool(voices, configuration.voiceLimit);
            sequencer.voicePool = voices;
            BuildViews(target.transform, facade, sequencer, configuration);
            BuildUI(target.transform, facade, configuration);
            MidiPerformanceBuilder.Configure(facade);
            UdonSharpEditorUtility.CopyProxyToUdon(facade, ProxySerializationPolicy.All);
            foreach (UdonSharpBehaviour behaviour in target.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                EditorUtility.SetDirty(behaviour);
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
                if (backing != null)
                {
                    backing.SyncMethod = behaviour == network ? VRC.SDKBase.Networking.SyncType.Manual : VRC.SDKBase.Networking.SyncType.None;
                    EditorUtility.SetDirty(backing);
                    if (PrefabUtility.IsPartOfPrefabInstance(backing)) PrefabUtility.RecordPrefabInstancePropertyModifications(backing);
                }
                if (PrefabUtility.IsPartOfPrefabInstance(behaviour)) PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            }
            EditorUtility.SetDirty(target);
            if (target.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.scene);
            AssetDatabase.SaveAssetIfDirty(configuration.bank);
        }

        public static void EnsurePrograms()
        {
            const string runtimeFolder = "Assets/UdonMidiPlayer/Runtime";
            const string assemblyPath = runtimeFolder + "/MusicMidiPlayer.Runtime.asmdef";
            const string registrationPath = runtimeFolder + "/MusicMidiPlayer.Runtime.UdonSharp.asset";
            AssemblyDefinitionAsset assembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(assemblyPath);
            if (assembly == null) throw new InvalidOperationException("找不到Runtime程序集资产: " + assemblyPath);
            UdonSharpAssemblyDefinition registration = AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(registrationPath);
            if (registration == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(registrationPath) != null) throw new InvalidOperationException("U#程序集注册路径已被其他资产占用");
                registration = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                registration.sourceAssembly = assembly;
                AssetDatabase.CreateAsset(registration, registrationPath);
            }
            else registration.sourceAssembly = assembly;
            EditorUtility.SetDirty(registration);
            AssetDatabase.SaveAssetIfDirty(registration);
            AssetDatabase.ImportAsset(registrationPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            UdonSharpEditorUtility.ResetAssemblyCaches();
            var programs = new List<UdonSharpProgramAsset>();
            var types = new List<Type>();
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { runtimeFolder }))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                Type scriptType = script == null ? null : script.GetClass();
                if (scriptType == null || scriptType.IsAbstract || !scriptType.IsSubclassOf(typeof(UdonSharpBehaviour))) continue;
                string programPath = Path.ChangeExtension(scriptPath, ".asset").Replace('\\', '/');
                UdonSharpProgramAsset program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
                if (program == null)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(programPath) != null) throw new InvalidOperationException("U#程序资产路径已被其他类型占用: " + programPath);
                    // Respect an explicitly moved program asset instead of creating a duplicate binding.
                    foreach (string programGuid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset", new[] { runtimeFolder }))
                    {
                        UdonSharpProgramAsset candidate = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(programGuid));
                        if (candidate != null && candidate.sourceCsScript == script) { program = candidate; break; }
                    }
                }
                if (program == null)
                {
                    program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    program.sourceCsScript = script;
                    program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                    AssetDatabase.CreateAsset(program, programPath);
                }
                else
                {
                    if (program.sourceCsScript != null && program.sourceCsScript != script)
                        throw new InvalidOperationException("U#程序资产已绑定另一源码: " + AssetDatabase.GetAssetPath(program));
                    program.sourceCsScript = script;
                    program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                    EditorUtility.SetDirty(program);
                }
                programs.Add(program); types.Add(scriptType);
            }
            if (programs.Count == 0) throw new InvalidOperationException("未找到已编译的播放器 UdonSharp 运行脚本，请先修复 Unity C# 编译错误");
            foreach (UdonSharpProgramAsset program in programs) AssetDatabase.SaveAssetIfDirty(program);
            // Public import APIs run the SDK postprocessor, which invalidates its program lookup caches.
            foreach (UdonSharpProgramAsset program in programs)
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(program), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions
            {
                IsEditorBuild = true,
                CurrentBuildTarget = EditorUserBuildSettings.activeBuildTarget
            });
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                throw new InvalidOperationException("UdonSharp 编译失败，已停止生成播放器；请查看 Unity Console 的源码诊断");
            for (int i = 0; i < programs.Count; i++)
                if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(types[i]) == null
                    || programs[i].CompiledVersion != UdonSharpProgramVersion.CurrentVersion
                    || programs[i].fieldDefinitions == null || programs[i].fieldDefinitions.Count == 0
                    || programs[i].SerializedProgramAsset == null || programs[i].SerializedProgramAsset.RetrieveProgram() == null)
                    throw new InvalidOperationException("U#程序未完成注册/序列化，禁止添加Behaviour: " + types[i].FullName);
            // Compilation marks the executable program assets dirty; persist them before returning.
            foreach (UdonSharpProgramAsset program in programs)
            {
                AssetDatabase.SaveAssetIfDirty(program);
                AssetDatabase.SaveAssetIfDirty(program.SerializedProgramAsset);
            }
        }

        private static void BuildViews(Transform root, MidiPlayerFacade facade, MidiSequencer sequencer, MidiPlayerConfigurationAsset config)
        {
            Transform presentation = Child(root, "Presentation");
            Transform viewRoot = Child(presentation, "ViewRoot");
            viewRoot.localPosition = new Vector3(0, 1.4f, 0);
            PianoKeyboardView keyboard = Behaviour<PianoKeyboardView>(viewRoot, "Keyboard");
            MidiWaterfallView waterfall = Behaviour<MidiWaterfallView>(viewRoot, "Waterfall");
            PianoPedalView pedals = Behaviour<PianoPedalView>(presentation, "Pedals");
            facade.keyboard = keyboard; facade.waterfall = waterfall; keyboard.sequencer = sequencer;
            facade.pedals = pedals; waterfall.keyboard = keyboard;
            keyboard.whiteMaterial = MaterialAsset("PianoWhite", new Color(0.92f, 0.96f, 1));
            keyboard.blackMaterial = MaterialAsset("PianoBlack", new Color(0.025f, 0.035f, 0.055f));
            keyboard.whitePressedMaterial = MaterialAsset("PianoWhitePressed", new Color(0.16f, 0.9f, 1));
            keyboard.blackPressedMaterial = MaterialAsset("PianoBlackPressed", new Color(0.1f, 0.48f, 1));
            waterfall.whiteNoteMaterial = MaterialAsset("NoteWhite", new Color(0.17f, 0.72f, 1));
            waterfall.blackNoteMaterial = MaterialAsset("NoteBlack", new Color(0.62f, 0.33f, 1));
            keyboard.keyTransforms = new Transform[88]; keyboard.keyRenderers = new Renderer[88]; keyboard.keyIsBlack = new bool[88];
            waterfall.keyX = new float[88]; waterfall.keyWidth = new float[88]; waterfall.keyIsBlack = new bool[88];
            float whiteWidth = 3.6f / 52;
            int whiteIndex = 0;
            if (config.pianoModel != null)
            {
                MidiPianoModelBinder.Bind(presentation, viewRoot, keyboard, waterfall, config.pianoModel);
                MidiPianoModelBinder.BindPedals(presentation, pedals);
            }
            else
            {
                Transform priorModel = presentation.Find("PianoModel"); if (priorModel != null) priorModel.gameObject.SetActive(false);
                keyboard.keyRestMaterials = new Material[88]; keyboard.keyPressOffsets = new Vector3[88];
                keyboard.keyPivotPositions = new Vector3[0]; keyboard.keyRotationAxes = new Vector3[0]; keyboard.keyPressAngles = new float[0];
                pedals.pedalTransforms = new Transform[0]; pedals.pedalPivotPositions = new Vector3[0];
                pedals.pedalRotationAxes = new Vector3[0]; pedals.pedalPressAngles = new float[0];
                for (int i = 0; i < 88; i++)
                {
                    int pitch = i + 21, pc = pitch % 12;
                    bool black = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                    float x = ((black ? whiteIndex : whiteIndex + 0.5f) - 26) * whiteWidth;
                    float width = whiteWidth * (black ? 0.62f : 1);
                    Material rest = black ? keyboard.blackMaterial : keyboard.whiteMaterial;
                    Transform key = Cube(keyboard.transform, "Key_" + pitch, rest);
                    key.gameObject.SetActive(true);
                    key.localPosition = new Vector3(x, black ? 0.014f : -0.009f, black ? -0.11f : -0.17f);
                    key.localScale = new Vector3(width * 0.95f, black ? 0.028f : 0.018f, black ? 0.22f : 0.34f);
                    keyboard.keyTransforms[i] = key; keyboard.keyRenderers[i] = key.GetComponent<Renderer>(); keyboard.keyIsBlack[i] = black;
                    keyboard.keyRestMaterials[i] = rest; keyboard.keyPressOffsets[i] = Vector3.down * 0.0015f;
                    waterfall.keyX[i] = x; waterfall.keyWidth[i] = width; waterfall.keyIsBlack[i] = black;
                    if (!black) whiteIndex++;
                }
            }
            waterfall.whiteHitMaterial = keyboard.whitePressedMaterial; waterfall.blackHitMaterial = keyboard.blackPressedMaterial;
            keyboard.keyPressDepth = 0.0015f;
            waterfall.windowMs = config.windowMs; waterfall.viewHeight = config.viewHeight;
            waterfall.barDepth = 0.008f; waterfall.blackBarZ = -0.012f; waterfall.minimumBarHeight = 0.025f;
            waterfall.barTransforms = new Transform[config.visibleNoteLimit]; waterfall.barRenderers = new Renderer[config.visibleNoteLimit];
            for (int i = 0; i < 512; i++)
            {
                Transform bar = Cube(waterfall.transform, "Note_" + i.ToString("000"), waterfall.whiteNoteMaterial);
                bar.localPosition = Vector3.zero; bar.localScale = new Vector3(whiteWidth * 0.92f, 0.03f, waterfall.barDepth);
                bar.gameObject.SetActive(false);
                if (i < config.visibleNoteLimit) { waterfall.barTransforms[i] = bar; waterfall.barRenderers[i] = bar.GetComponent<Renderer>(); }
            }
            Transform back = Cube(viewRoot, "WaterfallBackdrop", MaterialAsset("Backdrop", new Color(0.018f, 0.027f, 0.05f)));
            back.localPosition = new Vector3(0, config.viewHeight * 0.5f, 0.05f); back.localScale = new Vector3(3.72f, config.viewHeight, 0.025f);
            back.gameObject.SetActive(config.pianoModel == null);
            MidiWaterfallPresentationBuilder.Build(waterfall);
        }

        private static void BuildUI(Transform root, MidiPlayerFacade facade, MidiPlayerConfigurationAsset config, bool preservePlacement = false)
        {
            MidiPlayerUIBuilder.Build(root, facade, config, font, preservePlacement);
        }
        private static void BuildDemoScene()
        {
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Samples");
            Scene original = SceneManager.GetActiveScene();
            Scene demo = SceneManager.GetSceneByPath(DemoScenePath);
            bool wasOpen = demo.IsValid() && demo.isLoaded;
            try
            {
                if (!wasOpen) demo = File.Exists(DemoScenePath) ? EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(demo);
                GameObject player = FindRoot(demo, "UdonMidiPlayer");
                if (player == null) player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), demo);
                player.name = "UdonMidiPlayer";
                GameObject world = Root(demo, "MIDI Demo World");
                VRCSceneDescriptor descriptor = Component<VRCSceneDescriptor>(world);
                Transform spawn = Child(world.transform, "Spawn"); spawn.localPosition = new Vector3(0, 0, -3.5f);
                descriptor.spawns = new[] { spawn }; descriptor.RespawnHeightY = -10;
                Transform floor = Cube(world.transform, "Floor", MaterialAsset("Floor", new Color(0.07f, 0.085f, 0.12f)));
                floor.localPosition = new Vector3(0, -0.08f, 0); floor.localScale = new Vector3(20, 0.16f, 20);
                Component<BoxCollider>(floor.gameObject);
                Transform cameraHost = Child(world.transform, "Preview Camera");
                Camera camera = Component<Camera>(cameraHost.gameObject); cameraHost.tag = "MainCamera";
                cameraHost.localPosition = new Vector3(0, 3.7f, -8.4f); cameraHost.LookAt(new Vector3(0, 3.7f, 0));
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.018f, 0.025f, 0.05f); camera.fieldOfView = 60;
                Component<AudioListener>(cameraHost.gameObject); descriptor.ReferenceCamera = camera.gameObject;
                Transform lightHost = Child(world.transform, "Light"); Light light = Component<Light>(lightHost.gameObject);
                light.cullingMask &= ~LayerMask.GetMask("MidiNotes");
                light.enabled = false; light.shadows = UnityEngine.LightShadows.None;
                light.type = LightType.Directional; light.intensity = 0.8f; lightHost.localRotation = Quaternion.Euler(45, -30, 0);
                GameObject events = Root(demo, "EventSystem"); Component<EventSystem>(events); Component<StandaloneInputModule>(events);
                EditorSceneManager.MarkSceneDirty(demo);
                if (!EditorSceneManager.SaveScene(demo, DemoScenePath)) throw new InvalidOperationException("示例场景保存失败");
            }
            finally
            {
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (!wasOpen && demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
            }
        }

        private static Transform Child(Transform parent, string name, bool rect = false)
        {
            Transform found = parent.Find(name);
            if (found != null) { if (rect && !(found is RectTransform)) throw new InvalidOperationException("生成 UI 对象类型不匹配: " + name); return found; }
            GameObject created = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create MIDI Player Object");
            created.transform.SetParent(parent, false); return created.transform;
        }

        private static T Behaviour<T>(Transform parent, string name) where T : UdonSharpBehaviour
        {
            GameObject host = Child(parent, name).gameObject;
            T value = host.GetComponent<T>(); return value != null ? value : host.AddUdonSharpComponent<T>();
        }
        private static T Component<T>(GameObject host) where T : UnityEngine.Component
        { T value = host.GetComponent<T>(); return value != null ? value : Undo.AddComponent<T>(host); }

        private static Transform Cube(Transform parent, string name, Material material)
        {
            Transform result = parent.Find(name);
            if (result == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = name;
                Undo.RegisterCreatedObjectUndo(cube, "Create MIDI Player Geometry"); cube.transform.SetParent(parent, false); result = cube.transform;
                var collider = cube.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            }
            Renderer renderer = result.GetComponent<Renderer>();
            if (renderer == null) throw new InvalidOperationException("生成几何缺少 Renderer: " + name);
            renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return result;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            MidiImportService.EnsureFolder("Assets/UdonMidiPlayer/Materials");
            string path = "Assets/UdonMidiPlayer/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("缺少内置 Unlit/Color Shader");
                material = new Material(shader); material.name = name; AssetDatabase.CreateAsset(material, path);
            }
            if (name == "PianoWhitePressed" || name == "PianoBlackPressed")
            {
                Shader pressedShader = Shader.Find("MusicMidiPlayer/PianoKeyBreathing");
                if (pressedShader == null) throw new InvalidOperationException("缺少 PianoKeyBreathing Shader");
                material.shader = pressedShader;
            }
            material.color = color; EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
        }

        private static RectTransform Rect(Transform transform, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            RectTransform rect = Rect(Child(parent, name, true), position, size);
            TextMeshProUGUI label = Component<TextMeshProUGUI>(rect.gameObject);
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis; label.raycastTarget = false;
            return label;
        }

        private static Button Button(Transform parent, string name, string text, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(Child(parent, name, true), position, size);
            Image image = Component<Image>(rect.gameObject); image.color = color;
            Button button = Component<Button>(rect.gameObject); button.targetGraphic = image;
            TMP_Text label = Label(rect, "Label", text, new Vector2(-size.x * 0.5f + 18, 0), new Vector2(size.x - 36, size.y), 20, Color.white);
            if (name == "Play" || name == "Pause" || name == "Stop") label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static Slider Slider(Transform parent, string name, Vector2 position, Vector2 size, bool handle)
        {
            RectTransform rect = Rect(Child(parent, name, true), position, size);
            Image back = Component<Image>(rect.gameObject); back.color = new Color(0.1f, 0.17f, 0.25f);
            Slider slider = Component<Slider>(rect.gameObject); slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
            RectTransform fill = (RectTransform)Child(rect, "Fill", true);
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
            Image fillImage = Component<Image>(fill.gameObject); fillImage.color = new Color(0.07f, 0.69f, 0.88f); fillImage.raycastTarget = false;
            slider.fillRect = fill; slider.targetGraphic = back; slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            if (handle)
            {
                RectTransform thumb = (RectTransform)Child(rect, "Handle", true); thumb.sizeDelta = new Vector2(20, size.y + 10);
                Image thumbImage = Component<Image>(thumb.gameObject); thumbImage.color = Color.white;
                slider.handleRect = thumb; slider.targetGraphic = thumbImage;
            }
            return slider;
        }

        private static void Listen(UnityEventBase targetEvent, UdonSharpBehaviour receiver, string eventName)
        {
            while (targetEvent.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(targetEvent, 0);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
            if (backing == null) throw new InvalidOperationException("UI 回调没有 Udon backing behaviour: " + receiver.name);
            UnityEventTools.AddStringPersistentListener(targetEvent, backing.SendCustomEvent, eventName);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T result = AssetDatabase.LoadAssetAtPath<T>(path);
            if (result != null) return result;
            MidiImportService.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            result = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(result, path); return result;
        }

        private static void EnsureBankPrepared(PianoBankAsset bank)
        {
            if (bank == null) throw new InvalidOperationException("请选择有效钢琴音源");
            if (string.IsNullOrEmpty(bank.bankHash) || bank.energyStarts == null || bank.energyStarts.Length != bank.clips.Length) MidiDataBaker.PrepareBank(bank);
            else MidiDataBaker.ValidateBank(bank);
        }

        private static void RemoveStaleSongObjects(Transform dataRoot, HashSet<string> wanted)
        {
            for (int i = dataRoot.childCount - 1; i >= 0; i--)
            {
                Transform item = dataRoot.GetChild(i);
                if (item.name.StartsWith("Song_", StringComparison.Ordinal) && !wanted.Contains(item.name) && item.GetComponent<MidiSongData>() != null)
                    Undo.DestroyObjectImmediate(item.gameObject);
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        { foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root; return null; }
        private static GameObject Root(Scene scene, string name)
        {
            GameObject value = FindRoot(scene, name);
            if (value != null) return value;
            value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); return value;
        }

        private static void EnsureFont()
        {
            if (TryLoadFont()) return;
            throw new InvalidOperationException("TMP资源尚未导入完成。菜单与配置窗口会自动准备资源；脚本调用请先使用PrepareResources或BuildDemoAsync完成资源准备。");
        }

        private static bool TryLoadFont()
        {
            // TMP_Settings.instance opens a window if settings are absent. Never invoke that fallback.
            TMP_Settings settings = TMP_Settings.LoadDefaultSettings();
            if (settings == null) return false;
            font = MidiPlayerFontAssets.EnsureFont();
            return font != null;
        }

        public static void PrepareResources(Action onReady, Action<Exception> onError)
        {
            if (TryLoadFont())
            {
                try { if (onReady != null) onReady(); }
                catch (Exception exception) { if (onError != null) onError(exception); else Debug.LogException(exception); }
                return;
            }
            resourceReadyCallbacks.Add(onReady); resourceErrorCallbacks.Add(onError);
            if (resourceImportPending) return;
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                string resources = package == null ? "" : Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage");
                if (!File.Exists(resources)) throw new InvalidOperationException("本地TMP包没有Essential Resources，无法自动准备字体");
                resourceImportPending = true;
                resourceImportDeadline = EditorApplication.timeSinceStartup + 90;
                AssetDatabase.importPackageCompleted += OnResourcesImported;
                AssetDatabase.importPackageFailed += OnResourcesImportFailed;
                AssetDatabase.importPackageCancelled += OnResourcesImportCancelled;
                AssetDatabase.ImportPackage(resources, false);
                QueueResourcesCheck();
            }
            catch (Exception exception) { CompleteResources(exception); }
        }

        private static void OnResourcesImported(string packageName) { QueueResourcesCheck(); }
        private static void OnResourcesImportFailed(string packageName, string error)
        {
            if (packageName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) >= 0) CompleteResources(new InvalidOperationException("TMP资源导入失败: " + error));
        }
        private static void OnResourcesImportCancelled(string packageName)
        {
            if (packageName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) >= 0) CompleteResources(new OperationCanceledException("TMP资源导入已取消"));
        }
        private static void QueueResourcesCheck()
        {
            if (!resourceImportPending || resourceCheckQueued) return;
            resourceCheckQueued = true; EditorApplication.delayCall += CheckResources;
        }
        private static void CheckResources()
        {
            resourceCheckQueued = false;
            if (!resourceImportPending) return;
            if (TryLoadFont()) { CompleteResources(null); return; }
            if (EditorApplication.timeSinceStartup >= resourceImportDeadline)
            { CompleteResources(new InvalidOperationException("等待TMP资源导入超时，未生成任何成功报告")); return; }
            QueueResourcesCheck();
        }
        private static void CompleteResources(Exception error)
        {
            resourceImportPending = false; resourceCheckQueued = false;
            AssetDatabase.importPackageCompleted -= OnResourcesImported;
            AssetDatabase.importPackageFailed -= OnResourcesImportFailed;
            AssetDatabase.importPackageCancelled -= OnResourcesImportCancelled;
            EditorApplication.delayCall -= CheckResources;
            Action[] ready = resourceReadyCallbacks.ToArray();
            Action<Exception>[] failed = resourceErrorCallbacks.ToArray();
            resourceReadyCallbacks.Clear(); resourceErrorCallbacks.Clear();
            for (int i = 0; i < ready.Length; i++)
            {
                try
                {
                    if (error != null) { if (failed[i] != null) failed[i](error); else Debug.LogException(error); }
                    else if (ready[i] != null) ready[i]();
                }
                catch (Exception exception) { if (failed[i] != null) failed[i](exception); else Debug.LogException(exception); }
            }
        }
    }
}
