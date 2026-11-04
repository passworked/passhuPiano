// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Editor.Prefab;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace MusicMidiPlayer.Editor.Import
{
    public sealed class MidiImportWindow : global::UnityEditor.EditorWindow
    {
        [Serializable] private sealed class Row
        {
            public string key, title, author, status = "", baseline, rename = "";
            public MidiSongAsset song;
            public MidiLibraryEditing.Input input;
            public MidiImportSettings settings;
            public bool reimport, selected;
            public int events, notes, duration;
            public string[] tracks = Array.Empty<string>();
        }
        [Serializable] private sealed class Folder
        {
            public string path;
            public List<string> keys = new List<string>();
        }
        [SerializeField] private MidiLibraryAsset library;
        [SerializeField] private List<Row> rows = new List<Row>();
        [SerializeField] private List<Folder> folders = new List<Folder>();
        [SerializeField] private int folderIndex = -1;
        [SerializeField] private string activeKey, baseline, defaultId, libraryTitle;
        [SerializeField] private bool dirty, advanced, preserveFolders, preferVersions;
        [SerializeField] private string sourceFolder = MidiLibraryEditing.SourceFolder;
        [SerializeField] private string outputFolder = MidiImportService.DefaultOutputFolder;
        [SerializeField] private string search = "", batchAuthor = "", url = "";
        private string message;
        private MessageType messageType;
        private Vector2 folderScroll, songScroll, detailScroll;
        private ReorderableList folderList, songList;
        private List<Row> visible = new List<Row>();
        private readonly Queue<Row> previewQueue = new Queue<Row>();
        private UnityWebRequest download;
        private string downloadPath;
        private static string T(string text) { return MidiEditorI18n.Tr(text); }

        [MenuItem("Tools/Music MIDI Player/Import MIDI")]
        [MenuItem("Tools/Music MIDI Player/Edit Library")]
        public static void Open()
        {
            var window = GetWindow<MidiImportWindow>();
            window.minSize = new Vector2(900, 600);
            if (window.library == null) window.SetLibrary(MidiPlayerBuilder.FindSceneLibrary());
            window.Show();
        }

        public static void OpenLibrary(MidiLibraryAsset target)
        {
            var window = GetWindow<MidiImportWindow>();
            window.minSize = new Vector2(900, 600);
            if (!window.CanDiscard()) return;
            window.SetLibrary(target); window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += ProcessPreview;
            Undo.undoRedoPerformed += OnUndo;
            BuildLists();
            foreach (var row in rows.Where(r => HasInput(r) || r.reimport)) QueuePreview(row);
        }
        private void OnDisable()
        {
            EditorApplication.update -= ProcessPreview;
            Undo.undoRedoPerformed -= OnUndo;
            if (download != null) { download.Abort(); download.Dispose(); download = null; }
        }
        private void OnUndo() { BuildLists(); Repaint(); }
        private void Record(string name) { Undo.RecordObject(this, name); dirty = true; }
        private bool CanDiscard()
        {
            return !dirty || EditorUtility.DisplayDialog(T("Unsaved library edits"), T("Discard the current library draft?"), T("Discard"), T("Cancel"));
        }

        private void SetLibrary(MidiLibraryAsset target)
        {
            library = target; rows.Clear(); folders.Clear(); previewQueue.Clear();
            folderIndex = -1; activeKey = null; dirty = false;
            baseline = target == null ? "" : EditorJsonUtility.ToJson(target);
            libraryTitle = target == null ? "" : target.title;
            defaultId = target == null ? "" : target.defaultSongId;
            if (target != null)
            {
                foreach (var song in target.songs ?? Array.Empty<MidiSongAsset>())
                    if (song != null) rows.Add(FromSong(song));
                foreach (var folder in target.folders ?? Array.Empty<MidiLibraryFolder>())
                    if (folder != null) folders.Add(new Folder { path = folder.path, keys = (folder.songs ?? Array.Empty<MidiSongAsset>()).Where(s => s != null).Select(s => s.songId).ToList() });
            }
            BuildLists(); Repaint();
        }
        private static Row FromSong(MidiSongAsset song)
        {
            return new Row { key = song.songId, song = song, title = song.title, author = song.author,
                settings = JsonUtility.FromJson<MidiImportSettings>(JsonUtility.ToJson(song.importSettings)),
                baseline = SongStamp(song), notes = song.startMs.Length, duration = song.durationMs,
                tracks = song.trackNames, status = song.importFailed ? song.lastImportError : "" };
        }
        private static string SongStamp(MidiSongAsset song)
        {
            return string.Join("|", song.contentHash, song.title, song.author, song.sourceGuid, song.sourceHash, song.importFailed.ToString(), JsonUtility.ToJson(song.importSettings));
        }
        private static bool HasInput(Row row) { return row.input != null && !string.IsNullOrEmpty(row.input.path); }
        private Row ActiveRow() { return rows.FirstOrDefault(r => r.key == activeKey); }
        private List<Row> ScopeRows()
        {
            if (folderIndex < 0 || folderIndex >= folders.Count) return new List<Row>(rows);
            var byKey = rows.GroupBy(r => r.key).ToDictionary(g => g.Key, g => g.First());
            return folders[folderIndex].keys.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
        }

        private void BuildLists()
        {
            visible = ScopeRows().Where(r => string.IsNullOrWhiteSpace(search) || (r.title + " " + r.author + " " + string.Join(" ", folders.Where(f => f.keys.Contains(r.key)).Select(f => f.path))).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            folderList = new ReorderableList(folders, typeof(Folder), true, true, true, true);
            folderList.drawHeaderCallback = r => EditorGUI.LabelField(r, T("Folders"));
            folderList.drawElementCallback = (r, i, a, f) => EditorGUI.LabelField(r, folders[i].path + "  (" + folders[i].keys.Count + ")");
            folderList.onSelectCallback = l => { folderIndex = l.index; BuildLists(); };
            folderList.onReorderCallbackWithDetails = (l, oldIndex, newIndex) =>
            {
                var moved = folders[newIndex]; folders.RemoveAt(newIndex); folders.Insert(oldIndex, moved);
                Record("Reorder MIDI folders"); folders.RemoveAt(oldIndex); folders.Insert(newIndex, moved); folderIndex = newIndex;
            };
            folderList.onAddCallback = l =>
            {
                Record("Add MIDI folder"); string name = T("New folder"); int suffix = 2; string candidate = name;
                while (folders.Any(f => f.path == candidate)) candidate = name + " " + suffix++;
                folders.Add(new Folder { path = candidate }); folderIndex = folders.Count - 1; BuildLists();
            };
            folderList.onRemoveCallback = l =>
            {
                if (folderIndex < 0 || folderIndex >= folders.Count) return;
                Record("Remove MIDI folder"); folders.RemoveAt(folderIndex); folderIndex = -1; BuildLists();
            };
            folderList.index = folderIndex;
            songList = new ReorderableList(visible, typeof(Row), string.IsNullOrWhiteSpace(search), true, true, true);
            songList.elementHeight = 43;
            songList.drawHeaderCallback = r =>
            {
                EditorGUI.LabelField(new Rect(r.x + 22, r.y, r.width * .59f, r.height), T("Song title") + "  (" + visible.Count + ")");
                EditorGUI.LabelField(new Rect(r.x + 22 + r.width * .59f, r.y, r.width * .38f - 22, r.height), T("Author (optional)"));
            };
            songList.drawElementCallback = DrawRow;
            songList.onSelectCallback = l => { if (l.index >= 0 && l.index < visible.Count) activeKey = visible[l.index].key; };
            songList.onAddDropdownCallback = (r, l) => ShowAddMenu();
            songList.onRemoveCallback = l => RemoveRows(visible.Where(r => r.selected).DefaultIfEmpty(ActiveRow()).Where(r => r != null).ToList());
            songList.onReorderCallbackWithDetails = (l, oldIndex, newIndex) =>
            {
                // visible is a view copy; the authoritative draft still has its old order here.
                Record("Reorder MIDI songs");
                if (folderIndex < 0) rows = new List<Row>(visible);
                else folders[folderIndex].keys = visible.Select(r => r.key).ToList();
            };
            songList.index = visible.FindIndex(r => r.key == activeKey);
        }
        private void DrawRow(Rect rect, int index, bool active, bool focused)
        {
            var row = visible[index]; rect.y += 2;
            row.selected = EditorGUI.Toggle(new Rect(rect.x, rect.y, 18, 18), row.selected);
            float width = rect.width - 28;
            var titleRect = new Rect(rect.x + 22, rect.y, width * .59f, 18);
            var authorRect = new Rect(titleRect.xMax + 6, rect.y, width * .39f - 6, 18);
            EditorGUI.BeginChangeCheck();
            string title = EditorGUI.TextField(titleRect, row.title);
            string author = EditorGUI.TextField(authorRect, row.author);
            if (EditorGUI.EndChangeCheck()) { Record("Edit MIDI metadata"); row.title = title; row.author = author; }
            string state = !string.IsNullOrEmpty(row.status) ? row.status.Split('\n')[0] : row.song == null ? T("New song") : row.reimport ? T("Reimport pending") : "";
            EditorGUI.LabelField(new Rect(rect.x + 22, rect.y + 20, width, 18), row.notes + " " + T("notes") + "  ·  " + (row.duration / 1000f).ToString("0.#") + " s  " + state, EditorStyles.miniLabel);
        }

        private void OnGUI()
        {
            titleContent = new GUIContent(T("MIDI Library") + (dirty ? " *" : ""));
            MidiEditorI18n.DrawLanguageSelector();
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = (MidiLibraryAsset)EditorGUILayout.ObjectField(T("Library"), library, typeof(MidiLibraryAsset), false);
                if (next != library && CanDiscard()) { SetLibrary(next); GUIUtility.ExitGUI(); }
                if (GUILayout.Button(T("Use selected player"), GUILayout.Width(145)))
                {
                    var target = MidiPlayerBuilder.FindSceneLibrary();
                    if (target != null && CanDiscard()) SetLibrary(target);
                    else if (target == null) ShowMessage(T("Select a player in the Hierarchy first."), MessageType.Warning);
                }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(T("Reload"), EditorStyles.toolbarButton, GUILayout.Width(70)) && CanDiscard()) SetLibrary(library);
                using (new EditorGUI.DisabledScope(library == null || EditorApplication.isPlayingOrWillChangePlaymode || previewQueue.Count > 0 || download != null))
                    if (GUILayout.Button(T("Save and apply"), EditorStyles.toolbarButton, GUILayout.Width(120))) Run(SaveAndApply);
                if (GUILayout.Button(T("More"), EditorStyles.toolbarDropDown, GUILayout.Width(70))) ShowMoreMenu();
                GUILayout.FlexibleSpace(); GUILayout.Label(dirty ? T("Unsaved changes") : T("Saved"), EditorStyles.miniLabel);
            }
            if (library == null)
            {
                EditorGUILayout.HelpBox(T("Select a player or library to edit its songs."), MessageType.Info);
                if (GUILayout.Button(T("Create library"))) CreateLibrary();
                return;
            }
            if (baseline == null) SetLibrary(library);
            bool blocked = EditorApplication.isPlayingOrWillChangePlaymode;
            if (blocked) EditorGUILayout.HelpBox(T("Exit Play Mode before importing or reimporting MIDI."), MessageType.Warning);
            using (new EditorGUI.DisabledScope(blocked))
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    EditorGUI.BeginChangeCheck();
                    string title = EditorGUILayout.TextField(libraryTitle);
                    if (EditorGUI.EndChangeCheck()) { Record("Rename MIDI library"); libraryTitle = title; }
                    if (GUILayout.Toggle(folderIndex == -1, T("All songs") + "  (" + rows.Count + ")", "Button") && folderIndex != -1) { folderIndex = -1; BuildLists(); }
                    folderScroll = EditorGUILayout.BeginScrollView(folderScroll); folderList.DoLayoutList(); EditorGUILayout.EndScrollView();
                    if (folderIndex >= 0 && folderIndex < folders.Count)
                    {
                        EditorGUI.BeginChangeCheck(); string path = EditorGUILayout.DelayedTextField(T("Folder path"), folders[folderIndex].path);
                        if (EditorGUI.EndChangeCheck()) Run(() => RenameFolder(path));
                    }
                    EditorGUILayout.HelpBox(T("Use / for subfolders. Removing a folder keeps its songs."), MessageType.None);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    string nextSearch = EditorGUILayout.TextField(T("Search"), search, EditorStyles.toolbarSearchField);
                    if (nextSearch != search) { search = nextSearch; BuildLists(); }
                    songScroll = EditorGUILayout.BeginScrollView(songScroll, GUILayout.MinHeight(130), GUILayout.ExpandHeight(true));
                    songList.DoLayoutList();
                    Rect drop = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true)); GUI.Box(drop, T("Drop MIDI files, folders, ZIPs or song assets here"), EditorStyles.helpBox); HandleDrop(drop);
                    EditorGUILayout.EndScrollView();
                    DrawBatch();
                    detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(215)); DrawDetails(); EditorGUILayout.EndScrollView();
                }
            }
            if (previewQueue.Count > 0) EditorGUILayout.LabelField(T("Reading MIDI") + "  " + previewQueue.Count);
            if (download != null) EditorGUILayout.LabelField(T("Downloading") + "  " + download.downloadedBytes / 1024 + " KiB");
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
        }

        private void DrawBatch()
        {
            var selected = rows.Where(r => r.selected).ToList();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Select all"), GUILayout.Width(85))) foreach (var row in visible) row.selected = true;
                if (GUILayout.Button(T("Deselect"), GUILayout.Width(80))) foreach (var row in rows) row.selected = false;
                using (new EditorGUI.DisabledScope(selected.Count == 0))
                {
                    if (GUILayout.Button(T("Add to folder"))) FolderMenu(selected, false);
                    if (GUILayout.Button(T("Move to folder"))) FolderMenu(selected, true);
                }
            }
            if (selected.Count > 1)
                using (new EditorGUILayout.HorizontalScope())
                {
                    batchAuthor = EditorGUILayout.TextField(T("Author (optional)"), batchAuthor);
                    if (GUILayout.Button(T("Set selected authors"), GUILayout.Width(155))) { Record("Set MIDI authors"); foreach (var r in selected) r.author = batchAuthor; }
                }
        }
        private void DrawDetails()
        {
            var row = ActiveRow();
            if (row == null) { EditorGUILayout.LabelField(T("Select a song to edit source and import options.")); return; }
            EditorGUILayout.LabelField(row.title + (row.key == defaultId ? "  ★" : ""), EditorStyles.boldLabel);
            string sourcePath = HasInput(row) ? row.input.path : MidiLibraryEditing.ResolveSource(row.song);
            EditorGUILayout.LabelField(Path.GetFileName(sourcePath), EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Default song"))) { Record("Set default MIDI song"); defaultId = row.key; }
                if (GUILayout.Button(T("Replace MIDI")))
                {
                    string path = EditorUtility.OpenFilePanel(T("Select MIDI"), "", "mid,midi");
                    if (!string.IsNullOrEmpty(path)) { Record("Replace MIDI source"); row.input = new MidiLibraryEditing.Input { path = path, title = row.title, modifiedTicks = File.GetLastWriteTimeUtc(path).Ticks }; row.reimport = true; QueuePreview(row); }
                }
                if (GUILayout.Button(T("Reimport"))) { Record("Reimport MIDI"); row.reimport = true; QueuePreview(row); }
                if (row.song != null && GUILayout.Button(T("Locate source"))) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(MidiLibraryEditing.ResolveSource(row.song)));
                if (GUILayout.Button(T("Search author"))) Run(() => MidiLibraryEditing.SearchAuthor(row.title));
            }
            EditorGUI.BeginChangeCheck(); bool exclude = EditorGUILayout.Toggle(T("Exclude channel 10 percussion"), row.settings.excludePercussion);
            if (EditorGUI.EndChangeCheck()) { Record("Change MIDI import options"); row.settings.excludePercussion = exclude; row.reimport = true; QueuePreview(row); }
            if (row.tracks != null && row.tracks.Length > 1)
            {
                EditorGUILayout.LabelField(T("Tracks (all enabled by default)"), EditorStyles.miniBoldLabel);
                for (int track = 0; track < row.tracks.Length; track++)
                {
                    bool enabled = row.settings.IncludesTrack(track); bool next = EditorGUILayout.ToggleLeft(track + "  " + row.tracks[track], enabled);
                    if (next == enabled) continue;
                    var tracks = Enumerable.Range(0, row.tracks.Length).Where(row.settings.IncludesTrack).ToList();
                    if (next) tracks.Add(track); else tracks.Remove(track);
                    if (tracks.Count == 0) { ShowMessage(T("Keep at least one track selected."), MessageType.Warning); continue; }
                    Record("Select MIDI tracks"); row.settings.selectedTracks = tracks.Count == row.tracks.Length ? Array.Empty<int>() : tracks.OrderBy(t => t).ToArray(); row.reimport = true; QueuePreview(row);
                }
            }
            advanced = EditorGUILayout.Foldout(advanced, T("Source details"), true);
            if (advanced)
            {
                EditorGUILayout.SelectableLabel(sourcePath, EditorStyles.miniLabel, GUILayout.Height(18));
                EditorGUILayout.LabelField(T("Source events") + ": " + row.events);
                EditorGUI.BeginChangeCheck(); string rename = EditorGUILayout.TextField(T("Rename source on save"), row.rename);
                if (EditorGUI.EndChangeCheck()) { Record("Rename MIDI source"); row.rename = rename; }
                if (GUILayout.Button(T("Use song title as filename"))) { Record("Clean MIDI filename"); row.rename = MidiLibraryEditing.CleanName(row.title); }
                EditorGUILayout.HelpBox(T("Source replacements are backed up. Source renames and file writes are not reverted by Unity Undo."), MessageType.None);
                if (row.song != null) EditorGUILayout.SelectableLabel("songId: " + row.song.songId + "\nSHA-256: " + row.song.sourceHash, EditorStyles.miniLabel, GUILayout.Height(36));
            }
            if (!string.IsNullOrEmpty(row.status)) EditorGUILayout.HelpBox(row.status, MessageType.None);
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(T("MIDI file or ZIP")), false, () => { string p = EditorUtility.OpenFilePanelWithFilters(T("Add MIDI"), "", new[] { "MIDI / ZIP", "mid,midi,zip" }); if (p.Length > 0) Run(() => AddPath(p)); });
            menu.AddItem(new GUIContent(T("Folder")), false, () => { string p = EditorUtility.OpenFolderPanel(T("Add folder"), "", ""); if (p.Length > 0) Run(() => AddPath(p)); });
            menu.AddItem(new GUIContent(T("Add Project selection")), false, () => { foreach (var item in Selection.objects) AddObject(item); });
            menu.AddItem(new GUIContent(T("Download URL")), false, () => MidiDownloadPopup.Show(this));
            menu.ShowAsContext();
        }
        private void ShowMoreMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(T("Reverse order")), false, () => { Record("Reverse MIDI order"); if (folderIndex < 0) rows.Reverse(); else folders[folderIndex].keys.Reverse(); BuildLists(); });
            menu.AddItem(new GUIContent(T("Deduplicate identical MIDI")), false, () => Run(DeduplicateDraft));
            menu.AddItem(new GUIContent(T("Same title: most events, then newest")), preferVersions, () => preferVersions = !preferVersions);
            menu.AddItem(new GUIContent(T("Keep input subfolders")), preserveFolders, () => preserveFolders = !preserveFolders);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(T("Export all JSON")), false, () => Run(() => ExportJson(false)));
            menu.AddItem(new GUIContent(T("Export selected folder JSON")), false, () => Run(() => ExportJson(true)));
            menu.AddItem(new GUIContent(T("Load from JSON")), false, () => Run(ImportJson));
            menu.AddItem(new GUIContent(T("Reimport selected song assets")), false, () => { foreach (var r in rows.Where(r => r.selected)) { r.reimport = true; QueuePreview(r); } dirty = true; });
            menu.AddItem(new GUIContent(T("Use selected titles as filenames")), false, () => { Record("Clean MIDI filenames"); foreach (var r in rows.Where(r => r.selected)) r.rename = MidiLibraryEditing.CleanName(r.title); });
            menu.AddItem(new GUIContent(T("Import settings")), false, () => MidiImportOptionsPopup.Show(this));
            menu.AddItem(new GUIContent(T("Unity MIDI import tutorial")), false, () => EditorUtility.RevealInFinder(Path.GetFullPath(File.Exists("Docs/MIDI_IMPORT.md") ? "Docs/MIDI_IMPORT.md" : "Assets/UdonMidiPlayer/Documentation/MidiImport.md")));
            menu.ShowAsContext();
        }
        private void HandleDrop(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag(); foreach (string path in DragAndDrop.paths) Run(() => AddPath(path));
                foreach (var item in DragAndDrop.objectReferences) if (item is MidiSongAsset) AddObject(item);
            }
            e.Use();
        }
        private void AddObject(UnityEngine.Object item)
        {
            Run(() =>
            {
                var song = item as MidiSongAsset;
                if (song == null) { AddPath(AssetDatabase.GetAssetPath(item)); return; }
                Record("Add MIDI asset"); var row = rows.FirstOrDefault(r => r.song == song);
                if (row == null) { row = FromSong(song); rows.Add(row); }
                AddMembership(row, folderIndex < 0 ? "" : folders[folderIndex].path); activeKey = row.key; BuildLists();
            });
        }
        private void AddPath(string path)
        {
            var inputs = new List<MidiLibraryEditing.Input>();
            MidiLibraryEditing.ReadInputs(path, inputs, folderIndex < 0 ? "" : folders[folderIndex].path, preserveFolders);
            if (inputs.Count == 0) return;
            Record("Add MIDI files");
            foreach (var input in inputs)
            {
                var existing = rows.FirstOrDefault(r => string.Equals(HasInput(r) ? r.input.path : Path.GetFullPath(MidiLibraryEditing.ResolveSource(r.song)), input.path, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { AddMembership(existing, input.folder); continue; }
                var row = new Row { key = Guid.NewGuid().ToString("N"), input = input, title = input.title, author = input.author, settings = new MidiImportSettings { excludePercussion = false }, status = T("Reading MIDI") };
                rows.Add(row); AddMembership(row, input.folder); QueuePreview(row); activeKey = row.key;
            }
            BuildLists();
        }
        private void QueuePreview(Row row) { if (!previewQueue.Contains(row)) previewQueue.Enqueue(row); }
        private void ProcessPreview()
        {
            if (download != null && download.downloadedBytes > (ulong)MidiLibraryEditing.MaxInputBytes) download.Abort();
            if (previewQueue.Count == 0 || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var row = previewQueue.Dequeue(); if (!rows.Contains(row)) return;
            try
            {
                var data = SmfParser.Parse(File.ReadAllBytes(HasInput(row) ? row.input.path : MidiLibraryEditing.ResolveSource(row.song)), row.settings);
                row.events = data.sourceEventCount; row.notes = data.startMs.Length; row.duration = data.durationMs; row.tracks = data.trackNames;
                row.status = row.notes == 0 ? T("No playable notes.") : string.Join("\n", MidiEditorI18n.LocalizeDiagnostics(data.diagnostics));
            }
            catch (Exception exception) { row.status = T("Failed") + ": " + MidiEditorI18n.LocalizeDiagnostic(exception.Message); }
            Repaint();
        }
        private void AddMembership(Row row, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var folder = folders.FirstOrDefault(f => f.path == path);
            if (folder == null) { folder = new Folder { path = path }; folders.Add(folder); }
            if (!folder.keys.Contains(row.key)) folder.keys.Add(row.key);
        }
        private void RemoveRows(List<Row> selected)
        {
            Record("Remove MIDI songs");
            if (folderIndex >= 0) foreach (var row in selected) folders[folderIndex].keys.Remove(row.key);
            else foreach (var row in selected) { rows.Remove(row); foreach (var folder in folders) folder.keys.Remove(row.key); }
            if (!rows.Any(r => r.key == defaultId)) defaultId = rows.FirstOrDefault()?.key;
            BuildLists();
        }
        private void FolderMenu(List<Row> selected, bool move)
        {
            var menu = new GenericMenu();
            foreach (var folder in folders) menu.AddItem(new GUIContent(folder.path), false, () =>
            {
                Record("Assign MIDI folder");
                foreach (var row in selected) { if (move && folderIndex >= 0) folders[folderIndex].keys.Remove(row.key); AddMembership(row, folder.path); }
                BuildLists();
            });
            if (folders.Count == 0) menu.AddDisabledItem(new GUIContent(T("Create a folder first.")));
            menu.ShowAsContext();
        }
        private void RenameFolder(string path)
        {
            path = MidiLibraryEditing.FolderPath(path);
            if (path.Length == 0) throw new ArgumentException(T("Folder path is empty."));
            string previous = folders[folderIndex].path;
            var changes = folders.ToDictionary(f => f, f => f.path == previous || f.path.StartsWith(previous + "/", StringComparison.Ordinal) ? path + f.path.Substring(previous.Length) : f.path);
            if (changes.Values.Distinct(StringComparer.Ordinal).Count() != folders.Count) throw new ArgumentException(T("Folder already exists."));
            Record("Rename MIDI folder"); foreach (var pair in changes) pair.Key.path = pair.Value; BuildLists();
        }
        private void DeduplicateDraft()
        {
            Record("Deduplicate MIDI draft"); var seen = new Dictionary<string, Row>(StringComparer.Ordinal); int removed = 0;
            foreach (var row in rows.ToArray())
            {
                string path = HasInput(row) ? row.input.path : MidiLibraryEditing.ResolveSource(row.song);
                string key = MidiContentHash.Bytes(File.ReadAllBytes(path)) + ":" + MidiContentHash.Settings(row.settings); Row keep;
                if (!seen.TryGetValue(key, out keep)) { seen[key] = row; continue; }
                MergeRows(row, keep); removed++;
            }
            BuildLists(); ShowMessage(T("Merged duplicates") + ": " + removed, MessageType.Info);
        }
        private void MergeRows(Row remove, Row keep)
        {
            foreach (var folder in folders) folder.keys = folder.keys.Select(k => k == remove.key ? keep.key : k).Distinct().ToList();
            if (defaultId == remove.key) defaultId = keep.key; rows.Remove(remove);
        }

        private void SaveAndApply()
        {
            if (library == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorJsonUtility.ToJson(library) != baseline) throw new InvalidOperationException(T("The library changed outside this window. Reload before saving."));
            foreach (var row in rows.Where(r => r.song != null))
                if (SongStamp(row.song) != row.baseline) throw new InvalidOperationException(T("A song changed outside this window. Reload before saving.") + " " + row.title);
            if (rows.Count == 0) throw new InvalidOperationException(T("Keep at least one song in the library."));
            if (folders.Any(f => string.IsNullOrEmpty(MidiLibraryEditing.FolderPath(f.path))) || folders.Select(f => f.path).Distinct().Count() != folders.Count) throw new InvalidOperationException(T("Folder already exists."));
            MidiImportService.EnsureFolder(sourceFolder); MidiImportService.EnsureFolder(outputFolder);
            if (preferVersions) SelectVersions();
            DeduplicateForSave();
            var sources = AssetDatabase.FindAssets("t:MidiSongAsset").Select(id => AssetDatabase.LoadAssetAtPath<MidiSongAsset>(AssetDatabase.GUIDToAssetPath(id))).Where(s => s != null).ToArray();
            var byHash = sources.GroupBy(s => s.sourceHash + ":" + MidiContentHash.Settings(s.importSettings)).ToDictionary(g => g.Key, g => g.First());
            int failures = 0; var successful = new Dictionary<string, MidiSongAsset>();
            foreach (var row in rows)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(row.title)) throw new ArgumentException(T("Song title is empty."));
                    var song = row.song;
                    bool unchanged = song != null && !HasInput(row) && !row.reimport && string.IsNullOrWhiteSpace(row.rename)
                        && row.title == song.title && (row.author ?? "") == (song.author ?? "")
                        && MidiContentHash.Settings(row.settings) == MidiContentHash.Settings(song.importSettings);
                    if (unchanged)
                    {
                        MidiDataBaker.ValidateSong(song);
                        successful[row.key] = song;
                        row.status = "";
                        continue;
                    }
                    if (song == null)
                    {
                        byte[] bytes = File.ReadAllBytes(row.input.path); var preview = SmfParser.Parse(bytes, row.settings);
                        if (preview.startMs.Length == 0) throw new InvalidOperationException(T("No playable notes."));
                        string key = MidiContentHash.Bytes(bytes) + ":" + MidiContentHash.Settings(row.settings);
                        if (!byHash.TryGetValue(key, out song)) { string path = MidiLibraryEditing.CopySource(row.input, sourceFolder); song = MidiImportService.ImportAsset(path, outputFolder, row.settings); byHash[key] = song; }
                        else
                        {
                            MidiDataBaker.ValidateSong(song);
                            // Reusing exact bytes must not erase verified metadata with filename defaults.
                            if (row.title == row.input.title) row.title = song.title;
                            if (string.IsNullOrEmpty(row.author)) row.author = song.author;
                        }
                    }
                    else if (HasInput(row))
                    {
                        MidiLibraryEditing.ReplaceSource(song, row.input.path, row.settings);
                    }
                    else if (row.reimport || MidiContentHash.Settings(row.settings) != MidiContentHash.Settings(song.importSettings)) MidiImportService.ImportAsset(MidiLibraryEditing.ResolveSource(song), outputFolder, row.settings, song);
                    Undo.RecordObject(song, "Save MIDI metadata"); song.title = row.title.Trim(); song.author = row.author ?? ""; song.contentHash = MidiContentHash.Song(song);
                    if (!string.IsNullOrWhiteSpace(row.rename)) MidiLibraryEditing.RenameSource(song, row.rename);
                    MidiDataBaker.ValidateSong(song);
                    EditorUtility.SetDirty(song); AssetDatabase.SaveAssetIfDirty(song); successful[row.key] = song;
                    row.song = song; row.baseline = SongStamp(song); row.input = null; row.rename = ""; row.reimport = false; row.status = "";
                }
                catch (Exception exception)
                {
                    if (row.song != null) row.baseline = SongStamp(row.song);
                    row.status = T("Failed") + ": " + MidiEditorI18n.LocalizeDiagnostic(exception.Message); failures++;
                }
            }
            foreach (var row in rows.Where(r => r.song != null && !successful.ContainsKey(r.key))) successful[row.key] = row.song;
            var staged = CreateInstance<MidiLibraryAsset>();
            try
            {
                staged.name = library.name; staged.title = libraryTitle; staged.songs = rows.Where(r => successful.ContainsKey(r.key)).Select(r => successful[r.key]).Distinct().ToArray();
                staged.folders = folders.Select(f => new MidiLibraryFolder { path = f.path, songs = f.keys.Where(successful.ContainsKey).Select(k => successful[k]).Distinct().ToArray() }).ToArray();
                staged.defaultSongId = successful.ContainsKey(defaultId ?? "") ? successful[defaultId].songId : staged.songs.FirstOrDefault()?.songId;
                MidiDataBaker.ValidateFolders(staged); Undo.RecordObject(library, "Save MIDI library"); EditorUtility.CopySerialized(staged, library);
                EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); baseline = EditorJsonUtility.ToJson(library);
            }
            finally { DestroyImmediate(staged); }
            var report = new List<string>(); int applied = 0;
            try { if (failures == 0) { MidiDataBaker.ValidateLibrary(library); applied = MidiPlayerBuilder.SynchronizeLibraryPlayers(library, report.Add, true, true); } }
            catch (Exception exception) { report.Add(MidiEditorI18n.LocalizeDiagnostic(exception.Message)); failures++; }
            if (failures == 0) SetLibrary(library); else { dirty = true; BuildLists(); }
            ShowMessage(T("Library saved") + ". " + T("Applied players") + ": " + applied + ". " + T("Failed") + ": " + failures + ".\n" + string.Join("\n", report.Select(MidiEditorI18n.LocalizeDiagnostic)) + "\n" + (failures > 0 ? T("Fix failed songs and save again. Scene application was skipped for import failures.") : T("Save the affected scenes.")), failures > 0 ? MessageType.Warning : MessageType.Info);
        }
        private void DeduplicateForSave()
        {
            // Resolve new additions against the draft first, preserving existing row metadata and identity.
            var seen = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in rows.OrderBy(r => r.song == null ? 1 : 0).ToArray())
            {
                try
                {
                    string key = MidiContentHash.Bytes(File.ReadAllBytes(HasInput(row) ? row.input.path : MidiLibraryEditing.ResolveSource(row.song))) + ":" + MidiContentHash.Settings(row.settings);
                    Row keep;
                    if (!seen.TryGetValue(key, out keep)) { seen[key] = row; continue; }
                    if (row.song != null) continue; // Existing duplicates are merged only by the explicit command.
                    if (!string.IsNullOrEmpty(row.author)) keep.author = row.author;
                    if (row.title != row.input.title) keep.title = row.title;
                    MergeRows(row, keep);
                }
                catch (Exception exception) { row.status = T("Failed") + ": " + MidiEditorI18n.LocalizeDiagnostic(exception.Message); }
            }
        }
        private void SelectVersions()
        {
            foreach (var group in rows.GroupBy(r => r.title.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToArray())
            {
                var candidates = group.Select(r => new { row = r, path = HasInput(r) ? r.input.path : MidiLibraryEditing.ResolveSource(r.song) }).Select(x => new {
                    x.row, x.path, count = SmfParser.Parse(File.ReadAllBytes(x.path), new MidiImportSettings { excludePercussion = false }).sourceEventCount,
                    date = HasInput(x.row) ? x.row.input.modifiedTicks : File.GetLastWriteTimeUtc(x.path).Ticks }).OrderByDescending(x => x.count).ThenByDescending(x => x.date).ToArray();
                var best = candidates[0]; var keep = group.FirstOrDefault(r => r.song != null) ?? best.row;
                if (keep != best.row) { keep.input = new MidiLibraryEditing.Input { path = best.path, title = best.row.title, modifiedTicks = best.date }; keep.settings = best.row.settings; keep.author = best.row.author; keep.reimport = true; }
                foreach (var other in group.Where(r => r != keep).ToArray()) MergeRows(other, keep);
            }
        }
        private void ExportJson(bool selectedFolder)
        {
            if (dirty) throw new InvalidOperationException(T("Save the library before exporting JSON."));
            string path = EditorUtility.SaveFilePanel(T("Export playlist"), "", "midi-playlist.json", "json"); if (path.Length == 0) return;
            var copy = Instantiate(library);
            try
            {
                if (selectedFolder && folderIndex >= 0) { var folder = copy.folders[folderIndex]; copy.songs = folder.songs; copy.folders = new[] { folder }; copy.defaultSongId = copy.songs.FirstOrDefault()?.songId; }
                File.WriteAllText(path, MidiLibraryEditing.ExportPlaylist(copy));
            }
            finally { DestroyImmediate(copy); }
        }
        private void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel(T("Load from JSON"), "", "json"); if (path.Length == 0 || !CanDiscard()) return;
            var copy = CreateInstance<MidiLibraryAsset>();
            try { MidiLibraryEditing.ImportPlaylist(copy, File.ReadAllText(path)); var original = library; string originalBaseline = baseline; SetLibrary(copy); library = original; baseline = originalBaseline; dirty = true; }
            finally { DestroyImmediate(copy); }
        }
        private void CreateLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject(T("Create library"), "MidiLibrary", "asset", T("Create library")); if (path.Length == 0) return;
            var asset = CreateInstance<MidiLibraryAsset>(); AssetDatabase.CreateAsset(asset, path); SetLibrary(asset);
        }
        private void Run(Action action) { try { action(); } catch (Exception exception) { ShowMessage(MidiEditorI18n.LocalizeDiagnostic(exception.Message), MessageType.Error); } }
        private void ShowMessage(string text, MessageType type) { message = text; messageType = type; Repaint(); }

        private sealed class MidiDownloadPopup : global::UnityEditor.EditorWindow
        {
            private MidiImportWindow owner;
            public static void Show(MidiImportWindow owner) { var w = CreateInstance<MidiDownloadPopup>(); w.owner = owner; w.titleContent = new GUIContent(T("Download MIDI")); w.ShowUtility(); }
            private void OnGUI()
            {
                if (owner == null) { Close(); return; } owner.url = EditorGUILayout.TextField(T("Direct MIDI / ZIP URL"), owner.url);
                if (GUILayout.Button(T("Add"))) { owner.Run(() => owner.Download(owner.url)); Close(); }
            }
        }
        private void Download(string address)
        {
            if (download != null) throw new InvalidOperationException(T("A download is already running."));
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new ArgumentException(T("Enter an HTTP or HTTPS MIDI / ZIP URL."));
            string extension = Path.GetExtension(uri.AbsolutePath);
            if (!MidiLibraryEditing.IsMidi(uri.AbsolutePath) && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(T("Use a direct .mid, .midi or .zip download URL."));
            string folder = "Library/MidiImportInbox/" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(folder);
            downloadPath = folder + "/" + MidiLibraryEditing.CleanName(Path.GetFileNameWithoutExtension(uri.AbsolutePath)) + extension;
            download = UnityWebRequest.Get(uri); download.downloadHandler = new DownloadHandlerFile(downloadPath); download.timeout = 60;
            download.SendWebRequest().completed += operation =>
            {
                if (download == null) return;
                bool ok = download.result == UnityWebRequest.Result.Success && download.downloadedBytes <= (ulong)MidiLibraryEditing.MaxInputBytes; string error = download.error;
                download.Dispose(); download = null;
                if (ok) Run(() => AddPath(downloadPath)); else ShowMessage(T("Download failed") + ": " + error, MessageType.Error);
            };
        }
        private sealed class MidiImportOptionsPopup : global::UnityEditor.EditorWindow
        {
            private MidiImportWindow owner;
            public static void Show(MidiImportWindow owner) { var w = CreateInstance<MidiImportOptionsPopup>(); w.owner = owner; w.titleContent = new GUIContent(T("Import settings")); w.ShowUtility(); }
            private void OnGUI()
            {
                if (owner == null) { Close(); return; }
                owner.sourceFolder = EditorGUILayout.TextField(T("Source MIDI folder"), owner.sourceFolder);
                owner.outputFolder = EditorGUILayout.TextField(T("Output asset folder"), owner.outputFolder);
                owner.preserveFolders = EditorGUILayout.Toggle(T("Keep input subfolders"), owner.preserveFolders);
                owner.preferVersions = EditorGUILayout.Toggle(T("Same title: most events, then newest"), owner.preferVersions);
                EditorGUILayout.HelpBox(T("Version selection groups equal edited titles only. Original files and replaced source backups are kept."), MessageType.Info);
            }
        }
    }
}
