using System;
using System.Collections.Generic;
using MusicMidiPlayer.Editor.Assets;
using MusicMidiPlayer.Editor.Baking;
using MusicMidiPlayer.Logic;
using MusicMidiPlayer.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRC.SDK3.Components;

namespace MusicMidiPlayer.Editor.Prefab
{
    // VizVid-inspired palette and spacing. All sprites are authored by this project.
    public static class MidiPlayerUIBuilder
    {
        private static readonly Color Panel = Hex(0x37424F);
        private static readonly Color Inset = Hex(0x080D0C);
        private static readonly Color Accent = Hex(0x87E2E5);
        private static readonly Color Muted = Hex(0xBAC8D1);
        private static readonly Color Hover = Hex(0x46586E);
        private static readonly Color Tile = Hex(0x25313E);

        public static void Build(Transform root, MidiPlayerFacade facade, MidiPlayerConfigurationAsset config, TMP_FontAsset font, bool preservePlacement = false)
        {
            Transform presentation = Child(root, "Presentation");
            bool existing = presentation.Find("UI") != null;
            RectTransform canvasRect = (RectTransform)Child(presentation, "UI", true);
            canvasRect.sizeDelta = new Vector2(1000, 480);
            if (!existing || !preservePlacement)
            {
                canvasRect.localPosition = new Vector3(0, 0.76f, -0.12f);
                canvasRect.localScale = Vector3.one * 0.003f;
                if (config.pianoModel != null)
                {
                    Bounds piano = MidiPianoModelBinder.ModelBounds(presentation);
                    canvasRect.localPosition = new Vector3(piano.min.x - 1.05f, Mathf.Clamp(piano.center.y, 0.7f, 1.05f), presentation.Find("ViewRoot").localPosition.z - 0.12f);
                    canvasRect.localScale = Vector3.one * 0.0018f;
                }
            }
            Get<Canvas>(canvasRect).renderMode = RenderMode.WorldSpace;
            Get<CanvasScaler>(canvasRect);
            Get<GraphicRaycaster>(canvasRect);
            Get<VRCUiShape>(canvasRect);
            var uiCollider = Get<BoxCollider>(canvasRect);
            uiCollider.isTrigger = true;
            uiCollider.center = new Vector3(canvasRect.rect.center.x, canvasRect.rect.center.y, 0);
            uiCollider.size = new Vector3(canvasRect.rect.width, canvasRect.rect.height, 1);
            Paint(Get<Image>(canvasRect), Panel);
            var shadow = Get<Shadow>(canvasRect);
            shadow.effectColor = new Color(0, 0, 0, 0.24f);
            shadow.effectDistance = new Vector2(0, -7);
            MidiPlayerUI ui = canvasRect.GetComponent<MidiPlayerUI>();
            if (ui == null) ui = canvasRect.gameObject.AddUdonSharpComponent<MidiPlayerUI>();
            facade.playerUI = ui;
            ui.facade = facade;
            BuildOpening(facade, font);

            // Backgrounds go behind reused controls to preserve their object identities.
            Image main = Box(canvasRect, "NowPlayingPanel", new Vector2(-476, 40), new Vector2(558, 256), Inset);
            main.transform.SetAsFirstSibling();
            Image library = Box(canvasRect, "LibraryPanel", new Vector2(104, -26), new Vector2(372, 388), Tile);
            library.transform.SetAsFirstSibling();
            Icon(canvasRect, "BrandIcon", "Note", new Vector2(-472, 210), 24, Accent);
            TMP_Text brand = Label(canvasRect, "Brand", "MIDI PLAYER", new Vector2(-434, 210), new Vector2(440, 32), 24, Color.white, font);
            brand.fontStyle = FontStyles.Bold;
            brand.characterSpacing = 2;
            Label(canvasRect, "LibraryName", config.library.title, new Vector2(68, 210), new Vector2(404, 28), 17, Muted, font).alignment = TextAlignmentOptions.MidlineRight;
            Box(canvasRect, "HeaderDivider", new Vector2(-476, 182), new Vector2(952, 1), new Color(1, 1, 1, 0.09f), false);
            Label(canvasRect, "NowPlayingCaption", "NOW PLAYING", new Vector2(-450, 137), new Vector2(506, 24), 16, Accent, font).characterSpacing = 2;
            string initialTitle = config.library.songs[0].title;
            int initialDuration = config.library.songs[0].durationMs;
            foreach (var song in config.library.songs)
                if (song.songId == facade.library.defaultSongId) { initialTitle = song.title; initialDuration = song.durationMs; }
            ui.titleLabel = Label(canvasRect, "SongTitle", initialTitle, new Vector2(-450, 92), new Vector2(506, 54), 32, Color.white, font);
            ui.titleLabel.fontStyle = FontStyles.Bold;
            Box(canvasRect, "StatusDot", new Vector2(-450, 40), new Vector2(7, 7), Accent);
            ui.stateLabel = Label(canvasRect, "Status", "PREPARING...", new Vector2(-431, 40), new Vector2(487, 28), 16, Muted, font);
            ui.timeLabel = Label(canvasRect, "Time", "00:00 / " + Duration(initialDuration, facade.library.bank.maxReleaseMs), new Vector2(-450, -12), new Vector2(506, 28), 20, Color.white, font);
            ui.timeLabel.alignment = TextAlignmentOptions.MidlineRight;
            ui.progressSlider = Track(canvasRect, "Progress", new Vector2(-450, -50), new Vector2(506, 24), true);
            ConfigureProgressSlider(ui);
            ui.progressSlider.SetValueWithoutNotify(0);

            ui.playButton = Control(canvasRect, "Play", "PLAY", new Vector2(-450, -140), true, font);
            ui.pauseButton = Control(canvasRect, "Pause", "PAUSE", new Vector2(-278, -140), false, font);
            ui.stopButton = Control(canvasRect, "Stop", "STOP", new Vector2(-106, -140), false, font);
            Listen(ui.playButton.onClick, ui, "_OnPlayPressed");
            Listen(ui.pauseButton.onClick, ui, "_OnPausePressed");
            Listen(ui.stopButton.onClick, ui, "_OnStopPressed");
            Icon(canvasRect, "VolumeIcon", "Sound", new Vector2(-450, -204), 24, Accent);
            Label(canvasRect, "VolumeLabel", "VOLUME", new Vector2(-413, -204), new Vector2(78, 28), 14, Muted, font);
            ui.volumeSlider = Track(canvasRect, "Volume", new Vector2(-328, -204), new Vector2(252, 38), true);
            ui.volumeSlider.maxValue = 2f;
            ui.volumeSlider.SetValueWithoutNotify(facade.voicePool.localVolume);
            Listen(ui.volumeSlider.onValueChanged, ui, "_OnVolumeChanged");
            RectTransform muteRect = Rect(Child(canvasRect, "Mute", true), new Vector2(-56, -204), new Vector2(112, 36));
            Toggle mute = Get<Toggle>(muteRect);
            Image muteBack = Get<Image>(muteRect);
            Paint(muteBack, Inset);
            Image mark = Box(muteRect, "Check", new Vector2(-46, 0), new Vector2(5, 16), Accent);
            mute.targetGraphic = muteBack;
            mute.graphic = mark;
            mute.SetIsOnWithoutNotify(false);
            mute.colors = Colors(Color.white, new Color(1.3f, 1.3f, 1.3f), new Color(0.7f, 0.8f, 0.8f), new Color(0.5f, 0.5f, 0.5f));
            Label(muteRect, "Label", "MUTE", new Vector2(-31, 0), new Vector2(78, 30), 15, Accent, font).alignment = TextAlignmentOptions.Center;
            ui.muteToggle = mute;
            Listen(mute.onValueChanged, ui, "_OnMuteChanged");

            BuildLibrary(canvasRect, ui, facade, config, font);
            ConfigureLibraryInput(ui);
            MidiPlayerLocalizationBuilder.Configure(ui);
            ConfigureBackplate(canvasRect);
        }

        public static void ConfigureBackplate(RectTransform canvasRect)
        {
            const string path = "Assets/UdonMidiPlayer/Materials/UIBackplate.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("MusicMidiPlayer/UI Backplate");
                if (shader == null) throw new InvalidOperationException("Missing UI backplate shader.");
                material = new Material(shader) { name = "UIBackplate" };
                AssetDatabase.CreateAsset(material, path);
            }
            Image back = Box(canvasRect, "Backplate", Vector2.zero, Vector2.zero, Panel);
            var rect = (RectTransform)back.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            back.material = material;
            // Draw last to cover mirrored controls, including fallback-font submeshes.
            // Front-face culling leaves the existing front UI completely visible.
            ConfigureHierarchy(canvasRect);
        }

        // Keep late-added base controls below every popup without changing Find paths.
        public static void ConfigureHierarchy(RectTransform canvasRect)
        {
            Transform main = canvasRect.Find("NowPlayingPanel");
            if (main != null) main.SetAsFirstSibling();
            Transform library = canvasRect.Find("LibraryPanel");
            if (library != null) library.SetAsFirstSibling();
            foreach (string name in new[] { "InstrumentPanel", "LanguagePanel", "SettingsPanel", "FileMappingPanel", "Backplate" })
            {
                Transform layer = canvasRect.Find(name);
                if (layer != null) layer.SetAsLastSibling();
            }
        }

        public static void ConfigureSettings(MidiPlayerUI ui)
        {
            Transform parent = ui.transform;
            TMP_FontAsset font = ui.titleLabel.font;
            Transform language = parent.Find("Language");
            if (language != null) ((RectTransform)language).anchoredPosition = new Vector2(364, 210);
            Transform libraryName = parent.Find("LibraryName");
            if (libraryName != null) ((RectTransform)libraryName).sizeDelta = new Vector2(282, 28);
            RectTransform entry = Rect(Child(parent, "Settings", true), new Vector2(424, 210), new Vector2(52, 44));
            Image entryImage = Get<Image>(entry);
            Paint(entryImage, Inset);
            entryImage.raycastTarget = true;
            Button button = Get<Button>(entry);
            button.targetGraphic = entryImage;
            button.colors = Colors(Color.white, new Color(2, 3, 3), new Color(1.5f, 2, 2), Muted);
            Listen(button.onClick, ui, "_ToggleSettingsPanel");
            Icon(entry, "Icon", "Settings", new Vector2(-18, 0), 36, Accent).raycastTarget = false;

            RectTransform overlay = Rect(Child(parent, "SettingsPanel", true), new Vector2(-500, 0), new Vector2(1000, 480));
            Image hit = Get<Image>(overlay);
            hit.sprite = null;
            hit.color = Color.clear;
            hit.raycastTarget = true;
            Button dismiss = Get<Button>(overlay);
            dismiss.targetGraphic = hit;
            dismiss.transition = Selectable.Transition.None;
            Listen(dismiss.onClick, ui, "_CloseSettingsPanel");
            Image panelImage = Box(overlay, "Content", new Vector2(-24, 10), new Vector2(500, 480), Tile);
            panelImage.raycastTarget = true;
            Transform panel = panelImage.transform;
            Button panelHit = Get<Button>(panel);
            panelHit.targetGraphic = panelImage;
            panelHit.transition = Selectable.Transition.None;
            Label(panel, "Title", "SETTINGS", new Vector2(-226, 186), new Vector2(400, 30), 22, Accent, font);
            RectTransform close = Rect(Child(panel, "Close", true), new Vector2(202, 186), new Vector2(36, 36));
            Image closeImage = Get<Image>(close);
            Paint(closeImage, Inset);
            closeImage.raycastTarget = true;
            Button closeButton = Get<Button>(close);
            closeButton.targetGraphic = closeImage;
            Listen(closeButton.onClick, ui, "_CloseSettingsPanel");
            Label(close, "Label", "×", new Vector2(-18, 0), new Vector2(36, 36), 26, Accent, font).alignment = TextAlignmentOptions.Center;
            Label(panel, "NoteLandingLabel", "NOTE LANDING POSITION", new Vector2(-226, -8), new Vector2(452, 30), 18, Color.white, font);
            ui.currentLandingSelectionMark = LandingOption(panel, ui, "CurrentLanding", "Current position", -226, font, "_UseCurrentNoteLanding");
            ui.keyRootLandingSelectionMark = LandingOption(panel, ui, "KeyRootLanding", "Keyboard rear", 10, font, "_UseKeyRootNoteLanding");
            bool atRoot = ui.facade.waterfall != null && ui.facade.waterfall.landAtKeyRoot;
            ui.currentLandingSelectionMark.SetActive(!atRoot);
            ui.keyRootLandingSelectionMark.SetActive(atRoot);
            Label(panel, "StickScrollSensitivityLabel", "STICK SCROLL SENSITIVITY", new Vector2(-226, -110), new Vector2(350, 30), 18, Color.white, font);
            ui.stickScrollSensitivityValueLabel = Label(panel, "StickScrollSensitivityValue", "2×", new Vector2(134, -110), new Vector2(92, 30), 22, Accent, font);
            ui.stickScrollSensitivityValueLabel.alignment = TextAlignmentOptions.MidlineRight;
            ui.stickScrollSensitivitySlider = Track(panel, "StickScrollSensitivity", new Vector2(-218, -154), new Vector2(436, 38), true);
            ui.stickScrollSensitivitySlider.minValue = 0;
            ui.stickScrollSensitivitySlider.maxValue = 5;
            ui.stickScrollSensitivitySlider.wholeNumbers = true;
            ui.stickScrollSensitivity = Mathf.Pow(2f, ui.GetStickSensitivityStep() - 1);
            ui.stickScrollSensitivitySlider.SetValueWithoutNotify(ui.GetStickSensitivityStep());
            AddStepDots(ui.stickScrollSensitivitySlider, 6);
            ui.stickScrollSensitivityValueLabel.text = ui.stickScrollSensitivity.ToString("0.##") + "×";
            Listen(ui.stickScrollSensitivitySlider.onValueChanged, ui, "_OnStickScrollSensitivityChanged");
            Label(panel, "StickScrollMinimum", "0.5×", new Vector2(-226, -193), new Vector2(100, 28), 15, Muted, font);
            Label(panel, "StickScrollMaximum", "16×", new Vector2(126, -193), new Vector2(100, 28), 15, Muted, font).alignment = TextAlignmentOptions.MidlineRight;
            string[] rows = { "Title", "Close",
                "NoteLandingLabel", "CurrentLanding", "KeyRootLanding", "StickScrollSensitivityLabel",
                "StickScrollSensitivityValue", "StickScrollSensitivity", "StickScrollMinimum", "StickScrollMaximum" };
            float[] heights = { 194, 194, 135, 87, 87, 27, 27, -17, -52, -52 };
            for (int i = 0; i < rows.Length; i++)
            {
                RectTransform row = (RectTransform)panel.Find(rows[i]);
                row.anchoredPosition = new Vector2(row.anchoredPosition.x, heights[i]);
            }
            Label(panel, "AudioSourceLabel", "SOUND SOURCE", new Vector2(-226, -110), new Vector2(452, 30), 18, Color.white, font);
            ui.worldAudioSelectionMark = LandingOption(panel, ui, "WorldAudio", "Fixed in world", -226, font, "_UseWorldAudio");
            ui.twoDAudioSelectionMark = LandingOption(panel, ui, "TwoDAudio", "2D sound", 10, font, "_Use2DAudio");
            ((RectTransform)panel.Find("WorldAudio")).anchoredPosition = new Vector2(-226, -160);
            ((RectTransform)panel.Find("TwoDAudio")).anchoredPosition = new Vector2(10, -160);
            bool worldSpace = ui.facade != null && ui.facade.voicePool != null && ui.facade.voicePool.worldSpaceAudio;
            ui.worldAudioSelectionMark.SetActive(worldSpace);
            ui.twoDAudioSelectionMark.SetActive(!worldSpace);
            ui.measureSnapSelectionMark = LandingOption(panel, ui, "MeasureSnap", "Snap seek to measures", -226, font, "_ToggleMeasureSnap");
            ((RectTransform)panel.Find("MeasureSnap")).anchoredPosition = new Vector2(-226, -202);
            ((RectTransform)panel.Find("MeasureSnap")).sizeDelta = new Vector2(452, 34);
            ((RectTransform)panel.Find("MeasureSnap/Label")).sizeDelta = new Vector2(436, 30);
            ((RectTransform)panel.Find("MeasureSnap/Label")).anchoredPosition = new Vector2(-218, 0);
            ((RectTransform)ui.measureSnapSelectionMark.transform).sizeDelta = new Vector2(436, 3);
            ((RectTransform)ui.measureSnapSelectionMark.transform).anchoredPosition = new Vector2(-218, -13);
            ui.measureSnapSelectionMark.SetActive(ui.snapProgressToMeasures);
            ui.peakProtectionSelectionMark = LandingOption(panel, ui, "PeakProtection", "Peak protection", -226, font, "_TogglePeakProtection");
            ((RectTransform)panel.Find("PeakProtection")).anchoredPosition = new Vector2(-226, -240);
            ((RectTransform)panel.Find("PeakProtection")).sizeDelta = new Vector2(216, 34);
            ((RectTransform)ui.peakProtectionSelectionMark.transform).anchoredPosition = new Vector2(-92, -13);
            ui.peakProtectionSelectionMark.SetActive(ui.facade != null && ui.facade.voicePool != null && ui.facade.voicePool.peakProtectionEnabled);
            foreach (RectTransform row in panel)
                row.anchoredPosition += new Vector2(0, 20);
            ConfigurePercussionControl(ui);
            ui.settingsPanel = overlay.gameObject;
            MidiPerformanceBuilder.ConfigureSettings(ui);
            MidiHardwareMappingUIBuilder.Configure(ui);
            overlay.gameObject.SetActive(false);
            ConfigureBackplate((RectTransform)parent);
        }

        public static void ConfigurePercussionControl(MidiPlayerUI ui)
        {
            if(ui.facade==null || ui.facade.drumView==null)return;
            Transform panel=ui.transform.Find("SettingsPanel/Content");
            if(panel==null)return;
            ui.drumVisibilitySelectionMark=LandingOption(panel,ui,"DrumVisibility","Show drum kit",10,ui.titleLabel.font,"_ToggleDrumVisibility");
            var row=(RectTransform)panel.Find("DrumVisibility");row.anchoredPosition=new Vector2(10,-220);row.sizeDelta=new Vector2(216,34);
            ((RectTransform)ui.drumVisibilitySelectionMark.transform).anchoredPosition=new Vector2(-92,-13);
            ui.drumVisibilitySelectionMark.SetActive(ui.facade.drumView.presentationEnabled);
            ui.drumVisibilityLabel=panel.Find("DrumVisibility/Label").GetComponent<TMP_Text>();
            ui.drumVisibilityLabel.enableAutoSizing=true;ui.drumVisibilityLabel.fontSizeMin=12;
            ConfigureHierarchy((RectTransform)ui.transform);
        }

        public static void ConfigureInstruments(MidiPlayerUI ui)
        {
            Transform parent=ui.transform;
            TMP_FontAsset font=ui.titleLabel.font;
            string[] captions={"Digital piano","White grand piano","Marimba","Violin","Viola","Cello","Double Bass","String Ensemble"};
            Transform libraryName=parent.Find("LibraryName");
            if(libraryName!=null) libraryName.gameObject.SetActive(false);
            RectTransform entry=Rect(Child(parent,"Instrument",true),new Vector2(138,210),new Vector2(204,44));
            Image entryImage=Get<Image>(entry); Paint(entryImage,Inset); entryImage.raycastTarget=true;
            Button button=Get<Button>(entry); button.targetGraphic=entryImage;
            button.colors=Colors(Color.white,new Color(2,3,3),new Color(1.5f,2,2),Muted);
            Listen(button.onClick,ui,"_ToggleInstrumentPanel"); ui.instrumentButton=button;
            ui.instrumentEntryLabel=Label(entry,"Label",captions[Mathf.Clamp(ui.facade.selectedInstrument,0,7)],new Vector2(-92,0),new Vector2(164,32),17,Accent,font);
            ui.instrumentEntryLabel.alignment=TextAlignmentOptions.MidlineLeft;
            ui.instrumentEntryLabel.enableAutoSizing = true;
            ui.instrumentEntryLabel.fontSizeMin = 12; ui.instrumentEntryLabel.fontSizeMax = 17;
            Image arrow=Icon(entry,"Chevron","Chevron",new Vector2(82,0),14,Accent);
            arrow.rectTransform.pivot=new Vector2(.5f,.5f); arrow.rectTransform.anchoredPosition=new Vector2(86,0);
            arrow.raycastTarget=false; arrow.transform.localRotation=Quaternion.Euler(0,0,-90);
            ui.instrumentDropdownArrow=(RectTransform)arrow.transform;

            RectTransform overlay=Rect(Child(parent,"InstrumentPanel",true),new Vector2(-500,0),new Vector2(1000,480));
            Image hit=Get<Image>(overlay); hit.sprite=null; hit.color=Color.clear; hit.raycastTarget=true;
            Button dismiss=Get<Button>(overlay); dismiss.targetGraphic=hit; dismiss.transition=Selectable.Transition.None;
            Listen(dismiss.onClick,ui,"_CloseInstrumentPanel");
            int options=ui.facade.instrumentRoots.Length==8?8:3;
            int visibleOptions=options==8?7:options;
            float listHeight=Mathf.Min(320,visibleOptions*40);
            bool hasStyle=ui.facade.HasSelectedStrings();
            float panelHeight=listHeight+16+(hasStyle && ui.facade.instrumentRouter==null?56:0);
            Image panel=Box(overlay,"Content",new Vector2(138,180-panelHeight*.5f),new Vector2(280,panelHeight),Tile);
            panel.raycastTarget=true;
            Button blocker=Get<Button>(panel.transform); blocker.targetGraphic=panel; blocker.transition=Selectable.Transition.None;
            ui.instrumentDropdownPanel=(RectTransform)panel.transform;
            ui.instrumentTitle=Label(panel.transform,"Title","",Vector2.zero,new Vector2(240,32),20,Accent,font);
            ui.instrumentTitle.gameObject.SetActive(false);
            RectTransform viewport=Rect(Child(panel.transform,"Viewport",true),new Vector2(-132,panelHeight*.5f-8-listHeight*.5f),new Vector2(264,listHeight));
            Get<RectMask2D>(viewport);
            RectTransform content=Rect(Child(viewport,"Options",true),Vector2.zero,new Vector2(264,visibleOptions*40));
            content.anchorMin=content.anchorMax=new Vector2(0,1); content.pivot=new Vector2(0,1); content.anchoredPosition=Vector2.zero;
            ScrollRect scroll=Get<ScrollRect>(panel.transform); scroll.viewport=viewport; scroll.content=content;
            scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped;
            scroll.inertia=false; scroll.scrollSensitivity=24;
            ui.instrumentDropdownScroll=scroll; ui.instrumentDropdownViewport=viewport;
            ui.instrumentOptionButtons=new Button[options]; ui.instrumentLabels=new TMP_Text[options]; ui.instrumentSelectionMarks=new GameObject[options];
            ui.instrumentToggles = new Toggle[options];
            string[] methods={"_SelectDigitalPiano","_SelectGrandPiano","_SelectMarimba","_SelectViolin","_SelectViola","_SelectCello","_SelectContrabass","_SelectStrings"};
            string[] names={"DigitalPiano","GrandPiano","Marimba","Violin","Viola","Cello","Contrabass","Strings"};
            for(int i=0;i<options;i++)
            {
                Transform prior=content.Find(names[i]) ?? panel.transform.Find(names[i]);
                if(prior==null) prior=Child(content,names[i],true);
                if(prior.parent!=content) prior.SetParent(content,false);
                RectTransform option=Rect(prior,new Vector2(0,-20-i*40),new Vector2(264,38));
                option.gameObject.SetActive(i < visibleOptions);
                option.anchorMin=option.anchorMax=new Vector2(0,1);
                Image image=Get<Image>(option); Paint(image,Inset); image.raycastTarget=true;
                Button choice=Get<Button>(option); choice.targetGraphic=image; choice.colors=button.colors; Listen(choice.onClick,ui,methods[i]);
                ui.instrumentOptionButtons[i]=choice;
                bool multi = ui.facade.instrumentRouter != null;
                ui.instrumentLabels[i]=Label(option,"Label",captions[i],new Vector2(multi ? -90 : -122,0),new Vector2(multi ? 204 : 240,30),17,Color.white,font);
                ui.instrumentSelectionMarks[i]=Box(option,"Selected",new Vector2(multi ? -116 : -122,multi ? 0 : -16),new Vector2(multi ? 14 : 240,multi ? 14 : 2),Accent).gameObject;
                ui.instrumentSelectionMarks[i].SetActive(ui.facade.IsInstrumentChecked(i));
                if (multi)
                {
                    Image frame = Box(option, "Checkbox", new Vector2(-120, 0), new Vector2(22, 22), Muted);
                    frame.sprite = null; frame.type = Image.Type.Simple;
                    var fill = ui.instrumentSelectionMarks[i].GetComponent<Image>(); fill.sprite = null; fill.type = Image.Type.Simple;
                    frame.transform.SetAsFirstSibling();
                    // Keep the original Button component/references. Selectable disallows a
                    // Button and Toggle on one object, so the transparent input child owns Toggle.
                    Image input = Box(option, "CheckboxToggle", new Vector2(-132,0), new Vector2(264,38), Color.clear);
                    input.raycastTarget = true;
                    var toggle = Get<Toggle>(input.transform); choice.enabled = false;
                    toggle.targetGraphic = image; toggle.graphic = ui.instrumentSelectionMarks[i].GetComponent<Image>();
                    toggle.toggleTransition = Toggle.ToggleTransition.None; toggle.colors = choice.colors; toggle.group = null;
                    toggle.SetIsOnWithoutNotify(ui.facade.IsInstrumentChecked(i));
                    Listen(toggle.onValueChanged, ui, methods[i]); ui.instrumentToggles[i] = toggle;
                }
            }
            if(options==8)
            {
                RectTransform styles=Rect(Child(panel.transform,"StringsStyle",true),new Vector2(-132,-panelHeight*.5f+30),new Vector2(264,44));
                styles.SetAsLastSibling();
                ui.stringsStylePanel=styles.gameObject;
                // Reuse entries previously moved into the instrument rows on repeated updates.
                string[] existingEntries={"Entry","EntryViola","EntryCello","EntryContrabass"};
                if(ui.facade.instrumentRouter!=null)
                    for(int i=0;i<4;i++)
                    {
                        var existing=ui.instrumentOptionButtons[i+3].transform.Find(existingEntries[i]);
                        if(existing!=null) existing.SetParent(styles,false);
                    }
                Image styleEntry=Box(styles,"Entry",new Vector2(-132,0),new Vector2(264,40),Inset); styleEntry.raycastTarget=true;
                Button styleButton=Get<Button>(styleEntry.transform); styleButton.targetGraphic=styleEntry; styleButton.colors=button.colors;
                Listen(styleButton.onClick,ui,"_ToggleStringsStyleDropdown"); ui.stringsStyleDropdownButton=styleButton;
                ui.stringsStyleEntryLabel=Label(styleEntry.transform,"Label","Articulation: Sustain",new Vector2(-120,0),new Vector2(216,30),17,Accent,font);
                ui.stringsStyleEntryLabel.enableAutoSizing=true; ui.stringsStyleEntryLabel.fontSizeMin=13; ui.stringsStyleEntryLabel.fontSizeMax=17;
                Image styleArrow=Icon(styleEntry.transform,"Chevron","Chevron",new Vector2(108,0),14,Accent);
                styleArrow.rectTransform.pivot=new Vector2(.5f,.5f); styleArrow.rectTransform.anchoredPosition=new Vector2(116,0);
                styleArrow.raycastTarget=false; styleArrow.transform.localRotation=Quaternion.Euler(0,0,-90);
                ui.stringsStyleDropdownArrow=(RectTransform)styleArrow.transform;
                bool independent=ui.facade.instrumentRouter!=null;
                ui.stringStyleEntries=independent?new Button[4]:new Button[0];
                ui.stringStyleEntryLabels=independent?new TMP_Text[4]:new TMP_Text[0];
                ui.stringStyleArrows=independent?new RectTransform[4]:new RectTransform[0];
                if(independent)
                {
                    ui.stringStyleEntries[0]=styleButton; ui.stringStyleEntryLabels[0]=ui.stringsStyleEntryLabel; ui.stringStyleArrows[0]=ui.stringsStyleDropdownArrow;
                    string[] entryNames={"Entry","EntryViola","EntryCello","EntryContrabass"};
                    string[] entryEvents={"_ToggleStringsStyleDropdown","_ViolaStyle","_CelloStyle","_ContrabassStyle"};
                    for(int i=1;i<4;i++)
                    {
                        Image image=Box(styles,entryNames[i],new Vector2(-132,-i*40),new Vector2(264,38),Inset); image.raycastTarget=true;
                        Button choice=Get<Button>(image.transform); choice.targetGraphic=image; choice.colors=button.colors; Listen(choice.onClick,ui,entryEvents[i]);
                        ui.stringStyleEntries[i]=choice;
                        var label=Label(image.transform,"Label",captions[i+3]+": Automatic",new Vector2(-120,0),new Vector2(216,30),17,Accent,font);
                        label.enableAutoSizing=true; label.fontSizeMin=12; label.fontSizeMax=17; ui.stringStyleEntryLabels[i]=label;
                        var chevron=Icon(image.transform,"Chevron","Chevron",new Vector2(108,0),14,Accent);
                        chevron.rectTransform.pivot=new Vector2(.5f,.5f);chevron.rectTransform.anchoredPosition=new Vector2(116,0);chevron.raycastTarget=false;
                        chevron.transform.localRotation=Quaternion.Euler(0,0,-90); ui.stringStyleArrows[i]=(RectTransform)chevron.transform;
                    }
                }
                Image styleList=Box(styles,"Options",new Vector2(-132,independent?106:86),new Vector2(264,independent?168:128),Tile);
                styleList.transform.SetAsLastSibling();
                styleList.raycastTarget=true; Button styleBlocker=Get<Button>(styleList.transform); styleBlocker.targetGraphic=styleList; styleBlocker.transition=Selectable.Transition.None;
                ui.stringsStyleOptions=styleList.gameObject;
                int styleCount=independent?4:3;
                ui.stringsStyleButtons=new Button[styleCount]; ui.stringsStyleLabels=new TMP_Text[styleCount]; ui.stringsStyleMarks=new GameObject[styleCount];
                string[] events={"_StringsSustain","_StringsSpiccato","_StringsPizzicato","_StringsAutomatic"};
                string[] styleCaptions={"Sustain","Spiccato","Pizzicato","Automatic"};
                for(int i=0;i<styleCount;i++)
                {
                    string optionName=i==3?"StyleAuto":"Style"+i;
                    Transform prior=styleList.transform.Find(optionName) ?? styles.Find(optionName);
                    if(prior==null) prior=Child(styleList.transform,optionName,true);
                    if(prior.parent!=styleList.transform) prior.SetParent(styleList.transform,false);
                    int row=independent?(i==3?0:i+1):i;
                    RectTransform option=Rect(prior,new Vector2(-124,(independent?60:40)-row*40),new Vector2(248,36));
                    Image image=Get<Image>(option); Paint(image,Inset); image.raycastTarget=true;
                    Button choice=Get<Button>(option); choice.targetGraphic=image; choice.colors=button.colors; Listen(choice.onClick,ui,events[i]);
                    ui.stringsStyleButtons[i]=choice;
                    ui.stringsStyleLabels[i]=Label(option,"Label",styleCaptions[i],new Vector2(-114,0),new Vector2(228,30),17,Color.white,font);
                    ui.stringsStyleMarks[i]=Box(option,"Selected",new Vector2(-114,-15),new Vector2(228,2),Accent).gameObject;
                }
                if(independent)
                {
                    styles.sizeDelta=new Vector2(264,listHeight); styles.anchoredPosition=new Vector2(-132,0);
                    for(int i=0;i<4;i++)
                    {
                        var row=ui.instrumentOptionButtons[i+3].transform;
                        var inline=(RectTransform)ui.stringStyleEntries[i].transform;
                        inline.SetParent(row,false); Rect(inline,new Vector2(18,0),new Vector2(108,34)); inline.SetAsLastSibling(); inline.gameObject.SetActive(true);
                        var label=ui.stringStyleEntryLabels[i]; Rect(label.transform,new Vector2(-46,0),new Vector2(78,28));
                        label.alignment=TextAlignmentOptions.MidlineLeft; label.fontSizeMin=11; label.fontSizeMax=16;
                        ui.stringStyleArrows[i].anchoredPosition=new Vector2(44,0);
                        var name=ui.instrumentLabels[i+3]; ((RectTransform)name.transform).sizeDelta=new Vector2(104,30);
                        name.enableAutoSizing=true; name.fontSizeMin=12; name.fontSizeMax=17;
                        var input=(RectTransform)ui.instrumentToggles[i+3].transform;
                        input.sizeDelta=new Vector2(146,38);
                    }
                }
                styleList.gameObject.SetActive(false); styles.gameObject.SetActive(hasStyle);
            }
            ui.instrumentPanel=overlay.gameObject; overlay.gameObject.SetActive(false);
            ConfigureHierarchy((RectTransform)parent);
        }

        private static void AddStepDots(Slider slider, int count)
        {
            Transform area = slider.handleRect.parent;
            RectTransform dots = Rect(Child(area, "StepDots", true), Vector2.zero, Vector2.zero);
            dots.anchorMin = Vector2.zero;
            dots.anchorMax = Vector2.one;
            dots.offsetMin = dots.offsetMax = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                RectTransform dot = Rect(Child(dots, "Dot_" + i, true), Vector2.zero, new Vector2(8, 8));
                dot.anchorMin = dot.anchorMax = new Vector2((float)i / (count - 1), 0.5f);
                dot.pivot = new Vector2(0.5f, 0.5f);
                Image image = Get<Image>(dot);
                image.sprite = MidiPlayerUISkinAssets.Icon("Dot");
                image.type = Image.Type.Simple;
                image.color = Accent;
                image.raycastTarget = false;
            }
            dots.SetAsFirstSibling();
        }

        private static GameObject LandingOption(Transform parent, MidiPlayerUI ui, string name, string text, float x,
            TMP_FontAsset font, string method)
        {
            RectTransform rect = Rect(Child(parent, name, true), new Vector2(x, -62), new Vector2(216, 44));
            Image image = Get<Image>(rect);
            Paint(image, Inset);
            image.raycastTarget = true;
            Button button = Get<Button>(rect);
            button.targetGraphic = image;
            button.colors = Colors(Color.white, new Color(2, 3, 3), new Color(1.5f, 2, 2), Muted);
            Listen(button.onClick, ui, method);
            Label(rect, "Label", text, new Vector2(-100, 2), new Vector2(200, 32), 17, Accent, font).alignment = TextAlignmentOptions.Center;
            return Box(rect, "Selected", new Vector2(-92, -17), new Vector2(184, 3), Accent).gameObject;
        }

        public static void ConfigureLibraryInput(MidiPlayerUI ui)
        {
            if (ui.libraryScroll == null) return;
            EventTrigger trigger = Get<EventTrigger>(ui.libraryScroll.transform);
            ListenPointer(trigger, EventTriggerType.PointerEnter, ui, "_OnLibraryPointerEnter");
            ListenPointer(trigger, EventTriggerType.PointerExit, ui, "_OnLibraryPointerExit");
        }

        public static void ConfigureProgressSlider(MidiPlayerUI ui)
        {
            Slider slider = ui.progressSlider;
            var rect = (RectTransform)slider.transform;
            // Reuse the current placement and existing track objects when upgrading.
            Track(rect.parent, rect.name, rect.anchoredPosition, rect.sizeDelta, true);
            slider.interactable = true;
            Listen(slider.onValueChanged, ui, "_OnProgressChanged");
            EventTrigger trigger = Get<EventTrigger>(rect);
            ListenPointer(trigger, EventTriggerType.PointerDown, ui, "_OnProgressPointerDown");
            ListenPointer(trigger, EventTriggerType.PointerUp, ui, "_OnProgressPointerUp");
            ListenPointer(trigger, EventTriggerType.EndDrag, ui, "_OnProgressPointerUp");
        }

        private static void ListenPointer(EventTrigger trigger, EventTriggerType type, MidiPlayerUI ui, string eventName)
        {
            EventTrigger.Entry entry = trigger.triggers.Find(item => item.eventID == type);
            if (entry == null)
            {
                entry = new EventTrigger.Entry { eventID = type };
                trigger.triggers.Add(entry);
            }
            Listen(entry.callback, ui, eventName);
        }

        public static void SynchronizeLibrary(MidiPlayerUI ui, MidiPlayerFacade facade, MidiPlayerConfigurationAsset config)
        {
            // Upgrade just the library region on an existing scene player.
            if (ui.libraryScroll == null)
            {
                BuildLibrary((RectTransform)ui.transform, ui, facade, config, ui.titleLabel.font, true);
                ConfigureLibraryInput(ui);
                MidiPlayerLocalizationBuilder.Configure(ui);
                return;
            }
            Transform scrollRoot = ui.transform.Find("LibraryScroll");
            ScrollRect scroll = scrollRoot == null ? null : scrollRoot.GetComponent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null
                || ui.songButtons == null || ui.songButtons.Length == 0 || ui.songButtons[0] == null)
                throw new InvalidOperationException("曲目菜单不完整，请先在 Configure Player 应用配置");
            RectTransform content = scroll.content;
            RectTransform template = (RectTransform)ui.songButtons[0].transform;
            Vector2 firstPosition = template.anchoredPosition;
            float spacing = ui.libraryRowSpacing;
            var buttons = new MidiSongButton[config.library.songs.Length];
            var wanted = new HashSet<string>();
            for (int i = 0; i < buttons.Length; i++)
            {
                var song = config.library.songs[i];
                string name = "Song_" + song.songId;
                wanted.Add(name);
                RectTransform row = content.Find(name) as RectTransform;
                if (row == null)
                {
                    GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, content, false);
                    copy.name = name;
                    Undo.RegisterCreatedObjectUndo(copy, "Add MIDI Song Button");
                    row = (RectTransform)copy.transform;
                }
                row.anchoredPosition = new Vector2(firstPosition.x, firstPosition.y - i * spacing);
                row.Find("Number").GetComponent<TMP_Text>().text = (i + 1).ToString("00");
                row.Find("Label").GetComponent<TMP_Text>().text = song.title;
                row.Find("Duration").GetComponent<TMP_Text>().text = Duration(song.durationMs, facade.library.bank.maxReleaseMs);
                MidiSongButton songButton = row.GetComponent<MidiSongButton>();
                songButton.facade = facade;
                songButton.songId = song.songId;
                songButton.button = row.GetComponent<Button>();
                Image background = row.GetComponent<Image>();
                if (background != null) background.raycastTarget = true;
                songButton.selectionMark = row.Find("Selected").GetComponent<Image>();
                songButton.numberLabel = row.Find("Number").GetComponent<TMP_Text>();
                songButton.selectionMark.enabled = song.songId == facade.library.defaultSongId;
                Listen(songButton.button.onClick, songButton, "_OnClick");
                buttons[i] = songButton;
            }
            ui.songButtons = buttons;
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name.StartsWith("Song_", StringComparison.Ordinal) && !wanted.Contains(content.GetChild(i).name))
                    Undo.DestroyObjectImmediate(content.GetChild(i).gameObject);
            BuildFolders(content, ui, config.library, ui.titleLabel.font);
            ConfigureLibrarySearch(ui, config.library, ui.titleLabel.font);
            ConfigureLibraryInput(ui);
            MidiPlayerLocalizationBuilder.Configure(ui);
            ui.transform.Find("LibraryName").GetComponent<TMP_Text>().text = config.library.title;
            var initialSong = facade.library.FindSong(facade.library.defaultSongId);
            ui.titleLabel.text = initialSong.title;
            ui.timeLabel.text = "00:00 / " + Duration(initialSong.durationMs, facade.library.bank.maxReleaseMs);
            Canvas.ForceUpdateCanvases();
            ui.OpenLibraryFolder(ui.folderButtons.Length > 0 ? -1 : -2);
        }

        private static void BuildLibrary(RectTransform root, MidiPlayerUI ui, MidiPlayerFacade facade, MidiPlayerConfigurationAsset config, TMP_FontAsset font, bool preserveSongStyle = false)
        {
            RectTransform template = preserveSongStyle && ui.songButtons != null && ui.songButtons.Length > 0 && ui.songButtons[0] != null
                ? (RectTransform)ui.songButtons[0].transform : null;
            preserveSongStyle = preserveSongStyle && template != null;
            float rowHeight = template == null ? 60f : template.rect.height;
            float spacing = template == null ? 68f : rowHeight + 8f;
            float rowX = template == null ? 0f : template.anchoredPosition.x;
            if (template != null && ui.songButtons.Length > 1 && ui.songButtons[1] != null)
                spacing = Mathf.Max(rowHeight, template.anchoredPosition.y - ((RectTransform)ui.songButtons[1].transform).anchoredPosition.y);
            TMP_Text caption = preserveSongStyle ? root.Find("LibraryCaption")?.GetComponent<TMP_Text>() : null;
            if (caption == null)
                caption = Label(root, "LibraryCaption", "收藏曲库", new Vector2(126, 141), new Vector2(206, 28), 19, Color.white, font);
            caption.text = "收藏曲库";
            if (!preserveSongStyle) caption.fontStyle = FontStyles.Bold;
            ui.libraryCountLabel = preserveSongStyle ? root.Find("TrackCount")?.GetComponent<TMP_Text>() : null;
            if (ui.libraryCountLabel == null)
            {
                ui.libraryCountLabel = Label(root, "TrackCount", config.library.songs.Length.ToString("00") + " 首", new Vector2(336, 141), new Vector2(116, 26), 13, Muted, font);
                ui.libraryCountLabel.alignment = TextAlignmentOptions.MidlineRight;
            }
            RectTransform backRect = Rect(Child(root, "LibraryBack", true), new Vector2(124, 97), new Vector2(34, 34));
            Image backImage = Get<Image>(backRect);
            Paint(backImage, Inset);
            backImage.raycastTarget = true;
            ui.libraryBackButton = Get<Button>(backRect);
            ui.libraryBackButton.targetGraphic = backImage;
            ui.libraryBackButton.colors = Colors(Color.white, Hex(0x9AB8C4), Hex(0x658C91), Hex(0x414B58));
            Icon(backRect, "Icon", "Back", new Vector2(-12, 0), 24, Accent);
            Listen(ui.libraryBackButton.onClick, ui, "_OnLibraryBackPressed");
            ui.libraryPathLabel = Label(root, "LibraryPath", "收藏夹", new Vector2(170, 97), new Vector2(282, 30), 16, Accent, font);
            Box(root, "LibraryDivider", new Vector2(126, 73), new Vector2(326, 1), new Color(1, 1, 1, 0.09f), false);
            RectTransform scrollRoot = Rect(Child(root, "LibraryScroll", true), new Vector2(122, -70), new Vector2(336, 268));
            // An empty part of the viewport remains draggable, without covering other controls.
            Image input = Get<Image>(scrollRoot);
            input.color = Color.clear;
            input.raycastTarget = true;
            RectTransform viewport = Rect(Child(scrollRoot, "Viewport", true), new Vector2(-168, 0), new Vector2(318, 268));
            Get<RectMask2D>(viewport);
            Transform songs = root.Find("Songs");
            if (songs == null) songs = viewport.Find("Songs");
            if (songs == null) songs = Child(viewport, "Songs", true);
            songs.SetParent(viewport, false);
            RectTransform content = (RectTransform)songs;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, Mathf.Max(268, (config.library.songs.Length - 1) * spacing + rowHeight));
            ui.songButtons = new MidiSongButton[config.library.songs.Length];
            var wanted = new HashSet<string>();
            for (int i = 0; i < ui.songButtons.Length; i++)
            {
                var song = config.library.songs[i];
                string name = "Song_" + song.songId;
                wanted.Add(name);
                RectTransform row = preserveSongStyle ? content.Find(name) as RectTransform : null;
                if (preserveSongStyle && row == null)
                {
                    GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, content, false);
                    copy.name = name;
                    Undo.RegisterCreatedObjectUndo(copy, "Add MIDI Song Button");
                    row = (RectTransform)copy.transform;
                }
                if (!preserveSongStyle)
                {
                    row = Rect(Child(content, name, true), new Vector2(0, -i * spacing - rowHeight * 0.5f), new Vector2(318, rowHeight));
                    row.anchorMin = row.anchorMax = new Vector2(0, 1);
                }
                else row.anchoredPosition = new Vector2(rowX, -i * spacing - rowHeight * 0.5f);
                Button button = Get<Button>(row);
                Image background = Get<Image>(row);
                background.raycastTarget = true;
                Image selection;
                if (preserveSongStyle)
                {
                    row.Find("Number").GetComponent<TMP_Text>().text = (i + 1).ToString("00");
                    row.Find("Label").GetComponent<TMP_Text>().text = song.title;
                    row.Find("Duration").GetComponent<TMP_Text>().text = Duration(song.durationMs, facade.library.bank.maxReleaseMs);
                    selection = row.Find("Selected").GetComponent<Image>();
                }
                else
                {
                    Paint(background, Inset);
                    button.targetGraphic = background;
                    button.colors = Colors(Color.white, Hex(0x9AB8C4), Hex(0x658C91), Hex(0x78818B));
                    Label(row, "Number", (i + 1).ToString("00"), new Vector2(-143, 0), new Vector2(29, 28), 14, Muted, font);
                    Label(row, "Label", song.title, new Vector2(-103, 0), new Vector2(195, 42), 20, Accent, font);
                    Label(row, "Duration", Duration(song.durationMs, facade.library.bank.maxReleaseMs), new Vector2(96, 0), new Vector2(49, 28), 14, Muted, font).alignment = TextAlignmentOptions.MidlineRight;
                    selection = Box(row, "Selected", new Vector2(-157, 0), new Vector2(3, 30), Accent);
                }
                selection.enabled = song.songId == facade.library.defaultSongId;
                MidiSongButton songButton = row.GetComponent<MidiSongButton>();
                if (songButton == null) songButton = row.gameObject.AddUdonSharpComponent<MidiSongButton>();
                songButton.facade = facade;
                songButton.songId = song.songId;
                songButton.button = button;
                songButton.selectionMark = selection;
                songButton.numberLabel = row.Find("Number").GetComponent<TMP_Text>();
                Listen(button.onClick, songButton, "_OnClick");
                ui.songButtons[i] = songButton;
            }
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name.StartsWith("Song_", StringComparison.Ordinal) && !wanted.Contains(content.GetChild(i).name))
                    Undo.DestroyObjectImmediate(content.GetChild(i).gameObject);

            RectTransform barRect = Rect(Child(scrollRoot, "Scrollbar", true), new Vector2(158, 0), new Vector2(10, 268));
            if (!preserveSongStyle) Paint(Get<Image>(barRect), Inset);
            Scrollbar bar = Get<Scrollbar>(barRect);
            RectTransform sliding = Rect(Child(barRect, "SlidingArea", true), Vector2.zero, Vector2.zero);
            sliding.anchorMin = Vector2.zero; sliding.anchorMax = Vector2.one;
            sliding.offsetMin = sliding.offsetMax = Vector2.zero;
            Image thumb = preserveSongStyle ? sliding.Find("Handle")?.GetComponent<Image>() : null;
            if (thumb == null) thumb = Box(sliding, "Handle", Vector2.zero, Vector2.zero, Accent);
            bar.handleRect = null;
            bar.handleRect = (RectTransform)thumb.transform;
            bar.targetGraphic = thumb;
            bar.direction = Scrollbar.Direction.BottomToTop;
            if (!preserveSongStyle) bar.colors = Colors(Color.white, Color.white, Hex(0xA4BFC2), Hex(0x78818B));
            ScrollRect scroll = Get<ScrollRect>(scrollRoot);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            if (!preserveSongStyle)
            {
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.inertia = false;
                scroll.scrollSensitivity = 34;
            }
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            ui.libraryScroll = scroll;
            ui.libraryRowHeight = rowHeight;
            ui.libraryRowSpacing = spacing;
            ui.libraryEmptyLabel = Label(viewport, "Empty", "这个目录还没有曲目", new Vector2(-145, 0), new Vector2(290, 80), 17, Muted, font);
            ui.libraryEmptyLabel.alignment = TextAlignmentOptions.Center;
            BuildFolders(content, ui, config.library, font);
            ConfigureLibrarySearch(ui, config.library, font);
            Canvas.ForceUpdateCanvases();
            ui.OpenLibraryFolder(ui.folderButtons.Length > 0 ? -1 : -2);
        }

        private static void ConfigureLibrarySearch(MidiPlayerUI ui, MidiLibraryAsset library, TMP_FontAsset font)
        {
            RectTransform root = (RectTransform)ui.transform;
            RectTransform entry = Rect(Child(root, "LibrarySearch", true), new Vector2(126, 50), new Vector2(326, 36));
            Image background = Get<Image>(entry);
            Paint(background, Inset);
            background.raycastTarget = true;
            TMP_InputField input = Get<TMP_InputField>(entry);
            input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 128;
            input.richText = false;
            input.customCaretColor = true;
            input.caretColor = Accent;
            input.selectionColor = new Color(0.3f, 0.7f, 0.75f, 0.35f);
            RectTransform area = Rect(Child(entry, "TextArea", true), new Vector2(-151, 0), new Vector2(274, 30));
            Get<RectMask2D>(area);
            TMP_Text text = Label(area, "Text", "", new Vector2(-137, 0), new Vector2(274, 30), 16, Color.white, font);
            text.richText = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            TMP_Text placeholder = Label(area, "Placeholder", "Search all songs...", new Vector2(-137, 0), new Vector2(274, 30), 16, Muted, font);
            placeholder.enableWordWrapping = false;
            input.textViewport = area;
            input.textComponent = (TextMeshProUGUI)text;
            input.placeholder = placeholder;
            input.SetTextWithoutNotify("");
            Listen(input.onValueChanged, ui, "_OnLibrarySearchChanged");
            RectTransform clear = Rect(Child(entry, "Clear", true), new Vector2(128, 0), new Vector2(30, 32));
            Image clearImage = Get<Image>(clear);
            clearImage.color = Color.clear;
            clearImage.raycastTarget = true;
            Button clearButton = Get<Button>(clear);
            clearButton.targetGraphic = clearImage;
            Label(clear, "Label", "×", new Vector2(-15, 0), new Vector2(30, 30), 22, Accent, font).alignment = TextAlignmentOptions.Center;
            Listen(clearButton.onClick, ui, "_ClearLibrarySearch");
            ui.librarySearchInput = input;
            ui.librarySearchPlaceholder = placeholder;
            ui.librarySearchClearButton = clearButton;
            clear.gameObject.SetActive(false);
            RectTransform scroll = (RectTransform)ui.libraryScroll.transform;
            scroll.anchoredPosition = new Vector2(122, -92);
            scroll.sizeDelta = new Vector2(336, 224);
            ui.libraryScroll.viewport.sizeDelta = new Vector2(318, 224);
            ((RectTransform)ui.libraryScroll.verticalScrollbar.transform).sizeDelta = new Vector2(10, 224);
            ((RectTransform)root.Find("LibraryDivider")).anchoredPosition = new Vector2(126, 26);
            ui.songSearchKeys = new string[library.songs.Length];
            for (int i = 0; i < library.songs.Length; i++)
            {
                MidiSongAsset song = library.songs[i];
                string key = song.title + " " + song.author + " " + System.IO.Path.GetFileNameWithoutExtension(song.sourcePath);
                foreach (MidiLibraryFolder folder in library.folders ?? Array.Empty<MidiLibraryFolder>())
                    if (Array.IndexOf(folder.songs ?? Array.Empty<MidiSongAsset>(), song) >= 0) key += " " + folder.path;
                ui.songSearchKeys[i] = key.ToLowerInvariant().Replace('_', ' ');
            }
            ConfigureHierarchy(root);
        }

        private static void BuildFolders(RectTransform content, MidiPlayerUI ui, MidiLibraryAsset library, TMP_FontAsset font)
        {
            MidiDataBaker.ValidateFolders(library);
            var paths = new List<string>();
            if (library.folders != null)
                foreach (MidiLibraryFolder folder in library.folders)
                {
                    string path = "";
                    foreach (string segment in folder.path.Split('/'))
                    {
                        path = path.Length == 0 ? segment : path + "/" + segment;
                        if (!paths.Contains(path)) paths.Add(path);
                    }
                }
            var buttons = new MidiLibraryFolderButton[paths.Count];
            var wanted = new HashSet<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                int slash = path.LastIndexOf('/');
                int parent = slash < 0 ? -1 : paths.IndexOf(path.Substring(0, slash));
                MidiSongAsset[] members = Array.Empty<MidiSongAsset>();
                foreach (MidiLibraryFolder folder in library.folders)
                    if (folder.path == path) { members = folder.songs ?? Array.Empty<MidiSongAsset>(); break; }
                int children = 0;
                foreach (string other in paths)
                {
                    int split = other.LastIndexOf('/');
                    if (split >= 0 && other.Substring(0, split) == path) children++;
                }
                string name = "Folder_" + Uri.EscapeDataString(path);
                wanted.Add(name);
                string summary = members.Length + " 首曲目" + (children > 0 ? " · " + children + " 个子目录" : "");
                RectTransform row = FolderRow(content, name, slash < 0 ? path : path.Substring(slash + 1), summary, "Folder", font);
                MidiLibraryFolderButton button = row.GetComponent<MidiLibraryFolderButton>();
                if (button == null) button = row.gameObject.AddUdonSharpComponent<MidiLibraryFolderButton>();
                button.playerUI = ui;
                button.folderIndex = i;
                button.parentFolderIndex = parent;
                button.folderPath = path;
                button.songIndices = new int[members.Length];
                for (int j = 0; j < members.Length; j++) button.songIndices[j] = Array.IndexOf(library.songs, members[j]);
                Listen(row.GetComponent<Button>().onClick, button, "_OnClick");
                buttons[i] = button;
            }
            ui.folderButtons = buttons;
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name.StartsWith("Folder_", StringComparison.Ordinal) && !wanted.Contains(content.GetChild(i).name))
                    Undo.DestroyObjectImmediate(content.GetChild(i).gameObject);
            ui.allSongsRow = FolderRow(content, "AllSongs", "全部曲目", library.songs.Length + " 首曲目", "Note", font);
            Listen(ui.allSongsRow.GetComponent<Button>().onClick, ui, "_OnAllSongsPressed");
            foreach (MidiLibraryFolderButton button in buttons)
                UdonSharpEditorUtility.CopyProxyToUdon(button, ProxySerializationPolicy.All);
        }

        private static RectTransform FolderRow(Transform content, string name, string title, string summary, string icon, TMP_FontAsset font)
        {
            RectTransform row = Rect(Child(content, name, true), new Vector2(0, -30), new Vector2(318, 60));
            row.anchorMin = row.anchorMax = new Vector2(0, 1);
            Image background = Get<Image>(row);
            Paint(background, Inset);
            background.raycastTarget = true;
            Button button = Get<Button>(row);
            button.targetGraphic = background;
            button.colors = Colors(Color.white, Hex(0x9AB8C4), Hex(0x658C91), Hex(0x78818B));
            Icon(row, "Icon", icon, new Vector2(-143, 0), 24, Accent);
            Label(row, "Label", title, new Vector2(-109, 11), new Vector2(219, 30), 19, Color.white, font);
            Label(row, "Count", summary, new Vector2(-109, -14), new Vector2(219, 22), 12, Muted, font);
            Icon(row, "Chevron", "Chevron", new Vector2(130, 0), 18, Muted);
            return row;
        }

        private static Button Control(Transform parent, string name, string caption, Vector2 position, bool primary, TMP_FontAsset font)
        {
            RectTransform rect = Rect(Child(parent, name, true), position, new Vector2(162, 60));
            Image image = Get<Image>(rect);
            // Transition the background directly so disabled controls remain visibly distinct.
            Paint(image, Color.white);
            Button button = Get<Button>(rect);
            button.targetGraphic = image;
            button.colors = Colors(primary ? Accent : Tile, primary ? Hex(0xB5F3F4) : Hover, primary ? Hex(0x62B9BD) : Inset, Hex(0x414B58));
            Color ink = primary ? Inset : Accent;
            Icon(rect, "Icon", name, new Vector2(-62, 0), 26, ink);
            TMP_Text text = Label(rect, "Label", caption, new Vector2(-22, 0), new Vector2(92, 40), 17, ink, font);
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 0.5f;
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static Slider Track(Transform parent, string name, Vector2 position, Vector2 size, bool handle)
        {
            RectTransform rect = Rect(Child(parent, name, true), position, size);
            Image hit = Get<Image>(rect);
            hit.sprite = null;
            hit.color = Color.clear;
            hit.raycastTarget = handle;
            Image back = Box(rect, "Track", new Vector2(-size.x * 0.5f, 0), new Vector2(size.x, 6), handle ? Inset : Panel);
            RectTransform fillArea = Rect(Child(rect, "FillArea", true), new Vector2(-size.x * 0.5f, 0), new Vector2(size.x, 6));
            // Keep the existing Fill and Handle objects when updating an old skin.
            Transform fillTransform = rect.Find("Fill");
            if (fillTransform == null) fillTransform = fillArea.Find("Fill");
            if (fillTransform == null) fillTransform = Child(fillArea, "Fill", true);
            fillTransform.SetParent(fillArea, false);
            RectTransform fill = (RectTransform)fillTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0, 0.5f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Paint(Get<Image>(fill), Accent);
            Get<Image>(fill).raycastTarget = false;
            Slider slider = Get<Slider>(rect);
            slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
            slider.fillRect = null;
            slider.handleRect = null;
            slider.fillRect = fill;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            if (handle)
            {
                RectTransform handleArea = Rect(Child(rect, "HandleArea", true), new Vector2(-size.x * 0.5f, 0), new Vector2(size.x, 22));
                Transform old = rect.Find("Handle");
                if (old == null) old = handleArea.Find("Handle");
                if (old == null) old = Child(handleArea, "Handle", true);
                old.SetParent(handleArea, false);
                RectTransform thumb = (RectTransform)old;
                thumb.anchorMin = thumb.anchorMax = new Vector2(0, 0.5f);
                thumb.pivot = new Vector2(0.5f, 0.5f);
                // Slider drives both Y anchors to 0/1, so its parent supplies the height.
                thumb.sizeDelta = new Vector2(16, 0);
                thumb.anchoredPosition = Vector2.zero;
                Image thumbImage = Get<Image>(thumb);
                Paint(thumbImage, Color.white);
                thumbImage.raycastTarget = false;
                slider.handleRect = thumb;
                slider.targetGraphic = thumbImage;
                slider.colors = Colors(Color.white, Accent, Hex(0x62B9BD), Muted);
            }
            else
            {
                slider.handleRect = null;
                slider.targetGraphic = null;
            }
            return slider;
        }

        private static Image Icon(Transform parent, string name, string sprite, Vector2 position, float size, Color color)
        {
            Image icon = Box(parent, name, position, new Vector2(size, size), color, false);
            icon.sprite = MidiPlayerUISkinAssets.Icon(sprite);
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            return icon;
        }

        public static void BuildOpening(MidiPlayerFacade facade, TMP_FontAsset font)
        {
            if (facade.playerUI == null || facade.waterfall == null) return;
            MidiPlayerUI ui = facade.playerUI;
            Transform parent = facade.waterfall.transform;
            RectTransform rect = (RectTransform)Child(parent, "SongOpening", true);
            rect.localPosition = new Vector3(0, 0.5f, -0.06f);
            rect.localRotation = Quaternion.identity;
            float width = 0;
            if (facade.waterfall.keyX != null)
                foreach (float x in facade.waterfall.keyX) width = Mathf.Max(width, Mathf.Abs(x) * 2);
            float scale = Mathf.Max(0.001f, width / 1100f);
            rect.localScale = Vector3.one * scale;
            rect.sizeDelta = new Vector2(1000, 230);
            Get<Canvas>(rect).renderMode = RenderMode.WorldSpace;
            ui.opening = Get<CanvasGroup>(rect);
            ui.opening.interactable = false;
            ui.opening.blocksRaycasts = false;
            ui.openingTitle = Label(rect, "Title", "", new Vector2(-470, 35), new Vector2(940, 100), 64, Color.white, font);
            ui.openingTitle.alignment = TextAlignmentOptions.Center;
            ui.openingTitle.fontStyle = FontStyles.Bold;
            ui.openingTitle.enableAutoSizing = true;
            ui.openingTitle.fontSizeMin = 22;
            ui.openingTitle.fontSizeMax = 64;
            ui.openingAuthor = Label(rect, "Author", "", new Vector2(-450, -58), new Vector2(900, 50), 30, Muted, font);
            ui.openingAuthor.alignment = TextAlignmentOptions.Center;
            rect.gameObject.SetActive(false);
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 position, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI label = Get<TextMeshProUGUI>(Rect(Child(parent, name, true), position, size));
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = color;
            label.fontStyle = FontStyles.Normal;
            label.characterSpacing = 0;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static Image Box(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool rounded = true)
        {
            Image image = Get<Image>(Rect(Child(parent, name, true), position, size));
            Paint(image, color, rounded);
            image.raycastTarget = false;
            return image;
        }

        private static void Paint(Image image, Color color, bool rounded = true)
        {
            image.sprite = rounded ? MidiPlayerUISkinAssets.Rounded : null;
            image.type = rounded ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 2;
            image.color = color;
        }

        private static ColorBlock Colors(Color normal, Color highlighted, Color pressed, Color disabled)
        {
            return new ColorBlock { normalColor = normal, highlightedColor = highlighted, selectedColor = normal, pressedColor = pressed, disabledColor = disabled, colorMultiplier = 1, fadeDuration = 0.1f };
        }

        private static Color Hex(int hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1);
        }

        private static string Duration(int durationMs, int releaseMs)
        {
            long seconds = ((long)durationMs + releaseMs) / 1000;
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        private static Transform Child(Transform parent, string name, bool rect = false)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                var go = rect ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
                child = go.transform;
                child.SetParent(parent, false);
            }
            return child;
        }

        private static RectTransform Rect(Transform child, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)child;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static T Get<T>(Transform transform) where T : Component
        {
            T component = transform.GetComponent<T>();
            return component != null ? component : transform.gameObject.AddComponent<T>();
        }

        private static void Listen(UnityEventBase targetEvent, UdonSharpBehaviour receiver, string eventName)
        {
            while (targetEvent.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(targetEvent, 0);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(receiver);
            if (backing == null) throw new InvalidOperationException("UI 回调没有 Udon backing behaviour: " + receiver.name);
            UnityEventTools.AddStringPersistentListener(targetEvent, backing.SendCustomEvent, eventName);
        }
    }
}
