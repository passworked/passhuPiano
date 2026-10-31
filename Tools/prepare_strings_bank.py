"""Prepare pinned VSCO CE string banks, with natural attacks and seamless loop bodies.

Run: uv run --with numpy --with soundfile python Tools/prepare_strings_bank.py
Only this script downloads/derives audio; the Unity importer retains asset GUIDs.
"""
import concurrent.futures
import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path

import numpy as np
import soundfile as sf
from prepare_strings_research import download, WAV_COMMIT, SFZ_COMMIT

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT/'Downloads/StringsResearch'
OUT = ROOT/'Assets/UdonMidiPlayer/Audio/Strings'
RATE = 48000
SPECS = [
    ('SoloViolin', ['SViolinVib', 'SViolinSpic', 'SViolinPizz']),
    ('ViolinSection', ['ViolinEnsSusVib', 'ViolinEnsSpic', 'ViolinEnsPizz']),
    ('ViolaSection', ['ViolaEnsSusVib', 'ViolaEnsSpic', 'ViolaEnsPizz']),
    ('CelloSection', ['CelloEnsSusVib', 'CelloEnsSpic', 'CelloEnsPizz']),
    ('Contrabass', ['ContrabassSusVB', 'ContrabassSpic', 'ContrabassPizz']),
]


def regions(name):
    path = STAGE/'Definitions'/(name+'.sfz')
    download(SFZ_COMMIT, name+'.sfz', path)
    text = path.read_text(encoding='utf-8-sig')
    folder = re.search(r'^\s*default_path=(.*)$', text, re.M)[1].strip().replace('\\','/')
    by_root = {}
    for body in re.split('<region>', text)[1:]:
        r = dict(re.findall(r'^\s*(\w+)\s*=\s*([^\r\n]+)',body,re.M))
        if 'sample' not in r:
            continue
        r['path'] = folder.rstrip('/')+'/'+r['sample'].strip().replace('\\','/')
        root = int(r['pitch_keycenter'])
        # One documented strong recording layer, no synthetic velocity layers or RR claims.
        if root not in by_root or (-int(r.get('lovel',0)),r['sample']) < (-int(by_root[root].get('lovel',0)),by_root[root]['sample']):
            by_root[root] = r
    return by_root


def fetch(job):
    name, style, root, region = job
    src = STAGE/'Samples'/region['path']
    download(WAV_COMMIT, region['path'], src)
    return job


def make_loop(audio):
    # Search a quiet-energy, spectrally similar join inside the settled bow.
    # The head is only used at the crossfade, never repeated with the initial attack.
    fade = int(.08*RATE)
    max_end = min(len(audio)-int(.9*RATE), int(6.0*RATE))
    starts = np.arange(int(1.2*RATE), min(int(2.8*RATE),max_end-int(1.2*RATE)), int(.1*RATE))
    best = None
    for lo in starts:
        head = audio[lo:lo+fade]
        for length in (1.0,1.3,1.7,2.1):
            hi = lo+int(length*RATE)+fade
            if hi > max_end:
                continue
            tail = audio[hi-fade:hi]
            # Wide overlap avoids relying on waveform phase matching of ensemble recordings.
            hs = np.abs(np.fft.rfft(head*np.hanning(fade)))
            ts = np.abs(np.fft.rfft(tail*np.hanning(fade)))
            score = float(np.mean((np.log1p(hs)-np.log1p(ts))**2))
            score += float(abs(np.sqrt(np.mean(head*head))-np.sqrt(np.mean(tail*tail))))
            if best is None or score < best[0]:
                best = (score,int(lo),int(hi))
    if best is None:
        raise ValueError('No settled sustain interval long enough to loop')
    score,lo,hi = best
    blend = np.linspace(0,1,fade,dtype=np.float32)
    body = audio[lo+fade:hi].copy()
    # Constant-sum blend preserves level for correlated bow texture.
    body[-fade:] = audio[hi-fade:hi]*(1-blend)+audio[lo:lo+fade]*blend
    attack = audio[:lo+fade].copy()
    return attack,body,dict(startFrame=lo,endFrame=hi,crossfadeFrames=fade,score=score,
                            loopBoundaryStep=float(abs(body[-1]-body[0])))


def process(job):
    name,style,root,region = job
    source = STAGE/'Samples'/region['path']
    raw = subprocess.check_output([shutil.which('ffmpeg'),'-hide_banner','-loglevel','error',
                                  '-i',str(source),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
    audio = np.frombuffer(raw,dtype='<f4').copy()
    # Preserve the natural attack, removing only silence before it.
    audible = np.flatnonzero(np.abs(audio) > .0004)
    leading = max(0,int(audible[0])-int(.025*RATE)) if len(audible) else 0
    audio = audio[leading:]
    audio[:min(240,len(audio))] *= np.linspace(0,1,min(240,len(audio)),dtype=np.float32)
    folder = OUT/name
    folder.mkdir(parents=True,exist_ok=True)
    base = f'{name}-{style}-{root:03}'
    loop_info = None
    if style == 0:
        attack,body,loop_info = make_loop(audio)
    else:
        attack,body = audio,None
        fade = min(int(.025*RATE),len(attack))
        attack[-fade:] *= np.linspace(1,0,fade,dtype=np.float32)
    def save(kind,values):
        path = folder/(base+'-'+kind+'.wav')
        sf.write(path,values,RATE,subtype='PCM_16')
        return path.relative_to(ROOT).as_posix()
    result = dict(bank=name,articulation=style,root=root,attack=save('attack',attack),
                  loop=save('loop',body) if body is not None else '',
                  attackFrames=len(attack),loopFrames=len(body) if body is not None else 0,
                  peak=float(max(np.max(np.abs(attack)),np.max(np.abs(body)) if body is not None else 0)),
                  releaseMs=700 if style == 0 else 180,
                  source=region['path'],sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                  sourceVelocity=[int(region.get('lovel',0)),int(region.get('hivel',127))],
                  trimmedFrames=leading,loopAnalysis=loop_info)
    return result


def main():
    jobs = [(name,style,root,r) for name,patches in SPECS for style,patch in enumerate(patches)
            for root,r in sorted(regions(patch).items())]
    print('Downloading',len(jobs),'recorded roots / articulations',flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
        list(executor.map(fetch,jobs))
    print('Preparing attack / sustain clips',flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        rows = list(executor.map(process,jobs))
    # Auxiliary upper roots preserve original MIDI pitch within AudioSource.pitch <= 3.
    for name,patches in SPECS:
        for style in range(3):
            source = max((r for r in rows if r['bank']==name and r['articulation']==style),key=lambda r:r['root'])
            for pitch in range(source['root']+12,121,12):
                row = dict(source,root=pitch,derivedFrom=source['root'])
                for key in ('attack','loop'):
                    if not source[key]: continue
                    dest = OUT/name/f'{name}-{style}-{pitch:03}-{key}.wav'
                    ratio = 2**((pitch-source['root'])/12)
                    subprocess.run([shutil.which('ffmpeg'),'-hide_banner','-loglevel','error','-y',
                        '-i',str(ROOT/source[key]),'-af',f'asetrate={round(RATE*ratio)},aresample={RATE}',
                        '-c:a','pcm_s16le',str(dest)],check=True)
                    row[key]=dest.relative_to(ROOT).as_posix()
                    with sf.SoundFile(dest) as f: row[key+'Frames' if key=='attack' else 'loopFrames']=len(f)
                rows.append(row)
    for row in rows:
        for key in ('attack','loop'):
            if row[key]: row[key+'Sha256']=hashlib.sha256((ROOT/row[key]).read_bytes()).hexdigest()
    result = dict(wavCommit=WAV_COMMIT,sfzCommit=SFZ_COMMIT,sampleRate=RATE,channels=1,
                  velocityLayers=1,articulations=['sustain','spiccato','pizzicato'],regions=rows,
                  note='Single strong recorded layer; auxiliary roots are derived, not extra recordings. Loop joins require listening approval.')
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/'BANK-MAPPING.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    for file in ('LICENSE','Readme.txt','README.md'):
        download(WAV_COMMIT,file,STAGE/file)
        shutil.copyfile(STAGE/file,OUT/file if file!='README.md' else OUT/'ORIGINAL-README.md')
    (OUT/'ATTRIBUTION.txt').write_text('VSCO 2 Community Edition — Versilian Studios / Sam Gossner; Ivy Audio / Simon Dalzell. CC0-1.0.\nhttps://versilian-studios.com/vsco-community/\nAdaptation: mono 48 kHz PCM16, one strong recorded velocity layer, natural attacks and crossfaded sustain bodies; explicit spiccato and pizzicato; auxiliary upper roots. Full provenance in BANK-MAPPING.json.\n',encoding='utf-8')
    print(json.dumps(dict(regions=len(rows),recorded=len(jobs),bytes=sum(p.stat().st_size for p in OUT.rglob('*.wav')))),flush=True)


if __name__=='__main__': main()
