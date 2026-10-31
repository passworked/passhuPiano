using UdonSharp;
using UnityEngine;

namespace MusicMidiPlayer.UI
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Music MIDI Player/UI/Library Folder")]
    public class MidiLibraryFolderButton : UdonSharpBehaviour
    {
        public MidiPlayerUI playerUI;
        public int folderIndex;
        public int parentFolderIndex = -1;
        public string folderPath;
        public int[] songIndices = new int[0];

        public void _OnClick()
        {
            if (playerUI != null) playerUI.OpenLibraryFolder(folderIndex);
        }
    }
}
