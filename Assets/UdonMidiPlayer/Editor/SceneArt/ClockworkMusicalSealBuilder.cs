using System.Collections.Generic;
using MusicMidiPlayer.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicMidiPlayer.Editor.SceneArt
{
    /// <summary>One shared strip mesh and material; no full-screen decals, lights, colliders or runtime mesh building.</summary>
    public static class ClockworkMusicalSealBuilder
    {
        private const string Folder = "Assets/Scenes/ClockworkMaintenanceStreet/MusicLink";
        public static void Configure(ClockworkMusicLink link)
        {
            string path = Folder + "/MusicalSeal.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var built = CreateMesh();
            if (mesh == null) { mesh = built; AssetDatabase.CreateAsset(mesh, path); }
            else { EditorUtility.CopySerialized(built, mesh); Object.DestroyImmediate(built); EditorUtility.SetDirty(mesh); }
            string matPath = Folder + "/MusicalSeal.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null) { material = new Material(Shader.Find("MusicMidiPlayer/Clockwork/MusicalSeal")); AssetDatabase.CreateAsset(material, matPath); }
            material.SetVector("_Activation", Vector4.zero);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            var host = link.transform.Find("MusicalSeals");
            if (host == null) { host = new GameObject("MusicalSeals").transform; host.SetParent(link.transform, false); }
            string[] names = { "01_PianoOrigin", "02_RoomExpansion", "03_DialReceiver", "04_LeftGear", "05_RightGear", "06_PendulumMedallion", "07_Crown", "08_WestFloor", "09_EastFloor" };
            var clock = link.mechanism;
            Vector3 center = link.facade.instrumentFanLayout.roots[0].parent.TransformPoint(link.facade.instrumentFanLayout.center);
            var positions = new[] { new Vector3(center.x,.075f,center.z), new Vector3(center.x,.083f,center.z), clock.dialCenter + Vector3.back*.34f,
                clock.gears[3].position+Vector3.back*.65f, clock.gears[0].position+Vector3.back*.65f,
                clock.pendulum.TransformPoint(Vector3.down*clock.pendulumLength)+Vector3.back*.65f,
                clock.gears[6].position+Vector3.back*.65f, new Vector3(-8.3f,.055f,.8f), new Vector3(8.3f,.055f,.8f) };
            var radii = new[] { 1.45f, 11.8f, 5.12f, link.gearRadii[3]*1.035f, link.gearRadii[0]*1.035f, .72f, link.gearRadii[6]*1.09f, 1.45f, 1.45f };
            link.ritualNodes = new Renderer[9]; link.ritualAnchors = new Transform[9]; link.ritualDelays = new float[9];
            link.ritualAnchors[3] = clock.gears[3]; link.ritualAnchors[4] = clock.gears[0];
            link.ritualAnchors[5] = clock.pendulum; link.ritualAnchors[6] = clock.gears[6];
            for (int i = 0; i < names.Length; i++)
            {
                var child = host.Find(names[i]);
                if (child == null) { child = new GameObject(names[i]).transform; child.SetParent(host, false); }
                child.position = positions[i]; child.rotation = i <= 1 || i >= 7 ? Quaternion.Euler(90,0,0) : Quaternion.identity;
                child.localScale = Vector3.one*radii[i];
                var filter = child.GetComponent<MeshFilter>(); if (filter == null) filter = child.gameObject.AddComponent<MeshFilter>();
                var renderer = child.GetComponent<MeshRenderer>(); if (renderer == null) renderer = child.gameObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh; renderer.sharedMaterial = material; renderer.enabled = false;
                // The origin is half-sized; double its strip offsets to retain the original world-space ink width.
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_StrokeScale", i == 0 ? 2f : i >= 2 ? 1.8f : 1f);
                properties.SetFloat("_MinPixels", i >= 2 ? 2.4f : 1.6f);
                properties.SetFloat("_Contrast", i >= 2 ? .88f : .65f);
                renderer.SetPropertyBlock(properties);
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
                link.ritualNodes[i] = renderer;
                link.ritualDelays[i] = i <= 1 ? 0 : Vector3.Distance(positions[0], positions[i]) / 3.4f;
            }
            EditorUtility.SetDirty(material);
        }
        private static Mesh CreateMesh()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var centers = new List<Vector2>();
            var layers = new List<Vector2>(); var colors = new List<Color>(); var triangles = new List<int>();
            float layer = 0;
            System.Action<Vector2,Vector2,float,float> line = (a,b,width,band)=>{
                var n = new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f; int start=vertices.Count;
                vertices.Add(a-n); vertices.Add(a+n); vertices.Add(b-n); vertices.Add(b+n);
                uv.Add(new Vector2(0,0)); uv.Add(new Vector2(0,1)); uv.Add(new Vector2(1,0)); uv.Add(new Vector2(1,1));
                centers.Add(a); centers.Add(a); centers.Add(b); centers.Add(b);
                for(int i=0;i<4;i++)layers.Add(new Vector2(layer,0));
                for(int i=0;i<4;i++)colors.Add(new Color(band,band*.5f,0,1));
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start+2);triangles.Add(start+1);triangles.Add(start+3);
            };
            System.Func<float,float,Vector2> polar = (r,a)=>new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;
            float[] radii = { .18f,.31f,.49f,.55f,.83f,.96f,1f };
            for(int ring=0;ring<radii.Length;ring++)
            {
                layer = new[] {0,1,3,4,6,8,9}[ring];
                for(int step=0;step<144;step++)
                {
                    if(ring==2 && step%36>29)continue;
                    float a=step*Mathf.PI*2/144,b=(step+1)*Mathf.PI*2/144;
                    line(polar(radii[ring],a),polar(radii[ring],b),ring==6?.0024f:.0032f,ring%2);
                }
            }
            // Counter-rotating inscribed triangles and a twelve-point orbital lattice.
            layer = 3;
            for(int shape=0;shape<2;shape++)for(int i=0;i<3;i++)
                line(polar(.47f,i*Mathf.PI*2/3+shape*Mathf.PI),polar(.47f,(i+1)*Mathf.PI*2/3+shape*Mathf.PI),.003f,shape);
            for(int i=0;i<72;i++)
            {
                layer = 7;
                float a=i*Mathf.PI*2/72;
                line(polar(i%6==0?.865f:.90f,a),polar(.944f,a),i%6==0?.004f:.0026f,1);
            }
            for(int i=0;i<24;i++)
            {
                layer = 5;
                float a=i*Mathf.PI*2/24; Vector2 p=polar(.70f,a),radial=polar(1,a),tangent=new Vector2(-radial.y,radial.x);
                // Compact geometric musical glyphs, each made of four thin strokes.
                line(p-radial*.035f,p+radial*.035f,.004f,0);
                line(p+radial*.035f,p+tangent*.018f,.004f,0);
                line(p+tangent*.018f,p-radial*.018f,.004f,0);
                if(i%2==0)line(p-radial*.018f,p-tangent*.018f,.004f,0);
                else line(p-radial*.025f-tangent*.021f,p-radial*.025f+tangent*.021f,.004f,0);
                if(i%2==0)
                {
                    layer = 2;
                    Vector2 q=polar(.40f,a);
                    line(q+radial*.025f,q+tangent*.012f,.003f,1); line(q+tangent*.012f,q-radial*.025f,.003f,1);
                    line(q-radial*.025f,q-tangent*.012f,.003f,1); line(q-tangent*.012f,q+radial*.025f,.003f,1);
                }
            }
            var mesh = new Mesh { name="Clockwork_MusicalSeal_Strips" };
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,centers);mesh.SetUVs(2,layers);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds();return mesh;
        }
    }
}
