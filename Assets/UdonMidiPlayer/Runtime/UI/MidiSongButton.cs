using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.UI
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/UI/Song Button")]
    public class MidiSongButton : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public string songId;
        public Button button;
        public Image selectionMark;
        public TMP_Text numberLabel;
        public void _OnClick() { if (facade != null) facade.CommandSelectSong(songId); }
        public void Refresh(bool canControl, string selected)
        {
            if (button != null) button.interactable = canControl;
            if (selectionMark != null) selectionMark.enabled = songId == selected;
        }
    }
}
