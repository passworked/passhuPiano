"""Blender source for the music classroom. Run with Blender --background --python.

Geometry is authored in metres using Unity-style (x, up, forward) helper inputs.
The .blend retains individual editable pieces; only the FBX is joined into
material/zone batches. No player/instrument assets are included or replaced.
"""
import bpy
import math
import random
import json
import sys
from pathlib import Path
from mathutils import Vector
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(__file__).resolve().parent
OUT = ROOT / "Assets/Scenes/DemoMusicClassroom"
TEX = OUT / "Textures"
for folder in (SOURCE, OUT / "Models", TEX):
    folder.mkdir(parents=True, exist_ok=True)
random.seed(31026)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.preferences.filepaths.save_version = 0
for collection in list(bpy.data.collections):
    if collection.name != "Collection":
        bpy.data.collections.remove(collection)

def v(p):
    # Unity's FBX importer changes handedness by flipping X.
    return Vector((-p[0], -p[2], p[1]))

materials, manifest, collision = {}, [], []
zones = {}
zone = "Architecture"

def group(name):
    global zone
    zone = name
    if name not in zones:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
        zones[name] = col

def finish(obj, name, mat, tint=1):
    obj.name = name
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    zones[zone].objects.link(obj)
    obj.data.materials.append(materials[mat])
    attr = obj.data.color_attributes.new(name="Col", type="BYTE_COLOR", domain="CORNER")
    for c in attr.data:
        c.color = (tint, tint, tint, 1)
    return obj

def material(name, color, roughness=.65, metal=0, texture=None, emission=0, glass=False):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, .13 if glass else 1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get("Principled BSDF")
    bs.inputs["Base Color"].default_value = (*color, 1)
    bs.inputs["Roughness"].default_value = roughness
    bs.inputs["Metallic"].default_value = metal
    if glass:
        bs.inputs["Alpha"].default_value = .13
    if emission:
        bs.inputs["Emission Color"].default_value = (*color, 1)
        bs.inputs["Emission Strength"].default_value = emission
    if texture:
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(str(TEX / texture), check_existing=True)
        mat.node_tree.links.new(node.outputs["Color"], bs.inputs["Base Color"])
    materials[name] = mat
    manifest.append(dict(name=name, color=list(color), smoothness=1-roughness,
                         metallic=metal, texture=texture or "", emission=emission,
                         glass=glass))

def texture(name, parquet=False):
    n = 1024 if parquet else 512
    yy, xx = np.mgrid[0:n, 0:n]
    rng = np.random.default_rng(31026)
    if parquet:
        cell = n//4
        bx, by = xx//cell, yy//cell
        flip = (bx+by) % 2
        along = np.where(flip == 0, xx % cell, yy % cell)
        across = np.where(flip == 0, yy % cell, xx % cell)
        plank = across//(cell//4)
        seed = bx*73+by*137+plank*53
        variation = (np.sin(seed*21.17)*43758.5453) % 1
        grain = np.sin(across*1.35+np.sin(along*.032)*1.8)*.022
        grain += np.sin(across*.36+np.sin(along*.014)*2.3)*.035
        grain += rng.random((n,n))*.025
        tone = .81 + variation*.28 + grain
        seam = (across % (cell//4) < 2) | (along < 2) | (along >= cell-1)
        tone = np.where(seam, tone*.57, tone)
        base = np.array([.60,.39,.215])
    else:
        grain = np.sin(xx*.75+np.sin(yy*.041)*2.2)*.032
        grain += np.sin(xx*.18+np.sin(yy*.023)*3)*.045
        tone = .92+grain+rng.random((n,n))*.035
        base = np.array([.61,.41,.245])
    rgba = np.ones((n,n,4), dtype=np.float32)
    rgba[:,:,:3] = np.clip(tone[:,:,None]*base, 0, 1)
    image = bpy.data.images.new(name, width=n, height=n)
    # PNG pixels use the authored sRGB palette; the imported texture performs
    # the conversion to linear during shading in both Blender and Unity.
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(TEX / name)
    image.file_format = 'PNG'
    image.save()
    bpy.data.images.remove(image)

texture("Classroom_Parquet.png", True)
texture("Classroom_Oak.png")
if "--textures-only" in sys.argv:
    print("CLASSROOM_TEXTURES_UPDATED")
    sys.exit(0)
material("Classroom_Plaster", (.83,.80,.72), .88)
material("Classroom_Ceiling", (.78,.77,.70), .9)
material("Classroom_Oak", (1,1,1), .52, texture="Classroom_Oak.png")
material("Classroom_Floor", (1,1,1), .38, texture="Classroom_Parquet.png")
material("Classroom_DarkWood", (.22,.125,.064), .5)
material("Classroom_Aluminium", (.53,.52,.46), .35, .65)
material("Classroom_Steel", (.22,.25,.24), .32, .7)
material("Classroom_Ivory", (.88,.85,.75), .65)
material("Classroom_Curtain", (.91,.86,.74), .92)
material("Classroom_Chalkboard", (.073,.143,.119), .93)
material("Classroom_Chalk", (.57,.62,.50), .95)
material("Classroom_Glass", (.70,.84,.83), .18, .05, glass=True)
material("Classroom_Cork", (.44,.30,.17), .95)
material("Classroom_Paper", (.84,.82,.72), .9)
material("Classroom_Ink", (.22,.27,.29), .9)
material("Classroom_BookNavy", (.12,.23,.29), .75)
material("Classroom_BookSage", (.34,.41,.31), .75)
material("Classroom_BookRust", (.47,.22,.14), .75)
material("Classroom_Brass", (.55,.39,.15), .3, .7)
material("Classroom_Lamp", (1,.91,.73), .35, emission=1.8)
material("Classroom_Terracotta", (.45,.25,.15), .85)
material("Classroom_Bark", (.27,.23,.14), .95)
material("Classroom_Leaves", (.27,.40,.16), .95)
material("Classroom_Ground", (.34,.39,.21), .95)
material("Classroom_DistantSchool", (.70,.71,.65), .9)
material("Classroom_DistantWindow", (.27,.40,.42), .4, .15)

def cube(name, p, s, mat, bevel=.012, tint=1, yaw=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=v(p))
    obj = bpy.context.object
    obj.scale = (s[0], s[2], s[1])
    obj.rotation_euler.z = -math.radians(yaw)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        m = obj.modifiers.new("Soft manufactured edges", 'BEVEL')
        m.width = min(bevel, min(s)*.22)
        m.segments = 2
    return finish(obj, name, mat, tint)

def cylinder(name, a, b, radius, mat, radius2=None, sides=16, tint=1):
    av, bv = v(a), v(b)
    direction = bv-av
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius,
        radius2=radius if radius2 is None else radius2, depth=direction.length,
        location=(av+bv)*.5)
    obj = bpy.context.object
    obj.rotation_euler = direction.to_track_quat('Z','Y').to_euler()
    return finish(obj, name, mat, tint)

def ellipsoid(name, p, s, mat, tint=1, smooth=True):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=v(p))
    obj = bpy.context.object
    obj.scale = (s[0],s[2],s[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish(obj,name,mat,tint)
    for poly in obj.data.polygons:
        poly.use_smooth = smooth
    return obj

def collider(name, p, s, yaw=0):
    collision.append(dict(name=name, center=list(p), size=list(s), yaw=yaw))

group("Architecture")
# The existing world Floor collider stays in service. The new parquet sits at y=0.
floor=cube("Parquet floor", (0,-.06,-.95), (10.5,.12,8.9), "Classroom_Floor", 0)
# Cube top UVs are replaced with world planar UVs for a 2.4m parquet repeat.
uv=floor.data.uv_layers.active
for poly in floor.data.polygons:
    for li in poly.loop_indices:
        co=floor.matrix_world @ floor.data.vertices[floor.data.loops[li].vertex_index].co
        uv.data[li].uv=(co.x/2.4, -co.y/2.4)
cube("Acoustic ceiling", (0,3.92,-.95), (10.65,.14,9.0), "Classroom_Ceiling")
for z in [-5.38,3.48]:
    cube("Plaster end wall", (0,1.93,z), (10.65,3.86,.18), "Classroom_Plaster")
    collider("End wall", (0,1.93,z), (10.65,3.86,.18))
    cube("Oak end wainscot", (0,.53,z+(.105 if z<0 else -.105)), (10.5,1.06,.035), "Classroom_Oak")
    cube("End chair rail", (0,1.085,z+(.14 if z<0 else -.14)), (10.5,.065,.075), "Classroom_DarkWood")
    cube("End skirting", (0,.065,z+(.14 if z<0 else -.14)), (10.5,.13,.07), "Classroom_DarkWood")
    for x in np.arange(-5.1,5.2,.30):
        cube("Wainscot join", (float(x),.54,z+(.126 if z<0 else -.126)), (.009,.98,.006), "Classroom_DarkWood",0,.72)
cube("Window parapet", (-5.27,.54,-.95), (.18,1.08,8.9), "Classroom_Plaster")
collider("Window wall safety",(-5.27,1.9,-.95),(.18,3.8,8.9))
cube("Window wainscot", (-5.16,.53,-.95), (.035,1.06,8.9), "Classroom_Oak")
cube("Window skirting", (-5.13,.065,-.95), (.07,.13,8.9), "Classroom_DarkWood")
cube("Continuous window sill", (-5.10,1.10,-.95), (.35,.07,8.9), "Classroom_Oak")
cube("Window lintel", (-5.27,3.69,-.95), (.20,.35,8.9), "Classroom_Plaster")
for z in np.arange(-5.25,3.5,.3):
    cube("Window wainscot join",(-5.135,.54,float(z)),(.008,.98,.009),"Classroom_DarkWood",0,.75)

def curtain(center, side):
    # Gathered, pleated fabric with a gently scalloped hem, modelled as a sheet.
    verts, faces=[],[]
    nx, ny=28,12
    for j in range(ny+1):
        t=j/ny
        for i in range(nx+1):
            u=i/nx
            width=.42+.13*t
            z=center+side*(.70-width*u)
            x=-4.91+.047*math.cos(u*math.tau*5.5)+.035*math.sin(t*math.pi)
            y=3.52-2.54*t+.022*math.cos(u*math.tau*5.5)*t
            verts.append(v((x,y,z)))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i
            faces.append((a,a+1,a+nx+2,a+nx+1))
    mesh=bpy.data.meshes.new("Pleated linen geometry")
    mesh.from_pydata(verts,[],faces)
    obj=bpy.data.objects.new("Gathered linen curtain",mesh)
    zones[zone].objects.link(obj)
    finish(obj,"Gathered linen curtain","Classroom_Curtain")
    for poly in mesh.polygons:
        poly.use_smooth=True
    solid=obj.modifiers.new("Fabric thickness",'SOLIDIFY')
    solid.thickness=.006
    for z in [center+side*(.70-.42*u) for u in np.linspace(0,1,7)]:
        cylinder("Curtain tab",(-4.91,3.49,z),(-4.91,3.57,z),.013,"Classroom_Curtain",sides=8)

group("WindowsAndCurtains")
for z in [-4.45,-2.69,-.93,.83,2.59]:
    for zz in [z-.84,z+.84]:
        cube("Window pier",(-5.27,2.30,zz),(.19,2.37,.11),"Classroom_Plaster")
        cube("Frame vertical",(-5.11,2.31,zz),(.10,2.32,.055),"Classroom_Aluminium")
    for y in [1.16,2.69,3.48]:
        cube("Frame horizontal",(-5.11,y,z),(.10,.055,1.72),"Classroom_Aluminium")
    cube("Sash vertical",(-5.10,2.31,z),(.08,2.28,.04),"Classroom_Aluminium")
    cube("Window glass",(-5.15,2.31,z),(.008,2.27,1.62),"Classroom_Glass",0)
    cylinder("Curtain rail",(-4.93,3.59,z-.86),(-4.93,3.59,z+.86),.016,"Classroom_Aluminium",sides=10)
    curtain(z,-1)
    curtain(z,1)

group("Architecture")
# Right wall has two genuinely open 1.5m doorways into a short side corridor.
for a,b in [(-5.38,-4.45),(-2.85,.70),(2.30,3.48)]:
    z=(a+b)*.5
    cube("Corridor wall", (5.27,1.38,z),(.18,2.76,b-a),"Classroom_Plaster")
    collider("Corridor wall",(5.27,1.38,z),(.18,2.76,b-a))
    cube("Corridor wainscot",(5.16,.53,z),(.035,1.06,b-a),"Classroom_Oak")
    cube("Corridor chair rail",(5.12,1.085,z),(.08,.065,b-a),"Classroom_DarkWood")
    cube("Corridor skirting",(5.13,.065,z),(.07,.13,b-a),"Classroom_DarkWood")
    for zz in np.arange(a+.12,b,.30):
        cube("Corridor wainscot join",(5.135,.54,float(zz)),(.008,.98,.009),"Classroom_DarkWood",0,.75)
cube("Corridor transom header",(5.27,3.65,-.95),(.18,.42,8.9),"Classroom_Plaster")
for z in np.arange(-5.3,3.5,1.1):
    cube("Transom mullion",(5.17,3.08,float(z)),(.07,.67,.045),"Classroom_Aluminium")
for y in [2.75,3.43]:
    cube("Transom rail",(5.17,y,-.95),(.08,.055,8.9),"Classroom_Aluminium")
cube("Transom glazing",(5.22,3.09,-.95),(.008,.62,8.75),"Classroom_Glass",0)
for z in [-3.65,1.50]:
    for zz in [z-.81,z+.81]:
        cube("Door jamb",(5.15,1.38,zz),(.20,2.76,.08),"Classroom_Aluminium")
    cube("Door lintel",(5.15,2.73,z),(.20,.08,1.7),"Classroom_Aluminium")
    cube("Sliding track",(5.26,.018,z),(.20,.035,1.65),"Classroom_Aluminium")
    # The leaf is slid aside. A 1.14m clear opening remains, without a trigger.
    zz=z+.58
    cube("Sliding leaf lower",(5.34,.55,zz),(.075,1.08,.44),"Classroom_Oak")
    for zzz in [zz-.23,zz+.23]:
        cube("Sliding leaf stile",(5.34,1.36,zzz),(.075,2.64,.04),"Classroom_Aluminium")
    for y in [1.1,2.67]:
        cube("Sliding leaf rail",(5.34,y,zz),(.075,.05,.50),"Classroom_Aluminium")
    cube("Door leaf glass",(5.34,1.89,zz),(.008,1.52,.40),"Classroom_Glass",0)
    cube("Door recessed handle",(5.285,1.03,zz-.15),(.015,.18,.035),"Classroom_Steel")
    collider("Open sliding leaf",(5.34,1.35,zz),(.09,2.7,.50))

group("Corridor")
cube("Corridor floor",(6.45,-.07,-.95),(2.4,.14,9.0),"Classroom_Ivory")
cube("Corridor ceiling",(6.45,3.65,-.95),(2.4,.12,9.0),"Classroom_Ceiling")
cube("Corridor outer parapet",(7.62,.50,-.95),(.16,1,9),"Classroom_Plaster")
collider("Corridor outer barrier",(7.62,1.8,-.95),(.16,3.6,9))
for z in [-5.40,3.50]:
    cube("Corridor end wall",(6.45,1.8,z),(2.5,3.6,.15),"Classroom_Plaster")
    collider("Corridor end wall",(6.45,1.8,z),(2.5,3.6,.15))
for z in np.arange(-5.3,3.7,1.5):
    cube("Corridor outer post",(7.60,2.30,float(z)),(.12,2.60,.075),"Classroom_Aluminium")
for y in [1,2.5,3.55]:
    cube("Corridor outer rail",(7.60,y,-.95),(.12,.07,9),"Classroom_Aluminium")
cube("Corridor outer glazing",(7.62,2.25,-.95),(.008,2.45,8.85),"Classroom_Glass",0)

group("CeilingDetails")
for x in np.arange(-4.8,5.3,1.2):
    cube("Ceiling panel joint",(float(x),3.841,-.95),(.013,.006,8.82),"Classroom_Aluminium",0,.8)
for z in np.arange(-5.25,3.5,1.2):
    cube("Ceiling panel joint",(0,3.841,float(z)),(10.45,.006,.013),"Classroom_Aluminium",0,.8)
for z in [-2.3,1.3]:
    cube("Structural ceiling beam",(0,3.68,z),(10.5,.28,.18),"Classroom_Plaster")
for x in [-2.65,2.65]:
    for z in [-3.7,-.85,2]:
        for xx in [x-.55,x+.55]:
            cylinder("Luminaire suspension",(xx,3.83,z),(xx,3.53,z),.008,"Classroom_Steel",sides=8)
        cube("Fluorescent fixture",(x,3.49,z),(1.45,.085,.30),"Classroom_Ivory")
        for zz in [z-.085,z+.085]:
            cylinder("Diffused fluorescent tube",(x-.65,3.438,zz),(x+.65,3.438,zz),.024,"Classroom_Lamp",sides=10)
        cube("Luminaire reflector",(x,3.445,z),(1.36,.016,.26),"Classroom_Aluminium")

group("Chalkboard")
cube("Chalkboard wood surround",(0,2.23,3.30),(5.85,1.65,.095),"Classroom_DarkWood")
cube("Chalkboard aluminium frame",(0,2.23,3.24),(5.76,1.57,.045),"Classroom_Aluminium")
cube("Green writing surface",(0,2.23,3.208),(5.66,1.47,.025),"Classroom_Chalkboard",.006)
for yy in [1.82,2.20,2.58]:
    for k in range(5):
        cube("Music staff",(0,yy+k*.055,3.191),(5.32,.004,.0025),"Classroom_Chalk",0,.8)
cube("Chalk tray",(0,1.41,3.12),(5.83,.035,.24),"Classroom_Aluminium")
for x in [-2.2,-1.9,1.7]:
    cylinder("Chalk stick",(x,1.44,3.08),(x+.09,1.44,3.08),.006,"Classroom_Paper",sides=8)
cube("Felt eraser",(2.2,1.45,3.12),(.16,.047,.065),"Classroom_DarkWood")
cube("Eraser felt",(2.2,1.425,3.12),(.16,.018,.065),"Classroom_Ivory")
# Understated chalk notation at one end; the board remains a calm backdrop.
for i in range(8):
    x=-2.2+i*.25
    y=2.62+math.sin(i*1.4)*.055
    ellipsoid("Chalk note",(x,y,3.188),(.040,.025,.002),"Classroom_Chalk",.8)
    cube("Chalk stem",(x+.032,y+.09,3.188),(.006,.18,.002),"Classroom_Chalk",0,.8)

group("WallClock")
cylinder("Clock case",(3.90,3.10,3.20),(3.90,3.10,3.31),.245,"Classroom_Steel",sides=64)
cylinder("Clock ivory dial",(3.90,3.10,3.185),(3.90,3.10,3.196),.224,"Classroom_Ivory",sides=64)
for i in range(60):
    a=i*math.tau/60
    r=.195
    o=cube("Clock tick",(3.9+math.sin(a)*r,3.1+math.cos(a)*r,3.175),
        (.012 if i%5==0 else .005,.035 if i%5==0 else .015,.005),"Classroom_Ink",0)
    # World-XY dial: Blender X/Z plane, rotate around Blender Y.
    o.rotation_euler.y=a
cylinder("Clock hour hand",(3.9,3.1,3.16),(3.79,3.164,3.16),.009,"Classroom_Ink",sides=8)
cylinder("Clock minute hand",(3.9,3.1,3.15),(4.055,3.19,3.15),.006,"Classroom_Ink",sides=8)
ellipsoid("Clock pin",(3.9,3.1,3.14),(.018,.018,.01),"Classroom_Brass")

def books(x,y,z,width,rows):
    for row in range(rows):
        cursor=x-width*.46
        while cursor<x+width*.45:
            w=random.uniform(.038,.075)
            h=random.uniform(.20,.29)
            mat=random.choice(["Classroom_BookNavy","Classroom_BookSage","Classroom_BookRust","Classroom_Paper"])
            cube("Bound music score",(cursor+w*.5,y+row*.37+h*.5,z),(w,h,.20),mat,.003,random.uniform(.85,1))
            for yy in [y+row*.37+.035,y+row*.37+h-.035]:
                cube("Book spine band",(cursor+w*.5,yy,z-.104),(w*.65,.008,.004),"Classroom_Paper",0,.75)
            cursor+=w+.007

group("Storage")
x,z=-3.91,2.98
cube("Tall score cabinet back",(x,1.37,z+.20),(1.50,2.60,.07),"Classroom_Oak")
for xx in [x-.76,x+.76]:
    cube("Cabinet side",(xx,1.37,z),(.065,2.60,.50),"Classroom_Oak")
for y in [.10,.85,1.25,1.62,1.99,2.36,2.69]:
    cube("Cabinet shelf",(x,y,z),(1.55,.045,.53),"Classroom_Oak")
for xx in [x-.375,x+.375]:
    cube("Lower cupboard door",(xx,.47,z-.29),(.72,.69,.045),"Classroom_Oak",.008)
    cube("Lower cupboard handle",(xx+(.25 if xx<x else -.25),.55,z-.325),(.025,.11,.025),"Classroom_Brass")
    for xxx in [xx-.35,xx+.35]:
        cube("Upper glazed stile",(xxx,1.78,z-.29),(.035,1.78,.045),"Classroom_Oak")
    for y in [.90,2.66]:
        cube("Upper glazed rail",(xx,y,z-.29),(.74,.04,.045),"Classroom_Oak")
    cube("Cabinet glazing",(xx,1.78,z-.295),(.66,1.70,.008),"Classroom_Glass",0)
books(x,.90,z-.08,1.4,5)
collider("Tall cabinet",(x,1.37,z),(1.60,2.74,.62))
cube("Low cupboard",(3.92,.49,3.03),(1.85,.86,.58),"Classroom_Oak")
cube("Cupboard countertop",(3.92,.95,3.01),(1.94,.065,.64),"Classroom_Oak")
for xx in [3.48,4.37]:
    cube("Cupboard door",(xx,.50,2.72),(.85,.77,.04),"Classroom_Oak")
    cube("Cupboard handle",(xx+(.29 if xx<4 else -.29),.61,2.685),(.026,.115,.025),"Classroom_Brass")
books(4.22,.99,3.02,.72,1)
collider("Low cupboard",(3.92,.49,3.03),(1.96,.98,.64))

group("Noticeboards")
cube("Noticeboard frame",(5.08,2.02,-1.03),(.075,1.14,1.57),"Classroom_Oak")
cube("Cork pinboard",(5.027,2.02,-1.03),(.026,1.04,1.47),"Classroom_Cork")
for z,y in [(-1.48,2.20),(-1.02,2.20),(-.57,2.20),(-1.45,1.79),(-.99,1.79)]:
    cube("Pinned paper",(5.009,y,z),(.005,.32,.23),"Classroom_Paper",0)
    for k in range(6):
        cube("Printed line",(5.004,y+.085-k*.025,z),(.002,.004,random.uniform(.11,.17)),"Classroom_Ink",0,.8)
    ellipsoid("Notice pin",(4.999,y+.14,z),(.009,.009,.009),"Classroom_BookRust")
cube("Front noticeboard frame",(3.24,2.15,3.27),(.52,.96,.045),"Classroom_Oak")
cube("Front corkboard",(3.24,2.15,3.236),(.46,.90,.018),"Classroom_Cork")
for y in [1.95,2.34]:
    cube("Notice sheet",(3.24,y,3.222),(.31,.32,.005),"Classroom_Paper",0)
    for k in range(7):
        cube("Notice print",(3.24,y+.10-k*.025,3.217),(.22,.004,.002),"Classroom_Ink",0,.85)

def chair(px,pz,yaw):
    def p(x,y,z):
        a=math.radians(yaw)
        return (px+x*math.cos(a)+z*math.sin(a),y,pz-x*math.sin(a)+z*math.cos(a))
    cube("Moulded plywood seat",p(0,.46,0),(.43,.045,.43),"Classroom_Oak",.025,yaw=yaw)
    cube("Plywood backrest",p(0,.81,.18),(.43,.24,.035),"Classroom_Oak",.020,yaw=yaw)
    for xx in [-.175,.175]:
        for zz in [-.16,.16]:
            cylinder("Tubular chair leg",p(xx*1.14,.035,zz*1.22),p(xx,.44,zz),.014,"Classroom_Steel",sides=10)
            cylinder("Rubber chair foot",p(xx*1.14,.02,zz*1.22),p(xx*1.14,.06,zz*1.22),.018,"Classroom_Ink",sides=10)
        cylinder("Back upright",p(xx,.35,.16),p(xx,.91,.20),.013,"Classroom_Steel",sides=10)
        cylinder("Seat side rail",p(xx,.41,-.18),p(xx,.41,.18),.013,"Classroom_Steel",sides=10)
    collider("Classroom chair",p(0,.46,.02),(.52,.92,.53),yaw)

group("ClassroomChairs")
for z in [-1.90,-1.15,-.40]:
    chair(4.67,z,90)
for x in [-3.35,-2.57]:
    chair(x,-4.91,180)

group("Plants")
def plant(x,y,z):
    cylinder("Terracotta pot",(x,y,z),(x,y+.19,z),.09,"Classroom_Terracotta",radius2=.14,sides=20)
    cylinder("Pot rim",(x,y+.17,z),(x,y+.20,z),.145,"Classroom_Terracotta",sides=20)
    cylinder("Soil",(x,y+.191,z),(x,y+.195,z),.125,"Classroom_DarkWood",sides=16)
    for i in range(9):
        a=i*2.399
        endpoint=(x+math.cos(a)*.19,y+.39+random.random()*.19,z+math.sin(a)*.19)
        cylinder("Plant stem",(x,y+.19,z),endpoint,.004,"Classroom_BookSage",sides=6)
        leaf=ellipsoid("Broad plant leaf",endpoint,(.085,.028,.15),"Classroom_Leaves",random.uniform(.8,1))
        leaf.rotation_euler.z=a
plant(-3.96,2.73,2.98)
plant(-5.00,1.15,-3.65)

group("Exterior")
cube("School garden ground",(-17,-1.90,0),(25,.2,38),"Classroom_Ground",0)
cube("Distant school wing",(-20,1.15,0),(3.5,6.1,22),"Classroom_DistantSchool")
for y in [-.9,1.0,2.9]:
    cube("Distant facade course",(-18.22,y-.6,0),(.08,.13,22),"Classroom_Ivory")
    for z in np.arange(-10.1,10.5,1.05):
        cube("Distant classroom window",(-18.20,y,float(z)),(.07,1.05,.78),"Classroom_DistantWindow")
        cube("Distant window mullion",(-18.15,y,float(z)),(.035,1.05,.025),"Classroom_Ivory",0)
cube("School roof coping",(-20,4.25,0),(3.7,.18,22.2),"Classroom_Ivory")
for x,z in [(-9,-7),(-11,-4),(-9.8,.5),(-12,3.4),(-9,7),(-14,-8),(-14,6),(-13,-1)]:
    y=random.uniform(2.2,3.5)
    cylinder("Garden tree trunk",(x,-1.8,z),(x,y,z),.13,"Classroom_Bark",radius2=.065,sides=10)
    for i in range(8):
        a=i*2.399
        xx=x+math.cos(a)*random.uniform(.3,1.3)
        zz=z+math.sin(a)*random.uniform(.3,1.3)
        yy=y+random.uniform(-.2,1.3)
        cylinder("Garden branch",(x,y-.6,z),(xx,yy,zz),.04,"Classroom_Bark",radius2=.018,sides=7)
        ellipsoid("Soft tree crown",(xx,yy,zz),(random.uniform(.9,1.3),.9,random.uniform(.8,1.2)),"Classroom_Leaves",random.uniform(.73,1))

# Editable source includes layout markers, camera and lighting, not fake pianos.
group("LayoutGuides")
for name,p in [("Existing UdonMidiPlayer origin - do not replace",(0,0,0)),
               ("Existing player UI",(-2.3,.95,-.73)),("Existing performance seat",(0,0,-1.03)),
               ("Main view",(4.4,2.1,-4.7))]:
    obj=bpy.data.objects.new(name,None)
    obj.location=v(p)
    obj.empty_display_type='CUBE'
    obj.empty_display_size=.2
    zones[zone].objects.link(obj)
bpy.ops.object.light_add(type='SUN', location=v((-8,7,0)))
sun=bpy.context.object
sun.name="Golden afternoon reference sun"
sun.rotation_euler=v((1,-.65,-.32)).to_track_quat('-Z','Y').to_euler()
sun.data.energy=2
sun.data.color=(1,.81,.59)
sun.data.angle=.08
bpy.ops.object.camera_add(location=v((4.4,2.1,-4.7)))
cam=bpy.context.object
cam.name="Classroom composition"
cam.rotation_euler=(v((-.35,1.65,1))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.lens=22
bpy.context.scene.camera=cam
bpy.context.scene.world.color=(.35,.40,.47)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=13
            area.spaces.active.region_3d.view_location=v((0,1,-.8))
            area.spaces.active.shading.color_type='MATERIAL'
            area.spaces.active.clip_end=200
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"JapaneseMusicClassroom.blend"))

# Apply bevel/cloth modifiers and join by material inside each zone only for FBX.
exports=[]
for name,col in zones.items():
    if name=="LayoutGuides":
        continue
    batches={}
    for obj in list(col.objects):
        if obj.type!='MESH':
            continue
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        for mod in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
        mat=obj.data.materials[0].name
        batches.setdefault(mat,[]).append(obj)
    for mat,objects in batches.items():
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active=objects[0]
        if len(objects)>1:
            bpy.ops.object.join()
        obj=bpy.context.object
        obj.name=name+"__"+mat
        # Place all object origins at world origin to keep Unity coordinates clear.
        bpy.context.scene.cursor.location=(0,0,0)
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
        exports.append(obj)
bpy.ops.object.select_all(action='DESELECT')
for obj in exports:
    obj.select_set(True)
bpy.context.view_layer.objects.active=exports[0]
bpy.ops.export_scene.fbx(filepath=str(OUT/"Models/JapaneseMusicClassroom.fbx"),
    use_selection=True, object_types={'MESH'}, use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE',
    use_triangles=True, colors_type='SRGB')
report=dict(source="ArtSource/DemoMusicClassroom/JapaneseMusicClassroom.blend",
    unityAxes="x, y up, z forward", materials=manifest, colliders=collision,
    meshes=len(exports), triangles=sum(len(o.data.loop_triangles) for o in exports))
for obj in exports:
    obj.data.calc_loop_triangles()
report["triangles"]=sum(len(o.data.loop_triangles) for o in exports)
(OUT/"classroom-manifest.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
print("CLASSROOM_EXPORT="+json.dumps(dict(meshes=report["meshes"],triangles=report["triangles"],
    source=report["source"],fbx=str(OUT/"Models/JapaneseMusicClassroom.fbx"))))
