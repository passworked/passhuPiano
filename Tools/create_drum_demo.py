"""Deterministic mixed melody/GM percussion integration sample."""
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Tools/LiveMidi/python_packages'))
import mido

def track(name,events,channel):
    result=mido.MidiTrack([mido.MetaMessage('track_name',name=name,time=0)])
    last=0
    for tick,kind,note,velocity in sorted(events,key=lambda e:(e[0],e[1]=='note_on',e[2])):
        result.append(mido.Message(kind,channel=channel,note=note,velocity=velocity,time=tick-last));last=tick
    return result

def main():
    song=mido.MidiFile(type=1,ticks_per_beat=480)
    song.tracks.append(mido.MidiTrack([mido.MetaMessage('set_tempo',tempo=600000),mido.MetaMessage('time_signature',numerator=4,denominator=4)]))
    melody=[];drums=[]
    def hit(beat,pitch,velocity=104,duration=.055):
        tick=round(beat*480);drums.extend([(tick,'note_on',pitch,velocity),(tick+round(duration*480),'note_off',pitch,0)])
    for beat in range(32):
        pitch=(60,64,67,72)[beat%4];tick=beat*480
        melody.extend([(tick,'note_on',pitch,90),(tick+180,'note_off',pitch,0)])
        hit(beat,36 if beat%2==0 else 38)
        hit(beat,42,78);hit(beat+.5,46 if beat%4==3 else 42,92 if beat%4==3 else 67)
    hit(0,49,112);hit(8,51);hit(16,57,112);hit(24,55)
    for i,note in enumerate((41,43,45,47,48,50)):hit(14+i*.25,note)
    for i in range(24):hit(27+i*.125,38,55+i%4*18)
    for i,note in enumerate(range(35,82)):hit(34+i*.4,note,108)
    song.tracks.extend([track('Melody',melody,0),track('GM Drum Kit',drums,9)])
    out=ROOT/'Assets/UdonMidiPlayer/Samples/GMDrumStudy.mid';out.parent.mkdir(exist_ok=True,parents=True);song.save(out)
    print(out)

if __name__=='__main__':main()
