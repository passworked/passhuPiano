using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MusicMidiPlayer.Logic
{
    /// <summary>Local operation permission. Object ownership and instance Master are not permissions.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MidiControlGate : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public bool initiallyLocked;

        private bool _initialized;
        private bool _locked;

        private void Start()
        {
            _EnsureInitialized();
        }

        public bool CanControl()
        {
            _EnsureInitialized();
            return !_locked && Utilities.IsValid(facade) && facade.isReady;
        }

        public bool IsLocked()
        {
            _EnsureInitialized();
            return _locked;
        }

        public void SetLocked(bool value)
        {
            _EnsureInitialized();
            _locked = value;
        }

        public void _Lock()
        {
            SetLocked(true);
        }

        public void _Unlock()
        {
            SetLocked(false);
        }

        private void _EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _locked = initiallyLocked;
        }
    }
}
