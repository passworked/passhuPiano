using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Data;

namespace MusicMidiPlayer.Views
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiPercussionLaneView : UdonSharpBehaviour
    {
        public PercussionBankData bank;
        public Transform[] hitPoints=new Transform[0], bars=new Transform[0];
        public int windowMs=2400;
        public float height=1.85f;
        [HideInInspector] public int visibleCount,droppedCount;
        private MidiSongData _song;
        [System.NonSerialized] public int[] noteIds=new int[0];
        private MaterialPropertyBlock _properties;
        private Material _noteMaterial;
        private int[] _livePitch=new int[64];
        private float[] _liveStart=new float[64];
        private int _liveCursor;
        public void BindSong(MidiSongData song)
        {
            _song=song;int count=0;
            if(song!=null && bank!=null)for(int n=0;n<song.channel.Length;n++)if(song.channel[n]==9 && bank.Lane(song.pitch[n])>=0)count++;
            noteIds=new int[count];int cursor=0;
            if(song!=null && bank!=null)for(int n=0;n<song.channel.Length;n++)if(song.channel[n]==9 && bank.Lane(song.pitch[n])>=0)noteIds[cursor++]=n;
            Clear();
        }
        public void SetPalette(Color color)
        {
            if(_properties==null)_properties=new MaterialPropertyBlock();
            if(_noteMaterial==null)
                for(int i=0;i<bars.Length;i++)if(bars[i]!=null)
                {var renderer=bars[i].GetComponent<Renderer>();if(renderer!=null){_noteMaterial=renderer.material;break;}}
            if(_noteMaterial==null)return;
            // Match the piano's color/HDR property handling; one cached material per drum view.
            _noteMaterial.SetColor("_Color",color);_noteMaterial.SetColor("_BaseColor",color);
            _noteMaterial.SetColor("_EmissionColor",color*3f);
            for(int i=0;i<bars.Length;i++)if(bars[i]!=null)
            {
                var renderer=bars[i].GetComponent<Renderer>();if(renderer==null)continue;
                renderer.sharedMaterial=_noteMaterial;
                _properties.Clear();_properties.SetFloat("_NoteOrder",(i+1f)/Mathf.Max(1,bars.Length));
                renderer.SetPropertyBlock(_properties);
            }
        }
        public void Clear()
        {
            for(int i=0;i<bars.Length;i++)if(bars[i]!=null)bars[i].gameObject.SetActive(false);
            for(int i=0;i<64;i++)_liveStart[i]=-100;
            visibleCount=0;droppedCount=0;
        }
        public void Refresh(int ms)
        {
            if(_song==null || bank==null)return;
            visibleCount=0;droppedCount=0;
            int lo=0,hi=noteIds.Length;
            while(lo<hi){int m=lo+(hi-lo)/2;if(_song.startMs[noteIds[m]]<ms-80)lo=m+1;else hi=m;}
            int first=lo;
            int last=lo;hi=noteIds.Length;
            int until=ms>int.MaxValue-windowMs?int.MaxValue:ms+windowMs;
            while(last<hi){int m=last+(hi-last)/2;if(_song.startMs[noteIds[m]]<=until)last=m+1;else hi=m;}
            droppedCount=Mathf.Max(0,last-first-bars.Length);
            for(int i=first;i<last && visibleCount<bars.Length;i++)
            {
                int n=noteIds[i];
                int lane=bank.Lane(_song.pitch[n]);if(lane<0)continue;
                Show(visibleCount++,lane,Mathf.Max(0,(_song.startMs[n]-ms)/(float)windowMs)*height,_song.pitch[n],n);
            }
            for(int i=visibleCount;i<bars.Length;i++)if(bars[i]!=null)bars[i].gameObject.SetActive(false);
        }
        private void Show(int slot,int lane,float offset,int pitch,int identity)
        {
            if(lane>=hitPoints.Length || hitPoints[lane]==null || bars[slot]==null)return;
            var bar=bars[slot];bar.gameObject.SetActive(true);
            bar.position=hitPoints[lane].position+Vector3.up*(offset+.04f)+transform.right*((identity%3-1)*.018f);
            // Open hi-hat uses a thin plate; closed/pedal uses a short diamond-like block.
            bar.localScale=new Vector3(pitch==46?.13f:.09f,.065f,pitch==46?.025f:.065f);
        }
        public void LiveHit(int pitch,int velocity)
        {
            int slot=_liveCursor++%Mathf.Min(64,Mathf.Max(1,bars.Length));
            _livePitch[slot]=pitch;_liveStart[slot]=Time.realtimeSinceStartup;
        }
        public void RefreshLive()
        {
            visibleCount=0;
            for(int i=0;i<Mathf.Min(64,bars.Length);i++)
            {
                float age=Time.realtimeSinceStartup-_liveStart[i];
                int lane=bank.Lane(_livePitch[i]);
                if(age>=0 && age<.5f && lane>=0){Show(i,lane,age*.85f,_livePitch[i],i);visibleCount++;}
                else if(bars[i]!=null)bars[i].gameObject.SetActive(false);
            }
        }
    }
}
