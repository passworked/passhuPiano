using UdonSharp;
using UnityEngine;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Views
{
    // Opt-in scene presentation. The Editor bakes every selection; no network state or Update loop.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiInstrumentFanLayout : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        // Legacy views 0..8 (7 retired), then drums at 9. All share the same parent frame.
        public Transform[] roots = new Transform[0];
        public Vector3[] positions = new Vector3[0];
        public float[] yawAngles = new float[0];
        public Vector3[] footprintMinimum = new Vector3[0];
        public Vector3[] footprintMaximum = new Vector3[0];
        public Vector3 center = new Vector3(0, 0, .5f);
        public float edgeGap = .65f;
        public float maximumArc = 110f;
        // Scene opt-in; historical clockwork layouts keep the default furniture policy.
        public bool musicBoxSharesPiano;
        public Transform musicBoxPianoLid;
        public GameObject musicBoxPianoLidProp;
        public Quaternion musicBoxPianoLidOpen = Quaternion.identity;
        public Quaternion musicBoxPianoLidClosed = Quaternion.identity;

        public void ApplyLayout()
        {
            if (facade == null || facade.instrumentRouter == null || roots.Length != 10
                || positions.Length != 5120 || yawAngles.Length != 5120) return;
            int mask = facade.instrumentRouter.enabledMask;
            int selection = (mask & 63) | ((mask >> 1) & 192);
            int start = (selection * 2 + facade.instrumentRouter.pianoAppearance) * 10;
            for (int i = 0; i < 10; i++)
            {
                bool selected = i == 9 ? facade.instrumentRouter.IsDrumsEnabled() : facade.instrumentRouter.IsViewEnabled(i);
                if (!selected || roots[i] == null) continue;
                roots[i].localPosition = positions[start + i];
                roots[i].localRotation = Quaternion.Euler(0, yawAngles[start + i], 0);
            }
            bool onPiano = musicBoxSharesPiano && (selection & 65) == 65;
            if (facade.musicBoxTable != null) facade.musicBoxTable.SetActive(!onPiano);
            if (facade.musicBoxSeat != null) facade.musicBoxSeat.SetActive(!onPiano);
            if (musicBoxPianoLid != null)
                musicBoxPianoLid.localRotation = onPiano && facade.instrumentRouter.pianoAppearance == 1
                    ? musicBoxPianoLidClosed : musicBoxPianoLidOpen;
            if (musicBoxPianoLidProp != null)
                musicBoxPianoLidProp.SetActive(!(onPiano && facade.instrumentRouter.pianoAppearance == 1));
            if (facade.drumView != null)
                facade.drumView.gameObject.SetActive(facade.instrumentRouter.IsDrumsEnabled() && facade.drumView.presentationEnabled);
        }
    }
}
