"""Read-only audit of gallery joinery and stair-opening clearance in the editable source."""
import bpy, json
from pathlib import Path
from mathutils import Vector

def bounds(ob):
    points=[ob.matrix_world@Vector(p) for p in ob.bound_box]
    return [min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]

def contains(ob,p,tolerance=.015):
    lo,hi=bounds(ob)
    return all(lo[i]-tolerance<=p[i]<=hi[i]+tolerance for i in range(3))

studs=sorted([o for o in bpy.context.scene.objects if o.name.startswith('West gallery wall oak stud')],key=lambda o:o.location.y)
knees=sorted([o for o in bpy.context.scene.objects if o.name.startswith('West overhead knee brace')],key=lambda o:o.name)
header=next(o for o in bpy.context.scene.objects if o.name.startswith('CWGallery continuous wall head plate'))
connections=[]
for knee,post in zip(knees,studs):
    zs=[v.co.z for v in knee.data.vertices]
    a=knee.matrix_world@Vector((0,0,min(zs)))
    b=knee.matrix_world@Vector((0,0,max(zs)))
    connections.append({'name':knee.name,'postContact':contains(post,a),'headerContact':contains(header,b)})
    assert connections[-1]['postContact'] and connections[-1]['headerContact'],connections[-1]
    assert bounds(post)[0][2]<=4.095 and bounds(post)[1][2]>=bounds(header)[0][2]
opening_members=[]
for o in bpy.context.scene.objects:
    if not (o.name.startswith(('Gallery knee brace','CWGallery bearing floor joist'))):continue
    lo,hi=bounds(o)
    if hi[1]>-6.15 and lo[1]<1.7:
        opening_members.append({'name':o.name,'clearance':-10.65-hi[0]})
        assert hi[0]<-10.65,opening_members[-1]
result={'wallKnees':connections,'stairOpeningMembers':opening_members,'passed':True}
out=Path(__file__).resolve().parents[2]/'Logs/ClockworkStructureBake/source-audit.json'
out.write_text(json.dumps(result,indent=2),encoding='utf8')
print('GALLERY_AUDIT',json.dumps(result),flush=True)
