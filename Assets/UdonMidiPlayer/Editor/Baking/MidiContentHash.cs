using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Import;
using UnityEditor;

namespace MusicMidiPlayer.Editor.Baking
{
    public static class MidiContentHash
    {
        public static string Bytes(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes));
        }

        public static string Settings(MidiImportSettings settings)
        {
            settings = settings ?? new MidiImportSettings();
            return Build(writer =>
            {
                writer.Write(settings.excludePercussion);
                int[] tracks = settings.selectedTracks == null ? new int[0] : (int[])settings.selectedTracks.Clone();
                Array.Sort(tracks);
                Write(writer, tracks);
            });
        }

        public static string Song(MidiSongAsset song)
        {
            return Build(writer =>
            {
                writer.Write(song.schemaVersion); writer.Write(song.title ?? ""); writer.Write(song.durationMs);
                writer.Write(song.sourceHash ?? "");
                writer.Write(Settings(song.importSettings));
                Write(writer, song.startMs); Write(writer, song.keyUpMs); Write(writer, song.releaseMs);
                Write(writer, song.pitch); Write(writer, song.velocity); Write(writer, song.channel); Write(writer, song.trackIndex);
                Write(writer, song.eventMs); Write(writer, song.eventKind); Write(writer, song.eventNoteId);
                Write(writer, song.checkpointEventIndex); Write(writer, song.checkpointActiveOffsets); Write(writer, song.checkpointActiveNoteIds);
                Write(writer, song.pedalMs); Write(writer, song.pedalController); Write(writer, song.pedalValue); Write(writer, song.pedalOffsets);
                if (song.expressionMs != null && song.expressionMs.Length > 0)
                { writer.Write("expression-cc11-v1"); Write(writer, song.expressionMs); Write(writer, song.expressionValue); Write(writer, song.expressionOffsets); }
                if (song.timbreDataVersion == 1)
                {
                    writer.Write("timbre-v1"); Write(writer, song.timbreMs); Write(writer, song.timbreChannel); Write(writer, song.timbreKind);
                    Write(writer, song.timbreValue); Write(writer, song.timbreTrackIndex); Write(writer, song.timbreEventIndex);
                }
            });
        }

        public static string Bank(PianoBankAsset bank)
        {
            return Build(writer =>
            {
                writer.Write(3);
                writer.Write(bank.clips.Length);
                foreach (var clip in bank.clips)
                {
                    string path = AssetDatabase.GetAssetPath(clip);
                    writer.Write(AssetDatabase.AssetPathToGUID(path));
                    // Dependency hashes include platform-specific import artifacts. Hash the
                    // authored source/settings so switching PC/Android does not invalidate a bank.
                    writer.Write(SourceFileHash(path));
                    string meta = Encoding.UTF8.GetString(MidiAudioPacking.ReadBankMetadata(path)).TrimStart('\uFEFF')
                        .Replace("\r\n", "\n").Replace('\r', '\n');
                    writer.Write(Bytes(Encoding.UTF8.GetBytes(meta)));
                }
                Write(writer, bank.rootPitch); Write(writer, bank.lowPitch); Write(writer, bank.highPitch);
                Write(writer, bank.sourceRate); Write(writer, bank.sourceChannels); Write(writer, bank.sourceFrameCount);
                Write(writer, bank.sourcePeak); Write(writer, bank.regionGain); Write(writer, bank.tuningCents); Write(writer, bank.releaseDurationMs);
                Write(writer, bank.pitchToRegion); writer.Write(bank.maxReleaseMs);
                writer.Write(AudioEnergyBaker.Format);
                Write(writer, bank.energyStarts); Write(writer, bank.energyCounts); Write(writer, bank.energyValues);
                // Preserve existing single-layer piano identities.
                if (bank.velocityLayerCount != 1) { writer.Write(bank.velocityLayerCount == 4 ? "velocity-layers-v2:44,69,100" : "velocity-layers-v1:42,84"); writer.Write(bank.velocityLayerCount); }
            });
        }

        private static string SourceFileHash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return Hex(hash.ComputeHash(stream));
        }

        public static string Catalog(MidiLibraryAsset library, PianoBankAsset bank)
        {
            return Build(writer =>
            {
                writer.Write(2); writer.Write(MidiSongCodec.FormatVersion);
                writer.Write(bank.bankHash ?? ""); writer.Write(library.songs.Length);
                foreach (MidiSongAsset song in library.songs)
                { writer.Write(song.songId ?? ""); writer.Write(song.contentHash ?? ""); writer.Write(song.schemaVersion); }
                // Optional presentation metadata; preserve existing catalogs when all authors are empty.
                foreach (MidiSongAsset song in library.songs)
                    if (!string.IsNullOrEmpty(song.author)) { writer.Write(song.songId ?? ""); writer.Write(song.author); }
            });
        }

        private static string Build(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) { write(writer); writer.Flush(); }
                return Bytes(stream.ToArray());
            }
        }
        private static void Write(BinaryWriter writer, int[] values)
        { writer.Write(values == null ? -1 : values.Length); if (values != null) foreach (int item in values) writer.Write(item); }
        private static void Write(BinaryWriter writer, float[] values)
        { writer.Write(values == null ? -1 : values.Length); if (values != null) foreach (float item in values) writer.Write(item); }
        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2"));
            return text.ToString();
        }
    }
}
