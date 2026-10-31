"""Second Blender-only cover: exact Twinkle score and blue MIDI waterfall.

Open JapaneseMusicClassroom-Cover.blend, then run this script. The score is
engraved from the project MIDI. Nothing in Assets is written and Unity is
not accessed.
"""
import bpy, math, sys, random, runpy, re
from pathlib import Path
from mathutils import Vector, Matrix

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
OUT=ROOT/"Docs/UdonMidiPlayer/Covers"
OUT.mkdir(parents=True,exist_ok=True)
PREVIEW="--preview" in sys.argv
scene=bpy.context.scene
bpy.context.preferences.filepaths.save_version=0

def v(p):
    return Vector((-p[0],-p[2],p[1]))

# The source cover is preserved; remove notation only in this derived cover.
for obj in list(bpy.data.objects):
    if any(obj.name.startswith(prefix) for prefix in ['Music staff','Chalk note','Chalk stem']):
        bpy.data.objects.remove(obj,do_unlink=True)

effects=bpy.data.collections.new("Cover v2 - blue MIDI crystal waterfall")
scene.collection.children.link(effects)
def material(name,color,emission,transmission):
    m=bpy.data.materials.new(name)
    m.diffuse_color=(*color,1)
    m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=.15
    p.inputs['Roughness'].default_value=.16
    p.inputs['Transmission Weight'].default_value=transmission
    p.inputs['IOR'].default_value=1.42
    p.inputs['Coat Weight'].default_value=.35
    p.inputs['Coat Roughness'].default_value=.1
    p.inputs['Emission Color'].default_value=(*color,1)
    p.inputs['Emission Strength'].default_value=emission
    return m
light=material("Cover v2 - pale blue crystal",(.005,.15,.85),.85,.22)
dark=material("Cover v2 - deep blue crystal",(.003,.045,.32),.75,.18)
glint=material("Cover v2 - blue key impact",(.02,.32,1.0),1.2,0)

def bar(name,x,bottom,length,width,mat,depth=.026):
    bpy.ops.mesh.primitive_cube_add(size=1,location=v((x,bottom+length*.5,-.56)))
    obj=bpy.context.object
    obj.name=name
    obj.scale=(width,depth,length)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.data.materials.append(mat)
    mod=obj.modifiers.new('Crystal bevels','BEVEL')
    mod.width=min(width*.22,.006)
    mod.segments=2
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    effects.objects.link(obj)
    obj.visible_shadow=False
    return obj

score=runpy.run_path(str(HERE/'engrave_twinkle.py'))['build_score'](scene,HERE)
# The waterfall is also sampled from this same MIDI opening. Pitch X positions
# come from the real imported piano keys, with timing/lengths from the MIDI.
keys={}
for obj in scene.objects:
    match=re.match(r'Key_(\d{3})_',obj.name)
    if match and obj.type=='MESH':
        keys[int(match.group(1))]=-sum((obj.matrix_world@Vector(c)).x for c in obj.bound_box)/8
needed={n['pitch'] for n in score['notes']}
assert needed.issubset(keys), 'Missing physical key positions for MIDI cover'
for i,n in enumerate(score['notes']):
    beat=n['tick']/score['ppq']
    if beat>14:
        continue
    black=n['pitch']%12 in (1,3,6,8,10)
    bar('Blue MIDI source note %02d'%i,keys[n['pitch']],.80+beat*.135,
        (n['end']-n['tick'])/score['ppq']*.135,
        .018 if black else .026,dark if black else light)
    if n['tick']==0:
        bar('Blue key impact',keys[n['pitch']],.772,.009,.030,glint,.11)

# Modest bloom from the emissive blue crystal geometry, composited in Blender.
tree=bpy.data.node_groups.new('Cover v2 soft bloom','CompositorNodeTree')
tree.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
scene.compositing_node_group=tree
layers=tree.nodes.new('CompositorNodeRLayers')
glare=tree.nodes.new('CompositorNodeGlare')
glare.inputs['Type'].default_value='Fog Glow'
glare.inputs['Quality'].default_value='High'
glare.inputs['Threshold'].default_value=1.8
glare.inputs['Size'].default_value=.18
glare.inputs['Strength'].default_value=.14
output=tree.nodes.new('NodeGroupOutput')
tree.links.new(layers.outputs['Image'],glare.inputs['Image'])
tree.links.new(glare.outputs['Image'],output.inputs['Image'])
scene.render.use_compositing=True
scene.render.resolution_x=1280 if PREVIEW else 3840
scene.render.resolution_y=720 if PREVIEW else 2160
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGB'
scene.render.image_settings.color_depth='8' if PREVIEW else '16'
scene.cycles.samples=48 if PREVIEW else 256
scene.cycles.adaptive_threshold=.025 if PREVIEW else .008
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX'
prefs.get_devices()
for device in prefs.devices:
    device.use=device.type=='OPTIX'
scene.cycles.device='GPU'
scene.render.filepath=str(OUT/("classroom-cover-v2-preview.png" if PREVIEW else "classroom-cover-v2-4k.png"))
bpy.ops.file.pack_all()
if not PREVIEW:
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE/"JapaneseMusicClassroom-Cover-v2.blend"))
print("COVER_V2_SCORE","No.0 小星星 / ChatGPT / 12 measures / 90 notes")
bpy.ops.render.render(write_still=True)
print("COVER_V2_RENDERED",scene.render.filepath)
if not PREVIEW:
    scene.render.resolution_x=1920
    scene.render.resolution_y=1080
    scene.render.image_settings.file_format='JPEG'
    scene.render.image_settings.color_depth='8'
    scene.render.image_settings.quality=96
    scene.cycles.samples=128
    scene.render.filepath=str(OUT/"classroom-cover-v2-1080p.jpg")
    bpy.ops.render.render(write_still=True)
    print("COVER_V2_UPLOAD_RENDERED",scene.render.filepath)
