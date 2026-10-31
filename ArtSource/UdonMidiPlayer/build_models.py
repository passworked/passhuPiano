"""Blender 5.2 source generator for the standalone digital piano body.

Exports contain piano geometry only. Cameras, lighting and studio floor stay
in Preview_Only. No screen, UI, waterfall or notes are included.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector

SOURCE = Path(__file__).resolve().parent
EXPORT = SOURCE / 'Exports'
PREVIEW = SOURCE / 'Previews'
EXPORT.mkdir(parents=True, exist_ok=True)
PREVIEW.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for block in list(bpy.data.materials):
    bpy.data.materials.remove(block)
scene = bpy.context.scene
bpy.context.preferences.filepaths.save_version = 0
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x = 1800
scene.render.resolution_y = 1250
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'

model_collection = bpy.data.collections.new('Export_Player')
scene.collection.children.link(model_collection)
preview_collection = bpy.data.collections.new('Preview_Only')
scene.collection.children.link(preview_collection)

def move_collection(obj, collection):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)

def uv(v):
    # Authoring helper takes horizontal X, height, rearward distance.
    return (v[0], v[2], v[1])

def mat(name, color, metallic=0, roughness=.4, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

mats = {
    'Body_Graphite': mat('Body_Graphite', (.034, .052, .065), .68, .32),
    'Side_Walnut': mat('Side_Walnut', (.24, .105, .052), 0, .38),
    'Trim_Aluminum': mat('Trim_Aluminum', (.34, .42, .44), .82, .28),
    'Accent_Teal': mat('Accent_Teal', (.03, .68, .59), .35, .28, 1.8),
    'Screen_Ink': mat('Screen_Ink', (.009, .023, .032), .12, .56),
    'Key_Ivory': mat('Key_Ivory', (.87, .88, .81), 0, .27),
    'Key_Ebony': mat('Key_Ebony', (.012, .016, .02), .05, .3),
}

def empty(name, parent=None, pos=(0, 0, 0), scale=1, collection=model_collection):
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = uv(pos)
    obj.scale = (scale,) * 3
    obj.empty_display_size = .04
    return obj

root = empty('MidiPlayer_Visual')
cabinet = empty('Cabinet', root)
view = empty('ViewRoot', root, (0, .78, 0), .025)
keyboard = empty('Keyboard', view)
view['local_white_key_width'] = 1.0

def finish_mesh(obj, material, bevel=0, segments=2):
    obj.data.materials.append(material)
    if bevel:
        modifier = obj.modifiers.new('Manufactured edge', 'BEVEL')
        modifier.width = bevel
        modifier.segments = segments
        modifier.affect = 'EDGES'
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    # Explicit UVs for retexturing; materials themselves require no textures.
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name='UVMap')
    layer = obj.data.uv_layers.active.data
    for poly in obj.data.polygons:
        normal = poly.normal
        drop = max(range(3), key=lambda a: abs(normal[a]))
        axes = [a for a in range(3) if a != drop]
        for li in poly.loop_indices:
            co = obj.data.vertices[obj.data.loops[li].vertex_index].co
            layer[li].uv = (co[axes[0]], co[axes[1]])
    for poly in obj.data.polygons:
        poly.use_smooth = False
    return obj

def box(name, pos, dimensions, material, parent, bevel=.002,
        collection=model_collection):
    bpy.ops.mesh.primitive_cube_add(size=1)
    obj = bpy.context.object
    obj.name = name
    move_collection(obj, collection)
    # Bake dimensions into geometry; all export object rotations stay identity.
    sx, sy, sz = dimensions
    for vertex in obj.data.vertices:
        vertex.co.x *= sx
        vertex.co.y *= sz
        vertex.co.z *= sy
    obj.parent = parent
    obj.location = uv(pos)
    return finish_mesh(obj, material, bevel)

G, W, T, A, S, I, E = (mats[n] for n in (
    'Body_Graphite', 'Side_Walnut', 'Trim_Aluminum', 'Accent_Teal',
    'Screen_Ink', 'Key_Ivory', 'Key_Ebony'))

box('Chassis', (0, .715, .002), (1.414, .10, .338), G, cabinet, .012, )
box('Lower_Reveal', (0, .665, .002), (1.356, .014, .294), T, cabinet, .004)
box('Cheek_Left', (-.69, .748, 0), (.054, .115, .35), W, cabinet, .008)
box('Cheek_Right', (.69, .748, 0), (.054, .115, .35), W, cabinet, .008)
box('Front_Rail', (0, .762, -.15), (1.326, .033, .024), G, cabinet, .005)
box('Front_Accent', (0, .76, -.163), (1.25, .0024, .0018), A, cabinet, .0006)
box('Rear_Deck', (0, .773, .081), (1.326, .026, .144), G, cabinet, .004)
box('Keybed_Shadow', (0, .767, -.059), (1.31, .004, .133), S, cabinet, .0005)
for side in (-1, 1):
    x = side * .60
    box('Stand_Post_' + str(side), (x, .348, .045), (.037, .63, .055), G, cabinet, .006)
    box('Stand_Foot_' + str(side), (x, .026, .015), (.067, .035, .385), G, cabinet, .01)
    box('Foot_Endcap_' + str(side), (x, .028, -.172), (.058, .025, .014), T, cabinet, .003)
box('Stand_Crossbar', (0, .256, .068), (1.18, .027, .028), G, cabinet, .004)

white_count = 0
layout = []
black_pcs = (1, 3, 6, 8, 10)
pitch_names = ('C', 'Cs', 'D', 'Ds', 'E', 'F', 'Fs', 'G', 'Gs', 'A', 'As', 'B')
for pitch in range(21, 109):
    black = pitch % 12 in black_pcs
    x = (white_count if black else white_count + .5) - 26
    layout.append({'pitch': pitch, 'x': x, 'width': .62 if black else 1,
                   'black': black, 'name': 'Key_%03d_%s%d' % (pitch, pitch_names[pitch % 12], pitch // 12 - 1)})
    if not black:
        white_count += 1

def white_key_mesh(name, prev_black, next_black):
    half = .478
    left = -.165 if prev_black else -half
    right = .165 if next_black else half
    # Blender -Y faces the player, positive X is treble/right.
    shape = [(-half, -5), (half, -5), (half, -3.04), (right, -3.04),
             (right, 0), (left, 0), (left, -3.04), (-half, -3.04)]
    # Remove consecutive duplicates at non-notched octave boundaries.
    outline = []
    for p in shape:
        if not outline or p != outline[-1]:
            outline.append(p)
    n = len(outline)
    vertices = [(x, y, z) for z in (-.36, 0) for x, y in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
    faces += [(j, (j + 1) % n, (j + 1) % n + n, j + n) for j in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    return mesh

for item in layout:
    pitch, x, name = item['pitch'], item['x'], item['name']
    if item['black']:
        obj = box(name, (x, .15, -1.5), (.62, .42, 3), E, keyboard, .035)
    else:
        prev_black = pitch > 21 and (pitch - 1) % 12 in black_pcs
        next_black = pitch < 108 and (pitch + 1) % 12 in black_pcs
        mesh = white_key_mesh(name + '_Mesh', prev_black, next_black)
        obj = bpy.data.objects.new(name, mesh)
        model_collection.objects.link(obj)
        obj.parent = keyboard
        obj.location = (x, 0, 0)
        finish_mesh(obj, I, .025)
    obj['midi_pitch'] = pitch
    obj['key_array_index'] = pitch - 21
    item['origin_blender_local'] = list(obj.location)

# Join decorative subparts by assembly. The 88 key Renderers remain separate.
def join_assembly(parent, name):
    children = [o for o in parent.children if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for obj in children:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = children[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + '_Mesh'
    return obj

join_assembly(cabinet, 'Cabinet_Static')

def export_fbx(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={'EMPTY', 'MESH'}, global_scale=1.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', use_space_transform=True,
        bake_space_transform=False, use_mesh_modifiers=True,
        mesh_smooth_type='FACE', use_triangles=True, bake_anim=False,
        add_leaf_bones=False, path_mode='AUTO')

export_fbx(EXPORT / 'MidiPlayer_Visual.fbx', list(model_collection.objects))
bpy.ops.export_scene.gltf(filepath=str(EXPORT / 'MidiPlayer_Visual.glb'),
    export_format='GLB', use_selection=True, export_animations=False)

manifest = {
    'generator': 'Blender ' + bpy.app.version_string,
    'world_unit': 'metre', 'blender_axes': 'X=pitch,Y=rear,Z=up',
    'viewRootScale': .025, 'viewRootPosition': [0, .78, 0],
    'keyboardWidthM': 1.3, 'contents': 'piano body, 88 keys, stand only',
    'keys': layout, 'materials': list(mats),
    'meshCount': sum(o.type == 'MESH' for o in model_collection.objects),
    'triangles': sum(sum(len(p.vertices) - 2 for p in o.data.polygons)
                     for o in model_collection.objects if o.type == 'MESH'),
}
(SOURCE / 'model_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')

# The saved Blender project includes a separately removable studio preview.
floor = mat('Studio_Floor', (.085, .105, .115), 0, .77)
box('Studio_Ground', (0, -.011, 0), (200, .02, 200), floor, None, 0, preview_collection)
world = bpy.data.worlds.new('Studio_World')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (.18, .23, .28, 1)
world.node_tree.nodes['Background'].inputs[1].default_value = .45

def area(name, location, power, size, color):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    preview_collection.objects.link(obj)
    obj.location = uv(location)
    obj.rotation_euler = (Vector(uv((0, .7, 0))) - obj.location).to_track_quat('-Z', 'Y').to_euler()

area('Key_Softbox', (-2, 3, -2), 450, 3, (1, .86, .73))
area('Fill_Softbox', (2.5, 2, -1), 220, 2, (.7, .88, 1))
area('Rim_Softbox', (0, 2.5, 1.8), 500, 2, (.55, .86, 1))
cam_data = bpy.data.cameras.new('Studio_Camera')
cam = bpy.data.objects.new('Studio_Camera', cam_data)
preview_collection.objects.link(cam)
scene.camera = cam

def camera(location, target, ortho):
    cam.location = uv(location)
    cam.rotation_euler = (Vector(uv(target)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ortho

camera((1.9, 1.6, -3), (0, .42, 0), 1.9)
scene.render.filepath = str(PREVIEW / 'MidiPlayer_Hero.png')
for obj in preview_collection.objects:
    obj.hide_select = True
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active = root
for screen in bpy.data.screens:
    for area_space in screen.areas:
        if area_space.type == 'VIEW_3D':
            space = area_space.spaces.active
            space.region_3d.view_perspective = 'CAMERA'
            space.overlay.show_extras = False
            space.shading.color_type = 'MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'MidiPlayer_Visual.blend'))
bpy.ops.render.render(write_still=True)
camera((0, 1.40, -2), (0, .78, -.04), 1.51)
scene.render.resolution_x = 1800
scene.render.resolution_y = 800
scene.render.filepath = str(PREVIEW / 'MidiPlayer_Keyboard.png')
bpy.ops.render.render(write_still=True)
print('MODEL_DELIVERY', json.dumps({k: manifest[k] for k in ('meshCount', 'triangles', 'viewRootScale')}))
