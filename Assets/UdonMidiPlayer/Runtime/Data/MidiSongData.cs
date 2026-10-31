using System;
using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/Data/Song")]
    public class MidiSongData : UdonSharpBehaviour
    {
        public string songId;
        public string title;
        public string author = "";
        public string contentHash;
        public string sourceHash;
        public int schemaVersion = 2;
        public int durationMs;
        public int[] measureStartMs = new int[0];
        public string[] trackNames = new string[0];
        public TextAsset packedData;
        public int packedFormatVersion = 1;
        public int noteCount;
        public int packedDataBytes;
        [NonSerialized] public bool isDecoded;
        [NonSerialized] public string decodeError = "";
        // Playback arrays belong only to the currently loaded song; no expanded arrays are baked.
        [NonSerialized] public int[] startMs = new int[0];
        [NonSerialized] public int[] keyUpMs = new int[0];
        [NonSerialized] public int[] releaseMs = new int[0];
        [NonSerialized] public int[] pitch = new int[0];
        [NonSerialized] public int[] velocity = new int[0];
        [NonSerialized] public int[] channel = new int[0];
        [NonSerialized] public int[] trackIndex = new int[0];
        [NonSerialized] public int[] eventMs = new int[0];
        [NonSerialized] public int[] eventKind = new int[0];
        [NonSerialized] public int[] eventNoteId = new int[0];
        [NonSerialized] public int[] checkpointEventIndex = new int[0];
        [NonSerialized] public int[] checkpointActiveOffsets = new int[0];
        [NonSerialized] public int[] checkpointActiveNoteIds = new int[0];
        [NonSerialized] public int[] pedalMs = new int[0];
        [NonSerialized] public int[] pedalController = new int[0];
        [NonSerialized] public int[] pedalValue = new int[0];
        [NonSerialized] public int[] pedalOffsets = new int[0];
        [NonSerialized] public int[] expressionMs = new int[0];
        [NonSerialized] public int[] expressionValue = new int[0];
        [NonSerialized] public int[] expressionOffsets = new int[17];
        // Optional MPC1 tag 4 metadata; the instrument router consumes its original event boundaries.
        [NonSerialized] public int timbreDataVersion;
        [NonSerialized] public int[] timbreMs = new int[0], timbreChannel = new int[0], timbreKind = new int[0];
        [NonSerialized] public int[] timbreValue = new int[0], timbreTrackIndex = new int[0], timbreEventIndex = new int[0];

        private byte[] _packedBytes;
        private byte[] _eventPhases;
        private int _packedCursor;
        private int _packedEnd;
        private uint _packedChecksum;
        private int _decodeStage = -1;
        private int _decodeIndex;
        private int _previousTime;
        private int _previousToken;
        private int _checkpointCount;
        private int _pedalSegment;
        private int[] _pedalTimes0;
        private int[] _pedalTimes1;
        private int[] _pedalTimes2;
        private int[] _pedalValues0;
        private int[] _pedalValues1;
        private int[] _pedalValues2;
        private int _extensionCount;
        private int _extensionBytes;
        private int _expressionEnd, _expressionChannel, _expressionWrite, _expressionRemaining;
        private bool _hasExpressionExtension;
        private int _timbreEnd, _timbrePreviousEvent;

        public int ExpressionAt(int channel, int ms)
        {
            if(channel<0 || channel>=16 || expressionOffsets==null || expressionOffsets.Length!=17) return 127;
            int first=expressionOffsets[channel],lo=first,hi=expressionOffsets[channel+1];
            while(lo<hi) { int mid=lo+(hi-lo)/2; if(expressionMs[mid]<=ms) lo=mid+1; else hi=mid; }
            return lo>first?expressionValue[lo-1]:127;
        }

        public bool HasPackedData()
        {
            return packedData != null && packedFormatVersion == 1 && noteCount > 0 && packedDataBytes > 0;
        }

        /// <summary>Starts MPC1 decoding. The caller must drive DecodeStep and inspect decodeError.</summary>
        public bool BeginDecode()
        {
            ReleaseDecodedData();
            if (schemaVersion != 2 || string.IsNullOrEmpty(songId) || string.IsNullOrEmpty(sourceHash) || sourceHash.Length != 64 || durationMs < 0)
            {
                FailDecode("曲目描述无效");
                return false;
            }
            if (!HasPackedData())
            {
                FailDecode("曲目压缩数据缺失或版本不受支持");
                return false;
            }
            _packedBytes = packedData.bytes;
            if (_packedBytes == null || _packedBytes.Length != packedDataBytes || _packedBytes.Length < 14)
            {
                FailDecode("曲目压缩数据长度无效");
                return false;
            }
            _packedEnd = _packedBytes.Length - 4;
            _packedChecksum = 2166136261u;
            _decodeStage = 0;
            if (ReadPackedByte() != 77 || ReadPackedByte() != 80 || ReadPackedByte() != 67 || ReadPackedByte() != 49 || ReadPackedByte() != 1)
            {
                FailDecode("曲目压缩格式或必需功能不受支持");
                return false;
            }
            int count = ReadPackedInt();
            int duration = ReadPackedInt();
            if (_decodeStage < 0) return false;
            // At least ten bytes per note (note, channel/track, and three event tokens).
            if (count != noteCount || duration != durationMs || count < 1 || count > int.MaxValue / 4 || count > (_packedEnd - _packedCursor) / 10)
            {
                FailDecode("曲目压缩头与描述不一致");
                return false;
            }
            _checkpointCount = durationMs / 1000 + 1;
            if (_checkpointCount > (_packedEnd - _packedCursor - count * 10) / 2)
            {
                FailDecode("曲目检查点数量超出数据长度");
                return false;
            }
            startMs = new int[count]; keyUpMs = new int[count]; releaseMs = new int[count];
            pitch = new int[count]; velocity = new int[count]; channel = new int[count]; trackIndex = new int[count];
            eventMs = new int[count * 3]; eventKind = new int[count * 3]; eventNoteId = new int[count * 3];
            checkpointEventIndex = new int[_checkpointCount];
            checkpointActiveOffsets = new int[_checkpointCount + 1];
            pedalOffsets = new int[4];
            _eventPhases = new byte[count];
            return true;
        }

        /// <summary>Consumes at most budget records; true means decoded, errors are in decodeError.</summary>
        public bool DecodeStep(int budget)
        {
            if (isDecoded) return true;
            if (_decodeStage < 0 || _packedBytes == null) return false;
            budget = Mathf.Clamp(budget, 1, 8192);
            while (budget-- > 0 && _decodeStage >= 0)
            {
                if (_decodeStage == 0)
                {
                    int delta = ReadPackedInt();
                    int held = ReadPackedInt();
                    int tail = ReadPackedInt();
                    int key = ReadPackedByte();
                    int strength = ReadPackedByte();
                    if (_decodeStage < 0) return false;
                    if (delta > durationMs - _previousTime || held > durationMs - _previousTime - delta ||
                        tail > durationMs - _previousTime - delta - held || key > 127 || strength < 1 || strength > 127)
                    {
                        FailDecode("曲目音符时间或音高力度无效");
                        return false;
                    }
                    int time = _previousTime + delta;
                    startMs[_decodeIndex] = time; keyUpMs[_decodeIndex] = time + held; releaseMs[_decodeIndex] = time + held + tail;
                    pitch[_decodeIndex] = key; velocity[_decodeIndex] = strength;
                    _previousTime = time;
                    if (++_decodeIndex == noteCount) NextDecodeStage(1);
                }
                else if (_decodeStage == 1)
                {
                    int midiChannel = ReadPackedByte();
                    int track = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (midiChannel > 15)
                    {
                        FailDecode("曲目MIDI通道无效");
                        return false;
                    }
                    channel[_decodeIndex] = midiChannel; trackIndex[_decodeIndex] = track;
                    if (++_decodeIndex == noteCount) NextDecodeStage(2);
                }
                else if (_decodeStage == 2)
                {
                    uint encoded = ReadPackedUInt();
                    if (_decodeStage < 0) return false;
                    int delta = (int)(encoded >> 1);
                    if ((encoded & 1u) != 0) delta = -delta - 1;
                    if ((delta > 0 && _previousToken > int.MaxValue - delta) || delta < -_previousToken)
                    {
                        FailDecode("曲目事件索引溢出");
                        return false;
                    }
                    int token = _previousToken + delta;
                    int id = token >> 2;
                    int kind = token & 3;
                    if (id < 0 || id >= noteCount || kind > 2 || _eventPhases[id] != kind)
                    {
                        FailDecode("曲目事件索引或因果次序无效");
                        return false;
                    }
                    int time = kind == 0 ? startMs[id] : kind == 1 ? keyUpMs[id] : releaseMs[id];
                    if (_decodeIndex > 0 && time < eventMs[_decodeIndex - 1])
                    {
                        FailDecode("曲目事件时间次序无效");
                        return false;
                    }
                    eventMs[_decodeIndex] = time; eventKind[_decodeIndex] = kind; eventNoteId[_decodeIndex] = id;
                    _eventPhases[id]++;
                    _previousToken = token;
                    if (++_decodeIndex == eventMs.Length) NextDecodeStage(3);
                }
                else if (_decodeStage == 3)
                {
                    int index = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    int time = _decodeIndex * 1000;
                    if (index > eventMs.Length || (index > 0 && eventMs[index - 1] >= time) || (index < eventMs.Length && eventMs[index] < time))
                    {
                        FailDecode("曲目检查点事件索引无效");
                        return false;
                    }
                    checkpointEventIndex[_decodeIndex] = index;
                    if (++_decodeIndex == _checkpointCount) NextDecodeStage(4);
                }
                else if (_decodeStage == 4)
                {
                    int offset = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if ((_decodeIndex == 0 && offset != 0) || (_decodeIndex > 0 && offset < checkpointActiveOffsets[_decodeIndex - 1]) || offset > _packedEnd - _packedCursor)
                    {
                        FailDecode("曲目检查点活动偏移无效");
                        return false;
                    }
                    checkpointActiveOffsets[_decodeIndex] = offset;
                    if (++_decodeIndex == checkpointActiveOffsets.Length)
                    {
                        checkpointActiveNoteIds = new int[offset];
                        NextDecodeStage(offset == 0 ? 6 : 5);
                    }
                }
                else if (_decodeStage == 5)
                {
                    int id = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (id >= noteCount)
                    {
                        FailDecode("曲目检查点音符索引无效");
                        return false;
                    }
                    checkpointActiveNoteIds[_decodeIndex] = id;
                    if (++_decodeIndex == checkpointActiveNoteIds.Length) NextDecodeStage(6);
                }
                else if (_decodeStage == 6)
                {
                    int count = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (count > (_packedEnd - _packedCursor) / 2 || count > int.MaxValue - pedalOffsets[_pedalSegment])
                    {
                        FailDecode("曲目踏板数量超出数据长度");
                        return false;
                    }
                    pedalOffsets[_pedalSegment + 1] = pedalOffsets[_pedalSegment] + count;
                    if (_pedalSegment == 0) { _pedalTimes0 = new int[count]; _pedalValues0 = new int[count]; }
                    else if (_pedalSegment == 1) { _pedalTimes1 = new int[count]; _pedalValues1 = new int[count]; }
                    else { _pedalTimes2 = new int[count]; _pedalValues2 = new int[count]; }
                    _previousTime = 0;
                    NextDecodeStage(count == 0 ? 8 : 7);
                }
                else if (_decodeStage == 7)
                {
                    int delta = ReadPackedInt();
                    int value = ReadPackedByte();
                    if (_decodeStage < 0) return false;
                    if (delta > durationMs - _previousTime || (_decodeIndex > 0 && delta == 0) || value > 127)
                    {
                        FailDecode("曲目踏板时间或数值无效");
                        return false;
                    }
                    int time = _previousTime + delta;
                    if (_pedalSegment == 0) { _pedalTimes0[_decodeIndex] = time; _pedalValues0[_decodeIndex] = value; }
                    else if (_pedalSegment == 1) { _pedalTimes1[_decodeIndex] = time; _pedalValues1[_decodeIndex] = value; }
                    else { _pedalTimes2[_decodeIndex] = time; _pedalValues2[_decodeIndex] = value; }
                    _previousTime = time;
                    if (++_decodeIndex == pedalOffsets[_pedalSegment + 1] - pedalOffsets[_pedalSegment]) NextDecodeStage(8);
                }
                else if (_decodeStage == 8)
                {
                    if (++_pedalSegment < 3) NextDecodeStage(6);
                    else
                    {
                        int count = pedalOffsets[3];
                        pedalMs = new int[count]; pedalController = new int[count]; pedalValue = new int[count];
                        NextDecodeStage(count == 0 ? 10 : 9);
                    }
                }
                else if (_decodeStage == 9)
                {
                    if (_decodeIndex < pedalOffsets[1])
                    {
                        pedalMs[_decodeIndex] = _pedalTimes0[_decodeIndex]; pedalValue[_decodeIndex] = _pedalValues0[_decodeIndex]; pedalController[_decodeIndex] = 67;
                    }
                    else if (_decodeIndex < pedalOffsets[2])
                    {
                        int i = _decodeIndex - pedalOffsets[1];
                        pedalMs[_decodeIndex] = _pedalTimes1[i]; pedalValue[_decodeIndex] = _pedalValues1[i]; pedalController[_decodeIndex] = 66;
                    }
                    else
                    {
                        int i = _decodeIndex - pedalOffsets[2];
                        pedalMs[_decodeIndex] = _pedalTimes2[i]; pedalValue[_decodeIndex] = _pedalValues2[i]; pedalController[_decodeIndex] = 64;
                    }
                    if (++_decodeIndex == pedalMs.Length) NextDecodeStage(10);
                }
                else if (_decodeStage == 10)
                {
                    _extensionCount = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (_extensionCount > (_packedEnd - _packedCursor) / 2)
                    {
                        FailDecode("曲目扩展段数量无效");
                        return false;
                    }
                    NextDecodeStage(_extensionCount == 0 ? 13 : 11);
                }
                else if (_decodeStage == 11)
                {
                    uint tag = ReadPackedUInt();
                    int length = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if ((tag & 1u) != 0 || length > _packedEnd - _packedCursor)
                    {
                        FailDecode("曲目必需扩展不受支持或扩展长度无效");
                        return false;
                    }
                    _extensionBytes = length;
                    _extensionCount--;
                    if(tag==2)
                    {
                        if(_hasExpressionExtension || length==0) { FailDecode("曲目表达扩展无效"); return false; }
                        _hasExpressionExtension=true; _expressionEnd=_packedCursor+length;
                        NextDecodeStage(14);
                    }
                    else if (tag == 4)
                    {
                        if (timbreDataVersion != 0 || length == 0) { FailDecode("曲目音色扩展无效或重复"); return false; }
                        timbreDataVersion = 1; _timbreEnd = _packedCursor + length;
                        NextDecodeStage(17);
                    }
                    else NextDecodeStage(length == 0 ? (_extensionCount == 0 ? 13 : 11) : 12);
                }
                else if (_decodeStage == 12)
                {
                    ReadPackedByte();
                    if (_decodeStage < 0) return false;
                    if (--_extensionBytes == 0) NextDecodeStage(_extensionCount == 0 ? 13 : 11);
                }
                else if (_decodeStage == 14)
                {
                    int count=ReadPackedInt();
                    if(_decodeStage<0) return false;
                    if(count>_extensionBytes/2) { FailDecode("曲目表达扩展无效"); return false; }
                    expressionMs=new int[count]; expressionValue=new int[count]; expressionOffsets=new int[17];
                    _expressionChannel=0; _expressionWrite=0;
                    NextDecodeStage(15);
                }
                else if (_decodeStage == 15)
                {
                    if(_expressionChannel==16)
                    {
                        if(_expressionWrite!=expressionMs.Length || _packedCursor!=_expressionEnd) { FailDecode("曲目表达扩展无效"); return false; }
                        NextDecodeStage(_extensionCount==0?13:11);
                    }
                    else
                    {
                        _expressionRemaining=ReadPackedInt(); _previousTime=0;
                        if(_decodeStage<0) return false;
                        if(_expressionRemaining>expressionMs.Length-_expressionWrite) { FailDecode("曲目表达扩展无效"); return false; }
                        if(_expressionRemaining==0) expressionOffsets[++_expressionChannel]=_expressionWrite;
                        else NextDecodeStage(16);
                    }
                }
                else if (_decodeStage == 16)
                {
                    int delta=ReadPackedInt(); int value=ReadPackedByte();
                    if(_decodeStage<0) return false;
                    if(delta>durationMs-_previousTime || value>127 || _packedCursor>_expressionEnd) { FailDecode("曲目表达扩展无效"); return false; }
                    _previousTime+=delta; expressionMs[_expressionWrite]=_previousTime; expressionValue[_expressionWrite++]=value;
                    if(--_expressionRemaining==0) { expressionOffsets[++_expressionChannel]=_expressionWrite; NextDecodeStage(15); }
                }
                else if (_decodeStage == 17)
                {
                    int count = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (_packedCursor > _timbreEnd || count > (_timbreEnd - _packedCursor) / 5)
                    { FailDecode("曲目音色数量超出扩展长度"); return false; }
                    timbreMs = new int[count]; timbreChannel = new int[count]; timbreKind = new int[count];
                    timbreValue = new int[count]; timbreTrackIndex = new int[count]; timbreEventIndex = new int[count];
                    _previousTime = 0; _timbrePreviousEvent = 0;
                    NextDecodeStage(18);
                }
                else if (_decodeStage == 18)
                {
                    if (_decodeIndex == timbreMs.Length)
                    {
                        if (_packedCursor != _timbreEnd) { FailDecode("曲目音色段长度无效"); return false; }
                        NextDecodeStage(_extensionCount == 0 ? 13 : 11);
                        continue;
                    }
                    int delta = ReadPackedInt(), descriptor = ReadPackedByte(), value = ReadPackedByte();
                    int track = ReadPackedInt(), eventDelta = ReadPackedInt();
                    if (_decodeStage < 0) return false;
                    if (delta > durationMs - _previousTime || descriptor > 62 || (descriptor & 3) > 2 || value > 127 ||
                        eventDelta > eventMs.Length - _timbrePreviousEvent || _packedCursor > _timbreEnd)
                    { FailDecode("曲目音色事件无效"); return false; }
                    _previousTime += delta; _timbrePreviousEvent += eventDelta;
                    timbreMs[_decodeIndex] = _previousTime; timbreChannel[_decodeIndex] = descriptor >> 2;
                    timbreKind[_decodeIndex] = descriptor & 3; timbreValue[_decodeIndex] = value;
                    timbreTrackIndex[_decodeIndex] = track; timbreEventIndex[_decodeIndex++] = _timbrePreviousEvent;
                }
                else if (_decodeStage == 13)
                {
                    if (_packedCursor != _packedEnd)
                    {
                        FailDecode("曲目压缩数据存在未识别尾部");
                        return false;
                    }
                    uint expected = (uint)_packedBytes[_packedEnd] | ((uint)_packedBytes[_packedEnd + 1] << 8) |
                        ((uint)_packedBytes[_packedEnd + 2] << 16) | ((uint)_packedBytes[_packedEnd + 3] << 24);
                    if (_packedChecksum != expected)
                    {
                        FailDecode("曲目压缩数据校验失败");
                        return false;
                    }
                    ClearDecodeBuffers();
                    isDecoded = true;
                    _decodeStage = -1;
                    return true;
                }
            }
            return false;
        }

        public void ReleaseDecodedData()
        {
            isDecoded = false;
            decodeError = "";
            _decodeStage = -1;
            _decodeIndex = 0; _packedCursor = 0; _packedEnd = 0;
            _previousTime = 0; _previousToken = 0; _checkpointCount = 0; _pedalSegment = 0;
            _extensionCount = 0; _extensionBytes = 0;
            timbreDataVersion = 0; _timbreEnd = 0; _timbrePreviousEvent = 0;
            timbreMs = new int[0]; timbreChannel = new int[0]; timbreKind = new int[0];
            timbreValue = new int[0]; timbreTrackIndex = new int[0]; timbreEventIndex = new int[0];
            _hasExpressionExtension=false; _expressionEnd=0; _expressionChannel=0; _expressionWrite=0; _expressionRemaining=0;
            expressionMs=new int[0]; expressionValue=new int[0]; expressionOffsets=new int[17];
            ClearDecodeBuffers();
            startMs = new int[0]; keyUpMs = new int[0]; releaseMs = new int[0]; pitch = new int[0]; velocity = new int[0]; channel = new int[0]; trackIndex = new int[0];
            eventMs = new int[0]; eventKind = new int[0]; eventNoteId = new int[0];
            checkpointEventIndex = new int[0]; checkpointActiveOffsets = new int[0]; checkpointActiveNoteIds = new int[0];
            pedalMs = new int[0]; pedalController = new int[0]; pedalValue = new int[0]; pedalOffsets = new int[0];
        }

        private void ClearDecodeBuffers()
        {
            _packedBytes = null; _eventPhases = null;
            _pedalTimes0 = null; _pedalTimes1 = null; _pedalTimes2 = null;
            _pedalValues0 = null; _pedalValues1 = null; _pedalValues2 = null;
        }

        private void NextDecodeStage(int stage)
        {
            _decodeStage = stage;
            _decodeIndex = 0;
        }

        private void FailDecode(string error)
        {
            ReleaseDecodedData();
            decodeError = error;
        }

        private int ReadPackedByte()
        {
            if (_decodeStage < 0) return 0;
            if (_packedCursor >= _packedEnd)
            {
                FailDecode("曲目压缩数据意外结束");
                return 0;
            }
            byte value = _packedBytes[_packedCursor++];
            _packedChecksum = (_packedChecksum ^ value) * 16777619u;
            return value;
        }

        private uint ReadPackedUInt()
        {
            uint value = 0;
            for (int i = 0; i < 5; i++)
            {
                int next = ReadPackedByte();
                if (_decodeStage < 0) return 0;
                if (i == 4 && (next & 240) != 0)
                {
                    FailDecode("曲目变长整数溢出");
                    return 0;
                }
                value |= (uint)(next & 127) << (i * 7);
                if ((next & 128) == 0)
                {
                    if (i > 0 && (next & 127) == 0) FailDecode("曲目变长整数编码无效");
                    return value;
                }
            }
            FailDecode("曲目变长整数超长");
            return 0;
        }

        private int ReadPackedInt()
        {
            uint value = ReadPackedUInt();
            if (_decodeStage < 0) return 0;
            if (value > int.MaxValue)
            {
                FailDecode("曲目整数超出支持范围");
                return 0;
            }
            return (int)value;
        }

        /// <summary>Raw 0..127 animation value; physical left/right order is CC67, CC66, CC64.</summary>
        public int QueryPedalValue(int pedalIndex, int positionMs)
        {
            if (pedalIndex < 0 || pedalIndex > 2 || positionMs < 0 || pedalOffsets == null || pedalOffsets.Length != 4 ||
                pedalMs == null || pedalValue == null || pedalMs.Length != pedalValue.Length) return 0;
            int begin = pedalOffsets[pedalIndex];
            int end = pedalOffsets[pedalIndex + 1];
            if (begin < 0 || end < begin || end > pedalMs.Length) return 0;
            int lo = begin;
            int hi = end;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (pedalMs[mid] <= positionMs) lo = mid + 1;
                else hi = mid;
            }
            return lo > begin ? Mathf.Clamp(pedalValue[lo - 1], 0, 127) : 0;
        }

        public int LowerBoundEvent(int milliseconds)
        {
            int lo = 0;
            int hi = eventMs.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (eventMs[mid] < milliseconds) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public int UpperBoundEvent(int milliseconds)
        {
            int lo = 0;
            int hi = eventMs.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (eventMs[mid] <= milliseconds) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public bool IsValid()
        {
            if (!isDecoded || schemaVersion != 2 || string.IsNullOrEmpty(songId) || string.IsNullOrEmpty(sourceHash)
                || sourceHash.Length != 64 || startMs == null) return false;
            int count = startMs.Length;
            if (keyUpMs == null || releaseMs == null || pitch == null || velocity == null || channel == null || trackIndex == null) return false;
            if (keyUpMs.Length != count || releaseMs.Length != count || pitch.Length != count || velocity.Length != count || channel.Length != count || trackIndex.Length != count) return false;
            if (eventMs == null || eventKind == null || eventNoteId == null) return false;
            if (pedalMs == null || pedalController == null || pedalValue == null || pedalOffsets == null || pedalOffsets.Length != 4 ||
                pedalController.Length != pedalMs.Length || pedalValue.Length != pedalMs.Length || pedalOffsets[0] != 0 || pedalOffsets[3] != pedalMs.Length) return false;
            for (int pedal = 0; pedal < 3; pedal++)
            {
                int begin = pedalOffsets[pedal], end = pedalOffsets[pedal + 1];
                if (begin < 0 || end < begin || end > pedalMs.Length) return false;
                int controller = pedal == 0 ? 67 : pedal == 1 ? 66 : 64;
                for (int i = begin; i < end; i++)
                    if (pedalController[i] != controller || pedalMs[i] < 0 || pedalMs[i] > durationMs ||
                        (i > begin && pedalMs[i] <= pedalMs[i - 1]) || pedalValue[i] < 0 || pedalValue[i] > 127) return false;
            }
            return eventMs.Length == count * 3 && eventKind.Length == eventMs.Length && eventNoteId.Length == eventMs.Length && durationMs >= 0;
        }
    }
}
