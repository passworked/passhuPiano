using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Import
{
    /// <summary>Explicit metadata checks. No scene player or audio state is changed.</summary>
    public static class MidiTimbreValidation
    {
        public static string Run()
        {
            var control = Track(new byte[] { 0, 0xb0, 0, 127, 0, 0xb0, 32, 5, 0, 0xc0, 40, 0, 41 });
            var notes = Track(new byte[] { 0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0 });
            var parsed = SmfParser.Parse(Smf(control, notes), new MidiImportSettings { selectedTracks = new[] { 1 } });
            Require(parsed.timbreValue.SequenceEqual(new[] { 127, 5, 40, 41 }), "bank/program/running status from unselected control track");
            Require(parsed.timbreKind.SequenceEqual(new[] { 1, 2, 0, 0 }) && parsed.timbreEventIndex.All(i => i == 0), "kind and before-NoteOn ordering");
            var after = SmfParser.Parse(Smf(notes, control));
            Require(after.timbreEventIndex.All(i => i == 1), "control track after NoteOn at the same tick");
            var rounded = SmfParser.Parse(Smf(Track(new byte[] { 0, 0xc0, 0, 0, 0x90, 60, 100, 1, 0xc0, 127, 1, 0x80, 60, 0 }), 32767));
            Require(rounded.timbreMs.SequenceEqual(new[] { 0, 0 }) && rounded.timbreEventIndex.SequenceEqual(new[] { 0, 1 }), "rounded millisecond retains note/program order and 0/127 values");
            var drum = SmfParser.Parse(Smf(Track(new byte[] { 0, 0xc9, 127 }), notes));
            Require(drum.timbreChannel[0] == 9 && drum.timbreValue[0] == 127, "excluded drum channel metadata retained");
            Require(!parsed.diagnostics.Any(s => s.Contains("0xC0") || s.Contains("CC0,") || s.Contains("CC32")), "supported timbre events removed from unsupported diagnostics");
            foreach (var result in new[] { parsed, after, rounded, drum,
                SmfParser.Parse(Smf(notes)) })
            {
                var asset = ScriptableObject.CreateInstance<MidiSongAsset>();
                try
                {
                    MidiImportService.ApplyResult(asset, result);
                    asset.songId = new string('1', 32); asset.sourceHash = new string('0', 64);
                    var packed = MidiSongCodec.Encode(asset);
                    MidiSongCodec.ValidateRoundTrip(asset, packed);
                    CheckRuntime(asset, packed);
                }
                finally { UnityEngine.Object.DestroyImmediate(asset); }
            }
            MidiSongCodec.ValidateBoundaryCases();
            return "Timbre parser, Editor codec and incremental runtime decode passed: bank MSB/LSB, Program Change, running status, control tracks, drums, rounded ordering, empty metadata, old format and optional extensions.";
        }

        public static void CheckRuntime(MidiSongAsset source, byte[] packed)
        {
            var go = new GameObject("TimbreDecodeValidation"); go.hideFlags = HideFlags.HideAndDontSave;
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/UdonMidiPlayer/Editor/Generated/TimbreDecodeValidation.bytes");
            try
            {
                File.WriteAllBytes(path, packed);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                var target = go.AddUdonSharpComponent<MidiSongData>();
                target.songId = source.songId; target.sourceHash = source.sourceHash; target.durationMs = source.durationMs;
                target.noteCount = source.startMs.Length; target.packedData = text; target.packedDataBytes = packed.Length;
                Require(target.BeginDecode(), target.decodeError);
                for (int step = 0; step < packed.Length + 100 && !target.isDecoded && string.IsNullOrEmpty(target.decodeError); step++) target.DecodeStep(1);
                Require(target.isDecoded && target.IsValid(), "runtime decode: " + target.decodeError);
                Require(target.timbreDataVersion == source.timbreDataVersion && target.timbreMs.SequenceEqual(source.timbreMs) &&
                    target.timbreChannel.SequenceEqual(source.timbreChannel) && target.timbreKind.SequenceEqual(source.timbreKind) &&
                    target.timbreValue.SequenceEqual(source.timbreValue) && target.timbreTrackIndex.SequenceEqual(source.timbreTrackIndex) &&
                    target.timbreEventIndex.SequenceEqual(source.timbreEventIndex), "runtime timbre round trip");
                target.ReleaseDecodedData();
                Require(target.timbreDataVersion == 0 && target.timbreMs.Length == 0, "runtime release clears metadata");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); AssetDatabase.DeleteAsset(path); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static byte[] Track(byte[] events)
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(events, 0, events.Length); stream.Write(new byte[] { 0, 255, 47, 0 }, 0, 4); return stream.ToArray();
            }
        }
        private static byte[] Smf(params byte[][] tracks) { return Smf(tracks, 480); }
        private static byte[] Smf(byte[] track, int ppq) { return Smf(new[] { track }, ppq); }
        private static byte[] Smf(byte[][] tracks, int ppq)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 77, 84, 104, 100, 0, 0, 0, 6, 0, (byte)(tracks.Length == 1 ? 0 : 1), 0, (byte)tracks.Length, (byte)(ppq >> 8), (byte)ppq });
                foreach (byte[] track in tracks)
                {
                    writer.Write(new byte[] { 77, 84, 114, 107, (byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length });
                    writer.Write(track);
                }
                return stream.ToArray();
            }
        }
    }
}
