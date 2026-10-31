using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace MusicMidiPlayer.Editor.Prefab
{
    // Explicit, bounded Editor/ClientSim probe of real backing Udon, never run on build/import.
    [InitializeOnLoad]
    public static class MidiInstrumentRoutingValidation
    {
        private const string ActiveKey="MMP.InstrumentRouting.Probe";
        private const string Report="Logs/instrument-routing-playmode.json";
        private static readonly List<string> Checks=new List<string>();
        private static int stage;
        private static double started;
        private static GameObject temporary;
        private static UdonBehaviour facade,router,pool,clock,sequencer,network;
        private static MidiPlayerFacade proxy;
        static MidiInstrumentRoutingValidation()
        {
            if(SessionState.GetBool(ActiveKey,false)) { started=EditorApplication.timeSinceStartup; EditorApplication.update+=Tick; }
        }
        public static string Begin()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start the probe from Edit mode.");
            Checks.Clear(); stage=0; SessionState.SetBool(ActiveKey,true);
            EditorApplication.isPlaying=true;
            return "Started bounded native-toggle/backing-Udon routing probe.";
        }
        private static void Need(bool value,string check)
        { if(!value) throw new InvalidOperationException(check); Checks.Add(check); }
        private static UdonBehaviour Back(UdonSharpBehaviour b) { return UdonSharpEditorUtility.GetBackingUdonBehaviour(b); }
        private static void Call(UdonBehaviour b,string method,params object[] args)
        {
            var symbols=((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram().SymbolTable.GetSymbols();
            for(int i=0;i<args.Length;i+=2)
            {
                string name=(string)args[i];
                var parameters=symbols.Where(s=>Regex.IsMatch(s,"^__\\d+_"+name+"__param$")).ToArray();
                if(parameters.Length==0) throw new MissingMemberException(method+"."+name);
                foreach(string symbol in parameters) b.SetProgramVariable(symbol,args[i+1]);
            }
            var entries=((UdonSharpProgramAsset)b.programSource).SerializedProgramAsset.RetrieveProgram().EntryPoints.GetExportedSymbols();
            b.SendCustomEvent(entries.Contains(method)?method:"__0_"+method);
        }
        private static void Tick()
        {
            if(!EditorApplication.isPlaying) return;
            try
            {
                if(EditorApplication.timeSinceStartup-started>45) throw new TimeoutException("Instrument routing probe timed out.");
                if(stage==0)
                {
                    proxy=UnityEngine.Object.FindObjectOfType<MidiPlayerFacade>();
                    if(proxy==null) return;
                    facade=Back(proxy); network=Back(proxy.network); router=Back(proxy.instrumentRouter);
                    pool=Back(proxy.voicePool.ensemble); clock=Back(proxy.clock); sequencer=Back(proxy.sequencer);
                    if(!(bool)facade.GetProgramVariable("isReady") || (bool)facade.GetProgramVariable("isSongLoading")
                        || !(bool)network.GetProgramVariable("networkReady")) return;
                    Need(proxy.voicePool.voiceSources.Length==64,"Existing 64 physical sources retained");
                    Call(facade,"CommandStop");
                    Back(proxy.playerUI).SendCustomEvent("_ToggleInstrumentPanel");
                    proxy.playerUI.instrumentToggles[3].isOn=true;
                    Need((int)router.GetProgramVariable("enabledMask")==5,"Native violin checkbox adds to the existing piano selection");
                    Need(proxy.playerUI.instrumentPanel.activeSelf,"Instrument popup stays open for multiple selections");
                    Need(proxy.instrumentRoots[1].activeSelf && proxy.instrumentRoots[3].activeSelf
                        && Vector3.Distance(proxy.instrumentRoots[1].transform.position,proxy.instrumentRoots[3].transform.position)>1,
                        "Enabled instrument models are visible together at distinct stage positions");
                    int[] resolved=(int[])router.GetProgramVariable("resolvedPrograms");
                    Need(resolved[0]==0 && resolved[40]==2 && resolved[41]==2 && resolved[12]==0,"Piano and violin resolve matching and fallback programs");
                    proxy.playerUI.instrumentToggles[1].isOn=false;
                    resolved=(int[])router.GetProgramVariable("resolvedPrograms");
                    Need(resolved.All(t=>t==2),"Violin-only selection resolves every melodic program to violin");
                    proxy.playerUI.instrumentToggles[3].isOn=false;
                    Need(((int[])router.GetProgramVariable("resolvedPrograms")).All(t=>t==-1),"Empty selection resolves no melodic instrument");
                    proxy.playerUI.instrumentToggles[1].isOn=true;
                    proxy.playerUI.instrumentToggles[3].isOn=true;
                    Need((int)network.GetProgramVariable("_confirmedMask")==5 || (int)network.GetProgramVariable("_pendingMask")==5,
                        "Complete local intent or confirmed snapshot retains the checkbox set without assuming immediate network acknowledgement");
                    Call(facade,"_LiveNoteOn","channel",0,"number",60,"velocity",90);
                    var before=(double[])pool.GetProgramVariable("starts");
                    var enabled=(bool[])pool.GetProgramVariable("active");
                    int heldSlot=Enumerable.Range(0,64).First(i=>enabled[i]); double heldStart=before[heldSlot];
                    proxy.playerUI.instrumentToggles[0].isOn=true;
                    Need((int)router.GetProgramVariable("enabledMask")==5 && (int)router.GetProgramVariable("pianoAppearance")==0
                        && ((bool[])pool.GetProgramVariable("active"))[heldSlot] && ((double[])pool.GetProgramVariable("starts"))[heldSlot]==heldStart,
                        "Changing piano appearance preserves the enabled set and does not retrigger held audio");
                    proxy.playerUI.instrumentToggles[1].isOn=true;
                    Call(facade,"_LiveNoteOff","channel",0,"number",60,"velocity",0);
                    proxy.playerUI.playButton.onClick.Invoke(); stage=1; return;
                }
                if(stage==1)
                {
                    Need((int)clock.GetProgramVariable("_state")==1,"Native Play button starts the shared transport");
                    proxy.playerUI.pauseButton.onClick.Invoke();
                    Need((int)clock.GetProgramVariable("_state")==2,"Native Pause button freezes the shared transport");
                    Call(facade,"CommandSeek","progress",.1f);
                    Need((int)clock.GetProgramVariable("_state")==2 && (double)clock.GetProgramVariable("_position")>0,"Seek preserves paused state with the selection intact");
                    Need((int)router.GetProgramVariable("enabledMask")==5,"Transport changes preserve the instrument set");
                    proxy.playerUI.stopButton.onClick.Invoke();
                    facade.enabled=false; network.enabled=false;
                    Synthetic(); Finish(null);
                }
            }
            catch(Exception e) { Finish(e); }
        }
        private static void Synthetic()
        {
            temporary=new GameObject("InstrumentRoutingProbe_Temporary");
            var songProxy=temporary.AddUdonSharpComponent<MidiSongData>(); var song=Back(songProxy);
            song.SetProgramVariable("durationMs",5000);
            song.SetProgramVariable("isDecoded",true);
            song.SetProgramVariable("startMs",new[]{0,0,0,1});
            song.SetProgramVariable("keyUpMs",new[]{2000,2000,2000,2001});
            song.SetProgramVariable("releaseMs",new[]{2500,2500,2500,2501});
            song.SetProgramVariable("pitch",new[]{60,64,72,67});
            song.SetProgramVariable("velocity",new[]{90,90,90,90});
            song.SetProgramVariable("channel",new[]{0,1,2,0});
            song.SetProgramVariable("trackIndex",new[]{0,1,2,0});
            song.SetProgramVariable("eventMs",new[]{0,0,0,1,2000,2000,2000,2001,2500,2500,2500,2501});
            song.SetProgramVariable("eventKind",new[]{0,0,0,0,1,1,1,1,2,2,2,2});
            song.SetProgramVariable("eventNoteId",new[]{0,1,2,3,0,1,2,3,0,1,2,3});
            song.SetProgramVariable("timbreEventIndex",new[]{0,1,2,3});
            song.SetProgramVariable("timbreChannel",new[]{0,1,2,0});
            song.SetProgramVariable("timbreKind",new[]{0,0,0,0});
            song.SetProgramVariable("timbreValue",new[]{0,40,12,40});
            Call(router,"BeginBindSong","song",song); Call(router,"BindStep","budget",1);
            Need((int)router.GetProgramVariable("_bindEvent")==0 && (int)router.GetProgramVariable("_bindTimbre")==1,
                "Incremental routing budgets control records as well as note events");
            for(int step=0;step<16;step++) Call(router,"BindStep","budget",1);
            Need((bool)router.GetProgramVariable("_bound"),"Incremental routing completes across bounded work steps");
            Call(sequencer,"BindSong","data",song);
            Call(pool,"BindSong","song",song);
            Need(((int[])router.GetProgramVariable("notePrograms")).SequenceEqual(new[]{0,40,12,40}),"Same-tick Program changes retain original event ordering and per-note identity");
            clock.SetProgramVariable("_state",1); clock.SetProgramVariable("_position",.1); clock.SetProgramVariable("_frameDsp",AudioSettings.dspTime);
            Call(sequencer,"RebuildAt","positionMs",100,"nextGeneration",777);
            Call(pool,"RebuildAt","ms",100,"generation",777);
            Call(pool,"UpdateVoices","ms",100,"generation",777);
            var active=(bool[])pool.GetProgramVariable("active"); var targets=(int[])pool.GetProgramVariable("targets");
            var ids=(int[])pool.GetProgramVariable("identities");
            Need(Enumerable.Range(0,64).Where(i=>active[i]).Select(i=>targets[i]).OrderBy(t=>t).SequenceEqual(new[]{0,0,2,2}),"Piano and violin sound concurrently from separate original programs");
            var a=(int[])pool.GetProgramVariable("_a"); var b=(int[])pool.GetProgramVariable("_b");
            var claimed=Enumerable.Range(0,64).Where(i=>active[i]).SelectMany(i=>b[i]<0?new[]{a[i]}:new[]{a[i],b[i]}).ToArray();
            Need(claimed.Length==6 && claimed.Distinct().Count()==6,"Two sample voices and two string pairs claim six distinct shared sources");
            var sources=proxy.voicePool.voiceSources;
            Need(Enumerable.Range(0,64).Where(i=>active[i]).All(i=>sources[a[i]].clip!=null && sources[a[i]].pitch>0 && sources[a[i]].volume>0),"Actual AudioSources contain clips, pitch and nonzero gain");
            int piano=Enumerable.Range(0,64).First(i=>active[i] && targets[i]==0);
            Need(sources[a[piano]].clip==proxy.GetInstrumentBank(1).clips[proxy.GetInstrumentBank(1).SelectRegion(60,90)],"Piano sample remains the original four-layer bank");
            clock.SetProgramVariable("_state",2); Call(pool,"UpdateVoices","ms",100,"generation",777);
            Need(!((bool[])pool.GetProgramVariable("active")).Any(x=>x),"Pause cancels scheduled and playing sources");
            clock.SetProgramVariable("_state",1); Call(pool,"RebuildAt","ms",100,"generation",778);
            Need(((bool[])pool.GetProgramVariable("active")).Count(x=>x)==4,"Resume rebuild restores simultaneous voices at their real age");
            // Exercise the shared allocator under mixed one-source/two-source pressure.
            var livePrograms=(int[])router.GetProgramVariable("livePrograms"); livePrograms[0]=0; livePrograms[1]=40;
            router.SetProgramVariable("livePrograms",livePrograms);
            for(int i=0;i<90;i++) Call(pool,"LiveNoteDown","id",(i%2)*128+30+i%88,"pitch",30+i%88,"velocity",100);
            active=(bool[])pool.GetProgramVariable("active"); a=(int[])pool.GetProgramVariable("_a"); b=(int[])pool.GetProgramVariable("_b");
            claimed=Enumerable.Range(0,64).Where(i=>active[i]).SelectMany(i=>b[i]<0?new[]{a[i]}:new[]{a[i],b[i]}).ToArray();
            Need(claimed.Length<=64 && claimed.Distinct().Count()==claimed.Length,"Mixed allocator pressure never exceeds or double-claims the 64-source budget");
            Need((int)pool.GetProgramVariable("stolenVoiceCount")>0,"Mixed allocation uses shared voice stealing under pressure");
            Call(pool,"StopAll"); router.SetProgramVariable("enabledMask",0); Call(router,"ResolveSelection");
            Call(pool,"RebuildAt","ms",100,"generation",779);
            Need(!((bool[])pool.GetProgramVariable("active")).Any(x=>x),"No unchecked instrument sounds when the entire selection is empty");
            router.SetProgramVariable("enabledMask",4); Call(router,"ResolveSelection");
            Call(pool,"RebuildAt","ms",100,"generation",780);
            active=(bool[])pool.GetProgramVariable("active"); targets=(int[])pool.GetProgramVariable("targets");
            Need(Enumerable.Range(0,64).Where(i=>active[i]).All(i=>targets[i]==2) && active.Count(x=>x)==4,"Single selected violin receives all four synthetic voices");
            Call(pool,"StopAll");
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick; SessionState.SetBool(ActiveKey,false);
            try
            {
                if(pool!=null && facade!=null)
                {
                    Call(pool,"StopAll");
                    var original=facade.GetProgramVariable("_song") as UdonBehaviour;
                    if(original!=null) { Call(pool,"BindSong","song",original); Call(sequencer,"BindSong","data",original); }
                    clock.SetProgramVariable("_state",0); clock.SetProgramVariable("_position",0.0);
                }
            }
            catch(Exception cleanup) { if(error==null) error=cleanup; }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report,JsonUtility.ToJson(new Result { success=error==null, checks=Checks.ToArray(), error=error==null?"":error.ToString(),
                scope="Editor/ClientSim native controls and real backing Udon; no real VRChat networking, hardware MIDI or listening test", completedUtc=DateTime.UtcNow.ToString("o") },true));
            // AddUdonSharpComponent queues SDK setup. Let that earlier delay callback finish
            // before destroying its temporary backing object or leaving Play mode.
            EditorApplication.delayCall+=()=>
            {
                if(temporary!=null) UnityEngine.Object.DestroyImmediate(temporary);
                EditorApplication.isPlaying=false;
            };
            if(error!=null) Debug.LogError("Instrument routing validation failed: "+error);
            else Debug.Log("Instrument routing validation passed: "+Checks.Count+" checks.");
        }
        [Serializable] private sealed class Result { public bool success; public string[] checks; public string error,scope,completedUtc; }
    }
}
