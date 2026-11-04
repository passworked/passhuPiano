using System;
using System.Collections.Generic;
using System.IO;
using MusicMidiPlayer.Editor.Assets;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Baking
{
    /// <summary>
    /// MPC1: flags, note count, duration, delta-time notes, channel/track metadata,
    /// delta-ZigZag event tokens, exact checkpoints, three pedal segments, optional
    /// extension TLVs, then little-endian FNV-1a32. Times remain integer milliseconds.
    /// </summary>
    public static class MidiSongCodec
    {
        public const int FormatVersion = 1;
        public const string RuntimeOutputFolder = "Assets/UdonMidiPlayer/Generated/Songs";

        public sealed class DecodedSong
        {
            public byte[] violinArticulations = Array.Empty<byte>();
            public int noteCount, durationMs;
            public int[] startMs, keyUpMs, releaseMs, pitch, velocity, channel, trackIndex;
            public int[] eventMs, eventKind, eventNoteId;
            public int[] checkpointEventIndex, checkpointActiveOffsets, checkpointActiveNoteIds;
            public int[] pedalMs, pedalController, pedalValue, pedalOffsets;
            public int[] expressionMs = Array.Empty<int>(), expressionValue = Array.Empty<int>();
            public int[] expressionOffsets = new int[17];
            public int timbreDataVersion;
            public int[] timbreMs = Array.Empty<int>(), timbreChannel = Array.Empty<int>(), timbreKind = Array.Empty<int>();
            public int[] timbreValue = Array.Empty<int>(), timbreTrackIndex = Array.Empty<int>(), timbreEventIndex = Array.Empty<int>();
        }

        public static byte[] Encode(MidiSongAsset source)
        {
            MidiDataBaker.ValidateSong(source, false);
            int n = source.startMs.Length;
            if (n > int.MaxValue / 4) throw new InvalidDataException("MPC1 音符数量超出事件索引范围");
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 77, 80, 67, 49, 1 });
            WriteInt(bytes, n); WriteInt(bytes, source.durationMs);
            int previous = 0;
            for (int i = 0; i < n; i++)
            {
                WriteInt(bytes, source.startMs[i] - previous);
                WriteInt(bytes, source.keyUpMs[i] - source.startMs[i]);
                WriteInt(bytes, source.releaseMs[i] - source.keyUpMs[i]);
                bytes.Add((byte)source.pitch[i]); bytes.Add((byte)source.velocity[i]);
                previous = source.startMs[i];
            }
            for (int i = 0; i < n; i++)
            {
                bytes.Add((byte)source.channel[i]); WriteInt(bytes, source.trackIndex[i]);
            }
            previous = 0;
            for (int i = 0; i < source.eventMs.Length; i++)
            {
                int token = checked(source.eventNoteId[i] * 4 + source.eventKind[i]);
                long delta = (long)token - previous;
                WriteUInt(bytes, checked((uint)(delta >= 0 ? delta * 2 : -delta * 2 - 1)));
                previous = token;
            }
            WriteArray(bytes, source.checkpointEventIndex);
            WriteArray(bytes, source.checkpointActiveOffsets);
            WriteArray(bytes, source.checkpointActiveNoteIds);
            for (int pedal = 0; pedal < 3; pedal++)
            {
                int begin = source.pedalOffsets[pedal], end = source.pedalOffsets[pedal + 1];
                WriteInt(bytes, end - begin); previous = 0;
                for (int i = begin; i < end; i++)
                {
                    WriteInt(bytes, source.pedalMs[i] - previous);
                    bytes.Add((byte)source.pedalValue[i]); previous = source.pedalMs[i];
                }
            }
            bool expression=source.expressionMs!=null && source.expressionMs.Length>0;
            WriteInt(bytes, 1 + (expression ? 1 : 0) + (source.timbreDataVersion == 1 ? 1 : 0));
            if(expression)
            {
                var payload=new List<byte>(); WriteInt(payload,source.expressionMs.Length);
                for(int channel=0;channel<16;channel++)
                {
                    int first=source.expressionOffsets[channel],last=source.expressionOffsets[channel+1];
                    WriteInt(payload,last-first); previous=0;
                    for(int i=first;i<last;i++) { WriteInt(payload,source.expressionMs[i]-previous); payload.Add((byte)source.expressionValue[i]); previous=source.expressionMs[i]; }
                }
                WriteUInt(bytes,2); WriteInt(bytes,payload.Count); bytes.AddRange(payload);
            }
            if (source.timbreDataVersion == 1)
            {
                var payload = new List<byte>(); WriteInt(payload, source.timbreMs.Length);
                int previousTime = 0, previousEvent = 0;
                for (int i = 0; i < source.timbreMs.Length; i++)
                {
                    WriteInt(payload, source.timbreMs[i] - previousTime);
                    payload.Add((byte)(source.timbreChannel[i] * 4 + source.timbreKind[i]));
                    payload.Add((byte)source.timbreValue[i]); WriteInt(payload, source.timbreTrackIndex[i]);
                    WriteInt(payload, source.timbreEventIndex[i] - previousEvent);
                    previousTime = source.timbreMs[i]; previousEvent = source.timbreEventIndex[i];
                }
                WriteUInt(bytes, 4); WriteInt(bytes, payload.Count); bytes.AddRange(payload);
            }
            byte[] violinStyles = ViolinArticulationBaker.Bake(source);
            WriteUInt(bytes, 6); WriteInt(bytes, violinStyles.Length + 1);
            bytes.Add(1); bytes.AddRange(violinStyles);
            uint hash = Hash(bytes, bytes.Count);
            for (int i = 0; i < 4; i++) bytes.Add((byte)(hash >> (8 * i)));
            return bytes.ToArray();
        }

        public static DecodedSong Decode(byte[] packed)
        {
            if (packed == null || packed.Length < 13) throw new InvalidDataException("MPC1 数据被截断");
            int end = packed.Length - 4;
            uint expected = (uint)packed[end] | (uint)packed[end + 1] << 8 |
                (uint)packed[end + 2] << 16 | (uint)packed[end + 3] << 24;
            if (Hash(packed, end) != expected) throw new InvalidDataException("MPC1 校验和不匹配");
            var reader = new Reader(packed, end);
            if (reader.Byte() != 77 || reader.Byte() != 80 || reader.Byte() != 67 || reader.Byte() != 49)
                throw new InvalidDataException("不支持的 MPC 数据版本");
            if (reader.Byte() != 1) throw new InvalidDataException("不支持的 MPC1 标志");
            int n = reader.Int(), duration = reader.Int();
            // Each note needs at least five note bytes, two metadata bytes and three event bytes.
            if (n < 1 || n > int.MaxValue / 4 || n > reader.Remaining / 10)
                throw new InvalidDataException("MPC1 音符数量与数据边界不符");
            int k = duration / 1000 + 1;
            if ((long)k * 2 + 1 > reader.Remaining) throw new InvalidDataException("MPC1 检查点数量越界");
            var result = new DecodedSong
            {
                noteCount = n, durationMs = duration,
                startMs = new int[n], keyUpMs = new int[n], releaseMs = new int[n],
                pitch = new int[n], velocity = new int[n], channel = new int[n], trackIndex = new int[n],
                eventMs = new int[checked(n * 3)], eventKind = new int[checked(n * 3)], eventNoteId = new int[checked(n * 3)]
            };
            int previous = 0;
            for (int i = 0; i < n; i++)
            {
                int start = CheckedAdd(previous, reader.Int()), up = CheckedAdd(start, reader.Int());
                int release = CheckedAdd(up, reader.Int());
                if (release > duration) throw new InvalidDataException("MPC1 音符时间超过曲目时长");
                result.startMs[i] = start; result.keyUpMs[i] = up; result.releaseMs[i] = release;
                result.pitch[i] = reader.Byte(); result.velocity[i] = reader.Byte(); previous = start;
            }
            for (int i = 0; i < n; i++) { result.channel[i] = reader.Byte(); result.trackIndex[i] = reader.Int(); }
            previous = 0;
            for (int i = 0; i < result.eventMs.Length; i++)
            {
                uint value = reader.UInt();
                long delta = (value & 1) == 0 ? (long)(value >> 1) : -(long)(value >> 1) - 1;
                long token = previous + delta;
                if (token < 0 || token > int.MaxValue) throw new InvalidDataException("MPC1 事件差值越界");
                int id = (int)token / 4, kind = (int)token % 4;
                if (id >= n || kind > 2) throw new InvalidDataException("MPC1 事件索引或类型无效");
                result.eventNoteId[i] = id; result.eventKind[i] = kind;
                result.eventMs[i] = kind == 0 ? result.startMs[id] : kind == 1 ? result.keyUpMs[id] : result.releaseMs[id];
                previous = (int)token;
            }
            result.checkpointEventIndex = reader.Array(k);
            result.checkpointActiveOffsets = reader.Array(k + 1);
            int activeCount = result.checkpointActiveOffsets[k];
            result.checkpointActiveNoteIds = reader.Array(activeCount);
            var pedalMs = new List<int>(); var pedalController = new List<int>(); var pedalValue = new List<int>();
            result.pedalOffsets = new int[4];
            for (int pedal = 0; pedal < 3; pedal++)
            {
                int count = reader.Int();
                if (count > reader.Remaining / 2) throw new InvalidDataException("MPC1 踏板数量越界");
                previous = 0;
                for (int i = 0; i < count; i++)
                {
                    previous = CheckedAdd(previous, reader.Int());
                    pedalMs.Add(previous); pedalController.Add(pedal == 0 ? 67 : pedal == 1 ? 66 : 64);
                    pedalValue.Add(reader.Byte());
                }
                result.pedalOffsets[pedal + 1] = pedalMs.Count;
            }
            result.pedalMs = pedalMs.ToArray(); result.pedalController = pedalController.ToArray(); result.pedalValue = pedalValue.ToArray();
            int extensionCount = reader.Int();
            if (extensionCount > reader.Remaining / 2) throw new InvalidDataException("MPC1 扩展数量越界");
            for (int i = 0; i < extensionCount; i++)
            {
                uint tag = reader.UInt(); int length = reader.Int();
                if ((tag & 1) != 0) throw new InvalidDataException("MPC1 包含不支持的必需扩展");
                if(tag==2)
                {
                    int remaining=reader.Remaining; int nExpression=reader.Int();
                    if(nExpression>length/2) throw new InvalidDataException("MPC1 表达数量越界");
                    result.expressionMs=new int[nExpression]; result.expressionValue=new int[nExpression];
                    int cursor=0;
                    for(int channel=0;channel<16;channel++)
                    {
                        int count=reader.Int(); previous=0;
                        if(count>nExpression-cursor) throw new InvalidDataException("MPC1 表达通道数量越界");
                        for(int j=0;j<count;j++) { previous=CheckedAdd(previous,reader.Int()); int value=reader.Byte(); if(previous>duration || value>127) throw new InvalidDataException("MPC1 表达值无效"); result.expressionMs[cursor]=previous; result.expressionValue[cursor++]=value; }
                        result.expressionOffsets[channel+1]=cursor;
                    }
                    if(cursor!=nExpression || remaining-reader.Remaining!=length) throw new InvalidDataException("MPC1 表达段长度无效");
                }
                else if (tag == 4)
                {
                    if (result.timbreDataVersion != 0 || length > reader.Remaining) throw new InvalidDataException("MPC1 音色扩展重复或越界");
                    int remaining = reader.Remaining, count = reader.Int();
                    if (count > length / 5) throw new InvalidDataException("MPC1 音色数量越界");
                    result.timbreDataVersion = 1;
                    result.timbreMs = new int[count]; result.timbreChannel = new int[count]; result.timbreKind = new int[count];
                    result.timbreValue = new int[count]; result.timbreTrackIndex = new int[count]; result.timbreEventIndex = new int[count];
                    int time = 0, boundary = 0;
                    for (int j = 0; j < count; j++)
                    {
                        time = CheckedAdd(time, reader.Int()); int descriptor = reader.Byte(); int value = reader.Byte();
                        int track = reader.Int(); boundary = CheckedAdd(boundary, reader.Int());
                        if (time > duration || descriptor > 62 || (descriptor & 3) > 2 || value > 127 || boundary > n * 3)
                            throw new InvalidDataException("MPC1 音色事件无效");
                        result.timbreMs[j] = time; result.timbreChannel[j] = descriptor >> 2; result.timbreKind[j] = descriptor & 3;
                        result.timbreValue[j] = value; result.timbreTrackIndex[j] = track; result.timbreEventIndex[j] = boundary;
                    }
                    if (remaining - reader.Remaining != length) throw new InvalidDataException("MPC1 音色段长度无效");
                }
                else if (tag == 6)
                {
                    if (result.violinArticulations.Length != 0 || length != (n + 3) / 4 + 1 || length > reader.Remaining || reader.Byte() != 1)
                        throw new InvalidDataException("MPC1 violin articulation extension is invalid or duplicated");
                    result.violinArticulations = new byte[length - 1];
                    for (int j = 0; j < length - 1; j++) result.violinArticulations[j] = (byte)reader.Byte();
                }
                else reader.Skip(length);
            }
            if (reader.Remaining != 0) throw new InvalidDataException("MPC1 存在未声明的尾部数据");
            ValidateDecoded(result);
            return result;
        }

        public static void ValidateRoundTrip(MidiSongAsset source, byte[] packed = null)
        {
            DecodedSong decoded = Decode(packed ?? Encode(source));
            byte[] styles = ViolinArticulationBaker.Bake(source);
            if (styles.Length != decoded.violinArticulations.Length) throw new InvalidDataException("MPC1 violin articulation length mismatch");
            for (int i = 0; i < styles.Length; i++)
                if (styles[i] != decoded.violinArticulations[i]) throw new InvalidDataException("MPC1 violin articulation round-trip mismatch");
            if (source.durationMs != decoded.durationMs || source.startMs.Length != decoded.noteCount)
                throw new InvalidDataException("MPC1 曲目元数据往返不一致");
            if (source.timbreDataVersion != decoded.timbreDataVersion) throw new InvalidDataException("MPC1 音色版本往返不一致");
            int[][] original = Arrays(source), restored = Arrays(decoded);
            for (int array = 0; array < original.Length; array++)
            {
                if (original[array].Length != restored[array].Length) throw new InvalidDataException("MPC1 数组长度往返不一致: " + array);
                for (int i = 0; i < original[array].Length; i++)
                    if (original[array][i] != restored[array][i]) throw new InvalidDataException("MPC1 数组数值往返不一致: array=" + array + ", index=" + i);
            }
        }

        /// <summary>Explicit validation entry point; does not create or alter project assets.</summary>
        public static void ValidateBoundaryCases()
        {
            MidiSongAsset source = ScriptableObject.CreateInstance<MidiSongAsset>();
            try
            {
                const int n = 16385;
                source.sourceHash = new string('0', 64); source.durationMs = 0;
                source.startMs = new int[n]; source.keyUpMs = new int[n]; source.releaseMs = new int[n];
                source.pitch = new int[n]; source.velocity = new int[n]; source.channel = new int[n]; source.trackIndex = new int[n];
                source.eventMs = new int[n * 3]; source.eventKind = new int[n * 3]; source.eventNoteId = new int[n * 3];
                source.checkpointEventIndex = new[] { 0 }; source.checkpointActiveOffsets = new[] { 0, 0 };
                source.checkpointActiveNoteIds = new int[0]; source.pedalOffsets = new int[4];
                for (int i = 0; i < n; i++)
                {
                    source.pitch[i] = i % 2 == 0 ? 0 : 127; source.velocity[i] = i % 2 == 0 ? 1 : 127;
                    source.channel[i] = i % 2 == 0 ? 0 : 15;
                    source.trackIndex[i] = i % 4 == 0 ? int.MaxValue : i % 4 == 1 ? 256 : i % 4 == 2 ? 127 : 128;
                    for (int kind = 0; kind < 3; kind++) { source.eventNoteId[i * 3 + kind] = i; source.eventKind[i * 3 + kind] = kind; }
                }
                byte[] packed = Encode(source); ValidateRoundTrip(source, packed);
                byte[] damaged = (byte[])packed.Clone(); damaged[damaged.Length / 2] ^= 1; RequireReject(damaged);
                byte[] truncated = new byte[packed.Length - 1]; Array.Copy(packed, truncated, truncated.Length); RequireReject(truncated);
                byte[] unsupported = (byte[])packed.Clone(); unsupported[3] = 50; ReplaceChecksum(unsupported); RequireReject(unsupported);
                byte[] flags = (byte[])packed.Clone(); flags[4] = 3; ReplaceChecksum(flags); RequireReject(flags);
                // Unknown optional extension is skippable; unknown required extension must fail.
                byte[] optional = AddTestExtension(packed, 8, (n + 3) / 4 + 1); ValidateRoundTrip(source, optional);
                RequireReject(AddTestExtension(packed, 3, (n + 3) / 4 + 1));
                RequireReject(AddTestExtension(packed, 6, (n + 3) / 4 + 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static void ValidateDecoded(DecodedSong decoded)
        {
            MidiSongAsset staged = ScriptableObject.CreateInstance<MidiSongAsset>();
            try
            {
                staged.sourceHash = new string('0', 64); staged.durationMs = decoded.durationMs;
                staged.timbreDataVersion = decoded.timbreDataVersion;
                staged.timbreMs = decoded.timbreMs; staged.timbreChannel = decoded.timbreChannel; staged.timbreKind = decoded.timbreKind;
                staged.timbreValue = decoded.timbreValue; staged.timbreTrackIndex = decoded.timbreTrackIndex; staged.timbreEventIndex = decoded.timbreEventIndex;
                staged.startMs = decoded.startMs; staged.keyUpMs = decoded.keyUpMs; staged.releaseMs = decoded.releaseMs;
                staged.pitch = decoded.pitch; staged.velocity = decoded.velocity; staged.channel = decoded.channel; staged.trackIndex = decoded.trackIndex;
                staged.eventMs = decoded.eventMs; staged.eventKind = decoded.eventKind; staged.eventNoteId = decoded.eventNoteId;
                staged.checkpointEventIndex = decoded.checkpointEventIndex; staged.checkpointActiveOffsets = decoded.checkpointActiveOffsets;
                staged.checkpointActiveNoteIds = decoded.checkpointActiveNoteIds;
                staged.pedalMs = decoded.pedalMs; staged.pedalController = decoded.pedalController;
                staged.pedalValue = decoded.pedalValue; staged.pedalOffsets = decoded.pedalOffsets;
                staged.expressionMs = decoded.expressionMs; staged.expressionValue = decoded.expressionValue; staged.expressionOffsets = decoded.expressionOffsets;
                MidiDataBaker.ValidateSong(staged, false);
            }
            finally { UnityEngine.Object.DestroyImmediate(staged); }
        }

        private static int[][] Arrays(MidiSongAsset s)
        {
            return new[] { s.startMs, s.keyUpMs, s.releaseMs, s.pitch, s.velocity, s.channel, s.trackIndex,
                s.eventMs, s.eventKind, s.eventNoteId, s.checkpointEventIndex, s.checkpointActiveOffsets,
                s.checkpointActiveNoteIds, s.pedalMs, s.pedalController, s.pedalValue, s.pedalOffsets, s.expressionMs, s.expressionValue, s.expressionOffsets,
                s.timbreMs, s.timbreChannel, s.timbreKind, s.timbreValue, s.timbreTrackIndex, s.timbreEventIndex };
        }
        private static int[][] Arrays(DecodedSong s)
        {
            return new[] { s.startMs, s.keyUpMs, s.releaseMs, s.pitch, s.velocity, s.channel, s.trackIndex,
                s.eventMs, s.eventKind, s.eventNoteId, s.checkpointEventIndex, s.checkpointActiveOffsets,
                s.checkpointActiveNoteIds, s.pedalMs, s.pedalController, s.pedalValue, s.pedalOffsets, s.expressionMs, s.expressionValue, s.expressionOffsets,
                s.timbreMs, s.timbreChannel, s.timbreKind, s.timbreValue, s.timbreTrackIndex, s.timbreEventIndex };
        }
        private static int CheckedAdd(int a, int b)
        {
            if (b > int.MaxValue - a) throw new InvalidDataException("MPC1 时间差值溢出");
            return a + b;
        }
        private static void WriteArray(List<byte> bytes, int[] values) { foreach (int value in values) WriteInt(bytes, value); }
        private static void WriteInt(List<byte> bytes, int value)
        {
            if (value < 0) throw new InvalidDataException("MPC1 无符号字段不能为负");
            WriteUInt(bytes, (uint)value);
        }
        private static void WriteUInt(List<byte> bytes, uint value)
        {
            while (value >= 128) { bytes.Add((byte)((value & 127) | 128)); value >>= 7; }
            bytes.Add((byte)value);
        }
        private static uint Hash(IList<byte> bytes, int count)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < count; i++) hash = unchecked((hash ^ bytes[i]) * 16777619u);
            return hash;
        }
        private static void ReplaceChecksum(byte[] bytes)
        {
            uint hash = Hash(bytes, bytes.Length - 4);
            for (int i = 0; i < 4; i++) bytes[bytes.Length - 4 + i] = (byte)(hash >> (8 * i));
        }
        private static byte[] AddTestExtension(byte[] original, byte tag, int payloadLength)
        {
            var bytes = new List<byte>(original);
            // This boundary fixture has exactly one extension: tag 6, one version byte, packed styles.
            int countOffset = bytes.Count - 4 - payloadLength - 3 - 1;
            bytes[countOffset]++;
            bytes.RemoveRange(bytes.Count - 4, 4);
            bytes.AddRange(new byte[] { tag, 1, 42, 0, 0, 0, 0 });
            byte[] result = bytes.ToArray(); ReplaceChecksum(result); return result;
        }
        private static void RequireReject(byte[] bytes)
        {
            try { Decode(bytes); }
            catch (InvalidDataException) { return; }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("MPC1 损坏/不支持的数据没有被拒绝");
        }

        private sealed class Reader
        {
            private readonly byte[] bytes;
            private readonly int end;
            private int position;
            public int Remaining { get { return end - position; } }
            public Reader(byte[] data, int limit) { bytes = data; end = limit; }
            public int Byte()
            {
                if (position >= end) throw new InvalidDataException("MPC1 数据被截断");
                return bytes[position++];
            }
            public uint UInt()
            {
                uint value = 0;
                for (int i = 0; i < 5; i++)
                {
                    int next = Byte();
                    if (i == 4 && (next & 240) != 0) throw new InvalidDataException("MPC1 varint 溢出");
                    value |= (uint)(next & 127) << (i * 7);
                    if ((next & 128) == 0)
                    {
                        if (i > 0 && (next & 127) == 0) throw new InvalidDataException("MPC1 varint 不是最短编码");
                        return value;
                    }
                }
                throw new InvalidDataException("MPC1 varint 过长");
            }
            public int Int()
            {
                uint value = UInt();
                if (value > int.MaxValue) throw new InvalidDataException("MPC1 整数字段溢出");
                return (int)value;
            }
            public int[] Array(int count)
            {
                if (count < 0 || count > Remaining) throw new InvalidDataException("MPC1 数组边界无效");
                var values = new int[count];
                for (int i = 0; i < count; i++) values[i] = Int();
                return values;
            }
            public void Skip(int count)
            {
                if (count < 0 || count > Remaining) throw new InvalidDataException("MPC1 扩展长度越界");
                position += count;
            }
        }
    }
}
