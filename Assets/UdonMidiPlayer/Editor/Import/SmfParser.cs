using System;
using System.Collections.Generic;
using System.Text;

namespace MusicMidiPlayer.Editor.Import
{
    /// <summary>Editor-only SMF 0/1 parser. No SDK MIDI importer or runtime dependency.</summary>
    public static class SmfParser
    {
        private sealed class SourceEvent
        {
            public long tick;
            public int track, order, ordinal, kind, channel, a, b, ms;
        }

        private sealed class Note
        {
            public int start, up = -1, release = -1, pitch, velocity, channel, track;
            public int downOrdinal, upOrdinal, releaseOrdinal, releaseSubOrder;
        }

        private sealed class ActionEvent
        {
            public int ms, kind, id, ordinal, subOrder;
        }

        private sealed class Reader
        {
            public readonly byte[] data;
            public int position, end;
            public int track = -1, eventIndex = -1;
            public int totalEvents;
            public Reader(byte[] bytes) { data = bytes; end = bytes.Length; }
            public MidiImportException Error(string message)
            {
                return new MidiImportException(message + " (track=" + track + ", event=" + eventIndex + ", byte=" + position + ")");
            }
            public int Byte()
            {
                if (position >= end) throw Error("MIDI 数据被截断");
                return data[position++];
            }
            public int DataByte()
            {
                int value = Byte();
                if (value >= 128) throw Error("通道事件的数据字节不能是状态字节");
                return value;
            }
            public int U16() { return (Byte() << 8) | Byte(); }
            public uint U32() { return ((uint)Byte() << 24) | ((uint)Byte() << 16) | ((uint)Byte() << 8) | (uint)Byte(); }
            public int Vlq()
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    int next = Byte();
                    value = (value << 7) | (next & 127);
                    if ((next & 128) == 0) return value;
                }
                throw Error("VLQ 超过 4 字节");
            }
            public void Skip(int count)
            {
                if (count < 0 || count > end - position) throw Error("事件或数据块长度越界");
                position += count;
            }
            public string Chunk()
            {
                return new string(new[] { (char)Byte(), (char)Byte(), (char)Byte(), (char)Byte() });
            }
        }

        public static MidiImportResult Parse(byte[] bytes, MidiImportSettings settings = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            settings = settings ?? new MidiImportSettings();
            if (settings.maxNotes < 1 || settings.maxCheckpointEntries < 1 || settings.maxDataBytes < 1)
                throw new MidiImportException("导入预算必须为正数");
            var reader = new Reader(bytes);
            var warnings = new List<string>();
            if (reader.Chunk() != "MThd") throw reader.Error("缺少 MThd 文件头");
            uint headerLength = reader.U32();
            if (headerLength < 6 || headerLength > int.MaxValue) throw reader.Error("MThd 长度无效");
            int format = reader.U16();
            int tracks = reader.U16();
            int division = reader.U16();
            if (format != 0 && format != 1) throw reader.Error("仅支持 SMF Type 0/1，不支持 Type 2");
            if (tracks == 0 || (format == 0 && tracks != 1)) throw reader.Error("文件轨道数量不符合 SMF 格式");
            if ((division & 0x8000) != 0 || division == 0) throw reader.Error("仅支持正 PPQ，不支持 SMPTE 时间基准");
            reader.Skip((int)headerLength - 6);
            var source = new List<SourceEvent>();
            var names = new string[tracks];
            long endTick = 0;
            int trackIndex = 0;
            while (reader.position < bytes.Length)
            {
                reader.end = bytes.Length;
                reader.track = -1;
                string chunk = reader.Chunk();
                uint length = reader.U32();
                if (length > int.MaxValue || length > bytes.Length - reader.position) throw reader.Error("数据块长度越界");
                int chunkEnd = reader.position + (int)length;
                if (chunk != "MTrk")
                {
                    warnings.Add("忽略未知数据块: " + chunk);
                    reader.position = chunkEnd;
                    continue;
                }
                if (trackIndex >= tracks) throw reader.Error("实际 MTrk 数超过文件头声明");
                reader.end = chunkEnd;
                reader.track = trackIndex;
                long trackEnd = ReadTrack(reader, source, names, warnings);
                endTick = Math.Max(endTick, trackEnd);
                reader.position = chunkEnd;
                trackIndex++;
            }
            if (trackIndex != tracks) throw reader.Error("缺少文件头声明的 MTrk");
            for (int i = 0; i < names.Length; i++) if (string.IsNullOrEmpty(names[i])) names[i] = "Track " + i;
            if (settings.selectedTracks != null)
                foreach (int selected in settings.selectedTracks)
                    if (selected < 0 || selected >= tracks) throw new MidiImportException("所选轨道不存在: " + selected);
            source.Sort((x, y) =>
            {
                int cmp = x.tick.CompareTo(y.tick);
                if (cmp == 0) cmp = x.track.CompareTo(y.track);
                return cmp == 0 ? x.order.CompareTo(y.order) : cmp;
            });
            int tempoCount;
            int duration = ConvertTimes(source, endTick, division, warnings, out tempoCount);
            var result = BuildNotes(source, duration, settings, warnings);
            BuildPedalTimeline(source, result, settings, warnings);
            BuildExpressionTimeline(source, result, settings);
            result.format = format;
            result.sourceEventCount = reader.totalEvents;
            result.ppq = division;
            result.measureStartMs = BuildMeasures(source, endTick, division, duration);
            result.tempoChangeCount = tempoCount;
            result.trackNames = names;
            BuildIndexes(result, settings);
            result.diagnostics = warnings.ToArray();
            return result;
        }

        // A signature change starts a new measure, even in an incomplete measure.
        private static int[] BuildMeasures(List<SourceEvent> source, long endTick, int ppq, int duration)
        {
            var signatures = new List<SourceEvent>();
            var timeline = new List<SourceEvent>();
            foreach (SourceEvent item in source)
            {
                if (item.kind == 4)
                {
                    if (signatures.Count > 0 && signatures[signatures.Count - 1].tick == item.tick) signatures[signatures.Count - 1] = item;
                    else signatures.Add(item);
                }
                if (item.kind == 3) timeline.Add(new SourceEvent { tick = item.tick, kind = 3, a = item.a, order = item.ordinal });
            }
            int numerator = 4, denominator = 4, signatureIndex = 0, count = 0;
            decimal cursor = 0;
            while (cursor < endTick)
            {
                while (signatureIndex < signatures.Count && signatures[signatureIndex].tick <= cursor)
                {
                    numerator = signatures[signatureIndex].a; denominator = signatures[signatureIndex].b; signatureIndex++;
                }
                if (++count > 2000000) throw new MidiImportException("小节数量超过预算");
                timeline.Add(new SourceEvent { tick = (long)decimal.Round(cursor, 0, MidpointRounding.AwayFromZero), kind = 5 });
                decimal next = cursor + (decimal)ppq * 4 * numerator / denominator;
                if (signatureIndex < signatures.Count && signatures[signatureIndex].tick < next) next = signatures[signatureIndex].tick;
                cursor = next;
            }
            timeline.Sort((x, y) => { int cmp = x.tick.CompareTo(y.tick); return cmp != 0 ? cmp : x.order.CompareTo(y.order); });
            int ignored;
            ConvertTimes(timeline, endTick, ppq, new List<string>(), out ignored);
            var boundaries = new List<int>();
            boundaries.Add(0);
            foreach (SourceEvent item in timeline)
                if (item.kind == 5 && item.ms > boundaries[boundaries.Count - 1] && item.ms < duration) boundaries.Add(item.ms);
            if (duration > boundaries[boundaries.Count - 1]) boundaries.Add(duration);
            return boundaries.ToArray();
        }

        private static long ReadTrack(Reader r, List<SourceEvent> output, string[] names, List<string> warnings)
        {
            long tick = 0;
            int runningStatus = 0;
            int order = 0;
            bool ended = false;
            var unsupported = new SortedSet<int>();
            while (r.position < r.end)
            {
                r.eventIndex = order;
                tick = checked(tick + r.Vlq());
                int first = r.Byte();
                int status;
                bool usedRunning = first < 128;
                if (usedRunning)
                {
                    if (runningStatus == 0) throw r.Error("running status 缺少前置通道状态");
                    status = runningStatus;
                }
                else status = first;
                if (status >= 0x80 && status <= 0xef)
                {
                    runningStatus = status;
                    int a = usedRunning ? first : r.DataByte();
                    int type = status & 0xf0;
                    int b = (type == 0xc0 || type == 0xd0) ? 0 : r.DataByte();
                    if (type == 0xc0 || (type == 0xb0 && (a == 0 || a == 32)))
                        output.Add(new SourceEvent { tick = tick, track = r.track, order = order, kind = 6,
                            channel = status & 15, a = type == 0xc0 ? 0 : a == 0 ? 1 : 2, b = type == 0xc0 ? a : b });
                    else if (type == 0xb0 && (a == 11 || a == 121))
                        output.Add(new SourceEvent { tick=tick, track=r.track, order=order, kind=5, channel=status&15, a=11, b=a==121?127:b });
                    else if (type == 0x80 || type == 0x90 || (type == 0xb0 && (a == 64 || a == 66 || a == 67)))
                    {
                        int kind = type == 0xb0 ? 2 : (type == 0x80 || b == 0 ? 1 : 0);
                        output.Add(new SourceEvent { tick = tick, track = r.track, order = order,
                            kind = kind, channel = status & 15, a = a, b = b });
                    }
                    else unsupported.Add(type == 0xb0 ? 0x100 + a : type);
                }
                else if (status == 0xff)
                {
                    runningStatus = 0;
                    int metaType = r.Byte();
                    int length = r.Vlq();
                    if (length > r.end - r.position) throw r.Error("Meta 事件长度越界");
                    if (metaType == 0x2f)
                    {
                        if (length != 0) throw r.Error("EndOfTrack 长度必须为 0");
                        ended = true;
                        if (r.position != r.end) throw r.Error("EndOfTrack 后仍存在轨道事件");
                    }
                    else if (metaType == 0x51)
                    {
                        if (length != 3) throw r.Error("Set Tempo 长度必须为 3");
                        int tempo = (r.Byte() << 16) | (r.Byte() << 8) | r.Byte();
                        if (tempo == 0) throw r.Error("Set Tempo 必须大于零");
                        output.Add(new SourceEvent { tick = tick, track = r.track, order = order, kind = 3, a = tempo });
                    }
                    else if (metaType == 0x58)
                    {
                        if (length != 4) throw r.Error("Time Signature 长度必须为 4");
                        int numerator = r.Byte(), exponent = r.Byte();
                        r.Byte(); r.Byte();
                        if (numerator < 1 || exponent > 7) throw r.Error("拍号无效");
                        output.Add(new SourceEvent { tick = tick, track = r.track, order = order, kind = 4, a = numerator, b = 1 << exponent });
                    }
                    else if (metaType == 3)
                    {
                        names[r.track] = Encoding.UTF8.GetString(r.data, r.position, length);
                        r.Skip(length);
                    }
                    else r.Skip(length);
                }
                else if (status == 0xf0 || status == 0xf7)
                {
                    runningStatus = 0;
                    r.Skip(r.Vlq());
                    unsupported.Add(status);
                }
                else throw r.Error("SMF 内含非法系统状态: " + status.ToString("X2"));
                order++;
                r.totalEvents++;
            }
            if (!ended) warnings.Add("轨道 " + r.track + " 缺少 EndOfTrack，使用完整数据块末尾");
            if (unsupported.Count > 0)
            {
                var text = new StringBuilder();
                foreach (int value in unsupported)
                {
                    if (text.Length > 0) text.Append(", ");
                    text.Append(value >= 0x100 ? "CC" + (value - 0x100) : "0x" + value.ToString("X2"));
                }
                warnings.Add("轨道 " + r.track + " 未实现的表达/系统事件: " + text);
            }
            return tick;
        }

        private static int ConvertTimes(List<SourceEvent> source, long endTick, int ppq, List<string> warnings, out int tempoCount)
        {
            long tick = 0;
            decimal weightedTicks = 0;
            int tempo = 500000;
            long lastTempoTick = -1;
            tempoCount = 0;
            for (int i = 0; i < source.Count; i++)
            {
                SourceEvent item = source[i];
                weightedTicks += (decimal)(item.tick - tick) * tempo;
                tick = item.tick;
                item.ms = ToMs(weightedTicks / ppq);
                item.ordinal = i;
                if (item.kind == 3)
                {
                    if (lastTempoTick == tick) warnings.Add("tick " + tick + " 存在多条 Tempo，采用稳定源序最后一条");
                    tempo = item.a;
                    lastTempoTick = tick;
                    tempoCount++;
                }
            }
            weightedTicks += (decimal)(endTick - tick) * tempo;
            return ToMs(weightedTicks / ppq);
        }

        private static int ToMs(decimal microseconds)
        {
            decimal value = decimal.Floor(microseconds / 1000m + 0.5m);
            if (value < 0 || value > int.MaxValue) throw new MidiImportException("曲目时间超过 int 毫秒范围");
            return (int)value;
        }

        private static MidiImportResult BuildNotes(List<SourceEvent> source, int duration, MidiImportSettings settings, List<string> warnings)
        {
            var queues = new Queue<Note>[16 * 128];
            var sustained = new List<Note>[16];
            var pedal = new bool[16];
            var notes = new List<Note>();
            int unmatched = 0;
            for (int i = 0; i < sustained.Length; i++) sustained[i] = new List<Note>();
            foreach (SourceEvent item in source)
            {
                if (item.kind >= 3) continue;
                if (item.kind == 2)
                {
                    // CC66/67 are visual control data only; audio sustain retains its CC64 path.
                    if (item.a != 64) continue;
                    bool down = item.b >= 64;
                    if (pedal[item.channel] && !down)
                    {
                        foreach (Note note in sustained[item.channel])
                        { note.release = item.ms; note.releaseOrdinal = item.ordinal; note.releaseSubOrder = 0; }
                        sustained[item.channel].Clear();
                    }
                    pedal[item.channel] = down;
                    continue;
                }
                int key = item.channel * 128 + item.a;
                Queue<Note> queue = queues[key];
                if (item.kind == 0)
                {
                    if (notes.Count >= settings.maxNotes) throw new MidiImportException("源音符数超过导入预算 " + settings.maxNotes);
                    var note = new Note { start = item.ms, pitch = item.a, velocity = item.b,
                        channel = item.channel, track = item.track, downOrdinal = item.ordinal };
                    notes.Add(note);
                    if (queue == null) queues[key] = queue = new Queue<Note>();
                    queue.Enqueue(note);
                }
                else
                {
                    if (queue == null || queue.Count == 0) { unmatched++; continue; }
                    Note note = queue.Dequeue();
                    note.up = item.ms;
                    note.upOrdinal = item.ordinal;
                    if (pedal[item.channel]) sustained[item.channel].Add(note);
                    else { note.release = item.ms; note.releaseOrdinal = item.ordinal; note.releaseSubOrder = 1; }
                }
            }
            var result = new MidiImportResult { durationMs = duration };
            int syntheticOrdinal = source.Count;
            foreach (Note note in notes)
            {
                if (note.up < 0)
                {
                    note.up = duration; note.upOrdinal = syntheticOrdinal;
                    result.repairedKeyUps++;
                }
                if (note.release < 0)
                {
                    note.release = duration; note.releaseOrdinal = syntheticOrdinal; note.releaseSubOrder = 1;
                    result.repairedReleases++;
                }
            }
            if (unmatched > 0) warnings.Add("跳过没有匹配 NoteOn 的 NoteOff: " + unmatched);
            if (result.repairedKeyUps > 0) warnings.Add("文件末尾补全 KeyUp: " + result.repairedKeyUps);
            if (result.repairedReleases > 0) warnings.Add("文件末尾补全 Release: " + result.repairedReleases);
            var kept = new List<Note>();
            int extendedRangeNotes = 0;
            foreach (Note note in notes)
            {
                if (!settings.IncludesTrack(note.track)) continue;
                if (settings.excludePercussion && note.channel == 9) { result.skippedPercussion++; continue; }
                if (note.pitch < 21 || note.pitch > 108) extendedRangeNotes++;
                kept.Add(note);
            }
            if (result.skippedPercussion > 0) warnings.Add("排除打击乐音符: " + result.skippedPercussion);
            if (extendedRangeNotes > 0) warnings.Add("保留 88 键音域外音符: " + extendedRangeNotes + "（按黑白键规律延伸显示，变调保留原音高）");
            if (kept.Count == 0) throw new MidiImportException("文件没有可播放的音符（检查选轨和通道）");
            kept.Sort((x, y) => { int cmp = x.start.CompareTo(y.start); return cmp != 0 ? cmp : x.downOrdinal.CompareTo(y.downOrdinal); });
            int count = kept.Count;
            result.startMs = new int[count]; result.keyUpMs = new int[count]; result.releaseMs = new int[count];
            result.pitch = new int[count]; result.velocity = new int[count]; result.channel = new int[count]; result.trackIndex = new int[count];
            var actions = new List<ActionEvent>(checked(count * 3));
            for (int i = 0; i < count; i++)
            {
                Note note = kept[i];
                result.startMs[i] = note.start; result.keyUpMs[i] = note.up; result.releaseMs[i] = note.release;
                result.pitch[i] = note.pitch; result.velocity[i] = note.velocity; result.channel[i] = note.channel; result.trackIndex[i] = note.track;
                actions.Add(new ActionEvent { ms = note.start, kind = 0, id = i, ordinal = note.downOrdinal });
                actions.Add(new ActionEvent { ms = note.up, kind = 1, id = i, ordinal = note.upOrdinal });
                actions.Add(new ActionEvent { ms = note.release, kind = 2, id = i, ordinal = note.releaseOrdinal, subOrder = note.releaseSubOrder });
            }
            actions.Sort((x, y) =>
            {
                int cmp = x.ms.CompareTo(y.ms);
                if (cmp == 0) cmp = x.ordinal.CompareTo(y.ordinal);
                if (cmp == 0) cmp = x.subOrder.CompareTo(y.subOrder);
                if (cmp == 0) cmp = x.id.CompareTo(y.id);
                return cmp;
            });
            result.eventMs = new int[actions.Count]; result.eventKind = new int[actions.Count]; result.eventNoteId = new int[actions.Count];
            // Preserve every source timbre event, including separate control tracks and
            // excluded channels. Store a boundary against the final filtered action stream
            // so millisecond rounding never loses Program Change vs NoteOn order.
            var timbre = source.FindAll(item => item.kind == 6);
            int tcount = timbre.Count, actionCursor = 0;
            result.timbreMs = new int[tcount]; result.timbreChannel = new int[tcount]; result.timbreKind = new int[tcount];
            result.timbreValue = new int[tcount]; result.timbreTrackIndex = new int[tcount]; result.timbreEventIndex = new int[tcount];
            for (int i = 0; i < tcount; i++)
            {
                SourceEvent item = timbre[i];
                while (actionCursor < actions.Count && (actions[actionCursor].ms < item.ms ||
                    (actions[actionCursor].ms == item.ms && actions[actionCursor].ordinal < item.ordinal))) actionCursor++;
                result.timbreMs[i] = item.ms; result.timbreChannel[i] = item.channel; result.timbreKind[i] = item.a;
                result.timbreValue[i] = item.b; result.timbreTrackIndex[i] = item.track; result.timbreEventIndex[i] = actionCursor;
            }
            int held = 0, active = 0;
            for (int i = 0; i < actions.Count; i++)
            {
                ActionEvent action = actions[i];
                result.eventMs[i] = action.ms; result.eventKind[i] = action.kind; result.eventNoteId[i] = action.id;
                if (action.kind == 0) { held++; active++; }
                else if (action.kind == 1) held--;
                else active--;
                result.maxKeyPolyphony = Math.Max(result.maxKeyPolyphony, held);
                result.maxActivePolyphony = Math.Max(result.maxActivePolyphony, active);
            }
            return result;
        }

        private static void BuildExpressionTimeline(List<SourceEvent> source, MidiImportResult result, MidiImportSettings settings)
        {
            var times=new List<int>(); var values=new List<int>();
            result.expressionOffsets=new int[17];
            for(int channel=0;channel<16;channel++)
            {
                foreach(var item in source)
                    if(item.kind==5 && item.channel==channel && settings.IncludesTrack(item.track)
                        && !(settings.excludePercussion && channel==9))
                    { times.Add(item.ms); values.Add(item.b); }
                result.expressionOffsets[channel+1]=times.Count;
            }
            result.expressionMs=times.ToArray(); result.expressionValue=values.ToArray();
        }

        private static void BuildPedalTimeline(List<SourceEvent> source, MidiImportResult result, MidiImportSettings settings, List<string> warnings)
        {
            var times = new List<int>[3];
            var values = new List<int>[3];
            for (int i = 0; i < 3; i++) { times[i] = new List<int>(); values[i] = new List<int>(); }
            var channelValues = new int[3, 16];
            var previous = new int[3];
            bool hasVisualOnly = false;
            int changes = 0;
            int cursor = 0;
            while (cursor < source.Count)
            {
                int ms = source[cursor].ms;
                var changed = new bool[3];
                do
                {
                    SourceEvent item = source[cursor++];
                    if (item.kind != 2 || !settings.IncludesTrack(item.track) || item.channel == 9) continue;
                    int pedal = item.a == 67 ? 0 : item.a == 66 ? 1 : 2;
                    channelValues[pedal, item.channel] = item.b;
                    changed[pedal] = true;
                    if (item.a == 66 || item.a == 67) hasVisualOnly = true;
                } while (cursor < source.Count && source[cursor].ms == ms);
                for (int pedal = 0; pedal < 3; pedal++)
                {
                    if (!changed[pedal]) continue;
                    int maximum = 0;
                    for (int channel = 0; channel < 16; channel++) maximum = Math.Max(maximum, channelValues[pedal, channel]);
                    if (maximum == previous[pedal]) continue;
                    times[pedal].Add(ms); values[pedal].Add(maximum); previous[pedal] = maximum;
                    changes++;
                    if (changes * 12L + 16 > settings.maxDataBytes) throw new MidiImportException("踏板动画时间线超过数据预算");
                }
            }
            result.pedalMs = new int[changes]; result.pedalController = new int[changes]; result.pedalValue = new int[changes];
            result.pedalOffsets = new int[4];
            cursor = 0;
            for (int pedal = 0; pedal < 3; pedal++)
            {
                result.pedalOffsets[pedal] = cursor;
                for (int i = 0; i < times[pedal].Count; i++)
                {
                    result.pedalMs[cursor] = times[pedal][i];
                    result.pedalController[cursor] = pedal == 0 ? 67 : pedal == 1 ? 66 : 64;
                    result.pedalValue[cursor++] = values[pedal][i];
                }
            }
            result.pedalOffsets[3] = cursor;
            if (hasVisualOnly) warnings.Add("CC66/CC67 已导入三踏板动画；sostenuto/una corda 的音频语义尚未实现");
        }

        private static void BuildIndexes(MidiImportResult result, MidiImportSettings settings)
        {
            int count = result.durationMs / 1000 + 1;
            if ((long)count * 8 + 4 + (long)result.startMs.Length * 64 + result.pedalMs.Length * 12L + 16 > settings.maxDataBytes)
                throw new MidiImportException("曲目基础数组与检查点超过数据预算");
            result.checkpointEventIndex = new int[count];
            result.checkpointActiveOffsets = new int[count + 1];
            var active = new SortedSet<int>();
            var ids = new List<int>();
            int cursor = 0;
            for (int c = 0; c < count; c++)
            {
                int time = c * 1000;
                while (cursor < result.eventMs.Length && result.eventMs[cursor] < time)
                {
                    int kind = result.eventKind[cursor];
                    int note = result.eventNoteId[cursor];
                    if (kind == 0) active.Add(note);
                    else if (kind == 2) active.Remove(note);
                    cursor++;
                }
                result.checkpointEventIndex[c] = cursor;
                result.checkpointActiveOffsets[c] = ids.Count;
                if (active.Count > settings.maxCheckpointEntries - ids.Count) throw new MidiImportException("检查点活动索引超过预算");
                ids.AddRange(active);
            }
            result.checkpointActiveOffsets[count] = ids.Count;
            result.checkpointActiveNoteIds = ids.ToArray();
            result.estimatedDataBytes = 64L * result.startMs.Length + 4L * (2L * count + 1 + ids.Count) + result.pedalMs.Length * 12L + result.expressionMs.Length * 8L + 84;
            result.estimatedDataBytes += result.timbreMs.Length * 24L;
            if (result.estimatedDataBytes > settings.maxDataBytes) throw new MidiImportException("曲目运行数组超过数据预算: " + result.estimatedDataBytes);
        }
    }
}
