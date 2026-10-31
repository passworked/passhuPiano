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

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiInstrumentRoutingBuilder
    {
        public const string RulesVersion = "selected-gm-v4-independent-string-style";
        // P, M, organ/choir, solo treble, solo mid, solo low, bass,
        // ensemble, pizzicato ensemble, orchestra hit, tuned drums.
        private static readonly int[] Orders =
        {
            0,1,2,3,4,5,
            1,0,2,3,4,5,
            3,4,2,5,0,1,
            2,3,4,5,0,1,
            3,2,4,5,0,1,
            4,5,3,2,0,1,
            5,4,3,2,0,1,
            2,3,4,5,0,1,
            2,3,4,5,1,0,
            1,0,3,4,2,5,
            1,0,5,4,3,2
        };
        // The sixteen standard GM families, eight programs each.
        private static readonly int[] FamilyGroups = {0,1,2,1,6,7,7,3,3,3,3,7,0,1,1,0};
        // Program, template. Only differences from the family default are authored.
        private static readonly int[] Exceptions =
        {
            22,3, 29,3, 30,3,
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
        public static string Fingerprint()
        {
            return MidiContentHash.Bytes(System.Text.Encoding.UTF8.GetBytes(RulesVersion+":"+
                string.Join(",", ProgramGroups())+":"+string.Join(",", Orders)+":articulation=pizz32-34,36,37,45;spicc55;strings=55-96,48-86,36-77,24-60,overlap-midpoint-lower-tie,exact40-43;file-map=shared128digits0-6,file-only;manual=target-events3-8,snapshot-target20;string-style=target2-5,2bits-auto-sustain-spiccato-pizzicato"));
        }
        public static void Bake(MidiInstrumentRouter router)
        {
            router.programGroup=ProgramGroups(); router.priorityOrder=(int[])Orders.Clone(); router.rulesHash=Fingerprint();
            router.stringRangeLow=new[]{55,48,36,24}; router.stringRangeHigh=new[]{96,86,77,60};
            router.enabledMask &= 63;
            ValidateRules(router);
        }
        public static int ValidateRules(MidiInstrumentRouter router)
        {
            if(router.programGroup.Length!=128 || router.priorityOrder.Length!=66) throw new InvalidOperationException("Invalid GM routing table size.");
            if(!router.priorityOrder.SequenceEqual(Orders)) throw new InvalidOperationException("Rebake changed priority templates before validating the scene.");
            if(!router.stringRangeLow.SequenceEqual(new[]{55,48,36,24}) || !router.stringRangeHigh.SequenceEqual(new[]{96,86,77,60}))
                throw new InvalidOperationException("Rebake changed string ranges before validating the scene.");
            int checks=0;
            for(int group=0;group<11;group++)
                if(!router.priorityOrder.Skip(group*6).Take(6).OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,6)))
                    throw new InvalidOperationException("Every priority template must contain all six targets exactly once.");
            for(int p=0;p<128;p++)
            {
                int group=router.programGroup[p];
                if(group<0 || group>=11) throw new InvalidOperationException("Invalid priority group for "+p);
                for(int mask=1;mask<64;mask++)
                {
                    int target=router.priorityOrder.Skip(group*6).Take(6).FirstOrDefault(t=>(mask&(1<<t))!=0);
                    if((mask&(1<<target))==0) throw new InvalidOperationException("Routing escaped the enabled selection.");
                    checks++;
                }
            }
            return checks;
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
                router.enabledMask=1<<(f.selectedInstrument<2?0:f.selectedInstrument-1);
                router.pianoAppearance=f.selectedInstrument<2?f.selectedInstrument:1;
                router.viewRestPositions=f.instrumentRoots.Select(r=>r.transform.localPosition).ToArray();
                router.stageOffsets=new[] { Vector3.zero, new Vector3(-2.8f,0,0), new Vector3(-1.8f,0,2.4f),
                    new Vector3(-.6f,0,2.4f),new Vector3(.7f,0,2.4f),new Vector3(2f,0,2.4f),new Vector3(0,0,4.8f) };
            }
            var pool=f.voicePool.GetComponent<MidiEnsembleVoicePool>() ?? f.voicePool.gameObject.AddUdonSharpComponent<MidiEnsembleVoicePool>();
            pool.output=f.voicePool; pool.router=router;
            pool.soundOrigins=new Transform[7];
            pool.soundOrigins[0]=f.instrumentKeyboards[router.pianoAppearance].transform;
            pool.soundOrigins[1]=f.instrumentKeyboards[2].transform;
            for(int t=2;t<7;t++) pool.soundOrigins[t]=f.GetStringsView(t+1).bodies[0];
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
