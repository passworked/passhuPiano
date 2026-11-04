// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Import
{
    /// <summary>Editor source-file operations. Never changes runtime mappings or shared Prefabs.</summary>
    public static class MidiLibraryEditing
    {
        public const string SourceFolder = "Assets/music/Imported";
        public const long MaxInputBytes = 64L * 1024 * 1024;

        [Serializable]
        public sealed class Input
        {
            public string path, title, author = "", folder = "", status = "";
            public bool enabled = true;
            public long modifiedTicks;
        }

        public static bool IsMidi(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
        }

        public static string CleanName(string value)
        {
            value = (value ?? "").Replace('_', ' ').Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, ' ');
            return System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim().TrimEnd('.');
        }

        public static string FolderPath(string value)
        {
            string normalized = (value ?? "").Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) return "";
            string[] parts = normalized.Split('/');
            if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".."))
                throw new ArgumentException("Invalid folder path: " + value);
            return string.Join("/", parts.Select(p => p.Trim()));
        }

        public static void ReadInputs(string path, IList<Input> inputs, string folder = "", bool preserveFolders = false)
        {
            if (Directory.Exists(path))
            {
                string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                    if (IsMidi(file) || Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = Path.GetDirectoryName(file.Substring(root.Length)).Replace('\\', '/');
                        ReadInputs(file, inputs, preserveFolders ? JoinFolder(folder, relative) : folder, preserveFolders);
                    }
                return;
            }
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(path).Length > MaxInputBytes) throw new IOException("ZIP exceeds 64 MiB: " + path);
                // Extract MIDI entries only, under a unique local cache. Never trust archive paths.
                string cache = Path.GetFullPath("Library/MidiImportInbox/" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(cache);
                using (var archive = ZipFile.OpenRead(path))
                {
                    long total = 0;
                    int count = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (!IsMidi(entry.FullName)) continue;
                        total = checked(total + entry.Length);
                        if (total > MaxInputBytes || ++count > 4096) throw new IOException("ZIP MIDI payload exceeds import limits.");
                    }
                    foreach (var entry in archive.Entries)
                    {
                        if (!IsMidi(entry.FullName)) continue;
                        string relative = entry.FullName.Replace('\\', '/');
                        string destination = Path.GetFullPath(Path.Combine(cache, relative));
                        if (!destination.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Unsafe ZIP path: " + entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var source = entry.Open())
                        using (var target = File.Create(destination)) source.CopyTo(target);
                        File.SetLastWriteTimeUtc(destination, entry.LastWriteTime.UtcDateTime);
                        AddInput(destination, inputs, preserveFolders ? JoinFolder(folder, Path.GetDirectoryName(relative).Replace('\\', '/')) : folder);
                    }
                }
                return;
            }
            if (IsMidi(path)) AddInput(path, inputs, folder);
        }

        private static string JoinFolder(string parent, string child) { return FolderPath((parent + "/" + child).Trim('/')); }

        private static void AddInput(string path, IList<Input> inputs, string folder)
        {
            string absolute = Path.GetFullPath(path);
            if (inputs.Any(i => string.Equals(i.path, absolute, StringComparison.OrdinalIgnoreCase))) return;
            if (new FileInfo(absolute).Length > MaxInputBytes) throw new IOException("MIDI exceeds 64 MiB: " + path);
            inputs.Add(new Input { path = absolute, title = CleanName(Path.GetFileNameWithoutExtension(path)),
                folder = FolderPath(folder), modifiedTicks = File.GetLastWriteTimeUtc(absolute).Ticks });
        }

        public static string ResolveSource(MidiSongAsset song)
        {
            string path = AssetDatabase.GUIDToAssetPath(song.sourceGuid);
            return string.IsNullOrEmpty(path) ? song.sourcePath : path;
        }

        public static void SearchAuthor(string title)
        {
            string address = "https://www.google.com/search?q=" + Uri.EscapeDataString(title + " composer arranger MIDI");
#if UNITY_EDITOR_WIN
            string chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe");
            if (!File.Exists(chrome)) chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google/Chrome/Application/chrome.exe");
#elif UNITY_EDITOR_OSX
            string chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
#else
            string chrome = "google-chrome";
#endif
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = chrome, Arguments = "\"" + address + "\"", UseShellExecute = false, CreateNoWindow = true
            });
        }

        public static string CopySource(Input input, string destinationFolder)
        {
            try { return MidiImportService.ToAssetPath(input.path); }
            catch (ArgumentException) { /* External files are copied; originals are retained. */ }
            MidiImportService.EnsureFolder(destinationFolder);
            string name = CleanName(input.title);
            if (name.Length == 0) name = "MidiSong";
            string path = AssetDatabase.GenerateUniqueAssetPath(destinationFolder.TrimEnd('/') + "/" + name + Path.GetExtension(input.path).ToLowerInvariant());
            File.Copy(input.path, path, false);
            File.SetLastWriteTimeUtc(path, new DateTime(input.modifiedTicks, DateTimeKind.Utc));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // Explicit version replacement keeps source GUID, song GUID, songId and every library reference.
        public static MidiSongAsset ReplaceSource(MidiSongAsset song, string file, MidiImportSettings settings = null)
        {
            string target = MidiImportService.ToAssetPath(ResolveSource(song));
            byte[] replacement = File.ReadAllBytes(file);
            if (replacement.LongLength > MaxInputBytes) throw new IOException("MIDI exceeds 64 MiB.");
            settings = settings ?? song.importSettings;
            SmfParser.Parse(replacement, settings); // Fail before touching the old source.
            string backup = "RecoveryBackups/MidiSourceReplacement/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(backup);
            File.Copy(target, backup + "/" + Path.GetFileName(target));
            File.Copy(AssetDatabase.GetAssetPath(song), backup + "/song.asset");
            byte[] original = File.ReadAllBytes(target);
            string serialized = EditorJsonUtility.ToJson(song);
            try
            {
                File.WriteAllBytes(target, replacement);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                return MidiImportService.ImportAsset(target, Path.GetDirectoryName(AssetDatabase.GetAssetPath(song)), settings, song);
            }
            catch
            {
                File.WriteAllBytes(target, original);
                AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                EditorJsonUtility.FromJsonOverwrite(serialized, song);
                EditorUtility.SetDirty(song);
                AssetDatabase.SaveAssetIfDirty(song);
                throw;
            }
        }

        public static void RenameSource(MidiSongAsset song, string name)
        {
            string path = MidiImportService.ToAssetPath(ResolveSource(song));
            name = CleanName(name);
            if (name.Length == 0) throw new ArgumentException("Filename is empty.");
            string error = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            string guid = song.sourceGuid;
            foreach (string id in AssetDatabase.FindAssets("t:MidiSongAsset"))
            {
                var source = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(AssetDatabase.GUIDToAssetPath(id));
                if (source == null || source.sourceGuid != guid) continue;
                Undo.RecordObject(source, "Rename MIDI source");
                source.sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                EditorUtility.SetDirty(source);
                AssetDatabase.SaveAssetIfDirty(source);
            }
        }

        public static void AddSong(MidiLibraryAsset library, MidiSongAsset song, string folder)
        {
            var songs = new List<MidiSongAsset>(library.songs ?? Array.Empty<MidiSongAsset>());
            if (!songs.Contains(song)) songs.Add(song);
            library.songs = songs.ToArray();
            if (string.IsNullOrEmpty(library.defaultSongId)) library.defaultSongId = song.songId;
            folder = FolderPath(folder);
            if (folder.Length == 0) return;
            var folders = new List<MidiLibraryFolder>(library.folders ?? Array.Empty<MidiLibraryFolder>());
            var target = folders.FirstOrDefault(f => f.path == folder);
            if (target == null) { target = new MidiLibraryFolder { path = folder }; folders.Add(target); }
            var members = new List<MidiSongAsset>(target.songs ?? Array.Empty<MidiSongAsset>());
            if (!members.Contains(song)) members.Add(song);
            target.songs = members.ToArray(); library.folders = folders.ToArray();
        }

        // Exact source bytes + settings only. Different arrangements are never inferred to be the same work.
        public static int Deduplicate(MidiLibraryAsset library)
        {
            var canonical = new Dictionary<string, MidiSongAsset>(StringComparer.Ordinal);
            var replacements = new Dictionary<MidiSongAsset, MidiSongAsset>();
            var songs = new List<MidiSongAsset>();
            foreach (var song in library.songs)
            {
                if (song == null) continue;
                if (replacements.ContainsKey(song)) continue;
                string key = song.sourceHash + ":" + MidiContentHash.Settings(song.importSettings);
                MidiSongAsset keep;
                if (!canonical.TryGetValue(key, out keep)) { canonical.Add(key, song); keep = song; songs.Add(song); }
                replacements[song] = keep;
                if (library.defaultSongId == song.songId) library.defaultSongId = keep.songId;
            }
            int removed = library.songs.Length - songs.Count;
            library.songs = songs.ToArray();
            foreach (var folder in library.folders ?? Array.Empty<MidiLibraryFolder>())
                folder.songs = (folder.songs ?? Array.Empty<MidiSongAsset>()).Where(s => s != null && replacements.ContainsKey(s)).Select(s => replacements[s]).Distinct().ToArray();
            return removed;
        }

        [Serializable] public sealed class Playlist
        {
            public int version = 1;
            public string title, defaultSongId;
            public SongReference[] songs;
            public MidiFolderReference[] folders;
        }
        [Serializable] public sealed class SongReference { public string songId, assetGuid; }
        [Serializable] public sealed class MidiFolderReference { public string path; public string[] songIds; }

        public static string ExportPlaylist(MidiLibraryAsset library)
        {
            return JsonUtility.ToJson(new Playlist { title = library.title, defaultSongId = library.defaultSongId,
                songs = library.songs.Select(s => new SongReference { songId = s.songId, assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s)) }).ToArray(),
                folders = library.folders.Select(f => new MidiFolderReference { path = f.path, songIds = f.songs.Select(s => s.songId).ToArray() }).ToArray() }, true);
        }

        public static void ImportPlaylist(MidiLibraryAsset library, string json)
        {
            var data = JsonUtility.FromJson<Playlist>(json);
            if (data == null || data.version != 1 || data.songs == null || data.folders == null) throw new ArgumentException("Invalid MIDI playlist JSON.");
            var songs = new List<MidiSongAsset>();
            foreach (var item in data.songs)
            {
                var song = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(AssetDatabase.GUIDToAssetPath(item.assetGuid));
                if (song == null || song.songId != item.songId) throw new InvalidOperationException("Missing song asset: " + item.songId);
                if (!songs.Contains(song)) songs.Add(song);
            }
            var byId = songs.ToDictionary(s => s.songId);
            var folders = data.folders.Select(f => new MidiLibraryFolder { path = FolderPath(f.path),
                songs = f.songIds.Select(id => byId[id]).Distinct().ToArray() }).ToArray();
            if (songs.Count > 0 && !byId.ContainsKey(data.defaultSongId ?? "")) throw new ArgumentException("Playlist default song is missing.");
            library.title = data.title; library.songs = songs.ToArray(); library.folders = folders; library.defaultSongId = data.defaultSongId;
        }
    }
}
