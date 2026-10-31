"""Download pinned CC0 VCSL marimba and bake a piano-compatible 30-root bank.

The source is sparsely sampled: derived roots are resampled, never additional
recordings. Original WAVs remain outside Assets. No tail trimming or looping.
"""
import concurrent.futures, hashlib, json, math, shutil, subprocess, urllib.parse, urllib.request, wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMMIT = 'c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e'
BASE = f'https://raw.githubusercontent.com/sgossner/VCSL/{COMMIT}/'
ORIGINAL = ROOT / 'Downloads/MarimbaVCSL'
OUTPUT = ROOT / 'Assets/UdonMidiPlayer/Audio/Marimba'
FFMPEG = shutil.which('ffmpeg')

def fetch(path, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists():
        with urllib.request.urlopen(BASE + urllib.parse.quote(path), timeout=90) as response:
            target.write_bytes(response.read())
    return target

def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    notes = {'F1':29,'C2':36,'G2':43,'B2':47,'F3':53,'C4':60,'G4':67,'B4':71,'F5':77,'C6':84}
    def download(item):
        name, pitch, layer = item
        filename = f'Marimba_hit_Outrigger_{name}_{layer}_01.wav'
        path = fetch('Idiophones/Struck Idiophones/Marimba/' + filename, ORIGINAL / filename)
        with wave.open(str(path)) as wav:
            return {'pitch':pitch,'layer':layer,'path':path,'rate':wav.getframerate(),'channels':wav.getnchannels()}
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        sources = list(pool.map(download, [(name,pitch,layer) for layer in ('soft','med','loud') for name,pitch in notes.items()]))
    license_path = fetch('LICENSE', ORIGINAL / 'LICENSE.txt')
    shutil.copyfile(license_path, OUTPUT / 'LICENSE.txt')
    records = []
    for layer,pitch in [(layer,pitch) for layer in ('soft','med','loud') for pitch in [21] + list(range(24,109,3))]:
        src = min((s for s in sources if s['layer']==layer), key=lambda s:(abs(s['pitch']-pitch),s['pitch']))
        file = OUTPUT / (f'Marimba_{pitch:03d}.ogg' if layer=='med' else f'Marimba_{layer}_{pitch:03d}.ogg')
        rate = round(src['rate'] * 2**((pitch-src['pitch'])/12))
        subprocess.run([FFMPEG,'-hide_banner','-loglevel','error','-y','-i',str(src['path']),
                        '-af',f'asetrate={rate},aresample=48000','-c:a','libvorbis','-q:a','8',str(file)],check=True)
        records.append({'pitch':pitch,'layer':layer,'file':file.name,'recordedPitch':src['pitch'],
                        'semitonesResampled':pitch-src['pitch'],'source':src['path'].name,
                        'sourceSha256':hashlib.sha256(src['path'].read_bytes()).hexdigest(),
                        'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'bytes':file.stat().st_size})
    total = sum(x['bytes'] for x in records)
    if total > 10188801: raise RuntimeError(f'Encoded bank exceeds piano budget: {total}')
    report = {'source':'https://github.com/sgossner/VCSL','commit':COMMIT,'license':'CC0-1.0',
              'recordedRoots':sorted(notes.values()),'velocityLayers':{'soft':[1,42],'med':[43,84],'loud':[85,127]},
              'physicalRange':[36,96], 'selectionRange':[21,108], 'encodedSourceBytes':total,
              'pianoEncodedBudgetBytes':10188801,'clips':records,
              'adaptation':'Three recorded velocity layers, 30 derived playback roots per layer (90 clips). Nearest original recording within each layer; edge extension and sparse intervals are offline pitch resampling, not separately recorded notes. Full natural tails, no loops. MIDI 0..127 retains original pitch, same edge-selection rule as piano.'}
    (OUTPUT/'BANK-MAPPING.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    (OUTPUT/'ATTRIBUTION.txt').write_text('Versilian Community Sample Library — Versilian Studios LLC\nhttps://github.com/sgossner/VCSL\nCC0-1.0; pinned commit '+COMMIT+'\nAdapted for Music MIDI Player: all three recorded velocity layers (soft/med/loud), nearest-source resampling into 30 playback roots A0–C8 per layer (90 clips), 48 kHz Vorbis q8, full natural tails. Original recordings F1–C6; upper/lower extension is resampled, not recorded coverage. No loops. MIDI velocities 1–42 select soft, 43–84 medium, 85–127 loud.\n',encoding='utf-8')
    print(json.dumps({'bytes':total,'clips':len(records)}))

if __name__ == '__main__': main()
