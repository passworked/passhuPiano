using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MusicMidiPlayer.Logic;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace MusicMidiPlayer.UI
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/UI/Controls")]
    public class MidiPlayerUI : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public GameObject drumVisibilitySelectionMark;
        public TMP_Text drumVisibilityLabel;
        public void _ToggleDrumVisibility()
        {
            if(facade==null || facade.drumView==null)return;
            var view=facade.drumView;
            view.presentationEnabled=!view.presentationEnabled;
            view.gameObject.SetActive(view.presentationEnabled);
            if(drumVisibilitySelectionMark!=null)drumVisibilitySelectionMark.SetActive(view.presentationEnabled);
        }
        public Button instrumentButton;
        public GameObject instrumentPanel;
        public Button[] instrumentOptionButtons = new Button[0];
        public Toggle[] instrumentToggles = new Toggle[0];
        public GameObject[] instrumentSelectionMarks = new GameObject[0];
        public TMP_Text instrumentTitle;
        public TMP_Text instrumentEntryLabel;
        public TMP_Text[] instrumentLabels = new TMP_Text[0];
        public ScrollRect instrumentDropdownScroll;
        public RectTransform instrumentDropdownPanel, instrumentDropdownViewport, instrumentDropdownArrow;
        public Button stringsStyleDropdownButton;
        public TMP_Text stringsStyleEntryLabel;
        public RectTransform stringsStyleDropdownArrow;
        public GameObject stringsStyleOptions;
        public GameObject stringsStylePanel;
        public Button[] stringsStyleButtons = new Button[0];
        public TMP_Text[] stringsStyleLabels = new TMP_Text[0];
        public GameObject[] stringsStyleMarks = new GameObject[0];
        public Button[] stringStyleEntries = new Button[0];
        public TMP_Text[] stringStyleEntryLabels = new TMP_Text[0];
        public RectTransform[] stringStyleArrows = new RectTransform[0];
        private int _stringStyleTarget = 2;
        public void _SelectViolin() { SelectInstrument(3); }
        public void _SelectViola() { SelectInstrument(4); }
        public void _SelectCello() { SelectInstrument(5); }
        public void _SelectContrabass() { SelectInstrument(6); }
        public void _SelectStrings() { SelectInstrument(7); }
        public void _StringsSustain() { SelectStringsStyle(0); }
        public void _StringsSpiccato() { SelectStringsStyle(1); }
        public void _StringsPizzicato() { SelectStringsStyle(2); }
        public void _StringsAutomatic() { SelectStringsStyle(-1); }
        public void _ViolaStyle() { OpenStringsStyle(3); }
        public void _CelloStyle() { OpenStringsStyle(4); }
        public void _ContrabassStyle() { OpenStringsStyle(5); }
        private void SelectStringsStyle(int style)
        {
            if (facade != null)
            {
                if (facade.instrumentRouter != null) facade.CommandStringArticulation(_stringStyleTarget, style + 1);
                else facade.CommandStringsArticulation(style);
            }
            CloseStringsStyleDropdown();
        }
        public void _ToggleStringsStyleDropdown()
        { OpenStringsStyle(2); }
        private void OpenStringsStyle(int target)
        {
            if (stringsStyleOptions == null) return;
            if (facade.instrumentRouter != null && !facade.instrumentRouter.IsEnabled(target)) return;
            bool open = !stringsStyleOptions.activeSelf || _stringStyleTarget != target;
            CloseStringsStyleDropdown(); _stringStyleTarget = target;
            PositionStringStyleOptions();
            stringsStyleOptions.SetActive(open);
            if (target - 2 < stringStyleArrows.Length) stringStyleArrows[target - 2].localRotation = Quaternion.Euler(0, 0, open ? 90 : -90);
            else if (stringsStyleDropdownArrow != null) stringsStyleDropdownArrow.localRotation = Quaternion.Euler(0, 0, open ? 90 : -90);
            RefreshState();
        }
        private void CloseStringsStyleDropdown()
        {
            if (stringsStyleOptions != null) stringsStyleOptions.SetActive(false);
            if (stringsStyleDropdownArrow != null) stringsStyleDropdownArrow.localRotation = Quaternion.Euler(0, 0, -90);
            for (int i = 0; i < stringStyleArrows.Length; i++) stringStyleArrows[i].localRotation = Quaternion.Euler(0, 0, -90);
        }
        private void PositionStringStyleOptions()
        {
            if (facade.instrumentRouter == null || stringStyleEntries.Length != 4 || stringsStyleOptions == null) return;
            var host = (RectTransform)stringsStyleOptions.transform.parent;
            var entry = stringStyleEntries[_stringStyleTarget - 2].transform;
            float y = host.InverseTransformPoint(entry.position).y;
            ((RectTransform)stringsStyleOptions.transform).anchoredPosition = new Vector2(-132, y + 106);
        }

        public void _ToggleInstrumentPanel()
        {
            _CloseLanguagePanel();
            _CloseSettingsPanel();
            CloseStringsStyleDropdown();
            if (instrumentPanel != null)
            {
                bool open = !instrumentPanel.activeSelf;
                instrumentPanel.SetActive(open);
                if (instrumentDropdownArrow != null) instrumentDropdownArrow.localRotation = Quaternion.Euler(0, 0, open ? 90 : -90);
                if (open && instrumentDropdownScroll != null)
                    instrumentDropdownScroll.verticalNormalizedPosition = instrumentOptionButtons.Length <= 1 ? 1 : 1f - facade.selectedInstrument / (float)(instrumentOptionButtons.Length - 1);
            }
        }
        public void _CloseInstrumentPanel()
        {
            if (instrumentPanel != null) instrumentPanel.SetActive(false);
            CloseStringsStyleDropdown();
            if (instrumentDropdownArrow != null) instrumentDropdownArrow.localRotation = Quaternion.Euler(0, 0, -90);
        }
        public void _SelectDigitalPiano() { SelectInstrument(0); }
        public void _SelectGrandPiano() { SelectInstrument(1); }
        public void _SelectMarimba() { SelectInstrument(2); }
        private void SelectInstrument(int index)
        {
            if (facade != null) facade.CommandSelectInstrument(index);
            if (facade == null || facade.instrumentRouter == null) _CloseInstrumentPanel();
            RefreshState();
        }

        public GameObject fileMappingPanel, fileChoicePanel;
        public TMP_Text fileMappingEntryLabel, fileMappingTitle, fileMappingHint, fileMappingBack, fileMappingReset, fileMappingPage;
        public TMP_Text fileMappingPrevious, fileMappingNext, fileChoiceTitle;
        public Button fileMappingEntry, fileResetButton, filePreviousButton, fileNextButton;
        public Button[] fileProgramButtons = new Button[0], fileTargetButtons = new Button[0];
        public TMP_Text[] fileProgramLabels = new TMP_Text[0], fileProgramValues = new TMP_Text[0], fileTargetLabels = new TMP_Text[0];
        public GameObject[] fileTargetMarks = new GameObject[0];
        public RectTransform fileChoiceContent, fileChoiceTitleRect;
        private int[] _filePrograms = new int[128];
        private int _fileCount, _filePage, _fileProgram, _fileSignature;
        private bool _fileRefreshed;
        private string _fileSong = "", _fileMap = "", _fileChoiceSong = "";
        public void _OpenFileMapping()
        {
            if (fileMappingPanel == null || facade == null || facade.instrumentRouter == null) return;
            bool open = !fileMappingPanel.activeSelf;
            _CloseInstrumentPanel(); _CloseLanguagePanel(); _CloseSettingsPanel();
            fileMappingPanel.SetActive(open); _filePage = 0; RefreshFileMapping(true);
        }
        public void _CloseFileChoices() { if (fileChoicePanel != null) fileChoicePanel.SetActive(false); }
        public void _CloseFileMapping()
        {
            _CloseFileChoices(); if (fileMappingPanel != null) fileMappingPanel.SetActive(false);
        }
        public void _ResetFileMapping() { facade.CommandResetFileProgramMap(); _CloseFileChoices(); RefreshFileMapping(true); }
        public void _FilePrevious() { _filePage = Mathf.Max(0, _filePage - 1); _CloseFileChoices(); RefreshFileMapping(true); }
        public void _FileNext() { _filePage = Mathf.Min(Mathf.Max(0, (_fileCount - 1) / 16), _filePage + 1); _CloseFileChoices(); RefreshFileMapping(true); }
        public void _FileProgram0() { OpenFileProgram(0); }
        public void _FileProgram1() { OpenFileProgram(1); }
        public void _FileProgram2() { OpenFileProgram(2); }
        public void _FileProgram3() { OpenFileProgram(3); }
        public void _FileProgram4() { OpenFileProgram(4); }
        public void _FileProgram5() { OpenFileProgram(5); }
        public void _FileProgram6() { OpenFileProgram(6); }
        public void _FileProgram7() { OpenFileProgram(7); }
        public void _FileProgram8() { OpenFileProgram(8); }
        public void _FileProgram9() { OpenFileProgram(9); }
        public void _FileProgram10() { OpenFileProgram(10); }
        public void _FileProgram11() { OpenFileProgram(11); }
        public void _FileProgram12() { OpenFileProgram(12); }
        public void _FileProgram13() { OpenFileProgram(13); }
        public void _FileProgram14() { OpenFileProgram(14); }
        public void _FileProgram15() { OpenFileProgram(15); }
        private void OpenFileProgram(int row)
        {
            int index = _filePage * 16 + row;
            if (fileMappingPanel == null || !fileMappingPanel.activeInHierarchy || facade.isSongLoading
                || index >= _fileCount || _fileSong != facade.selectedSongId) return;
            _fileProgram = _filePrograms[index]; _fileChoiceSong = facade.selectedSongId;
            fileChoicePanel.SetActive(true); RefreshFileMapping(true);
        }
        private string FileProgramName(int program) { return (program + 1).ToString("000") + " · " + Tr("gm." + program.ToString()); }
        private void RefreshFileMapping(bool force)
        {
            if (force && fileMappingEntryLabel != null) fileMappingEntryLabel.text = Tr("file.entry");
            if (fileMappingPanel == null || !fileMappingPanel.activeSelf || facade == null || facade.instrumentRouter == null) return;
            var router = facade.instrumentRouter;
            bool can = facade.isReady && !facade.isSongLoading && facade.gate.CanControl() && facade.network.CanAcceptCommands();
            int signature = (facade.InstrumentMask() * 31 + _loadedLanguage) * 31 + _filePage * 4 + (can ? 1 : 0) + (facade.isSongLoading ? 2 : 0);
            string map = facade.FileProgramMap();
            if (_fileSong != facade.selectedSongId) { _filePage = 0; _CloseFileChoices(); }
            if (!force && _fileRefreshed && signature == _fileSignature && _fileSong == facade.selectedSongId && map == _fileMap) return;
            _fileRefreshed = true; _fileSignature = signature; _fileSong = facade.selectedSongId; _fileMap = map;
            _fileCount = 0;
            if (!facade.isSongLoading)
                for (int p = 0; p < 128; p++) if (router.usedFilePrograms[p]) _filePrograms[_fileCount++] = p;
            _filePage = Mathf.Clamp(_filePage, 0, Mathf.Max(0, (_fileCount - 1) / 16));
            fileMappingTitle.text = Tr("file.title"); fileMappingBack.text = Tr("midi.back"); fileMappingReset.text = Tr("file.reset");
            fileMappingHint.text = facade.isSongLoading ? Tr("file.loading") : _fileCount == 0 ? Tr("file.empty") : Tr("file.hint");
            fileMappingPrevious.text = Tr("file.previous"); fileMappingNext.text = Tr("file.next");
            fileMappingPage.text = (_filePage + 1).ToString() + " / " + Mathf.Max(1, (_fileCount + 15) / 16).ToString();
            fileResetButton.interactable = can && map != "";
            filePreviousButton.interactable = _filePage > 0; fileNextButton.interactable = (_filePage + 1) * 16 < _fileCount;
            for (int i = 0; i < fileProgramButtons.Length; i++)
            {
                int index = _filePage * 16 + i; bool visible = index < _fileCount;
                fileProgramButtons[i].gameObject.SetActive(visible); if (!visible) continue;
                int p = _filePrograms[index], target = router.FileProgramTarget(p);
                fileProgramLabels[i].text = FileProgramName(p);
                string name = MidiTargetName(target);
                if (target >= 0 && !router.IsEnabled(target)) name = Tr("midi.unavailable").Replace("{instrument}", name);
                fileProgramValues[i].text = name; fileProgramButtons[i].interactable = can;
            }
            if (fileChoicePanel == null || !fileChoicePanel.activeSelf) return;
            fileChoiceTitle.text = FileProgramName(_fileProgram);
            float height = 72 + 7 * 40;
            fileChoiceContent.sizeDelta = new Vector2(456, height);
            fileChoiceTitleRect.anchoredPosition = new Vector2(0, height * .5f - 26);
            int row = 0;
            for (int i = 0; i < fileTargetButtons.Length; i++)
            {
                bool enabled = true; fileTargetButtons[i].gameObject.SetActive(true);
                fileTargetButtons[i].interactable = can; fileTargetLabels[i].text = MidiTargetName(i - 1);
                fileTargetMarks[i].SetActive(router.FileProgramTarget(_fileProgram) == i - 1);
                if (enabled) ((RectTransform)fileTargetButtons[i].transform).anchoredPosition = new Vector2(0, height * .5f - 72 - row++ * 40);
            }
        }

        public GameObject midiMappingPanel, midiChoicePanel;
        public Button midiMappingEntry;
        public TMP_Text midiMappingTitle, midiMappingBack, midiChoiceTitle;
        public RectTransform midiChoiceContent, midiChoiceTitleRect;
        public Button[] midiChannelButtons = new Button[0], midiTargetButtons = new Button[0];
        public TMP_Text[] midiChannelNames = new TMP_Text[0], midiChannelValues = new TMP_Text[0], midiTargetLabels = new TMP_Text[0];
        public GameObject[] midiTargetMarks = new GameObject[0];
        private int _midiChannel, _midiSignature;
        private bool _midiRefreshed;
        public GameObject midiPerformancePanel;
        public TMP_Text midiPerformanceEntryLabel, midiPerformanceTitle, midiPerformancePrompt, midiPerformanceHint;
        public TMP_Text midiPerformanceBack, midiPerformanceAdvanced, midiPerformanceRescan;
        public Button midiPerformanceRescanButton;
        public Button[] midiPerformanceTargets = new Button[0];
        public TMP_Text[] midiPerformanceTargetLabels = new TMP_Text[0];
        public GameObject[] midiPerformanceTargetMarks = new GameObject[0];
        private int _midiLearnChannel = -1, _midiLearnState, _midiLearnSignature;
        private bool _midiLearnRefreshed, _midiLearnDrum, _midiExplicitRescan;

        public void _OpenMidiMapping()
        {
            if (midiMappingPanel == null || facade == null || facade.instrumentRouter == null) return;
            midiMappingPanel.SetActive(false); _CloseMidiChoices();
            if (midiPerformancePanel == null) return;
            midiPerformancePanel.SetActive(true);
            _midiLearnChannel = facade.preferredMidiChannel;
            _midiLearnState = _midiLearnChannel < 0 ? 0 : 2; _midiLearnDrum = false; _midiExplicitRescan = false;
            RefreshMidiMapping(true);
        }
        public void _CloseMidiMapping()
        {
            _CloseFileMapping();
            _CloseMidiChoices();
            if (midiMappingPanel != null) midiMappingPanel.SetActive(false);
            if (midiPerformancePanel != null) midiPerformancePanel.SetActive(false);
        }
        public void _OpenMidiAdvanced()
        {
            if (midiMappingPanel == null) return;
            if (midiPerformancePanel != null) midiPerformancePanel.SetActive(false);
            midiMappingPanel.SetActive(true); _CloseMidiChoices(); RefreshMidiMapping(true);
        }
        public void _CloseMidiAdvanced()
        {
            if (midiMappingPanel != null) midiMappingPanel.SetActive(false);
            _CloseMidiChoices();
            if (midiPerformancePanel != null) midiPerformancePanel.SetActive(true);
            RefreshMidiMapping(true);
        }
        public void _RescanMidi()
        {
            _midiLearnChannel = -1; _midiLearnState = 0; _midiLearnDrum = false; _midiExplicitRescan = true;
            RefreshMidiMapping(true);
        }
        public bool TryLearnMidiInput(int channel, int number, int velocity)
        {
            if (midiPerformancePanel == null || !midiPerformancePanel.activeInHierarchy || _midiLearnState == 2
                || channel < 0 || channel > 15 || number < 0 || number > 127 || velocity <= 0 || velocity > 127) return false;
            if (_midiLearnState == 0)
            {
                if (channel == 9) _midiLearnDrum = true;
                else { _midiLearnChannel = channel; _midiLearnState = 1; _midiLearnDrum = false; }
                RefreshMidiMapping(true);
            }
            // Learning notes never trigger the previously configured instrument.
            return true;
        }
        public void _OnMidiPreferencesRestored()
        {
            if (!_midiExplicitRescan && _midiLearnState == 0 && _midiLearnChannel < 0 && facade.preferredMidiChannel >= 0)
            { _midiLearnChannel = facade.preferredMidiChannel; _midiLearnState = 2; }
            RefreshMidiMapping(true);
        }
        public void _CloseMidiChoices() { if (midiChoicePanel != null) midiChoicePanel.SetActive(false); }
        public void _MidiChannel1() { OpenMidiChannel(0); }
        public void _MidiChannel2() { OpenMidiChannel(1); }
        public void _MidiChannel3() { OpenMidiChannel(2); }
        public void _MidiChannel4() { OpenMidiChannel(3); }
        public void _MidiChannel5() { OpenMidiChannel(4); }
        public void _MidiChannel6() { OpenMidiChannel(5); }
        public void _MidiChannel7() { OpenMidiChannel(6); }
        public void _MidiChannel8() { OpenMidiChannel(7); }
        public void _MidiChannel9() { OpenMidiChannel(8); }
        public void _MidiChannel10() { OpenMidiChannel(9); }
        public void _MidiChannel11() { OpenMidiChannel(10); }
        public void _MidiChannel12() { OpenMidiChannel(11); }
        public void _MidiChannel13() { OpenMidiChannel(12); }
        public void _MidiChannel14() { OpenMidiChannel(13); }
        public void _MidiChannel15() { OpenMidiChannel(14); }
        public void _MidiChannel16() { OpenMidiChannel(15); }
        public void _MidiTarget0() { SelectMidiTarget(-1); }
        public void _MidiTarget1() { SelectMidiTarget(0); }
        public void _MidiTarget2() { SelectMidiTarget(1); }
        public void _MidiTarget3() { SelectMidiTarget(2); }
        public void _MidiTarget4() { SelectMidiTarget(3); }
        public void _MidiTarget5() { SelectMidiTarget(4); }
        public void _MidiTarget6() { SelectMidiTarget(5); }
        public void _MidiTarget7() { SelectMidiTarget(6); }
        private void OpenMidiChannel(int channel)
        {
            if (channel == 9 || midiMappingPanel == null || !midiMappingPanel.activeSelf || midiChoicePanel == null) return;
            _midiChannel = channel; midiChoicePanel.SetActive(true); RefreshMidiMapping(true);
        }
        private void SelectMidiTarget(int target)
        {
            if (fileMappingPanel != null && fileMappingPanel.activeInHierarchy)
            {
                if (fileChoicePanel == null || !fileChoicePanel.activeSelf || _fileChoiceSong != facade.selectedSongId) return;
                facade.CommandFileProgramTarget(_fileProgram, target);
                _CloseFileChoices(); RefreshFileMapping(true); return;
            }
            if (midiPerformancePanel != null && midiPerformancePanel.activeInHierarchy)
            {
                if (_midiLearnState == 0 || _midiLearnChannel < 0 || target < 0 || target > 6
                    || !facade.instrumentRouter.IsEnabled(target)) return;
                facade.SetHardwareMidiChannelTarget(_midiLearnChannel, target);
                facade.RememberMidiChannel(_midiLearnChannel); _midiLearnState = 2;
                RefreshMidiMapping(true); return;
            }
            if (facade == null || facade.instrumentRouter == null || midiChoicePanel == null || !midiChoicePanel.activeSelf
                || _midiChannel == 9 || (target >= 0 && !facade.instrumentRouter.IsEnabled(target))) return;
            facade.SetHardwareMidiChannelTarget(_midiChannel, target);
            _CloseMidiChoices(); RefreshMidiMapping(true);
        }
        private string MidiTargetName(int target)
        {
            return target < 0 ? Tr("midi.auto") : target == 0 ? Tr("midi.piano") : InstrumentName(target + 1);
        }
        private void RefreshMidiMapping(bool force)
        {
            RefreshFileMapping(force);
            if (force && midiPerformanceEntryLabel != null) midiPerformanceEntryLabel.text = Tr("midi.play_entry");
            RefreshMidiPerformance(force);
            if (midiMappingPanel == null || !midiMappingPanel.activeSelf || facade == null || facade.instrumentRouter == null) return;
            int[] map = facade.hardwareMidiChannelTargets;
            if (map == null || map.Length != 16) return;
            int signature = facade.InstrumentMask() * 31 + _loadedLanguage;
            for (int i = 0; i < 16; i++) signature = signature * 31 + map[i];
            if (!force && _midiRefreshed && signature == _midiSignature) return;
            _midiRefreshed = true; _midiSignature = signature;
            midiMappingTitle.text = Tr("midi.title"); midiMappingBack.text = Tr("midi.back");
            for (int i = 0; i < midiChannelButtons.Length; i++)
            {
                midiChannelNames[i].text = Tr("midi.channel").Replace("{channel}", (i + 1).ToString());
                string name = i == 9 ? Tr("midi.drums") : MidiTargetName(map[i]);
                if (i != 9 && map[i] >= 0 && !facade.instrumentRouter.IsEnabled(map[i]))
                    name = Tr("midi.unavailable").Replace("{instrument}", name);
                midiChannelValues[i].text = name; midiChannelButtons[i].interactable = i != 9;
            }
            if (midiChoicePanel == null || !midiChoicePanel.activeSelf) return;
            midiChoiceTitle.text = Tr("midi.channel").Replace("{channel}", (_midiChannel + 1).ToString());
            int count = 1;
            for (int t = 0; t < 7; t++) if (facade.instrumentRouter.IsEnabled(t)) count++;
            float height = 72 + count * 40;
            midiChoiceContent.sizeDelta = new Vector2(456, height);
            midiChoiceTitleRect.anchoredPosition = new Vector2(0, height * .5f - 26);
            int row = 0;
            for (int i = 0; i < midiTargetButtons.Length; i++)
            {
                bool enabled = i == 0 || facade.instrumentRouter.IsEnabled(i - 1);
                midiTargetButtons[i].gameObject.SetActive(enabled);
                midiTargetLabels[i].text = MidiTargetName(i - 1);
                midiTargetMarks[i].SetActive(map[_midiChannel] == i - 1);
                if (enabled) ((RectTransform)midiTargetButtons[i].transform).anchoredPosition = new Vector2(0, height * .5f - 72 - row++ * 40);
            }
        }
        private void RefreshMidiPerformance(bool force)
        {
            if (midiPerformancePanel == null || !midiPerformancePanel.activeSelf || facade == null || facade.instrumentRouter == null) return;
            int target = _midiLearnChannel < 0 ? -1 : facade.hardwareMidiChannelTargets[_midiLearnChannel];
            if (_midiLearnState == 2 && (target < 0 || !facade.instrumentRouter.IsEnabled(target))) _midiLearnState = 1;
            bool canPlay = facade.CanReceiveLiveMidi() && (facade.practice != null || facade.gate.CanControl());
            int signature = facade.InstrumentMask() * 4096 + (_loadedLanguage + 1) * 512 + (_midiLearnState + 1) * 64 + target + 1 + (canPlay ? 256 : 0);
            if (!force && _midiLearnRefreshed && signature == _midiLearnSignature) return;
            _midiLearnRefreshed = true; _midiLearnSignature = signature;
            midiPerformanceTitle.text = Tr("midi.play_title"); midiPerformanceBack.text = Tr("midi.back");
            midiPerformanceAdvanced.text = Tr("midi.advanced"); midiPerformanceRescan.text = Tr("midi.rescan");
            midiPerformanceRescanButton.interactable = _midiLearnChannel >= 0;
            string key = _midiLearnState == 0 ? "midi.press_key" : _midiLearnState == 1 ? "midi.choose_instrument" : canPlay ? "midi.ready" : "midi.configured";
            midiPerformancePrompt.text = Tr(key).Replace("{instrument}", MidiTargetName(target));
            midiPerformanceHint.text = Tr(_midiLearnDrum ? "midi.use_keyboard" : facade.InstrumentMask() == 0 ? "midi.enable_instrument"
                : _midiLearnState == 0 ? "midi.keyboard_hint" : _midiLearnState == 1 ? "midi.input_found" : canPlay ? "midi.change_anytime"
                : facade.clock.GetState() != 0 ? "midi.playback_wait" : "midi.control_wait");
            int row = 0;
            for (int i = 0; i < midiPerformanceTargets.Length; i++)
            {
                bool enabled = facade.instrumentRouter.IsEnabled(i) && _midiLearnState != 0;
                midiPerformanceTargets[i].gameObject.SetActive(enabled);
                if (enabled)
                {
                    ((RectTransform)midiPerformanceTargets[i].transform).anchoredPosition = new Vector2(row % 2 == 0 ? -225 : 225, 25 - row / 2 * 55);
                    row++;
                }
                midiPerformanceTargets[i].interactable = _midiLearnState != 0;
                midiPerformanceTargetLabels[i].text = MidiTargetName(i);
                midiPerformanceTargetMarks[i].SetActive(_midiLearnState == 2 && target == i);
            }
        }
        public TMP_Text titleLabel;
        public CanvasGroup opening;
        public TMP_Text openingTitle;
        public TMP_Text openingAuthor;
        public TMP_Text stateLabel;
        public TMP_Text timeLabel;
        public Button playButton;
        public Button pauseButton;
        public Button stopButton;
        public Slider progressSlider;
        public bool snapProgressToMeasures;
        public GameObject measureSnapSelectionMark;
        public GameObject peakProtectionSelectionMark;

        public void _TogglePeakProtection()
        {
            if (facade == null || facade.voicePool == null) return;
            facade.voicePool.SetPeakProtection(!facade.voicePool.peakProtectionEnabled);
            RefreshPeakProtectionSelection();
        }

        private void RefreshPeakProtectionSelection()
        {
            if (peakProtectionSelectionMark != null)
                peakProtectionSelectionMark.SetActive(facade != null && facade.voicePool != null && facade.voicePool.peakProtectionEnabled);
        }


        public void _ToggleMeasureSnap()
        {
            snapProgressToMeasures = !snapProgressToMeasures;
            if (measureSnapSelectionMark != null) measureSnapSelectionMark.SetActive(snapProgressToMeasures);
        }

        public Slider volumeSlider;
        public Toggle muteToggle;
        public MidiSongButton[] songButtons = new MidiSongButton[0];
        public ScrollRect libraryScroll;
        public Button libraryBackButton;
        public TMP_Text libraryPathLabel;
        public TMP_Text libraryCountLabel;
        public TMP_Text libraryEmptyLabel;
        public RectTransform allSongsRow;
        public MidiLibraryFolderButton[] folderButtons = new MidiLibraryFolderButton[0];
        public float libraryRowHeight = 60f;
        public float libraryRowSpacing = 68f;
        public TMP_InputField librarySearchInput;
        public TMP_Text librarySearchPlaceholder;
        public Button librarySearchClearButton;
        public string[] songSearchKeys = new string[0];
        private string _searchQuery = "";
        private int _searchResultCount;
        private float _beforeSearchScroll;
        // English, Simplified Chinese, Traditional Chinese and Japanese, in that order.
        public TextAsset[] languageFiles = new TextAsset[0];
        public TMP_Text[] localizedLabels = new TMP_Text[0];
        public string[] localizedKeys = new string[0];
        // Zero detects the initial language; visible choices are English, 中文 and 日本語.
        // Chinese follows the client's script. The choice remains local to this player.
        public int languageSelection;
        public Button languageButton;
        public GameObject languagePanel;
        public GameObject settingsPanel;
        public GameObject worldAudioSelectionMark;
        public GameObject twoDAudioSelectionMark;
        public float stickScrollSensitivity = 2f;
        public Slider stickScrollSensitivitySlider;
        public TMP_Text stickScrollSensitivityValueLabel;
        public GameObject currentLandingSelectionMark;
        public GameObject keyRootLandingSelectionMark;
        public Button[] languageOptionButtons = new Button[0];
        public GameObject[] languageSelectionMarks = new GameObject[0];
        public TMP_Text[] folderSummaryLabels = new TMP_Text[0];
        public int[] folderChildCounts = new int[0];
        public TMP_Text allSongsTitleLabel;
        public TMP_Text allSongsSummaryLabel;
        private DataDictionary _englishLanguage;
        private DataDictionary _currentLanguage;
        private int _loadedLanguage = -1;
        private string _clientLanguage;
        private int _openFolder = -1;
        private float _nextTextUpdate;
        private float _volume = 0.65f;
        private bool _muted;
        private bool _progressPreview;
        private bool _progressDragging;
        private bool _progressCommitQueued;
        private string _progressSongId;
        private int _libraryHoverCount;
        private float _rightScroll;
        private float _mouseScrollSensitivity;

        private void Start()
        {
            // Movement axes must never navigate controls or feed the native ScrollRect.
            Selectable[] controls = GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < controls.Length; i++)
            {
                Navigation navigation = controls[i].navigation;
                navigation.mode = Navigation.Mode.None;
                controls[i].navigation = navigation;
            }
            if (libraryScroll != null)
            {
                _mouseScrollSensitivity = libraryScroll.scrollSensitivity;
                libraryScroll.scrollSensitivity = 0f;
            }
            _CloseLanguagePanel();
            _CloseSettingsPanel();
            if (measureSnapSelectionMark != null) measureSnapSelectionMark.SetActive(snapProgressToMeasures);
            RefreshNoteLandingSelection();
            RefreshAudioSourceSelection();
            RefreshPeakProtectionSelection();
            stickScrollSensitivity = Mathf.Pow(2f, GetStickSensitivityStep() - 1);
            if (stickScrollSensitivitySlider != null)
                stickScrollSensitivitySlider.SetValueWithoutNotify(GetStickSensitivityStep());
            RefreshStickScrollSensitivityValue();
            InitializeVolume();
            ApplyLanguage();
            OpenLibraryFolder(folderButtons.Length > 0 ? -1 : -2);
        }

        public void _OnLibraryPointerEnter() { _libraryHoverCount++; }
        public void _OnLibraryPointerExit() { _libraryHoverCount = Mathf.Max(0, _libraryHoverCount - 1); }

        public override void InputLookVertical(float value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.RIGHT) _rightScroll = value;
        }

        private void Update()
        {
            if (_libraryHoverCount == 0 || libraryScroll == null) return;
            bool inVR = VRC.SDKBase.Networking.LocalPlayer != null && VRC.SDKBase.Networking.LocalPlayer.IsUserInVR();
            // Map the stick travel outside the dead zone linearly to the full speed range.
            float stickSpeed = Mathf.Sign(_rightScroll) * Mathf.Clamp01((Mathf.Abs(_rightScroll) - 0.15f) / 0.85f);
            float delta = inVR
                ? stickSpeed * 125f * stickScrollSensitivity * Time.deltaTime
                : Input.GetAxisRaw("Mouse ScrollWheel") * 10f * _mouseScrollSensitivity;
            if (delta == 0f) return;
            float height = libraryScroll.content.rect.height - libraryScroll.viewport.rect.height;
            if (height <= 0f) return;
            libraryScroll.StopMovement();
            libraryScroll.verticalNormalizedPosition = Mathf.Clamp01(
                libraryScroll.verticalNormalizedPosition + delta / height);
        }

        public string Tr(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_englishLanguage == null) LoadLanguage();
            DataToken value;
            if (_currentLanguage != null && _currentLanguage.TryGetValue(key, TokenType.String, out value)) return value.String;
            if (_englishLanguage != null && _englishLanguage.TryGetValue(key, TokenType.String, out value)) return value.String;
            return key;
        }

        public void _ToggleLanguagePanel()
        {
            _CloseInstrumentPanel();
            _CloseSettingsPanel();
            if (languagePanel != null) languagePanel.SetActive(!languagePanel.activeSelf);
        }

        public void _ToggleSettingsPanel()
        {
            _CloseMidiMapping();
            _CloseInstrumentPanel();
            _CloseLanguagePanel();
            if (settingsPanel != null) settingsPanel.SetActive(!settingsPanel.activeSelf);
        }

        public void _CloseSettingsPanel()
        {
            _CloseMidiMapping();
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void _OnStickScrollSensitivityChanged()
        {
            if (stickScrollSensitivitySlider == null) return;
            stickScrollSensitivity = Mathf.Pow(2f, Mathf.RoundToInt(stickScrollSensitivitySlider.value) - 1);
            RefreshStickScrollSensitivityValue();
        }

        public int GetStickSensitivityStep()
        {
            int nearest = 0;
            float distance = Mathf.Abs(stickScrollSensitivity - 0.5f);
            for (int i = 1; i < 6; i++)
            {
                float next = Mathf.Abs(stickScrollSensitivity - Mathf.Pow(2f, i - 1));
                if (next < distance) { nearest = i; distance = next; }
            }
            return nearest;
        }

        private void InitializeVolume()
        {
            if (facade != null && facade.voicePool != null) _volume = facade.voicePool.localVolume;
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(_volume);
            if (facade != null && facade.voicePool != null) facade.voicePool.SetLocalOutput(_volume, _muted);
        }

        private void RefreshStickScrollSensitivityValue()
        {
            if (stickScrollSensitivityValueLabel != null)
                stickScrollSensitivityValueLabel.text = stickScrollSensitivity.ToString("0.##") + "×";
        }

        public void _UseWorldAudio() { SetAudioSource(true); }
        public void _Use2DAudio() { SetAudioSource(false); }

        private void SetAudioSource(bool worldSpace)
        {
            if (facade == null || facade.voicePool == null) return;
            facade.voicePool.SetWorldSpaceAudio(worldSpace);
            RefreshAudioSourceSelection();
        }

        private void RefreshAudioSourceSelection()
        {
            bool worldSpace = facade != null && facade.voicePool != null && facade.voicePool.worldSpaceAudio;
            if (worldAudioSelectionMark != null) worldAudioSelectionMark.SetActive(worldSpace);
            if (twoDAudioSelectionMark != null) twoDAudioSelectionMark.SetActive(!worldSpace);
        }

        public void _UseCurrentNoteLanding() { SetNoteLanding(false); }
        public void _UseKeyRootNoteLanding() { SetNoteLanding(true); }

        private void SetNoteLanding(bool atRoot)
        {
            if (facade == null || facade.waterfall == null) return;
            facade.waterfall.SetLandingAtKeyRoot(atRoot);
            RefreshNoteLandingSelection();
        }

        private void RefreshNoteLandingSelection()
        {
            bool atRoot = facade != null && facade.waterfall != null && facade.waterfall.landAtKeyRoot;
            if (currentLandingSelectionMark != null) currentLandingSelectionMark.SetActive(!atRoot);
            if (keyRootLandingSelectionMark != null) keyRootLandingSelectionMark.SetActive(atRoot);
        }

        public void _CloseLanguagePanel()
        {
            if (languagePanel != null) languagePanel.SetActive(false);
        }

        public void _SelectLanguageEnglish() { SetLanguageSelection(1); }
        public void _SelectLanguageChinese() { SetLanguageSelection(2); }
        public void _SelectLanguageJapanese() { SetLanguageSelection(3); }

        private void SetLanguageSelection(int index)
        {
            languageSelection = index;
            ApplyLanguage();
            _CloseLanguagePanel();
        }

        public override void OnLanguageChanged(string language)
        {
            _clientLanguage = language;
            if (languageSelection == 0 || languageSelection == 2) ApplyLanguage();
        }

        public void ApplyLanguage()
        {
            LoadLanguage();
            RefreshMidiMapping(true);
            if(drumVisibilityLabel!=null)drumVisibilityLabel.text=Tr("settings.show_drums");
            for (int i = 0; i < localizedLabels.Length && i < localizedKeys.Length; i++)
                if (localizedLabels[i] != null) localizedLabels[i].text = Tr(localizedKeys[i]);
            if (librarySearchPlaceholder != null) librarySearchPlaceholder.text = Tr("library.search_placeholder");
            if (allSongsTitleLabel != null) allSongsTitleLabel.text = Tr("library.all_songs");
            if (allSongsSummaryLabel != null) allSongsSummaryLabel.text = SongCount(songButtons.Length);
            for (int i = 0; i < folderSummaryLabels.Length && i < folderButtons.Length; i++)
            {
                if (folderSummaryLabels[i] == null || folderButtons[i] == null) continue;
                int count = folderButtons[i].songIndices == null ? 0 : folderButtons[i].songIndices.Length;
                string summary = SongCount(count);
                if (i < folderChildCounts.Length && folderChildCounts[i] > 0)
                    summary += " · " + Tr("library.child_count").Replace("{count}", folderChildCounts[i].ToString());
                folderSummaryLabels[i].text = summary;
            }
            int selectedOption = languageSelection == 0
                ? (_loadedLanguage == 0 ? 0 : _loadedLanguage == 3 ? 2 : 1)
                : languageSelection - 1;
            for (int i = 0; i < languageSelectionMarks.Length; i++)
                if (languageSelectionMarks[i] != null) languageSelectionMarks[i].SetActive(i == selectedOption);
            // Text-only refresh preserves each player's folder, scrolling and playback state.
            RefreshLibraryText();
            _nextTextUpdate = 0;
            if (IsRuntime()) RefreshState();
        }

        private bool IsRuntime()
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            return Application.isPlaying;
#else
            return true;
#endif
        }

        private void LoadLanguage()
        {
            int selection = languageSelection;
            int language = selection == 1 ? 0 : selection == 2 ? (DetectLanguage() == 2 ? 2 : 1)
                : selection == 3 ? 3 : DetectLanguage();
            // Edit-mode previews also pick up edits to the external dictionaries.
            if (_englishLanguage != null && _loadedLanguage == language && IsRuntime()) return;
            _englishLanguage = ReadLanguageFile(0);
            _currentLanguage = language == 0 ? _englishLanguage : ReadLanguageFile(language);
            _loadedLanguage = language;
        }

        private int DetectLanguage()
        {
            if (!IsRuntime()) return 0;
            if (string.IsNullOrEmpty(_clientLanguage)) _clientLanguage = VRCPlayerApi.GetCurrentLanguage();
            if (string.IsNullOrEmpty(_clientLanguage)) return 0;
            string language = _clientLanguage.ToLowerInvariant().Replace('_', '-');
            if (language == "zh" || language == "zh-cn" || language == "zh-sg" || language.StartsWith("zh-hans")) return 1;
            if (language == "zh-tw" || language == "zh-hk" || language == "zh-mo" || language.StartsWith("zh-hant")) return 2;
            if (language == "ja" || language.StartsWith("ja-")) return 3;
            return 0;
        }

        private DataDictionary ReadLanguageFile(int index)
        {
            if (languageFiles == null || index >= languageFiles.Length || languageFiles[index] == null) return null;
            DataToken parsed;
            if (!VRCJson.TryDeserializeFromJson(languageFiles[index].text, out parsed) || parsed.TokenType != TokenType.DataDictionary) return null;
            return parsed.DataDictionary;
        }

        private string SongCount(int count)
        {
            return Tr("library.song_count").Replace("{count}", count.ToString());
        }

        private void RefreshLibraryText()
        {
            bool searching = _searchQuery.Length > 0;
            if (libraryEmptyLabel != null) libraryEmptyLabel.text = Tr(searching ? "library.no_results" : "library.empty");
            bool hasFolder = _openFolder >= 0 && _openFolder < folderButtons.Length && folderButtons[_openFolder] != null;
            if (libraryPathLabel != null)
                libraryPathLabel.text = searching ? Tr("library.search_results") : hasFolder ? folderButtons[_openFolder].folderPath.Replace("/", " / ")
                    : Tr(_openFolder == -2 ? "library.all_songs" : "library.root");
            int count = songButtons.Length;
            if (hasFolder)
            {
                count = 0;
                int[] songs = folderButtons[_openFolder].songIndices;
                if (songs != null)
                    for (int i = 0; i < songs.Length; i++)
                        if (songs[i] >= 0 && songs[i] < songButtons.Length && songButtons[songs[i]] != null) count++;
            }
            if (libraryCountLabel != null)
                libraryCountLabel.text = Tr("library.count").Replace("{count}", (searching ? _searchResultCount : count).ToString("00"));
        }

        public void _OnLibrarySearchChanged()
        {
            string query = librarySearchInput == null ? "" : librarySearchInput.text.Trim().ToLowerInvariant().Replace('_', ' ');
            if (librarySearchClearButton != null) librarySearchClearButton.gameObject.SetActive(librarySearchInput != null && librarySearchInput.text.Length > 0);
            if (query == _searchQuery) return;
            if (_searchQuery.Length == 0 && libraryScroll != null) _beforeSearchScroll = libraryScroll.content.anchoredPosition.y;
            _searchQuery = query;
            OpenLibraryFolder(_openFolder);
            if (query.Length == 0 && libraryScroll != null)
                libraryScroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(_beforeSearchScroll, 0, Mathf.Max(0, libraryScroll.content.rect.height - libraryScroll.viewport.rect.height)));
        }

        public void _ClearLibrarySearch()
        {
            if (librarySearchInput != null) librarySearchInput.SetTextWithoutNotify("");
            _OnLibrarySearchChanged();
        }

        public void _OnAllSongsPressed() { OpenLibraryFolder(-2); }

        public void _OnLibraryBackPressed()
        {
            if (_searchQuery.Length > 0) { _ClearLibrarySearch(); return; }
            if (_openFolder == -1) return;
            OpenLibraryFolder(_openFolder < 0 ? -1 : folderButtons[_openFolder].parentFolderIndex);
        }

        // -1 is the collection root; -2 is the complete catalog. Browsing is local.
        public void OpenLibraryFolder(int index)
        {
            if (libraryScroll == null || libraryScroll.content == null || libraryScroll.viewport == null) return;
            if (index < -2 || index >= folderButtons.Length) return;
            _openFolder = index;
            libraryScroll.StopMovement();
            bool searching = _searchQuery.Length > 0;
            int row = 0;
            if (allSongsRow != null)
            {
                allSongsRow.gameObject.SetActive(index == -1 && !searching);
                if (index == -1 && !searching) PlaceLibraryRow(allSongsRow, row++);
            }
            for (int i = 0; i < folderButtons.Length; i++)
            {
                MidiLibraryFolderButton folder = folderButtons[i];
                if (folder == null) continue;
                bool visible = !searching && index != -2 && folder.parentFolderIndex == index;
                folder.gameObject.SetActive(visible);
                if (visible)
                {
                    PlaceLibraryRow((RectTransform)folder.transform, row++);
                }
            }
            for (int i = 0; i < songButtons.Length; i++)
                if (songButtons[i] != null) songButtons[i].gameObject.SetActive(false);
            int songCount = 0;
            if (index == -2 || searching)
            {
                string[] terms = _searchQuery.Split(' ');
                for (int i = 0; i < songButtons.Length; i++)
                {
                    if (songButtons[i] == null) continue;
                    if (searching)
                    {
                        string key = i < songSearchKeys.Length ? songSearchKeys[i] : "";
                        bool matches = true;
                        for (int t = 0; t < terms.Length; t++)
                            if (terms[t].Length > 0 && !key.Contains(terms[t])) { matches = false; break; }
                        if (!matches) continue;
                    }
                    songButtons[i].gameObject.SetActive(true);
                    PlaceLibraryRow((RectTransform)songButtons[i].transform, row++);
                    if (songButtons[i].numberLabel != null) songButtons[i].numberLabel.text = (songCount + 1).ToString("00");
                    songCount++;
                }
            }
            else if (index >= 0 && folderButtons[index] != null)
            {
                int[] songs = folderButtons[index].songIndices;
                for (int i = 0; i < songs.Length; i++)
                {
                    int song = songs[i];
                    if (song < 0 || song >= songButtons.Length || songButtons[song] == null) continue;
                    songButtons[song].gameObject.SetActive(true);
                    PlaceLibraryRow((RectTransform)songButtons[song].transform, row++);
                    if (songButtons[song].numberLabel != null) songButtons[song].numberLabel.text = (songCount + 1).ToString("00");
                    songCount++;
                }
            }
            _searchResultCount = songCount;
            if (librarySearchClearButton != null) librarySearchClearButton.gameObject.SetActive(librarySearchInput != null && librarySearchInput.text.Length > 0);
            if (libraryBackButton != null) libraryBackButton.interactable = searching || index != -1;
            RefreshLibraryText();
            if (libraryEmptyLabel != null) libraryEmptyLabel.gameObject.SetActive(row == 0);
            float height = Mathf.Max(0f, row * libraryRowSpacing - (libraryRowSpacing - libraryRowHeight));
            RectTransform content = libraryScroll.content;
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(libraryScroll.viewport.rect.height, height));
            content.anchoredPosition = Vector2.zero;
            libraryScroll.verticalNormalizedPosition = 1f;
            if (libraryScroll.verticalScrollbar != null)
                libraryScroll.verticalScrollbar.gameObject.SetActive(height > libraryScroll.viewport.rect.height);
        }

        private void PlaceLibraryRow(RectTransform row, int index)
        {
            row.anchoredPosition = new Vector2(row.anchoredPosition.x, -libraryRowHeight * 0.5f - index * libraryRowSpacing);
        }

        public void _OnPlayPressed() { if (facade != null) facade.CommandPlay(); }
        public void _OnPausePressed() { if (facade != null) facade.CommandPause(); }
        public void _OnStopPressed() { if (facade != null) facade.CommandStop(); }
        public void _OnProgressPointerDown()
        {
            if (facade == null || progressSlider == null || !progressSlider.interactable) return;
            _progressDragging = true;
            _progressPreview = true;
            _progressSongId = facade.selectedSongId;
            _nextTextUpdate = 0;
        }

        public void _OnProgressChanged()
        {
            if (facade == null || progressSlider == null || !progressSlider.interactable) return;
            if (snapProgressToMeasures)
                progressSlider.SetValueWithoutNotify(facade.SnapProgressToMeasure(progressSlider.normalizedValue));
            if (!_progressPreview) _progressSongId = facade.selectedSongId;
            _progressPreview = true;
            _nextTextUpdate = 0;
            // PointerDown can run after Slider's value callback. Wait one frame so
            // pointer gestures commit on release and keyboard changes still seek.
            if (!_progressDragging && !_progressCommitQueued)
            {
                _progressCommitQueued = true;
                SendCustomEventDelayedFrames("_CommitProgressChange", 1);
            }
        }

        public void _OnProgressPointerUp()
        {
            if (!_progressDragging) return;
            _progressDragging = false;
            _CommitProgressChange();
        }

        public void _CommitProgressChange()
        {
            _progressCommitQueued = false;
            if (_progressDragging || !_progressPreview) return;
            _progressPreview = false;
            _nextTextUpdate = 0;
            if (facade != null && progressSlider != null && progressSlider.interactable
                && _progressSongId == facade.selectedSongId)
                facade.CommandSeek(snapProgressToMeasures ? facade.SnapProgressToMeasure(progressSlider.normalizedValue) : progressSlider.normalizedValue);
        }

        private void OnDisable()
        {
            _CloseInstrumentPanel();
            _libraryHoverCount = 0;
            _rightScroll = 0f;
            _CloseLanguagePanel();
            _progressDragging = false;
            _progressPreview = false;
            _progressCommitQueued = false;
        }

        public void _OnVolumeChanged()
        {
            if (volumeSlider != null) _volume = volumeSlider.value;
            if (facade != null && facade.voicePool != null) facade.voicePool.SetLocalOutput(_volume, _muted);
        }
        public void _OnMuteChanged()
        {
            if (muteToggle != null) _muted = muteToggle.isOn;
            if (facade != null && facade.voicePool != null) facade.voicePool.SetLocalOutput(_volume, _muted);
        }

        public void RefreshState()
        {
            if (facade == null) return;
            RefreshMidiMapping(false);
            int state = facade.GetState();
            RefreshOpening(state);
            bool can = facade.isReady && (facade.practice != null || (facade.gate != null && facade.gate.CanControl() && facade.network != null && facade.network.CanAcceptCommands()));
            if (fileMappingEntry != null) fileMappingEntry.interactable = facade.isReady && !facade.isSongLoading;
            if (instrumentButton != null) instrumentButton.interactable = can && !facade.isSongLoading;
            if (instrumentTitle != null) instrumentTitle.text = Tr("instrument.title");
            if (instrumentEntryLabel != null)
            {
                string summary = "";
                if (facade.instrumentRouter != null)
                {
                    int count = 0;
                    for (int i = 0; i < instrumentOptionButtons.Length; i++) if (facade.IsInstrumentChecked(i)) { count++; summary = InstrumentName(i); }
                    if (count == 0) summary = Tr("instrument.none");
                    else if (count > 1) summary = Tr("instrument.selected_count").Replace("{count}", count.ToString());
                }
                else summary = instrumentDropdownPanel == null ? Tr("instrument.title") : InstrumentName(facade.selectedInstrument);
                instrumentEntryLabel.text = summary;
            }
            for (int i = 0; i < instrumentOptionButtons.Length; i++)
            {
                if (i == 7 && facade.instrumentRouter != null) instrumentOptionButtons[i].gameObject.SetActive(false);
                if (instrumentOptionButtons[i] != null) instrumentOptionButtons[i].interactable = can && !facade.isSongLoading;
                if (i < instrumentSelectionMarks.Length && instrumentSelectionMarks[i] != null)
                    instrumentSelectionMarks[i].SetActive(facade.IsInstrumentChecked(i));
                if (i < instrumentToggles.Length && instrumentToggles[i] != null)
                {
                    instrumentToggles[i].interactable = can && !facade.isSongLoading;
                    instrumentToggles[i].SetIsOnWithoutNotify(facade.IsInstrumentChecked(i));
                }
                if (i < instrumentLabels.Length && instrumentLabels[i] != null)
                    instrumentLabels[i].text = InstrumentName(i);
            }
            if (stringsStylePanel != null) stringsStylePanel.SetActive(facade.HasSelectedStrings());
            if (facade.instrumentRouter != null && !facade.instrumentRouter.IsEnabled(_stringStyleTarget)) CloseStringsStyleDropdown();
            UpdateInstrumentDropdownLayout();
            if (stringsStyleDropdownButton != null) stringsStyleDropdownButton.interactable = can && !facade.isSongLoading;
            if (stringsStyleEntryLabel != null && facade.voicePool.strings != null)
                stringsStyleEntryLabel.text = Tr("strings.style") + ": " + Tr(facade.voicePool.strings.articulation == 0 ? "strings.sustain" : facade.voicePool.strings.articulation == 1 ? "strings.spiccato" : "strings.pizzicato");
            for (int i = 0; i < stringStyleEntries.Length; i++)
            {
                stringStyleEntries[i].interactable = can && !facade.isSongLoading && facade.IsInstrumentChecked(i + 3);
                int choice = facade.instrumentRouter == null ? 0 : facade.instrumentRouter.StringArticulationChoice(i + 2);
                stringStyleEntryLabels[i].text = Tr(choice == 0 ? "midi.auto" : choice == 1 ? "strings.sustain" : choice == 2 ? "strings.spiccato" : "strings.pizzicato");
            }
            for (int i = 0; i < stringsStyleButtons.Length; i++)
            {
                stringsStyleButtons[i].interactable = can && !facade.isSongLoading;
                stringsStyleLabels[i].text = Tr(i == 3 ? "midi.auto" : i == 0 ? "strings.sustain" : i == 1 ? "strings.spiccato" : "strings.pizzicato");
                int choice = facade.instrumentRouter == null ? -1 : facade.instrumentRouter.StringArticulationChoice(_stringStyleTarget);
                stringsStyleMarks[i].SetActive(facade.instrumentRouter == null ? facade.voicePool.strings != null && facade.voicePool.strings.articulation == i : choice == (i == 3 ? 0 : i + 1));
            }
            if (playButton != null) playButton.interactable = can && !facade.isSongLoading && (facade.practice != null ? !facade.practice.running : state != 1);
            if (pauseButton != null) pauseButton.interactable = can && (facade.practice != null ? facade.practice.running : state == 1);
            if (stopButton != null) stopButton.interactable = can;
            if (_progressPreview && (!can || _progressSongId != facade.selectedSongId))
            {
                _progressDragging = false;
                _progressPreview = false;
            }
            for (int i = 0; i < songButtons.Length; i++) if (songButtons[i] != null) songButtons[i].Refresh(can, facade.selectedSongId);
            if (progressSlider != null && facade.clock != null)
            {
                double duration = facade.clock.GetPlaybackEndSeconds();
                progressSlider.interactable = can && duration > 0;
                if (!_progressPreview)
                    progressSlider.SetValueWithoutNotify(duration > 0 ? (float)(facade.clock.GetPositionSeconds() / duration) : 0);
            }
            if (Time.unscaledTime < _nextTextUpdate) return;
            _nextTextUpdate = Time.unscaledTime + 0.2f;
            bool live = facade.network != null && facade.network.IsLiveMidiActive();
            for (int i = 0; i < localizedKeys.Length && i < localizedLabels.Length; i++)
                if (localizedKeys[i] == "player.now_playing" && localizedLabels[i] != null)
                    localizedLabels[i].text = Tr(live ? "player.now_performing" : "player.now_playing");
            if (titleLabel != null)
            {
                string author = facade.GetSongAuthor();
                titleLabel.text = live ? facade.network.GetLiveMidiPlayerName()
                    : facade.library != null && facade.library.FindSong(facade.selectedSongId) != null
                        ? facade.GetSongTitle() + (string.IsNullOrWhiteSpace(author) ? "" : " · " + author)
                        : Tr("player.no_songs");
            }
            if (stateLabel != null)
            {
                string status = Tr(live ? "status.performing" : state == 1 ? "status.playing" : state == 2 ? "status.paused" : "status.stopped");
                if (!string.IsNullOrEmpty(facade.lastError)) status = LocalizeRuntimeError(facade.lastError);
                else if (facade.isSongLoading) status = Tr("status.loading");
                else if (!facade.isReady || facade.network == null || !facade.network.CanAcceptCommands()) status = Tr("status.preparing");
                else if (facade.gate == null || !facade.gate.CanControl()) status += " / " + Tr("status.locked");
                stateLabel.text = status;
            }
            if (timeLabel != null && facade.clock != null)
            {
                double duration = facade.clock.GetPlaybackEndSeconds();
                double position = _progressPreview && progressSlider != null ? progressSlider.normalizedValue * duration : facade.clock.GetPositionSeconds();
                timeLabel.text = FormatTime(position) + " / " + FormatTime(duration);
            }
        }

        private string LocalizeRuntimeError(string message)
        {
            // Existing diagnostics remain authoritative; only their displayed text is translated.
            const string loadPrefix = "曲目加载失败：";
            if (message.StartsWith(loadPrefix))
                return Tr("error.song_load").Replace("{error}", TranslateErrorDetail(message.Substring(loadPrefix.Length)));
            return TranslateErrorDetail(message);
        }

        private string TranslateErrorDetail(string message)
        {
            string key;
            switch (message)
            {
                case "播放器配置不完整": key = "error.configuration_incomplete"; break;
                case "暂无可播放曲目或有效音源": key = "error.no_songs_or_bank"; break;
                case "播放器配置不一致，请联系世界作者": key = "error.configuration_mismatch"; break;
                case "播放器同步数据无效": key = "error.sync_data"; break;
                case "播放器状态数据无效": key = "error.state_data"; break;
                case "播放器曲目不存在": key = "error.song_missing"; break;
                case "播放器进度数据无效": key = "error.position_data"; break;
                case "曲目数据无效，请重新导入并配置曲库": key = "error.song_data"; break;
                case "曲目解码数据无效": key = "error.decoded_data"; break;
                case "曲目描述无效": key = "error.song_descriptor"; break;
                case "曲目压缩数据缺失或版本不受支持": key = "error.packed_version"; break;
                case "曲目压缩数据长度无效": key = "error.packed_length"; break;
                case "曲目压缩格式或必需功能不受支持": key = "error.packed_format"; break;
                case "曲目压缩头与描述不一致": key = "error.packed_header"; break;
                case "曲目检查点数量超出数据长度": key = "error.checkpoint_count"; break;
                case "曲目音符时间或音高力度无效": key = "error.note_data"; break;
                case "曲目MIDI通道无效": key = "error.midi_channel"; break;
                case "曲目事件索引溢出": key = "error.event_overflow"; break;
                case "曲目事件索引或因果次序无效": key = "error.event_order"; break;
                case "曲目事件时间次序无效": key = "error.event_time"; break;
                case "曲目检查点事件索引无效": key = "error.checkpoint_event"; break;
                case "曲目检查点活动偏移无效": key = "error.checkpoint_offset"; break;
                case "曲目检查点音符索引无效": key = "error.checkpoint_note"; break;
                case "曲目踏板数量超出数据长度": key = "error.pedal_count"; break;
                case "曲目踏板时间或数值无效": key = "error.pedal_data"; break;
                case "曲目扩展段数量无效": key = "error.extension_count"; break;
                case "曲目必需扩展不受支持或扩展长度无效": key = "error.extension_data"; break;
                case "曲目表达扩展无效": key = "error.expression_data"; break;
                case "曲目压缩数据存在未识别尾部": key = "error.packed_trailing"; break;
                case "曲目压缩数据校验失败": key = "error.packed_checksum"; break;
                case "曲目压缩数据意外结束": key = "error.packed_end"; break;
                case "曲目变长整数溢出": key = "error.varint_overflow"; break;
                case "曲目变长整数编码无效": key = "error.varint_encoding"; break;
                case "曲目变长整数超长": key = "error.varint_length"; break;
                case "曲目整数超出支持范围": key = "error.integer_range"; break;
                default: return message;
            }
            string translated = Tr(key);
            return translated == key ? message : translated;
        }

        private string InstrumentName(int index)
        {
            return Tr(index == 0 ? "instrument.digital" : index == 1 ? "instrument.grand" : index == 2 ? "instrument.marimba"
                : index == 3 ? "instrument.violin" : index == 4 ? "instrument.viola" : index == 5 ? "instrument.cello"
                : index == 6 ? "instrument.contrabass" : "instrument.strings");
        }

        private void UpdateInstrumentDropdownLayout()
        {
            if (instrumentDropdownPanel == null || instrumentDropdownViewport == null) return;
            bool style = facade.HasSelectedStrings();
            bool inline = facade.instrumentRouter != null && stringStyleEntries.Length == 4;
            float styleHeight = style && !inline ? 56 : 0;
            int count = facade.instrumentRouter != null && instrumentOptionButtons.Length == 8 ? 7 : instrumentOptionButtons.Length;
            float listHeight = Mathf.Min(320, count * 40, 400 - 16 - styleHeight);
            float height = listHeight + 16 + styleHeight;
            if (inline)
            {
                var rect = (RectTransform)stringsStylePanel.transform;
                rect.sizeDelta = new Vector2(264, listHeight);
                rect.anchoredPosition = new Vector2(-132, 0);
                if (stringsStyleOptions != null && stringsStyleOptions.activeSelf) PositionStringStyleOptions();
            }
            if (!Mathf.Approximately(instrumentDropdownPanel.sizeDelta.y, height))
            {
                instrumentDropdownPanel.sizeDelta = new Vector2(280, height);
                instrumentDropdownPanel.anchoredPosition = new Vector2(138, 180 - height * .5f);
                instrumentDropdownViewport.sizeDelta = new Vector2(264, listHeight);
                instrumentDropdownViewport.anchoredPosition = new Vector2(-132, height * .5f - 8 - listHeight * .5f);
                if (stringsStylePanel != null && stringStyleEntries.Length != 4)
                    ((RectTransform)stringsStylePanel.transform).anchoredPosition = new Vector2(-132, -height * .5f + 30);
            }
        }

        private string FormatTime(double seconds)
        {
            int whole = (int)System.Math.Max(0, seconds);
            int minutes = whole / 60;
            int remain = whole % 60;
            return minutes.ToString("00") + ":" + remain.ToString("00");
        }

        private void RefreshOpening(int state)
        {
            if (opening == null) return;
            var stringsView = facade.GetStringsView(facade.selectedInstrument);
            if (stringsView != null && opening.transform.parent != stringsView.transform)
            {
                opening.transform.SetParent(stringsView.transform, false);
                opening.transform.localPosition = new Vector3(0, 1.6f, -.1f);
                opening.transform.localRotation = Quaternion.identity;
                opening.transform.localScale = Vector3.one * .002f;
            }
            // The shared opening must follow the active instrument, whose siblings are disabled.
            if (facade.waterfall != null && opening.transform.parent != facade.waterfall.transform)
            {
                Transform openingTransform = opening.transform;
                openingTransform.SetParent(facade.waterfall.transform, false);
                openingTransform.localPosition = new Vector3(0, 0.5f, -0.06f);
                openingTransform.localRotation = Quaternion.identity;
                float width = 0;
                if (facade.waterfall.keyX != null)
                    foreach (float x in facade.waterfall.keyX) width = Mathf.Max(width, Mathf.Abs(x) * 2);
                openingTransform.localScale = Vector3.one * Mathf.Max(0.001f, width / 1100f);
            }
            double position = facade.clock == null ? 0 : facade.clock.GetPositionSeconds();
            bool visible = state != 0 && position < 0 && !facade.isSongLoading && string.IsNullOrEmpty(facade.lastError);
            if (opening.gameObject.activeSelf != visible) opening.gameObject.SetActive(visible);
            if (!visible) return;
            float elapsed = (float)(position + 3);
            opening.alpha = Mathf.Min(Mathf.Clamp01(elapsed / 0.35f), Mathf.Clamp01((float)-position / 0.55f));
            if (openingTitle != null) openingTitle.text = facade.GetSongTitle();
            if (openingAuthor != null)
            {
                string author = facade.GetSongAuthor();
                openingAuthor.text = author;
                openingAuthor.gameObject.SetActive(!string.IsNullOrWhiteSpace(author));
            }
        }
    }
}
