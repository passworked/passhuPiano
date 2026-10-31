"""Build an editable, real-metre white grand piano with Blender 5.2.

Run: blender --background --python build_white_grand.py
This source only writes its .blend, Exports, Previews, and manifest beside itself.
Blender axes: +X keyboard right, -Y player/front, +Z up. No Unity integration.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
EXPORT = HERE / 'Exports'
PREVIEW = HERE / 'Previews'
EXPORT.mkdir(parents=True, exist_ok=True)
PREVIEW.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for mat_block in list(bpy.data.materials):
    bpy.data.materials.remove(mat_block)
scene = bpy.context.scene
bpy.context.preferences.filepaths.save_version = 0
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.render.engine = 'CYCLES'
scene.cycles.samples = 80
scene.cycles.use_denoising = True
scene.render.resolution_x = 1800
scene.render.resolution_y = 1250
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
scene.view_settings.look = 'AgX - Medium High Contrast'
scene.view_settings.exposure = -.6
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.world.color = (.16, .16, .16)
world = scene.world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.47, .52, .6, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .35

model = bpy.data.collections.new('WhiteGrandPiano_Export')
scene.collection.children.link(model)
studio = bpy.data.collections.new('Preview_Only')
scene.collection.children.link(studio)

def move_to(obj, collection):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)

def material(name, color, metal=0., rough=.3, coat=0.):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    p.inputs['Coat Weight'].default_value = coat
    p.inputs['Coat Roughness'].default_value = .13
    return m

WHITE = material('Porcelain_White_Polished_Lacquer', (.89, .89, .855), rough=.18, coat=.38)
IVORY = material('Keys_Warm_Ivory', (.91, .895, .84), rough=.23, coat=.18)
EBONY = material('Keys_Ebony', (.011, .013, .016), rough=.23, coat=.12)
GOLD = material('Hardware_Warm_Brass', (.64, .39, .12), metal=.82, rough=.24, coat=.12)
PLATE = material('Cast_Plate_Champagne_Gold', (.57, .34, .105), metal=.58, rough=.32)
STEEL = material('Treble_Strings_Steel', (.54, .57, .60), metal=.9, rough=.27)
COPPER = material('Bass_Strings_Copper', (.43, .20, .08), metal=.85, rough=.32)
WOOD = material('Soundboard_Spruce', (.48, .29, .12), rough=.48)
BRIDGE = material('Bridge_Maple', (.27, .115, .038), rough=.4)
GRAIN = material('Soundboard_Board_Seams', (.30, .17, .075), rough=.65)
FELT = material('Felt_Burgundy', (.17, .018, .027), rough=.95)
SHADOW = material('Keybed_Shadow', (.022, .019, .018), rough=.7)
RUBBER = material('Caster_Treads', (.035, .03, .022), rough=.56)
FLOOR = material('Studio_Warm_Grey', (.23, .25, .275), rough=.65)

# Spruce pore/longitudinal grain remains procedural and editable in the .blend.
p = WOOD.node_tree.nodes.get('Principled BSDF')
tex = WOOD.node_tree.nodes.new('ShaderNodeTexNoise')
tex.inputs['Scale'].default_value = 15
tex.inputs['Detail'].default_value = 2
coord = WOOD.node_tree.nodes.new('ShaderNodeTexCoord')
mapping = WOOD.node_tree.nodes.new('ShaderNodeVectorMath')
mapping.operation = 'MULTIPLY'
mapping.inputs[1].default_value = (8, .23, 5)
WOOD.node_tree.links.new(coord.outputs['Generated'], mapping.inputs[0])
WOOD.node_tree.links.new(mapping.outputs[0], tex.inputs['Vector'])
bump = WOOD.node_tree.nodes.new('ShaderNodeBump')
bump.inputs['Strength'].default_value = .10
bump.inputs['Distance'].default_value = .0005
WOOD.node_tree.links.new(tex.outputs['Fac'], bump.inputs['Height'])
WOOD.node_tree.links.new(bump.outputs['Normal'], p.inputs['Normal'])

def empty(name, parent=None, location=(0, 0, 0)):
    o = bpy.data.objects.new(name, None)
    model.objects.link(o)
    o.parent = parent
    o.location = location
    o.empty_display_size = .06
    return o

root = empty('WhiteGrandPiano')
root['units'] = 'metres'
root['front'] = '-Y'
root['keyboard_right'] = '+X'
root['description'] = 'White lacquer acoustic grand piano, 88 individually editable keys'
cabinet = empty('Cabinet', root)
interior = empty('Acoustic_Interior', root)
keyboard = empty('Keyboard_88_Keys', root)
legs = empty('Legs_And_Casters', root)
lyre = empty('Pedal_Lyre', root)

def finish(o, mat, bevel=0., segments=3, smooth=True):
    o.data.materials.append(mat)
    if bevel:
        mod = o.modifiers.new('Small polished edge radius', 'BEVEL')
        mod.width = bevel
        mod.segments = segments
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data)
    bm.free()
    for poly in o.data.polygons:
        poly.use_smooth = smooth
    if smooth:
        mod = o.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
        mod.weight = 50
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if not o.data.uv_layers:
        o.data.uv_layers.new(name='UVMap')
    uv = o.data.uv_layers.active.data
    for poly in o.data.polygons:
        axes = [a for a in range(3) if a != max(range(3), key=lambda a: abs(poly.normal[a]))]
        for li in poly.loop_indices:
            co = o.data.vertices[o.data.loops[li].vertex_index].co
            uv[li].uv = co[axes[0]], co[axes[1]]
    return o

def mesh_obj(name, verts, faces, mat, parent=None, bevel=0., smooth=True):
    mesh = bpy.data.meshes.new(name + '_Mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    o = bpy.data.objects.new(name, mesh)
    model.objects.link(o)
    o.parent = parent or root
    return finish(o, mat, bevel, smooth=smooth)

def cube(name, loc, size, mat, parent=None, bevel=.003):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.object
    o.name = name
    move_to(o, model)
    o.parent = parent or root
    for v in o.data.vertices:
        v.co.x *= size[0]
        v.co.y *= size[1]
        v.co.z *= size[2]
    return finish(o, mat, bevel)

def cylinder(name, loc, radius, depth, mat, parent=None, vertices=20, direction=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    o = bpy.context.object
    o.name = name
    move_to(o, model)
    o.parent = parent or root
    if direction:
        o.rotation_euler = Vector(direction).to_track_quat('Z', 'Y').to_euler()
    return finish(o, mat, min(radius * .12, .0012), segments=2)

def rod(name, a, b, radius, mat, parent=None, vertices=12):
    vec = Vector(b) - Vector(a)
    return cylinder(name, (Vector(a)+Vector(b))/2, radius, vec.length, mat, parent, vertices, vec)

def prism(name, outline, z0, z1, mat, parent=None, bevel=.003):
    n = len(outline)
    verts = [(x,y,z) for z in (z0,z1) for x,y in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n,2*n))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    return mesh_obj(name, verts, faces, mat, parent, bevel)

def inset(poly, amount):
    out = []
    for i,p in enumerate(poly):
        prev = Vector(poly[i-1]); cur = Vector(p); nex = Vector(poly[(i+1)%len(poly)])
        d0 = (cur-prev).normalized(); d1 = (nex-cur).normalized()
        n0 = Vector((-d0.y,d0.x)); n1 = Vector((-d1.y,d1.x))
        bis = (n0+n1).normalized()
        q = cur + bis * amount / max(.2,bis.dot(n0))
        out.append(tuple(q))
    return out

def ring(name, poly, width, z0, z1, mat, parent=None, bevel=.002):
    inn = inset(poly,width)
    n = len(poly)
    verts = [(x,y,z) for z in (z0,z1) for loop in (poly,inn) for x,y in loop]
    faces = []
    for i in range(n):
        j = (i+1)%n
        faces += [(i,j,j+2*n,i+2*n),(i+n,i+3*n,j+3*n,j+n),
                  (i+2*n,j+2*n,j+3*n,i+3*n),(i+n,j+n,j,i)]
    return mesh_obj(name,verts,faces,mat,parent,bevel)

def cubic(a,b,c,d,steps=20):
    pts=[]
    for i in range(steps):
        t=i/steps; s=1-t
        pts.append((s*s*s*a[0]+3*s*s*t*b[0]+3*s*t*t*c[0]+t*t*t*d[0],
                    s*s*s*a[1]+3*s*s*t*b[1]+3*s*t*t*c[1]+t*t*t*d[1]))
    return pts

# The long straight bass cheek and the concave treble shoulder are a continuous rim.
outline = [(-.76,-.255),(.76,-.255),(.76,-.015)]
outline += cubic((.76,-.015),(.76,.30),(.36,.35),(.355,.85),30)[1:]
outline += cubic((.355,.85),(.35,1.25),(.13,1.585),(-.22,1.59),26)
outline += cubic((-.22,1.59),(-.57,1.60),(-.76,1.40),(-.76,1.06),24)
outline += [(-.76,1.06)]
rim = ring('Bent_White_Outer_Rim',outline,.043,.698,.968,WHITE,cabinet,.005)
rim['construction'] = 'Continuous curved open rim; not a solid box'
prism('Underside_Shell',outline,.681,.703,WHITE,cabinet,.005)
ring('Upper_Rim_Lip',outline,.054,.965,.980,WHITE,cabinet,.003)
ring('Lower_Cabinet_Reveal',inset(outline,.007),.012,.698,.712,WHITE,cabinet,.002)
soundboard = inset(outline,.060)
prism('Spruce_Soundboard',soundboard,.870,.886,WOOD,interior,.002)
ring('Cast_Plate_Outer_Flange',inset(outline,.078),.032,.898,.916,PLATE,interior,.003)

def bounds_y_at_x(poly,x):
    ys=[]
    for i,(a,b) in enumerate(zip(poly,poly[1:]+poly[:1])):
        if (a[0] <= x < b[0]) or (b[0] <= x < a[0]):
            t=(x-a[0])/(b[0]-a[0]); ys.append(a[1]+t*(b[1]-a[1]))
    return sorted(ys)

# Fine board joins are physical lines, retained in geometry-only interchange files.
for i in range(19):
    x=-.65+i*.069
    hits=bounds_y_at_x(soundboard,x)
    if len(hits)>=2:
        a,b=hits[0]+.006,hits[-1]-.006
        cube('Spruce_Join_%02d'%i,(x,(a+b)/2,.887),(.00032,b-a,.00018),GRAIN,interior,0)

cube('Tuning_Block_White_Back', (0,-.16,.930),(1.37,.14,.073),WHITE,interior,.008)
cube('Cast_Plate_Tuning_Beam',(0,-.027,.923),(1.346,.102,.038),PLATE,interior,.008)
cube('Burgundy_String_Felt',(0,.035,.929),(1.30,.016,.006),FELT,interior,.001)
for j,(a,b) in enumerate([
    ((-.62,.06,.922),(-.48,1.37,.922)),
    ((-.17,.06,.922),(-.23,1.43,.922)),
    ((.24,.06,.922),(.10,1.19,.922)),
    ((.60,.06,.922),(.35,.51,.922)),
]):
    rod('Cast_Plate_Longitudinal_Rib_%d'%j,a,b,.014,PLATE,interior,16)

# Discrete ribs and bridge make the open harp legible; no opaque gold slab.
bridge_curve = cubic((-.61,1.28),(-.22,1.32),(.12,.61),(.56,.30),40)
def ribbon(name, points, width, z, thickness, mat, parent):
    left=[]; right=[]
    for i,p in enumerate(points):
        d=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        n=Vector((-d.y,d.x)).normalized()*width/2
        left.append((p[0]+n.x,p[1]+n.y)); right.append((p[0]-n.x,p[1]-n.y))
    return prism(name,left+list(reversed(right)),z,z+thickness,mat,parent,.0015)
ribbon('Curved_Maple_Treble_Bridge',bridge_curve,.029,.886,.026,BRIDGE,interior)
bass_bridge = [(-.63,.78),(-.56,.96),(-.49,1.16),(-.40,1.33)]
ribbon('Bass_Bridge',bass_bridge,.033,.886,.023,BRIDGE,interior)

# Strings are separate editable meshes by register, with 1/2/3 strings per course.
def tube_paths(name, paths, radius, mat, parent, sides=5):
    verts=[]; faces=[]
    for path in paths:
        for k in range(len(path)-1):
            a=Vector(path[k]); b=Vector(path[k+1]); d=(b-a).normalized()
            u=d.cross(Vector((0,0,1))).normalized(); v=d.cross(u).normalized()
            n=len(verts)
            for center in (a,b):
                for j in range(sides):
                    q=center+radius*(math.cos(j*math.tau/sides)*u+math.sin(j*math.tau/sides)*v)
                    verts.append(tuple(q))
            for j in range(sides):
                q=(j+1)%sides; faces.append((n+j,n+q,n+sides+q,n+sides+j))
            faces.extend((tuple(n+j for j in reversed(range(sides))),tuple(n+sides+j for j in range(sides))))
    return mesh_obj(name,verts,faces,mat,parent,0,True)

bass_paths=[]; treble_paths=[]; tuning_pin_paths=[]; bridge_pin_paths=[]
for course in range(88):
    t=course/87
    start_x=-.635+1.272*t
    # Longer bass courses fan left-to-right; high treble terminates in the shoulder.
    end_y=1.36-1.04*(t**.78)
    end_x=-.46+.99*t
    count=1 if course<9 else (2 if course<28 else 3)
    spacing=.0025 if count<3 else .0020
    for s in range(count):
        off=(s-(count-1)/2)*spacing
        a=(start_x+off,.028,.940)
        b=(end_x+off,end_y,.927)
        paths=bass_paths if course<28 else treble_paths
        paths.append([a,b])
        tuning_pin_paths.append([(start_x+off,-.031+(s%2)*.013,.930),(start_x+off,-.031+(s%2)*.013,.955)])
        bridge_pin_paths.append([(end_x+off,end_y,.912),(end_x+off,end_y,.934)])
tube_paths('Bass_Copper_Wound_Strings',bass_paths,.00074,COPPER,interior,6)
tube_paths('Treble_Steel_Strings',treble_paths,.00038,STEEL,interior,5)
tube_paths('Tuning_Pins',tuning_pin_paths,.0015,STEEL,interior,8)
tube_paths('Bridge_Pins',bridge_pin_paths,.0007,STEEL,interior,6)
for j,(x,y) in enumerate([(-.64,.25),(-.63,1.13),(-.30,1.45),(.18,1.08),(.49,.28)]):
    cylinder('Plate_Fastener_%02d'%j,(x,y,.924),.008,.009,GOLD,interior,16)
    cube('Fastener_Slot_%02d'%j,(x,y,.929),(.008,.0012,.0005),SHADOW,interior,.0002)

# Keyboard case and fallboard: standard 23.5 mm white-key spacing.
cube('Keyboard_Keybed',(0,-.445,.708),(1.50,.396,.045),WHITE,cabinet,.009)
cube('Keybed_Recess',(0,-.459,.743),(1.259,.303,.009),SHADOW,cabinet,.001)
cube('Front_Keyslip',(0,-.624,.737),(1.43,.034,.055),WHITE,cabinet,.006)
for side in (-1,1):
    cube('Keyboard_Cheek_' + ('Bass' if side<0 else 'Treble'),(side*.697,-.453,.802),(.126,.364,.150),WHITE,cabinet,.010)
    cube('Cheek_Front_Shadow_' + str(side),(side*.697,-.640,.720),(.098,.002,.008),SHADOW,cabinet,.001)
fallboard=cube('Raised_Fallboard',(0,-.384,.841),(1.274,.055,.157),WHITE,cabinet,.013)
fallboard.rotation_euler.x=math.radians(-7)
cube('Keyboard_Rear_Deck',(0,-.312,.890),(1.359,.151,.045),WHITE,cabinet,.007)
cube('Key_Back_Red_Felt',(0,-.418,.771),(1.236,.008,.009),FELT,cabinet,.001)
cube('Fallboard_Bottom_Brass_Trim',(0,-.414,.782),(1.235,.003,.0024),GOLD,cabinet,.0005)

black_pcs=(1,3,6,8,10)
pitch_names=('C','Cs','D','Ds','E','F','Fs','G','Gs','A','As','B')
white_count=0
key_manifest=[]
W=.0235
for pitch in range(21,109):
    is_black=pitch%12 in black_pcs
    x=(white_count if is_black else white_count+.5)*W-26*W
    name='Key_%03d_%s%d'%(pitch,pitch_names[pitch%12],pitch//12-1)
    if is_black:
        outline_key=[(-.00655,-.108),(.00655,-.108),(.00655,0),(-.00655,0)]
        obj=prism(name,outline_key,-.026,0,EBONY,keyboard,.0012)
        obj.location=(x,-.422,.781)
    else:
        half=.01136
        left=-.0047 if pitch>21 and (pitch-1)%12 in black_pcs else -half
        right=.0047 if pitch<108 and (pitch+1)%12 in black_pcs else half
        shape=[(-half,-.178),(half,-.178),(half,-.108),(right,-.108),(right,0),(left,0),(left,-.108),(-half,-.108)]
        clean=[]
        for p in shape:
            if not clean or p!=clean[-1]: clean.append(p)
        obj=prism(name,clean,-.022,0,IVORY,keyboard,.00065)
        obj.location=(x,-.422,.765)
        white_count+=1
    obj['midi_pitch']=pitch
    obj['key_index']=pitch-21
    obj['key_type']='black' if is_black else 'white'
    obj['pivot_note']='Back of key; rotate local X for a physical key press'
    key_manifest.append({'name':name,'midi_pitch':pitch,'black':is_black,'pivot_m':list(obj.location),'material_slots':len(obj.data.materials)})

# Three polished tapered legs with brass ferrules and working-scale casters.
def lathe(name, loc, profile, mat, parent, sides=28):
    verts=[]; faces=[]
    for z,r in profile:
        for i in range(sides):
            a=math.tau*i/sides
            verts.append((r*math.cos(a),r*math.sin(a),z))
    for j in range(len(profile)-1):
        for i in range(sides):
            k=(i+1)%sides; a=j*sides
            faces.append((a+i,a+k,a+sides+k,a+sides+i))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple((len(profile)-1)*sides+i for i in range(sides)))
    o=mesh_obj(name,verts,faces,mat,parent,.0009)
    o.location=loc
    return o

leg_positions=[(-.635,-.438),(.635,-.438),(-.32,1.28)]
for i,(x,y) in enumerate(leg_positions,1):
    cube('Leg_%d_Upper_Block'%i,(x,y,.666),(.135,.138,.091),WHITE,legs,.013)
    lathe('Leg_%d_Tapered_White'%i,(x,y,0),[(.130,.031),(.165,.034),(.220,.037),(.46,.049),(.603,.058),(.634,.060)],WHITE,legs)
    lathe('Leg_%d_Brass_Ferrule'%i,(x,y,0),[(.086,.034),(.135,.034),(.145,.031)],GOLD,legs)
    cylinder('Caster_%d_Swivel'%i,(x,y,.077),.023,.020,GOLD,legs,24)
    cube('Caster_%d_Fork'%i,(x,y+.010,.055),(.048,.042,.033),GOLD,legs,.005)
    cylinder('Caster_%d_Wheel'%i,(x,y+.016,.036),.031,.034,GOLD,legs,32,(1,0,0))
    for side in (-1,1):
        cylinder('Caster_%d_AxleCap_%d'%(i,side),(x+side*.019,y+.016,.036),.010,.006,GOLD,legs,20,(1,0,0))
    # Narrow tread ring, not a dark toy wheel.
    bpy.ops.mesh.primitive_torus_add(major_radius=.029,minor_radius=.003,major_segments=32,minor_segments=8,location=(x,y+.016,.036),rotation=(0,math.pi/2,0))
    o=bpy.context.object; o.name='Caster_%d_Tread'%i; move_to(o,model); o.parent=legs; finish(o,RUBBER)

# Pedal lyre belongs to the piano; three independent brass pedal meshes.
for side in (-1,1):
    a=(side*.145,-.195,.675); b=(side*.108,-.245,.226)
    rod('Lyre_White_Support_'+str(side),a,b,.022,WHITE,lyre,20)
    cylinder('Lyre_Gold_Collar_'+str(side),(side*.108,-.245,.236),.025,.028,GOLD,lyre,20)
cube('Lyre_Upper_Anchor',(0,-.197,.673),(.352,.106,.055),WHITE,lyre,.012)
cube('Lyre_Pedal_Block',(0,-.248,.215),(.282,.105,.051),WHITE,lyre,.010)
pedal_shape=[(-.013,.055),(.013,.055),(.014,-.025),(.024,-.073),(.022,-.105),(.014,-.119),(-.014,-.119),(-.022,-.105),(-.024,-.073),(-.014,-.025)]
for i,x in enumerate((-.082,0,.082)):
    pedal=prism('Pedal_'+('UnaCorda','Sostenuto','Sustain')[i],pedal_shape,-.008,.008,GOLD,lyre,.004)
    pedal.location=(x,-.286,.119)
    rod('Pedal_Rod_%d'%i,(x,-.244,.127),(x,-.184,.662),.0033,GOLD,lyre,12)

# Lid is independently editable about its true left-side hinge; no armature.
hinge_x=-.759
lid_root=empty('Lid_Hinge_Pivot',root,(hinge_x,0,.995))
lid_root.rotation_euler.y=math.radians(-35)
lid_root['open_angle_degrees']=35
lid_root['close_instructions']='Set local rotation Y to 0 degrees; hide/remove Lid_Prop for closed pose'
lid_poly=inset(outline,-.010)
lid_poly_local=[(x-hinge_x,y) for x,y in lid_poly]
lid=prism('Grand_Lid_White_Lacquer',lid_poly_local,0,.034,WHITE,lid_root,.006)
ring('Lid_Underside_Edge_Beading',inset(lid_poly_local,.034),.012,-.005,0,WHITE,lid_root,.002)
for y in (-.08,.36,.81,1.15):
    cylinder('Lid_Brass_Hinge_%.2f'%y,(hinge_x,y,.991),.008,.070,GOLD,cabinet,20,(0,1,0))
prop_low=(.34,.53,.963)
prop_closed=Vector((.39-hinge_x,.53,-.006))
prop_top=lid_root.matrix_local @ prop_closed
# matrix_local updates after scene dependency refresh; calculate hinge rotation explicitly.
a=math.radians(35)
prop_top=(hinge_x+(.39-hinge_x)*math.cos(a),.53,.995+(.39-hinge_x)*math.sin(a)-.007)
rod('Lid_Prop',prop_low,prop_top,.012,WHITE,cabinet,20)
for suffix,pnt in [('Lower',prop_low),('Upper',prop_top)]:
    cylinder('Lid_Prop_Brass_'+suffix,pnt,.016,.022,GOLD,cabinet,16)

# Studio collection is excluded from both exchange exports.
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,.001))
floor=bpy.context.object; floor.name='Studio_Floor'; move_to(floor,studio); floor.data.materials.append(FLOOR)
def area(name, location, energy, size, target, color=(1,1,1), size_y=None):
    data=bpy.data.lights.new(name,'AREA'); data.energy=energy; data.shape='RECTANGLE'; data.size=size; data.size_y=size_y or size; data.color=color
    obj=bpy.data.objects.new(name,data); studio.objects.link(obj); obj.location=location
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj
area('Key_Softbox',(-2.8,-3.2,4.7),1100,4.0,(0,.3,.65),(1,.91,.82),3.5)
area('Right_Reflection_Strip',(3.8,-.5,3.3),950,2.5,(0,.3,.9),(.83,.91,1),4)
area('Rear_Rim_Softbox',(-.2,3.7,4.2),1300,3.0,(0,.7,.9),(1,.93,.82),3)
area('Front_Keyboard_Fill',(.3,-3.9,2.0),300,3,(0,-.4,.65),(.9,.95,1),1.5)
camdata=bpy.data.cameras.new('Hero_Camera')
cam=bpy.data.objects.new('Hero_Camera',camdata); studio.objects.link(cam)
scene.camera=cam
def aim_camera(location,target,lens):
    cam.location=location
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.lens=lens
    cam.data.clip_start=.02
    cam.data.clip_end=200
aim_camera((3.12,-4.25,2.95),(0,.40,.70),52)

bpy.context.view_layer.update()
export_objects=list(model.all_objects)
mesh_objects=[o for o in export_objects if o.type=='MESH']
key_objects=[o for o in mesh_objects if 'midi_pitch' in o]
assert len(key_objects)==88
assert sum(o['key_type']=='white' for o in key_objects)==52
assert sum(o['key_type']=='black' for o in key_objects)==36
assert sorted(o['midi_pitch'] for o in key_objects)==list(range(21,109))
assert all(len(o.data.materials)==1 for o in key_objects)
assert len({o.data.as_pointer() for o in key_objects})==88
assert all(o.type in {'EMPTY','MESH'} for o in export_objects)
assert not any(o in studio.objects.values() for o in export_objects)
deps=bpy.context.evaluated_depsgraph_get()
triangles=0
vertices=0
for o in mesh_objects:
    ev=o.evaluated_get(deps); mesh=ev.to_mesh(); mesh.calc_loop_triangles()
    triangles+=len(mesh.loop_triangles); vertices+=len(mesh.vertices); ev.to_mesh_clear()
coords=[o.matrix_world@v.co for o in mesh_objects for v in o.data.vertices]
bounds={'min':[min(v[i] for v in coords) for i in range(3)],'max':[max(v[i] for v in coords) for i in range(3)]}
manifest={
    'name':'White Grand Piano', 'generator':Path(__file__).name, 'blender_version':bpy.app.version_string,
    'units':'metres','axes':{'up':'+Z','front':'-Y','keyboard_right':'+X'},
    'nominal_dimensions_m':{'width':1.52,'closed_body_length':2.24,'white_key_top':.765,'black_key_top':.781,'exposed_white_key_length':.178,'black_key_length':.108},
    'open_lid_angle_degrees':35,'keys':{'total':88,'white':52,'black':36,'separate_objects':True,'single_material_slot_each':True,'pitch_range':[21,108]},
    'export_mesh_objects':len(mesh_objects),'export_total_objects':len(export_objects),'vertices':vertices,'triangles':triangles,
    'world_bounds_m':bounds,
    'files':{'blend':'WhiteGrandPiano.blend','fbx':'Exports/WhiteGrandPiano.fbx','glb':'Exports/WhiteGrandPiano.glb','hero':'Previews/WhiteGrandPiano_Hero.png','keyboard':'Previews/WhiteGrandPiano_Keyboard.png'},
    'export_notes':['Only WhiteGrandPiano_Export collection included. Preview_Only never exported.','FBX axis_forward=-Z, axis_up=Y; mesh sources use Blender real-metre axes.','No armature; individual keys pivot at their backs; lid uses a separate hinge empty.','Spruce procedural bump is retained in .blend; interchange materials use base-color factors.','No Unity import or validation performed.'],
    'key_objects':key_manifest,
}
(HERE/'white_grand_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.object.select_all(action='DESELECT')
for obj in export_objects: obj.select_set(True)
bpy.context.view_layer.objects.active=lid
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'WhiteGrandPiano.fbx'),use_selection=True,object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=False,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
bpy.ops.export_scene.gltf(filepath=str(EXPORT/'WhiteGrandPiano.glb'),export_format='GLB',use_selection=True,export_apply=True,export_yup=True,export_extras=True)

# Preserve a useful source view and render both whole-body and playable-key detail.
for obj in studio.objects:
    obj.hide_select=True
for screen in bpy.data.screens:
    for area_ui in screen.areas:
        if area_ui.type=='VIEW_3D':
            space=area_ui.spaces.active
            space.overlay.show_extras=False
            space.region_3d.view_perspective='CAMERA'
for obj in export_objects: obj.select_set(False)
root.select_set(True); bpy.context.view_layer.objects.active=root
scene.render.filepath=str(PREVIEW/'WhiteGrandPiano_Hero.png')
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'WhiteGrandPiano.blend'))
bpy.ops.render.render(write_still=True)
aim_camera((1.3,-2.55,1.72),(0,-.40,.79),64)
scene.render.resolution_x=1800; scene.render.resolution_y=1100
scene.render.filepath=str(PREVIEW/'WhiteGrandPiano_Keyboard.png')
bpy.ops.render.render(write_still=True)
aim_camera((3.12,-4.25,2.95),(0,.40,.70),52)
scene.render.resolution_x=1800; scene.render.resolution_y=1250
scene.render.filepath=str(PREVIEW/'WhiteGrandPiano_Hero.png')
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'WhiteGrandPiano.blend'))
print('WHITE_GRAND_COMPLETE',json.dumps({'mesh_objects':len(mesh_objects),'objects':len(export_objects),'vertices':vertices,'triangles':triangles,'key_count':len(key_manifest),'output':str(HERE)},ensure_ascii=False))
