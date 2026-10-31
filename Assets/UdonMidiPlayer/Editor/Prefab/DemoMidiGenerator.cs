using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Import;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class DemoMidiGenerator
    {
        private const string Folder = "Assets/UdonMidiPlayer/Samples/MIDI";
        private sealed class Message
        {
            public int tick;
            public int ordinal;
            public byte[] bytes;
        }

        public static MidiSongAsset[] EnsureDemoSongs()
        {
            Directory.CreateDirectory(Folder);
            string study = Folder + "/ClockworkStudy.mid";
            string sync = Folder + "/SyncStudy.mid";
            if (!File.Exists(study)) WriteStudy(study, false);
            if (!File.Exists(sync)) WriteStudy(sync, true);
            AssetDatabase.Refresh();
            return new[] { MidiImportService.ImportAsset(study), MidiImportService.ImportAsset(sync) };
        }

        private static void WriteStudy(string path, bool diagnostic)
        {
            var conductor = new List<Message>();
            var notes = new List<Message>();
            Add(conductor, 0, new byte[] { 0xff, 0x51, 3, 0x07, 0xa1, 0x20 });
            Add(conductor, 0, MetaText(diagnostic ? "Sync Study" : "Clockwork Study"));
            int[] harmony = { 48, 53, 57, 55, 48, 53, 50, 55 };
            int[] melody = { 72, 76, 79, 76, 74, 77, 81, 77, 76, 81, 84, 81, 74, 79, 83, 79 };
            int bars = diagnostic ? 4 : 12;
            for (int bar = 0; bar < bars; bar++)
            {
                int tick = bar * 1920;
                int bass = harmony[bar % harmony.Length];
                Add(notes, tick, new byte[] { 0xb0, 64, 127 });
                for (int n = 0; n < 4; n++)
                {
                    int pitch = bass + new[] { 0, 7, 12, 16 }[n];
                    Note(notes, tick + n * 120, pitch, 67 + n * 4, 680);
                }
                for (int n = 0; n < 8; n++) Note(notes, tick + n * 240, melody[(bar * 2 + n) % melody.Length], 80 + n % 3 * 7, 190);
                Add(notes, tick + 1800, new byte[] { 0xb0, 64, 0 });
            }
            if (diagnostic)
            {
                Add(conductor, 1920, new byte[] { 0xff, 0x51, 3, 0x06, 0x1a, 0x80 });
                Note(notes, 480, 60, 90, 960);
                Note(notes, 720, 60, 70, 960);
                Note(notes, 0, 21, 80, 120);
                Note(notes, 240, 108, 70, 120);
            }
            int end = bars * 1920 + 480;
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(Encoding.ASCII.GetBytes("MThd")); Big32(writer, 6); Big16(writer, 1); Big16(writer, 2); Big16(writer, 480);
                WriteTrack(writer, conductor, end);
                WriteTrack(writer, notes, end);
            }
        }

        private static void Note(List<Message> list, int tick, int pitch, int velocity, int length)
        {
            Add(list, tick, new byte[] { 0x90, (byte)pitch, (byte)velocity });
            Add(list, tick + length, new byte[] { 0x80, (byte)pitch, 0 });
        }
        private static void Add(List<Message> list, int tick, byte[] bytes) { list.Add(new Message { tick = tick, ordinal = list.Count, bytes = bytes }); }
        private static byte[] MetaText(string text)
        {
            byte[] value = Encoding.ASCII.GetBytes(text);
            var result = new byte[value.Length + 3]; result[0] = 0xff; result[1] = 3; result[2] = (byte)value.Length;
            Array.Copy(value, 0, result, 3, value.Length); return result;
        }
        private static void WriteTrack(BinaryWriter output, List<Message> events, int end)
        {
            events.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.ordinal.CompareTo(b.ordinal));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int tick = 0;
                foreach (var entry in events) { Vlq(writer, entry.tick - tick); writer.Write(entry.bytes); tick = entry.tick; }
                Vlq(writer, end - tick); writer.Write(new byte[] { 0xff, 0x2f, 0 });
                output.Write(Encoding.ASCII.GetBytes("MTrk")); Big32(output, (int)stream.Length); output.Write(stream.ToArray());
            }
        }
        private static void Vlq(BinaryWriter writer, int value)
        {
            int buffer = value & 127;
            while ((value >>= 7) > 0) buffer = (buffer << 8) | (value & 127) | 128;
            while (true) { writer.Write((byte)buffer); if ((buffer & 128) == 0) break; buffer >>= 8; }
        }
        private static void Big16(BinaryWriter writer, int value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        private static void Big32(BinaryWriter writer, int value) { writer.Write((byte)(value >> 24)); writer.Write((byte)(value >> 16)); writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    }
}
