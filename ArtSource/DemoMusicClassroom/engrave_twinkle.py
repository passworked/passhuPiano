"""Blender mesh/curve engraving of the exact project MIDI, not a score image."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix

def build_score(scene, here):
    score=json.loads((here/'twinkle-score.json').read_text(encoding='utf-8'))
    ppq=score['ppq']
    notes=score['notes']
    assert len(notes)==90
    assert score['meters'][0]['numerator']==4 and score['meters'][0]['denominator']==4
    grouped={}
    for i,n in enumerate(notes):
        n['source_note_id']=i
        n['beat']=n['tick']/ppq
        n['notated_beats']=round((n['end']-n['tick'])/ppq)
        assert abs((n['end']-n['tick'])/ppq-n['notated_beats'])<.11
        assert n['notated_beats'] in (1,2)
        grouped.setdefault(n['tick'],[]).append(n)
    for group in grouped.values():
        top=max(group,key=lambda n:n['pitch'])
        for n in group:
            n['staff']='treble' if n is top else 'bass'
    assert sum(n['staff']=='treble' for n in notes)==42
    assert sum(n['staff']=='bass' for n in notes)==48

    collection=bpy.data.collections.new("Cover v2 - exact Twinkle grand staff")
    scene.collection.children.link(collection)
    music_font=bpy.data.fonts.load(str(here/'Fonts/Bravura.otf'))
    text_font=bpy.data.fonts.load(str(here.parents[1]/'Assets/UdonMidiPlayer/Fonts/NotoSansCJKsc-Regular.otf'))
    facing=Matrix(((-1,0,0),(0,0,1),(0,1,0))).to_euler()
    def v(p): return Vector((-p[0],-p[2],p[1]))
    def mat(name,color):
        m=bpy.data.materials.new(name)
        m.use_nodes=True
        shader=m.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value=(*color,1)
        shader.inputs['Roughness'].default_value=.95
        return m
    chalk=mat('Score - warm ivory chalk',(.79,.82,.71))
    staff_chalk=mat('Score - soft staff chalk',(.39,.45,.36))
    def link(obj):
        for c in list(obj.users_collection): c.objects.unlink(obj)
        collection.objects.link(obj)
    def text(name,body,x,y,size,font=music_font,align='LEFT',material=chalk):
        data=bpy.data.curves.new(name,'FONT')
        data.body=body
        data.font=font
        # Blender normalizes fonts by their global bounding box, rather than
        # preserving the SMuFL em. Measured in Blender 5.2: a .128-sized
        # Bravura black head is .00811565 m tall; restore one .032m staff space.
        data.size=size*(3.943 if font==music_font else 3.2)
        data.align_x=align
        data.extrude=.0003
        obj=bpy.data.objects.new(name,data)
        collection.objects.link(obj)
        obj.location=v((x,y,3.171))
        obj.rotation_euler=facing
        data.materials.append(material)
        return obj
    def line(name,x0,y0,x1,y1,width=.0019,material=chalk):
        start,end=v((x0,y0,3.178)),v((x1,y1,3.178))
        delta=end-start
        bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=width*.5,
            depth=delta.length,location=(start+end)*.5)
        obj=bpy.context.object
        obj.name=name
        obj.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
        obj.data.materials.append(material)
        link(obj)
        return obj
    def brace(x,low,high):
        h=high-low
        points=[(x+.025,high),(x-.018,high-.06*h),(x-.005,high-.40*h),
                (x-.032,low+.5*h),(x-.005,low+.40*h),(x-.018,low+.06*h),(x+.025,low)]
        data=bpy.data.curves.new('Grand staff brace','CURVE')
        data.dimensions='3D'
        data.bevel_depth=.0022
        data.bevel_resolution=2
        spline=data.splines.new('BEZIER')
        spline.bezier_points.add(len(points)-1)
        for point,(xx,yy) in zip(spline.bezier_points,points):
            point.co=v((xx,yy,3.175))
            point.handle_left_type='AUTO'
            point.handle_right_type='AUTO'
        obj=bpy.data.objects.new('Piano grand staff brace',data)
        collection.objects.link(obj)
        data.materials.append(chalk)

    text('Score title','No.0  小星星',-2.49,2.82,.13,text_font)
    text('Score author','作者：ChatGPT',2.55,2.82,.095,text_font,'RIGHT')
    # Metronome mark uses the music-font quarter head plus a geometric stem.
    text('Tempo quarter head','\ue0a4',-1.35,2.85,.08)
    line('Tempo stem',-1.327,2.853,-1.327,2.915,.0026)
    bpm=round(60000000/score['tempos'][0]['microseconds'])
    text('Tempo BPM','= '+str(bpm),-1.29,2.84,.070,text_font)
    spacing=.032
    x_left,x_notes,x_right=-2.50,-2.10,2.58
    bar_width=(x_right-x_notes)/6
    systems=[dict(treble=2.54,bass=2.25),dict(treble=1.94,bass=1.66)]
    for system,ys in enumerate(systems):
        top,bottom=ys['treble']+4*spacing,ys['bass']
        brace(x_left-.065,bottom,top)
        line('System start bar',x_left,bottom,x_left,top,.0027)
        for staff,y in ys.items():
            for i in range(5):
                line('Five-line '+staff,x_left,y+i*spacing,x_right,y+i*spacing,
                    .0015,staff_chalk)
            text('Treble clef' if staff=='treble' else 'Bass clef',
                '\ue050' if staff=='treble' else '\ue062',
                x_left+.035,y+(spacing if staff=='treble' else spacing*3),spacing*4)
            for pos in [spacing,spacing*3]:
                text('4/4 time signature','\ue084',x_left+.245,y+pos,spacing*4)
        for bar in range(6):
            start=x_notes+bar*bar_width
            text('Measure number',str(system*6+bar+1),start+.015,top+.035,.044,text_font)
            end=start+bar_width
            line('Measure bar',end,bottom,end,top,.0026)
        if system==1:
            line('Final thin bar',x_right-.021,bottom,x_right-.021,top,.0022)
            line('Final thick bar',x_right,bottom,x_right,top,.0055)

    natural={0:0,2:1,4:2,5:3,7:4,9:5,11:6}
    def degree(pitch): return (pitch//12-1)*7+natural[pitch%12]
    engraved=[]
    # Quarter/half glyphs use SMuFL's notehead origin at left, on the pitch.
    for tick,group in grouped.items():
        beat=tick/ppq
        measure=int(beat//4)
        system=measure//6
        bar=measure%6
        offset=beat%4
        center=x_notes+bar*bar_width+.085+offset*(bar_width-.15)/4
        for staff in ('treble','bass'):
            chord=[n for n in group if n['staff']==staff]
            if not chord: continue
            bottom=systems[system][staff]
            base_degree=degree(64 if staff=='treble' else 43)
            positions=[]
            ledgers=set()
            for n in chord:
                step=degree(n['pitch'])-base_degree
                y=bottom+step*spacing*.5
                positions.append(y)
                head=text('MIDI note '+str(n['source_note_id']),
                    '\ue0a3' if n['notated_beats']==2 else '\ue0a4',
                    center-spacing*.59,y,spacing*4)
                head['midi_pitch']=n['pitch']
                head['midi_tick']=n['tick']
                head['source_note_id']=n['source_note_id']
                head['notated_beats']=n['notated_beats']
                if step<0:
                    ledgers.update(range(-2,step-1,-2))
                elif step>8:
                    ledgers.update(range(10,step+1,2))
                engraved.append(dict(source_note_id=n['source_note_id'],measure=measure+1,
                    beat=offset,pitch=n['pitch'],duration=n['notated_beats'],staff=staff))
            for step in ledgers:
                yy=bottom+step*spacing*.5
                line('Pitch ledger',center-spacing*.86,yy,center+spacing*.86,yy,.0018)
            if staff=='treble':
                xx=center+spacing*.56
                line('Melody stem',xx,min(positions)+.003,xx,max(positions)+spacing*3.5,.0028)
            else:
                xx=center-spacing*.55
                line('Accompaniment chord stem',xx,max(positions)-.003,xx,min(positions)-spacing*3.5,.0028)
    assert sorted(n['source_note_id'] for n in engraved)==list(range(90))
    report=dict(source=score['source'],sha256=score['sha256'],
        title='No.0 小星星',author='ChatGPT',time_signature='4/4',bpm=bpm,
        complete_measures=12,source_notes=90,engraved_notes=len(engraved),
        melody_notes=42,accompaniment_notes=48,
        duration_policy='Quarter/half notes for MIDI 95-percent articulation gates; onsets and pitches unchanged',
        mapping=sorted(engraved,key=lambda n:n['source_note_id']))
    (here/'twinkle-engraving-map.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('EXACT_TWINKLE_ENGRAVED',len(engraved),'notes;',bpm,'BPM; 12 complete measures')
    return score
