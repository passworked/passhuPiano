"""Export the approved external Blender study as environment-only Unity geometry.
Run: blender -b ClockworkMaintenanceStreet-v1.blend -P export_unity.py
No lights/cameras, presentation instruments, haze or runtime scripts are exported.
"""
import bpy, bmesh, math, json, re, random
from pathlib import Path
from collections import defaultdict
from mathutils import Matrix, Vector
import numpy as np

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
OUT=ROOT/'Assets/Scenes/ClockworkMaintenanceStreet'
for folder in [HERE,OUT/'Models',OUT/'Textures']:folder.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
random.seed(404)
bpy.context.preferences.filepaths.save_version=0
# The loaded editable source is preserved in RecoveryBackups by the caller.

# The concept gallery was continuous above its stairs. Cut a real stairwell
# in the import source so players have head clearance all the way to the landing.
gallery=bpy.data.objects.get('West gallery floor')
if gallery:
    for name,center,width,depth in [
        ('West gallery south deck',(-10.65,-8.475,4),4,4.65),
        ('West gallery north deck',(-10.65,7.45,4),4,11.5),
        ('West gallery stairwell side deck',(-11.65,-2.225,4),2,7.85)]:
        part=gallery.copy();part.data=gallery.data;part.name=name
        gallery.users_collection[0].objects.link(part);part.location=center
        part.scale=(width/4,depth/24,1)
    bpy.data.objects.remove(gallery,do_unlink=True)
for ob in list(scene.objects):
    if ob.name.startswith('Gallery oak board') and -6.15<ob.location.y<1.7:
        ob.location.x=-11.65;ob.scale.x=2/3.96

def rail_box(name,loc,size,matname):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    ob=bpy.context.object;ob.name=name;ob.scale=size
    for col in list(ob.users_collection):col.objects.unlink(ob)
    bpy.data.collections['03 | Gallery and service stair'].objects.link(ob)
    ob.data.materials.append(bpy.data.materials[matname])
for ob in list(scene.objects):
    if ob.name.startswith(('Stairwell protective','Stairwell brass','Stairwell iron','Stairwell south')):
        bpy.data.objects.remove(ob,do_unlink=True)
for x in [-10.65,-8.62]:
    for y in [-6,-4.5,-3,-1.5,0,1.5]:
        rail_box('Stairwell protective iron post',(x,y,4.68),(.04,.04,1.02),'Aged petrol green cast iron')
    rail_box('Stairwell brass handrail',(x,-2.225,5.19),(.045,7.85,.045),'Polished brass wear')
    rail_box('Stairwell iron midrail',(x,-2.225,4.65),(.025,7.85,.025),'Aged petrol green cast iron')
for x in [-10.65,-9.65,-8.62]:
    rail_box('Stairwell south end post',(x,-6.15,4.68),(.04,.04,1.02),'Aged petrol green cast iron')
rail_box('Stairwell south brass handrail',(-9.635,-6.15,5.19),(2.03,.045,.045),'Polished brass wear')
rail_box('Stairwell south iron midrail',(-9.635,-6.15,4.65),(2.03,.025,.025),'Aged petrol green cast iron')

def unit(p):return [float(p[0]),float(p[2]),float(p[1])]
def gamma(x):return 12.92*x if x<=.0031308 else 1.055*x**(1/2.4)-.055
def texture(name,kind):
    n=512;y,x=np.mgrid[0:n,0:n];rng=np.random.default_rng(404)
    if kind=='wood':
        noise=np.sin(y*.31+np.sin(x*.024)*2)*.06+np.sin(y*1.37+np.sin(x*.05))*.035
        value=np.clip(.88+noise+rng.random((n,n))*.035,.60,1)
    elif kind=='stone':value=np.clip(.90+rng.standard_normal((n,n))*.027+np.sin(x*.042)*np.sin(y*.037)*.04,.72,1)
    else:value=np.clip(.91+rng.standard_normal((n,n))*.02+np.sin(x*.09)*np.sin(y*.071)*.035,.72,1)
    rgba=np.ones((n,n,4),np.float32);rgba[:,:,:3]=value[:,:,None]
    im=bpy.data.images.new(name,width=n,height=n)
    im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(OUT/'Textures'/name);im.file_format='PNG';im.save()
    return name
textures={'wood':texture('Clockwork_WoodGrain.png','wood'),'stone':texture('Clockwork_StoneGrain.png','stone'),'metal':texture('Clockwork_MetalWear.png','metal')}
exec(compile((HERE/'upgrade_mechanism.py').read_text(encoding='utf8'),str(HERE/'upgrade_mechanism.py'),'exec'))
import runpy
runpy.run_path(str(HERE/'refresh_decorative_lettering.py'))['apply_lettering']()
runpy.run_path(str(HERE/'refine_workshop_front.py'))['apply_details']()
runpy.run_path(str(HERE/'refine_gallery_structure.py'))['apply_structure']()
runpy.run_path(str(HERE/'repair_west_lane_cabinets.py'))['apply_alignment']()
runpy.run_path(str(HERE/'arrange_recital_space.py'))['apply_layout']()

selected=[]
for ob in scene.objects:
    cols=[c.name for c in ob.users_collection]
    if any(c.startswith(('09 |','10 |','13 |')) for c in cols):continue
    if ob.type not in {'MESH','CURVE','FONT'} or ob.hide_render:continue
    selected.append(ob)

materials={};mat_index={}
for ob in selected:
    for slot in ob.material_slots:
        m=slot.material
        if not m or m.name in mat_index:continue
        identifier='CW_'+re.sub('[^A-Za-z0-9]+','_',m.name).strip('_')
        color=list(m.diffuse_color[:3]);rough=.55;metal=0
        if m.use_nodes:
            p=m.node_tree.nodes.get('Principled BSDF')
            if p:
                color=list(p.inputs['Base Color'].default_value[:3]);rough=p.inputs['Roughness'].default_value;metal=p.inputs['Metallic'].default_value
                ramps=[n for n in m.node_tree.nodes if n.type=='VALTORGB']
                if ramps:
                    a,b=ramps[0].color_ramp.elements[0].color,ramps[0].color_ramp.elements[-1].color
                    color=[(a[i]+b[i])*.5 for i in range(3)]
        kind='wood' if any(s in m.name.lower() for s in ['oak','walnut']) else 'stone' if any(s in m.name.lower() for s in ['stone','plaster','mortar']) else 'metal' if metal>.4 else None
        if any(s in m.name for s in ['Lantern opal','Clock face']):color=[.79,.71,.53]
        materials[identifier]={'name':identifier,'source':m.name,'color':[gamma(max(0,min(1,c))) for c in color],
            'smoothness':max(.05,min(.65,1-rough)),'metallic':min(.55,metal),'texture':textures.get(kind,''),'emission':0}
        mat_index[m.name]=identifier

# The hand-authored primitive boxes in the render study use inward winding.
# Blender renders both sides; Unity culls backfaces. Recalculate outward normals
# on each unique source mesh before evaluating bevels or saving the Unity source.
for data in {ob.data for ob in selected if ob.type=='MESH'}:
    bm=bmesh.new();bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(data);bm.free();data.update()
# Save the editable, walkable source before flattening export batches.
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ClockworkMaintenanceStreet.blend'))
# Evaluate modeled bevels, curves and lettering; batch by collection/material.
# Rotate the Blender coordinates before FBX conversion so Unity uses (x,z,y).
turn=Matrix.Diagonal((-1,-1,1,1));dg=bpy.context.evaluated_depsgraph_get()
batches=defaultdict(lambda:{'v':[],'f':[],'uv':[],'c':[],'n':[]})
for ob in selected:
    ev=ob.evaluated_get(dg)
    try:me=ev.to_mesh()
    except Exception:continue
    if me is None:continue
    me.calc_loop_triangles()
    motion=ob.get('cw_motion_group')
    zone='Moving_'+motion if motion else 'DialGlass' if ob.get('cw_dial_glass') else next((c.name.split(' |')[0] for c in ob.users_collection),'00')
    world=ob.matrix_world
    if motion:
        item=cw_motion[motion];p=item['pivot'];pivot=Vector((p[0],p[2],p[1]));phase=math.radians(item['phase'])
        local=Matrix.Rotation(phase,4,'Y')@Matrix.Translation(-pivot)@world
        matrices=turn@local
    else:matrices=turn@world
    normal_matrix=matrices.to_3x3().inverted().transposed()
    for tri in me.loop_triangles:
        slot=ob.material_slots[min(tri.material_index,len(ob.material_slots)-1)] if len(ob.material_slots) else None
        if not slot or not slot.material:continue
        material=mat_index[slot.material.name];batch=batches[(zone,material)]
        base=len(batch['v']);face=[]
        normal=(world.to_3x3()@tri.normal).normalized();axis=max(range(3),key=lambda a:abs(normal[a]))
        tone=.92+random.random()*.08
        for vi,li in zip(tri.vertices,tri.loops):
            v=world@me.vertices[vi].co
            uv=(v.x*.5,v.y*.5) if axis==2 else (v.x*.5,v.z*.5) if axis==1 else (v.y*.5,v.z*.5)
            batch['v'].append(tuple(matrices@me.vertices[vi].co));batch['uv'].append(uv);batch['c'].append((tone,tone,tone,1))
            batch['n'].append(tuple((normal_matrix@me.corner_normals[li].vector).normalized()));face.append(base+len(face))
        batch['f'].append(tuple(face))
    ev.to_mesh_clear()

# Remove only temporary scene objects in this exporter process, not the saved source.
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
exports=[]
export_materials={name:bpy.data.materials.new(name) for name in materials}
for name,mat in export_materials.items():mat.diffuse_color=(*materials[name]['color'],1)
moving_roots={}
for name,item in cw_motion.items():
    ob=bpy.data.objects.new('CW_Moving_'+name,None);p=item['pivot'];ob.location=(-p[0],-p[2],p[1])
    scene.collection.objects.link(ob);moving_roots[name]=ob
for (zone,material),batch in batches.items():
    if not batch['f']:continue
    name='Clockwork_'+zone+'__'+material
    me=bpy.data.meshes.new(name);me.from_pydata(batch['v'],[],batch['f']);me.update()
    for poly in me.polygons:poly.use_smooth=True
    me.normals_split_custom_set_from_vertices(batch['n'])
    me.materials.append(export_materials[material])
    uv=me.uv_layers.new(name='UVMap');col=me.color_attributes.new(name='Col',type='BYTE_COLOR',domain='CORNER')
    for loop in me.loops:
        uv.data[loop.index].uv=batch['uv'][loop.vertex_index];col.data[loop.index].color=batch['c'][loop.vertex_index]
    obj=bpy.data.objects.new(name,me);scene.collection.objects.link(obj)
    if zone.startswith('Moving_'):obj.parent=moving_roots[zone[len('Moving_'):]]
    exports.append(obj)
bpy.ops.object.select_all(action='DESELECT')
for ob in exports:ob.select_set(True)
for ob in moving_roots.values():ob.select_set(True)
bpy.context.view_layer.objects.active=exports[0]
bpy.ops.export_scene.fbx(filepath=str(OUT/'Models/ClockworkMaintenanceStreet.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
    use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',path_mode='STRIP',embed_textures=False,
    mesh_smooth_type='FACE',use_triangles=True,colors_type='SRGB')

colliders=[]
def collider(name,center,size):colliders.append({'name':name,'center':center,'size':size})
collider('Ground',(0,-.14,.25),(26,.25,26.2))
collider('West wall',(-13.1,8,.5),(.35,17,26.5))
collider('East wall',(13.1,8,.5),(.35,17,26.5))
collider('Rear wall',(0,9,13.1),(26.3,19,.3))
collider('Front wall',(0,9,-12.96),(26.3,19,.35))
for sign in [-1,1]:
    collider('Pitched roof '+str(sign),(sign*6.4,16.4,.1),(13.1,.20,26.7))
    colliders[-1]['rotation']=[0,0,-sign*16]
collider('Ridge closure',(0,18.17,.1),(.6,.35,26.7))
collider('West gallery south deck',(-10.65,4.02,-8.475),(4,.25,4.65))
collider('West gallery north deck',(-10.65,4.02,7.45),(4,.25,11.5))
collider('West stairwell side deck',(-11.65,4.02,-2.225),(2,.25,7.85))
collider('Rear gallery',(-5.5,4.02,10.8),(10.4,.25,3.5))
collider('West gallery north rail',(-8.62,4.7,7.25),(.1,1.1,9.5))
collider('West gallery south rail',(-8.62,4.7,-8.35),(.1,1.1,3.3))
collider('Stairwell west protective rail',(-10.65,4.7,-2.225),(.1,1.1,7.85))
collider('Stairwell east protective rail',(-8.62,4.7,-2.225),(.1,1.1,7.85))
collider('Stairwell south protective rail',(-9.635,4.7,-6.15),(2.03,1.1,.1))
collider('Rear gallery rail',(-4.5,4.7,9.04),(8.2,1.1,.10))
collider('Clock mechanism safety rail',(3.75,.6,6.45),(16.5,1.2,.1))
for i in range(23):collider('Stair tread '+str(i),(-9.78,(i+1)*.18-.065,-5.7+i*.33),(1.62,.13,.35))
for x,z,w,d,h in [(-5.7,5.15,3.7,2.8,3.1),(-10.55,9.2,2.8,2,2.8),(-6.2,-7.15,3.6,1,1.15),(10.65,-.6,3.2,1,1.2)]:
    collider('Workshop furniture', (x,h/2,z),(w,h,d))
report={'source':'ArtSource/ClockworkMaintenanceStreet/ClockworkMaintenanceStreet.blend','unityAxes':'x, up=z, forward=y from Blender',
    'materials':list(materials.values()),'colliders':colliders,'meshes':len(exports),'triangles':sum(len(b['f']) for b in batches.values()),
    'excluded':'Blender lights, cameras, atmosphere and presentation instrument models; existing Unity player is reused.',
    'lighting':'Exactly one moving realtime Point Light through the dial aperture; Unity ceiling-emission indirect lightmaps/probes configured by ClockworkIndirectLightingBuilder, with no added Light components.',
    'dynamicGroups':list(cw_motion.keys()),'normals':'Recalculated outward, weighted bevels, preserved per-corner normals.'}
(OUT/'clockwork-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('CLOCKWORK_UNITY_EXPORT',json.dumps({'meshes':report['meshes'],'triangles':report['triangles'],'materials':len(materials),'colliders':len(colliders)}),flush=True)
