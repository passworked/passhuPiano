"""Read-only SMF note extraction for the current cover's exact Twinkle source."""
from pathlib import Path
import struct,json,hashlib

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/"Assets/music/Classic/01_Twinkle_Twinkle_Little_Star.mid"
data=SOURCE.read_bytes()
header_len=struct.unpack_from(">I",data,4)[0]
fmt,tracks,ppq=struct.unpack_from(">HHH",data,8)
position=8+header_len
notes,tempos,meters,track_names=[],[],[],[]
for track in range(tracks):
    if data[position:position+4]!=b"MTrk":
        raise ValueError("Invalid MIDI track")
    length=struct.unpack_from(">I",data,position+4)[0]
    p=position+8
    end=p+length
    tick,status=0,0
    active={}
    def vlq():
        global p
        value=0
        while True:
            b=data[p];p+=1
            value=(value<<7)|(b&127)
            if b<128:
                return value
    while p<end:
        tick+=vlq()
        event=data[p]
        if event>=128:
            status=event
            p+=1
        if status==255:
            kind=data[p];p+=1
            size=vlq()
            payload=data[p:p+size];p+=size
            if kind==81:
                tempos.append(dict(tick=tick,microseconds=int.from_bytes(payload,"big")))
            elif kind==88:
                meters.append(dict(tick=tick,numerator=payload[0],denominator=2**payload[1]))
            elif kind==3:
                track_names.append(dict(track=track,name=payload.decode("utf-8",errors="replace")))
        elif status in (240,247):
            size=vlq()
            p+=size
        else:
            kind=status>>4
            channel=status&15
            size=1 if kind in (12,13) else 2
            a=data[p]
            b=data[p+1] if size==2 else 0
            p+=size
            key=(channel,a)
            if kind==9 and b:
                active.setdefault(key,[]).append((tick,b))
            elif kind==8 or (kind==9 and not b):
                if active.get(key):
                    start,velocity=active[key].pop(0)
                    notes.append(dict(tick=start,end=tick,pitch=a,velocity=velocity,track=track,channel=channel))
    position=end
notes.sort(key=lambda n:(n["tick"],-n["pitch"]))
score=dict(source=str(SOURCE.relative_to(ROOT)),sha256=hashlib.sha256(data).hexdigest(),
           ppq=ppq,format=fmt,tempos=tempos,meters=meters,tracks=track_names,notes=notes)
path=Path(__file__).resolve().parent/"twinkle-score.json"
path.write_text(json.dumps(score,indent=2),encoding="utf-8")
print("TWINKLE",json.dumps(dict(ppq=ppq,tempos=tempos,meters=meters,tracks=track_names,
    note_count=len(notes),beats=max(n["end"] for n in notes)/ppq)))
for bar in range(12):
    print("BAR",bar+1,[(round(n["tick"]/ppq-bar*4,3),n["pitch"],
        round((n["end"]-n["tick"])/ppq,3)) for n in notes if bar*4*ppq<=n["tick"]<(bar+1)*4*ppq])
