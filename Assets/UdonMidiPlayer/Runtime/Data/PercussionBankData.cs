using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.Data
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PercussionBankData : UdonSharpBehaviour
    {
        public string bankHash;
        public AudioClip[] clips = new AudioClip[0];
        public int[] notes = new int[0], lowVelocity = new int[0], highVelocity = new int[0];
        public int[] roundRobin = new int[0], roundRobinCount = new int[0], referenceVelocity = new int[0];
        public int[] lanes = new int[0], frames = new int[0], noteOffsets = new int[0];
        public float[] gains = new float[0], peaks = new float[0];
        public int maxTailMs;

        public bool IsValid()
        {
            int n=clips==null?0:clips.Length;
            if(n==0 || noteOffsets==null || noteOffsets.Length!=129 || noteOffsets[0]!=0 || noteOffsets[128]!=n) return false;
            if(notes.Length!=n || lowVelocity.Length!=n || highVelocity.Length!=n || roundRobin.Length!=n
                || roundRobinCount.Length!=n || referenceVelocity.Length!=n || lanes.Length!=n || frames.Length!=n
                || gains.Length!=n || peaks.Length!=n || maxTailMs<=0) return false;
            for(int p=0;p<128;p++)
            {
                if(noteOffsets[p]<0 || noteOffsets[p+1]<noteOffsets[p] || noteOffsets[p+1]>n) return false;
                for(int r=noteOffsets[p];r<noteOffsets[p+1];r++)
                    if(clips[r]==null || notes[r]!=p || frames[r]<=0 || lanes[r]<0 || lanes[r]>8
                        || lowVelocity[r]<1 || highVelocity[r]>127 || highVelocity[r]<lowVelocity[r]
                        || referenceVelocity[r]<1 || roundRobinCount[r]<1 || roundRobin[r]<0 || roundRobin[r]>=roundRobinCount[r]
                        || float.IsNaN(gains[r]) || float.IsInfinity(gains[r]) || gains[r]<0
                        || float.IsNaN(peaks[r]) || float.IsInfinity(peaks[r]) || peaks[r]<0) return false;
            }
            return true;
        }

        public int SelectRegion(int pitch,int velocity,int eventId)
        {
            if(pitch<0 || pitch>127 || velocity<=0 || noteOffsets.Length!=129) return -1;
            // Use high bits so a normal NoteOn/NoteOff stream (step two) also varies its samples.
            int variant=((eventId*214013+2531011)>>16)&0x7fff;
            for(int r=noteOffsets[pitch];r<noteOffsets[pitch+1];r++)
                if(velocity>=lowVelocity[r] && velocity<=highVelocity[r]
                    && roundRobin[r]==variant%roundRobinCount[r]) return r;
            return -1;
        }
        public float Gain(int region,int velocity)
        {
            // Retain the recording's dynamics, with a continuous response within each selected layer.
            return gains[region]*Mathf.Clamp(velocity/(float)referenceVelocity[region],0f,1.5f);
        }
        public int Lane(int pitch)
        {
            return pitch>=0 && pitch<128 && noteOffsets.Length==129 && noteOffsets[pitch]<noteOffsets[pitch+1]
                ? lanes[noteOffsets[pitch]] : -1;
        }
    }
}
