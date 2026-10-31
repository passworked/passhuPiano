using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MusicMidiPlayer.Editor.Import
{
    /// <summary>Deterministic parser edge checks; callable with Unity -executeMethod, no NUnit dependency.</summary>
    public static class MidiParserValidation
    {
        public static void Run()
        {
            string report = RunAll();
#if UNITY_EDITOR
            UnityEngine.Debug.Log(report);
#else
            Console.WriteLine(report);
#endif
        }

        public static string RunAll()
        {
            int checks = 0;
            MidiImportResult basic = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 100), E(480, 0x80, 60, 0))));
            Check(basic.startMs.Length == 1 && basic.startMs[0] == 0 && basic.keyUpMs[0] == 500 && basic.releaseMs[0] == 500, "basic timing", ref checks);

            MidiImportResult measures = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0xff, 0x58, 4, 4, 2, 24, 8), E(960, 0xff, 0x51, 3, 15, 0x42, 0x40),
                    E(960, 0xff, 0x58, 4, 3, 2, 24, 8), E(480, 0xff, 0x51, 3, 3, 0xd0, 0x90)),
                Track(E(0, 0x90, 60, 90), E(4800, 0x80, 60, 0)),
                Track(E(0, 0x91, 72, 90), E(4800, 0x81, 72, 0))),
                new MidiImportSettings { selectedTracks = new[] { 1, 2 } });
            Check(measures.measureStartMs.Length == 4 && measures.measureStartMs[0] == 0 &&
                measures.measureStartMs[1] == 3000 && measures.measureStartMs[2] == 4500 && measures.measureStartMs[3] == 5250 &&
                measures.startMs.Length == 2 && measures.keyUpMs[0] == 5250,
                "measure grid follows tempo and signature changes from unselected conductor for two pianos", ref checks);
            MidiImportResult partialMeasure = SmfParser.Parse(Smf(0, 480, Track(
                E(0, 0x90, 60, 90), E(480, 0xff, 0x58, 4, 3, 3, 24, 8), E(1440, 0x80, 60, 0))));
            Check(partialMeasure.measureStartMs.Length == 4 && partialMeasure.measureStartMs[1] == 500 &&
                partialMeasure.measureStartMs[2] == 1250 && partialMeasure.measureStartMs[3] == 2000,
                "off-bar signature change starts an incomplete measure; 3/8 denominator", ref checks);
            Check(basic.measureStartMs.Length == 2 && basic.measureStartMs[1] == 500,
                "missing signature defaults to 4/4 with terminal partial measure", ref checks);

            MidiImportResult running = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 100), E(480, 60, 0))));
            Check(running.keyUpMs[0] == 500 && running.startMs.Length == 1, "running status and velocity zero", ref checks);

            MidiImportResult tempo = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0xff, 0x51, 3, 7, 0xa1, 0x20), E(480, 0xff, 0x51, 3, 15, 0x42, 0x40), E(480, 0xff, 0x2f, 0)),
                Track(E(0, 0x90, 60, 90), E(960, 0x80, 60, 0))), new MidiImportSettings { selectedTracks = new[] { 1 } });
            Check(tempo.keyUpMs[0] == 1500 && tempo.durationMs == 1500 && tempo.tempoChangeCount == 2, "tempo map across unselected track", ref checks);

            MidiImportResult fifo = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(240, 0x90, 60, 70), E(240, 0x80, 60, 0), E(480, 0x80, 60, 0))));
            Check(fifo.keyUpMs[0] == 500 && fifo.keyUpMs[1] == 1000 && fifo.maxKeyPolyphony == 2, "overlap FIFO", ref checks);

            MidiImportResult pedal = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0xb0, 64, 127), E(960, 0xb0, 64, 0)),
                Track(E(0, 0x90, 60, 90), E(0, 0x91, 64, 90), E(480, 0x80, 60, 0), E(0, 0x81, 64, 0))),
                new MidiImportSettings { selectedTracks = new[] { 1 } });
            Check(pedal.keyUpMs[0] == 500 && pedal.releaseMs[0] == 1000 && pedal.releaseMs[1] == 500, "channel sustain from control track", ref checks);
            Check(pedal.pedalMs.Length == 0 && pedal.pedalOffsets.Length == 4, "pedal animation respects selected tracks while existing audio CC64 pairing is unchanged", ref checks);

            MidiImportResult pedalMax = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(0, 0xb0, 67, 20),
                E(240, 0xb1, 67, 100), E(240, 0xb0, 67, 0), E(240, 0xb1, 67, 40), E(240, 0xb1, 67, 0), E(0, 0x80, 60, 0))));
            Check(pedalMax.pedalMs.Length == 4 && pedalMax.pedalController[0] == 67 && pedalMax.pedalMs[0] == 0 &&
                pedalMax.pedalMs[1] == 250 && pedalMax.pedalMs[2] == 750 && pedalMax.pedalMs[3] == 1000 &&
                SamplePedal(pedalMax, 0, 0) == 20 && SamplePedal(pedalMax, 0, 500) == 100 && SamplePedal(pedalMax, 0, 999) == 40 &&
                SamplePedal(pedalMax, 0, 1000) == 0, "pedal cross-channel maximum and unchanged-value deduplication", ref checks);

            MidiImportResult pedalTracks = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0xb0, 64, 127), E(240, 0xb0, 64, 0)),
                Track(E(0, 0x90, 60, 90), E(0, 0xb0, 66, 96), E(0, 0xb0, 64, 45), E(480, 0xb0, 66, 0), E(0, 0xb0, 64, 0), E(0, 0x80, 60, 0))),
                new MidiImportSettings { selectedTracks = new[] { 1 } });
            Check(SamplePedal(pedalTracks, 0, 0) == 0 && SamplePedal(pedalTracks, 1, 0) == 96 && SamplePedal(pedalTracks, 2, 0) == 45 &&
                SamplePedal(pedalTracks, 1, 500) == 0 && SamplePedal(pedalTracks, 2, 500) == 0, "physical pedal order and selected-track filtering", ref checks);

            byte[] drumPedalBytes = Smf(0, 480, Track(E(0, 0x90, 60, 90), E(0, 0xb9, 67, 127), E(0, 0xb0, 67, 30), E(480, 0xb0, 67, 0), E(0, 0x80, 60, 0)));
            MidiImportResult pedalDrums = SmfParser.Parse(drumPedalBytes);
            MidiImportResult pedalDrumsIncluded = SmfParser.Parse(drumPedalBytes, new MidiImportSettings { excludePercussion = false });
            Check(SamplePedal(pedalDrums, 0, 0) == 30 && SamplePedal(pedalDrums, 0, 500) == 0 &&
                SamplePedal(pedalDrumsIncluded, 0, 500) == 0, "drum controllers never animate piano pedals, including drum-enabled imports", ref checks);

            MidiImportResult pedalQuantized = SmfParser.Parse(Smf(0, 32767, Track(E(0, 0x90, 60, 90), E(0, 0xb0, 67, 60),
                E(1, 0xb0, 67, 10), E(1, 0xb1, 67, 90), E(1, 0xb1, 67, 0), E(1, 0xb0, 67, 20), E(0, 0x80, 60, 0))));
            Check(pedalQuantized.pedalMs.Length == 1 && pedalQuantized.pedalMs[0] == 0 && pedalQuantized.pedalValue[0] == 20,
                "same rounded millisecond pedal changes collapse to stable final maximum", ref checks);

            MidiImportResult pedalTempo = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0xff, 0x51, 3, 7, 0xa1, 0x20), E(480, 0xff, 0x51, 3, 15, 0x42, 0x40), E(480, 0xff, 0x2f, 0)),
                Track(E(0, 0x90, 60, 90), E(480, 0xb0, 64, 100), E(240, 0xb0, 66, 80), E(240, 0xb0, 67, 40), E(0, 0x80, 60, 0))),
                new MidiImportSettings { selectedTracks = new[] { 1 } });
            Check(SamplePedal(pedalTempo, 2, 499) == 0 && SamplePedal(pedalTempo, 2, 500) == 100 && SamplePedal(pedalTempo, 1, 999) == 0 &&
                SamplePedal(pedalTempo, 1, 1000) == 80 && SamplePedal(pedalTempo, 0, 1500) == 40, "pedal timeline uses complete tempo map from unselected conductor", ref checks);

            MidiImportResult visualOnly = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(0, 0xb0, 66, 127), E(0, 0xb0, 67, 127), E(480, 0x80, 60, 0))));
            Check(visualOnly.releaseMs[0] == 500 && SamplePedal(visualOnly, 0, 0) == 127 && SamplePedal(visualOnly, 1, 0) == 127,
                "CC66/67 animations do not add sostenuto or una-corda audio semantics", ref checks);

            MidiImportResult crossTrack = SmfParser.Parse(Smf(1, 480,
                Track(E(0, 0x90, 60, 90)), Track(E(480, 0x80, 60, 0))), new MidiImportSettings { selectedTracks = new[] { 0 } });
            Check(crossTrack.keyUpMs[0] == 500 && crossTrack.repairedKeyUps == 0, "pair before track filtering", ref checks);

            MidiImportResult zero = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(0, 0x80, 60, 0))));
            Check(zero.eventMs[2] == 0 && zero.eventKind[0] == 0 && zero.eventKind[1] == 1 && zero.eventKind[2] == 2, "zero length causal chain", ref checks);

            MidiImportResult quantized = SmfParser.Parse(Smf(0, 32767, Track(E(0, 0x90, 60, 90), E(1, 0x80, 60, 0))));
            Check(quantized.keyUpMs[0] == 0 && quantized.eventKind[0] == 0 && quantized.eventKind[1] == 1, "quantized zero duration", ref checks);

            MidiImportResult boundary = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(960, 0x80, 60, 0))));
            Check(boundary.checkpointEventIndex[1] == 1 && boundary.checkpointActiveOffsets[1] == 0
                && boundary.checkpointActiveOffsets[2] == 1 && boundary.checkpointActiveNoteIds[0] == 0, "CSR before-C release equality", ref checks);

            MidiImportResult exact = SmfParser.Parse(Smf(0, 1000, Track(E(0, 0x90, 60, 90), E(1, 0x80, 60, 0))));
            Check(exact.keyUpMs[0] == 1, "half millisecond rounds upwards", ref checks);

            MidiImportResult repaired = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(480, 0xff, 0x2f, 0))));
            Check(repaired.keyUpMs[0] == 500 && repaired.releaseMs[0] == 500 && repaired.repairedKeyUps == 1, "hanging note repair", ref checks);

            MidiImportResult range = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x99, 60, 90), E(0, 0x90, 10, 90), E(0, 0x90, 60, 90),
                E(480, 0x89, 60, 0), E(0, 0x80, 10, 0), E(0, 0x80, 60, 0))));
            Check(range.startMs.Length == 2 && range.pitch[0] == 10 && range.pitch[1] == 60 && range.skippedPercussion == 1
                && range.skippedOutOfRange == 0, "percussion filtering retains original out-of-piano-range pitch", ref checks);

            var fullRangeEvents = new List<byte[]>();
            for (int pitch = 0; pitch < 128; pitch++) fullRangeEvents.Add(E(0, 0x90, (byte)pitch, 90));
            for (int pitch = 0; pitch < 128; pitch++) fullRangeEvents.Add(E(pitch == 0 ? 480 : 0, 0x80, (byte)pitch, 0));
            MidiImportResult fullRange = SmfParser.Parse(Smf(0, 480, Track(fullRangeEvents.ToArray())));
            Check(fullRange.pitch.Length == 128 && fullRange.eventMs.Length == 384 && fullRange.skippedOutOfRange == 0,
                "complete MIDI pitch range is retained without octave folding", ref checks);
            for (int pitch = 0; pitch < 128; pitch++)
                Check(fullRange.pitch[pitch] == pitch && fullRange.startMs[pitch] == 0 && fullRange.keyUpMs[pitch] == 500
                    && fullRange.releaseMs[pitch] == 500, "full-range note pairing: " + pitch, ref checks);

            MidiImportResult extendedOnly = SmfParser.Parse(Smf(0, 480, Track(E(0, 0x90, 0, 90), E(0, 0x90, 127, 90),
                E(480, 0x80, 0, 0), E(0, 0x80, 127, 0))));
            Check(extendedOnly.pitch.Length == 2 && extendedOnly.pitch[0] == 0 && extendedOnly.pitch[1] == 127,
                "songs wholly outside the 88-key range remain playable", ref checks);

            ExpectFailure(Smf(2, 480, Track(E(0, 0x90, 60, 90))), "Type", ref checks);
            ExpectFailure(Smf(0, 0xe728, Track(E(0, 0x90, 60, 90))), "SMPTE", ref checks);
            ExpectFailure(Smf(0, 480, Track(E(0, 0xff, 0x51, 3, 0, 0, 0))), "Tempo", ref checks);
            ExpectFailure(Smf(0, 480, new byte[] { 0x81, 0x80, 0x80, 0x80, 0 }), "VLQ", ref checks);
            ExpectFailure(Smf(0, 480, new byte[] { 0, 60, 90 }), "running", ref checks);
            ExpectFailure(Smf(0, 480, new byte[] { 0, 0x90, 60 }), "截断", ref checks);
            ExpectFailure(Smf(0, 480, Track(E(0, 0x90, 128, 90))), "状态字节", ref checks);
            ExpectFailure(Smf(0, 480, new byte[] { 0, 0xff, 0x2f, 0, 0, 0x90, 60, 90 }), "EndOfTrack", ref checks);
            ExpectFailure(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(480, 0x80, 60, 0))), "预算", ref checks,
                new MidiImportSettings { maxDataBytes = 4 });
            ExpectFailure(Smf(0, 480, Track(E(0, 0x90, 60, 90), E(0, 0xb0, 67, 20), E(240, 0xb0, 67, 40), E(240, 0xb0, 67, 0), E(0, 0x80, 60, 0))),
                "预算", ref checks, new MidiImportSettings { maxDataBytes = 120 });

            byte[] truncated = Smf(0, 480, Track(E(0, 0x90, 60, 90), E(480, 0x80, 60, 0)));
            Array.Resize(ref truncated, truncated.Length - 1);
            ExpectFailure(truncated, "越界", ref checks);

            return "MidiParserValidation passed: " + checks + " checks (SMF, full MIDI 0..127 pitch range, tempo, FIFO, CC64 audio, three-pedal animation/max/track filtering, CSR, rounding, corruption and budgets).";
        }

        private static int SamplePedal(MidiImportResult result, int pedal, int time)
        {
            int begin = result.pedalOffsets[pedal], end = result.pedalOffsets[pedal + 1];
            int lo = begin, hi = end;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (result.pedalMs[mid] <= time) lo = mid + 1; else hi = mid;
            }
            return lo > begin ? result.pedalValue[lo - 1] : 0;
        }

        private static void Check(bool condition, string name, ref int count)
        {
            if (!condition) throw new InvalidOperationException("MIDI parser validation failed: " + name);
            count++;
        }

        private static void ExpectFailure(byte[] bytes, string text, ref int count, MidiImportSettings settings = null)
        {
            try { SmfParser.Parse(bytes, settings); }
            catch (MidiImportException exception)
            {
                Check(exception.Message.Contains(text), "error context: " + text + ": " + exception.Message, ref count);
                return;
            }
            throw new InvalidOperationException("MIDI parser accepted invalid input: " + text);
        }

        private static byte[] E(int delta, params byte[] message)
        {
            var bytes = new List<byte>();
            var vlq = new List<byte> { (byte)(delta & 127) };
            while ((delta >>= 7) != 0) vlq.Insert(0, (byte)((delta & 127) | 128));
            bytes.AddRange(vlq); bytes.AddRange(message); return bytes.ToArray();
        }

        private static byte[] Track(params byte[][] events)
        {
            var bytes = new List<byte>();
            foreach (byte[] item in events) bytes.AddRange(item);
            if (events.Length == 0 || events[events.Length - 1].Length < 4 || events[events.Length - 1][events[events.Length - 1].Length - 3] != 0xff
                || events[events.Length - 1][events[events.Length - 1].Length - 2] != 0x2f) bytes.AddRange(new byte[] { 0, 0xff, 0x2f, 0 });
            return bytes.ToArray();
        }

        private static byte[] Smf(int format, int division, params byte[][] tracks)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("MThd")); U32(writer, 6); U16(writer, format); U16(writer, tracks.Length); U16(writer, division);
                foreach (byte[] track in tracks) { writer.Write(Encoding.ASCII.GetBytes("MTrk")); U32(writer, track.Length); writer.Write(track); }
                return stream.ToArray();
            }
        }
        private static void U16(BinaryWriter writer, int value) { writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
        private static void U32(BinaryWriter writer, int value) { writer.Write((byte)(value >> 24)); writer.Write((byte)(value >> 16)); writer.Write((byte)(value >> 8)); writer.Write((byte)value); }
    }
}
