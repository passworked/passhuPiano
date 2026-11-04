"""Reposition existing rugs and two workshop stools; no new geometry or dressing."""
import bpy
import json
import re
import runpy
from pathlib import Path
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
REPORT = ROOT / 'Logs/ClockworkComfort'


def apply_layout():
    changed = []
    keys = set()
    rugs = [o for o in bpy.context.scene.objects
            if any(c.name.startswith('12 |') for c in o.users_collection)
            and 'rug' in o.name.lower()]
    seats = [o for o in bpy.context.scene.objects
             if o.name.startswith(('Workshop stool seat', 'Splayed stool leg'))]
    if len(seats) != 10 or not rugs:
        raise RuntimeError('Expected the existing two five-part stools and woven rug.')
    rug_transform = (Matrix.Translation(Vector((0, -1.8, 0)))
                     @ Matrix.Diagonal((.80, .84, 1, 1))
                     @ Matrix.Translation(Vector((-.05, -.4, 0))))
    for ob in rugs + seats:
        # Keep the original authored matrix so maintenance/export cannot compound offsets.
        if 'cw_recital_original_matrix' not in ob:
            ob['cw_recital_original_matrix'] = [v for row in ob.matrix_world for v in row]
        values = ob['cw_recital_original_matrix']
        original = Matrix([values[i:i+4] for i in range(0, 16, 4)])
        if ob in rugs:
            transform = rug_transform
        else:
            left = original.translation.x < -6
            origin = Vector((-7.6 if left else -4.75, -6.05, 0))
            target = Vector((-2.0 if left else 2.0, -5.5, 0))
            transform = Matrix.Translation(target - origin)
        ob.matrix_world = transform @ original
        changed.append(ob.name)
        for collection in ob.users_collection:
            for slot in ob.material_slots:
                if slot.material:
                    keys.add((collection.name.split(' |')[0],
                              'CW_' + re.sub('[^A-Za-z0-9]+', '_', slot.material.name).strip('_')))
    bpy.context.view_layer.update()
    return {'objectsRepositioned': changed, 'newObjects': 0, 'batchKeys': sorted(keys),
            'rugCenterUnity': [0, 0, -1.8], 'rugScaleXZ': [.8, .84],
            'stoolCentersUnity': [[-2.0, .54, -5.5], [2.0, .54, -5.5]]}


if __name__ == '__main__':
    REPORT.mkdir(parents=True, exist_ok=True)
    result = apply_layout()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'ClockworkMaintenanceStreet.blend'))
    result['patch'] = runpy.run_path(str(HERE / 'refresh_decorative_lettering.py'))['patch_batches'](result['batchKeys'], REPORT)
    (REPORT / 'layout-model.json').write_text(json.dumps(result, indent=2), encoding='utf8')
    print('RECITAL_LAYOUT', json.dumps(result), flush=True)
