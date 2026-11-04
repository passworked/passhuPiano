using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Audio
{
    // Uses the last sixteen sources of the existing shared 64-source output, never creates sources.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PercussionVoicePool : UdonSharpBehaviour
    {
        public PianoVoicePool output;
        public PercussionBankData bank;
        public MidiTransportClock clock;
        public Transform[] hitPoints = new Transform[0];
        [HideInInspector] public bool hasSongDrums;
        [HideInInspector] public int activeVoiceCount, stolenVoiceCount, unmappedNoteCount, playbackEndMs;
        private const int Capacity=16;
        private MidiSongData _song;
        private int[] _ids=new int[0], _regions=new int[0], _ends=new int[0], _chokes=new int[0], _seen=new int[0];
        private int _generation=-1;
        private bool[] _active=new bool[Capacity], _live=new bool[Capacity];
        private int[] _slotIndex=new int[Capacity], _slotPitch=new int[Capacity], _slotRegion=new int[Capacity], _slotGeneration=new int[Capacity];
        private double[] _start=new double[Capacity], _end=new double[Capacity], _choke=new double[Capacity];
        private float[] _baseGain=new float[Capacity], _level=new float[Capacity];
        private Vector3[] _rest=new Vector3[Capacity];
        private bool _initialized;

        private int Limit() { return Mathf.Min(Capacity,Mathf.Max(1,output.voiceLimit/4)); }
        private bool IsEnabled() { return output.ensemble == null || output.ensemble.router.IsDrumsEnabled(); }
        public int ReservedCount() { return IsEnabled() && (hasSongDrums || activeVoiceCount>0) ? Limit() : 0; }
        private int Offset() { return output.voiceSources.Length-Capacity; }
        private AudioSource Source(int slot) { return output.voiceSources[Offset()+slot]; }
        private void Initialize()
        {
            if(_initialized || output==null || output.voiceSources==null || output.voiceSources.Length<Capacity) return;
            for(int i=0;i<Capacity;i++) _rest[i]=Source(i).transform.localPosition;
            _initialized=true;
        }
        private int _bindStage = -1, _bindCursor, _bindCount, _bindWrite, _bindNextClose;
        public void BindSong(MidiSongData song)
        {
            BeginBindSong(song);
            while(!BindStep(512)) { }
        }
        public void BeginBindSong(MidiSongData song)
        {
            Initialize(); CancelSong(); _song=song; _generation=-1; playbackEndMs=0; unmappedNoteCount=0;
            hasSongDrums=false;
            _bindStage=0; _bindCursor=0; _bindCount=0; _bindWrite=0; _bindNextClose=int.MaxValue;
        }
        public bool BindStep(int budget)
        {
            while(budget-- > 0 && _bindStage>=0)
            {
                if(_bindStage==0)
                {
                    if(_song!=null && bank!=null && _bindCursor<_song.channel.Length)
                    { if(IsDrum(_bindCursor++))_bindCount++; continue; }
                    int count=_bindCount;
                    _ids=new int[count]; _regions=new int[count]; _ends=new int[count]; _chokes=new int[count]; _seen=new int[count];
                    hasSongDrums=count>0; _bindCursor=0; _bindStage=1;
                }
                else if(_bindStage==1)
                {
                    if(_song!=null && bank!=null && _bindCursor<_song.channel.Length)
                    {
                        int n=_bindCursor++;
                        if(!IsDrum(n))continue;
                        int r=bank.SelectRegion(DrumPitch(n),_song.velocity[n],n), cursor=_bindWrite++;
                        _ids[cursor]=n; _regions[cursor]=r; _seen[cursor]=-1; _chokes[cursor]=int.MaxValue;
                        _ends[cursor]=r<0?_song.startMs[n]:_song.startMs[n]+Mathf.CeilToInt(bank.clips[r].length*1000f);
                        if(r<0)unmappedNoteCount++;
                        continue;
                    }
                    _bindStage=2; _bindCursor=_bindCount-1;
                }
                else if(_bindCursor>=0)
                {
                    int i=_bindCursor--, pitch=DrumPitch(_ids[i]);
                    if(pitch==46 && _bindNextClose<int.MaxValue)
                    { _chokes[i]=_bindNextClose; _ends[i]=Mathf.Min(_ends[i],_bindNextClose+25); }
                    if(pitch==42 || pitch==44)_bindNextClose=_song.startMs[_ids[i]];
                    playbackEndMs=Mathf.Max(playbackEndMs,_ends[i]);
                }
                else
                {
                    _bindStage=-1;
                    if(hasSongDrums && IsEnabled())output.ClaimPercussionSources();
                }
            }
            return _bindStage<0;
        }
        private bool IsDrum(int note)
        { return output.ensemble == null ? _song.channel[note] == 9 : output.ensemble.router.IsFileDrum(note); }
        private int DrumPitch(int note)
        { return output.ensemble == null ? _song.pitch[note] : output.ensemble.router.FileDrumPitch(note); }
        private int Upper(int ms)
        {
            int lo=0,hi=_ids.Length;
            while(lo<hi) { int m=lo+(hi-lo)/2; if(_song.startMs[_ids[m]]<=ms) lo=m+1;else hi=m; }
            return lo;
        }
        public void RebuildAt(int ms,int generation)
        {
            CancelSong(); _generation=generation;
            if(clock.GetState()==1) RecoverAt(ms,generation);
        }
        public void RecoverAt(int ms,int generation)
        {
            if(_song==null || bank==null || generation!=_generation || clock.GetState()!=1) return;
            int first=Upper(ms-bank.maxTailMs-26),last=Upper(ms);
            for(int i=first;i<last;i++) Request(i,ms,false);
        }
        public void PrepareWindow(int from,int to,int generation)
        {
            if(!hasSongDrums || generation!=_generation || clock.GetState()!=1) return;
            int first=Upper(from),last=Upper(output.useDspScheduling?to:from);
            for(int i=first;i<last;i++) Request(i,from,true);
        }
        public void CatchUpRecentNotes(int from,int ms,int generation)
        {
            if(_song==null || generation!=_generation || clock.GetState()!=1)return;
            int first=Upper(from),last=Upper(ms);
            for(int i=first;i<last;i++)Request(i,ms,true);
            RecoverAt(ms,generation);
        }
        public void NoteDown(int note,int ms,int generation)
        {
            if(_song==null || generation!=_generation || clock.GetState()!=1) return;
            int lo=0,hi=_ids.Length;
            while(lo<hi) { int m=lo+(hi-lo)/2;if(_ids[m]<note)lo=m+1;else hi=m; }
            if(lo<_ids.Length && _ids[lo]==note) Request(lo,ms,true);
        }
        private int Allocate(bool future)
        {
            for(int i=0;i<Limit();i++) if(!_active[i]) return i;
            if(future) return -1;
            double now=AudioSettings.dspTime;
            int victim=-1;
            for(int i=0;i<Limit();i++)
            {
                if(now-_start[i]<.08) continue;
                if(victim<0 || _level[i]<_level[victim] || (_level[i]==_level[victim] && _start[i]<_start[victim])) victim=i;
            }
            if(victim<0) victim=0;
            // Unheard look-ahead reservations can be retried at their actual start.
            if(!_live[victim] && _start[victim]>now) _seen[_slotIndex[victim]]=-1;
            StopSlot(victim); stolenVoiceCount++; return victim;
        }
        private void Request(int index,int ms,bool attack)
        {
            if(!IsEnabled() || _seen[index]==_generation || _regions[index]<0 || (!attack && _ends[index]<=ms)) return;
            int note=_ids[index];
            int slot=Allocate(_song.startMs[note]>ms); if(slot<0) return;
            int r=_regions[index];
            double actualAge=System.Math.Max(0,(ms-_song.startMs[note])*.001);
            double age=attack ? 0:actualAge;
            double delay=attack ? actualAge:0;
            double dsp=clock.GetFrameDspSeconds();
            double start=dsp+System.Math.Max(0,(_song.startMs[note]-ms)*.001);
            _live[slot]=false;_slotIndex[slot]=index;_slotGeneration[slot]=_generation;
            _seen[index]=_generation;
            StartSlot(slot,r,DrumPitch(note),_song.velocity[note],start,age,
                dsp+(_ends[index]-ms)*.001+delay,_chokes[index]==int.MaxValue?double.MaxValue:dsp+(_chokes[index]-ms)*.001+delay);
        }
        public void LiveNoteDown(int pitch,int velocity,int eventId)
        {
            if(bank==null || !IsEnabled()) return;
            int r=bank.SelectRegion(pitch,velocity,eventId);if(r<0) {unmappedNoteCount++;return;}
            Initialize();output.ClaimPercussionSources();
            double dsp=AudioSettings.dspTime;
            if(pitch==42 || pitch==44)
                for(int i=0;i<Capacity;i++) if(_active[i] && _live[i] && _slotPitch[i]==46)
                { _choke[i]=dsp;_end[i]=System.Math.Min(_end[i],dsp+.025);Source(i).SetScheduledEndTime(_end[i]); }
            int slot=Allocate(false);if(slot<0)return;
            _live[slot]=true;_slotGeneration[slot]=-1;
            StartSlot(slot,r,pitch,velocity,dsp,0,dsp+bank.clips[r].length,double.MaxValue);
        }
        private void StartSlot(int slot,int r,int pitch,int velocity,double start,double age,double end,double choke)
        {
            AudioSource source=Source(slot);source.Stop();source.clip=bank.clips[r];source.loop=false;source.pitch=1;
            source.timeSamples=Mathf.Clamp((int)(age*source.clip.frequency),0,source.clip.samples-1);
            int lane=bank.lanes[r];
            if(lane<hitPoints.Length && hitPoints[lane]!=null)source.transform.position=hitPoints[lane].position;
            source.spatialBlend=output.worldSpaceAudio?1:0;source.dopplerLevel=0;
            _slotPitch[slot]=pitch;_slotRegion[slot]=r;_baseGain[slot]=bank.Gain(r,velocity);
            _level[slot]=_baseGain[slot]*(choke==double.MaxValue?1:1-Mathf.Clamp01((float)((AudioSettings.dspTime-choke)/.025)));
            _start[slot]=start-age;_end[slot]=end;_choke[slot]=choke;
            if(!_active[slot]) activeVoiceCount++;
            _active[slot]=true;source.priority=40;
            source.volume=0; // Refresh the common headroom bound before this attack can be heard.
            source.PlayScheduled(start);source.SetScheduledEndTime(end);
            output.RefreshOutput();
        }
        public void RelocateSources()
        {
            if (!_initialized || bank == null) return;
            for (int i = 0; i < Capacity; i++) if (_active[i])
            {
                int lane = bank.lanes[_slotRegion[i]];
                if (lane >= 0 && lane < hitPoints.Length && hitPoints[lane] != null)
                    Source(i).transform.position = hitPoints[lane].position;
            }
        }
        public void ReclaimEndedVoices()
        {
            if(!_initialized)return;
            double dsp=AudioSettings.dspTime;
            int limit=Limit();
            for(int i=0;i<Capacity;i++)if(_active[i] && (i>=limit || dsp>=_end[i]))StopSlot(i);
        }
        public void UpdateVoices(int ms,int generation)
        {
            if(!_initialized)return;
            double dsp=AudioSettings.dspTime;
            for(int i=0;i<Capacity;i++) if(_active[i])
            {
                if(i>=Limit()){StopSlot(i);continue;}
                if(dsp>=_end[i]) {StopSlot(i);continue;}
                if(!_live[i] && (clock.GetState()!=1 || _slotGeneration[i]!=generation))
                { _choke[i]=System.Math.Min(_choke[i],dsp);_end[i]=System.Math.Min(_end[i],dsp+.005); }
                float fade=_choke[i]==double.MaxValue?1:1-Mathf.Clamp01((float)((dsp-_choke[i])/.025));
                _level[i]=_baseGain[i]*fade;
                Source(i).priority=dsp-_start[i]<.12?40:160+Mathf.Clamp((int)((dsp-_start[i])*8),0,60);
            }
            ApplyOutputGains(output.compressionGain);
        }
        public float EnergySum(double dsp)
        {
            float sum = 0f;
            for (int i = 0; i < Capacity; i++) if (_active[i] && dsp >= _start[i])
                sum += _level[i] * _level[i] * bank.EnergyAt(_slotRegion[i], (float)(dsp - _start[i]));
            return sum;
        }
        public float PeakSum()
        {
            float sum=0;for(int i=0;i<Capacity;i++) if(_active[i])sum+=_level[i]*bank.peaks[_slotRegion[i]];return sum;
        }
        public float MaxLevel()
        {
            float level=0;for(int i=0;i<Capacity;i++)if(_active[i])level=Mathf.Max(level,_level[i]);return level;
        }
        public void ApplyOutputGains(float normalization)
        {
            for(int i=0;i<Capacity;i++)if(_active[i])Source(i).volume=output.muted?0:Mathf.Clamp01(output.localVolume*_level[i]*normalization);
        }
        public void CancelSong()
        {
            for(int i=0;i<Capacity;i++) if(_active[i] && !_live[i]) StopSlot(i);
        }
        public void StopLive()
        {
            for(int i=0;i<Capacity;i++)if(_active[i] && _live[i])StopSlot(i);
        }
        public void StopAll()
        {
            for(int i=0;i<Capacity;i++)if(_active[i])
            {
                // A kit toggle keeps the song generation. Let canceled tails/look-ahead restart.
                if(!_live[i])_seen[_slotIndex[i]]=-1;
                StopSlot(i);
            }
        }
        private void StopSlot(int slot)
        {
            if(!_active[slot])return;
            Source(slot).Stop();Source(slot).volume=0;Source(slot).transform.localPosition=_rest[slot];
            _active[slot]=false;_level[slot]=0;activeVoiceCount=Mathf.Max(0,activeVoiceCount-1);
        }
    }
}
