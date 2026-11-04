using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiStringsBuilder
    {
        private const string Folder="Assets/UdonMidiPlayer/Models/Strings";
        private const string Audio="Assets/UdonMidiPlayer/Audio/Strings";
        private static readonly string[] BankNames={"SoloViolin","ViolinSection","ViolaSection","CelloSection","Contrabass"};
        [Serializable] private class Mapping { public Region[] regions; }
        [Serializable] private class Region
        {
            public string bank,attack,loop,attackSha256,loopSha256;
            public int articulation,root,releaseMs;
            public float peak;
        }

        [MenuItem("Tools/Music MIDI Player/Install or Update Strings")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=MidiInstrumentMigration.MainScene) throw new InvalidOperationException("Open the merged main scene first.");
            var players=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MidiPlayerFacade>(true)).ToArray();
            if(players.Length!=1) throw new InvalidOperationException("Expected one merged player.");
            var player=players[0];
            if(player.instrumentRoots.Length!=3 && player.instrumentRoots.Length!=8) throw new InvalidOperationException("Unexpected instrument configuration.");
            if(!File.Exists(Audio+"/BANK-MAPPING.json")) throw new FileNotFoundException("Run Tools/prepare_strings_bank.py first.");
            MidiPlayerBuilder.EnsurePrograms();
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var map=JsonUtility.FromJson<Mapping>(File.ReadAllText(Audio+"/BANK-MAPPING.json"));
            foreach(string path in map.regions.SelectMany(r=>new[]{r.attack,r.loop}).Where(x=>!string.IsNullOrEmpty(x)).Distinct())
            {
                var importer=AssetImporter.GetAtPath(path) as AudioImporter;
                if(importer==null) throw new InvalidOperationException("Audio not imported: "+path);
                var settings=importer.defaultSampleSettings;
                settings.loadType=AudioClipLoadType.DecompressOnLoad; settings.compressionFormat=AudioCompressionFormat.PCM;
                settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData=true;
                importer.defaultSampleSettings=settings; importer.loadInBackground=false; importer.forceToMono=false;
                importer.SaveAndReimport();
            }
            Transform root=player.transform.root;
            var pool=Component<StringsVoicePool>(Child(root.Find("Runtime"),"StringsAudio"));
            pool.output=player.voicePool; pool.clock=player.clock;
            player.clock.strings=pool;
            if(pool.channelSections.Length!=16) pool.channelSections=new[]{0,1,2,3,4,0,0,0,0,0,0,0,0,0,0,0};
            pool.banks=new StringsBankData[5];
            for(int b=0;b<5;b++)
            {
                var data=Component<StringsBankData>(Child(root.Find("Data"),BankNames[b]+"Bank"));
                var rows=map.regions.Where(r=>r.bank==BankNames[b]).OrderBy(r=>r.articulation).ThenBy(r=>r.root).ToArray();
                data.bankId=BankNames[b];
                data.attacks=rows.Select(r=>AssetDatabase.LoadAssetAtPath<AudioClip>(r.attack)).ToArray();
                data.loops=rows.Select(r=>string.IsNullOrEmpty(r.loop)?null:AssetDatabase.LoadAssetAtPath<AudioClip>(r.loop)).ToArray();
                data.roots=rows.Select(r=>r.root).ToArray(); data.articulations=rows.Select(r=>r.articulation).ToArray();
                data.releaseMs=rows.Select(r=>r.releaseMs).ToArray(); data.peaks=rows.Select(r=>r.peak).ToArray(); data.gains=rows.Select(r=>1f).ToArray();
                data.pitchToRegion=new int[384];
                for(int style=0;style<3;style++) for(int pitch=0;pitch<128;pitch++)
                    data.pitchToRegion[style*128+pitch]=Enumerable.Range(0,rows.Length).Where(i=>rows[i].articulation==style)
                        .OrderBy(i=>Math.Abs(pitch-rows[i].root)).ThenBy(i=>rows[i].root).First();
                AudioEnergyBaker.Bake(data);
                data.bankHash=BankHash(data);
                if(!data.IsValid()) throw new InvalidOperationException("Invalid strings bank: "+data.bankId);
                pool.banks[b]=data;
            }
            player.voicePool.strings=pool;
            Array.Resize(ref player.instrumentRoots,8); Array.Resize(ref player.instrumentKeyboards,8);
            Array.Resize(ref player.instrumentPedals,8); Array.Resize(ref player.instrumentWaterfalls,8);
            Array.Resize(ref player.instrumentBanks,8); Array.Resize(ref player.instrumentStrings,8);
            string[] names={"SoloViolin","Viola","Cello","Contrabass","StringsEnsemble"};
            for(int mode=0;mode<5;mode++)
            {
                Transform presentation=Child(root.Find("Presentation"),names[mode]);
                var view=Component<StringsView>(presentation);
                view.facade=player; view.voices=pool;
                int count=mode==4?5:1;
                view.bodies=new Transform[count]; view.bows=new Transform[count]; view.contacts=new Transform[count];
                view.strings=new Renderer[count*4]; view.openNotes=new int[count*4];
                view.stringLengths=new float[count]; view.nutPositions=new float[count];
                for(int i=0;i<count;i++)
                {
                    int kind=mode==4?Math.Max(0,i-1):mode;
                    Transform stage=Child(presentation,"Section"+i);
                    stage.localPosition=new Vector3(mode==4?(i-2)*.65f:0,kind<2?1.1f:kind==2?.65f:.92f,mode==4?Mathf.Abs(i-2)*.12f:0);
                    stage.localRotation=Quaternion.Euler(0,180+(mode==4?(i-2)*-8:0),kind<2?-26:0);
                    stage.localScale=Vector3.one*(kind==0?1:kind==1?1.12f:kind==2?1.9f:2.7f);
                    BuildInstrument(stage,kind);
                    view.bodies[i]=stage; view.bows[i]=stage.Find("Bow"); view.contacts[i]=stage.Find("Contact");
                    int[] open=kind==0?new[]{55,62,69,76}:kind<3?new[]{kind==1?48:36,kind==1?55:43,kind==1?62:50,kind==1?69:57}:new[]{28,33,38,43};
                    for(int s=0;s<4;s++) { view.strings[i*4+s]=stage.Find("String"+s).GetComponent<Renderer>(); view.openNotes[i*4+s]=open[s]; }
                    view.stringLengths[i]=.395f; view.nutPositions[i]=.35f;
                }
                player.instrumentRoots[mode+3]=presentation.gameObject;
                player.instrumentKeyboards[mode+3]=null; player.instrumentWaterfalls[mode+3]=null; player.instrumentPedals[mode+3]=null;
                // The transport retains the existing clock/tail contract; dedicated banks live on StringsAudio.
                player.instrumentBanks[mode+3]=player.library.bank; player.instrumentStrings[mode+3]=view;
                presentation.gameObject.SetActive(player.selectedInstrument==mode+3);
            }
            UpdatePresentation(player);
            MidiPlayerUIBuilder.ConfigureInstruments(player.playerUI);
            MidiPerformanceBuilder.Configure(player);
            MidiInstrumentMigration.ApplyToneChangesTo(player);
            foreach(var behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            { UdonSharpEditorUtility.CopyProxyToUdon(behaviour,ProxySerializationPolicy.All); EditorUtility.SetDirty(behaviour); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        public static string BankHash(StringsBankData bank)
        {
            AudioEnergyBaker.Validate(AudioEnergyBaker.Clips(bank), bank.energyStarts, bank.energyCounts, bank.energyValues);
            var text=new System.Text.StringBuilder(bank.bankId+":strings-v1:" + AudioEnergyBaker.Format);
            for(int i=0;i<bank.attacks.Length;i++)
            {
                text.Append(':').Append(bank.roots[i]).Append(':').Append(bank.articulations[i]).Append(':').Append(bank.releaseMs[i]);
                text.Append(':').Append(bank.gains[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                foreach(var clip in new[]{bank.attacks[i],bank.loops[i]}) if(clip!=null)
                { string path=AssetDatabase.GetAssetPath(clip); text.Append(':').Append(MidiContentHash.Bytes(File.ReadAllBytes(path))).Append(':').Append(MidiContentHash.Bytes(File.ReadAllBytes(path+".meta"))); }
            }
            text.Append(AudioEnergyBaker.Fingerprint(bank.energyStarts, bank.energyCounts, bank.energyValues));
            foreach(int r in bank.pitchToRegion) text.Append(':').Append(r);
            return MidiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        }

        public static void UpdateModelGeometry(MidiPlayerFacade player)
        {
            for(int mode=0;mode<5;mode++)
            {
                var view=player.GetStringsView(mode+3);
                if(view==null) continue;
                for(int i=0;i<view.bodies.Length;i++)
                {
                    int kind=mode==4?Math.Max(0,i-1):mode;
                    Transform stage=view.bodies[i];
                    stage.localPosition=new Vector3(mode==4?(i-2)*.65f:0,kind<2?1.1f:kind==2?.65f:.92f,mode==4?Mathf.Abs(i-2)*.12f:0);
                    stage.localRotation=Quaternion.Euler(0,180+(mode==4?(i-2)*-8:0),kind<2?-26:0);
                    BuildInstrument(stage,kind);
                }
            }
        }

        // Incremental effect update; only removes the discarded strings note-window objects.
        public static void UpdatePresentation(MidiPlayerFacade player)
        {
            var contact=Material("StringsContact",Color.cyan);
            contact.shader=Shader.Find("Unlit/Color"); EditorUtility.SetDirty(contact);
            var spark=AssetDatabase.LoadAssetAtPath<Material>("Assets/UdonMidiPlayer/Materials/WaterfallSoftSpark.mat");
            if(spark==null) throw new InvalidOperationException("Missing existing soft-spark material.");
            for(int mode=0;mode<5;mode++)
            {
                var view=player.GetStringsView(mode+3); if(view==null) continue;
                view.routedTarget=mode+2;
                foreach(var child in view.transform.Cast<Transform>().Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^Note(?:Window|Hit)?\d+$")).ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                view.stringParticles=new ParticleSystem[view.strings.Length];
                view.symbolParticles=new ParticleSystem[view.strings.Length];
                for(int section=0;section<view.bodies.Length;section++)
                {
                    view.contacts[section].GetComponent<Renderer>().sharedMaterial=contact;
                    for(int s=0;s<4;s++)
                    {
                        var host=Child(view.bodies[section],"StringSpark"+s);
                        host.localPosition=new Vector3((s-1.5f)*.009f,-.045f,.08f);
                        // Both layers rise in world up, including tilted violin/viola stages.
                        host.rotation=Quaternion.Euler(-90,0,0); host.localScale=Vector3.one;
                        var system=host.GetComponent<ParticleSystem>(); if(system==null) system=host.gameObject.AddComponent<ParticleSystem>();
                        ConfigureStringParticles(system,spark,(uint)(41+section*4+s));
                        view.stringParticles[section*4+s]=system;
                        var symbolHost=Child(view.bodies[section],"StringSymbols"+s);
                        symbolHost.localPosition=host.localPosition; symbolHost.rotation=host.rotation; symbolHost.localScale=Vector3.one;
                        var symbols=symbolHost.GetComponent<ParticleSystem>(); if(symbols==null) symbols=symbolHost.gameObject.AddComponent<ParticleSystem>();
                        ConfigureStringParticles(symbols,null,(uint)(141+section*4+s));
                        view.symbolParticles[section*4+s]=symbols;
                    }
                }
                UdonSharpEditorUtility.CopyProxyToUdon(view,ProxySerializationPolicy.All); EditorUtility.SetDirty(view);
            }
        }

        private static void ConfigureStringParticles(ParticleSystem system,Material material,uint seed)
        {
            bool symbols=material==null;
            if(symbols) MidiWaterfallPresentationBuilder.ConfigureSymbols(system,.04f,(int)seed);
            else MidiWaterfallPresentationBuilder.ConfigureParticles(system,material,.04f);
            system.useAutoRandomSeed=false; system.randomSeed=seed;
            var main=system.main;
            main.maxParticles=symbols?4:12; main.scalingMode=ParticleSystemScalingMode.Shape;
            main.startSize=new ParticleSystem.MinMaxCurve(symbols?.025f:.006f,symbols?.05f:.013f);
            // Same life/opacity/velocity curves as the piano, with a shorter travel near strings.
            var velocity=system.velocityOverLifetime;
            velocity.xMultiplier*=.3f; velocity.yMultiplier*=.3f; velocity.zMultiplier*=.3f;
            var shape=system.shape; shape.radius=.002f;
            var collision=system.collision; collision.enabled=false;
            EditorUtility.SetDirty(system);
        }

        private static T Component<T>(Transform target) where T:UdonSharpBehaviour
        { return target.GetComponent<T>() ?? target.gameObject.AddUdonSharpComponent<T>(); }
        private static Transform Child(Transform parent,string name)
        { var child=parent.Find(name); if(child!=null) return child; var obj=new GameObject(name); obj.transform.SetParent(parent,false); return obj.transform; }
        private static Mesh PrimitiveMesh(PrimitiveType type)
        { var obj=GameObject.CreatePrimitive(type); var mesh=obj.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(obj); return mesh; }
        private static Material Material(string name,Color color,float metal=0,float smooth=.4f,bool glow=false)
        {
            string path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path); if(mat!=null) return mat;
            mat=new Material(Shader.Find("Standard")); mat.color=color; mat.SetFloat("_Metallic",metal); mat.SetFloat("_Glossiness",smooth);
            if(glow) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",color*.75f); }
            AssetDatabase.CreateAsset(mat,path); return mat;
        }
        private static void MeshPart(Transform parent,string name,Mesh mesh,Material material)
        {
            string path=Folder+"/"+name+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null) AssetDatabase.CreateAsset(mesh,path); else { EditorUtility.CopySerialized(mesh,existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
            var child=Child(parent,name); (child.GetComponent<MeshFilter>() ?? child.gameObject.AddComponent<MeshFilter>()).sharedMesh=mesh; (child.GetComponent<MeshRenderer>() ?? child.gameObject.AddComponent<MeshRenderer>()).sharedMaterial=material;
        }
        private static void Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var child=Child(parent,name); child.localPosition=position; child.localScale=size;
            var filter=child.GetComponent<MeshFilter>(); if(filter==null) filter=child.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh=PrimitiveMesh(PrimitiveType.Cube);
            var renderer=child.GetComponent<MeshRenderer>(); if(renderer==null) renderer=child.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
        }
        private static Mesh Tube(Vector3[] points,float radius,int sides=8)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            for(int i=0;i<points.Length;i++)
            {
                Vector3 axis=(points[Math.Min(i+1,points.Length-1)]-points[Math.Max(0,i-1)]).normalized;
                Vector3 right=Vector3.Cross(axis,Mathf.Abs(axis.z)>.9f?Vector3.up:Vector3.forward).normalized;
                Vector3 up=Vector3.Cross(axis,right).normalized;
                for(int s=0;s<sides;s++) { float a=s*Mathf.PI*2/sides; vertices.Add(points[i]+radius*(Mathf.Cos(a)*right+Mathf.Sin(a)*up)); uv.Add(new Vector2(s/(float)sides,i/(float)(points.Length-1))); }
            }
            for(int i=0;i<points.Length-1;i++) for(int s=0;s<sides;s++) { int a=i*sides+s,b=i*sides+(s+1)%sides,c=a+sides,d=b+sides; triangles.AddRange(new[]{a,c,b,b,c,d}); }
            var mesh=new Mesh(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Vector2[] Outline()
        {
            var right=new[]{new Vector2(0,.16f),new Vector2(.07f,.16f),new Vector2(.12f,.13f),new Vector2(.14f,.08f),new Vector2(.13f,.035f),new Vector2(.09f,.015f),new Vector2(.073f,-.025f),new Vector2(.09f,-.065f),new Vector2(.14f,-.09f),new Vector2(.16f,-.145f),new Vector2(.145f,-.20f),new Vector2(.085f,-.235f),new Vector2(0,-.245f)};
            var control=right.Concat(right.Reverse().Skip(1).Take(right.Length-2).Select(v=>new Vector2(-v.x,v.y))).ToArray();
            var result=new List<Vector2>();
            for(int i=0;i<control.Length;i++) for(int s=0;s<5;s++)
            {
                float t=s/5f; Vector2 a=control[(i+control.Length-1)%control.Length],b=control[i],c=control[(i+1)%control.Length],d=control[(i+2)%control.Length];
                result.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
            }
            return result.ToArray();
        }
        private static Mesh Plate(bool back)
        {
            var outline=Outline(); int n=outline.Length,rings=7; var v=new List<Vector3>(); var tris=new List<int>(); var uv=new List<Vector2>();
            for(int r=0;r<rings;r++)
            {
                float f=.001f+r/(float)(rings-1)*.999f;
                for(int i=0;i<n;i++) { Vector2 p=Vector2.Lerp(new Vector2(0,-.04f),outline[i],f); v.Add(new Vector3(p.x,p.y,(back?-1:1)*(.025f+.024f*(1-f*f)))); uv.Add(new Vector2(p.x*3+.5f,p.y*2+.5f)); }
            }
            for(int r=0;r<rings-1;r++) for(int i=0;i<n;i++) { int a=r*n+i,b=r*n+(i+1)%n,c=a+n,d=b+n; tris.AddRange(back?new[]{a,c,b,b,c,d}:new[]{a,b,c,b,d,c}); }
            var mesh=new Mesh(); mesh.SetVertices(v); mesh.SetUVs(0,uv); mesh.SetTriangles(tris,0); mesh.RecalculateNormals(); return mesh;
        }
        private static void BuildInstrument(Transform model,int kind)
        {
            var maple=Material("StringsVarnish",new Color(.39f,.14f,.035f),0,.72f);
            var dark=Material("StringsEbony",new Color(.018f,.012f,.009f),0,.38f);
            var edge=Material("StringsBinding",new Color(.67f,.40f,.15f),0,.6f);
            var bridge=Material("StringsBridge",new Color(.68f,.47f,.25f),0,.3f);
            var hair=Material("StringsBowHair",new Color(.79f,.74f,.58f),0,.3f);
            MeshPart(model,"TopPlate",Plate(false),maple); MeshPart(model,"BackPlate",Plate(true),maple);
            var outline=Outline(); var points=outline.Select(p=>new Vector3(p.x,p.y,.025f)).Concat(new[]{new Vector3(outline[0].x,outline[0].y,.025f)}).ToArray();
            MeshPart(model,"PlateBinding",Tube(points,.002f),edge);
            var sideV=new List<Vector3>(); var sideT=new List<int>();
            for(int i=0;i<outline.Length;i++) { sideV.Add(new Vector3(outline[i].x,outline[i].y,-.025f)); sideV.Add(new Vector3(outline[i].x,outline[i].y,.025f)); }
            for(int i=0;i<outline.Length;i++) { int a=i*2,b=((i+1)%outline.Length)*2; sideT.AddRange(new[]{a,a+1,b,b,a+1,b+1}); }
            var sides=new Mesh(); sides.SetVertices(sideV); sides.SetTriangles(sideT,0); sides.RecalculateNormals(); MeshPart(model,"Ribs",sides,maple);
            Box(model,"Neck",new Vector3(0,.235f,0),new Vector3(.029f,.235f,.029f),maple);
            Box(model,"Fingerboard",new Vector3(0,.20f,.049f),new Vector3(.036f,.32f,.014f),dark);
            Box(model,"Tailpiece",new Vector3(0,-.16f,.05f),new Vector3(.037f,.105f,.015f),dark);
            Box(model,"Bridge",new Vector3(0,-.045f,.057f),new Vector3(.047f,.007f,.05f),bridge);
            Box(model,"Pegbox",new Vector3(0,.4f,.006f),new Vector3(.036f,.105f,.035f),maple);
            for(int i=0;i<4;i++) MeshPart(model,"Peg"+i,Tube(new[]{new Vector3(-.034f,.37f+i*.021f,.006f),new Vector3(.034f,.37f+i*.021f,.006f)},.006f),dark);
            var scroll=Enumerable.Range(0,56).Select(i=>{float a=i/55f*Mathf.PI*4.2f,r=.022f*(1-i/66f);return new Vector3(Mathf.Cos(a)*r,.465f+Mathf.Sin(a)*r,.015f+i*.0002f);}).ToArray();
            MeshPart(model,"Scroll",Tube(scroll,.005f),maple);
            for(int side=-1;side<=1;side+=2)
            {
                var f=Enumerable.Range(0,40).Select(i=>{float t=i/39f;return new Vector3(side*(.061f+.01f*Mathf.Sin(t*Mathf.PI*2)),.025f-t*.14f,.048f);}).ToArray();
                MeshPart(model,"SoundHole"+(side+1),Tube(f,.003f),dark);
            }
            for(int i=0;i<4;i++)
            {
                string path=Folder+"/String"+i+".mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(mat==null) { mat=new Material(Shader.Find("MusicMidiPlayer/StringVibration")); AssetDatabase.CreateAsset(mat,path); }
                float x=(i-1.5f)*.009f;
                var stringPoints=Enumerable.Range(0,25).Select(k=>{float t=k/24f;return new Vector3(x*Mathf.Lerp(1,.55f,t),Mathf.Lerp(-.185f,.35f,t),Mathf.Lerp(.05f,.065f,t)+.029f*Mathf.Sin(t*Mathf.PI));}).ToArray();
                MeshPart(model,"String"+i,Tube(stringPoints,.0006f,5),mat);
            }
            var bow=Child(model,"Bow"); bow.localPosition=new Vector3(0,-.03f,.093f);
            var stick=Enumerable.Range(0,32).Select(i=>{float x=(i/31f-.5f)*.65f;return new Vector3(x,0,.015f+.018f*Mathf.Sin(i/31f*Mathf.PI));}).ToArray();
            MeshPart(bow,"BowStick",Tube(stick,.0032f),maple);
            Box(bow,"BowHair",new Vector3(0,0,0),new Vector3(.64f,.006f,.0015f),hair);
            Box(bow,"Frog",new Vector3(-.27f,0,.015f),new Vector3(.045f,.012f,.028f),dark);
            var marker=Child(model,"Contact"); (marker.GetComponent<MeshFilter>() ?? marker.gameObject.AddComponent<MeshFilter>()).sharedMesh=PrimitiveMesh(PrimitiveType.Sphere); (marker.GetComponent<MeshRenderer>() ?? marker.gameObject.AddComponent<MeshRenderer>()).sharedMaterial=Material("StringsNotes",Color.cyan,0,.7f,true); marker.localScale=Vector3.one*.009f; marker.gameObject.SetActive(false);
            if(kind>=2) MeshPart(model,"Endpin",Tube(new[]{new Vector3(0,-.245f,0),new Vector3(0,-.34f,0)},.002f),dark);
            AssetDatabase.SaveAssets();
        }
    }
}
