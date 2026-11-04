using System;
using System.Collections.Generic;
using MusicMidiPlayer.Editor.Assets;

namespace MusicMidiPlayer.Editor.Baking
{
    // All phrase analysis runs in the Editor. Two bits per original note, independent of routing.
    public static class ViolinArticulationBaker
    {
        public const string RulesVersion = "violin-auto-v1:track-channel-program;chord12;phrase1200;median7;short220-gate65;pizz160-gate35-gap100-vel80-run3;short-run2";

        private sealed class Attack
        {
            public int start, end, velocity, candidate, style;
            public readonly List<int> notes = new List<int>();
        }

        public static byte[] Bake(MidiSongAsset song)
        {
            int count = song.startMs.Length;
            var packed = new byte[(count + 3) / 4];
            var streams = new Dictionary<long, List<Attack>>();
            var programEpochs = new int[16];
            int timbre = 0;
            for (int e = 0; e < song.eventMs.Length; e++)
            {
                while (timbre < song.timbreEventIndex.Length && song.timbreEventIndex[timbre] <= e)
                {
                    if (song.timbreKind[timbre] == 0) programEpochs[song.timbreChannel[timbre]]++;
                    timbre++;
                }
                if (song.eventKind[e] != 0) continue;
                int n = song.eventNoteId[e], channel = song.channel[n];
                if (channel == 9) continue;
                // A channel-wide Program Change, even on a control-only track, starts a new stream.
                long key = ((long)song.trackIndex[n] * 16 + channel) * (song.timbreEventIndex.Length + 1L) + programEpochs[channel];
                if (!streams.TryGetValue(key, out var attacks)) streams[key] = attacks = new List<Attack>();
                Attack a = attacks.Count == 0 ? null : attacks[attacks.Count - 1];
                if (a == null || song.startMs[n] - a.start > 12)
                {
                    a = new Attack { start = song.startMs[n] };
                    attacks.Add(a);
                }
                a.end = Math.Max(a.end, song.keyUpMs[n]); // Never let a short chord tone truncate a held tone.
                a.velocity = Math.Max(a.velocity, song.velocity[n]);
                a.notes.Add(n);
            }
            foreach (var attacks in streams.Values)
            {
                int first = 0;
                while (first < attacks.Count)
                {
                    int last = first + 1;
                    while (last < attacks.Count && attacks[last].start - attacks[last - 1].start < 1200) last++;
                    BakePhrase(attacks, first, last, packed);
                    first = last;
                }
            }
            return packed;
        }

        private static void BakePhrase(List<Attack> attacks, int first, int last, byte[] packed)
        {
            for (int i = first; i < last; i++)
            {
                var intervals = new List<int>();
                for (int j = Math.Max(first, i - 3); j < Math.Min(last - 1, i + 4); j++)
                    intervals.Add(attacks[j + 1].start - attacks[j].start);
                intervals.Sort();
                var a = attacks[i];
                int held = a.end - a.start;
                int pulse = intervals.Count == 0 ? 0 : intervals[intervals.Count / 2];
                int next = i + 1 < last ? attacks[i + 1].start - a.start : pulse;
                // Require a real gap and both absolute and local rhythmic evidence. Default to sustain.
                if (pulse > 0 && held <= 220 && held <= pulse * .65 && held <= next * .65)
                {
                    a.candidate = held <= 160 && held <= pulse * .35 && next - held >= 100 && a.velocity <= 80 ? 2 : 1;
                }
            }
            // Confirm complete runs offline, including their first note; isolated short notes remain bowed.
            for (int i = first; i < last;)
            {
                int end = i + 1, candidate = attacks[i].candidate;
                while (end < last && attacks[end].candidate == candidate) end++;
                int style = candidate == 2 ? (end - i >= 3 ? 2 : end - i >= 2 ? 1 : 0)
                    : candidate == 1 && end - i >= 2 ? 1 : 0;
                for (int j = i; j < end; j++) attacks[j].style = style;
                i = end;
            }
            for (int i = first; i < last; i++)
                foreach (int n in attacks[i].notes)
                    packed[n >> 2] |= (byte)((attacks[i].style + 1) << ((n & 3) * 2));
        }
    }
}
