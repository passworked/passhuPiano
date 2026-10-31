"""Render cover artwork in Blender only. Does not export or touch Unity assets.

Usage: blender -b JapaneseMusicClassroom.blend -P render_cover.py -- --preview
       blender -b JapaneseMusicClassroom.blend -P render_cover.py
"""
import bpy, math, sys, random, json
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
OUT=ROOT/"Docs/UdonMidiPlayer/Covers"
OUT.mkdir(parents=True,exist_ok=True)
PREVIEW="--preview" in sys.argv
random.seed(31)
scene=bpy.context.scene
bpy.context.preferences.filepaths.save_version=0

def v(p):
    return Vector((-p[0],-p[2],p[1]))

def area(name,point,target,energy,size,color,size_y=None):
    data=bpy.data.lights.new(name,'AREA')
    data.energy=energy
    data.color=color
    data.shape='RECTANGLE'
    data.size=size
    data.size_y=size_y or size
    obj=bpy.data.objects.new(name,data)
    scene.collection.objects.link(obj)
    obj.location=v(point)
    obj.rotation_euler=(v(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj

for image in bpy.data.images:
    if image.source=='FILE':
        image.filepath=bpy.path.abspath(image.filepath)
        if Path(image.filepath).is_file():
            image.reload()

# Read the actual existing player model, without loading or modifying Unity.
prior=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(ROOT/"Assets/UdonMidiPlayer/Models/WhiteGrandPiano/WhiteGrandPiano.fbx"))
piano=[obj for obj in bpy.data.objects if obj not in prior]
col=bpy.data.collections.new("Cover - existing player piano")
scene.collection.children.link(col)
turn=Matrix.Rotation(math.pi,4,'Z')
for obj in piano:
    if obj.parent is None:
        obj.matrix_world=turn @ obj.matrix_world
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)
    if obj.type=='MESH':
        for mat in obj.data.materials:
            if not mat or not mat.use_nodes:
                continue
            bs=mat.node_tree.nodes.get("Principled BSDF")
            if not bs:
                continue
            if any(word in mat.name for word in ['Brass','Gold','Steel','Copper']):
                bs.inputs['Metallic'].default_value=.78
                bs.inputs['Roughness'].default_value=.24
            elif any(word in mat.name for word in ['White','Ivory','Ebony']):
                bs.inputs['Roughness'].default_value=.24 if 'Lacquer' in mat.name else .32
                bs.inputs['Coat Weight'].default_value=.28
                bs.inputs['Coat Roughness'].default_value=.18

# Reproduce the existing simple dark performance bench for the cover.
mat=bpy.data.materials.new("Cover bench charcoal")
mat.diffuse_color=(.055,.065,.075,1)
mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value=mat.diffuse_color
bs.inputs['Roughness'].default_value=.43
def box(name,point,size,material,bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1,location=v(point))
    obj=bpy.context.object
    obj.name=name
    obj.scale=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.data.materials.append(material)
    m=obj.modifiers.new("Edge highlights",'BEVEL')
    m.width=bevel
    m.segments=3
    return obj
box("Cover - existing performance seat cushion",(0,.425,-1.03),(.60,.10,.34),mat)
for x in [-.24,.24]:
    for z in [-1.15,-.91]:
        box("Cover - existing performance seat leg",(x,.1875,z),(.045,.375,.045),mat,.006)

# Physically renderable thin window glazing. Keep window light unobstructed.
glass=bpy.data.materials.get("Classroom_Glass")
nodes=glass.node_tree.nodes
nodes.clear()
output=nodes.new('ShaderNodeOutputMaterial')
transparent=nodes.new('ShaderNodeBsdfTransparent')
pane=nodes.new('ShaderNodeBsdfGlass')
pane.inputs['Color'].default_value=(.96,.985,1,1)
pane.inputs['Roughness'].default_value=.035
pane.inputs['IOR'].default_value=1.45
mix=nodes.new('ShaderNodeMixShader')
mix.inputs[0].default_value=.035
links=glass.node_tree.links
links.new(transparent.outputs[0],mix.inputs[1])
links.new(pane.outputs[0],mix.inputs[2])
links.new(mix.outputs[0],output.inputs['Surface'])
for obj in scene.objects:
    if obj.type=='MESH' and any(m==glass for m in obj.data.materials):
        obj.visible_shadow=False
    if obj.type=='EMPTY':
        obj.hide_render=True

# Retain the room geometry. Lighting/camera enhancements live in the cover file.
sun=bpy.data.objects.get("Golden afternoon reference sun")
sun.data.energy=2.3
sun.data.color=(1,.84,.64)
sun.data.angle=math.radians(3.5)
sun.rotation_euler=v((1,-.62,-.32)).to_track_quat('-Z','Y').to_euler()
world=bpy.data.worlds.new("Cover - soft afternoon sky")
scene.world=world
world.use_nodes=True
bg=world.node_tree.nodes.get('Background')
bg.inputs['Color'].default_value=(.52,.65,.82,1)
bg.inputs['Strength'].default_value=.45
area("Cover - soft window bounce",(-4.75,2.6,-.5),(0,1.2,.3),210,5.8,(1,.88,.70),2)
area("Cover - broad ceiling bounce",(0,3.55,-.8),(0,.4,-.8),85,4.2,(1,.93,.82),3.5)

camera=scene.camera
camera.location=v((3.05,1.75,-3.72))
camera.rotation_euler=(v((-.40,1.40,.65))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.lens=22
camera.data.sensor_width=36
camera.data.clip_start=.04
camera.data.clip_end=150
camera.data.dof.use_dof=False
scene.render.engine='CYCLES'
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX'
prefs.get_devices()
for device in prefs.devices:
    device.use=device.type=='OPTIX'
scene.cycles.device='GPU'
scene.cycles.samples=48 if PREVIEW else 256
scene.cycles.use_adaptive_sampling=True
scene.cycles.adaptive_threshold=.025 if PREVIEW else .008
scene.cycles.use_denoising=True
scene.cycles.max_bounces=10
scene.cycles.diffuse_bounces=5
scene.cycles.glossy_bounces=4
scene.cycles.transmission_bounces=8
scene.cycles.transparent_max_bounces=8
scene.cycles.sample_clamp_indirect=5
scene.render.resolution_x=1280 if PREVIEW else 3840
scene.render.resolution_y=720 if PREVIEW else 2160
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGB'
scene.render.image_settings.color_depth='8' if PREVIEW else '16'
scene.render.film_transparent=False
scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast'
scene.view_settings.exposure=.8
scene.render.filepath=str(OUT/("classroom-cover-preview.png" if PREVIEW else "classroom-cover-4k.png"))
scene.render.use_compositing=False

# Pack external textures only into the independent cover .blend.
bpy.ops.file.pack_all()
source=HERE/"JapaneseMusicClassroom-Cover.blend"
if not PREVIEW:
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
print("COVER_RENDER_BEGIN",json.dumps(dict(preview=PREVIEW,output=scene.render.filepath,
    width=scene.render.resolution_x,height=scene.render.resolution_y)))
bpy.ops.render.render(write_still=True)
print("COVER_RENDER_COMPLETE",scene.render.filepath)
if not PREVIEW:
    scene.render.resolution_x=1920
    scene.render.resolution_y=1080
    scene.render.image_settings.file_format='JPEG'
    scene.render.image_settings.color_depth='8'
    scene.render.image_settings.quality=96
    scene.cycles.samples=128
    scene.render.filepath=str(OUT/'classroom-cover-1080p.jpg')
    bpy.ops.render.render(write_still=True)
    print('COVER_UPLOAD_COMPLETE',scene.render.filepath)
