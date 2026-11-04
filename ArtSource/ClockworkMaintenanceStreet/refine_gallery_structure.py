"""Repair gallery load paths; patch only existing collection/material FBX batches."""
import bpy, json, runpy
from pathlib import Path
from mathutils import Vector

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
REPORT=ROOT/'Logs/ClockworkStructureBake'

def apply_structure():
    helpers=runpy.run_path(str(HERE/'refine_workshop_front.py'))
    box,beam=helpers['box'],helpers['beam']
    wall=bpy.data.collections['11 | Architectural enclosure and workshop street']
    gallery=bpy.data.collections['03 | Gallery and service stair']
    wood=bpy.data.materials['Dark structural walnut']
    for ob in list(bpy.context.scene.objects):
        if ob.get('cw_gallery_structure'): bpy.data.objects.remove(ob,do_unlink=True)
    def new(ob):
        if 'cw_workshop_front_detail' in ob: del ob['cw_workshop_front_detail']
        ob['cw_gallery_structure']=True
        return ob
    def replace_beam(ob,a,b):
        temp=beam('CWGallery temporary beam',a,b,.20,.20,wood,ob.users_collection[0],.006)
        ob.data=temp.data;ob.location=temp.location.copy();ob.rotation_euler=temp.rotation_euler.copy();ob.scale=(1,1,1)
        ob.modifiers.clear()
        for mod in temp.modifiers:
            copy=ob.modifiers.new(mod.name,mod.type)
            if mod.type=='BEVEL':copy.width=mod.width;copy.segments=mod.segments
            if mod.type=='WEIGHTED_NORMAL':copy.keep_sharp=True
        bpy.data.objects.remove(temp,do_unlink=True)
    # Posts bear on the gallery deck, with a continuous head plate above the windows.
    studs=sorted([o for o in wall.objects if o.name.startswith('West gallery wall oak stud')],key=lambda o:o.location.y)
    for ob in studs:
        ob.location.z=6.535
        ob.dimensions=(.24,.24,5.27) # 3.90 to 9.17; plate underside 9.09.
    new(box('CWGallery continuous wall head plate',(-12.55,.75,9.25),(.28,24.5,.32),wood,wall,.009))
    # Old braces projected into open air. Turn them into connected wall-plane knees.
    braces=sorted([o for o in wall.objects if o.name.startswith('West overhead knee brace')],key=lambda o:o.location.y)
    joints=[]
    for ob,post in zip(braces,studs):
        a=Vector((-12.55,post.location.y,7.80));b=Vector((-12.55,post.location.y+1.30,9.14))
        replace_beam(ob,a,b)
        joints.append({'brace':ob.name,'post':post.name,'start':list(a),'end':list(b)})
    # Below-deck triangular brackets terminate in actual joists and wall ledger.
    new(box('CWGallery masonry wall ledger',(-12.53,1,3.70),(.28,24,.30),wood,gallery,.009))
    floor_braces=sorted([ob for ob in gallery.objects if ob.name.startswith('Gallery knee brace')],key=lambda o:o.name)
    for ob,yy in zip(floor_braces,[-9,-5,-1,3,7,11]):
        tip=-10.83 if -6.15<yy<1.7 else -8.82
        a=Vector((-12.52,yy,2.68));b=Vector((tip,yy,3.69))
        replace_beam(ob,a,b)
        new(box('CWGallery bearing floor joist',((tip-12.52)/2,yy,3.77),(tip+12.52+.22,.24,.26),wood,gallery,.008))
    for yy,length in [(-8.475,4.65),(7.45,11.50)]:
        new(box('CWGallery outer floor girder',(-8.81,yy,3.73),(.26,length,.34),wood,gallery,.009))
    bpy.context.view_layer.update()
    return {'batchKeys':[('03','CW_Dark_structural_walnut'),('11','CW_Dark_structural_walnut')],
            'wallKneeConnections':joints,'headPlateUnderside':9.09,'postTop':9.17,
            'stairOpeningPreserved':True}

if __name__=='__main__':
    REPORT.mkdir(parents=True,exist_ok=True)
    result=apply_structure()
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ClockworkMaintenanceStreet.blend'))
    result['patch']=runpy.run_path(str(HERE/'refresh_decorative_lettering.py'))['patch_batches'](result['batchKeys'],REPORT)
    (REPORT/'structure-update.json').write_text(json.dumps(result,indent=2),encoding='utf8')
    print('GALLERY_STRUCTURE',json.dumps(result),flush=True)
