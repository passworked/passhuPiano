using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiDrumKitView : UdonSharpBehaviour
    {
        public PercussionBankData bank;
        public Transform[] heads = new Transform[0], cymbals = new Transform[0], sticks = new Transform[0];
        public Transform kickBeater, kickPedal, hiHatPedal;
        public Transform[] hitPoints = new Transform[0], glowTransforms = new Transform[0];
        public Renderer[] glowRenderers = new Renderer[0];
        public MidiPercussionLaneView lanes;
        public bool presentationEnabled=true;
        [HideInInspector] public int processedHitCount;
        private MidiSongData _song;
        private string _performancePlayerName = "";
        private int _generation=-1,_lastMs,_noteCursor;
        private bool _ready,_hasPosition,_liveMode,_openHat;
        private int[] _ringCursor=new int[9];
        private int[] _times=new int[72];
        private float[] _strength=new float[72];
        private Vector3[] _headRest,_cymbalRest;
        private Quaternion[] _cymbalRotation,_stickRotation;
        private Quaternion _beaterRest,_kickPedalRest,_hatPedalRest;
        private MaterialPropertyBlock _properties;
        private Vector3[] _headDelta,_hatDelta;
        private Color[] _glowColors;
        private Color[] _channelColors = new Color[16];
        private bool IsEnabled() { return presentationEnabled && (lanes == null || lanes.instrumentRouter == null || lanes.instrumentRouter.IsDrumsEnabled()); }
        private void Initialize()
        {
            if(_ready)return;
            _headRest=new Vector3[heads.Length];_cymbalRest=new Vector3[cymbals.Length];
            _cymbalRotation=new Quaternion[cymbals.Length];_stickRotation=new Quaternion[sticks.Length];
            _headDelta=new Vector3[heads.Length];_hatDelta=new Vector3[cymbals.Length];_glowColors=new Color[glowRenderers.Length];
            for(int i=0;i<heads.Length;i++)if(heads[i]!=null)_headRest[i]=heads[i].localPosition;
            for(int i=0;i<heads.Length;i++)if(heads[i]!=null)_headDelta[i]=heads[i].parent.InverseTransformVector((i==0?transform.forward:Vector3.up)*.0015f);
            for(int i=0;i<cymbals.Length;i++)if(cymbals[i]!=null){_cymbalRest[i]=cymbals[i].localPosition;_cymbalRotation[i]=cymbals[i].localRotation;}
            for(int i=0;i<cymbals.Length;i++)if(cymbals[i]!=null)_hatDelta[i]=cymbals[i].parent.InverseTransformVector(Vector3.up*.018f);
            for(int i=0;i<glowRenderers.Length;i++)if(glowRenderers[i]!=null)_glowColors[i]=glowRenderers[i].sharedMaterial.GetColor("_Color");
            for(int i=0;i<sticks.Length;i++)if(sticks[i]!=null)_stickRotation[i]=sticks[i].localRotation;
            if(kickBeater!=null)_beaterRest=kickBeater.localRotation;
            if(kickPedal!=null)_kickPedalRest=kickPedal.localRotation;
            if(hiHatPedal!=null)_hatPedalRest=hiHatPedal.localRotation;
            _properties=new MaterialPropertyBlock();_ready=true;
        }
        public void BindSong(MidiSongData song)
        {
            BeginBindSong(song);
            while(!BindStep(512)) { }
        }
        public void BeginBindSong(MidiSongData song)
        {
            Initialize();_song=song;Clear(_generation);if(lanes!=null)lanes.BeginBindSong(song);
            ApplySongPalette();
        }
        public bool BindStep(int budget) { return lanes==null || lanes.BindStep(budget); }
        private void ApplySongPalette()
        {
            // Same canonical source-MIDI palette as PianoKeyboardView, including strings-only presentation.
            string text=_song==null || string.IsNullOrEmpty(_song.sourceHash)?"MusicMidiPlayer":_song.sourceHash.ToLowerInvariant();
            if(!string.IsNullOrEmpty(_performancePlayerName))text=_performancePlayerName;
            uint seed=2166136261u;
            for(int i=0;i<text.Length;i++)seed=(seed^(uint)text[i])*16777619u;
            uint tone=seed*1664525u+1013904223u;
            float hue=((seed&0x00ffffffu)*(360f/16777216f))/360f;
            float saturation=.70f+.18f*((tone&0x0000ffffu)/65535f);
            float value=.90f+.10f*((tone>>16)/65535f);
            Color color=Color.HSVToRGB(hue,saturation*.82f,value);
            for(int i=0;i<_glowColors.Length;i++)_glowColors[i]=color;
            for(int channel=0;channel<16;channel++)
                _channelColors[channel]=Color.HSVToRGB(Mathf.Repeat(hue+channel*.38196601125f,1f),saturation*.82f,value);
            if(lanes!=null){lanes.SetPalette(color);lanes.SetChannelPalette(_channelColors);}
        }
        public void SetPerformancePalette(string displayName)
        {
            Initialize();displayName=displayName??"";
            if(_performancePlayerName==displayName)return;
            _performancePlayerName=displayName;ApplySongPalette();
        }
        public Color FileChannelColor(int channel)
        { Initialize(); return _channelColors[Mathf.Clamp(channel,0,15)]; }
        public Color CurrentPaletteColor()
        { Initialize(); return _glowColors.Length > 0 ? _glowColors[0] : Color.white; }
        private int Upper(int ms)
        {
            int lo=0,hi=_song==null || lanes==null?0:lanes.noteIds.Length;
            while(lo<hi){int m=lo+(hi-lo)/2;if(_song.startMs[lanes.noteIds[m]]<=ms)lo=m+1;else hi=m;}return lo;
        }
        public void Clear(int generation)
        {
            Initialize();_generation=generation;_hasPosition=false;_liveMode=false;_openHat=false;
            for(int i=0;i<72;i++)_strength[i]=0;
            for(int i=0;i<9;i++)_ringCursor[i]=0;
            ApplyPose(0);if(lanes!=null)lanes.Clear();
        }
        public void RebuildAt(int ms,int generation)
        {
            Clear(generation);if(!IsEnabled())return;
            _lastMs=ms;_hasPosition=true;_noteCursor=Upper(ms);
            if(_song!=null)
            {
                int first=Upper(ms-1900);
                // Restore residual motion without replaying historical attack effects.
                for(int i=Mathf.Max(first,_noteCursor-256);i<_noteCursor;i++)
                {int n=lanes.noteIds[i];Record(lanes.DrumPitch(n),_song.velocity[n],_song.startMs[n],_song.channel[n]);}
                for(int i=_noteCursor-1;i>=0;i--)
                {int n=lanes.noteIds[i], pitch=lanes.DrumPitch(n);if(pitch==42 || pitch==44 || pitch==46)
                    {_openHat=pitch==46;break;}}
            }
            ApplyPose(ms);if(lanes!=null)lanes.Refresh(ms);
        }
        public void Refresh(int ms,int generation)
        {
            Initialize();if(!IsEnabled()){Clear(generation);return;}
            if(_liveMode || !_hasPosition || generation!=_generation || ms<_lastMs || ms-_lastMs>150)
            {RebuildAt(ms,generation);return;}
            int end=Upper(ms);
            for(int i=Mathf.Max(_noteCursor,end-64);i<end;i++)
            {int n=lanes.noteIds[i];if(ms-_song.startMs[n]<=80)
                {Record(lanes.DrumPitch(n),_song.velocity[n],_song.startMs[n],_song.channel[n]);processedHitCount++;}}
            _noteCursor=end;_lastMs=ms;ApplyPose(ms);if(lanes!=null)lanes.Refresh(ms);
        }
        public void LiveHit(int pitch,int velocity,int generation)
        {
            Initialize();if(!IsEnabled())return;
            if(!_liveMode || generation!=_generation){Clear(generation);_liveMode=true;ApplySongPalette();}
            int ms=(int)(Time.realtimeSinceStartup*1000f);Record(pitch,velocity,ms,-1);processedHitCount++;
            ApplyPose(ms);if(lanes!=null)lanes.LiveHit(pitch,velocity);
        }
        public void RefreshLive()
        {
            if(!IsEnabled() || !_liveMode)return;ApplyPose((int)(Time.realtimeSinceStartup*1000f));if(lanes!=null)lanes.RefreshLive();
        }
        private void Record(int pitch,int velocity,int ms,int channel)
        {
            int lane=bank==null?-1:bank.Lane(pitch);if(lane<0)return;
            if(channel>=0 && lane<_glowColors.Length)_glowColors[lane]=_channelColors[Mathf.Clamp(channel,0,15)];
            int slot=lane*8+_ringCursor[lane];_ringCursor[lane]=(_ringCursor[lane]+1)%8;
            _times[slot]=ms;_strength[slot]=Mathf.Sqrt(velocity/127f);
            if(pitch==46)_openHat=true;if(pitch==42 || pitch==44)_openHat=false;
        }
        private void ApplyPose(int ms)
        {
            for(int lane=0;lane<9;lane++)
            {
                float impact=0,wobble=0;
                for(int i=lane*8;i<lane*8+8;i++)
                {
                    if(_strength[i]<=0 || ms<_times[i])continue;
                    float age=(ms-_times[i])*.001f;
                    if(age>2f){_strength[i]=0;continue;}
                    impact+=_strength[i]*Mathf.Exp(-age*22f);
                    wobble+=_strength[i]*Mathf.Sin(age*34f)*Mathf.Exp(-age*3.5f);
                }
                impact=Mathf.Min(1.4f,impact);wobble=Mathf.Clamp(wobble,-1.6f,1.6f);
                if(lane<heads.Length && heads[lane]!=null)
                    heads[lane].localPosition=_headRest[lane]-_headDelta[lane]*impact;
                if(lane<cymbals.Length && cymbals[lane]!=null)
                {
                    cymbals[lane].localRotation=_cymbalRotation[lane]*Quaternion.Euler(wobble*4.5f,0,wobble*2.4f);
                    cymbals[lane].localPosition=_cymbalRest[lane]+(lane==2 && _openHat?_hatDelta[lane]:Vector3.zero);
                }
                if(lane<sticks.Length && sticks[lane]!=null)
                    sticks[lane].localRotation=_stickRotation[lane]*Quaternion.Euler(-25f*impact,0,0);
                if(lane<glowTransforms.Length && glowTransforms[lane]!=null)
                {
                    glowTransforms[lane].localScale=Vector3.one*(.13f+.10f*(1-Mathf.Clamp01(impact)));
                    if(lane<glowRenderers.Length && glowRenderers[lane]!=null)
                    {
                        glowRenderers[lane].enabled=presentationEnabled && impact>.015f;
                        Color color=_glowColors[lane];color.a=Mathf.Clamp01(impact)*.55f;
                        _properties.Clear();_properties.SetColor("_Color",color);_properties.SetColor("_EmissionColor",color*2f);
                        glowRenderers[lane].SetPropertyBlock(_properties);
                    }
                }
                if(lane==0)
                {
                    if(kickBeater!=null)kickBeater.localRotation=_beaterRest*Quaternion.Euler(impact*28f,0,0);
                    if(kickPedal!=null)kickPedal.localRotation=_kickPedalRest*Quaternion.Euler(impact*12f,0,0);
                }
                if(lane==2 && hiHatPedal!=null)hiHatPedal.localRotation=_hatPedalRest*Quaternion.Euler(_openHat?0:10f,0,0);
            }
        }
        private void OnDisable(){if(_ready)Clear(_generation);}
    }
}
