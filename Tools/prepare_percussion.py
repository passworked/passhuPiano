"""Build the pinned Virtuosity GM drum bank. Originals remain in Downloads."""
from pathlib import Path
import concurrent.futures, hashlib, json, re, shutil, subprocess, time, urllib.request, urllib.parse

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Downloads/PercussionResearch/virtuosity'
OUT=ROOT/'Assets/UdonMidiPlayer/Audio/VirtuosityDrums'
COMMIT='9f04cf9a734527edfbb0a4eee1f674e45bbf71bc'
BASE=f'https://raw.githubusercontent.com/sfzinstruments/virtuosity_drums/{COMMIT}/'

def download(path):
    dest=SOURCE/path
    if dest.exists() and dest.stat().st_size: return dest
    dest.parent.mkdir(parents=True,exist_ok=True)
    for attempt in range(4):
        try:
            with urllib.request.urlopen(BASE+urllib.parse.quote(path),timeout=50) as response:
                data=response.read()
            if data.startswith(b'version https://git-lfs'): raise RuntimeError('LFS pointer: '+path)
            dest.write_bytes(data)
            return dest
        except Exception:
            if attempt==3: raise
            time.sleep(attempt+1)

def parallel_download(paths):
    files=sorted(set(paths))
    with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
        futures={pool.submit(download,p):p for p in files}
        for i,f in enumerate(concurrent.futures.as_completed(futures),1):
            f.result()
            if i%20==0 or i==len(files): print(f'Downloaded {i}/{len(files)}',flush=True)

def parse_map(path):
    text=download(path).read_text(encoding='utf-8-sig')
    rows=[]
    for block in text.split('<region>')[1:]:
        values=dict(re.findall(r'(\w+)\s*=\s*([^\s<>]+)',re.sub(r'//[^\n]*','',block)))
        sample=values.get('sample','').replace('\\','/').removeprefix('../')
        if not sample: continue
        lo=int(values.get('lovel',1)); hi=int(values.get('hivel',127))
        rows.append(dict(source=sample,lo=lo,hi=hi,center=(lo+hi)/2))
    return rows

def main():
    inventory=subprocess.check_output(['git','-C',str(SOURCE),'ls-tree','-r','--name-only','HEAD'],encoding='utf-8').splitlines()
    maps=[p for p in inventory if p.startswith('Programs/mappings/perc/close/') or p.startswith('Programs/mappings/oh/') or (p.startswith('Programs/mappings/perc/oh/') and any(x in p for x in ('timbale','woodblock','cuica')))]
    parallel_download(maps+['LICENSE','README.md'])
    if '--maps' in __import__('sys').argv:
        print('\n'.join(maps));return
    # note: source map, physical lane, offline semitones, substitution description.
    defs={
      35:('kick_snoff',0,0,''),36:('kick_snon',0,0,''),37:('snare_crossstick',1,0,''),
      38:('snare_center',1,0,''),39:('snare_stickshot1',1,0,'Hand clap substituted by acoustic stickshot'),
      40:('snare_rimshot',1,0,'Electric snare substituted by acoustic rimshot'),
      41:('ltom_center',3,-3,'Derived tom tuning'),42:('hh_closed',2,0,''),
      43:('ltom_center',3,0,''),44:('hh_pedal',2,0,''),45:('ltom_center',4,3,'Derived tom tuning'),
      46:('hh_open',2,0,''),47:('htom_center',4,-2,'Derived tom tuning'),48:('htom_center',5,0,''),
      49:('crash_crash',6,0,''),50:('htom_center',5,2,'Derived tom tuning'),51:('ride_ride',7,0,''),
      52:('crash_crash',6,-4,'Chinese cymbal substituted by detuned crash'),53:('ride_bell',7,0,''),
      55:('flatride_crash',6,0,'Splash substituted by flat ride crash'),57:('crash_sizzle',6,0,''),
      59:('flatride_ride',7,0,''),
    }
    auxiliary={54:'tambourine',56:'cowbell',58:'vibraslap',60:'bongo_high',61:'bongo_low',62:'conga_muted',
       63:'conga_open',64:'tumba',65:'@timbale_13',66:'@timbale_14',67:'agogo_high',68:'agogo_low',
       69:'cabasa',70:'shaker_down',71:'whistle_short',72:'whistle_long',73:'guiro_fast',74:'guiro_slow',
       75:'claves',76:'@woodblock_high',77:'@woodblock_low',78:'@cuica_muted',79:'@cuica_open',80:'triangle_muted',81:'triangle_open'}
    # Exact auxiliary names are checked against the pinned tree, never guessed at runtime.
    for note,name in auxiliary.items(): defs[note]=('perc/'+name,8,0,'Shaker substituted for maracas' if note==70 else '')
    allrows=[]; source_files=[]
    for note,(name,lane,shift,substitution) in sorted(defs.items()):
        path='Programs/mappings/'+(('perc/oh/'+name[6:]) if name.startswith('perc/@') else ('perc/close/'+name[5:]) if name.startswith('perc/') else 'oh/'+name)+'_map.sfz'
        if path not in inventory: raise RuntimeError('Unknown source map '+path)
        regions=parse_map(path)
        layers=sorted({(r['lo'],r['hi'],r['center']) for r in regions})
        # Retain up to four original dynamic layers. Repartition around their original centers.
        selected=sorted({min(layers,key=lambda l:abs(l[2]-v)) for v in (16,53,90,122)},key=lambda l:l[2])
        for k,layer in enumerate(selected):
            lo=1 if k==0 else int((selected[k-1][2]+layer[2])/2)+1
            hi=127 if k==len(selected)-1 else int((layer[2]+selected[k+1][2])/2)
            variants=sorted({r['source'] for r in regions if (r['lo'],r['hi'],r['center'])==layer})[:2]
            for rr,src in enumerate(variants):
                source_files.append(src)
                allrows.append(dict(note=note,lane=lane,source=src,sourceMap=path,sourceLo=layer[0],sourceHi=layer[1],
                    lowVelocity=lo,highVelocity=hi,roundRobin=rr,roundRobinCount=len(variants),semitones=shift,
                    substitution=substitution,chokeGroup=1 if note in (42,44,46) else 0,
                    chokes=note in (42,44),openHat=note==46,gain=1.0))
    parallel_download(source_files)
    OUT.mkdir(parents=True,exist_ok=True)
    total_float=0
    # Share recordings across aliases; shifted tom/cymbal clips are explicitly derived.
    encodings={}
    for i,row in enumerate(allrows):
        key=(row['source'],row['semitones'])
        if key not in encodings:
            src=SOURCE/row['source']; digest=hashlib.sha256((row['source']+str(row['semitones'])).encode()).hexdigest()[:12]
            dst=OUT/(Path(row['source']).stem+'_'+digest+'.ogg')
            filters='aresample=48000'
            if row['semitones']: filters+=f',asetrate={48000*2**(row["semitones"]/12):.6f},aresample=48000'
            subprocess.run([shutil.which('ffmpeg'),'-hide_banner','-loglevel','error','-y','-i',str(src),
                '-af',filters,'-ar','48000','-ac','2','-c:a','libvorbis','-q:a','4',str(dst)],check=True)
            meta=json.loads(subprocess.check_output([shutil.which('ffprobe'),'-v','error','-show_streams','-of','json',str(dst)],encoding='utf-8'))['streams'][0]
            length=float(meta['duration']);frames=round(length*48000)
            encodings[key]=dict(file=dst.relative_to(ROOT).as_posix(),frames=frames,rate=48000,channels=2,
                bytes=dst.stat().st_size,sha256=hashlib.sha256(dst.read_bytes()).hexdigest(),
                sourceSha256=hashlib.sha256(src.read_bytes()).hexdigest())
            total_float+=frames*2*4
        row.update(encodings[key])
        if i%40==0: print(f'Encoded {i}/{len(allrows)}',flush=True)
    shutil.copyfile(SOURCE/'LICENSE',OUT/'LICENSE.txt')
    (OUT/'ATTRIBUTION.txt').write_text('Virtuosity Drums by Versilian Studios LLC and Karoryfer Samples; performed by Austin McMahon.\nCC0-1.0. https://github.com/sfzinstruments/virtuosity_drums\nPinned commit: '+COMMIT+'\nAdaptation: selected original velocity layers and up to two round robins, OH kit and close auxiliary percussion; 48 kHz stereo Vorbis q4, full natural tails, no normalization. Explicit GM aliases and offline tom/cymbal tuning are listed in BANK-MAPPING.json.\n',encoding='utf-8')
    manifest=dict(repository='sfzinstruments/virtuosity_drums',commit=COMMIT,license='CC0-1.0',quality=4,rows=allrows,
        clipCount=len(encodings),regionCount=len(allrows),mappedNotes=sorted(defs),float32Bytes=total_float,
        compressedBytes=sum(v['bytes'] for v in encodings.values()))
    (OUT/'BANK-MAPPING.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in manifest.items() if k!='rows'},indent=2),flush=True)

if __name__=='__main__':main()
