// SPDX-License-Identifier: MIT
// Copyright (c) 2026 passhu
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    /// <summary>Fail-closed permissions shared by package and public-source exports.</summary>
    [Serializable]
    public sealed class MidiReleasePolicy
    {
        public const string PathInProject = "Assets/UdonMidiPlayer/Documentation/ReleasePolicy.json";
        public int schemaVersion;
        public string owner, contact, codeLicense, artLicense;
        public string[] allowedMusicLicenses;
        public Resource[] music, audio;
        public Song[] songs;
        public AudioGroup[] audioGroups;

        [Serializable] private sealed class CandidateScope
        {
            public string version;
            public string[] excludedPaths, excludedPrefixes;
        }

        public static bool ExcludedFromCandidate(string path)
        {
            string scopePath = "Tools/Release/candidates/" + Version + ".json";
            if (!File.Exists(scopePath)) return false;
            var scope = JsonUtility.FromJson<CandidateScope>(File.ReadAllText(scopePath));
            if (scope == null || scope.version != Version) throw new InvalidOperationException("Invalid candidate scope");
            string value = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
            return (scope.excludedPaths ?? new string[0]).Contains(value)
                || (scope.excludedPrefixes ?? new string[0]).Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal) || value == prefix.TrimEnd('/'));
        }

        [Serializable]
        public class Resource { public string path, guid, sha256, license; }

        [Serializable]
        public sealed class Song : Resource { public string songId, sourceGuid, sourceHash; }

        [Serializable]
        public sealed class AudioGroup
        {
            public string prefix, license, author, source;
            public string[] notices;
        }

        public static string Version
        {
            get
            {
                string version = File.ReadAllText(File.Exists("VERSION") ? "VERSION" : "Assets/UdonMidiPlayer/Documentation/VERSION.txt").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-rc\.\d+)?$"))
                    throw new InvalidOperationException("Invalid release VERSION");
                return version;
            }
        }

        public static MidiReleasePolicy Load()
        {
            var policy = JsonUtility.FromJson<MidiReleasePolicy>(File.ReadAllText(PathInProject));
            if (policy == null || policy.schemaVersion != 1 || policy.codeLicense != "MIT" || policy.artLicense != "CC-BY-4.0"
                || policy.songs == null || policy.music == null || policy.audio == null || policy.audioGroups == null)
                throw new InvalidOperationException("Invalid release policy; run the reviewed release inventory.");
            string[] accepted = { "LicenseRef-Public-Domain", "CC0-1.0", "CC-BY-3.0", "CC-BY-4.0", "LicenseRef-Iowa-MIS-Unrestricted" };
            foreach (Resource item in policy.music.Concat(policy.audio).Concat<Resource>(policy.songs))
                if (!accepted.Contains(item.license)) throw new InvalidOperationException("Disallowed score/sample license: " + item.path);
            foreach (AudioGroup group in policy.audioGroups)
                foreach (string notice in group.notices)
                    if (!File.Exists(group.prefix + notice)) throw new FileNotFoundException("Missing sample notice", group.prefix + notice);
            return policy;
        }

        public bool Approves(MidiSongAsset source)
        {
            if (source == null) return false;
            string path = AssetDatabase.GetAssetPath(source);
            Song approved = songs.FirstOrDefault(x => x.path == path && x.songId == source.songId);
            return approved != null && approved.sourceGuid == source.sourceGuid && approved.sourceHash == source.sourceHash;
        }

        public void ValidateSong(MidiSongAsset source)
        {
            if (!Approves(source)) throw new InvalidOperationException("Unapproved MIDI-derived asset: " + AssetDatabase.GetAssetPath(source));
            Song approved = songs.First(x => x.path == AssetDatabase.GetAssetPath(source));
            CheckFile(approved);
            string sourcePath = AssetDatabase.GUIDToAssetPath(source.sourceGuid);
            Resource midi = music.FirstOrDefault(x => x.path == sourcePath && x.sha256 == source.sourceHash);
            if (midi == null) throw new InvalidOperationException("Unapproved MIDI source: " + sourcePath);
            CheckFile(midi);
        }

        public void ValidateRoot(GameObject root)
        {
            foreach (MidiSongData song in root.GetComponentsInChildren<MidiSongData>(true))
                if (!songs.Any(x => x.songId == song.songId && x.sourceHash == song.sourceHash))
                    throw new InvalidOperationException("Unapproved embedded song: " + root.name + "/" + song.name);
        }

        public void ValidateAsset(string path)
        {
            if (path.Contains("/RegressionTemp/") || path.Contains("/ReleaseStaging/") || path.StartsWith("Assets/UdonSharp/", StringComparison.Ordinal)
                || path == "Assets/TextMesh Pro/Resources/TMP Settings.asset")
                throw new InvalidOperationException("Development/global asset cannot be distributed: " + path);
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".mid" || extension == ".midi")
            {
                Resource midi = music.FirstOrDefault(x => x.path == path);
                if (midi == null) throw new InvalidOperationException("Unapproved MIDI: " + path);
                CheckFile(midi);
            }
            else if (new[] { ".ogg", ".wav", ".flac", ".aif", ".aiff", ".mp3" }.Contains(extension))
            {
                Resource clip = audio.FirstOrDefault(x => x.path == path);
                if (clip == null) throw new InvalidOperationException("Unapproved sound sample: " + path);
                CheckFile(clip);
            }
            else if (path.StartsWith("Assets/UdonMidiPlayer/Generated/Songs/", StringComparison.Ordinal) && extension == ".bytes")
            {
                if (!songs.Any(x => x.songId == System.IO.Path.GetFileNameWithoutExtension(path)))
                    throw new InvalidOperationException("Unapproved packed song data: " + path);
            }
            else if (extension == ".asset")
            {
                var source = AssetDatabase.LoadAssetAtPath<MidiSongAsset>(path);
                if (source != null) ValidateSong(source);
                var library = AssetDatabase.LoadAssetAtPath<MidiLibraryAsset>(path);
                if (library != null) foreach (MidiSongAsset song in library.songs) ValidateSong(song);
                var configuration = AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(path);
                if (configuration != null)
                {
                    if (configuration.library == null) throw new InvalidOperationException("Release configuration has no library: " + path);
                    foreach (MidiSongAsset song in configuration.library.songs) ValidateSong(song);
                }
            }
            else if (extension == ".prefab")
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) ValidateRoot(prefab);
            }
        }

        public static string FileHash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void CheckFile(Resource item)
        {
            if (!File.Exists(item.path) || ResourceHash(item.path) != item.sha256 || AssetDatabase.AssetPathToGUID(item.path) != item.guid)
                throw new InvalidOperationException("Approved resource changed; review inventory: " + item.path);
        }

        private static string ResourceHash(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.Ordinal)) return FileHash(path);
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    File.ReadAllText(path).Replace("\r\n", "\n")))).Replace("-", "").ToLowerInvariant();
        }
    }
}
