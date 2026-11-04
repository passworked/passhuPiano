using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Audio
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StringsVoicePool : UdonSharpBehaviour
    {
        public PianoVoicePool output;
        public MidiTransportClock clock;
        public StringsBankData[] banks = new StringsBankData[0];
        public Transform[] soundOrigins = new Transform[0];
        // First/second violin, viola, cello, bass. Other channels explicitly default to first violin.
        public int[] channelSections = new int[0];
        // Optional per-track overrides; -1 means use the channel mapping.
        public int[] trackSections = new int[0];
        [HideInInspector] public bool activeStrings;
        [HideInInspector] public int mode;
        public int articulation;
        [HideInInspector] public int activeVoiceCount;
        [HideInInspector] public int stolenVoiceCount;
        [HideInInspector] public int generation;
        [HideInInspector] public int[] noteIds = new int[32];
        [HideInInspector] public int[] pitches = new int[32];
        [HideInInspector] public int[] velocities = new int[32];
        [HideInInspector] public int[] sections = new int[32];
        [HideInInspector] public bool[] sounding = new bool[32];
        [HideInInspector] public float[] levels = new float[32];
        [HideInInspector] public double[] starts = new double[32];
        private bool[] _live = new bool[32];
        private int[] _bank = new int[32];
        private int[] _region = new int[32];
        private int[] _channel = new int[32];
        private double[] _release = new double[32];
        private double[] _end = new double[32];
        private double[] _attackEnd = new double[32];
        private float[] _baseGain = new float[32];
        private float[] _expression = new float[16];
        private int[] _seenGeneration = new int[0];
        private MidiSongData _song;
        private MidiSequencer _sequencer;
        private Vector3[] _restPositions;

        public void Configure(int nextMode)
        {
            if (_restPositions == null)
            {
                _restPositions = new Vector3[output.voiceSources.Length];
                for (int i = 0; i < _restPositions.Length; i++) _restPositions[i] = output.voiceSources[i].transform.localPosition;
            }
            StopAll();
            activeStrings = nextMode >= 0;
            mode = Mathf.Max(0, nextMode);
            for (int i = 0; i < 16; i++) _expression[i] = 1f;
        }

        public int SectionFor(int channel, int track)
        {
            if (mode != 4) return 0;
            if (track >= 0 && track < trackSections.Length && trackSections[track] >= 0)
                return Mathf.Clamp(trackSections[track], 0, 4);
            return channel >= 0 && channel < channelSections.Length ? Mathf.Clamp(channelSections[channel], 0, 4) : 0;
        }

        private int BankFor(int section)
        {
            if (mode == 0) return 0; // Solo violin.
            if (mode < 4) return mode + 1;
            return section < 2 ? 1 : section;
        }

        public void BindSong(MidiSongData song, MidiSequencer sequencer)
        {
            StopAll(); _song = song; _sequencer = sequencer;
            _seenGeneration = new int[song == null ? 0 : song.startMs.Length];
            // Facade generations are positive, so zero-initialized entries are unseen.
        }

        public void RebuildAt(int ms, int nextGeneration)
        {
            StopAll(); generation = nextGeneration;
            if (clock.GetState() == 1) RecoverAt(ms, nextGeneration);
        }

        public void RecoverAt(int ms, int nextGeneration)
        {
            if (!activeStrings || _song == null || generation != nextGeneration || clock.GetState() != 1) return;
            for (int i = 0; i < _sequencer.activeNoteCount; i++) StartNote(_sequencer.activeNoteIds[i], ms, nextGeneration, false);
            int lo = 0, hi = _song.eventMs.Length;
            while (lo < hi) { int mid = lo + (hi-lo)/2; if (_song.eventMs[mid] < ms-700) lo=mid+1; else hi=mid; }
            for (int i = lo; i < _song.eventMs.Length && _song.eventMs[i] <= ms; i++)
                if (_song.eventKind[i] == 2) StartNote(_song.eventNoteId[i], ms, nextGeneration, false);
        }

        public void PrepareWindow(int from, int to, int nextGeneration)
        {
            if (_song == null || clock.GetState() != 1 || nextGeneration != generation) return;
            int lo=0, hi=_song.startMs.Length;
            while (lo<hi) { int mid=lo+(hi-lo)/2; if (_song.startMs[mid] <= from) lo=mid+1; else hi=mid; }
            for (int n=lo; n<_song.startMs.Length && _song.startMs[n]<=to; n++)
            {
                // Future reservations never steal an already sounding note.
                if (FreeSlot() < 0) break;
                NoteDown(n, from, nextGeneration);
            }
        }

        public void NoteDown(int note, int ms, int nextGeneration)
        {
            StartNote(note, ms, nextGeneration, true);
        }

        private void StartNote(int note, int ms, int nextGeneration, bool attack)
        {
            if (!activeStrings || _song == null || note < 0 || note >= _seenGeneration.Length
                || nextGeneration != generation || _seenGeneration[note] == generation) return;
            if (_song.channel[note] == 9) return;
            if (output.practice != null && output.practice.IsTargetTrack(_song.trackIndex[note])) return;
            int section=SectionFor(_song.channel[note], _song.trackIndex[note]);
            int bankIndex=BankFor(section);
            int r=banks[bankIndex].SelectRegion(_song.pitch[note], articulation);
            if (r<0 || (!attack && ms >= _song.releaseMs[note]+banks[bankIndex].releaseMs[r])) return;
            int delayMs=attack ? Mathf.Max(0,ms-_song.startMs[note]):0;
            double age=attack ? 0:System.Math.Max(0.0, (ms-_song.startMs[note])*.001);
            float ratio=Mathf.Pow(2f, (_song.pitch[note]-banks[bankIndex].roots[r])/12f);
            if (banks[bankIndex].loops[r] == null && age*ratio >= banks[bankIndex].attacks[r].length) return;
            int slot=Allocate(); if (slot<0) return;
            _seenGeneration[note]=generation;
            double dsp=clock.GetFrameDspSeconds();
            double start=dsp+System.Math.Max(.01, (_song.startMs[note]-ms)*.001);
            _live[slot]=false; noteIds[slot]=note;
            _release[slot]=dsp+(_song.releaseMs[note]+delayMs-ms)*.001;
            StartSlot(slot,bankIndex,r,section,_song.channel[note],_song.pitch[note],_song.velocity[note],start,age);
        }

        public void LiveNoteDown(int identity, int pitch, int velocity)
        {
            if (!activeStrings) return;
            int section=SectionFor(identity/128,-1), bankIndex=BankFor(section);
            int r=banks[bankIndex].SelectRegion(pitch,articulation);
            int slot=Allocate(); if (slot<0 || r<0) return;
            _live[slot]=true; noteIds[slot]=identity; _release[slot]=-1;
            StartSlot(slot,bankIndex,r,section,identity/128,pitch,velocity,AudioSettings.dspTime+.01,0);
        }

        public void LiveNoteRelease(int identity)
        {
            for(int i=0;i<32;i++) if(sounding[i] && _live[i] && noteIds[i]==identity && _release[i]<0) _release[i]=AudioSettings.dspTime;
        }

        public void LiveAllSoundOff(int channel)
        {
            for(int i=0;i<32;i++) if(sounding[i] && _live[i] && (channel<0 || _channel[i]==channel)) StopSlot(i);
        }

        public void SetExpression(int channel, int value)
        {
            if(channel>=0 && channel<16) _expression[channel]=Mathf.Clamp(value,0,127)/127f;
        }

        public void StopAll()
        {
            for(int i=0;i<32;i++) StopSlot(i);
            activeVoiceCount=0;
        }

        public void ReleasePercussionSources()
        {
            int first=Mathf.Max(0,(output.voiceSources.Length-16)/2);
            for(int i=first;i<32;i++) if(sounding[i]) StopSlot(i);
        }
        private int Limit() { return Mathf.Min(32, Mathf.Max(0,(output.voiceLimit-output.ReservedPercussionSources())/2), (output.voiceSources.Length-output.ReservedPercussionSources())/2); }
        public int GetVoiceLimit() { return Limit(); }
        private int FreeSlot() { for(int i=0;i<Limit();i++) if(!sounding[i]) return i; return -1; }
        private int Allocate()
        {
            if(Limit()==0) return -1;
            int free=FreeSlot(); if(free>=0) return free;
            int victim=0;
            for(int i=1;i<Limit();i++) if(levels[i]<levels[victim] || (Mathf.Abs(levels[i]-levels[victim])<.001f && starts[i]<starts[victim])) victim=i;
            StopSlot(victim); stolenVoiceCount++; return victim;
        }

        private void StartSlot(int slot,int b,int r,int section,int channel,int pitch,int velocity,double start,double age)
        {
            StringsBankData data=banks[b];
            AudioSource attack=output.voiceSources[slot*2], body=output.voiceSources[slot*2+1];
            float ratio=Mathf.Pow(2f,(pitch-data.roots[r])/12f);
            _bank[slot]=b; _region[slot]=r; _channel[slot]=channel;
            sections[slot]=section; pitches[slot]=pitch; velocities[slot]=velocity; starts[slot]=start-age;
            _baseGain[slot]=Mathf.Pow(velocity/127f,1.2f)*data.gains[r];
            _attackEnd[slot]=start+data.attacks[r].length/ratio-age;
            _end[slot]=data.loops[r] != null ? double.MaxValue : _attackEnd[slot];
            sounding[slot]=true;
            attack.Stop(); body.Stop(); attack.volume=body.volume=0f;
            if (section >= 0 && section < soundOrigins.Length && soundOrigins[section] != null)
            { Vector3 point = soundOrigins[section].TransformPoint(new Vector3(0,-.045f,.08f)); attack.transform.position=point; body.transform.position=point; }
            attack.clip=data.attacks[r]; attack.loop=false; attack.pitch=ratio;
            body.clip=data.loops[r]; body.loop=true; body.pitch=ratio;
            attack.spatialBlend=body.spatialBlend=output.worldSpaceAudio?1f:0f;
            attack.dopplerLevel=body.dopplerLevel=0;
            if(age*ratio<data.attacks[r].length)
            {
                attack.timeSamples=Mathf.Clamp((int)(age*ratio*attack.clip.frequency),0,attack.clip.samples-1);
                attack.PlayScheduled(start);
            }
            if(body.clip!=null)
            {
                double offset=System.Math.Max(0,age*ratio-data.attacks[r].length);
                body.timeSamples=(int)((offset % body.clip.length)*body.clip.frequency);
                body.PlayScheduled(System.Math.Max(start,_attackEnd[slot]));
            }
            ApplySlot(slot,start);
            RefreshOutput();
        }

        public void UpdateVoices(int ms,int nextGeneration)
        {
            double dsp=clock.GetFrameDspSeconds(); activeVoiceCount=0;
            for(int i=0;i<32;i++)
            {
                if(!sounding[i]) continue;
                if((!_live[i] && (clock.GetState()!=1 || nextGeneration!=generation)) || dsp>=_end[i]) { StopSlot(i); continue; }
                StringsBankData data=banks[_bank[i]];
                if(_release[i]>=0 && dsp>=_release[i]+data.releaseMs[_region[i]]*.001) { StopSlot(i); continue; }
                activeVoiceCount++;
            }
            RefreshOutput();
        }

        public void RefreshOutput()
        {
            double dsp = clock.GetFrameDspSeconds();
            float power = output.peakProtectionEnabled && output.drums != null ? output.drums.EnergySum(dsp) : 0f;
            float peakSum = output.drums == null ? 0f : output.drums.PeakSum();
            float maxLevel = output.drums == null ? 0f : output.drums.MaxLevel();
            for (int i = 0; i < 32; i++) if (sounding[i])
            {
                ApplySlot(i, dsp);
                peakSum += levels[i] * banks[_bank[i]].peaks[_region[i]];
                maxLevel = Mathf.Max(maxLevel, levels[i]);
                if (output.peakProtectionEnabled)
                {
                    float age = (float)(dsp - starts[i]) * output.voiceSources[i * 2].pitch;
                    power += levels[i] * levels[i] * banks[_bank[i]].EnergyAt(_region[i], age);
                }
            }
            float gain = output.LimitOutputGain(output.CalculateCompressionGain(power, dsp), peakSum, maxLevel, dsp);
            for (int i = 0; i < 32; i++) if (sounding[i])
            {
                float volume = output.muted ? 0f : Mathf.Clamp01(output.localVolume * levels[i] * gain);
                output.voiceSources[i * 2].volume = output.voiceSources[i * 2 + 1].volume = volume;
            }
            if (output.drums != null) output.drums.ApplyOutputGains(gain);
        }

        private void ApplySlot(int slot,double dsp)
        {
            float release=_release[slot]<0?0f:Mathf.Clamp01((float)((dsp-_release[slot])*1000)/banks[_bank[slot]].releaseMs[_region[slot]]);
            float attack=Mathf.Clamp01((float)((dsp-starts[slot])/.008));
            float expression=_live[slot]?_expression[_channel[slot]]:_song.ExpressionAt(_channel[slot],(int)(clock.GetPositionSeconds()*1000))/127f;
            levels[slot]=_baseGain[slot]*(1-release)*(1-release)*expression*attack;
            AudioSource a=output.voiceSources[slot*2], b=output.voiceSources[slot*2+1];
            int priority=release>0?200:96+(int)Mathf.Clamp((float)(dsp-starts[slot]),0,64);
            a.priority=b.priority=priority;
        }

        private void StopSlot(int slot)
        {
            if(sounding[slot] && output!=null && output.voiceSources!=null && slot*2+1<output.voiceSources.Length)
            {
                output.voiceSources[slot*2].Stop(); output.voiceSources[slot*2+1].Stop();
                if (_restPositions != null)
                { output.voiceSources[slot*2].transform.localPosition=_restPositions[slot*2]; output.voiceSources[slot*2+1].transform.localPosition=_restPositions[slot*2+1]; }
            }
            sounding[slot]=false; levels[slot]=0;
        }
    }
}
