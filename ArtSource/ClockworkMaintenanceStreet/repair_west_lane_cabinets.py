"""Restore the three west-lane banks' lost assembly offsets in place.

Run in Blender with the existing editable source loaded. Only six existing
collection/material geometries are patched; FBX node IDs remain unchanged.
"""
import bpy
import json
import math
import runpy
from pathlib import Path
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
REPORT = ROOT / 'Logs/ClockworkCabinetFix'
PREFIX = 'West lane storage bank '


def numbered(kind):
    return sorted((o for o in bpy.context.scene.objects if o.name.startswith(PREFIX + kind)),
                  key=lambda o: int(o.name.rsplit('.', 1)[1]) if '.' in o.name else -1)


def apply_alignment():
    bodies = numbered('carcass')
    tops = numbered('oak top')
    fronts = numbered('drawer front')
    holders = numbered('label holder')
    labels = numbered('ivory label')
    handles = numbered('handle')
    if [len(g) for g in [bodies, tops, fronts, holders, labels, handles]] != [3, 3, 36, 36, 36, 36]:
        raise RuntimeError('Expected the three existing four-column, three-row west banks.')
    report = []
    for bank, body in enumerate(bodies):
        if 'cw_bank_anchor' not in body:
            body['cw_bank_anchor'] = list(body.location)
        anchor = Vector(body['cw_bank_anchor'])
        assembly = Matrix.Translation(anchor) @ Matrix.Rotation(math.pi / 2, 4, 'Z')

        def place(ob, local):
            ob.matrix_world = assembly @ Matrix.Translation(Vector(local))

        place(body, (0, 0, .6))
        place(tops[bank], (0, 0, 1.255))
        for slot in range(12):
            i = bank * 12 + slot
            column, row = divmod(slot, 3)
            x = (column - 1.5) * .575
            z = .2966666666666667 + row / 3
            place(fronts[i], (x, -.325, z))
            place(holders[i], (x, -.365, z + .025))
            place(labels[i], (x, -.380, z + .025))
            # Curves already contain their own assembly-local offsets. The old
            # endpoint was 32.5 mm clear of the drawer; embed it by 4 mm instead.
            handles[i].matrix_world = assembly
            points = handles[i].data.splines[0].points
            if len(points) != 4:
                raise RuntimeError('Unexpected existing drawer pull topology.')
            for p, (xx, yy, zz) in zip(points, [(x-.065,-.3535,z-.07),
                    (x-.065,-.410,z-.09),(x+.065,-.410,z-.09),(x+.065,-.3535,z-.07)]):
                p.co = (xx, yy, zz, 1)
        report.append({'bank': body.name, 'anchor': list(anchor), 'drawers': 12,
                       'bodyBottom': anchor.z, 'topUnderside': anchor.z + 1.2,
                       'pullEndpointEmbedMetres': .004})
    bpy.context.view_layer.update()
    # Check real transformed geometry rather than trusting the requested offsets.
    for bank, body in enumerate(bodies):
        low = min((body.matrix_world @ Vector(p)).z for p in body.bound_box)
        top_low = min((tops[bank].matrix_world @ Vector(p)).z for p in tops[bank].bound_box)
        body_high = max((body.matrix_world @ Vector(p)).z for p in body.bound_box)
        assert abs(low) < 1e-5 and abs(top_low - body_high) < 1e-5
        for slot in range(12):
            i = bank * 12 + slot
            expected_z = .2966666666666667 + (slot % 3) / 3
            assert abs(fronts[i].location.z - expected_z) < 1e-5
            assert abs(fronts[i].location.y - holders[i].location.y) < 1e-5
            assert abs(fronts[i].location.y - labels[i].location.y) < 1e-5
    return {'banks': report, 'batchKeys': [('11', 'CW_' + name) for name in [
        'Dark_structural_walnut', 'Worn_oak_edge', 'Honey_oak_long_grain',
        'Old_brushed_brass', 'Workshop_drawing_paper', 'Polished_brass_wear']]}


if __name__ == '__main__':
    REPORT.mkdir(parents=True, exist_ok=True)
    result = apply_alignment()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'ClockworkMaintenanceStreet.blend'))
    result['patch'] = runpy.run_path(str(HERE / 'refresh_decorative_lettering.py'))['patch_batches'](
        result['batchKeys'], REPORT)
    (REPORT / 'model-update.json').write_text(json.dumps(result, indent=2), encoding='utf8')
    print('CABINET_ALIGNMENT', json.dumps(result), flush=True)
