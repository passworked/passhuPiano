"""Stage representative VSCO CE strings for review; does not modify Unity assets."""
import concurrent.futures
import hashlib
import http.client
import json
import re
import struct
import subprocess
import time
import urllib.parse
import urllib.request
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Downloads/StringsResearch'
REPO = OUT / 'VSCO-2-CE'
WAV_COMMIT = '440300901dfe9275fd84e0b7763af1f8443ae62e'
SFZ_COMMIT = '6dd651d55dde97fd4028699be9d4481f26917891'
PATCHES = [
    ('ViolinSection', 69, ['ViolinEnsSusVib', 'ViolinEnsSpic', 'ViolinEnsPizz']),
    ('ViolaSection', 60, ['ViolaEnsSusVib', 'ViolaEnsSpic', 'ViolaEnsPizz']),
    ('CelloSection', 48, ['CelloEnsSusVib', 'CelloEnsSpic', 'CelloEnsPizz']),
    ('SoloContrabass', 40, ['ContrabassSusVB', 'ContrabassSpic', 'ContrabassPizz']),
    ('SoloViolin', 69, ['SViolinVib', 'SViolinSpic', 'SViolinPizz']),
]


def download(commit, source, destination):
    url = f'https://raw.githubusercontent.com/sgossner/VSCO-2-CE/{commit}/' + urllib.parse.quote(source, safe='/')
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not destination.exists():
        request = urllib.request.Request(url, headers={'User-Agent': 'MusicMidiPlayer-StringsResearch'})
        for attempt in range(3):
            try:
                with urllib.request.urlopen(request, timeout=25) as response:
                    data = response.read()
                break
            except (OSError, urllib.error.URLError, http.client.HTTPException):
                if attempt == 2:
                    raise
                time.sleep(1)
        destination.write_bytes(data)
    return url


def wave_info(path):
    with wave.open(str(path), 'rb') as wav:
        result = dict(rate=wav.getframerate(), channels=wav.getnchannels(),
                      frames=wav.getnframes(), bits=wav.getsampwidth()*8,
                      seconds=round(wav.getnframes()/wav.getframerate(), 3))
    result['embeddedLoops'] = []
    with path.open('rb') as stream:
        stream.seek(12)
        while True:
            header = stream.read(8)
            if len(header) < 8:
                break
            kind, size = struct.unpack('<4sI', header)
            if kind == b'smpl':
                chunk = stream.read(size)
                if len(chunk) >= 36:
                    fields = struct.unpack_from('<9I', chunk)
                    result['embeddedMidiUnityNote'] = fields[3]
                    for i in range(fields[7]):
                        if 36+(i+1)*24 > len(chunk):
                            raise ValueError(f'Truncated smpl loop in {path}')
                        loop = struct.unpack_from('<6I', chunk, 36+i*24)
                        result['embeddedLoops'].append(dict(type=loop[1], start=loop[2], end=loop[3]))
            else:
                stream.seek(size, 1)
            if size % 2:
                stream.seek(1, 1)
    return result


def prepare_patch(item):
    instrument, target, name = item
    sfz_path = OUT / 'Definitions' / (name+'.sfz')
    sfz_url = download(SFZ_COMMIT, name+'.sfz', sfz_path)
    text = sfz_path.read_text(encoding='utf-8-sig')
    default = re.search(r'^\s*default_path=(.*)$', text, re.M)[1].strip().replace('\\', '/')
    regions = []
    for body in re.split(r'<region>', text)[1:]:
        values = dict(re.findall(r'^\s*(\w+)\s*=\s*([^\r\n]+)', body, re.M))
        if 'sample' in values:
            regions.append(values)
    if not regions:
        raise ValueError(f'No regions in {name}')
    # Choose the closest recorded root, then the stronger velocity and first RR.
    region = min(regions, key=lambda r: (abs(int(r['pitch_keycenter'])-target),
                                         -int(r.get('lovel', 0)), r['sample']))
    source = default.rstrip('/')+'/'+region['sample'].strip().replace('\\', '/')
    if source not in SOURCE_PATHS:
        raise ValueError(f'SFZ sample missing from pinned WAV tree: {source}')
    path = OUT / 'Samples' / source
    url = download(WAV_COMMIT, source, path)
    data = path.read_bytes()
    return dict(instrument=instrument, patch=name, definitionUrl=sfz_url,
                definitionSha256=hashlib.sha256(sfz_path.read_bytes()).hexdigest(),
                regionCount=len(regions),
                recordedSampleCount=len({r['sample'] for r in regions}),
                recordedRootCount=len({int(r['pitch_keycenter']) for r in regions}),
                midiLow=min(int(r['lokey']) for r in regions),
                midiHigh=max(int(r['hikey']) for r in regions),
                velocityRanges=sorted({(int(r.get('lovel', 0)), int(r.get('hivel', 127))) for r in regions}),
                explicitLoopOpcodes=bool(re.search(r'\bloop_(?:start|end|mode)\s*=', text)),
                representative=dict(source=source, url=url,
                                    file=path.relative_to(ROOT).as_posix(),
                                    midiRoot=int(region['pitch_keycenter']),
                                    region=region, bytes=len(data),
                                    sha256=hashlib.sha256(data).hexdigest(), **wave_info(path)))


def main():
    global SOURCE_PATHS
    if not (REPO/'.git').exists():
        REPO.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', 'clone', '--filter=blob:none', '--depth', '1', '--no-checkout',
                        'https://github.com/sgossner/VSCO-2-CE.git', str(REPO)], check=True)
    exists = subprocess.run(['git', '-C', str(REPO), 'cat-file', '-e', WAV_COMMIT+'^{commit}'],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if exists.returncode:
        subprocess.run(['git', '-C', str(REPO), 'fetch', '--depth', '1', '--filter=blob:none',
                        'origin', WAV_COMMIT], check=True)
    SOURCE_PATHS = set(subprocess.check_output(
        ['git', '-C', str(REPO), 'ls-tree', '-r', '--name-only', WAV_COMMIT], text=True).splitlines())
    jobs = [(instrument, target, patch) for instrument, target, patches in PATCHES for patch in patches]
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        rows = list(executor.map(prepare_patch, jobs))
    for name in ['LICENSE', 'Readme.txt', 'README.md']:
        download(WAV_COMMIT, name, OUT / name)
    inventory = {}
    for path in sorted(SOURCE_PATHS):
        if path.startswith('Strings/') and path.endswith('.wav'):
            folder = path.rsplit('/', 1)[0]
            inventory[folder] = inventory.get(folder, 0)+1
    result = dict(status='research-only; not imported into Unity or listening-approved',
                  source='https://versilian-studios.com/vsco-community/', license='CC0-1.0',
                  wavCommit=WAV_COMMIT, sfzCommit=SFZ_COMMIT,
                  note='Use SFZ MIDI roots; filename octave labels are not standard scientific pitch notation.',
                  inventory=inventory, patches=rows,
                  representativeBytes=sum(r['representative']['bytes'] for r in rows))
    (OUT/'manifest.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(dict(patches=len(rows), sampleBytes=result['representativeBytes'],
                         summary=[dict(patch=r['patch'], range=[r['midiLow'],r['midiHigh']],
                                       samples=r['recordedSampleCount'], seconds=r['representative']['seconds'],
                                       embeddedLoops=len(r['representative']['embeddedLoops'])) for r in rows])))


if __name__ == '__main__':
    main()
