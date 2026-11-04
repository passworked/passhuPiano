using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using Newtonsoft.Json.Linq;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Explicit and bounded; never started by build, import or a menu rebuild.
    [InitializeOnLoad]
    public static class MidiMusicBoxValidation
    {
        const string Key="MMP.MusicBox.Probe";
        static readonly List<string> Checks=new List<string>();
        static MidiPlayerFacade f;
        static UdonBehaviour facade,router,pool,clock,seq,keys,fall;
        static GameObject temporary;
        static int stage;
        static double started;
        static MidiMusicBoxValidation()
        { if(SessionState.GetBool(Key,false)){started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;} }
        public static string Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Wait for the existing Play session to finish.");
            Checks.Clear();stage=0;started=EditorApplication.timeSinceStartup;SessionState.SetBool(Key,true);
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
            return "Started bounded music-box backing-Udon probe.";
        }
        static UdonBehaviour Back(UdonSharpBehaviour b){return UdonSharpEditorUtility.GetBackingUdonBehaviour(b);}
        static void Need(bool ok,string label){if(!ok)throw new InvalidOperationException(label);Checks.Add(label);}
        static void Call(UdonBehaviour b,string method,params object[] args)
        {
            var program=((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram();
            var symbols=program.SymbolTable.GetSymbols();
            for(int i=0;i<args.Length;i+=2)
            {
                var names=symbols.Where(s=>Regex.IsMatch(s,"^__\\d+_"+args[i]+"__param$")).ToArray();
                if(names.Length==0)throw new MissingMemberException(method+"."+args[i]);
                foreach(string name in names)b.SetProgramVariable(name,args[i+1]);
            }
            b.SendCustomEvent(program.EntryPoints.GetExportedSymbols().Contains(method)?method:"__0_"+method);
        }
        static void Tick()
        {
            if(!EditorApplication.isPlaying)return;
            try
            {
                if(EditorApplication.timeSinceStartup-started>55)throw new TimeoutException("Music box probe timed out.");
                if(stage==0)
                {
                    f=UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();if(f==null)return;
                    facade=Back(f);if(!(bool)facade.GetProgramVariable("isReady")||(bool)facade.GetProgramVariable("isSongLoading")||!(bool)Back(f.network).GetProgramVariable("networkReady"))return;
                    router=Back(f.instrumentRouter);pool=Back(f.voicePool.ensemble);clock=Back(f.clock);seq=Back(f.sequencer);keys=Back(f.instrumentKeyboards[8]);fall=Back(f.instrumentWaterfalls[8]);
                    Call(facade,"CommandStop");
                    for(int i=0;i<f.playerUI.instrumentToggles.Length;i++)if(i!=1)f.playerUI.instrumentToggles[i].isOn=false;
                    f.playerUI.instrumentToggles[1].isOn=true;f.playerUI.instrumentToggles[8].isOn=true;
                    Need((int)router.GetProgramVariable("enabledMask")==129,"Native checkbox enables piano and music box together");
                    Need(f.instrumentRoots[8].activeInHierarchy&&Vector3.Distance(f.instrumentRoots[1].transform.TransformPoint(f.musicBoxGrandPianoOffset),f.instrumentRoots[8].transform.position)<.001f,"Music box sits on the enabled grand piano");
                    Need(!f.musicBoxTable.activeSelf&&!f.musicBoxSeat.activeSelf,"Grand piano placement hides separate music box furniture");
                    Call(facade,"_ApplyInstrumentSelection","mask",129,"appearance",0);
                    Need(f.musicBoxTable.activeSelf&&f.musicBoxSeat.activeSelf&&Vector3.Distance(f.instrumentRoots[8].transform.localPosition,f.instrumentRouter.viewRestPositions[8]+f.instrumentRouter.stageOffsets[7])<.001f,"Digital piano retains the separate mixed-ensemble music box placement");
                    Call(facade,"_ApplyInstrumentSelection","mask",129,"appearance",1);
                    f.playerUI.instrumentToggles[1].isOn=false;
                    Need((int)router.GetProgramVariable("enabledMask")==128,"Native checkbox selects music box alone without retired target");
                    Need(f.musicBoxTable.activeSelf&&f.musicBoxSeat.activeSelf&&Vector3.Distance(f.instrumentRoots[8].transform.localPosition,f.instrumentRouter.viewRestPositions[8])<.001f,"Disabling grand piano restores the original solo music box placement");
                    Need(((int[])router.GetProgramVariable("resolvedPrograms")).All(t=>t==7),"Music-box-only selection resolves all 128 GM programs");
                    foreach(var pair in new[]{new[]{129,0,7},new[]{130,1,7},new[]{191,0,7}})
                    {
                        router.SetProgramVariable("enabledMask",pair[0]);Call(router,"ResolveSelection");
                        var resolved=(int[])router.GetProgramVariable("resolvedPrograms");
                        Need(resolved[0]==pair[1]&&resolved[10]==pair[2],"GM10 keeps music box priority for selection "+pair[0]);
                    }
                    router.SetProgramVariable("enabledMask",128);Call(router,"ResolveSelection");
                    foreach(string language in new[]{"_SelectLanguageEnglish","_SelectLanguageChinese","_SelectLanguageJapanese"})
                    {Back(f.playerUI).SendCustomEvent(language);Need(!f.playerUI.instrumentLabels[8].text.Contains("instrument."),"Native music box translation "+language);}
                    Need(f.performanceInput.instrumentFrames.Length==9&&f.performanceInput.keyMaximum.Length==9*88,"Physical comb touch lanes preserve the performance range");
                    Need(f.instrumentRoots[8].GetComponentInChildren<MidiPerformanceSeat>(true).instrumentIndex==8,"Music box desktop seat retains its own view identity");
                    // Use the actual file target button, then let the bounded routing loader finish.
                    var ui=Back(f.playerUI);ui.SendCustomEvent("_OpenFileMapping");
                    ui.SetProgramVariable("_fileTracks",false);ui.SetProgramVariable("_fileProgram",0);ui.SetProgramVariable("_fileChoiceSong",facade.GetProgramVariable("selectedSongId"));
                    f.playerUI.fileChoicePanel.SetActive(true);f.playerUI.fileTargetButtons[8].onClick.Invoke();
                    Need(((string)router.GetProgramVariable("fileProgramMap")).StartsWith("8"),"Native file target button writes digit8 for music box");
                    stage=1;return;
                }
                if(stage==1)
                {
                    if((bool)facade.GetProgramVariable("isSongLoading"))return;
                    Back(f.playerUI).SendCustomEvent("_CloseFileMapping");
                    // Stop autonomous transport advancement for deterministic, real Udon calls.
                    facade.enabled=false;Back(f.network).enabled=false;
                    Synthetic();Finish(null);
                }
            }
            catch(Exception e){Finish(e);}
        }
        static void Synthetic()
        {
            router.SetProgramVariable("fileProgramMap","");router.SetProgramVariable("fileTrackMap","");router.SetProgramVariable("autoTrackRouting",false);
            router.SetProgramVariable("enabledMask",128);Call(router,"ResolveSelection");
            temporary=new GameObject("MusicBoxProbe_Temporary");var sp=temporary.AddUdonSharpComponent<MidiSongData>();var song=Back(sp);
            song.SetProgramVariable("durationMs",5000);song.SetProgramVariable("isDecoded",true);song.SetProgramVariable("sourceHash","MusicBoxProbe-20261104");song.SetProgramVariable("contentHash","MusicBoxProbe-v1");
            int[] pitches={0,21,60,84,108,127};
            song.SetProgramVariable("startMs",new[]{0,0,0,0,0,0});song.SetProgramVariable("keyUpMs",Enumerable.Repeat(2000,6).ToArray());song.SetProgramVariable("releaseMs",Enumerable.Repeat(2200,6).ToArray());
            song.SetProgramVariable("pitch",pitches);song.SetProgramVariable("velocity",Enumerable.Repeat(100,6).ToArray());song.SetProgramVariable("channel",new int[6]);song.SetProgramVariable("trackIndex",new int[6]);song.SetProgramVariable("trackNames",new[]{"Music box"});
            song.SetProgramVariable("eventMs",new[]{0,0,0,0,0,0,2000,2000,2000,2000,2000,2000,2200,2200,2200,2200,2200,2200});
            song.SetProgramVariable("eventKind",new[]{0,0,0,0,0,0,1,1,1,1,1,1,2,2,2,2,2,2});song.SetProgramVariable("eventNoteId",Enumerable.Range(0,18).Select(i=>i%6).ToArray());
            song.SetProgramVariable("timbreEventIndex",new[]{0});song.SetProgramVariable("timbreChannel",new[]{0});song.SetProgramVariable("timbreKind",new[]{0});song.SetProgramVariable("timbreValue",new[]{10});
            Call(seq,"BindSong","data",song);Call(pool,"BindSong","song",song);
            clock.SetProgramVariable("_state",1);clock.SetProgramVariable("_position",.1);clock.SetProgramVariable("_frameDsp",AudioSettings.dspTime);
            Call(seq,"RebuildAt","positionMs",100,"nextGeneration",900);Call(pool,"RebuildAt","ms",100,"generation",900);Call(pool,"UpdateVoices","ms",100,"generation",900);
            var active=(bool[])pool.GetProgramVariable("active");var targets=(int[])pool.GetProgramVariable("targets");var a=(int[])pool.GetProgramVariable("_a");var b=(int[])pool.GetProgramVariable("_b");
            var slots=Enumerable.Range(0,64).Where(i=>active[i]).ToArray();
            Need(slots.Length==6&&slots.All(i=>targets[i]==7&&b[i]<0),"Six file notes including MIDI0/127 each claim one shared source");
            Need(slots.All(i=>f.voicePool.voiceSources[a[i]].clip!=null&&f.voicePool.voiceSources[a[i]].pitch>0&&f.voicePool.voiceSources[a[i]].pitch<=3&&f.voicePool.voiceSources[a[i]].volume>0),"Actual music-box AudioSources contain clips, preserved pitch and nonzero gain");
            Call(keys,"BindSong","song",song);
            var drumView=f.instrumentWaterfalls[8];
            Need(Enumerable.Range(0,88).All(i=>Vector3.Distance(drumView.transform.TransformPoint(drumView.keyHitPositions[i]),
                f.instrumentKeyboards[8].keyTransforms[i].TransformPoint(new Vector3(0,0,.5f)))<.0001f),"All 88 landing points meet the physical tine tips at rest");
            Call(keys,"Refresh","positionMs",100,"generation",900,"force",true);
            var drum=f.instrumentKeyboards[8].rotatingMechanism;
            var tine=f.instrumentKeyboards[8].keyTransforms[39];
            Quaternion rot100=drum.localRotation,tine100=tine.localRotation;
            Call(fall,"BindSong","data",song,"sequencer",seq);Call(fall,"RebuildAt","positionMs",-20,"generation",900);
            var fx=drumView.effects;
            var particlePools=fx.hitParticles.Concat(fx.symbolParticles).ToArray();
            Need(particlePools.Sum(p=>p.particleCount)==0,"Approaching notes do not emit particles before contact");
            Call(fall,"Refresh","positionMs",0,"generation",900);
            Need(fx.hitParticles.Sum(p=>p.particleCount)>0&&fx.symbolParticles.Sum(p=>p.particleCount)>0,"Actual note onset emits both contact dust and music symbols");
            var offset=drumView.transform.up*fx.particleLift+drumView.transform.forward*fx.particleDepthOffset;
            var emitters=pitches.Select(p=>drumView.transform.TransformPoint(new Vector3(Mathf.Lerp(-1.55f,1.55f,p/127f),
                drumView.drumNoteRadius*Mathf.Cos(drumView.drumContactAngle),drumView.drumNoteRadius*Mathf.Sin(drumView.drumContactAngle)))+offset).ToArray();
            Need(particlePools.Where(p=>p.particleCount>0).All(p=>emitters.Any(pos=>Vector3.Distance(pos,p.transform.position)<.0002f)),"Particle emitters follow the calibrated contact points");
            var emitted=particlePools.Sum(p=>p.particleCount);
            Call(fall,"Refresh","positionMs",0,"generation",900);
            Need(particlePools.Sum(p=>p.particleCount)==emitted&&particlePools.All(p=>p.isPaused),"Holding the same music time freezes contact particles");
            Call(fall,"RebuildAt","positionMs",100,"generation",900);
            Need(particlePools.Sum(p=>p.particleCount)==0,"Seeking clears previous contact particles without a new burst");
            var rolled=f.instrumentWaterfalls[8].barRenderers.Where(r=>r.gameObject.activeSelf).ToArray();
            Need(rolled.Length>0&&rolled.All(r=>r.sharedMaterial.GetFloat("_RollOverDrum")>.5f&&Mathf.Abs(r.transform.localPosition.y)<.001f),"Actual note slots follow the cylinder shader and are centered on its shaft");
            var shellRadii=drum.Find("BrassCylinder").GetComponent<MeshFilter>().sharedMesh.vertices
                .Where(v=>Mathf.Abs(Mathf.Abs(v.x)-1.622f)<.0001f).Select(v=>new Vector2(v.y,v.z).magnitude).ToArray();
            Need(shellRadii.Length>=128&&shellRadii.All(r=>Mathf.Abs(r-drumView.drumNoteRadius)<.0001f)
                &&rolled.All(r=>Mathf.Abs(r.sharedMaterial.GetFloat("_DrumNoteThicknessRatio")*drumView.drumNoteRadius-drumView.barDepth)<.0001f),
                "The crystal inner radius equals the 64-sided drum shell, with separately specified outward depth");
            Need(f.instrumentKeyboards[8].keyTransforms.Count(t=>t!=null)==88,"The music box retains all 88 comb tines");
            Need(drum.Find("SteelAxle").GetComponent<MeshFilter>().sharedMesh.bounds.extents.y<.03f,"The drum surface has no raised pins");
            Call(keys,"Refresh","positionMs",160,"generation",900,"force",false);
            Need(Quaternion.Angle(tine100,tine.localRotation)>.05f,"The comb still vibrates on note onset");
            Need(Quaternion.Angle(rot100,drum.localRotation)>1,"Actual pin belt rotates with song time");
            Quaternion pausedDrum=drum.localRotation,pausedTine=tine.localRotation;
            Call(keys,"Refresh","positionMs",160,"generation",901,"force",false);
            Need(Quaternion.Angle(pausedDrum,drum.localRotation)<.001f&&Quaternion.Angle(pausedTine,tine.localRotation)<.001f,"Pause freezes the drum and comb pose");
            Call(fall,"RebuildAt","positionMs",4000,"generation",902);
            Need(f.instrumentWaterfalls[8].barTransforms.All(t=>!t.gameObject.activeSelf),"Late seek clears expired markers from the drum");
            Call(keys,"Clear","generation",903);
            Need(Quaternion.Angle(drum.localRotation,Quaternion.identity)<.001f,"Stop clears pin-belt phase");
            Call(pool,"StopAll");clock.SetProgramVariable("_state",0);
            // Existing sparse snapshot encoding must keep target+1=8 separate from identity bits.
            int packed=(8<<20)|(60<<8)|100;
            Call(facade,"_RestoreLiveMidi","data",new[]{packed});
            Need(((int[])facade.GetProgramVariable("_liveTargetOverrides"))[60]==8,"Sparse live snapshot restores music box target8 without corrupting note identity");
            Call(facade,"_LiveNoteOff","channel",0,"number",60,"velocity",0);Call(pool,"StopAll");
            int eventPacked=(10<<18)|(64<<7)|100;Call(facade,"_ApplyLiveMidi","packed",eventPacked);
            Need(((int[])facade.GetProgramVariable("_liveTargetOverrides"))[64]==8,"Live event kind10 carries the new explicit target");
            Need(f.instrumentWaterfalls[8].barRenderers.Any(r=>r.gameObject.activeSelf&&r.sharedMaterial.GetFloat("_RollOverDrum")>.5f),"Live note feedback uses the same drum arc");
            Call(facade,"_LiveControlChange","channel",0,"number",120,"value",0);Call(pool,"StopAll");
            for(int pitch=0;pitch<128;pitch++)
            {
                Call(pool,"LiveNoteDown","id",pitch,"pitch",pitch,"velocity",100);
                active=(bool[])pool.GetProgramVariable("active");a=(int[])pool.GetProgramVariable("_a");
                int slot=Array.FindIndex(active,x=>x);
                Need(slot>=0&&f.voicePool.voiceSources[a[slot]].pitch>0&&f.voicePool.voiceSources[a[slot]].pitch<=3,"Backing-Udon full-range pitch "+pitch);
                Call(pool,"StopAll");
            }
            Need(f.voicePool.voiceSources.Length==64,"No additional physical audio pool was created");
            // Exercise dense real audio voices while the bounded visual capacity stays independent.
            for(int pitch=21;pitch<109;pitch++)Call(pool,"LiveNoteDown","id",pitch,"pitch",pitch,"velocity",100);
            active=(bool[])pool.GetProgramVariable("active");a=(int[])pool.GetProgramVariable("_a");
            var used=Enumerable.Range(0,64).Where(i=>active[i]).Select(i=>a[i]).ToArray();
            Need(used.Length<=64&&used.Distinct().Count()==used.Length,"Dense music-box polyphony stays within unique shared physical sources");
            Call(pool,"StopAll");
            var counts=(int[])router.GetProgramVariable("keyCounts");for(int p=21;p<=108;p++)counts[7*128+p]=1;router.SetProgramVariable("keyCounts",counts);
            Call(keys,"RefreshLive","ms",3000,"generation",905);
            var materials=f.instrumentRoots[8].GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
            Need(materials.Length<=8,"Actual initialized instrument uses at most eight materials including effects and opening text");
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;SessionState.SetBool(Key,false);
            try {if(pool!=null)Call(pool,"StopAll");}catch{}
            File.WriteAllText("Logs/MusicBox-Validation.json",JObject.FromObject(new {success=error==null,checks=Checks,error=error?.ToString(),scope="Editor/ClientSim native UI and real backing Udon; no real VRChat, physical MIDI/VR, multi-client or listening acceptance",completedUtc=DateTime.UtcNow.ToString("o")}).ToString());
            EditorApplication.delayCall+=()=>{if(temporary!=null)UnityEngine.Object.DestroyImmediate(temporary);EditorApplication.isPlaying=false;};
            if(error!=null)Debug.LogError("Music box validation failed: "+error);else Debug.Log("Music box validation passed: "+Checks.Count);
        }
    }
}
