"""Stage the audition-selected q4 banks without altering existing asset GUIDs."""
import argparse, hashlib, json, re, shutil, subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / 'Downloads/ToneAudition'
BASE = ROOT / 'Downloads/SalamanderAnalysis'
OUT = ROOT / 'Assets/UdonMidiPlayer/Audio/SelectedQ4'

def main():
    global PREVIEW, BASE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--audition-dir', type=Path, default=PREVIEW,
                        help='Reviewed q4 audio, Iowa mapping and gain calibration directory')
    parser.add_argument('--salamander-analysis-dir', type=Path, default=BASE,
                        help='Directory containing analysis.json and compression.json')
    args = parser.parse_args()
    PREVIEW, BASE = args.audition_dir.resolve(), args.salamander_analysis_dir.resolve()
    for required in (BASE/'analysis.json', BASE/'compression.json', PREVIEW/'iowa-mapping.json', PREVIEW/'gain-calibration.json'):
        if not required.is_file():
            parser.error(f'Required reviewed source input missing: {required}. Existing shipped banks do not require regeneration.')
    analysis = json.loads((BASE/'analysis.json').read_text(encoding='utf-8'))
    compression = json.loads((BASE/'compression.json').read_text(encoding='utf-8'))
    zones = {z['sample_id']: z for z in analysis['zones']}
    signed = lambda x: x-65536 if x >= 32768 else x
    piano = []
    for s in compression['samples']:
        layer = int(re.search(r'v(\d+)$', s['name'])[1])
        if layer not in (3,7,11,15): continue
        z = zones[s['left_sample_id']]
        piano.append(dict(file=s['name']+'-q4.ogg', path=str(PREVIEW/'quality-data'/(s['name']+'-q4.ogg')),
                          pitch=analysis['samples'][s['left_sample_id']]['root'], layer=layer,
                          tune=signed(z['generators'].get('52',0)),
                          release=round(1000*2**(signed(z['generators'].get('38',53536))/1200)), gain=1.0))
    piano.sort(key=lambda r:(r['layer'],r['pitch']))
    assert len(piano)==120
    iowa = sorted((r for r in json.loads((PREVIEW/'iowa-mapping.json').read_text()) if r['mallet']=='yarn'),key=lambda r:r['pitch'])
    assert len(iowa)==60 and len({r['pitch'] for r in iowa})==60
    calibration=json.loads((PREVIEW/'gain-calibration.json').read_text())
    gain=10**(calibration['sources']['marimbaYarn']['gainDb']/20)
    marimba=[dict(file=Path(r['quality']['4']['path']).name,path=r['quality']['4']['path'],pitch=r['pitch'],layer=1,tune=0,release=900,gain=gain,source=r['source']) for r in iowa]
    OUT.mkdir(parents=True,exist_ok=True)
    # AudioSource.pitch is limited to 3. Pretranspose only the upper edge so MIDI127
    # retains its pitch. This is an auxiliary copy, never counted as a recording.
    edge=next(r for r in iowa if r['pitch']==96)
    edge_path=OUT/'Marimba.yarn.derived.C8-q4.ogg'
    subprocess.run([shutil.which('ffmpeg'),'-hide_banner','-loglevel','error','-y','-i',str(ROOT/'Downloads/MarimbaIowa/yarn'/edge['source']),
                    '-af',f"aresample=48000,atrim=start_sample={edge['trimmedLeadingFrames']},asetrate=96000,aresample=48000",
                    '-c:a','libvorbis','-q:a','4',str(edge_path)],check=True)
    marimba.append(dict(file=edge_path.name,path=str(edge_path),pitch=108,layer=1,tune=0,release=900,gain=gain,derivedFrom=96))
    for bank,rows in [('Piano',piano),('Marimba',marimba)]:
        folder=OUT/bank;folder.mkdir(exist_ok=True)
        for r in rows:
            src=Path(r.pop('path'));dst=folder/r['file']
            shutil.copyfile(src,dst)
            r['sha256']=hashlib.sha256(dst.read_bytes()).hexdigest();r['bytes']=dst.stat().st_size
            r['file']=dst.relative_to(ROOT).as_posix()
    edge_path.unlink()
    shutil.copyfile(ROOT/'Downloads/MarimbaIowa/SOURCE-LICENSE.txt',OUT/'Marimba/LICENSE.txt')
    for src in (ROOT/'Assets/UdonMidiPlayer/Audio/SalamanderV9').glob('*.txt'):
        if src.name=='ORIGINAL-README.txt':shutil.copyfile(src,OUT/'Piano'/src.name)
    (OUT/'Piano/ATTRIBUTION.txt').write_text('Salamander Grand Piano — Alexander Holm; FreePats SF2 v3+20200602. CC BY 3.0. https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html\nAdaptation: original v3/v7/v11/v15, 30 minor-third roots per layer, 48kHz stereo Vorbis q4 encoded from lossless extracted FLAC, full tails; original SF2 tuning and release. Velocity boundaries 44/69/100 match audition nearest-center selection. Retain ORIGINAL-README.txt.\n',encoding='utf-8')
    (OUT/'Marimba/ATTRIBUTION.txt').write_text('University of Iowa Musical Instrument Samples, yarn ff, 60 real roots C2–C7, one velocity layer. Original labelled Eb2 measures D2; Eb2 uses nearest root. 48kHz stereo Vorbis q4, leading silence removed as audition, full tails. One auxiliary C8 clip derived from C7 supports MIDI127 within AudioSource pitch limit; not an additional recording. Constant gain matches audition reference. See LICENSE.txt.\n',encoding='utf-8')
    report=dict(quality=4,piano=piano,marimba=marimba,pianoLayers=[3,7,11,15],velocityUpperBounds=[44,69,100,127],marimbaRecordedCount=60,
                bytes={k:sum(r['bytes'] for r in rows) for k,rows in [('piano',piano),('marimba',marimba)]})
    (OUT/'BANK-MAPPING.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'clips':{'piano':len(piano),'marimba':len(marimba)},'bytes':report['bytes']}))

if __name__=='__main__':main()
