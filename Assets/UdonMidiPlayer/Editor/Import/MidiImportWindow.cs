using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Prefab;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Import
{
    public sealed class MidiImportWindow : global::UnityEditor.EditorWindow
    {
        private readonly List<string> files = new List<string>();
        private readonly List<string> messages = new List<string>();
        private string outputFolder = MidiImportService.DefaultOutputFolder;
        private string selectedTracks = "";
        private bool excludePercussion;
        [SerializeField] private MidiLibraryAsset library;
        private Vector2 scroll;

        [MenuItem("Tools/Music MIDI Player/Import MIDI")]
        public static void Open()
        {
            var window = GetWindow<MidiImportWindow>(MidiEditorI18n.Tr("Import MIDI"));
            if (window.library == null) window.library = MidiPlayerBuilder.FindSceneLibrary();
        }

        private void OnGUI()
        {
            titleContent = MidiEditorI18n.Content("Import MIDI");
            MidiEditorI18n.DrawLanguageSelector();
            EditorGUILayout.LabelField(MidiEditorI18n.Tr("Batch import MIDI"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(MidiEditorI18n.Tr("Reads SMF 0/1 with PPQ from Assets, preserving MIDI pitches 0–127. Notes beyond the 88-key range extend the keyboard pattern and use pitch-shifted playback. Reimport after changing tracks or updating a song to preserve asset references."), MessageType.Info);
            library = (MidiLibraryAsset)EditorGUILayout.ObjectField(MidiEditorI18n.Tr("Add to library and sync scene"), library, typeof(MidiLibraryAsset), false);
            EditorGUILayout.HelpBox(MidiEditorI18n.Tr("After import or reimport, players and song menus using this library in loaded scenes are synchronized automatically. Save the scenes afterward. Leave empty to create song assets only."), MessageType.Info);
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            if (playing) EditorGUILayout.HelpBox(MidiEditorI18n.Tr("Exit Play Mode before importing or reimporting MIDI."), MessageType.Warning);
            outputFolder = EditorGUILayout.TextField(MidiEditorI18n.Tr("Output asset folder"), outputFolder);
            selectedTracks = EditorGUILayout.TextField(MidiEditorI18n.Tr("Track indices (empty = all)"), selectedTracks);
            excludePercussion = EditorGUILayout.Toggle(MidiEditorI18n.Tr("Exclude channel 10 percussion"), excludePercussion);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(MidiEditorI18n.Tr("Add files"))) AddFileDialog();
            if (GUILayout.Button(MidiEditorI18n.Tr("Add folder"))) AddFolderDialog();
            if (GUILayout.Button(MidiEditorI18n.Tr("Add Project selection"))) AddSelection();
            if (GUILayout.Button(MidiEditorI18n.Tr("Clear"))) { files.Clear(); messages.Clear(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(files.Count == 0))
            {
                if (GUILayout.Button(MidiEditorI18n.Tr("Preview and diagnostics"))) Process(false);
            }
            using (new EditorGUI.DisabledScope(files.Count == 0 || playing))
            {
                if (GUILayout.Button(MidiEditorI18n.Tr("Import / update songs"))) Process(true);
            }
            using (new EditorGUI.DisabledScope(playing))
            {
                if (GUILayout.Button(MidiEditorI18n.Tr("Reimport selected song assets"))) ReimportSelection();
            }
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(MidiEditorI18n.Format("Queued files: {0}", files.Count));
            foreach (string path in files) EditorGUILayout.LabelField(path);
            foreach (string message in messages) EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void AddFileDialog()
        {
            string path = EditorUtility.OpenFilePanel(MidiEditorI18n.Tr("Select MIDI in Assets"), Application.dataPath, "mid,midi");
            if (!string.IsNullOrEmpty(path)) AddPath(path);
        }

        private void AddFolderDialog()
        {
            string folder = EditorUtility.OpenFolderPanel(MidiEditorI18n.Tr("Select a folder in Assets"), Application.dataPath, "");
            if (!string.IsNullOrEmpty(folder)) AddPath(folder);
        }

        private void AddSelection()
        {
            foreach (UnityEngine.Object item in Selection.objects)
            {
                var song = item as MidiSongAsset;
                if (song != null)
                {
                    string source = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
                    if (string.IsNullOrEmpty(source)) source = song.sourcePath;
                    AddPath(source);
                }
                else AddPath(AssetDatabase.GetAssetPath(item));
            }
        }

        private void AddPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        if (IsMidi(file)) AddPath(file);
                    return;
                }
                if (!IsMidi(path)) return;
                string assetPath = MidiImportService.ToAssetPath(path);
                if (!files.Contains(assetPath)) files.Add(assetPath);
                files.Sort(StringComparer.Ordinal);
            }
            catch (Exception exception) { messages.Add(MidiEditorI18n.LocalizeDiagnostic(exception.Message)); }
        }

        private static bool IsMidi(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
        }

        private MidiImportSettings Settings()
        {
            var settings = new MidiImportSettings { excludePercussion = excludePercussion };
            if (!string.IsNullOrWhiteSpace(selectedTracks))
            {
                var tracks = new SortedSet<int>();
                foreach (string item in selectedTracks.Split(new[] { ',', ';', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int value;
                    if (!int.TryParse(item, out value) || value < 0) throw new ArgumentException(MidiEditorI18n.Tr("Track indices must be comma-separated non-negative integers."));
                    tracks.Add(value);
                }
                settings.selectedTracks = new List<int>(tracks).ToArray();
            }
            return settings;
        }

        private void Process(bool commit)
        {
            messages.Clear();
            if (commit && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                messages.Add(MidiEditorI18n.Tr("Exit Play Mode before importing or reimporting MIDI."));
                return;
            }
            MidiImportSettings settings;
            try { settings = Settings(); }
            catch (Exception exception) { messages.Add(MidiEditorI18n.LocalizeDiagnostic(exception.Message)); return; }
            int success = 0, failed = 0;
            foreach (string path in files)
            {
                try
                {
                    if (commit)
                    {
                        MidiSongAsset song = MidiImportService.ImportAsset(path, outputFolder, settings);
                        AddToLibrary(song);
                        messages.Add(path + " → " + AssetDatabase.GetAssetPath(song) + "\n" + Summary(song.startMs.Length, song.durationMs, song.tempoChangeCount,
                            song.maxKeyPolyphony, song.maxActivePolyphony, song.estimatedDataBytes) + "\n" + string.Join("\n", MidiEditorI18n.LocalizeDiagnostics(song.diagnostics)));
                    }
                    else
                    {
                        MidiImportResult result = SmfParser.Parse(File.ReadAllBytes(path), settings);
                        messages.Add(path + "\n" + Summary(result.startMs.Length, result.durationMs, result.tempoChangeCount,
                            result.maxKeyPolyphony, result.maxActivePolyphony, result.estimatedDataBytes)
                            + "\n" + MidiEditorI18n.Format("Tracks: {0}", string.Join(" / ", result.trackNames)) + "\n" + string.Join("\n", MidiEditorI18n.LocalizeDiagnostics(result.diagnostics)));
                    }
                    success++;
                }
                catch (Exception exception) { failed++; messages.Add(path + "\n" + MidiEditorI18n.Format("Failed: {0}", MidiEditorI18n.LocalizeDiagnostic(exception.Message))); }
            }
            messages.Insert(0, MidiEditorI18n.Format("{0} complete: {1} succeeded, {2} failed.", MidiEditorI18n.Tr(commit ? "Import" : "Preview"), success, failed));
            if (commit && success > 0) SynchronizeScenePlayers();
        }

        private void ReimportSelection()
        {
            messages.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                messages.Add(MidiEditorI18n.Tr("Exit Play Mode before importing or reimporting MIDI."));
                return;
            }
            int success = 0;
            foreach (UnityEngine.Object item in Selection.objects)
            {
                var source = item as MidiSongAsset;
                if (source == null) continue;
                try { AddToLibrary(MidiImportService.Reimport(source)); success++; messages.Add(MidiEditorI18n.Format("{0}: updated, preserving songId and asset GUID.", source.title)); }
                catch (Exception exception) { messages.Add(MidiEditorI18n.Format("{0}: failed; previous successful data was preserved. {1}", source.title, MidiEditorI18n.LocalizeDiagnostic(exception.Message))); }
            }
            messages.Insert(0, MidiEditorI18n.Format("Reimported {0} songs.", success));
            if (success > 0) SynchronizeScenePlayers();
        }

        private void SynchronizeScenePlayers()
        {
            if (library == null)
            {
                messages.Add(MidiEditorI18n.Tr("No library selected. Song assets were saved. Select a library and import again to add songs and synchronize scene menus."));
                return;
            }
            try
            {
                int updated = MidiPlayerBuilder.SynchronizeLibraryPlayers(library, message => messages.Add(MidiEditorI18n.LocalizeDiagnostic(message)));
                if (updated > 0) messages.Add(MidiEditorI18n.Format("Synchronized {0} scene players. Save the affected scenes.", updated));
            }
            catch (Exception exception)
            {
                messages.Add(MidiEditorI18n.Format("Songs and library were saved, but scene synchronization failed: {0}", MidiEditorI18n.LocalizeDiagnostic(exception.Message)));
            }
        }

        private void AddToLibrary(MidiSongAsset song)
        {
            if (library == null) return;
            var songs = new List<MidiSongAsset>(library.songs ?? Array.Empty<MidiSongAsset>());
            if (songs.Contains(song)) return;
            Undo.RecordObject(library, "Add MIDI To Library");
            songs.Add(song); library.songs = songs.ToArray();
            if (string.IsNullOrEmpty(library.defaultSongId)) library.defaultSongId = song.songId;
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        }

        private static string Summary(int count, int duration, int tempos, int keys, int active, long bytes)
        {
            return MidiEditorI18n.Format("{0} notes · {1} s · tempo changes {2} · peak keys {3} · peak active (with pedal) {4} · arrays {5} KiB",
                count, (duration / 1000.0).ToString("0.###"), tempos, keys, active, (bytes / 1024.0).ToString("0.0"));
        }
    }
}
