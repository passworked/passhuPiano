"""Update the two decorative shop signs, preserving every other FBX object.

Run with Blender 5.2: blender -b ClockworkMaintenanceStreet.blend --python-exit-code 1 -P this_file.py
Requires the locally installed Segoe Script font; no font file is redistributed.
The normal environment exporter also calls apply_lettering before saving.
"""
import bpy
import hashlib
import json
import random
import re
from pathlib import Path
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
MODEL = ROOT / 'Assets/Scenes/ClockworkMaintenanceStreet/Models/ClockworkMaintenanceStreet.fbx'
REPORT = ROOT / 'Logs/ClockworkDecor'
SIGN_BODY = 'Bottega del tempo'


def apply_lettering():
    font_path = Path('C:/Windows/Fonts/segoesc.ttf')
    if not font_path.is_file():
        raise RuntimeError('Segoe Script must be installed before exporting decorative lettering.')
    font = next((f for f in bpy.data.fonts if Path(bpy.path.abspath(f.filepath)) == font_path), None)
    font = font or bpy.data.fonts.load(str(font_path))
    signs = [o for o in bpy.context.scene.objects if o.type == 'FONT' and o.name.startswith('Hand painted shop name')]
    if len(signs) != 2:
        raise RuntimeError('Expected the two existing decorative shop signs.')
    for ob in signs:
        ob.data.body = SIGN_BODY
        ob.data.font = font
        ob.data.align_x = 'CENTER'
        ob.data.size = .30
        ob.data.resolution_u = 6
        bpy.context.view_layer.update()
        # Keep the writing inside the existing fascia, including tall ascenders.
        factor = min(1.0, 2.05 / ob.dimensions.x, .22 / ob.dimensions.y)
        ob.data.size *= factor
    bpy.context.view_layer.update()
    return signs


def fingerprint(elem):
    h = hashlib.sha256()
    h.update(elem.id)
    h.update(elem.props_type)
    for p in elem.props:
        h.update(p.tobytes() if hasattr(p, 'tobytes') else repr(p).encode())
    for child in elem.elems:
        h.update(fingerprint(child).encode())
    return h.hexdigest()


def encode_element(src, encode_bin):
    dst = encode_bin.FBXElem(src.id)
    methods = {
        'Y': 'add_int16', 'C': 'add_char', 'B': 'add_bool', 'Z': 'add_int8', 'I': 'add_int32',
        'L': 'add_int64', 'F': 'add_float32', 'D': 'add_float64',
        'R': 'add_bytes', 'S': 'add_string', 'i': 'add_int32_array',
        'l': 'add_int64_array', 'f': 'add_float32_array', 'd': 'add_float64_array',
        'b': 'add_bool_array', 'c': 'add_byte_array',
    }
    for kind, value in zip(src.props_type, src.props):
        getattr(dst, methods[chr(kind)])(value)
    dst.elems = [encode_element(child, encode_bin) for child in src.elems]
    return dst


def patch_batches(batch_keys, report_folder=REPORT):
    """Replace only existing static collection/material geometries, preserving FBX IDs."""
    from collections import defaultdict
    from io_scene_fbx import parse_fbx, encode_bin
    batch_keys = set(batch_keys)
    zones = {key[0] for key in batch_keys}
    mat_index = {m.name: 'CW_' + re.sub('[^A-Za-z0-9]+', '_', m.name).strip('_')
                 for m in bpy.data.materials}
    selected = [ob for ob in bpy.context.scene.objects
                if ob.type in {'MESH', 'CURVE', 'FONT'} and not ob.hide_render
                and any((c.name.split(' |')[0], mat_index[s.material.name]) in batch_keys
                        for c in ob.users_collection for s in ob.material_slots if s.material)]
    if any(any((ob.users_collection[0].name.split(' |')[0], mat_index[s.material.name]) not in batch_keys
               for s in ob.material_slots if s.material) for ob in selected):
        raise RuntimeError('Mixed-material source requires reviewing the batch patch.')
    OUT = report_folder / 'staging'
    (OUT / 'Models').mkdir(parents=True, exist_ok=True)
    materials = {key[1]: {'color': [.84, .68, .38]} for key in batch_keys}
    scene = bpy.context.scene
    scope = globals() | locals() | {'defaultdict': defaultdict, 'cw_motion': {}}
    exporter = (HERE / 'export_unity.py').read_text(encoding='utf8')
    fragment = exporter[exporter.index('turn=Matrix.Diagonal'):exporter.index('\ncolliders=[]')]
    random.seed(404)
    exec(compile(fragment, str(HERE / 'export_unity.py'), 'exec'), scope)

    original, version = parse_fbx.parse(str(MODEL))
    partial, partial_version = parse_fbx.parse(str(OUT / 'Models/ClockworkMaintenanceStreet.fbx'))
    if version != partial_version:
        raise RuntimeError('Unexpected FBX version change.')
    original_objects = next(e for e in original.elems if e.id == b'Objects')
    partial_objects = next(e for e in partial.elems if e.id == b'Objects')
    normalize = lambda n: re.sub(rb'\.\d+(?=\x00)', b'', n)
    replacements = {normalize(e.props[1]): e for e in partial_objects.elems if e.id == b'Geometry'}
    expected = {f'Clockwork_{z}__{mat}'.encode() + b'\x00\x01Geometry' for z, mat in batch_keys}
    if set(replacements) != expected:
        raise RuntimeError('Unexpected replacement geometry names: ' + repr(set(replacements)))
    before = {e.props[0]: fingerprint(e) for e in original_objects.elems}
    changed_ids = []
    for elem in original_objects.elems:
        if elem.id == b'Geometry' and normalize(elem.props[1]) in replacements:
            # Preserve original FBX IDs, mesh names, connections, materials and nodes.
            elem.elems[:] = replacements[normalize(elem.props[1])].elems
            changed_ids.append(elem.props[0])
    if len(changed_ids) != len(expected):
        raise RuntimeError('Not all target mesh batches existed in the original model.')
    temp = report_folder / 'ClockworkMaintenanceStreet-patched.fbx'
    encode_bin.write(str(temp), encode_element(original, encode_bin), version)
    check, _ = parse_fbx.parse(str(temp))
    after_objects = next(e for e in check.elems if e.id == b'Objects')
    after = {e.props[0]: fingerprint(e) for e in after_objects.elems}
    changed = [key for key in before if before[key] != after[key]]
    if set(before) != set(after) or not set(changed).issubset(changed_ids):
        raise RuntimeError('An unrelated FBX object changed; model not replaced.')
    for old, new in zip(original.elems, check.elems):
        if old.id != b'Objects' and fingerprint(old) != fingerprint(new):
            raise RuntimeError('Non-geometry FBX data changed: ' + repr(old.id))
    MODEL.write_bytes(temp.read_bytes())
    geometries=[e for e in after_objects.elems if e.id==b'Geometry']
    triangle_count=sum(len(next(c for c in e.elems if c.id==b'PolygonVertexIndex').props[0])//3 for e in geometries)
    manifest_path=MODEL.parents[1]/'clockwork-manifest.json'
    if manifest_path.is_file():
        manifest=json.loads(manifest_path.read_text(encoding='utf8'))
        manifest['meshes']=len(geometries)
        manifest['triangles']=triangle_count
        manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf8')
    return {'changedGeometryNames': [n.decode().split('\x00')[0] for n in replacements],
              'preservedFbxObjectCount': len(before) - len(changed_ids), 'unchangedOtherFbxData': True,
              'environmentMeshCount':len(geometries),'environmentTriangleCount':triangle_count}


def refresh():
    signs = apply_lettering()
    report_signs = [{'name':o.name, 'body':o.data.body, 'dimensions':list(o.dimensions)} for o in signs]
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'ClockworkMaintenanceStreet.blend'))
    keys = {(c.name.split(' |')[0], 'CW_Polished_brass_wear') for ob in signs for c in ob.users_collection}
    result = patch_batches(keys)
    result['signs'] = report_signs
    (REPORT / 'model-patch.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf8')
    print('DECOR_PATCH', json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    refresh()
