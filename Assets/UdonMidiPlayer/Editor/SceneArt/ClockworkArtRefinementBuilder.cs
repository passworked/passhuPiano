using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Views;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>Original, bounded scene dressing. Never rebuilds the environment or instrument layout.</summary>
    public static class ClockworkArtRefinementBuilder
    {
        const string Base = ClockworkMaintenanceSceneBuilder.Folder;
        const string Folder = Base + "/ArtRefinement";
        const string TextureFolder = Folder + "/Textures";
        static readonly Rect Blueprint = new Rect(0, .5f, .5f, .5f);
        static readonly Rect Journal = new Rect(.5f, .5f, .5f, .5f);
        static readonly Rect Crest = new Rect(0, 0, .5f, .5f);
        static readonly Rect Carpet = new Rect(.5f, 0, .5f, .5f);

        [MenuItem("Tools/Music MIDI Player/Scene Art/Refine Clockwork Art (10 MB)")]
        public static void Install()
        {
            Scene scene = RequireScene();
            Directory.CreateDirectory("RecoveryBackups/ClockworkArt-20261104");
            if (!EditorSceneManager.SaveScene(scene, "RecoveryBackups/ClockworkArt-20261104/BeforeInstall-" + DateTime.Now.Ticks + ".unity", true))
                throw new IOException("Could not preserve current scene memory.");
            Directory.CreateDirectory(TextureFolder); Directory.CreateDirectory(Folder + "/Meshes"); Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!File.Exists(TextureFolder + "/WorkshopAtlas.png")) throw new IOException("Run ArtSource/ClockworkMaintenanceStreet/create-workshop-atlas.ps1 first.");
            CreateSurfaceTextures("Oak", 0);
            CreateSurfaceTextures("Limestone", 1);
            CreateSurfaceTextures("Bronze", 2);
            ImportTexture(TextureFolder + "/WorkshopAtlas.png", false, 1024);
            RefreshInstalledMaterials();
            var art = scene.GetRootGameObjects().Single(g => g.name == "ClockworkMaintenanceStreet").transform;
            Transform host = art.Find("ArtRefinement");
            if (!host) { host = new GameObject("ArtRefinement").transform; host.SetParent(art, false); }
            var c = art.GetComponentInChildren<ClockworkSceneController>(true);
            var atlas = Material("DraftingAndWovenAtlas", Shader.Find("MusicMidiPlayer/Clockwork/HeritageSurface"));
            atlas.color = Color.white; atlas.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/WorkshopAtlas.png");
            atlas.SetFloat("_DetailAmount", 0); atlas.SetFloat("_Composition", 0); atlas.SetFloat("_Smoothness", .12f);
            var paper = Material("IvoryPaperEdges", Shader.Find("MusicMidiPlayer/Clockwork/HeritageSurface"));
            paper.color = new Color(.79f, .73f, .57f); paper.SetFloat("_DetailAmount", 0); paper.SetFloat("_Smoothness", .08f); paper.SetFloat("_Composition", 0);
            var oak = EnvironmentMaterial("CW_Honey_oak_long_grain");
            var dark = EnvironmentMaterial("CW_Dark_structural_walnut");
            var brass = EnvironmentMaterial("CW_Polished_brass_wear");
            var bronze = EnvironmentMaterial("CW_Old_brushed_brass");
            var iron = EnvironmentMaterial("CW_Aged_petrol_green_cast_iron");
            var steel = EnvironmentMaterial("CW_Dark_gunmetal_fastenings");

            var desk = Group(host, "ClockkeepersDesk", new Vector3(-11.45f, 0, -10.1f), new Vector3(0, 270, 0));
            var b = new Dressing(desk);
            b.Box(oak, new Vector3(0, 1.055f, 0), new Vector3(2.0f, .11f, .92f), .025f);
            b.Box(dark, new Vector3(0, .90f, .04f), new Vector3(1.90f, .21f, .72f), .015f);
            foreach (float x in new[] { -.82f, .82f }) foreach (float z in new[] { -.32f, .32f })
                b.Box(iron, new Vector3(x, .47f, z), new Vector3(.065f, .94f, .065f), .008f);
            b.Box(brass, new Vector3(0, .35f, .30f), new Vector3(1.75f, .025f, .025f), .005f);
            for (int i = 0; i < 3; i++)
            {
                b.Box(oak, new Vector3(-.60f + .60f * i, .91f, -.345f), new Vector3(.55f, .15f, .06f), .008f);
                b.Ring(brass, new Vector3(-.60f + .60f * i, .92f, -.385f), .029f, .017f, .008f, Quaternion.Euler(90, 0, 0), 16);
            }
            b.Panel(atlas, new Vector3(-.42f, 1.12f, -.06f), new Vector2(.77f, .55f), Quaternion.Euler(90, 0, -7), Blueprint);
            b.Box(paper, new Vector3(.38f, 1.123f, -.02f), new Vector3(.43f, .025f, .49f), .004f);
            b.Panel(atlas, new Vector3(.38f, 1.139f, -.02f), new Vector2(.43f, .49f), Quaternion.Euler(90, 0, 4), Journal);
            OilCan(b, bronze, brass, iron, new Vector3(.72f, 1.11f, .22f));
            b.Cylinder(iron, new Vector3(-.80f, 1.16f, .24f), .06f, .10f, Quaternion.identity, 20);
            for (int i=0;i<4;i++) b.Rod(brass,new Vector3(-.82f+i*.015f,1.19f,.24f),new Vector3(-.83f+i*.026f,1.40f-i*.012f,.25f),.004f);
            b.Box(dark,new Vector3(.05f,1.127f,-.34f),new Vector3(.26f,.018f,.035f),.004f);
            b.Rod(brass,new Vector3(-.05f,1.14f,-.32f),new Vector3(.15f,1.14f,-.26f),.004f);
            FramedPanel(b, atlas, dark, brass, new Vector3(0, 2.12f, .53f), new Vector2(1.27f, .92f), Blueprint);
            b.Panel(atlas,new Vector3(.75f,1.63f,.535f),new Vector2(.32f,.37f),Quaternion.identity,Journal);
            b.Commit("Desk");
            var collider = desk.GetComponent<BoxCollider>(); if(!collider) collider=desk.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .55f, 0); collider.size = new Vector3(2.02f, 1.1f, .94f);

            // Existing repair stall: concentrated props behind the instrument circulation envelope.
            var stall = Group(host,"RepairCounterDetails",new Vector3(-5.85f,0,5.62f),Vector3.zero);
            b = new Dressing(stall);
            b.Box(oak,new Vector3(0,1.03f,0),new Vector3(2.65f,.10f,.75f),.018f);
            b.Panel(atlas,new Vector3(-.31f,1.087f,-.03f),new Vector2(.75f,.49f),Quaternion.Euler(90,0,-9),Blueprint);
            OilCan(b, bronze, brass, iron, new Vector3(.74f,1.08f,.07f));
            b.Gear(bronze,new Vector3(.32f,1.12f,.05f),.15f,.045f,24);
            b.Gear(brass,new Vector3(.43f,1.13f,-.17f),.10f,.036f,18);
            b.Rod(steel,new Vector3(-.65f,1.10f,-.25f),new Vector3(-.40f,1.10f,-.20f),.012f);
            b.Ring(steel,new Vector3(-.68f,1.10f,-.255f),.04f,.025f,.015f,Quaternion.identity,20);
            b.Cylinder(brass,new Vector3(-1.03f,1.13f,.19f),.075f,.10f,Quaternion.identity,24);
            b.Box(dark,new Vector3(-.92f,.20f,-.18f),new Vector3(.50f,.38f,.46f),.018f);
            b.Box(brass,new Vector3(-.92f,.20f,-.42f),new Vector3(.17f,.06f,.008f),.003f);
            b.Commit("Counter");

            var rug = Group(host,"RecitalCarpet",Vector3.zero,Vector3.zero);
            b = new Dressing(rug);
            b.Panel(atlas,new Vector3(.05f,.045f,.40f),new Vector2(8.74f,5.62f),Quaternion.Euler(90,0,0),Carpet);
            b.Commit("Carpet");
            if (scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MusicMidiPlayer.Views.MidiInstrumentFanLayout>(true))
                .Any(l => Mathf.Abs(l.center.z + 1.8f) < .001f)) ClockworkComfortLayoutBuilder.ApplyCarpet(rug);

            var entrance = Group(host,"ClockmakersSeal",new Vector3(0,1.65f,-12.55f),new Vector3(0,180,0));
            b = new Dressing(entrance);
            FramedPanel(b,atlas,dark,brass,Vector3.zero,new Vector2(.58f,.58f),Crest);
            b.Commit("Seal");

            // Repeated small brass inlays tie the desk and existing workshop architecture together.
            var accents = Group(host,"ArchitecturalAccents",Vector3.zero,Vector3.zero);
            b = new Dressing(accents);
            foreach(float x in new[]{-12.28f,12.28f})
                for(int i=0;i<5;i++)
                    b.Box(brass,new Vector3(x,.22f,-9.5f+i*4.2f),new Vector3(.035f,.032f,1.4f),.004f);
            // Continuous inner boarding closes subpixel seams in the existing roof export.
            b.RoofLining(dark);
            b.Commit("Accents");

            var air = Group(host,"DialAtmosphere",Vector3.zero,Vector3.zero);
            var beam = Material("DialLightRibbons",Shader.Find("MusicMidiPlayer/Clockwork/Atmosphere"));
            beam.SetColor("_Color",new Color(1,.83f,.58f,.055f)); beam.SetFloat("_Dust",0);
            b = new Dressing(air);
            for(int i=0;i<5;i++)
            {
                Vector3 p = c.dialCenter + new Vector3(-2.2f+i*1.05f,-.9f+Mathf.Sin(i)*.7f,-.16f);
                b.Ribbon(beam,p,.42f+i*.035f,7.5f);
            }
            b.Commit("Atmosphere");
            foreach(var r in air.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false; }
            var dustMaterial = Material("DriftingDust",Shader.Find("MusicMidiPlayer/Clockwork/Atmosphere"));
            dustMaterial.SetColor("_Color",new Color(1,.84f,.64f,.22f));dustMaterial.SetFloat("_Dust",1);
            ConfigureDust(air,dustMaterial);
            c.atmosphereMaterials = new[] {beam,dustMaterial};
            c.RefreshFromClientClock();
            UdonSharpEditorUtility.CopyProxyToUdon(c,ProxySerializationPolicy.All);
            EditorUtility.SetDirty(c);EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(c));
            foreach(var m in c.atmosphereMaterials) EditorUtility.SetDirty(m);
            EditorUtility.SetDirty(atlas);EditorUtility.SetDirty(paper);
            foreach(var m in c.readabilityMaterials) if(m) AssetDatabase.SaveAssetIfDirty(m);
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Folder})) AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)));
            foreach(var r in host.GetComponentsInChildren<Renderer>(true))
            {
                r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
                // Object-space detail stays attached; avoid static batching changing normal bases.
                GameObjectUtility.SetStaticEditorFlags(r.gameObject,StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
            }
            Validate();WriteBudget();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            SceneView.RepaintAll();
            Debug.Log("Clockwork art refinement installed: heritage materials, two workshop vignettes, woven clock carpet, one-source atmosphere. No instrument transforms changed.");
        }

        public static void RefreshInstalledMaterials()
        {
            if(!File.Exists(TextureFolder+"/OakColor.png")) return;
            var shader=Shader.Find("MusicMidiPlayer/Clockwork/HeritageSurface");
            if(!shader) throw new InvalidOperationException("Heritage shader is missing.");
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Base+"/Materials"}))
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(!m.name.StartsWith("CW_",StringComparison.Ordinal)||m.name.Contains("glass"))continue;
                string kind=null;float relief=.30f,polish=.25f;
                if(m.name.Contains("oak")||m.name.Contains("walnut")){kind="Oak";relief=.42f;polish=m.name.Contains("Dark")?.25f:.42f;}
                else if(m.name.Contains("limestone")||m.name.Contains("Limestone")||m.name.Contains("plaster")||m.name.Contains("mortar")){kind="Limestone";relief=.60f;polish=.08f;}
                else if(m.name.Contains("brass")||m.name.Contains("iron")||m.name.Contains("gunmetal")){kind="Bronze";relief=.25f;polish=m.name.Contains("Polished")?.72f:.46f;}
                if(kind==null)continue;
                m.shader=shader;m.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/"+kind+"Color.png");
                m.SetTexture("_DetailTex",AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder+"/"+kind+"Detail.png"));
                m.SetFloat("_DetailAmount",1);m.SetFloat("_BumpStrength",relief);m.SetFloat("_Smoothness",polish);m.SetFloat("_Composition",1);
                if(kind=="Bronze")m.SetFloat("_Metallic",m.name.Contains("brass")?.68f:.48f);
                if(m.name=="CW_Old_brushed_brass")m.color=new Color(.68f,.53f,.31f);
                if(m.name=="CW_Polished_brass_wear")m.color=new Color(.85f,.70f,.45f);
                // Stone colors now form a restrained family rather than alternating checker squares.
                if(m.name=="CW_Limestone_face_variation")m.color=new Color(.66f,.63f,.56f);
                if(m.name=="CW_Warm_limestone_mineral_grain")m.color=new Color(.61f,.58f,.52f);
                m.globalIlluminationFlags=m.HasProperty("_BakedCeilingFill") && m.GetFloat("_BakedCeilingFill")>0
                    ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);
            }
        }

        static Scene RequireScene()
        {
            var s=SceneManager.GetActiveScene();
            if(s.path!=ClockworkMaintenanceSceneBuilder.ScenePath||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Open the clockwork second scene outside Play Mode and compilation.");
            return s;
        }
        static Material EnvironmentMaterial(string name) { return AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/"+name+".mat"); }
        static Material Material(string name,Shader shader)
        {
            if(!shader)throw new InvalidOperationException("Required scene shader not found.");
            string path=Folder+"/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,path);}
            m.shader=shader;m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;return m;
        }
        static Transform Group(Transform parent,string name,Vector3 p,Vector3 angles)
        {
            var t=parent.Find(name);if(!t){t=new GameObject(name).transform;t.SetParent(parent,false);}
            t.localPosition=p;t.localEulerAngles=angles;t.localScale=Vector3.one;return t;
        }

        static float Noise(float x,float y,float period)
        {
            float u=x/period,v=y/period;
            return Mathf.Lerp(Mathf.Lerp(Mathf.PerlinNoise(x+37,y+19),Mathf.PerlinNoise(x-period+37,y+19),u),
                Mathf.Lerp(Mathf.PerlinNoise(x+37,y-period+19),Mathf.PerlinNoise(x-period+37,y-period+19),u),v);
        }
        static void CreateSurfaceTextures(string name,int kind)
        {
            string path=TextureFolder+"/"+name;
            int size=kind==0?1024:512;
            var loaded=AssetDatabase.LoadAssetAtPath<Texture2D>(path+"Color.png");
            if(loaded&&loaded.width==size&&File.Exists(path+"Detail.png")){ImportTexture(path+"Color.png",false,size);ImportTexture(path+"Detail.png",true,Mathf.Min(size,512));return;}
            var heights=new float[size*size];var colors=new Color32[size*size];var detail=new Color32[size*size];
            var random=new System.Random(404+kind);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(float)x/size,v=(float)y/size;
                float cloud=Noise(u*6,v*6,6)*.64f+Noise(u*21,v*21,21)*.25f+Noise(u*79,v*79,79)*.11f;
                float fine=(float)random.NextDouble();float value,h,polish;
                if(kind==0)
                {
                    float warp=Noise(u*4,v*4,4)*1.6f;
                    float grain=Mathf.PerlinNoise(u*65+warp*4,Mathf.Sin(v*Mathf.PI*2)*.5f+31);
                    float fibres=Noise(u*180,v*180,180);
                    h=cloud*.23f+grain*.65f+fibres*.12f;
                    value=.67f+cloud*.25f+grain*.13f+(fine-.5f)*.025f;
                    polish=.38f+grain*.36f;
                    colors[y*size+x]=new Color(value*1.025f,value,value*.94f,1);
                }
                else if(kind==1)
                {
                    float pits=Mathf.Pow(Mathf.Clamp01((.38f-Noise(u*140,v*140,140))*5),2);
                    h=cloud*.72f+fine*.035f-pits*.14f;value=.79f+cloud*.22f-pits*.10f;
                    polish=.18f+cloud*.25f;
                    colors[y*size+x]=new Color(value,value*.985f,value*.955f,1);
                }
                else
                {
                    float patina=Mathf.SmoothStep(0,1,Mathf.Clamp01((cloud-.48f)*4));
                    float scratch=Noise(u*160,v*160,160);
                    h=cloud*.45f+scratch*.12f+(fine-.5f)*.035f;value=.82f+cloud*.18f;
                    polish=.85f-patina*.65f;
                    colors[y*size+x]=Color.Lerp(new Color(value,value*.98f,value*.89f),new Color(.46f,.64f,.56f),patina*.42f);
                }
                heights[y*size+x]=h;detail[y*size+x]=new Color(.5f,.5f,.85f+cloud*.15f,polish);
            }
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=(heights[y*size+(x+1)%size]-heights[y*size+(x+size-1)%size])*2;
                float dy=(heights[((y+1)%size)*size+x]-heights[((y+size-1)%size)*size+x])*2;
                var d=detail[y*size+x];d.r=(byte)Mathf.Clamp(128-dx*127,0,255);d.g=(byte)Mathf.Clamp(128-dy*127,0,255);detail[y*size+x]=d;
            }
            SavePixels(path+"Color.png",size,colors,false);SavePixels(path+"Detail.png",size,detail,true);
        }
        static void SavePixels(string path,int size,Color32[] pixels,bool linear)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false,linear);t.SetPixels32(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);ImportTexture(path,linear,linear?Mathf.Min(size,512):size);
        }
        static void ImportTexture(string path,bool linear,int size)
        {
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);if(!ti)throw new IOException(path);
            ti.textureType=TextureImporterType.Default;ti.sRGBTexture=!linear;ti.mipmapEnabled=true;ti.isReadable=false;ti.maxTextureSize=size;
            ti.wrapMode=path.Contains("Atlas")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=4;
            ti.textureCompression=TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Standalone",overridden=true,maxTextureSize=size,format=linear?TextureImporterFormat.BC7:TextureImporterFormat.DXT1,compressionQuality=100});
            ti.SaveAndReimport();
        }
        static void FramedPanel(Dressing b,Material atlas,Material wood,Material brass,Vector3 center,Vector2 size,Rect uv)
        {
            b.Box(wood,center+Vector3.forward*.022f,new Vector3(size.x+.11f,size.y+.11f,.07f),.012f);
            b.Panel(atlas,center-Vector3.forward*.017f,size,Quaternion.identity,uv);
            foreach(float sign in new[]{-1f,1f})
            {
                b.Box(brass,center+new Vector3(sign*(size.x*.5f+.025f),0,-.02f),new Vector3(.013f,size.y+.07f,.014f),.003f);
                b.Box(brass,center+new Vector3(0,sign*(size.y*.5f+.025f),-.02f),new Vector3(size.x+.07f,.013f,.014f),.003f);
            }
        }
        static void OilCan(Dressing b,Material bronze,Material brass,Material iron,Vector3 p)
        {
            b.Cylinder(bronze,p+new Vector3(0,.085f,0),.075f,.17f,Quaternion.identity,24);
            b.Cylinder(brass,p+new Vector3(0,.175f,0),.052f,.025f,Quaternion.identity,24);
            b.Rod(brass,p+new Vector3(0,.18f,0),p+new Vector3(-.10f,.35f,.03f),.012f);
            b.Rod(brass,p+new Vector3(-.10f,.35f,.03f),p+new Vector3(-.14f,.37f,.03f),.006f);
            b.Ring(iron,p+new Vector3(.09f,.10f,0),.055f,.045f,.016f,Quaternion.Euler(90,0,0),20);
        }
        static void ConfigureDust(Transform air,Material material)
        {
            var t=air.Find("WindowDust");if(!t){t=new GameObject("WindowDust").transform;t.SetParent(air,false);}
            t.localPosition=new Vector3(1.0f,5.2f,6.0f);
            var ps=t.GetComponent<ParticleSystem>();if(!ps)ps=t.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.duration=24;main.startLifetime=new ParticleSystem.MinMaxCurve(12,22);
            main.startSize=new ParticleSystem.MinMaxCurve(.008f,.025f);main.startSpeed=.025f;main.maxParticles=96;
            main.startColor=new Color(1,1,1,.65f);main.simulationSpace=ParticleSystemSimulationSpace.Local;main.prewarm=true;main.playOnAwake=true;
            var emission=ps.emission;emission.rateOverTime=4;
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(8,6,7);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;velocity.x=.012f;velocity.y=.018f;velocity.z=-.025f;
            var lights=ps.lights;lights.enabled=false;
            var noise=ps.noise;noise.enabled=false;
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.75f,.2f),new GradientAlphaKey(.75f,.8f),new GradientAlphaKey(0,1)});color.color=gradient;
            var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }

        public static string Validate()
        {
            var s=SceneManager.GetActiveScene();
            if(s.path!=ClockworkMaintenanceSceneBuilder.ScenePath||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Wrong scene/state for art validation.");
            var roots=s.GetRootGameObjects();var lights=roots.SelectMany(g=>g.GetComponentsInChildren<Light>(true)).ToArray();
            if(lights.Length!=1||lights[0].type!=LightType.Point)throw new InvalidOperationException("Exactly one Point Light required.");
            int missing=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if(missing!=0)throw new InvalidOperationException("Missing scripts: "+missing);
            var host=GameObject.Find("ClockworkMaintenanceStreet/ArtRefinement");
            if(host)
            {
                if(host.GetComponentsInChildren<Collider>(true).Any(c=>c.bounds.max.x>-8.5f))throw new InvalidOperationException("Dressing intrudes into the central instrument envelope.");
                if(host.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.lights.enabled))throw new InvalidOperationException("Particle lights must remain disabled.");
                foreach(var r in host.GetComponentsInChildren<Renderer>(true)) if(r.sharedMaterials.Any(m=>!m||!m.shader||ShaderUtil.ShaderHasError(m.shader)))throw new InvalidOperationException("Invalid dressing material: "+r.name);
            }
            return "1 Point Light including inactive; no missing scripts; dressing colliders outside central envelope; no particle lights; shaders valid.";
        }
        static void WriteBudget()
        {
            var files=Directory.GetFiles(Folder,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta",StringComparison.Ordinal)).ToArray();
            long source=files.Sum(p=>new FileInfo(p).Length);
            long textureBytes=AssetDatabase.FindAssets("t:Texture2D",new[]{Folder}).Sum(g=>ProfilerBytes(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))));
            long meshBytes=AssetDatabase.FindAssets("t:Mesh",new[]{Folder}).Sum(g=>ProfilerBytes(AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(g))));
            Directory.CreateDirectory("Logs/ClockworkArtRefinement");
            string report="New art source assets (no .meta): "+source+" bytes\nImported texture runtime memory: "+textureBytes+" bytes\nImported mesh runtime memory: "+meshBytes+" bytes\n"+Validate()+"\nDownload increment not measured: no world build/upload performed. Texture memory, source size and download size are distinct.\n";
            File.WriteAllText("Logs/ClockworkArtRefinement/budget.txt",report);
            if(source>10000000||textureBytes+meshBytes>10000000)throw new InvalidOperationException("Art resource budget exceeded. See budget.txt.");
        }
        static long ProfilerBytes(Object asset) { return asset?UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(asset):0; }

        public static void CaptureReview(string folder)
        {
            var s=RequireScene();Directory.CreateDirectory(folder);
            var c=s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ClockworkSceneController>(true)).Single();
            var go=new GameObject("ClockworkArtReviewCamera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();
            cam.nearClipPlane=.06f;cam.farClipPlane=150;cam.clearFlags=CameraClearFlags.Skybox;cam.allowHDR=true;cam.allowMSAA=true;
            var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32){antiAliasing=4};
            var previous=RenderTexture.active;
            try
            {
                cam.targetTexture=rt;
                foreach(int hour in new[]{12,17,0})
                {
                    c.EvaluateAt(12345,hour*3600);
                    foreach(string view in new[]{"entry","gallery","workshop"})
                    {
                        if(view=="entry"){cam.transform.position=new Vector3(0,1.65f,-9.5f);cam.transform.LookAt(new Vector3(.8f,6.0f,8.0f));cam.fieldOfView=62;}
                        else if(view=="gallery"){cam.transform.position=new Vector3(-10.3f,5.55f,-4.0f);cam.transform.LookAt(new Vector3(1.4f,7.1f,7.4f));cam.fieldOfView=70;}
                        else {cam.transform.position=new Vector3(-8.4f,1.8f,-8.3f);cam.transform.LookAt(new Vector3(-11.6f,1.65f,-10.1f));cam.fieldOfView=52;}
                        cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();
                        File.WriteAllBytes(folder+"/"+hour.ToString("00")+"-"+view+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
                    }
                }
            }
            finally {RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);c.RefreshFromClientClock();}
        }

        // Small bevelled primitives are combined per vignette/material, retaining asset GUIDs on rerun.
        sealed class Dressing
        {
            sealed class Batch { public readonly List<Vector3> v=new List<Vector3>();public readonly List<Vector3> n=new List<Vector3>();public readonly List<Vector2> uv=new List<Vector2>();public readonly List<int> tri=new List<int>(); }
            readonly Transform host;readonly Dictionary<Material,Batch> batches=new Dictionary<Material,Batch>();
            public Dressing(Transform t){host=t;}
            Batch Get(Material m){if(!m)throw new InvalidOperationException("Missing dressing material");Batch b;if(!batches.TryGetValue(m,out b)){b=new Batch();batches.Add(m,b);}return b;}
            void Tri(Batch b,Vector3 a,Vector3 c,Vector3 d,Vector3 na,Vector3 nc,Vector3 nd,Vector2 ua,Vector2 uc,Vector2 ud)
            {
                int k=b.v.Count;b.v.Add(a);b.v.Add(c);b.v.Add(d);b.n.Add(na);b.n.Add(nc);b.n.Add(nd);b.uv.Add(ua);b.uv.Add(uc);b.uv.Add(ud);b.tri.Add(k);b.tri.Add(k+1);b.tri.Add(k+2);
            }
            void Quad(Batch b,Vector3 a,Vector3 c,Vector3 d,Vector3 e,Vector3 normal,Rect uv)
            {
                Vector2 bl=new Vector2(uv.xMin,uv.yMin),br=new Vector2(uv.xMax,uv.yMin),tr=new Vector2(uv.xMax,uv.yMax),tl=new Vector2(uv.xMin,uv.yMax);
                Tri(b,a,c,d,normal,normal,normal,bl,br,tr);Tri(b,a,d,e,normal,normal,normal,bl,tr,tl);
            }
            public void Panel(Material m,Vector3 p,Vector2 size,Quaternion q,Rect uv)
            {
                var b=Get(m);var n=q*Vector3.back;
                Quad(b,p+q*new Vector3(-size.x/2,-size.y/2,0),p+q*new Vector3(-size.x/2,size.y/2,0),p+q*new Vector3(size.x/2,size.y/2,0),p+q*new Vector3(size.x/2,-size.y/2,0),n,new Rect(uv.xMin,uv.yMax,uv.width,-uv.height));
                // Explicit UVs preserve the drafted artwork's horizontal orientation.
                int start=b.uv.Count-6;b.uv[start]=new Vector2(uv.xMin,uv.yMin);b.uv[start+1]=new Vector2(uv.xMin,uv.yMax);b.uv[start+2]=new Vector2(uv.xMax,uv.yMax);b.uv[start+3]=new Vector2(uv.xMin,uv.yMin);b.uv[start+4]=new Vector2(uv.xMax,uv.yMax);b.uv[start+5]=new Vector2(uv.xMax,uv.yMin);
            }
            public void Box(Material m,Vector3 p,Vector3 size,float bevel)
            {
                var b=Get(m);Vector3 h=size*.5f;bevel=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.8f);Vector3 core=h-Vector3.one*bevel;
                foreach(Vector3 n in new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back})
                {
                    Vector3 u=Vector3.Cross(Mathf.Abs(n.y)>.5f?Vector3.forward:Vector3.up,n),v=Vector3.Cross(n,u);
                    float hu=Vector3.Dot(h,new Vector3(Mathf.Abs(u.x),Mathf.Abs(u.y),Mathf.Abs(u.z))),hv=Vector3.Dot(h,new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z)));
                    float hn=Vector3.Dot(h,new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z)));
                    float[] xs={-hu,-hu+bevel,hu-bevel,hu},ys={-hv,-hv+bevel,hv-bevel,hv};
                    for(int iy=0;iy<3;iy++)for(int ix=0;ix<3;ix++)
                    {
                        var vv=new Vector3[4];var nn=new Vector3[4];var uu=new Vector2[4];int[] dx={0,1,1,0},dy={0,0,1,1};
                        for(int k=0;k<4;k++)
                        {
                            Vector3 raw=n*hn+u*xs[ix+dx[k]]+v*ys[iy+dy[k]];
                            Vector3 clamped=new Vector3(Mathf.Clamp(raw.x,-core.x,core.x),Mathf.Clamp(raw.y,-core.y,core.y),Mathf.Clamp(raw.z,-core.z,core.z));
                            nn[k]=(raw-clamped).normalized;vv[k]=p+clamped+nn[k]*bevel;uu[k]=new Vector2(xs[ix+dx[k]],ys[iy+dy[k]])*.5f;
                        }
                        Tri(b,vv[0],vv[1],vv[2],nn[0],nn[1],nn[2],uu[0],uu[1],uu[2]);Tri(b,vv[0],vv[2],vv[3],nn[0],nn[2],nn[3],uu[0],uu[2],uu[3]);
                    }
                }
            }
            public void Cylinder(Material m,Vector3 p,float radius,float length,Quaternion q,int segments){Ring(m,p,radius,0,length,q,segments);}
            public void Ring(Material m,Vector3 p,float outer,float inner,float height,Quaternion q,int segments)
            {
                var b=Get(m);
                for(int i=0;i<segments;i++)
                {
                    float a=i*Mathf.PI*2/segments,z=(i+1)*Mathf.PI*2/segments;
                    Vector3 na=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),nb=new Vector3(Mathf.Cos(z),0,Mathf.Sin(z));
                    Vector3 a0=na*outer+Vector3.down*height/2,a1=na*outer+Vector3.up*height/2,b0=nb*outer+Vector3.down*height/2,b1=nb*outer+Vector3.up*height/2;
                    Quad(b,p+q*a0,p+q*a1,p+q*b1,p+q*b0,q*((na+nb).normalized),new Rect((float)i/segments,0,1f/segments,height));
                    Quad(b,p+q*a1,p+q*(na*inner+Vector3.up*height/2),p+q*(nb*inner+Vector3.up*height/2),p+q*b1,q*Vector3.up,new Rect(0,0,1,1));
                    Quad(b,p+q*b0,p+q*(nb*inner+Vector3.down*height/2),p+q*(na*inner+Vector3.down*height/2),p+q*a0,q*Vector3.down,new Rect(0,0,1,1));
                    if(inner>0)Quad(b,p+q*(na*inner+Vector3.down*height/2),p+q*(nb*inner+Vector3.down*height/2),p+q*(nb*inner+Vector3.up*height/2),p+q*(na*inner+Vector3.up*height/2),-(q*((na+nb).normalized)),new Rect(0,0,1,1));
                }
            }
            public void Rod(Material m,Vector3 from,Vector3 to,float radius){Cylinder(m,(from+to)*.5f,radius,Vector3.Distance(from,to),Quaternion.FromToRotation(Vector3.up,to-from),12);}
            public void Gear(Material m,Vector3 p,float radius,float h,int teeth)
            {
                Ring(m,p,radius*.82f,radius*.50f,h,Quaternion.identity,teeth*2);Cylinder(m,p,radius*.24f,h*1.3f,Quaternion.identity,24);
                for(int i=0;i<teeth;i++)
                {
                    float a=i*Mathf.PI*2/teeth;Vector3 n=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    Rod(m,p+n*radius*.79f,p+n*radius*.98f,radius*.048f);
                }
                for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Vector3 n=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));Rod(m,p+n*radius*.18f,p+n*radius*.70f,radius*.055f);}
            }
            public void Ribbon(Material m,Vector3 origin,float width,float length)
            {
                var b=Get(m);Quad(b,origin+Vector3.left*width,origin+Vector3.right*width,origin+new Vector3(width*2,-length,0),origin+new Vector3(-width*2,-length,0),Vector3.back,new Rect(0,0,1,length));
            }
            public void RoofLining(Material m)
            {
                var b=Get(m);float low=18.14f-13.16f*Mathf.Tan(16*Mathf.Deg2Rad);
                Quad(b,new Vector3(-13.16f,low,-13.25f),new Vector3(0,18.14f,-13.25f),new Vector3(0,18.14f,13.45f),new Vector3(-13.16f,low,13.45f),new Vector3(.276f,-.961f,0),new Rect(0,0,6.58f,13.35f));
                Quad(b,new Vector3(0,18.14f,-13.25f),new Vector3(13.16f,low,-13.25f),new Vector3(13.16f,low,13.45f),new Vector3(0,18.14f,13.45f),new Vector3(-.276f,-.961f,0),new Rect(0,0,6.58f,13.35f));
            }
            public void Commit(string prefix)
            {
                foreach(var pair in batches)
                {
                    var data=pair.Value;string name=prefix+"_"+pair.Key.name;string path=Folder+"/Meshes/"+name+".asset";
                    var vertices=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var indices=new List<int>();
                    var lookup=new Dictionary<(Vector3,Vector3,Vector2),int>();
                    foreach(int index in data.tri)
                    {
                        var key=(data.v[index],data.n[index],data.uv[index]);int target;
                        if(!lookup.TryGetValue(key,out target)){target=vertices.Count;lookup.Add(key,target);vertices.Add(key.Item1);normals.Add(key.Item2);uvs.Add(key.Item3);}
                        indices.Add(target);
                    }
                    var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uvs);mesh.SetTriangles(indices,0);mesh.SetColors(Enumerable.Repeat(Color.white,vertices.Count).ToList());mesh.RecalculateTangents();mesh.RecalculateBounds();
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
                    mesh.UploadMeshData(true);EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
                    var node=host.Find(name);if(!node){node=new GameObject(name).transform;node.SetParent(host,false);}
                    var filter=node.GetComponent<MeshFilter>();if(!filter)filter=node.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
                    var renderer=node.GetComponent<MeshRenderer>();if(!renderer)renderer=node.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                }
            }
        }
    }
}
