"""Read-only local source geometry check, including near-coplanar overlap.

Run in Blender with -- label (e.g. -- before / -- after). This checks the first
shop and its immediate surroundings, not a whole-world or VR-client guarantee.
"""
import bpy
import json
import math
import sys
from pathlib import Path
from collections import defaultdict, Counter
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Logs/ClockworkShopDetail'
LOW=(-7.85,3.35,-.02)
HIGH=(-3.55,6.65,4.15)
TOL=.0005
GRID=.15


def intersect(a,b):
    return all(a[0][i]<=b[1][i]+1e-8 and b[0][i]<=a[1][i]+1e-8 for i in range(len(a[0])))


def cross(a,b,c):
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def overlap(a,b):
    if cross(b[0],b[1],b[2])<0:b=list(reversed(b))
    p=list(a)
    for c,d in zip(b,b[1:]+b[:1]):
        old,p=p,[]
        if not old:return 0
        for v,w in zip(old,old[1:]+old[:1]):
            cv,cw=cross(c,d,v),cross(c,d,w)
            if cv>=-1e-10:p.append(v)
            if (cv<0)<(cw<0) or (cw<0)<(cv<0):
                t=cv/(cv-cw)
                p.append((v[0]+(w[0]-v[0])*t,v[1]+(w[1]-v[1])*t))
    return abs(sum(p[i][0]*p[(i+1)%len(p)][1]-p[i][1]*p[(i+1)%len(p)][0] for i in range(len(p))))*.5 if p else 0


def main():
    label=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'after'
    if label=='unity':
        # Consume an Editor mesh snapshot to include independent scene dressing,
        # not just Blender source. Snapshot vertices use Blender-compatible XYZ.
        for ob in list(bpy.context.scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
        snapshot=json.loads((OUT/'unity-surface-snapshot.json').read_text(encoding='utf8'))
        for item in snapshot:
            data=bpy.data.meshes.new(item['name']);vs=item['vertices']
            data.from_pydata(vs,[],[(i,i+1,i+2) for i in range(0,len(vs),3)])
            ob=bpy.data.objects.new(item['name'],data);bpy.context.scene.collection.objects.link(ob)
        bpy.context.view_layer.update()
    dg=bpy.context.evaluated_depsgraph_get()
    buckets=defaultdict(list)
    findings=defaultdict(lambda:{'trianglePairs':0,'maxProjectedOverlapM2':0,'minPlaneGapM':1})
    contacts=defaultdict(int)
    closure=[]
    count=0
    objects=set()
    for ob in bpy.context.scene.objects:
        if ob.type not in {'MESH','CURVE','FONT'} or ob.hide_render:continue
        if any(c.name.startswith(('09 |','10 |','13 |')) for c in ob.users_collection):continue
        ev=ob.evaluated_get(dg)
        me=ev.to_mesh()
        if not me:continue
        world=[ob.matrix_world@v.co for v in me.vertices]
        if not world:
            ev.to_mesh_clear();continue
        lo=[min(v[i] for v in world) for i in range(3)]
        hi=[max(v[i] for v in world) for i in range(3)]
        if not intersect((lo,hi),(LOW,HIGH)):
            ev.to_mesh_clear();continue
        me.calc_loop_triangles()
        objects.add(ob.name)
        if ob.get('cw_workshop_front_detail') or ob.name=='Little workshop gabled roof' or (ob.get('cw_shop_vine') and ob.type=='MESH'):
            edges=Counter(tuple(sorted((p.vertices[i],p.vertices[(i+1)%len(p.vertices)]))) for p in me.polygons for i in range(len(p.vertices)))
            closure.append({'object':ob.name,'boundaryEdges':sum(n==1 for n in edges.values()),'nonManifoldEdges':sum(n>2 for n in edges.values())})
        for ti,tri in enumerate(me.loop_triangles):
            points=[world[i] for i in tri.vertices]
            lo=[min(v[i] for v in points) for i in range(3)]
            hi=[max(v[i] for v in points) for i in range(3)]
            if not intersect((lo,hi),(LOW,HIGH)):continue
            normal=(points[1]-points[0]).cross(points[2]-points[0])
            if normal.length<1e-10:continue
            normal.normalize()
            axis=max(range(3),key=lambda i:abs(normal[i]))
            orientation=-1 if normal[axis]<0 else 1
            if orientation<0:normal=-normal
            distance=normal.dot(points[0])
            plane=tuple(round(v,4) for v in normal)
            axes=[i for i in range(3) if i!=axis]
            projected=[(p[axes[0]],p[axes[1]]) for p in points]
            bbox=([min(p[i] for p in projected) for i in range(2)],[max(p[i] for p in projected) for i in range(2)])
            cell=math.floor(distance/TOL)
            cells=[(u,v)
                   for u in range(math.floor(max(bbox[0][0],LOW[axes[0]])/GRID),math.floor(min(bbox[1][0],HIGH[axes[0]])/GRID)+1)
                   for v in range(math.floor(max(bbox[0][1],LOW[axes[1]])/GRID),math.floor(min(bbox[1][1],HIGH[axes[1]])/GRID)+1)]
            seen=set()
            for near in [cell-1,cell,cell+1]:
                for u,v in cells:
                    for item in buckets[(plane,near,u,v)]:
                        idx,name,other,d,n,otherbox,otherorientation=item
                        if idx in seen:continue
                        seen.add(idx)
                        if abs(distance-d)>TOL or normal.dot(n)<.999999 or not intersect(bbox,otherbox):continue
                        area=overlap(projected,other)
                        if area>1e-6:
                            key=tuple(sorted([ob.name,name]))
                            if orientation!=otherorientation:
                                contacts[key]+=1
                                continue
                            f=findings[key];f['trianglePairs']+=1;f['maxProjectedOverlapM2']=max(f['maxProjectedOverlapM2'],area);f['minPlaneGapM']=min(f['minPlaneGapM'],abs(distance-d))
            item=(count,ob.name,projected,distance,normal.copy(),bbox,orientation)
            for u,v in cells:buckets[(plane,cell,u,v)].append(item)
            count+=1
        ev.to_mesh_clear()
    result={'scope':'First workshop and immediate surroundings','regionBlenderXYZ':[LOW,HIGH],
            'planeGapToleranceM':TOL,'overlapAreaThresholdM2':1e-6,'testedObjects':len(objects),'testedTriangles':count,
            'coplanarOverlapPairs':[dict(objects=k,**v) for k,v in sorted(findings.items())],
            'opposingFaceContactPairs':[{'objects':k,'trianglePairs':v} for k,v in sorted(contacts.items())],
            'closedDetailObjects':closure,'boundaryEdgeTotal':sum(o['boundaryEdges'] for o in closure),
            'nonManifoldEdgeTotal':sum(o['nonManifoldEdges'] for o in closure)}
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/('surface-audit-'+label+'.json')).write_text(json.dumps(result,indent=2),encoding='utf8')
    print('SURFACE_AUDIT',json.dumps({k:v for k,v in result.items() if k not in ['closedDetailObjects']}),flush=True)


if __name__=='__main__':main()
