using System;
using System.IO;
using System.Linq;
using MusicMidiPlayer.Audio;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiInstrumentRoutingBuilder
    {
        public const string RulesVersion = "selected-gm-v13-track-pitch";
        // First named part wins for mixed labels. Unrecognized names retain GM routing.
        // 128..134 are approximate percussion roles, not instrument target IDs.
        private static readonly string[] TrackKeywords =
        {
            "piano|truepianos|close grand|e.p|ep|rho|rhodes|harpci|harpsi|harpsicord|harpsichord",
            "bass|basss|bassss|basssss|bassssss|basseses|bassesese|bassesesese|contrabass|double bass",
            "violin|vio", "viola", "cello", "string|strings|srting|stirng|arco|orch|orche|orchestra|pad|atmos|organ|choir|chorus",
            "music box|musicbox|八音盒|オルゴール",
            "marimba|marim|bell|xilo|xiol|xylo|xylophone|pluck|arp|harp|guitar|e.guitar|eg|e.g|e g|gita|aco",
            "lead|melody|meldoy|synth|sytnh|square|chiptune|chip|vocal|voice|vox|voc|vocie|miku|rin|len|luka|kaito|meiko|teto|brass|trump|flute|woodwind|woodwinds|recorder|whistle|ocarina|sax",
            "drum|drums|drumstick|breakcore|amen", "kick|kickk|kickkkk|kickkkkkk|kiiiickckckckck",
            "snare|snar e|clap", "hi-hat|hi hat|hihat|hat", "crash|cymbal", "ride", "tom"
        };
        private static readonly int[] TrackPrograms = { 0,32,40,41,42,48,10,12,80,128,129,130,131,132,133,134 };
        public static string[] AutoTrackKeywords() { return (string[])TrackKeywords.Clone(); }
        public static int[] AutoTrackPrograms() { return (int[])TrackPrograms.Clone(); }
        // P, M, organ/choir, solo treble, solo mid, solo low, bass,
        // ensemble, pizzicato ensemble, orchestra hit, tuned drums.
        private static readonly int[] Orders =
        {
            0,1,7,2,3,4,5,
            1,7,0,2,3,4,5,
            3,4,2,5,0,1,7,
            2,3,4,5,0,1,7,
            3,2,4,5,0,1,7,
            4,5,3,2,0,1,7,
            5,4,3,2,0,1,7,
            2,3,4,5,0,1,7,
            2,3,4,5,1,7,0,
            1,7,0,3,4,2,5,
            1,7,0,5,4,3,2,
            7,1,0,2,3,4,5
        };
        // The sixteen standard GM families, eight programs each.
        private static readonly int[] FamilyGroups = {0,1,2,1,6,7,7,3,3,3,3,7,0,1,1,0};
        // Program, template. Only differences from the family default are authored.
        private static readonly int[] Exceptions =
        {
            10,11, 22,3, 29,3, 30,3,
            40,3, 41,4, 42,5, 43,6, 45,8, 46,1, 47,10,
            52,2, 53,2, 54,2, 55,9,
            57,5, 58,6, 60,4, 61,7, 62,7, 63,7,
            65,4, 66,5, 67,6, 69,4, 70,6, 87,6,
            97,7, 98,1, 99,7, 100,1, 101,7, 103,7,
            109,3, 110,3, 111,3, 116,10, 117,10, 118,10, 119,0,
            121,2, 122,2, 123,3, 124,1, 125,6, 126,10, 127,10
        };
        public static int[] ProgramGroups()
        {
            var groups = Enumerable.Range(0,128).Select(p => FamilyGroups[p / 8]).ToArray();
            for (int i=0;i<Exceptions.Length;i+=2) groups[Exceptions[i]]=Exceptions[i+1];
            return groups;
        }
        // Source manifests distinguish actual recordings from octave-derived auxiliary regions.
        public static int[][] RecordedRanges()
        {
            var selected=JObject.Parse(File.ReadAllText("Assets/UdonMidiPlayer/Audio/SelectedQ4/BANK-MAPPING.json"));
            var strings=JObject.Parse(File.ReadAllText("Assets/UdonMidiPlayer/Audio/Strings/BANK-MAPPING.json"));
            var box=JObject.Parse(File.ReadAllText("Assets/UdonMidiPlayer/Audio/MusicBox/BANK-MAPPING.json"));
            var low=new int[8];var high=Enumerable.Repeat(127,8).ToArray();
            string[] names={"SoloViolin","ViolaSection","CelloSection","Contrabass"};
            for(int target=0;target<8;target++)
            {
                if(target==6)continue;
                int[] pitches=target<2
                    ? selected[target==0?"piano":"marimba"].Where(r=>r["derivedFrom"]==null).Select(r=>(int)r["pitch"]).ToArray()
                    : target==7 ? box["sources"].Select(r=>(int)r["pitch"]).ToArray()
                    : strings["regions"].Where(r=>(string)r["bank"]==names[target-2] && r["derivedFrom"]==null).Select(r=>(int)r["root"]).ToArray();
                if(pitches.Length==0 || pitches.Min()<0 || pitches.Max()>127 || pitches.Max()-pitches.Min()<11)
                    throw new InvalidOperationException("Original sample range must cover every pitch class: "+target);
                low[target]=pitches.Min();high[target]=pitches.Max();
            }
            return new[]{low,high};
        }
        public static string Fingerprint()
        {
            int[][] ranges = RecordedRanges();
            return MidiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(RulesVersion+":"+
                "drum-selection=shared-mask-bit8-default-on,valid447,menu9,file-and-live-gate,classification-retained;"+
                "native-ranges="+string.Join(",",ranges[0])+":"+string.Join(",",ranges[1])+":track-pitch=P0/P1-seven-char-track-target-octave0-K,minus10-plus10,resolve-target-before-octave-fold,drums-unshifted;"+string.Join(",", ProgramGroups())+":"+string.Join(",", Orders)+":"+ViolinArticulationBaker.RulesVersion+":"+string.Join(";",TrackKeywords)+":"+string.Join(",",TrackPrograms)+":articulation=pizz32-34,36,37,45;spicc55;strings=55-96,48-86,36-77,24-60,overlap-midpoint-lower-tie,exact40-43;file-map=shared128digits0-6,8-9,file-only,9-drums-original-gm-pitch;track-map=shared-sorted5digit-index-plus-target1-6,8-9;disabled-a-g,i-j-retains-target,a-auto;track-switch-before-all-file-routing,manual-track-before-program-before-opt-in-role-before-gm,channel9-default-only;auto-track=shared-bool-default-off,first-word-boundary-match,bass-fixed-priority,drums-register36-38-45-47-42-49;manual=target-events3-8,10,snapshot-target20-23;string-style=target2-5,2bits-auto-sustain-spiccato-pizzicato"));
        }
        public static void Bake(MidiInstrumentRouter router)
        {
            router.programGroup=ProgramGroups(); router.priorityOrder=(int[])Orders.Clone(); router.rulesHash=Fingerprint();
            int[][] ranges=RecordedRanges();router.nativeRangeLow=ranges[0];router.nativeRangeHigh=ranges[1];
            router.autoTrackKeywords=AutoTrackKeywords(); router.autoTrackPrograms=AutoTrackPrograms();
            router.stringRangeLow=new[]{55,48,36,24}; router.stringRangeHigh=new[]{96,86,77,60};
            router.enabledMask &= 447;
            // Existing scene overrides retain the old 896-entry array after recompilation.
            router.keyCounts = new int[1024];
            ValidateRules(router);
        }
        public static int ValidateRules(MidiInstrumentRouter router)
        {
            if(router.programGroup.Length!=128 || router.priorityOrder.Length!=84) throw new InvalidOperationException("Invalid GM routing table size.");
            if(router.keyCounts.Length!=1024) throw new InvalidOperationException("Rebake the expanded target key-state array.");
            int[][] ranges=RecordedRanges();
            if(!router.nativeRangeLow.SequenceEqual(ranges[0]) || !router.nativeRangeHigh.SequenceEqual(ranges[1]))
                throw new InvalidOperationException("Rebake original recorded pitch ranges before validating the scene.");
            if(!router.autoTrackKeywords.SequenceEqual(TrackKeywords) || !router.autoTrackPrograms.SequenceEqual(TrackPrograms))
                throw new InvalidOperationException("Rebake changed automatic track rules before validating the scene.");
            if(!router.priorityOrder.SequenceEqual(Orders)) throw new InvalidOperationException("Rebake changed priority templates before validating the scene.");
            if(!router.stringRangeLow.SequenceEqual(new[]{55,48,36,24}) || !router.stringRangeHigh.SequenceEqual(new[]{96,86,77,60}))
                throw new InvalidOperationException("Rebake changed string ranges before validating the scene.");
            int checks=0;
            for(int group=0;group<12;group++)
                if(!router.priorityOrder.Skip(group*7).Take(7).OrderBy(x=>x).SequenceEqual(new[]{0,1,2,3,4,5,7}))
                    throw new InvalidOperationException("Every priority template must contain all seven active targets exactly once.");
            for(int p=0;p<128;p++)
            {
                int group=router.programGroup[p];
                if(group<0 || group>=12) throw new InvalidOperationException("Invalid priority group for "+p);
                for(int mask=1;mask<256;mask++)
                {
                    if ((mask & ~191) != 0) continue;
                    int target=router.priorityOrder.Skip(group*7).Take(7).FirstOrDefault(t=>(mask&(1<<t))!=0);
                    if((mask&(1<<target))==0) throw new InvalidOperationException("Routing escaped the enabled selection.");
                    checks++;
                }
            }
            return checks;
        }

        [MenuItem("Tools/Music MIDI Player/Update Automatic Track Routing")]
        public static void UpdateAutomaticTrackRouting()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Open the main scene outside Play mode.");
            var f=UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if(f==null || f.instrumentRouter==null) throw new InvalidOperationException("The main-scene router is required.");
            string backup="RecoveryBackups/AutoTrackRouting-20261104/BeforeUpdate-InMemory.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if(scene.isDirty && !File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true))
                throw new InvalidOperationException("Could not preserve unsaved scene work.");
            Bake(f.instrumentRouter);
            if(f.drumView!=null && f.drumView.lanes!=null) f.drumView.lanes.instrumentRouter=f.instrumentRouter;
            f.network.autoTrackRouting=f.instrumentRouter.autoTrackRouting;
            MidiHardwareMappingUIBuilder.ConfigurePlaybackMapping(f.playerUI);
            f.playerUI.ApplyLanguage();
            var config=AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(f.library.configurationAssetGuid));
            f.library.catalogHash=MidiInstrumentMigration.InstrumentCatalogHash(f,MidiContentHash.Catalog(config.library,config.bank));
            f.network.catalogHash=f.library.catalogHash;
            foreach(var b in new UdonSharpBehaviour[]{f,f.instrumentRouter,f.network,f.library,f.playerUI,f.drumView==null?null:f.drumView.lanes})
                if(b!=null) { UdonSharpEditorUtility.CopyProxyToUdon(b,ProxySerializationPolicy.All); EditorUtility.SetDirty(b); }
            MidiPlayerUIBuilder.ConfigureHierarchy((RectTransform)f.playerUI.transform);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Updated optional track adaptation, file channel-10 overrides and main-scene UI; no repertoire or distribution prefab changes.");
        }

        [MenuItem("Tools/Music MIDI Player/Update String Routing and Playback Mapping")]
        public static void UpdatePlaybackMapping()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Open the main scene outside Play mode.");
            var f=UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if(f==null || f.instrumentRouter==null) throw new InvalidOperationException("The existing main-scene router is required.");
            string backup="RecoveryBackups/StringsRangeFileMapping-20261101/BeforeUpdate-InMemory.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if(scene.isDirty && !File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true))
                throw new InvalidOperationException("Could not preserve unsaved scene work.");
            Bake(f.instrumentRouter); f.instrumentRouter.ResolveSelection();
            if(f.instrumentRoots.Length>7 && f.instrumentRoots[7]!=null) f.instrumentRoots[7].SetActive(false);
            if(f.selectedInstrument==7) f.selectedInstrument=f.instrumentRouter.pianoAppearance;
            f.network.enabledInstrumentMask=f.InstrumentMask();
            MidiPlayerUIBuilder.ConfigureInstruments(f.playerUI);
            MidiHardwareMappingUIBuilder.Configure(f.playerUI);
            f.playerUI.ApplyLanguage();
            var config=AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(f.library.configurationAssetGuid));
            f.library.catalogHash=MidiInstrumentMigration.InstrumentCatalogHash(f,MidiContentHash.Catalog(config.library,config.bank));
            f.network.catalogHash=f.library.catalogHash;
            foreach(var b in new UdonSharpBehaviour[]{f,f.instrumentRouter,f.network,f.library,f.playerUI})
            { UdonSharpEditorUtility.CopyProxyToUdon(b,ProxySerializationPolicy.All); EditorUtility.SetDirty(b); }
            MidiPlayerUIBuilder.ConfigureHierarchy((RectTransform)f.playerUI.transform);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Updated pitch-aware string fallback and playback timbre mapping in the main scene only.");
        }

        [MenuItem("Tools/Music MIDI Player/Install Instrument Checkboxes")]
        public static void Install()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!=MidiInstrumentMigration.MainScene)
                throw new InvalidOperationException("Open the main scene outside Play mode before installing instrument checkboxes.");
            var f=UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
            if(f==null || f.instrumentRoots.Length!=8 || f.voicePool.strings==null) throw new InvalidOperationException("The existing eight instrument bindings are required.");
            string backup="RecoveryBackups/InstrumentSelection-20261101/";
            Directory.CreateDirectory(backup);
            if(!File.Exists(backup+scene.name+".unity")) File.Copy(scene.path,backup+scene.name+".unity");
            if(scene.isDirty && !File.Exists(backup+"BeforeRouting-InMemory.unity") && !EditorSceneManager.SaveScene(scene,backup+"BeforeRouting-InMemory.unity",true))
                throw new InvalidOperationException("Could not preserve the current in-memory scene before the incremental update.");
            MidiPlayerBuilder.EnsurePrograms();
            Transform data=f.transform.root.Find("Data");
            Transform host=data.Find("InstrumentRouter");
            if(host==null) { host=new GameObject("InstrumentRouter").transform; host.SetParent(data,false); }
            var router=host.GetComponent<MidiInstrumentRouter>() ?? host.gameObject.AddUdonSharpComponent<MidiInstrumentRouter>();
            bool fresh=f.instrumentRouter==null;
            router.facade=f; Bake(router);
            if(fresh)
            {
                router.enabledMask=(1<<(f.selectedInstrument<2?0:f.selectedInstrument-1)) | (f.voicePool.drums != null ? 256 : 0);
                router.pianoAppearance=f.selectedInstrument<2?f.selectedInstrument:1;
                router.viewRestPositions=f.instrumentRoots.Select(r=>r.transform.localPosition).ToArray();
                router.stageOffsets=new[] { Vector3.zero, new Vector3(-2.8f,0,0), new Vector3(-1.8f,0,2.4f),
                    new Vector3(-.6f,0,2.4f),new Vector3(.7f,0,2.4f),new Vector3(2f,0,2.4f),new Vector3(0,0,4.8f) };
            }
            var pool=f.voicePool.GetComponent<MidiEnsembleVoicePool>() ?? f.voicePool.gameObject.AddUdonSharpComponent<MidiEnsembleVoicePool>();
            pool.output=f.voicePool; pool.router=router;
            pool.soundOrigins=new Transform[f.instrumentRoots.Length-1];
            pool.soundOrigins[0]=f.instrumentKeyboards[router.pianoAppearance].transform;
            pool.soundOrigins[1]=f.instrumentKeyboards[2].transform;
            for(int t=2;t<7;t++) pool.soundOrigins[t]=f.GetStringsView(t+1).bodies[0];
            if(f.instrumentRoots.Length>8) pool.soundOrigins[7]=f.instrumentKeyboards[8].transform;
            f.instrumentRouter=router; f.voicePool.ensemble=pool; f.clock.instrumentRouter=router;
            if (f.GetStringsView(3).stringParticles.Length != f.GetStringsView(3).strings.Length)
                MidiStringsBuilder.UpdatePresentation(f);
            for(int i=0;i<8;i++)
            {
                if(f.instrumentKeyboards[i]!=null) { f.instrumentKeyboards[i].instrumentRouter=router; f.instrumentKeyboards[i].instrumentTarget=router.TargetForView(i); }
                if(f.instrumentWaterfalls[i]!=null) { f.instrumentWaterfalls[i].instrumentRouter=router; f.instrumentWaterfalls[i].instrumentTarget=router.TargetForView(i); }
                // Preserve the independently maintained strings glow and particles.
                var view=f.GetStringsView(i);
                if(view!=null)
                {
                    var so=new SerializedObject(view); var target=so.FindProperty("routedTarget");
                    if(target!=null) { target.intValue=router.TargetForView(i); so.ApplyModifiedPropertiesWithoutUndo(); }
                }
                f.instrumentRoots[i].SetActive(router.IsViewEnabled(i));
            }
            f.network.enabledInstrumentMask=router.enabledMask; f.network.pianoAppearance=router.pianoAppearance;
            var config=AssetDatabase.LoadAssetAtPath<MidiPlayerConfigurationAsset>(AssetDatabase.GUIDToAssetPath(f.library.configurationAssetGuid));
            f.library.catalogHash=MidiInstrumentMigration.InstrumentCatalogHash(f,MidiContentHash.Catalog(config.library,config.bank));
            f.network.catalogHash=f.library.catalogHash;
            MidiPlayerUIBuilder.ConfigureInstruments(f.playerUI);
            f.playerUI.ApplyLanguage();
            foreach(var b in f.transform.root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            { UdonSharpEditorUtility.CopyProxyToUdon(b,ProxySerializationPolicy.All); EditorUtility.SetDirty(b); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("Instrument checkboxes installed; "+ValidateRules(router)+" routing selections validated. Main scene only; no prefab or song data changed.");
        }
    }
}
