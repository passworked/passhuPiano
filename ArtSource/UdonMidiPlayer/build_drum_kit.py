"""Real-metre drum kit with separately bound heads, cymbals, pedals and strike anchors."""
import bpy, json, math, shutil
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
EXPORT=ROOT/'Exports'; PREVIEW=ROOT/'Previews'
for folder in (EXPORT,PREVIEW):folder.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene;scene.unit_settings.system='METRIC'
model=bpy.data.collections.new('DrumKit_Model');scene.collection.children.link(model)
preview=bpy.data.collections.new('Preview_Only');scene.collection.children.link(preview)
def vec(p):return Vector((p[0],p[2],p[1]))
def move(o,col=model):
    for c in list(o.users_collection):c.objects.unlink(o)
    col.objects.link(o)
def mat(name,color,metal=0,rough=.4):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    return m
shell=mat('DrumKit_BurgundyLacquer',(.23,.025,.045),.42,.24)
chrome=mat('DrumKit_BrushedChrome',(.46,.49,.53),.86,.24)
brass=mat('DrumKit_LathedBrass',(.64,.42,.12),.83,.32)
head=mat('DrumKit_CoatedHeads',(.76,.75,.69),0,.72)
black=mat('DrumKit_BlackRubber',(.014,.019,.025),.08,.68)
wood=mat('DrumKit_MapleSticks',(.57,.34,.13),0,.52)
def empty(name,p):
    o=bpy.data.objects.new(name,None);model.objects.link(o);o.location=vec(p);return o
def parent(o,p):
    old=o.matrix_world.copy();o.parent=p;o.matrix_world=old;return o
def cylinder(name,p,r,depth,material,axis=(0,1,0),vertices=40):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=vec(p))
    o=bpy.context.object;o.name=name;o.rotation_euler=vec(axis).to_track_quat('Z','Y').to_euler()
    o.data.materials.append(material);move(o)
    for face in o.data.polygons:face.use_smooth=len(face.vertices)==4
    bevel=o.modifiers.new('Machined edge','BEVEL');bevel.width=min(.0025,depth*.17);bevel.segments=2
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=bevel.name)
    return o
def bar(name,a,b,r,material):
    av=Vector(a);bv=Vector(b);return cylinder(name,(av+bv)/2,r,(bv-av).length,material,bv-av,12)
def torus(name,p,r,t,material,axis=(0,1,0),segments=40):
    bpy.ops.mesh.primitive_torus_add(major_radius=r,minor_radius=t,major_segments=segments,minor_segments=6,location=vec(p))
    o=bpy.context.object;o.name=name;o.rotation_euler=vec(axis).to_track_quat('Z','Y').to_euler();o.data.materials.append(material);move(o)
    for f in o.data.polygons:f.use_smooth=True
    return o
def cube(name,p,size,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=vec(p));o=bpy.context.object;o.name=name;o.dimensions=(size[0],size[2],size[1]);
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(material);move(o);return o
def stand(x,z,height):
    cylinder('ChromeStand',(x,height*.5,z),.012,height,chrome,vertices=12)
    for angle in (0,2.094,4.189):
        end=(x+.23*math.cos(angle),.035,z+.23*math.sin(angle))
        bar('TripodLeg',(x,.30,z),end,.010,chrome);cylinder('RubberFoot',end,.021,.033,black,vertices=12)
    cylinder('StandCollar',(x,height*.55,z),.022,.048,chrome,vertices=16)

anchors=[];moving=[]
def drum(name,lane,p,r,d,kick=False):
    axis=(0,0,1) if kick else (0,1,0)
    cylinder(name+'_Shell',p,r,d,shell,axis)
    for sign in (-1,1):
        q=Vector(p)+Vector(axis)*d*.5*sign
        torus(name+'_Hoop',q,r+.004,.009,chrome,axis)
        for i in range(8):
            a=math.tau*i/8
            offset=Vector((math.cos(a)*r,math.sin(a)*r,0)) if kick else Vector((math.cos(a)*r,0,math.sin(a)*r))
            cylinder(name+'_TensionLug',Vector(p)+offset+Vector(axis)*sign*d*.26,.012,.046,chrome,axis,12)
    top=Vector(p)+Vector(axis)*(-d*.5-.003 if kick else d*.5+.003)
    pivot=empty('HeadPivot_'+str(lane),top)
    parent(cylinder('Head_'+str(lane),top,r-.011,.004,black if kick else head,axis),pivot)
    if kick:
        # Front port and minimal hardware details; rear pedal and beater remain visible from player side.
        parent(torus('KickFrontPort',top+Vector((.12,-.10,-.003)),.048,.005,chrome,axis),pivot)
        for sign in (-1,1):bar('KickSpur',(p[0]+sign*.27,.23,p[2]-.12),(p[0]+sign*.39,.035,p[2]-.30),.012,chrome)
        hit=(p[0],p[1]+r*.6,p[2]-.23)
    else:hit=top
    empty('Hit_'+str(lane),hit);anchors.append({'lane':lane,'name':name,'hit':list(hit)})
    moving.append(pivot.name)
    return pivot
drum('Kick',0,(0,.34,0),.32,.40,True)
drum('Snare',1,(-.56,.72,.53),.185,.13)
drum('FloorTom',3,(.65,.68,.53),.235,.32)
drum('MidTom',4,(.18,.98,.29),.18,.22)
drum('HighTom',5,(-.26,1.02,.26),.155,.20)
stand(-.56,.53,.64)
for angle in (0,2.094,4.189):
    x=.65+.22*math.cos(angle);z=.53+.22*math.sin(angle)
    bar('FloorTomLeg',(x,.66,z),(x,.035,z),.011,chrome)
bar('TomMount',(0,.55,.12),(0,.95,.20),.017,chrome)
bar('TomCrossbar',(-.26,.91,.26),(.18,.87,.29),.013,chrome)

def cymbal(name,lane,p,r):
    pivot=empty('CymbalPivot_'+str(lane),p);verts=[];faces=[]
    rings=[(0,.035),(.08*r,.035),(.22*r,.026),(.32*r,.006),(.65*r,-.008),(r,-.022)]
    for rr,yy in rings:
        for i in range(48):
            a=math.tau*i/48;verts.append((rr*math.cos(a),rr*math.sin(a),yy))
    for ring in range(len(rings)-1):
        for i in range(48):
            j=(i+1)%48;a=ring*48+i;b=ring*48+j;faces.append((a,a+48,b+48,b))
    mesh=bpy.data.meshes.new(name+'Mesh');mesh.from_pydata(verts,[],faces);mesh.materials.append(brass);mesh.update()
    o=bpy.data.objects.new(name,mesh);model.objects.link(o);o.parent=pivot;o.location=(0,0,0)
    solid=o.modifiers.new('Physical cymbal thickness','SOLIDIFY');solid.thickness=.0014
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=solid.name)
    for f in mesh.polygons:f.use_smooth=True
    for factor in (.50,.68,.85,.97):parent(torus('LatheGroove',Vector(p)+Vector((0,-.015,0)),r*factor,.00065,brass,segments=48),pivot)
    parent(cylinder('FeltWasher',Vector(p)+Vector((0,.040,0)),.024,.010,black,vertices=16),pivot)
    parent(cylinder('WingNut',Vector(p)+Vector((0,.053,0)),.009,.018,chrome,vertices=12),pivot)
    stand(p[0],p[2],p[1]+.02)
    empty('Hit_'+str(lane),Vector(p)+Vector((r*.40,.015,-r*.25)))
    anchors.append({'lane':lane,'name':name,'hit':list(Vector(p)+Vector((r*.4,.015,-r*.25)))})
    moving.append(pivot.name);return pivot
cymbal('Crash',6,(-.75,1.40,-.20),.285)
cymbal('Ride',7,(.82,1.29,.02),.305)
hat=cymbal('HiHatUpper',2,(-.90,.98,.59),.18)
cylinder('HiHatLower',(-.90,.957,.59),.18,.004,brass)
empty('HiHatOpenPivot',(-.90,.98,.59))
# The upper pivot can move vertically without altering the lower plate or stand.
for side,x in (('Kick',0),('HiHat',-.90)):
    z=.39 if side=='Kick' else .88
    pivot=empty(side+'PedalPivot',(x,.035,z+.10))
    parent(cube(side+'Footboard',(x,.065,z),(.085,.018,.20),chrome),pivot)
    cube(side+'PedalBase',(x,.020,z),(.105,.025,.25),black)
beater=empty('KickBeaterPivot',(0,.09,.28))
parent(bar('BeaterShaft',(0,.09,.28),(0,.31,.22),.005,chrome),beater)
parent(cylinder('BeaterHead',(0,.31,.22),.027,.045,head,axis=(0,0,1),vertices=20),beater)
stand(0,1.05,.50);cylinder('DrummerThrone',(0,.54,1.05),.20,.075,black)
for lane,p in ((1,(-.56,.79,.53)),(3,(.65,.84,.53)),(4,(.18,1.10,.29)),(5,(-.26,1.13,.26))):
    pivot=empty('StickPivot_'+str(lane),Vector(p)+Vector((.02,.26,.14)))
    parent(bar('DrumStick_'+str(lane),Vector(p)+Vector((-.03,.015,-.07)),Vector(p)+Vector((.02,.26,.14)),.006,wood),pivot)
# Compact percussion rack for GM auxiliary notes, with its own impact target.
stand(1.15,.72,.83)
cylinder('TambourineShell',(1.15,.88,.72),.12,.045,shell)
for i in range(8):
    a=math.tau*i/8;cylinder('TambourineJingle',(1.15+.115*math.cos(a),.88,.72+.115*math.sin(a)),.022,.004,chrome,vertices=16)
aux=empty('HeadPivot_8',(1.15,.91,.72));parent(cylinder('AuxHead',(1.15,.91,.72),.105,.003,head),aux)
empty('Hit_8',(1.15,.92,.72));anchors.append({'lane':8,'name':'Auxiliary','hit':[1.15,.92,.72]})

# Consolidate only static meshes, preserving all animation pivots and independent parts.
for material in (shell,chrome,brass,head,black,wood):
    objects=[o for o in model.objects if o.type=='MESH' and o.parent is None and o.data.materials and o.data.materials[0]==material]
    if not objects:continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();objects[0].name='Hardware_'+material.name

bpy.ops.object.select_all(action='DESELECT')
for o in model.objects:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'VirtuosityDrumKit.fbx'),use_selection=True,object_types={'MESH','EMPTY'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True)
bpy.ops.export_scene.gltf(filepath=str(EXPORT/'VirtuosityDrumKit.glb'),use_selection=True,export_format='GLB')
assets=ROOT.parents[1]/'Assets/UdonMidiPlayer/Models';assets.mkdir(parents=True,exist_ok=True)
shutil.copyfile(EXPORT/'VirtuosityDrumKit.fbx',assets/'VirtuosityDrumKit.fbx')

floor=mat('DrumPreviewFloor',(.035,.044,.055),0,.7)
o=cube('PreviewFloor',(0,-.025,0),(200,.04,200),floor);move(o,preview)
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3.3,-4.2,2.8));cam=bpy.context.object;move(cam,preview);aim(cam,(0,.2,.73));cam.data.type='ORTHO';cam.data.ortho_scale=3.55;scene.camera=cam
for p,power,size in (((0,-2.5,4),1000,4),((2,2,3),900,3),((-3,.5,2.8),750,3)):
    bpy.ops.object.light_add(type='AREA',location=p);o=bpy.context.object;move(o,preview);o.data.energy=power;o.data.shape='DISK';o.data.size=size;aim(o,(0,.2,.6))
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.world.color=(.12,.12,.12)
scene.render.filepath=str(PREVIEW/'VirtuosityDrumKit_Hero.png')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'VirtuosityDrumKit.blend'))
bpy.ops.render.render(write_still=True)
(ROOT/'drum_kit_manifest.json').write_text(json.dumps({'generator':'Blender','metres':True,'anchors':anchors,'animatedPivots':moving,
    'objects':len(model.objects),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in model.objects if o.type=='MESH')},indent=2))
