"""Five-octave marimba, independently addressable bars, FBX/GLB and source."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
EXPORT = ROOT/'Exports'
PREVIEW = ROOT/'Previews'
for folder in (EXPORT,PREVIEW): folder.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
model=bpy.data.collections.new('Marimba_Model'); scene.collection.children.link(model)
preview=bpy.data.collections.new('Preview_Only'); scene.collection.children.link(preview)

def move(obj,col):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    col.objects.link(obj)

def material(name,color,metal=0,rough=.4):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m

rose=material('Marimba_Rosewood',(.23,.065,.025),0,.32)
dark=material('Marimba_AccidentalWood',(.13,.032,.014),0,.32)
gold=material('Marimba_ResonatorBrass',(.52,.32,.09),.76,.27)
frame=material('Marimba_GraphiteFrame',(.032,.043,.052),.65,.32)
rubber=material('Marimba_Rubber',(.012,.016,.02),0,.65)
cord=material('Marimba_Cord',(.65,.47,.20),0,.8)

def cube(name,pos,size,mat,bevel=.005):
    # Coordinates supplied as Unity-like x, height, rear distance.
    bpy.ops.mesh.primitive_cube_add(size=1,location=(pos[0],pos[2],pos[1]))
    o=bpy.context.object; o.name=name; o.dimensions=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat); move(o,model)
    if bevel:
        mod=o.modifiers.new('Soft machined edges','BEVEL'); mod.width=bevel; mod.segments=2
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

bars=[]; natural=0; spacing=.060; count=36
for pitch in range(36,97):
    black=pitch%12 in (1,3,6,8,10)
    x=((natural-.5) if black else natural)*spacing-(count-1)*spacing/2
    if not black: natural+=1
    length=.67*2**(-(pitch-36)/72)
    z=.34 if black else 0
    y=.99 if black else .95
    o=cube(f'Key_{pitch:03d}',(x,y,z),(.050,.022,length),dark if black else rose,.003)
    bars.append({'pitch':pitch,'x':x,'y':y,'z':z,'length':length})
    # Brass tubes are visibly open at the top; 12 sides, low polygon count.
    tube_len=.70*2**(-(pitch-36)/27)+.035
    radius=.021 if not black else .019
    top=y-.035
    verts=[]; faces=[]
    for h,r in ((top-tube_len,radius),(top,radius),(top,radius-.002),(top-tube_len+.005,radius-.002)):
        for i in range(12):
            a=2*math.pi*i/12; verts.append((x+r*math.cos(a),z+r*math.sin(a),h))
    for ring in range(3):
        for i in range(12):
            j=(i+1)%12; faces.append((ring*12+i,ring*12+j,(ring+1)*12+j,(ring+1)*12+i))
    mesh=bpy.data.meshes.new(f'TubeMesh_{pitch}'); mesh.from_pydata(verts,[],faces); mesh.update()
    tube=bpy.data.objects.new(f'Resonator_{pitch:03d}',mesh); model.objects.link(tube); mesh.materials.append(gold)
    for p in mesh.polygons: p.use_smooth=True

for z in (-.22,.22,.52): cube('CrossRail',(0,.84,z),(2.25,.045,.035),frame)
for x in (-1.14,1.14):
    cube('RosewoodEnd',(x,.90,.17),(.075,.15,.98),rose,.012)
    for z in (-.18,.50):
        cube('AdjustableLeg',(x,.45,z),(.045,.80,.045),frame)
        cube('Caster',(x,.055,z),(.075,.065,.075),rubber,.016)
    cube('FootBrace',(x,.13,.16),(.055,.045,.86),frame)
cube('LowerBrace',(0,.22,.48),(2.26,.035,.035),frame)
for z in (-.17,.17): cube('SuspensionCord',(0,.936,z),(2.16,.003,.003),cord,.001)
for z in (.24,.43): cube('UpperSuspensionCord',(0,.976,z),(2.10,.003,.003),cord,.001)

# Combine static hardware by material; bars stay separate for MIDI feedback.
for mat in (rose,gold,frame,rubber,cord):
    static=[o for o in model.objects if o.type=='MESH' and not o.name.startswith('Key_')]
    objects=[o for o in static if o.data.materials and o.data.materials[0]==mat]
    if not objects: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]; bpy.ops.object.join(); objects[0].name='Hardware_'+mat.name

bpy.ops.object.select_all(action='DESELECT')
for o in model.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'ConcertMarimba.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
bpy.ops.export_scene.gltf(filepath=str(EXPORT/'ConcertMarimba.glb'),use_selection=True,export_format='GLB')

floor=material('PreviewFloor',(.035,.045,.06),0,.7)
o=cube('PreviewFloor',(0,-.035,0),(200,.04,200),floor,0); move(o,preview)
def aim(o,point):o.rotation_euler=(Vector(point)-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3.4,-4.2,3.0)); cam=bpy.context.object; move(cam,preview); aim(cam,(0,.15,.60)); cam.data.type='ORTHO'; cam.data.ortho_scale=3.7; scene.camera=cam
for pos,power,size in (((0,-2,4),1100,5),((2,2,3),800,3),((-3,1,2),650,3)):
    bpy.ops.object.light_add(type='AREA',location=pos); o=bpy.context.object; move(o,preview); o.data.energy=power; o.data.shape='DISK'; o.data.size=size; aim(o,(0,0,.6))
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
scene.render.resolution_x=1500; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
scene.world.color=(.12,.12,.12)
scene.render.filepath=str(PREVIEW/'ConcertMarimba_Hero.png')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ConcertMarimba.blend'))
bpy.ops.render.render(write_still=True)
(ROOT/'marimba_manifest.json').write_text(json.dumps({'physicalRange':[36,96],'bars':bars,'objects':len(model.objects),'triangles':sum(len(o.data.polygons) for o in model.objects if o.type=='MESH')},indent=2))
