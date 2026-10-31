using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MusicMidiPlayer.Logic
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiPerformanceSeat : UdonSharpBehaviour
    {
        public MidiPerformanceInput input;
        public VRC.SDK3.Components.VRCStation station;
        public int instrumentIndex;
        private bool _localSeated;

        public override void Interact()
        {
            VRCPlayerApi player = VRC.SDKBase.Networking.LocalPlayer;
            if (!Utilities.IsValid(player) || station == null || input == null) return;
            if (_localSeated) { ExitLocal(); return; }
            if (!input.CanPlayView(instrumentIndex)) return;
            // Desktop uses movement keys for notes, with an explicit Enter-to-leave shortcut.
            station.disableStationExit = !player.IsUserInVR();
            player.UseAttachedStation();
        }

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal || input == null) return;
            _localSeated = true;
            input.EnterSeat(this);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            _localSeated = false;
            if (input != null) input.LeaveSeat(this);
        }

        public void ExitLocal()
        {
            if (!_localSeated) return;
            _localSeated = false;
            if (input != null) input.LeaveSeat(this);
            if (station != null && Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer)) station.ExitStation(VRC.SDKBase.Networking.LocalPlayer);
        }

        private void OnDisable() { ExitLocal(); }
        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal) ExitLocal();
        }
    }
}
