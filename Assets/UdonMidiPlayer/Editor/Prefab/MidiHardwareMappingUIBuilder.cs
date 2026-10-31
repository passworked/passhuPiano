using MusicMidiPlayer.UI;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace MusicMidiPlayer.Editor.Prefab
{
    public static class MidiHardwareMappingUIBuilder
    {
        public static void Configure(MidiPlayerUI ui)
        {
            if (ui.settingsPanel == null || ui.facade == null || ui.facade.instrumentRouter == null) return;
            Transform settings = ui.settingsPanel.transform, general = settings.Find("Content");
            Image panelStyle = general.GetComponent<Image>();
            Image rowStyle = general.Find("CurrentLanding").GetComponent<Image>();
            Button buttonStyle = general.Find("CurrentLanding").GetComponent<Button>();
            TMP_Text labelStyle = general.Find("CurrentLanding/Label").GetComponent<TMP_Text>();
            Image markStyle = ui.currentLandingSelectionMark.GetComponent<Image>();
            RectTransform title = (RectTransform)general.Find("Title"); title.sizeDelta = new Vector2(200, title.sizeDelta.y);
            ui.fileMappingEntry = MakeButton(ui.transform,"FileMappingEntry",new Vector2(-20,137),new Vector2(140,32),ui,"_OpenFileMapping",rowStyle,buttonStyle);
            ui.fileMappingEntryLabel=Text(ui.fileMappingEntry.transform,"Label","Timbre mapping",Vector2.zero,new Vector2(128,28),labelStyle,17);
            ((RectTransform)ui.transform.Find("NowPlayingCaption")).sizeDelta=new Vector2(340,24);
            ui.midiMappingEntry = MakeButton(general, "MidiMappingEntry", new Vector2(100,214), new Vector2(112,32), ui, "_OpenMidiMapping", rowStyle, buttonStyle);
            ui.midiPerformanceEntryLabel=Text(ui.midiMappingEntry.transform,"Label","MIDI PLAY",Vector2.zero,new Vector2(100,28),labelStyle,18);
            var page = Rect(settings,"MidiMapping",Vector2.zero,new Vector2(980,480));
            Paint(page,panelStyle); Block(page);
            ui.midiMappingPanel = page.gameObject;
            ui.midiMappingTitle = Text(page,"Title","MIDI CHANNELS",new Vector2(-100,210),new Vector2(700,32),labelStyle,22);
            var back = MakeButton(page,"Back",new Vector2(400,210),new Vector2(120,34),ui,"_CloseMidiAdvanced",rowStyle,buttonStyle);
            ui.midiMappingBack = Text(back.transform,"Label","Back",Vector2.zero,new Vector2(104,30),labelStyle,18);
            ui.midiChannelButtons = new Button[16]; ui.midiChannelNames = new TMP_Text[16]; ui.midiChannelValues = new TMP_Text[16];
            for(int i=0;i<16;i++)
            {
                var row = MakeButton(page,"Channel"+(i+1),new Vector2(i<8?-242:242,150-i%8*43),new Vector2(456,38),ui,"_MidiChannel"+(i+1),rowStyle,buttonStyle);
                ui.midiChannelButtons[i]=row;
                ui.midiChannelNames[i]=Text(row.transform,"Channel","CH "+(i+1),new Vector2(-173,0),new Vector2(90,30),labelStyle,18);
                ui.midiChannelValues[i]=Text(row.transform,"Value","",new Vector2(53,0),new Vector2(320,30),labelStyle,18);
            }
            var overlay = Rect(page,"TargetPicker",Vector2.zero,new Vector2(980,480));
            var shade=Paint(overlay,panelStyle);shade.sprite=null;shade.color=new Color(0,0,0,.65f);
            var dismiss=Get<Button>(overlay);dismiss.transition=Selectable.Transition.None;dismiss.targetGraphic=shade;
            Listen(dismiss,ui,"_CloseMidiChoices");
            var content=Rect(overlay,"Content",Vector2.zero,new Vector2(456,392));Paint(content,panelStyle);Block(content);
            ui.midiChoicePanel=overlay.gameObject;ui.midiChoiceContent=content;
            ui.midiChoiceTitle=Text(content,"Title","",new Vector2(0,170),new Vector2(416,30),labelStyle,22);
            ui.midiChoiceTitleRect=(RectTransform)ui.midiChoiceTitle.transform;
            ui.midiTargetButtons=new Button[8];ui.midiTargetLabels=new TMP_Text[8];ui.midiTargetMarks=new GameObject[8];
            for(int i=0;i<8;i++)
            {
                var option=MakeButton(content,"Target"+i,new Vector2(0,124-i*40),new Vector2(424,36),ui,"_MidiTarget"+i,rowStyle,buttonStyle);
                ui.midiTargetButtons[i]=option;
                ui.midiTargetLabels[i]=Text(option.transform,"Label","",Vector2.zero,new Vector2(400,30),labelStyle,18);
                var selected=Rect(option.transform,"Selected",new Vector2(0,-15),new Vector2(400,3));
                Paint(selected,markStyle).raycastTarget=false;ui.midiTargetMarks[i]=selected.gameObject;
            }
            var performance=Rect(settings,"MidiPerformance",Vector2.zero,new Vector2(980,480));Paint(performance,panelStyle);Block(performance);
            ui.midiPerformancePanel=performance.gameObject;
            ui.midiPerformanceTitle=Text(performance,"Title","MIDI PLAY",new Vector2(-100,210),new Vector2(700,32),labelStyle,22);
            var returnButton=MakeButton(performance,"Back",new Vector2(400,210),new Vector2(120,34),ui,"_CloseMidiMapping",rowStyle,buttonStyle);
            ui.midiPerformanceBack=Text(returnButton.transform,"Label","Back",Vector2.zero,new Vector2(104,30),labelStyle,18);
            ui.midiPerformancePrompt=Text(performance,"Prompt","",new Vector2(0,125),new Vector2(880,60),labelStyle,28);
            ui.midiPerformancePrompt.alignment=TextAlignmentOptions.Center;
            ui.midiPerformancePrompt.enableWordWrapping=true;
            ui.midiPerformanceHint=Text(performance,"Hint","",new Vector2(0,77),new Vector2(880,32),labelStyle,18);
            ui.midiPerformanceHint.alignment=TextAlignmentOptions.Center;
            ui.midiPerformanceTargets=new Button[7];ui.midiPerformanceTargetLabels=new TMP_Text[7];ui.midiPerformanceTargetMarks=new GameObject[7];
            for(int i=0;i<7;i++)
            {
                var option=MakeButton(performance,"Instrument"+i,new Vector2(i%2==0?-225:225,25-i/2*55),new Vector2(424,42),ui,"_MidiTarget"+(i+1),rowStyle,buttonStyle);
                ui.midiPerformanceTargets[i]=option;
                ui.midiPerformanceTargetLabels[i]=Text(option.transform,"Label","",Vector2.zero,new Vector2(400,34),labelStyle,20);
                var selected=Rect(option.transform,"Selected",new Vector2(0,-18),new Vector2(400,3));Paint(selected,markStyle).raycastTarget=false;
                ui.midiPerformanceTargetMarks[i]=selected.gameObject;
            }
            var rescan=MakeButton(performance,"Rescan",new Vector2(-225,-205),new Vector2(424,36),ui,"_RescanMidi",rowStyle,buttonStyle);
            ui.midiPerformanceRescanButton=rescan;ui.midiPerformanceRescan=Text(rescan.transform,"Label","",Vector2.zero,new Vector2(400,30),labelStyle,18);
            var advanced=MakeButton(performance,"Advanced",new Vector2(225,-205),new Vector2(424,36),ui,"_OpenMidiAdvanced",rowStyle,buttonStyle);
            ui.midiPerformanceAdvanced=Text(advanced.transform,"Label","",Vector2.zero,new Vector2(400,30),labelStyle,18);
            overlay.gameObject.SetActive(false);page.gameObject.SetActive(false);performance.gameObject.SetActive(false);
            ConfigureFileMapping(ui,ui.transform,panelStyle,rowStyle,buttonStyle,labelStyle,markStyle);
            MidiPlayerUIBuilder.ConfigureHierarchy((RectTransform)ui.transform);
            UdonSharpEditorUtility.CopyProxyToUdon(ui,ProxySerializationPolicy.All);EditorUtility.SetDirty(ui);
        }
        private static void ConfigureFileMapping(MidiPlayerUI ui,Transform settings,Image panelStyle,Image rowStyle,Button buttonStyle,TMP_Text labelStyle,Image markStyle)
        {
            var page=Rect(settings,"FileMappingPanel",Vector2.zero,new Vector2(980,480));Paint(page,panelStyle);Block(page);
            ui.fileMappingPanel=page.gameObject;
            ui.fileMappingTitle=Text(page,"Title","MIDI FILE TIMBRES",new Vector2(-80,210),new Vector2(720,32),labelStyle,22);
            var back=MakeButton(page,"Back",new Vector2(400,210),new Vector2(120,34),ui,"_CloseFileMapping",rowStyle,buttonStyle);
            ui.fileMappingBack=Text(back.transform,"Label","Back",Vector2.zero,new Vector2(104,30),labelStyle,18);
            ui.fileMappingHint=Text(page,"Hint","",new Vector2(0,174),new Vector2(920,30),labelStyle,16);
            ui.fileProgramButtons=new Button[16];ui.fileProgramLabels=new TMP_Text[16];ui.fileProgramValues=new TMP_Text[16];
            for(int i=0;i<16;i++)
            {
                var row=MakeButton(page,"Program"+i,new Vector2(i<8?-242:242,132-i%8*40),new Vector2(456,36),ui,"_FileProgram"+i,rowStyle,buttonStyle);
                ui.fileProgramButtons[i]=row;
                ui.fileProgramLabels[i]=Text(row.transform,"Source","",new Vector2(-93,0),new Vector2(250,30),labelStyle,17);
                ui.fileProgramValues[i]=Text(row.transform,"Value","",new Vector2(130,0),new Vector2(178,30),labelStyle,17);
            }
            var reset=MakeButton(page,"Reset",new Vector2(-290,-205),new Vector2(350,34),ui,"_ResetFileMapping",rowStyle,buttonStyle);
            ui.fileResetButton=reset;ui.fileMappingReset=Text(reset.transform,"Label","",Vector2.zero,new Vector2(330,30),labelStyle,18);
            var prev=MakeButton(page,"Previous",new Vector2(105,-205),new Vector2(120,34),ui,"_FilePrevious",rowStyle,buttonStyle);
            ui.filePreviousButton=prev;ui.fileMappingPrevious=Text(prev.transform,"Label","",Vector2.zero,new Vector2(104,30),labelStyle,18);
            ui.fileMappingPage=Text(page,"Page","1 / 1",new Vector2(235,-205),new Vector2(100,30),labelStyle,18);
            ui.fileMappingPage.alignment=TextAlignmentOptions.Center;
            var next=MakeButton(page,"Next",new Vector2(380,-205),new Vector2(120,34),ui,"_FileNext",rowStyle,buttonStyle);
            ui.fileNextButton=next;ui.fileMappingNext=Text(next.transform,"Label","",Vector2.zero,new Vector2(104,30),labelStyle,18);
            var overlay=Rect(page,"TargetPicker",Vector2.zero,new Vector2(980,480));
            var shade=Paint(overlay,panelStyle);shade.sprite=null;shade.color=new Color(0,0,0,.65f);
            var dismiss=Get<Button>(overlay);dismiss.transition=Selectable.Transition.None;dismiss.targetGraphic=shade;Listen(dismiss,ui,"_CloseFileChoices");
            var content=Rect(overlay,"Content",Vector2.zero,new Vector2(456,352));Paint(content,panelStyle);Block(content);
            ui.fileChoicePanel=overlay.gameObject;ui.fileChoiceContent=content;
            ui.fileChoiceTitle=Text(content,"Title","",new Vector2(0,150),new Vector2(416,30),labelStyle,20);
            ui.fileChoiceTitleRect=(RectTransform)ui.fileChoiceTitle.transform;
            ui.fileTargetButtons=new Button[7];ui.fileTargetLabels=new TMP_Text[7];ui.fileTargetMarks=new GameObject[7];
            for(int i=0;i<7;i++)
            {
                var option=MakeButton(content,"Target"+i,new Vector2(0,104-i*40),new Vector2(424,36),ui,"_MidiTarget"+i,rowStyle,buttonStyle);
                ui.fileTargetButtons[i]=option;ui.fileTargetLabels[i]=Text(option.transform,"Label","",Vector2.zero,new Vector2(400,30),labelStyle,18);
                var selected=Rect(option.transform,"Selected",new Vector2(0,-15),new Vector2(400,3));
                Paint(selected,markStyle).raycastTarget=false;ui.fileTargetMarks[i]=selected.gameObject;
            }
            overlay.gameObject.SetActive(false);page.gameObject.SetActive(false);
        }
        private static T Get<T>(Component c) where T:Component {var v=c.GetComponent<T>();return v!=null?v:c.gameObject.AddComponent<T>();}
        private static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var t=parent.Find(name);if(t==null){t=new GameObject(name,typeof(RectTransform)).transform;t.SetParent(parent,false);}
            t.gameObject.layer=parent.gameObject.layer;
            var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;return r;
        }
        private static Image Paint(Component node,Image style)
        {var v=Get<Image>(node);v.sprite=style.sprite;v.type=style.type;v.color=style.color;v.pixelsPerUnitMultiplier=style.pixelsPerUnitMultiplier;v.raycastTarget=true;return v;}
        private static void Block(Component node){var b=Get<Button>(node);b.transition=Selectable.Transition.None;b.targetGraphic=node.GetComponent<Image>();b.onClick=new Button.ButtonClickedEvent();}
        private static void Listen(Button b,MidiPlayerUI ui,string method)
        {b.onClick=new Button.ButtonClickedEvent();UnityEventTools.AddStringPersistentListener(b.onClick,UdonSharpEditorUtility.GetBackingUdonBehaviour(ui).SendCustomEvent,method);}
        private static Button MakeButton(Transform parent,string name,Vector2 position,Vector2 size,MidiPlayerUI ui,string method,Image image,Button style)
        {
            var r=Rect(parent,name,position,size);var b=Get<Button>(r);b.targetGraphic=Paint(r,image);b.colors=style.colors;b.transition=style.transition;
            var nav=b.navigation;nav.mode=Navigation.Mode.None;b.navigation=nav;Listen(b,ui,method);return b;
        }
        private static TMP_Text Text(Transform parent,string name,string text,Vector2 position,Vector2 size,TMP_Text style,int fontSize)
        {
            var v=Get<TextMeshProUGUI>(Rect(parent,name,position,size));v.font=style.font;v.fontSharedMaterial=style.fontSharedMaterial;v.color=style.color;
            v.text=text;v.fontSize=fontSize;v.enableAutoSizing=true;v.fontSizeMin=12;v.fontSizeMax=fontSize;v.alignment=TextAlignmentOptions.MidlineLeft;
            v.enableWordWrapping=false;v.overflowMode=TextOverflowModes.Ellipsis;v.raycastTarget=false;return v;
        }
    }
}
