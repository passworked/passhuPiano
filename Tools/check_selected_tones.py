"""Build the focused backing-Udon verification request from the existing MIDI probe."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
req=json.loads((root/'Logs/instrument-test.json').read_text(encoding='utf-8'))
s=req['code'].replace('foreach(int pitch in new[]{0,35,36,60,96,97,127}) {','foreach(int pitch in new[]{0,36,38,39,60,96,108,127}) foreach(int velocity in new[]{1,44,45,69,70,100,101,127}) {')
s=s.replace('"midiNoteOnVelocity",100);u.SendCustomEvent("_midiNoteOn");','"midiNoteOnVelocity",velocity);u.SendCustomEvent("_midiNoteOn");',1)
s=s.replace('rows.Add(new {instrument,pitch,clip=sources[slot].clip.name,ratio=sources[slot].pitch});','''var activeBank=(VRC.Udon.UdonBehaviour)v.GetProgramVariable("bank");
  var clips=(AudioClip[])activeBank.GetProgramVariable("clips");
  int region=((int[])v.GetProgramVariable("_slotRegion"))[slot];
  int expectedLayer=instrument==2?0:velocity<=44?0:velocity<=69?1:velocity<=100?2:3;
  if(region/(instrument==2?61:30)!=expectedLayer || sources[slot].clip!=clips[region])throw new Exception("Wrong actual velocity layer");
  var roots=(int[])activeBank.GetProgramVariable("rootPitch"); var tunes=(float[])activeBank.GetProgramVariable("tuningCents");
  float expectedRatio=Mathf.Pow(2f,(pitch-roots[region])/12f+tunes[region]/1200f);
  if(Mathf.Abs(sources[slot].pitch-expectedRatio)>.0001f)throw new Exception("Wrong actual pitch ratio");
  rows.Add(new {instrument,pitch,velocity,region,clip=sources[slot].clip.name,ratio=sources[slot].pitch});''')
s=s.replace('live[i]&&notes[i]==pitch','live[i]&&notes[i]==pitch&&((double[])v.GetProgramVariable("_liveRelease"))[i]<0')
s=s.replace('"MIDI failed instrument="+instrument+" pitch="+pitch','"MIDI failed instrument="+instrument+" pitch="+pitch+" velocity="+velocity+" slot="+slot+" held="+held[pitch]+" live="+live[slot]+" playing="+sources[slot].isPlaying')
s=s.replace('if(((bool[])u.GetProgramVariable("_liveHeld"))[pitch])throw new System.Exception("Stuck note");','if(((bool[])u.GetProgramVariable("_liveHeld"))[pitch])throw new System.Exception("Stuck note");\n  u.SetProgramVariable("midiControlChangeChannel",0);u.SetProgramVariable("midiControlChangeNumber",120);u.SetProgramVariable("midiControlChangeValue",0);u.SendCustomEvent("_midiControlChange");')
s=s.replace('Logs/instrument-midi-playmode.json','Logs/selected-q4-playmode.json')
req['code']=s
(root/'Logs/selected-q4-probe.json').write_text(json.dumps(req),encoding='utf-8')
