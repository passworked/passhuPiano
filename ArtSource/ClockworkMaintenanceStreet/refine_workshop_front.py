"""Incrementally refine the first workshop shown in the decorative-sign review.

Blender 5.2: -b ClockworkMaintenanceStreet.blend --python-exit-code 1 -P this_file.py
Only existing collection/material batches are patched; no runtime or scene changes.
"""
import bpy
import bmesh
import math
import json
import runpy
from pathlib import Path
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
REPORT = ROOT / 'Logs/ClockworkShopDetail'
TAG = 'cw_workshop_front_detail'


def outward(data):
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    data.update()


def mesh(name, vertices, faces, material, col, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.materials.append(material)
    outward(data)
    ob = bpy.data.objects.new(name, data)
    col.objects.link(ob)
    ob[TAG] = True
    if bevel:
        mod = ob.modifiers.new('Soft manufactured edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        mod = ob.modifiers.new('Weighted face normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
    return ob


def box(name, p, size, mat, col, bevel=.005):
    x, y, z = [s / 2 for s in size]
    vertices = [(a*x,b*y,c*z) for c in [-1,1] for b in [-1,1] for a in [-1,1]]
    ob = mesh(name, vertices, [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)], mat, col, bevel)
    ob.location = p
    return ob


def beam(name, a, b, width, depth, mat, col, bevel=.004):
    a, b = Vector(a), Vector(b)
    ob = box(name, (a+b)*.5, (width,depth,(b-a).length), mat, col, bevel)
    ob.rotation_euler = Vector((0,0,1)).rotation_difference((b-a).normalized()).to_euler()
    return ob


def rectangle_frame(name,p,width,height,border,depth,mat,col,bevel):
    bx,by=border if isinstance(border,tuple) else (border,border)
    rings=[]
    for yy in [-depth/2,depth/2]:
        for ww,hh in [(width,height),(width-2*bx,height-2*by)]:
            rings += [(-ww/2,yy,-hh/2),(ww/2,yy,-hh/2),(ww/2,yy,hh/2),(-ww/2,yy,hh/2)]
    faces=[]
    for i in range(4):
        j=(i+1)%4
        faces += [(i,j,j+4,i+4),(i+8,i+12,j+12,j+8),(i,i+8,j+8,j),(i+4,j+4,j+12,i+12)]
    ob=mesh(name,rings,faces,mat,col,bevel)
    ob.location=p
    return ob


def lathe(name, profile, mat, col, n=20):
    vertices = [(r*math.cos(i*math.tau/n),r*math.sin(i*math.tau/n),z)
                for r,z in profile for i in range(n)]
    faces = [(j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i)
             for j in range(len(profile)-1) for i in range(n)]
    faces += [tuple(reversed(range(n))),tuple((len(profile)-1)*n+i for i in range(n))]
    return mesh(name, vertices, faces, mat, col)


def bolt(name, p, radius, mat, col):
    ob = lathe(name, [(radius*.94,0),(radius,.002),(radius,.008),(radius*.76,.011)], mat, col, 12)
    ob.location = p
    ob.rotation_euler.x = math.pi/2
    return ob


def world_bounds(ob):
    points = [ob.matrix_world @ Vector(p) for p in ob.bound_box]
    return [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]


def apply_details():
    col = bpy.data.collections['06 | Clockmaker shops and cabinets']
    for ob in list(bpy.context.scene.objects):
        if ob.get(TAG):
            bpy.data.objects.remove(ob, do_unlink=True)
    mats = bpy.data.materials
    walnut, iron = mats['Dark structural walnut'], mats['Aged petrol green cast iron']
    oak, gold = mats['Worn oak edge'], mats['Polished brass wear']
    x, y, h, w, d = -5.7, 5.15, 3.05, 3.3, 2.35
    front, back, ridge = y-d/2-.23, y+d/2+.2, h+.78

    # Preserve the original roof object, replacing the open two-face sheet with
    # one closed 75 mm shell, including its underside and both gable edges.
    roof = bpy.data.objects['Little workshop gabled roof']
    top = [(x-w/2-.25,front,h),(x+w/2+.25,front,h),
           (x-w/2-.25,y,ridge),(x+w/2+.25,y,ridge),
           (x-w/2-.25,back,h),(x+w/2+.25,back,h)]
    vertices = top + [(a,b,c-.075) for a,b,c in top]
    faces = [(0,1,3,2),(2,3,5,4),(8,9,7,6),(10,11,9,8)]
    faces += [(a,a+6,b+6,b) for a,b in [(0,1),(1,3),(3,5),(5,4),(4,2),(2,0)]]
    data = bpy.data.meshes.new('Closed 75mm workshop roof')
    data.from_pydata(vertices, [], faces)
    data.materials.append(walnut)
    outward(data)
    roof.data = data
    roof.modifiers.clear()
    mod = roof.modifiers.new('Rounded roof edges', 'BEVEL'); mod.width=.005; mod.segments=2
    for ob in list(col.objects):
        if ob.name.startswith('Standing seam roof rib'):
            bpy.data.objects.remove(ob, do_unlink=True)

    # Folded standing seams sit 4 mm above the weathering face, not on it.
    for side, edge in [('Front',front),('Rear',back)]:
        tangent = Vector((0,y-edge,ridge-h)).normalized()
        normal = Vector((0,-tangent.z,tangent.y))
        if normal.z < 0: normal = -normal
        for i in range(18):
            xx=x-w/2-.20+i*(w+.4)/17
            a=Vector((xx,edge,h))+normal*.004
            b=Vector((xx,y,ridge))-tangent*.055+normal*.004
            profile=[(-.024,0),(.024,0),(.024,.006),(.010,.006),(.010,.031),(-.010,.031),(-.010,.006),(-.024,.006)]
            vs=[tuple(p+Vector((sx,0,0))+normal*nz) for p in [a,b] for sx,nz in profile]
            fs=[tuple(reversed(range(8))),tuple(8+j for j in range(8))]
            fs += [(j,(j+1)%8,(j+1)%8+8,j+8) for j in range(8)]
            mesh('CWShop '+side+' folded seam '+str(i),vs,fs,iron,col,.0015)
        box('CWShop '+side+' eave fascia',(x,edge,h-.034),(w+.53,.065,.13),oak,col,.009)
        box('CWShop '+side+' eave metal lip',(x,edge-.045 if side=='Front' else edge+.045,h-.017),(w+.54,.023,.065),iron,col,.003)
    for xx in [x-w/2-.25,x+w/2+.25]:
        profile=[(front-.012,h-.045),(y,ridge-.045),(back+.012,h-.045),(back+.012,h-.145),(y,ridge-.145),(front-.012,h-.145)]
        vs=[(side,yy,zz) for side in [xx-.04,xx+.04] for yy,zz in profile]
        fs=[tuple(reversed(range(6))),tuple(6+j for j in range(6))]
        fs += [(j,(j+1)%6,(j+1)%6+6,j+6) for j in range(6)]
        mesh('CWShop solid gable verge',vs,fs,oak,col,.004)
    # Ridge cap has a solid folded V section rather than a coplanar decal.
    profile=[(-.13,-.049),(0,.030),(.13,-.049),(.13,-.059),(0,.020),(-.13,-.059)]
    vs=[(xx,y+dy,ridge+dz) for xx in [x-w/2-.29,x+w/2+.29] for dy,dz in profile]
    fs=[tuple(reversed(range(6))),tuple(6+j for j in range(6))]
    fs += [(j,(j+1)%6,(j+1)%6+6,j+6) for j in range(6)]
    mesh('CWShop folded ridge weather cap',vs,fs,iron,col,.0015)

    # Layered joinery: deep back board, raised moulding and recessed enamel face.
    box('CWShop enamel inset',(x,3.862,2.81),(3.05,.024,.32),iron,col,.009)
    rectangle_frame('CWShop mitred walnut sign frame',(x,3.858,2.81),3.30,.49,(.09,.08),.115,walnut,col,.007)
    rectangle_frame('CWShop continuous brass inlay',(x,3.822,2.81),3.078,.346,.012,.020,gold,col,.002)
    for xx in [x-1.60,x+1.60]:
        for zz in [2.655,2.965]:
            bolt('CWShop frame screw',(xx,3.795,zz),.014,gold,col)
    lettering=bpy.data.objects['Hand painted shop name']
    lettering.location.y=3.847
    lettering.data.extrude=.004
    lettering.data.bevel_depth=0
    lettering.data.bevel_resolution=0
    # Union intersecting cursive strokes, removing their overlapping coplanar caps.
    # Keep the editable text source hidden and export its closed relief mesh.
    bpy.context.view_layer.update()
    cast_data=bpy.data.meshes.new_from_object(lettering.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    cast=bpy.data.objects.new('CWShop cast script lettering',cast_data)
    col.objects.link(cast);cast[TAG]=True;cast.matrix_world=lettering.matrix_world.copy()
    bpy.ops.object.select_all(action='DESELECT');cast.select_set(True);bpy.context.view_layer.objects.active=cast
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.intersect_boolean(operation='UNION',use_self=True,solver='EXACT')
    bpy.ops.object.mode_set(mode='OBJECT')
    outward(cast.data)
    lettering.hide_render=True
    # Stagger exposed planes at joinery intersections instead of depth bias.
    bpy.data.objects['Shop fascia sign'].dimensions=(w-.04,.18,.44)
    apron=bpy.data.objects['Shop front apron'];apron.dimensions=(w-.20,.12,1.0);apron.location.z=.54
    bpy.data.objects['Shop window shelf'].location.y=3.78
    bpy.data.objects['Brass ensemble side inlay'].dimensions=(.024,10.072,.012)
    for xx in [x-w/2,x+w/2]:
        # Iron post shoes, straps and visible fasteners give the fascia support.
        box('CWShop post shoe',(xx,3.975,.115),(.215,.245,.25),iron,col,.010)
        box('CWShop post capital',(xx,3.975,2.49),(.20,.23,.12),oak,col,.009)
        box('CWShop fascia fixing strap',(xx,3.868,2.77),(.075,.022,.38),iron,col,.004)
        for zz in [.07,.17,2.45]: bolt('CWShop post bolt',(xx,3.843,zz),.018,gold,col)
        beam('CWShop shelf corbel',(xx,3.965,.76),(xx,3.53,1.055),.07,.085,walnut,col,.007)
        beam('CWShop roof knee brace',(xx,4.01,2.29),(xx,4.43,2.90),.07,.075,walnut,col,.006)
    box('CWShop counter front nosing',(x,3.507,1.105),(3.55,.035,.10),oak,col,.009)
    box('CWShop counter inset bead',(x,3.466,1.115),(3.40,.012,.016),gold,col,.003)
    # Brackets beneath the two existing stock shelves.
    for zz in [1.75,2.35]:
        for xx in [x-1.22,x+1.22]:
            beam('CWShop shelf bracket',(xx,6.19,zz-.30),(xx,5.88,zz-.043),.025,.030,iron,col,.003)
    for ob in list(col.objects):
        if ob.name.startswith('Oil bottle'):
            if 'cw_bottle_anchor' not in ob:
                lo,hi=world_bounds(ob);ob['cw_bottle_anchor']=[(lo[0]+hi[0])/2,(lo[1]+hi[1])/2,lo[2]]
            anchor=Vector(ob['cw_bottle_anchor'])
            material=ob.data.materials[0]
            replacement=lathe('CWShop temporary bottle',[(.054,0),(.067,.008),(.069,.025),(.069,.165),(.060,.190),(.034,.215),(.032,.239)],material,col,20)
            ob.data=replacement.data
            ob.matrix_world=Matrix.Translation(anchor)
            for poly in ob.data.polygons: poly.use_smooth=True
            bpy.data.objects.remove(replacement,do_unlink=True)

    # Bring the existing vines to the posts, outside the lettering. Leaves gain
    # a closed thin section so their backs no longer vanish under backface culling.
    ivy=[]
    for ob in bpy.data.collections['08 | Ivy and workshop life'].objects:
        if ob.name.startswith(('Ivy leaf','Ivy stem')):
            lo,hi=world_bounds(ob)
            if ob.get('cw_shop_vine') or (3.70<lo[1]<3.95 and -7.1<(lo[0]+hi[0])/2<-4.3):
                if not ob.get('cw_shop_vine'):
                    ob.location.x += -.68 if (lo[0]+hi[0])/2 < -5.7 else .78
                    ob.location.y -= .10
                    ob['cw_shop_vine']=True
                if ob.type=='MESH':
                    # Replace the former flat diamonds with smaller lobed leaves.
                    if 'cw_leaf_anchor' not in ob:
                        ob['cw_leaf_anchor']=list(ob.matrix_world@ob.data.vertices[0].co)
                    anchor=Vector(ob['cw_leaf_anchor'])
                    index=int(ob.name.rsplit('.',1)[1]) if '.' in ob.name else 0
                    sign=-1 if index%2==0 else 1
                    along=Vector((sign*.82,-.06-.03*(index%3),.56)).normalized()
                    across=Vector((-along.z,0,along.x)).normalized()
                    outline=[(0,0),(-.018,.026),(-.045,.025),(-.032,.060),(-.055,.078),(-.018,.086),(0,.145),(.020,.085),(.052,.075),(.030,.052),(.043,.027),(.016,.026)]
                    points=[tuple(anchor+across*a+along*b) for a,b in outline]
                    points.append(tuple(anchor+along*.065+Vector((0,-.014,0))))
                    data=bpy.data.meshes.new('Lobed ivy leaf');data.from_pydata(points,[],[(i,(i+1)%12,12) for i in range(12)])
                    data.materials.append(mats['Ivy sage leaves']);ob.data=data;ob.matrix_world=Matrix.Identity(4)
                    outward(ob.data)
                    for poly in ob.data.polygons:poly.use_smooth=True
                    mod=ob.modifiers.get('Leaf thickness') or ob.modifiers.new('Leaf thickness','SOLIDIFY')
                    mod.thickness=.0015;mod.offset=0
                ivy.append(ob)
    bpy.context.view_layer.update()
    keys={('06','CW_'+name) for name in ['Dark_structural_walnut','Aged_petrol_green_cast_iron','Worn_oak_edge',
            'Polished_brass_wear','Old_brushed_brass','Unglazed_terracotta']}
    keys |= {('08','CW_Dark_structural_walnut'),('08','CW_Ivy_sage_leaves'),('01','CW_Polished_brass_wear')}
    return {'batchKeys': sorted(keys),'closedRoofThicknessMetres':.075,'raisedLetterThicknessMetres':.008,
            'leafThicknessMetres':.0015,'movedVineObjectCount':len(ivy),
            'newSourceObjects':sum(bool(o.get(TAG)) for o in bpy.context.scene.objects)}


def main():
    REPORT.mkdir(parents=True,exist_ok=True)
    config=apply_details()
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ClockworkMaintenanceStreet.blend'))
    patch=runpy.run_path(str(HERE/'refresh_decorative_lettering.py'))['patch_batches']
    config['modelPatch']=patch(config['batchKeys'],REPORT)
    (REPORT/'detail-update.json').write_text(json.dumps(config,indent=2),encoding='utf8')
    print('WORKSHOP_DETAIL',json.dumps(config),flush=True)


if __name__=='__main__':
    main()
