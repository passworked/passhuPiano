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
using MusicMidiPlayer.Editor.Import;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiPercussionBuilder
    {
        public const string AudioFolder="Assets/UdonMidiPlayer/Audio/VirtuosityDrums";
        public const string ModelPath="Assets/UdonMidiPlayer/Models/VirtuosityDrumKit.fbx";
        private const string MaterialsFolder="Assets/UdonMidiPlayer/Materials/DrumKit";
        [Serializable] private class Mapping { public Region[] rows; public int clipCount,regionCount; public long float32Bytes,compressedBytes; }
        [Serializable] private class Region
        {
            public string file,source,sha256,substitution;
            public int note,lane,lowVelocity,highVelocity,roundRobin,roundRobinCount,sourceHi;
            public float gain;
        }
        [MenuItem("Tools/Music MIDI Player/Install or Update GM Drum Kit")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var players=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MidiPlayerFacade>(true)).ToArray();
            if(players.Length!=1)throw new InvalidOperationException("Open the merged player scene first.");
            var player=players[0];
            if(player.voicePool.voiceSources.Length!=64)throw new InvalidOperationException("Expected the existing shared 64-source output.");
            string manifestPath=AudioFolder+"/BANK-MAPPING.json";
            if(!File.Exists(manifestPath) || !File.Exists(ModelPath))throw new FileNotFoundException("Run prepare_percussion.py and build_drum_kit.py first.");
            Directory.CreateDirectory("RecoveryBackups/GMDrums-20261031");
            EditorSceneManager.SaveScene(scene);
            string backup="RecoveryBackups/GMDrums-20261031/"+Path.GetFileName(scene.path);
            if(!File.Exists(backup))File.Copy(scene.path,backup);
            var map=JsonUtility.FromJson<Mapping>(File.ReadAllText(manifestPath));
            ImportAudio(map);
            MidiPlayerBuilder.EnsurePrograms();
            Transform root=player.transform.root;
            var bank=Component<PercussionBankData>(Child(root.Find("Data"),"PercussionBank"));
            MidiAudioPacking.BakeWithSourcePcm(map.rows.Select(row => row.file), () => BakeBank(bank,map));
            Transform presentation=Child(root.Find("Presentation"),"DrumKit");
            bool fresh=presentation.GetComponent<MidiDrumKitView>()==null;
            if(fresh)presentation.localPosition=new Vector3(3.15f,0,1.45f);
            Transform model=presentation.Find("Model");
            if(model==null)
            {
                var obj=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),presentation);
                obj.name="Model";model=obj.transform;model.localRotation=Quaternion.Euler(0,180,0);
            }
            ConfigureMaterials(model);
            var view=Component<MidiDrumKitView>(presentation);
            var lanes=Component<MidiPercussionLaneView>(Child(presentation,"PercussionLanes"));
            view.bank=bank;view.lanes=lanes;lanes.bank=bank;
            view.heads=new Transform[9];view.cymbals=new Transform[9];view.sticks=new Transform[9];view.hitPoints=new Transform[9];
            view.glowTransforms=new Transform[9];view.glowRenderers=new Renderer[9];
            var all=model.GetComponentsInChildren<Transform>(true);
            for(int i=0;i<9;i++)
            {
                int lane=i;
                view.heads[i]=all.FirstOrDefault(t=>t.name=="HeadPivot_"+lane);
                view.cymbals[i]=all.FirstOrDefault(t=>t.name=="CymbalPivot_"+lane);
                view.sticks[i]=all.FirstOrDefault(t=>t.name=="StickPivot_"+lane);
                view.hitPoints[i]=all.First(t=>t.name=="Hit_"+lane);
                var glow=Child(presentation,"Impact_"+i);
                glow.position=view.hitPoints[i].position+Vector3.up*.004f;
                glow.rotation=Quaternion.Euler(90,0,0);glow.localScale=Vector3.one*.2f;
                var filter=Get<MeshFilter>(glow);filter.sharedMesh=PrimitiveMesh(PrimitiveType.Quad);
                var renderer=Get<MeshRenderer>(glow);renderer.sharedMaterial=GlowMaterial();renderer.enabled=false;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                view.glowTransforms[i]=glow;view.glowRenderers[i]=renderer;
            }
            view.kickBeater=all.First(t=>t.name=="KickBeaterPivot");
            view.kickPedal=all.First(t=>t.name=="KickPedalPivot");view.hiHatPedal=all.First(t=>t.name=="HiHatPedalPivot");
            lanes.hitPoints=view.hitPoints;lanes.bars=new Transform[64];
            var crystal=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/UdonMidiPlayer/Meshes/WaterfallCrystal.asset");
            var noteMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/UdonMidiPlayer/Materials/WaterfallVolume.mat");
            for(int i=0;i<64;i++)
            {
                var bar=Child(lanes.transform,"HitNote_"+i.ToString("00"));
                Get<MeshFilter>(bar).sharedMesh=crystal;
                var renderer=Get<MeshRenderer>(bar);renderer.sharedMaterial=noteMaterial;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                bar.gameObject.SetActive(false);lanes.bars[i]=bar;
            }
            var pool=Component<PercussionVoicePool>(Child(root.Find("Runtime"),"PercussionAudio"));
            pool.output=player.voicePool;pool.bank=bank;pool.clock=player.clock;pool.hitPoints=view.hitPoints;
            player.voicePool.drums=pool;player.clock.drums=pool;player.drumView=view;
            MidiPlayerUIBuilder.ConfigurePercussionControl(player.playerUI);
            player.playerUI.ApplyLanguage();
            var enabledSongs=EnableLibraryPercussion(player);
            player.library.catalogHash=MidiInstrumentMigration.InstrumentCatalogHash(player,
                MidiContentHash.Catalog(Configuration(player).library,Configuration(player).bank));
            player.network.catalogHash=player.library.catalogHash;
            foreach(var behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {UdonSharpEditorUtility.CopyProxyToUdon(behaviour,ProxySerializationPolicy.All);EditorUtility.SetDirty(behaviour);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/percussion-install.json",JsonUtility.ToJson(new InstallReport {
                clipCount=map.clipCount,regions=map.regionCount,compressedBytes=map.compressedBytes,float32Bytes=map.float32Bytes,
                sharedSources=player.voicePool.voiceSources.Length,reimportedSongs=enabledSongs.ToArray(),bankHash=bank.bankHash,
                modelTriangles=model.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh==null?0:f.sharedMesh.triangles.Length/3),
                modelRenderers=model.GetComponentsInChildren<Renderer>(true).Length},true));
        }
        [Serializable] private class InstallReport
        {public int clipCount,regions,sharedSources,modelTriangles,modelRenderers;public long compressedBytes,float32Bytes;public string bankHash;public string[] reimportedSongs;}
        private static void ImportAudio(Mapping map)
        {
            foreach(string path in map.rows.Select(r=>r.file).Distinct())
            {
                var importer=AssetImporter.GetAtPath(path) as AudioImporter;
                if(importer==null){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=AssetImporter.GetAtPath(path) as AudioImporter;}
                if(importer==null)throw new InvalidOperationException("Audio not imported: "+path);
                var settings=importer.defaultSampleSettings;
                bool changed=settings.compressionFormat!=AudioCompressionFormat.PCM || settings.loadType!=AudioClipLoadType.DecompressOnLoad
                    || settings.sampleRateSetting!=AudioSampleRateSetting.PreserveSampleRate || !settings.preloadAudioData || importer.loadInBackground || importer.forceToMono;
                if(changed)
                {
                    settings.compressionFormat=AudioCompressionFormat.PCM;settings.loadType=AudioClipLoadType.DecompressOnLoad;
                    settings.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;
                    importer.loadInBackground=false;importer.forceToMono=false;
                }
                if(MidiAudioPacking.Configure(importer) || changed)importer.SaveAndReimport();
            }
        }
        private static void BakeBank(PercussionBankData bank,Mapping map)
        {
            int n=map.rows.Length;
            bank.clips=new AudioClip[n];bank.notes=new int[n];bank.lowVelocity=new int[n];bank.highVelocity=new int[n];
            bank.roundRobin=new int[n];bank.roundRobinCount=new int[n];bank.referenceVelocity=new int[n];bank.lanes=new int[n];
            bank.frames=new int[n];bank.gains=new float[n];bank.peaks=new float[n];bank.noteOffsets=new int[129];bank.maxTailMs=0;
            int cursor=0;
            for(int p=0;p<129;p++){while(cursor<n && map.rows[cursor].note<p)cursor++;bank.noteOffsets[p]=cursor;}
            for(int i=0;i<n;i++)
            {
                var row=map.rows[i];var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(row.file);
                if(clip==null)throw new InvalidOperationException("Missing clip: "+row.file);
                if(MidiContentHash.Bytes(File.ReadAllBytes(row.file))!=row.sha256)throw new InvalidOperationException("Sample checksum mismatch: "+row.file);
                bank.clips[i]=clip;bank.notes[i]=row.note;bank.lowVelocity[i]=row.lowVelocity;bank.highVelocity[i]=row.highVelocity;
                bank.roundRobin[i]=row.roundRobin;bank.roundRobinCount[i]=row.roundRobinCount;bank.referenceVelocity[i]=row.sourceHi;
                bank.lanes[i]=row.lane;bank.frames[i]=clip.samples;bank.gains[i]=row.gain;
                var samples=new float[clip.samples*clip.channels];if(!clip.GetData(samples,0))throw new InvalidOperationException("Cannot read audio: "+row.file);
                float peak=0;foreach(float sample in samples)peak=Mathf.Max(peak,Mathf.Abs(sample));bank.peaks[i]=peak;
                bank.maxTailMs=Mathf.Max(bank.maxTailMs,Mathf.CeilToInt(clip.length*1000));
            }
            AudioEnergyBaker.Bake(bank);
            bank.bankHash=ComputeBankHash(bank);
            if(!bank.IsValid())throw new InvalidOperationException("Invalid GM drum bank.");
            for(int p=35;p<=81;p++)for(int v=1;v<=127;v++)for(int rr=0;rr<2;rr++)
                if(bank.SelectRegion(p,v,rr)<0)throw new InvalidOperationException("Unmapped GM note/velocity: "+p+"/"+v);
        }
        public static string ComputeBankHash(PercussionBankData bank)
        {
            var text=new System.Text.StringBuilder("gm-drums-v2:oneshot:hat-choke25:shared64-reserve16:off-note-ignored:stable-event-hash:");
            AudioEnergyBaker.Validate(bank.clips, bank.energyStarts, bank.energyCounts, bank.energyValues);
            text.Append(AudioEnergyBaker.Fingerprint(bank.energyStarts, bank.energyCounts, bank.energyValues));
            text.Append(File.ReadAllText(AudioFolder+"/BANK-MAPPING.json"));
            foreach(string path in Directory.GetFiles(AudioFolder,"*.ogg").OrderBy(p=>p,StringComparer.Ordinal))
                text.Append(MidiContentHash.Bytes(File.ReadAllBytes(path))).Append(MidiContentHash.Bytes(MidiAudioPacking.ReadBankMetadata(path.Replace('\\', '/'))));
            return MidiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        }
        private static MidiPlayerConfigurationAsset Configuration(MidiPlayerFacade player)
        {return AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(player.library.configurationAssetGuid));}
        private static List<string> EnableLibraryPercussion(MidiPlayerFacade player)
        {
            var config=Configuration(player);var changed=new List<string>();
            foreach(var song in config.library.songs)
            {
                if(song==null || !song.importSettings.excludePercussion)continue;
                string path=AssetDatabase.GUIDToAssetPath(song.sourceGuid);if(string.IsNullOrEmpty(path))path=song.sourcePath;
                if(!File.Exists(path))continue;
                var settings=JsonUtility.FromJson<MidiImportSettings>(JsonUtility.ToJson(song.importSettings));settings.excludePercussion=false;
                var parsed=SmfParser.Parse(File.ReadAllBytes(path),settings);
                if(!parsed.channel.Any(c=>c==9))continue;
                MidiImportService.ImportAsset(path,Path.GetDirectoryName(AssetDatabase.GetAssetPath(song)).Replace('\\','/'),settings,song);
                changed.Add(song.title);
            }
            string demoPath="Assets/UdonMidiPlayer/Samples/GMDrumStudy.mid";
            if(File.Exists(demoPath))
            {
                var demo=config.library.songs.FirstOrDefault(s=>s!=null && s.sourcePath==demoPath);
                demo=MidiImportService.ImportAsset(demoPath,"Assets/UdonMidiPlayer/Editor/Generated/Songs",new MidiImportSettings{excludePercussion=false},demo);
                demo.title="GM Drum Study";demo.author="Music MIDI Player";demo.contentHash=MidiContentHash.Song(demo);EditorUtility.SetDirty(demo);
                if(!config.library.songs.Contains(demo))config.library.songs=config.library.songs.Concat(new[]{demo}).ToArray();
                const string folder="Demo/Drums";
                var entry=config.library.folders.FirstOrDefault(f=>f.path==folder);
                if(entry==null)config.library.folders=config.library.folders.Concat(new[]{new MidiLibraryFolder{path=folder,songs=new[]{demo}}}).ToArray();
                else if(!entry.songs.Contains(demo))entry.songs=entry.songs.Concat(new[]{demo}).ToArray();
                EditorUtility.SetDirty(config.library);
            }
            MidiPlayerBuilder.SynchronizePlayerLibrary(player);return changed;
        }
        private static Transform Child(Transform parent,string name)
        {var t=parent.Find(name);if(t!=null)return t;var obj=new GameObject(name);obj.transform.SetParent(parent,false);return obj.transform;}
        private static T Component<T>(Transform t)where T:UdonSharpBehaviour
        {var component=t.GetComponent<T>();return component!=null?component:t.gameObject.AddUdonSharpComponent<T>();}
        private static T Get<T>(Transform t)where T:UnityEngine.Component
        {var component=t.GetComponent<T>();return component!=null?component:t.gameObject.AddComponent<T>();}
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {var obj=GameObject.CreatePrimitive(type);var mesh=obj.GetComponent<MeshFilter>().sharedMesh;UnityEngine.Object.DestroyImmediate(obj);return mesh;}
        private static Material GlowMaterial()
        {
            return Material("DrumImpact",new Color(.25f,.85f,.9f,0),0,.3f,"MusicMidiPlayer/WaterfallImpactGlow");
        }
        private static Material Material(string name,Color color,float metallic,float smoothness,string shader="Standard")
        {
            MidiImportService.EnsureFolder(MaterialsFolder);string path=MaterialsFolder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            mat=new Material(Shader.Find(shader));mat.name=name;mat.color=color;
            if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",metallic);
            if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",smoothness);
            if(mat.HasProperty("_EmissionColor"))mat.SetColor("_EmissionColor",new Color(.15f,.6f,.8f));
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        private static void ConfigureMaterials(Transform model)
        {
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string name=materials[i]==null?renderer.name:materials[i].name;
                    Color color=new Color(.38f,.045f,.06f);float metal=.15f,smooth=.65f;
                    if(name.Contains("Chrome")){color=new Color(.58f,.60f,.64f);metal=.85f;smooth=.6f;}
                    else if(name.Contains("Brass")){color=new Color(.7f,.49f,.17f);metal=.8f;smooth=.55f;}
                    else if(name.Contains("Heads")){color=new Color(.83f,.82f,.76f);metal=0;smooth=.2f;}
                    else if(name.Contains("Rubber")){color=new Color(.018f,.024f,.03f);metal=0;smooth=.2f;}
                    else if(name.Contains("Maple")){color=new Color(.66f,.43f,.22f);metal=0;smooth=.3f;}
                    materials[i]=Material(name,color,metal,smooth);
                }
                renderer.sharedMaterials=materials;
            }
        }
    }
}
