"""Incremental source upgrade, executed by export_unity.py inside Blender.
Metres; Blender x=Unity x, z=up, y=Unity forward. No physics simulation.
"""
import bpy, math, json
from mathutils import Vector, Matrix
from fractions import Fraction
from math import gcd

for colname in ['14 | Working clock mechanism','15 | Closed clock tower']:
    old=bpy.data.collections.get(colname)
    if old:
        for ob in list(old.objects):bpy.data.objects.remove(ob,do_unlink=True)
        bpy.data.collections.remove(old)
CW_COL=bpy.data.collections.new('14 | Working clock mechanism');bpy.context.scene.collection.children.link(CW_COL)
cw_old=['East main wheel','East upper transfer','East foreground pinion','West return wheel','West lower escapement',
    'Machine green iron tower','Machine bronze bearing pedestal','Machine pedestal cross tie',
    'Pendulum central steel hanger','Great lenticular pendulum bob','Pendulum concentric detail','Pendulum adjustment nut',
    'Astronomical clock hand','Opal clock face','Rear tall plaster infill','Rear timber facade upright','Timber roof boarding']
for ob in list(bpy.context.scene.objects):
    if any(ob.name.startswith(prefix) for prefix in cw_old):bpy.data.objects.remove(ob,do_unlink=True)

cw_brass=bpy.data.materials['Old brushed brass'];cw_gold=bpy.data.materials['Polished brass wear']
cw_iron=bpy.data.materials['Aged petrol green cast iron'];cw_steel=bpy.data.materials['Dark gunmetal fastenings']
cw_wood=bpy.data.materials['Dark structural walnut'];cw_stone=bpy.data.materials['Warm limestone | mineral grain']
cw_plaster=bpy.data.materials['Mineral lime plaster'];cw_ivory=bpy.data.materials['Warm cream enamel']
cw_motion={}

def cw_link(ob):CW_COL.objects.link(ob);return ob
def cw_mesh(name,vs,fs,mat,group=None):
    data=bpy.data.meshes.new(name);data.from_pydata(vs,[],fs);data.materials.append(mat);data.update()
    ob=cw_link(bpy.data.objects.new(name,data))
    if group:ob['cw_motion_group']=group
    return ob
def cw_box(name,p,size,mat,group=None,bevel=.012):
    x,y,z=[v*.5 for v in size]
    vs=[(-x,-y,-z),(-x,-y,z),(-x,y,-z),(-x,y,z),(x,-y,-z),(x,-y,z),(x,y,-z),(x,y,z)]
    fs=[(0,2,6,4),(1,5,7,3),(0,4,5,1),(2,3,7,6),(0,1,3,2),(4,6,7,5)]
    ob=cw_mesh(name,vs,fs,mat,group);ob.location=p
    if bevel:
        mod=ob.modifiers.new('Machined edge','BEVEL');mod.width=bevel;mod.segments=2;mod.harden_normals=True
        mod=ob.modifiers.new('Weighted face normals','WEIGHTED_NORMAL');mod.keep_sharp=True
    return ob
def cw_beam(name,a,b,width,mat,group=None,depth=None):
    a,b=Vector(a),Vector(b);o=cw_box(name,(a+b)*.5,(width,depth or width,(b-a).length),mat,group,min(.009,width*.1))
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def cw_cyl(name,p,r,depth,mat,group=None,n=48):
    vs=[]
    for yy in [-depth/2,depth/2]:
        for i in range(n):
            a=i*math.tau/n;vs.append((r*math.cos(a),yy,r*math.sin(a)))
    fs=[tuple(range(n)),tuple(reversed(range(n,2*n)))]+[(i,i+n,(i+1)%n+n,(i+1)%n) for i in range(n)]
    ob=cw_mesh(name,vs,fs,mat,group);ob.location=p
    for poly in ob.data.polygons:
        if len(poly.vertices)==4:poly.use_smooth=True
    return ob
def cw_curve(name,points,r,mat,group=None):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.bevel_depth=r;cu.bevel_resolution=2
    sp=cu.splines.new('POLY');sp.points.add(len(points)-1)
    for q,p in zip(sp.points,points):q.co=(*p,1)
    ob=cw_link(bpy.data.objects.new(name,cu));cu.materials.append(mat)
    if group:ob['cw_motion_group']=group
    return ob
def cw_ring(name,p,r,width,depth,mat,group=None,n=64):
    x,y,z=p;vs=[]
    for yy in [-depth/2,depth/2]:
        for rr in [r-width,r]:
            for i in range(n):
                a=i*math.tau/n;vs.append((x+rr*math.cos(a),y+yy,z+rr*math.sin(a)))
    fs=[]
    for i in range(n):
        j=(i+1)%n
        fs += [(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),(i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)]
    return cw_mesh(name,vs,fs,mat,group)

def cw_intersections(a,ra,b,rb):
    a,b=Vector(a),Vector(b);d=(b-a).length;u=(b-a)/d
    t=(ra*ra-rb*rb+d*d)/(2*d);h=math.sqrt(max(0,ra*ra-t*t))
    mid=a+u*t;v=Vector((-u.y,u.x))
    return [mid+v*h,mid-v*h]

module=.16;pressure=math.radians(20)
main=Vector((8.6,6.25))
upper=main+Vector((math.cos(math.radians(113)),math.sin(math.radians(113))))*6.4
lower=main+Vector((math.cos(math.radians(230)),math.sin(math.radians(230))))*5.76
west=Vector((-7.5,8.2));westlower=west+Vector((0,-4.8))
bridge_a=west+Vector((1,1)).normalized()*6.4
bridge_b=max(cw_intersections(bridge_a,5.44,upper,4.48),key=lambda p:p.y)
escape=bridge_b+(Vector((0,12.1))-bridge_b).normalized()*3.2
centers=[main,upper,lower,west,westlower,bridge_a,bridge_b,escape]
counts=[52,28,20,40,20,40,28,20]
parents=[-1,0,0,5,3,6,1,6]
order=[0,1,2,6,5,3,4,7]
phases=[0.0]*8;rootratios=[Fraction(0)]*8;rootratios[0]=Fraction(1)
for i in order[1:]:
    parent=parents[i];n0=20 if i==7 else counts[parent];n1=counts[i]
    v=centers[i]-centers[parent];a=math.atan2(v.y,v.x)
    phases[i]=(((n0+n1)*a+n1*math.pi-math.pi-n0*phases[parent])/n1)%(math.tau/n1)
    rootratios[i]=-rootratios[parent]*Fraction(n0,n1)
ratios=[r/rootratios[7] for r in rootratios]
repeat=1
for ratio in ratios:repeat=repeat*ratio.denominator//gcd(repeat,ratio.denominator)

def cw_involute_gear(index,p,n,depth,group,suffix=''):
    rp=module*n/2;rb=rp*math.cos(pressure);ra=rp+module;rf=rp-1.25*module
    invpitch=math.tan(pressure)-pressure;half=math.pi/(2*n)-.008*module/rp
    def flank(r):
        a=math.acos(min(1,rb/max(rb,r)));return half+invpitch-(math.tan(a)-a)
    outline=[]
    for tooth in range(n):
        a=tooth*math.tau/n
        radii=sorted(set([rf,max(rf,rb)]+[max(rb,rf)+(ra-max(rb,rf))*k/5 for k in range(1,6)]))
        for r in radii:outline.append((r,a-flank(r)))
        for k in [1,2]:outline.append((ra,a-flank(ra)+2*flank(ra)*k/3))
        for r in reversed(radii):outline.append((r,a+flank(r)))
        end=a+math.tau/n-flank(rf)
        for k in [1,2]:outline.append((rf,a+flank(rf)+(end-a-flank(rf))*k/3))
    x,y,z=p;v=[];N=len(outline);inner=rp*.69
    for yy in [-depth/2,depth/2]:
        for kind in ['inner','outer']:
            for r,a in outline:
                rr=inner if kind=='inner' else r;v.append((x+rr*math.cos(a),y+yy,z+rr*math.sin(a)))
    faces=[]
    for k in range(N):
        j=(k+1)%N
        faces += [(k,j,N+j,N+k),(2*N+k,3*N+k,3*N+j,2*N+j),(k,2*N+k,2*N+j,j),(N+k,N+j,3*N+j,3*N+k)]
    ob=cw_mesh('CW Involute '+str(index)+suffix,v,faces,cw_brass,group)
    bevel=ob.modifiers.new('Small tooth edge bevel','BEVEL');bevel.width=.007;bevel.segments=1;bevel.harden_normals=True
    norm=ob.modifiers.new('Recomputed flat faces and bevel normals','WEIGHTED_NORMAL');norm.keep_sharp=True
    for j in range(6 if n<40 else 8):
        a=j*math.tau/(6 if n<40 else 8)
        cw_beam('CW Cast gear spoke',(x+rp*.16*math.cos(a),y,z+rp*.16*math.sin(a)),(x+rp*.75*math.cos(a),y,z+rp*.75*math.sin(a)),max(.06,rp*.07),cw_brass,group,depth*.72)
    cw_cyl('CW Gear hub',(x,y,z),rp*.17,depth*1.35,cw_iron,group)
    cw_cyl('CW Polished axle boss',(x,y-depth*.78,z),rp*.105,.10,cw_gold,group)
    for j in range(8):
        a=j*math.tau/8
        cw_cyl('CW Rim bolt',(x+rp*.80*math.cos(a),y-depth*.51,z+rp*.80*math.sin(a)),max(.018,rp*.009),.03,cw_gold,group,n=8)

for i in range(8):
    group='Gear'+str(i).zfill(2);p=centers[i];plane=9.05 if i==7 else 7.2
    pivot=(p.x,plane,p.y)
    cw_motion[group]={'name':group,'pivot':unit(pivot),'phase':math.degrees(phases[i]),'teeth':counts[i],
        'parent':parents[i],'parentContactTeeth':20 if i==7 else (counts[parents[i]] if parents[i]>=0 else 0),
        'ratioNumerator':ratios[i].numerator,'ratioDenominator':ratios[i].denominator,'plane':plane,'blenderPivot':pivot}
    cw_involute_gear(i,pivot,counts[i],.42 if i==7 else .54,group)
    cw_cyl('CW Driven shaft',(p.x,8.6,p.y),.085,3.0,cw_steel,group,n=32)
    # Each axle is held by a real bearing and crossmember, supported behind the moving wheels.
    cw_cyl('CW Stationary bearing outer',(p.x,10.18,p.y),.23,.30,cw_iron)
    cw_cyl('CW Bearing brass liner',(p.x,10.0,p.y),.15,.07,cw_gold)
    cw_box('CW Bearing pedestal',(p.x,10.34,p.y-.34),(.62,.60,.55),cw_iron)
    for dx in [-.38,.38]:
        cw_beam('CW Gear frame column',(p.x+dx,10.5,.25),(p.x+dx,10.5,p.y),.12,cw_iron)
    cw_beam('CW Gear frame bearing bridge',(p.x-.52,10.4,p.y-.24),(p.x+.52,10.4,p.y-.24),.16,cw_iron)
    cw_box('CW Gear frame foot',(p.x,10.5,.22),(1.2,.75,.38),cw_stone)
# Compound rear wheel shares the upper bridge shaft, but meshes on its own depth plane.
cw_involute_gear(6,(bridge_b.x,9.05,bridge_b.y),20,.42,'Gear06',' rear compound 20T')

# Thirty-tooth escape wheel on the rear branch axle, with alternating locking pallets.
ex,ez=escape.x,escape.y;ey=9.78;N=30;outline=[]
for i in range(N):
    a=i*math.tau/N+math.radians(45-2.16)-phases[7]
    outline.extend([(.62,a),(.77,a+.18*math.tau/N),(.64,a+.40*math.tau/N)])
vs=[]
for yy in [-.045,.045]:
    for r,a in outline:vs.append((ex+r*math.cos(a),ey+yy,ez+r*math.sin(a)))
n=len(outline);fs=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
cw_mesh('CW Thirty-tooth escape wheel',vs,fs,cw_gold,'Gear07')

# Suspension spring, rigid mounting, pendulum rod, bob, crutch and anchor.
pend_z=13.29246;anchor_z=ez+1.0;pend_y=8.4
pend_p=(ex,pend_y,pend_z);anchor_p=(ex,ey,anchor_z)
cw_motion['Pendulum']={'name':'Pendulum','pivot':unit(pend_p),'phase':0,'blenderPivot':pend_p}
cw_motion['Anchor']={'name':'Anchor','pivot':unit(anchor_p),'phase':0,'blenderPivot':anchor_p}
for dx in [-.8,.8]:
    cw_beam('CW Suspension mounting beam',(ex+dx,8.4,13.85),(ex+dx,12.2,13.85),.17,cw_iron)
    cw_box('CW Suspension rear bolted plate',(ex+dx,12.13,13.85),(.4,.18,.5),cw_iron)
cw_beam('CW Suspension crosshead',(ex-.95,8.4,13.85),(ex+.95,8.4,13.85),.20,cw_iron)
cw_box('CW Fixed suspension clamp',(ex,pend_y,13.66),(.33,.25,.25),cw_brass)
for dx in [-.075,.075]:cw_box('CW Flexible suspension steel strip',(ex+dx,pend_y,(13.54+pend_z)/2),(.035,.018,13.54-pend_z),cw_steel,bevel=.001)
cw_box('CW Lower suspension clamp',(ex,pend_y,pend_z-.08),(.27,.23,.16),cw_brass,'Pendulum')
cw_beam('CW Pendulum steel rod',(ex,pend_y,pend_z),(ex,pend_y,3.01),.085,cw_steel,'Pendulum',.11)
profile=[(0,-.19),(.45,-.24),(1.02,-.20),(1.28,-.08),(1.32,0),(1.28,.08),(1.02,.20),(.45,.24),(0,.19)]
vs=[]
for rr,yy in profile:
    for j in range(80):
        a=j*math.tau/80;vs.append((ex+rr*math.cos(a),pend_y+yy,4.35+rr*math.sin(a)))
fs=[]
for j in range(len(profile)-1):
    for k in range(80):fs.append((j*80+k,j*80+(k+1)%80,(j+1)*80+(k+1)%80,(j+1)*80+k))
ob=cw_mesh('CW Suspended lenticular pendulum bob',vs,fs,cw_gold,'Pendulum')
for poly in ob.data.polygons:poly.use_smooth=True
cw_ring('CW Bob engraved ring',(ex,pend_y-.242,4.35),.96,.016,.015,cw_brass,'Pendulum')
cw_box('CW Pendulum rating nut',(ex,pend_y,2.94),(.29,.28,.18),cw_brass,'Pendulum',.03)
cw_beam('CW Fixed anchor bearing bracket',(ex-.45,10.55,anchor_z),(ex+.45,10.55,anchor_z),.15,cw_iron)
cw_cyl('CW Anchor fixed bearing',(ex,10.20,anchor_z),.17,.65,cw_iron)
cw_cyl('CW Anchor arbor',(ex,9.19,anchor_z),.065,2.05,cw_steel,'Anchor',n=24)
for s in [-1,1]:
    cw_beam('CW Anchor curved arm',(ex,ey,anchor_z),(ex+s*.53,ey,ez+.64),.085,cw_steel,'Anchor',.11)
    pallet=cw_box('CW Locking pallet',(ex+s*.53,ey-.035,ez+.53),(.14,.15,.12),cw_gold,'Anchor',.008)
    pallet.rotation_euler.y=math.radians(-45 if s>0 else -135)
fork_z=11.35
cw_beam('CW Crutch rod',(ex,9.18,anchor_z),(ex,9.18,fork_z),.04,cw_steel,'Anchor',.045)
cw_beam('CW Crutch fork rear bridge',(ex-.12,9.18,fork_z),(ex+.12,9.18,fork_z),.045,cw_brass,'Anchor')
for s in [-1,1]:cw_beam('CW Crutch fork embracing pendulum',(ex+s*.12,9.18,fork_z),(ex+s*.12,8.31,fork_z),.035,cw_brass,'Anchor')

# Civil-time hands: individually pivoted around the existing dial arbor.
for name,length,width,y,mat in [('HourHand',3.35,.14,11.37,cw_iron),('MinuteHand',4.45,.09,11.28,cw_iron),('SecondHand',4.95,.035,11.18,cw_gold)]:
    p=(1.65,y,10.8);cw_motion[name]={'name':name,'pivot':unit(p),'phase':0,'blenderPivot':p}
    cw_beam('CW '+name+' blade',(1.65,y,10.46),(1.65,y,10.8+length),width,mat,name,.045)
    cw_cyl('CW '+name+' hub',p,.19 if name!='SecondHand' else .10,.07,mat,name,n=32)
cw_cyl('CW Clock arbor centre cap',(1.65,11.08,10.8),.13,.12,cw_gold,n=32)

# Weight barrel, hanging weight and a cable path, all behind the safety boundary.
cw_cyl('CW Winding barrel',(8.6,9.5,6.25),.44,.65,cw_wood,n=64)
for y in [9.17,9.83]:cw_cyl('CW Winding barrel rim',(8.6,y,6.25),.49,.06,cw_brass,n=64)
cw_cyl('CW Overhead cable pulley',(11.6,10.1,12.9),.28,.13,cw_brass,n=48)
cw_curve('CW Weight drive cable',[(9.02,9.5,6.25),(9.02,9.5,12.9),(11.6,10.1,13.18),(11.88,10.1,12.9),(11.88,10.1,3.8)],.012,cw_steel)
cw_box('CW Clock driving weight',(11.88,10.1,2.95),(.52,.52,1.70),cw_iron,bevel=.07)
cw_beam('CW Pulley support',(11.6,10.1,13.20),(11.6,12.0,13.7),.10,cw_iron)

# Source rest pose has the exact mesh phases; exporter removes that phase from
# vertex data and restores it on the Unity pivot so normals and pivots stay clean.
bpy.context.view_layer.update()
for ob in list(CW_COL.objects):
    group=ob.get('cw_motion_group')
    if group and cw_motion[group]['phase']:
        pivot=Vector(cw_motion[group]['blenderPivot']);phase=math.radians(cw_motion[group]['phase'])
        ob.matrix_world=Matrix.Translation(pivot)@Matrix.Rotation(-phase,4,'Y')@Matrix.Translation(-pivot)@ob.matrix_world

CW_COL=bpy.data.collections.new('15 | Closed clock tower');bpy.context.scene.collection.children.link(CW_COL)
cx,cz=1.65,10.8;aperture=5.85
outline=[(-13.15,-.5),(13.15,-.5),(13.15,14.65),(0,18.30),(-13.15,14.65)]
angles=[i*math.tau/128 for i in range(128)]+[math.atan2(z-cz,x-cx)%math.tau for x,z in outline]
angles=sorted(set(angles))
def cw_ray_boundary(a):
    dx,dz=math.cos(a),math.sin(a);distances=[]
    for j in range(len(outline)):
        x0,z0=outline[j];x1,z1=outline[(j+1)%len(outline)];ex0,ez0=x1-x0,z1-z0
        den=dx*ez0-dz*ex0
        if abs(den)<1e-9:continue
        t=((x0-cx)*ez0-(z0-cz)*ex0)/den;u=((x0-cx)*dz-(z0-cz)*dx)/den
        if t>0 and -.00001<=u<=1.00001:distances.append(t)
    return min(distances)
vs=[]
for y in [12.97,13.25]:
    for kind in ['inner','outer']:
        for a in angles:
            r=aperture if kind=='inner' else cw_ray_boundary(a)
            vs.append((cx+r*math.cos(a),y,cz+r*math.sin(a)))
N=len(angles);fs=[]
for i in range(N):
    j=(i+1)%N
    fs += [(i,j,N+j,N+i),(2*N+i,3*N+i,3*N+j,2*N+j),(i,2*N+i,2*N+j,j),(N+i,N+j,3*N+j,3*N+i)]
cw_mesh('CW Rear gable wall with genuine circular aperture',vs,fs,cw_plaster)
cw_box('CW Closed front wall',(0,-12.96,7.1),(26.3,.35,15.2),cw_plaster)
cw_mesh('CW Front gable infill',[(x,y,z) for y in [-13.135,-12.785] for x,z in [(-13.15,14.6),(13.15,14.6),(0,18.3)]],[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],cw_plaster)
for sign in [-1,1]:
    roof=cw_box('CW Complete pitched roof',(sign*6.4,.1,16.4),(13.1,26.7,.16),cw_wood,bevel=0)
    roof.rotation_euler.y=sign*math.radians(16)
for x in [-13.14,13.14]:cw_box('CW Continuous side enclosure',(x,.1,7.1),(.30,26.5,15.2),cw_plaster)
cw_box('CW Roof ridge closure',(0,.1,18.17),(.60,26.7,.35),cw_wood)
for x in [-12.85,12.85]:cw_box('CW Eaves closure',(x,.1,14.48),(.68,26.7,.58),cw_wood)
for x in [-10,-6,-2,2,6,10]:
    half=math.sqrt(max(0,aperture*aperture-(x-cx)**2)) if abs(x-cx)<aperture else 0
    pieces=[(0,cz-half),(cz+half,17.1)] if half else [(0,17.1)]
    for lo,hi in pieces:
        if hi>lo:cw_box('CW Rear timber outside dial opening',(x,12.8,(lo+hi)/2),(.22,.25,hi-lo),cw_wood)
for x in [-12,-8,-4,0,4,8,12]:cw_box('CW Front wall timber upright',(x,-12.72,7),(.23,.22,14),cw_wood)
cw_box('CW Closed entry door panel',(0,-12.67,1.4),(2.15,.12,2.8),cw_iron)
for z in [.35,.9,1.45,2.0,2.55]:cw_box('CW Entry door iron strap',(0,-12.59,z),(2.05,.06,.06),cw_brass)
cw_cyl('CW Closed entry door handle',(.7,-12.51,1.25),.07,.12,cw_gold,n=24)
# Single flat pane; no layered cylinder, refraction, screen grab or shadow caster.
glass=bpy.data.materials.get('Clockwork lightweight dial glass') or bpy.data.materials.new('Clockwork lightweight dial glass')
glass.diffuse_color=(.86,.82,.68,.32)
vs=[(cx,12.1,cz)]+[(cx+5.75*math.cos(i*math.tau/128),12.1,cz+5.75*math.sin(i*math.tau/128)) for i in range(128)]
ob=cw_mesh('CW Single layer translucent clock dial',vs,[(0,i+1,(i+1)%128+1) for i in range(128)],glass)
ob['cw_dial_glass']=True

cw_mechanism={'module':module,'pressureAngle':20,'gears':[cw_motion['Gear'+str(i).zfill(2)] for i in range(8)],
    'motions':[v for k,v in cw_motion.items() if not k.startswith('Gear')],
    'pendulumLength':pend_z-4.35,'pendulumPivot':unit(pend_p),'anchorPivot':unit(anchor_p),
    'crutchRatio':(pend_z-fork_z)/(anchor_z-fork_z),'escapeTeeth':30,'cycleBeats':repeat*60,
    'dialCenter':[1.65,10.8,12.1],'apertureRadius':aperture,
    'sourceReferences':['https://mechanics.ju.se/MachineElements/spur-gears.html','https://www.metmuseum.org/essays/european-clocks-in-the-seventeenth-and-eighteenth-century'],
    'lighting':'One moving Point Light outside the genuine rear aperture; client-local facade-oriented day/night trajectory.'}
for item in cw_mechanism['gears']+cw_mechanism['motions']:item.pop('blenderPivot',None)
(OUT/'mechanism-manifest.json').write_text(json.dumps(cw_mechanism,indent=2),encoding='utf8')
print('CLOCKWORK_MECHANISM_GEOMETRY',json.dumps({'gearAxles':8,'spurWheels':9,'escapeWheels':1,'pendulumLength':cw_mechanism['pendulumLength'],'cycleBeats':cw_mechanism['cycleBeats']}),flush=True)
