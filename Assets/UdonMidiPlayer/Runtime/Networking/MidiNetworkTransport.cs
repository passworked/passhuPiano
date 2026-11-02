using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using MusicMidiPlayer.Data;
using MusicMidiPlayer.Logic;

namespace MusicMidiPlayer.Networking
{
    /// <summary>
    /// Transferable-owner state publisher. User commands stay local; only state and
    /// authenticated maintenance and live MIDI events cross the network.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MidiNetworkTransport : UdonSharpBehaviour
    {
        public MidiPlayerFacade facade;
        public MidiTransportClock clock;
        public MidiLibraryData library;
        public MidiControlGate gate;

        [Header("Network timing (seconds)")]
        public float heartbeatInterval = 3f;
        public float snapshotRequestInterval = 2f;
        public float snapshotSendInterval = 0.2f;
        public float ownershipTimeout = 5f;
        public float takeoverMinimum = 6f;
        public float takeoverMaximum = 10f;

        [UdonSynced] public string catalogHash = "";
        [UdonSynced] public string songId = "";
        [UdonSynced] public int state;
        [UdonSynced] public double anchorPositionSeconds;
        [UdonSynced] public double anchorServerSeconds;
        [UdonSynced] public int writerPlayerId;
        [UdonSynced] public int snapshotSequence;
        [UdonSynced] public string seekToken = "";
        [UdonSynced] public int instrumentIndex;
        [UdonSynced] public int enabledInstrumentMask = 1;
        [UdonSynced] public int pianoAppearance = 1;
        [UdonSynced] public int stringsArticulation;
        [UdonSynced] public string fileProgramMap = "";
        [UdonSynced] public int stringArticulationOverrides;

        [UdonSynced] public bool liveMidiActive;
        [UdonSynced] public double liveMidiEpoch;
        [UdonSynced] public int liveMidiRevision;
        [UdonSynced] public int[] liveMidiSnapshot = new int[0];

        private bool _liveActive;
        private double _liveEpoch;
        private double _liveFloor;
        private bool _liveHasEpoch;
        private bool _liveHasFloor;
        private int _liveRevision;
        private int _liveOwner;
        private float _liveLastInputAt;
        private float _liveLastSnapshotAt;
        private float _liveNextBatchAt;
        private int[] _liveSend = new int[256];
        private int _liveSendCount;
        private int[] _liveClaim = new int[128];
        private int _liveClaimCount;
        private int[] _pendingExpression = new int[16];
        private float _liveClaimAt;

        [HideInInspector] public bool networkReady;
        [HideInInspector] public bool hasPendingIntent;
        [HideInInspector] public string lastError = "";

        private const int Stopped = 0;
        private const int Playing = 1;
        private const int Paused = 2;

        private bool _initialized;
        private bool _localFallback;
        private bool _hasConfirmed;
        private bool _deferredReceive;
        private bool _applyingSnapshot;
        private bool _snapshotDirty;
        private bool _sendQueued;
        private bool _flightValid;
        private bool _ownershipRequested;
        private bool _pendingNeedsPermission;
        private bool _pendingWasOwner;
        private bool _pendingApplied;
        private bool _requestSnapshot;
        private bool _naturalEndSubmitted;
        private bool _takeoverAttempted;
        private int _observedOwnerId;
        private int _ownershipGeneration;
        private int _localSequence;
        private int _localSeekSequence;
        private int _intentGeneration;
        private float _ownershipRequestedAt;
        private float _lastActivityAt;
        private float _takeoverAt;
        private float _lastSentAt;
        private float _nextSendAt;
        private float _nextRequestAt;
        private string _validationError = "";

        // A replaceable complete target, never a queue of button/RPC commands.
        private string _pendingSongId = "";
        private int _pendingInstrument;
        private int _pendingMask = 1, _pendingPianoAppearance = 1;
        private int _pendingArticulation;
        private string _pendingFileProgramMap = "";
        private int _pendingStringArticulations;
        private int _pendingState;
        private double _pendingPosition;
        private double _pendingServer;
        private string _pendingSeekToken = "";

        // Separate from synced fields: local serialization and incoming data may overwrite them.
        private string _confirmedHash = "";
        private string _confirmedSongId = "";
        private int _confirmedInstrument;
        private int _confirmedMask = 1, _confirmedPianoAppearance = 1;
        private int _confirmedArticulation;
        private string _confirmedFileProgramMap = "";
        private int _confirmedStringArticulations;
        private int _confirmedState;
        private double _confirmedPosition;
        private double _confirmedServer;
        private int _confirmedWriter;
        private int _confirmedSequence;
        private string _confirmedSeekToken = "";

        // Bind a serialization result to the candidate actually sampled in OnPreSerialization.
        private string _flightHash = "";
        private string _flightSongId = "";
        private int _flightInstrument;
        private int _flightMask = 1, _flightPianoAppearance = 1;
        private int _flightArticulation;
        private string _flightFileProgramMap = "";
        private int _flightStringArticulations;
        private int _flightState;
        private double _flightPosition;
        private double _flightServer;
        private int _flightWriter;
        private int _flightSequence;
        private string _flightSeekToken = "";
        private int _flightIntentGeneration;
        private int _flightOwnershipGeneration;

        public void _Initialize()
        {
            if (NetworkCalling.InNetworkCall || _initialized) return;
            if (!_DependenciesReady()) return;
            _initialized = true;
            _localFallback = !Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer);
            _observedOwnerId = _localFallback ? 0 : _CurrentOwnerId();
            _MarkActivity();
            if (_localFallback)
            {
                networkReady = true;
                _SaveConfirmed(library.catalogHash, facade.selectedSongId, clock.GetState(),
                    clock.GetPositionSeconds(), clock.GetServerSeconds(), 0, 0, facade.selectedInstrument, facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides());
                return;
            }
            _TryBootstrap();
        }

        public bool HasConfirmedState()
        {
            return _hasConfirmed;
        }

        public bool IsTakeoverPending()
        {
            return hasPendingIntent;
        }

        public bool CanAcceptCommands()
        {
            return _initialized && networkReady && _hasConfirmed && _DependenciesReady() && lastError == "";
        }

        public void _CommitLocalState() { _CommitState(false); }

        public void _CommitSeek()
        {
            if (NetworkCalling.InNetworkCall || _applyingSnapshot) return;
            _CommitState(false);
            if (!hasPendingIntent) return;
            _localSeekSequence = _localSeekSequence == int.MaxValue ? 1 : _localSeekSequence + 1;
            _pendingSeekToken = _LocalPlayerId() + ":" + _localSeekSequence;
        }

        public void _CommitInstrumentState() { _CommitState(true); }

        private void _CommitState(bool preserveLive)
        {
            // This public UdonSharp entry is deliberately not NetworkCallable.
            if (NetworkCalling.InNetworkCall || _applyingSnapshot) return;
            if (!CanAcceptCommands() || (Utilities.IsValid(gate) && !gate.CanControl()))
            {
                if (hasPendingIntent) _CancelPending();
                else _Rollback();
                return;
            }

            double nowServer = clock.GetServerSeconds();
            string nextSong = facade.selectedSongId;
            int nextState = clock.GetState();
            double nextPosition = nextState == Stopped ? 0 : clock.GetPositionAtServerTime(nowServer);
            if (!_ValidateSnapshot(library.catalogHash, nextSong, nextState, nextPosition, nowServer, facade.selectedInstrument, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides()))
            {
                if (hasPendingIntent) _CancelPending();
                else _Rollback();
                _SetError(_validationError);
                return;
            }
            _naturalEndSubmitted = false;
            if (!preserveLive) _InvalidateLiveMidi(nowServer);

            if (_localFallback)
            {
                _SaveConfirmed(library.catalogHash, nextSong, nextState, nextPosition, nowServer, 0, 0, facade.selectedInstrument, facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides());
                return;
            }

            _SetPending(nextSong, nextState, nextPosition, nowServer, true, true);
            _BeginOwnershipRequest();
        }

        public void _NotifyNaturalEnd()
        {
            if (NetworkCalling.InNetworkCall || _applyingSnapshot || !_DependenciesReady() || lastError != "") return;
            // Drum tail indices and the final playback end are committed after loading.
            if (facade.isSongLoading) return;
            if (_naturalEndSubmitted || clock.GetState() != Playing) return;
            if (clock.GetPositionSeconds() + 0.001 < clock.GetPlaybackEndSeconds()) return;
            if (!_localFallback && !_IsLocalOwner()) return;

            _naturalEndSubmitted = true;
            double nowServer = clock.GetServerSeconds();
            string endedSong = facade.selectedSongId;
            _applyingSnapshot = true;
            facade.ApplyRemoteSnapshot(library.catalogHash, endedSong, Stopped, 0,
                nowServer, _LocalPlayerId(), _localSequence, true);
            _applyingSnapshot = false;
            if (_localFallback)
            {
                _SaveConfirmed(library.catalogHash, endedSong, Stopped, 0, nowServer, 0, 0, facade.selectedInstrument, facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides());
                return;
            }
            // A public maintenance transition does not require the local user's operation gate.
            _SetPending(endedSong, Stopped, 0, nowServer, false, true);
        }

        private void Update()
        {
            if (!_initialized || !_DependenciesReady()) return;

            // Editor Play Mode can start without a VRC player; do not call networking in that mode.
            if (_localFallback)
            {
                if (!Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer)) { _TickLiveMidi(); return; }
                _localFallback = false;
                _hasConfirmed = false;
                networkReady = false;
                hasPendingIntent = false;
                _observedOwnerId = _CurrentOwnerId();
                _MarkActivity();
            }

            _ObserveOwnership();
            if (_deferredReceive && writerPlayerId == _CurrentOwnerId()) _ProcessReceivedSnapshot();
            if (!_hasConfirmed) _TryBootstrap();
            networkReady = _hasConfirmed && VRC.SDKBase.Networking.IsNetworkSettled;
            if (!networkReady)
            {
                _SendInitialSnapshotRequest();
                return;
            }

            _TickLiveMidi();

            if (hasPendingIntent)
            {
                if (_pendingNeedsPermission && Utilities.IsValid(gate) && !gate.CanControl())
                {
                    _CancelPending();
                }
                else if (_IsLocalOwner())
                {
                    _pendingWasOwner = true;
                    if (!_pendingApplied)
                    {
                        _applyingSnapshot = true;
                        if (facade.instrumentRouter != null) facade._ApplyRoutingState(_pendingMask, _pendingPianoAppearance, _pendingFileProgramMap);
                        else facade._ApplyInstrument(_pendingInstrument);
                        facade._ApplyStringsArticulation(_pendingArticulation);
                        facade._ApplyStringArticulationOverrides(_pendingStringArticulations);
                        facade.ApplyRemoteSnapshot(library.catalogHash, _pendingSongId, _pendingState,
                            _pendingPosition, _pendingServer, _LocalPlayerId(), _localSequence, true);
                        _applyingSnapshot = false;
                        _pendingApplied = true;
                    }
                    if (!_sendQueued) _snapshotDirty = true;
                }
                else if (_pendingWasOwner || (_ownershipRequested &&
                    Time.realtimeSinceStartup - _ownershipRequestedAt >= Mathf.Max(1f, ownershipTimeout)))
                {
                    _CancelPending();
                }
            }

            if (_IsLocalOwner())
            {
                // The Facade may have reached the end before this client acquired ownership.
                // Check again here so that its local natural-ended latch cannot suppress the public terminal state.
                _NotifyNaturalEnd();
                if (_snapshotDirty && lastError == "" && !_sendQueued && Time.realtimeSinceStartup >= _nextSendAt)
                {
                    _sendQueued = true;
                    _snapshotDirty = false;
                    _nextSendAt = Time.realtimeSinceStartup + Mathf.Max(0.1f, snapshotSendInterval);
                    RequestSerialization();
                }
                if (lastError == "" && clock.GetState() == Playing &&
                    Time.realtimeSinceStartup - _lastSentAt >= Mathf.Max(1f, heartbeatInterval))
                {
                    _lastSentAt = Time.realtimeSinceStartup;
                    SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_Heartbeat));
                }
            }
            else
            {
                _TryInactiveOwnerTakeover();
                if (_requestSnapshot && !_IsLocalOwner() && Time.realtimeSinceStartup >= _nextRequestAt)
                {
                    _requestSnapshot = false;
                    _nextRequestAt = Time.realtimeSinceStartup + Mathf.Max(1f, snapshotRequestInterval);
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestSnapshot));
                }
            }
        }

        public override void OnPreSerialization()
        {
            _flightValid = false;
            if (_localFallback || !_initialized || !networkReady || !_hasConfirmed ||
                !_DependenciesReady() || !_IsLocalOwner()) return;
            if (hasPendingIntent && _pendingNeedsPermission && Utilities.IsValid(gate) && !gate.CanControl())
                _CancelPending();

            double nowServer = clock.GetServerSeconds();
            int currentState = clock.GetState();
            double position = currentState == Stopped ? 0 : clock.GetPositionAtServerTime(nowServer);
            if (!_ValidateSnapshot(library.catalogHash, facade.selectedSongId, currentState, position, nowServer, facade.selectedInstrument, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides())) return;

            liveMidiActive = _liveActive && clock.GetState() == Stopped;
            liveMidiEpoch = _liveEpoch;
            liveMidiRevision = _liveRevision;
            liveMidiSnapshot = liveMidiActive ? facade._CaptureLiveMidi() : new int[0];
            catalogHash = library.catalogHash;
            songId = facade.selectedSongId;
            state = currentState;
            anchorPositionSeconds = position;
            anchorServerSeconds = nowServer;
            writerPlayerId = _LocalPlayerId();
            _localSequence = _localSequence == int.MaxValue ? 1 : _localSequence + 1;
            snapshotSequence = _localSequence;
            seekToken = hasPendingIntent ? _pendingSeekToken : _confirmedSeekToken;
            instrumentIndex = facade.selectedInstrument;
            enabledInstrumentMask = facade.InstrumentMask();
            pianoAppearance = facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance;
            stringsArticulation = facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation;
            fileProgramMap = facade.FileProgramMap();
            stringArticulationOverrides = facade.StringArticulationOverrides();

            _flightHash = catalogHash;
            _flightSongId = songId;
            _flightInstrument = instrumentIndex;
            _flightMask = enabledInstrumentMask; _flightPianoAppearance = pianoAppearance;
            _flightArticulation = stringsArticulation;
            _flightFileProgramMap = fileProgramMap;
            _flightStringArticulations = stringArticulationOverrides;
            _flightState = state;
            _flightPosition = anchorPositionSeconds;
            _flightServer = anchorServerSeconds;
            _flightWriter = writerPlayerId;
            _flightSequence = snapshotSequence;
            _flightSeekToken = seekToken;
            _flightIntentGeneration = _intentGeneration;
            _flightOwnershipGeneration = _ownershipGeneration;
            _flightValid = true;
            _deferredReceive = false;
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            _sendQueued = false;
            if (!_flightValid) return;
            _flightValid = false;
            if (!_IsLocalOwner() || _flightOwnershipGeneration != _ownershipGeneration) return;
            if (!result.success)
            {
                _snapshotDirty = true;
                _nextSendAt = Time.realtimeSinceStartup + Mathf.Max(0.5f, snapshotSendInterval);
                return;
            }

            _lastSentAt = Time.realtimeSinceStartup;
            _confirmedSeekToken = _flightSeekToken;
            _SaveConfirmed(_flightHash, _flightSongId, _flightState, _flightPosition,
                _flightServer, _flightWriter, _flightSequence, _flightInstrument, _flightArticulation, _flightMask, _flightPianoAppearance, _flightFileProgramMap, _flightStringArticulations);
            _MarkActivity();
            if (hasPendingIntent && _flightIntentGeneration == _intentGeneration)
            {
                hasPendingIntent = false;
                _ownershipRequested = false;
            }
            else if (hasPendingIntent)
            {
                _snapshotDirty = true;
            }
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            _deferredReceive = true;
            if (!_initialized || !_DependenciesReady()) return;
            _ProcessReceivedSnapshot();
            // result.sendTime is diagnostic only. Server-anchor extrapolation already includes transit.
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!_initialized || _localFallback) return;
            _ObserveOwnership();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && !player.isLocal && _IsLocalOwner()) _snapshotDirty = true;
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void _Heartbeat()
        {
            if (!NetworkCalling.InNetworkCall) return;
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            VRCPlayerApi owner = VRC.SDKBase.Networking.GetOwner(gameObject);
            if (!Utilities.IsValid(sender) || !Utilities.IsValid(owner) || sender.playerId != owner.playerId) return;
            if (_hasConfirmed && _confirmedState == Playing) _MarkActivity();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void _RequestSnapshot()
        {
            if (!NetworkCalling.InNetworkCall || !_IsLocalOwner()) return;
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender) || !Utilities.IsValid(VRCPlayerApi.GetPlayerById(sender.playerId))) return;
            // Merge requests. Update submits later outside the network-call context.
            _snapshotDirty = true;
        }

        // One performer owns the same network object as song transport. Hardware input is
        // never a remote-call entry point; permission is checked before taking ownership.
        public void _SubmitLiveMidi(int kind, int channel, int number, int value)
        {
            if (NetworkCalling.InNetworkCall || !CanAcceptCommands() || hasPendingIntent ||
                !facade.CanReceiveLiveMidi() || gate == null || !gate.CanControl()) return;
            if (kind < 0 || kind > 9 || channel < 0 || channel > 15 || number < 0 || number > 127 || value < 0 || value > 127) return;
            if (kind >= 3 && (channel == 9 || value == 0 || facade.instrumentRouter == null || !facade.instrumentRouter.IsEnabled(kind - 3))) return;
            if (kind == 2 && number != 11 && number != 64 && number != 120 && number != 121 && number != 123) return;
            if (kind == 0 && value == 0) kind = 1;
            int packed = (kind << 18) | (channel << 14) | (number << 7) | value;
            // Keep controller settings before the first key without reserving an idle instrument.
            if (kind == 2 && number == 11 && !_liveActive)
            { _pendingExpression[channel] = value + 1; return; }
            if (!_localFallback && !_IsLocalOwner())
            {
                if (_liveClaimCount == 0 && kind != 0 && kind < 3 && !(kind == 2 && number == 64 && value >= 64)) return;
                if (_liveActive && _liveOwner == _CurrentOwnerId()) return;
                // Keep the initial chord and its note-offs while ownership is in flight.
                if (_liveClaimCount >= _liveClaim.Length) return;
                _liveClaim[_liveClaimCount++] = packed;
                if (_liveClaimCount == 1)
                {
                    _liveClaimAt = Time.realtimeSinceStartup;
                    VRC.SDKBase.Networking.SetOwner(VRC.SDKBase.Networking.LocalPlayer, gameObject);
                }
                _ObserveOwnership();
                if (!_IsLocalOwner()) return;
                _DrainLiveClaim();
                return;
            }
            // Ownership can complete between hardware callbacks and Update.
            // Replay earlier note-ons before accepting their later note-offs.
            if (_liveClaimCount > 0) _DrainLiveClaim();
            _AcceptLocalLiveMidi(packed);
        }

        private void _AcceptLocalLiveMidi(int packed)
        {
            if (!_liveActive)
            {
                // Stray note-offs/controllers should not reserve an otherwise idle instrument.
                int kind = packed >> 18;
                if (kind != 0 && kind < 3 && !(kind == 2 && ((packed >> 7) & 127) == 64 && (packed & 127) >= 64)) return;
                _ResetLiveSession();
                _liveEpoch = clock.GetServerSeconds();
                if (_liveHasFloor && _LiveDelta(_liveEpoch, _liveFloor) <= 0) _liveEpoch = _liveFloor + 0.001;
                _liveHasEpoch = true;
                _liveRevision = 0;
                _liveActive = true;
                _liveOwner = _LocalPlayerId();
                for (int channel = 0; channel < 16; channel++)
                {
                    if (_pendingExpression[channel] == 0) continue;
                    int expression = (2 << 18) | (channel << 14) | (11 << 7) | (_pendingExpression[channel] - 1);
                    _pendingExpression[channel] = 0;
                    facade._ApplyLiveMidiEvent(expression, ++_liveRevision);
                    if (!_localFallback)
                    {
                        if (_liveSendCount >= _liveSend.Length) _liveSendCount = 0;
                        _liveSend[_liveSendCount++] = expression;
                    }
                }
            }
            _liveRevision++;
            _liveLastInputAt = Time.realtimeSinceStartup;
            facade._ApplyLiveMidiEvent(packed, _liveRevision);
            if (!_localFallback)
            {
                // Overload falls back to the complete state snapshot, never leaves a stuck key.
                if (_liveSendCount >= _liveSend.Length) _liveSendCount = 0;
                _liveSend[_liveSendCount++] = packed;
                _snapshotDirty = true;
            }
        }

        private void _DrainLiveClaim()
        {
            int count = _liveClaimCount;
            _liveClaimCount = 0;
            if (!facade.CanReceiveLiveMidi() || gate == null || !gate.CanControl() || hasPendingIntent) return;
            for (int i = 0; i < count; i++) _AcceptLocalLiveMidi(_liveClaim[i]);
        }

        private void _TickLiveMidi()
        {
            if (_liveClaimCount > 0)
            {
                if (_IsLocalOwner()) _DrainLiveClaim();
                else if (Time.realtimeSinceStartup - _liveClaimAt > Mathf.Max(1f, ownershipTimeout)) _liveClaimCount = 0;
            }
            if (!_liveActive || (!_localFallback && !_IsLocalOwner())) return;
            if (!facade.CanReceiveLiveMidi() || gate == null || !gate.CanControl())
            { _InvalidateLiveMidi(clock.GetServerSeconds()); return; }
            float now = Time.realtimeSinceStartup;
            float releaseDelay = facade.waterfall == null ? 2f : Mathf.Max(2f, facade.waterfall.windowMs * 0.001f + 0.1f);
            if (now - _liveLastInputAt > releaseDelay && !facade._HasLiveMidiState())
            { _InvalidateLiveMidi(clock.GetServerSeconds()); return; }
            if (_localFallback) return;
            if (_liveSendCount > 0 && now >= _liveNextBatchAt)
            {
                int count = Mathf.Min(64, _liveSendCount);
                int[] batch = new int[count];
                for (int i = 0; i < count; i++) batch[i] = _liveSend[i];
                int revision = _liveRevision - _liveSendCount + count;
                _liveSendCount -= count;
                for (int i = 0; i < _liveSendCount; i++) _liveSend[i] = _liveSend[i + count];
                _liveNextBatchAt = now + 0.05f;
                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(_ReceiveLiveMidi), _liveEpoch, revision, batch);
            }
            // A held note also needs a durable state for late joiners and event-gap recovery.
            if (now - _liveLastSnapshotAt >= 1f)
            { _liveLastSnapshotAt = now; _snapshotDirty = true; }
        }

        [NetworkCallable(maxEventsPerSecond: 20)]
        public void _ReceiveLiveMidi(double epoch, int revision, int[] events)
        {
            if (!NetworkCalling.InNetworkCall || !_DependenciesReady() || !_hasConfirmed || hasPendingIntent) return;
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender) || sender.playerId != _CurrentOwnerId()) return;
            if (_observedOwnerId != sender.playerId) _ObserveOwnership();
            _ApplyLiveBatch(epoch, revision, events);
        }

        private void _ApplyLiveBatch(double epoch, int revision, int[] events)
        {
            if (!facade.CanReceiveLiveMidi() || !_Finite(epoch) ||
                (_liveHasFloor && _LiveDelta(epoch, _liveFloor) <= 0) ||
                (_liveHasEpoch && _LiveDelta(epoch, _liveEpoch) < 0) ||
                _LiveDelta(epoch, clock.GetServerSeconds()) > 2 || events == null || events.Length == 0 || events.Length > 64 || revision < events.Length) return;
            for (int i = 0; i < events.Length; i++)
                if (events[i] < 0 || events[i] >= (10 << 18) || ((events[i] >> 18) >= 3 && (((events[i] >> 14) & 15) == 9 || (events[i] & 127) == 0))) return;
            if (!_liveHasEpoch || _LiveDelta(epoch, _liveEpoch) > 0)
            {
                _ResetLiveSession();
                _liveEpoch = epoch;
                _liveHasEpoch = true;
                _liveRevision = 0;
            }
            int first = revision - events.Length + 1;
            if (first > _liveRevision + 1) _requestSnapshot = true;
            var ensemble = facade.voicePool.ensemble;
            if (ensemble != null) ensemble.BeginOutputBatch();
            for (int i = 0; i < events.Length; i++)
            {
                int sequence = first + i;
                if (sequence <= _liveRevision) continue;
                facade._ApplyLiveMidiEvent(events[i], sequence);
                _liveRevision = sequence;
            }
            if (ensemble != null) ensemble.EndOutputBatch();
            _liveActive = true;
            _liveOwner = _CurrentOwnerId();
        }

        private void _ApplyLiveSnapshot()
        {
            if (!_Finite(liveMidiEpoch) ||
                (_liveHasFloor && _LiveDelta(liveMidiEpoch, _liveFloor) < 0) ||
                (_liveHasEpoch && _LiveDelta(liveMidiEpoch, _liveEpoch) < 0) ||
                _LiveDelta(liveMidiEpoch, clock.GetServerSeconds()) > 2 || liveMidiRevision < 0 ||
                (_liveHasEpoch && liveMidiEpoch == _liveEpoch && liveMidiRevision < _liveRevision)) return;
            if (!liveMidiActive || state != Stopped)
            {
                _ResetLiveSession();
                // Initial inactive snapshots have no session timestamp.
                if (liveMidiRevision == 0 && liveMidiEpoch == 0 && !_liveHasEpoch) return;
                _liveEpoch = liveMidiEpoch;
                _liveHasEpoch = true;
                _liveRevision = liveMidiRevision;
                _AdvanceLiveFloor(liveMidiEpoch);
                return;
            }
            if ((_liveHasFloor && _LiveDelta(liveMidiEpoch, _liveFloor) <= 0) || !facade.CanReceiveLiveMidi() || liveMidiSnapshot == null || liveMidiSnapshot.Length > 2080) return;
            if (!_liveHasEpoch || _LiveDelta(liveMidiEpoch, _liveEpoch) > 0) _ResetLiveSession();
            facade._RestoreLiveMidi(liveMidiSnapshot);
            _liveEpoch = liveMidiEpoch;
            _liveHasEpoch = true;
            _liveRevision = liveMidiRevision;
            _liveActive = true;
            _liveOwner = _CurrentOwnerId();
        }

        public bool IsLiveMidiActive() { return _liveActive && facade != null && facade.CanReceiveLiveMidi(); }

        public string GetLiveMidiPlayerName()
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(_liveOwner);
            return Utilities.IsValid(player) ? player.displayName : "";
        }

        private void _ResetLiveSession()
        {
            _liveActive = false;
            _liveSendCount = 0;
            if (facade != null && facade.isReady) facade._ResetLiveMidiOutput();
        }

        public void _InvalidateLiveMidi(double server)
        {
            bool wasActive = _liveActive;
            _AdvanceLiveFloor(server);
            _ResetLiveSession();
            _liveClaimCount = 0;
            _liveEpoch = _liveFloor;
            _liveHasEpoch = _liveHasFloor;
            _liveRevision = 0;
            if (!_localFallback && _IsLocalOwner() && wasActive) _snapshotDirty = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.playerId == _liveOwner)
                _InvalidateLiveMidi(clock.GetServerSeconds());
        }

        private void _SetPending(string desiredSong, int desiredState, double position, double server,
            bool needsPermission, bool alreadyApplied)
        {
            if (!hasPendingIntent) _pendingSeekToken = _confirmedSeekToken;
            _intentGeneration = _intentGeneration == int.MaxValue ? 1 : _intentGeneration + 1;
            _pendingSongId = desiredSong;
            _pendingInstrument = facade.selectedInstrument;
            _pendingMask = facade.InstrumentMask(); _pendingPianoAppearance = facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance;
            _pendingArticulation = facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation;
            _pendingFileProgramMap = facade.FileProgramMap();
            _pendingStringArticulations = facade.StringArticulationOverrides();
            _pendingState = desiredState;
            _pendingPosition = position;
            _pendingServer = server;
            _pendingNeedsPermission = needsPermission;
            _pendingApplied = alreadyApplied;
            hasPendingIntent = true;
            _pendingWasOwner = _IsLocalOwner();
            if (_pendingWasOwner) _snapshotDirty = true;
        }

        private void _BeginOwnershipRequest()
        {
            if (_IsLocalOwner())
            {
                _pendingWasOwner = true;
                _snapshotDirty = true;
                return;
            }
            if (_ownershipRequested) return;
            _ownershipRequested = true;
            _ownershipRequestedAt = Time.realtimeSinceStartup;
            VRC.SDKBase.Networking.SetOwner(VRC.SDKBase.Networking.LocalPlayer, gameObject);
            // IsOwner is checked again in Update and OnPreSerialization, not treated as a global ack.
        }

        private void _ObserveOwnership()
        {
            int ownerId = _CurrentOwnerId();
            if (ownerId == _observedOwnerId) return;
            int previousOwner = _observedOwnerId;
            _observedOwnerId = ownerId;
            _ownershipGeneration++;
            // Events from the previous owner are rejected even if they are still queued in transit.
            _ResetLiveSession();
            if (_liveHasEpoch) _AdvanceLiveFloor(_liveEpoch);
            _liveEpoch = _liveFloor;
            _liveHasEpoch = _liveHasFloor;
            _liveRevision = 0;
            _sendQueued = false;
            _flightValid = false;
            _MarkActivity();

            if (_IsLocalOwner())
            {
                if (hasPendingIntent)
                {
                    _pendingWasOwner = true;
                    return;
                }
                if (_hasConfirmed && lastError == "")
                {
                    _ApplyConfirmed(true);
                    _snapshotDirty = true;
                }
            }
            else
            {
                _snapshotDirty = false;
                if (hasPendingIntent && (previousOwner == _LocalPlayerId() || ownerId != previousOwner))
                    _CancelPending();
                _requestSnapshot = true;
            }
        }

        private void _CancelPending()
        {
            hasPendingIntent = false;
            _ownershipRequested = false;
            _pendingWasOwner = false;
            _pendingApplied = false;
            _snapshotDirty = false;
            _Rollback();
            _requestSnapshot = true;
        }

        private void _Rollback()
        {
            if (_hasConfirmed && _DependenciesReady()) _ApplyConfirmed(true);
        }

        private void _ApplyConfirmed(bool forceRebuild)
        {
            if (!_hasConfirmed || !_DependenciesReady()) return;
            _applyingSnapshot = true;
            if (facade.instrumentRouter != null) facade._ApplyRoutingState(_confirmedMask, _confirmedPianoAppearance, _confirmedFileProgramMap);
            else facade._ApplyInstrument(_confirmedInstrument);
            facade._ApplyStringsArticulation(_confirmedArticulation);
            facade._ApplyStringArticulationOverrides(_confirmedStringArticulations);
            facade.ApplyRemoteSnapshot(_confirmedHash, _confirmedSongId, _confirmedState,
                _confirmedPosition, _confirmedServer, _confirmedWriter, _confirmedSequence, forceRebuild);
            _applyingSnapshot = false;
        }

        private void _ProcessReceivedSnapshot()
        {
            if (!_ValidateSnapshot(catalogHash, songId, state, anchorPositionSeconds, anchorServerSeconds, instrumentIndex, enabledInstrumentMask, pianoAppearance, fileProgramMap, stringArticulationOverrides))
            {
                _deferredReceive = false;
                if (hasPendingIntent) _CancelPending();
                _SetError(_validationError);
                _requestSnapshot = true;
                return;
            }
            int actualOwner = _CurrentOwnerId();
            if (writerPlayerId <= 0 || writerPlayerId != actualOwner)
            {
                _requestSnapshot = true;
                return;
            }

            if (_hasConfirmed && (state != _confirmedState || songId != _confirmedSongId))
                _InvalidateLiveMidi(anchorServerSeconds);
            bool duplicate = _hasConfirmed && catalogHash == _confirmedHash && songId == _confirmedSongId &&
                state == _confirmedState && anchorPositionSeconds == _confirmedPosition &&
                anchorServerSeconds == _confirmedServer && writerPlayerId == _confirmedWriter &&
                snapshotSequence == _confirmedSequence && seekToken == _confirmedSeekToken && instrumentIndex == _confirmedInstrument && stringsArticulation == _confirmedArticulation && enabledInstrumentMask == _confirmedMask && pianoAppearance == _confirmedPianoAppearance && fileProgramMap == _confirmedFileProgramMap && stringArticulationOverrides == _confirmedStringArticulations;
            bool recoveringFromError = lastError != "";
            bool explicitSeek = _hasConfirmed && seekToken != _confirmedSeekToken;
            _confirmedSeekToken = seekToken;
            _SaveConfirmed(catalogHash, songId, state, anchorPositionSeconds,
                anchorServerSeconds, writerPlayerId, snapshotSequence, instrumentIndex, stringsArticulation, enabledInstrumentMask, pianoAppearance, fileProgramMap, stringArticulationOverrides);
            _deferredReceive = false;
            _requestSnapshot = false;
            _MarkActivity();
            _ClearError();
            if (!hasPendingIntent && (!duplicate || recoveringFromError))
            {
                _naturalEndSubmitted = false;
                _ApplyConfirmed(recoveringFromError || explicitSeek);
            }
            if (!hasPendingIntent) _ApplyLiveSnapshot();
        }

        private void _SaveConfirmed(string hash, string selected, int currentState, double position,
            double server, int writer, int sequence, int selectedInstrument, int articulation, int mask, int appearance, string fileMap, int choices)
        {
            _confirmedHash = hash;
            _confirmedSongId = selected;
            _confirmedInstrument = selectedInstrument;
            _confirmedMask = mask; _confirmedPianoAppearance = appearance;
            _confirmedArticulation = articulation;
            _confirmedFileProgramMap = fileMap;
            _confirmedStringArticulations = choices;
            _confirmedState = currentState;
            _confirmedPosition = position;
            _confirmedServer = server;
            _confirmedWriter = writer;
            _confirmedSequence = sequence;
            _hasConfirmed = true;
        }

        private void _TryBootstrap()
        {
            if (_hasConfirmed || _localFallback || !VRC.SDKBase.Networking.IsNetworkSettled) return;
            if (writerPlayerId > 0)
            {
                if (!_ValidateSnapshot(catalogHash, songId, state, anchorPositionSeconds, anchorServerSeconds, instrumentIndex, enabledInstrumentMask, pianoAppearance, fileProgramMap, stringArticulationOverrides))
                {
                    _SetError(_validationError);
                    _requestSnapshot = true;
                    return;
                }
                if (writerPlayerId == _CurrentOwnerId()) _ProcessReceivedSnapshot();
                else if (_IsLocalOwner())
                {
                    // A new owner can inherit the last stored complete state when the old writer left
                    // before this client was ready. Do not reset a populated instance to defaults.
                    _confirmedSeekToken = seekToken;
                    _SaveConfirmed(catalogHash, songId, state, anchorPositionSeconds,
                        anchorServerSeconds, writerPlayerId, snapshotSequence, instrumentIndex, stringsArticulation, enabledInstrumentMask, pianoAppearance, fileProgramMap, stringArticulationOverrides);
                    _ClearError();
                    _ApplyConfirmed(true);
                    _snapshotDirty = true;
                }
                else _requestSnapshot = true;
                networkReady = _hasConfirmed;
                return;
            }
            if (!_IsLocalOwner())
            {
                _requestSnapshot = true;
                _SendInitialSnapshotRequest();
                return;
            }

            string initialSong = library.defaultSongId;
            if (!Utilities.IsValid(library.FindSong(initialSong)))
                initialSong = library.songs != null && library.songs.Length > 0 && Utilities.IsValid(library.songs[0])
                    ? library.songs[0].songId : "";
            double nowServer = clock.GetServerSeconds();
            _SaveConfirmed(library.catalogHash, initialSong, Stopped, 0, nowServer, _LocalPlayerId(), 0, facade.selectedInstrument, facade.voicePool.strings == null ? 0 : facade.voicePool.strings.articulation, facade.InstrumentMask(), facade.instrumentRouter == null ? 1 : facade.instrumentRouter.pianoAppearance, facade.FileProgramMap(), facade.StringArticulationOverrides());
            _ClearError();
            _ApplyConfirmed(true);
            networkReady = true;
            _snapshotDirty = true;
        }

        private void _SendInitialSnapshotRequest()
        {
            if (!_requestSnapshot || _IsLocalOwner() || Time.realtimeSinceStartup < _nextRequestAt) return;
            _requestSnapshot = false;
            _nextRequestAt = Time.realtimeSinceStartup + Mathf.Max(1f, snapshotRequestInterval);
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestSnapshot));
        }

        private void _TryInactiveOwnerTakeover()
        {
            if (!_hasConfirmed || hasPendingIntent || _takeoverAttempted || _confirmedState != Playing || lastError != "") return;
            if (Time.realtimeSinceStartup < _takeoverAt) return;
            if (!VRC.SDKBase.Networking.IsNetworkSettled || _IsLocalOwner()) return;
            double nowServer = clock.GetServerSeconds();
            double position = _confirmedPosition +
                VRC.SDKBase.Networking.CalculateServerDeltaTime(nowServer, _confirmedServer);
            double end = _PlaybackEndFor(_confirmedSongId, _confirmedInstrument, _confirmedArticulation);
            if (position < -3) position = -3;
            if (position > end) position = end;
            _takeoverAttempted = true;
            _SetPending(_confirmedSongId, Playing, position, nowServer, false, false);
            _pendingInstrument = _confirmedInstrument;
            _pendingMask = _confirmedMask; _pendingPianoAppearance = _confirmedPianoAppearance;
            _pendingArticulation = _confirmedArticulation;
            _pendingFileProgramMap = _confirmedFileProgramMap;
            _pendingStringArticulations = _confirmedStringArticulations;
            _BeginOwnershipRequest();
        }

        private void _MarkActivity()
        {
            _lastActivityAt = Time.realtimeSinceStartup;
            _takeoverAttempted = false;
            float minimum = Mathf.Max(heartbeatInterval + 1f, takeoverMinimum);
            float maximum = Mathf.Max(minimum, takeoverMaximum);
            _takeoverAt = _lastActivityAt + Random.Range(minimum, maximum);
        }

        private bool _ValidateSnapshot(string hash, string selected, int currentState, double position, double server, int selectedInstrument, int mask, int appearance, string fileMap, int choices)
        {
            _validationError = "";
            if (!Utilities.IsValid(library) || hash != library.catalogHash)
                _validationError = "播放器配置不一致，请联系世界作者";
            else if (facade.instrumentRouter != null && (mask < 0 || mask > 63 || appearance < 0 || appearance > 1
                || !facade.instrumentRouter.IsValidFileProgramMap(fileMap) || choices < 0 || choices > 255))
                _validationError = "播放器状态数据无效";
            else if (!facade.HasInstrument(selectedInstrument) || stringsArticulation < 0 || stringsArticulation > 2)
                _validationError = "播放器状态数据无效";
            else if (currentState < Stopped || currentState > Paused || !_Finite(position) || !_Finite(server) || position < -3)
                _validationError = "播放器状态数据无效";
            else if (string.IsNullOrEmpty(selected))
            {
                if (currentState != Stopped || position != 0 || (library.songs != null && library.songs.Length > 0))
                    _validationError = "播放器曲目不存在";
            }
            else if (!Utilities.IsValid(library.FindSong(selected)))
                _validationError = "播放器曲目不存在";
            else if (position > _PlaybackEndFor(selected, selectedInstrument, stringsArticulation) + 0.001 || (currentState == Stopped && position != 0))
                _validationError = "播放器进度数据无效";
            return _validationError == "";
        }

        private double _PlaybackEndFor(string selected, int selectedInstrument, int articulation)
        {
            MidiSongData song = library.FindSong(selected);
            if (!Utilities.IsValid(song)) return 0;
            var bank = facade.GetInstrumentBank(selectedInstrument);
            int tail = Utilities.IsValid(bank) ? bank.maxReleaseMs : 0;
            if (facade.instrumentRouter != null) tail = facade.instrumentRouter.MaxReleaseMs();
            else if (facade.GetStringsView(selectedInstrument) != null) tail = articulation == 0 ? 700 : 180;
            if (facade.voicePool.drums != null && facade.voicePool.drums.bank != null)
                tail = Mathf.Max(tail, facade.voicePool.drums.bank.maxTailMs);
            return ((double)song.durationMs + tail) * 0.001;
        }

        // Server time can be negative and wraps; zero is not an initial lower bound.
        private double _LiveDelta(double current, double previous)
        {
            return _localFallback ? current - previous
                : VRC.SDKBase.Networking.CalculateServerDeltaTime(current, previous);
        }

        private void _AdvanceLiveFloor(double server)
        {
            if (!_liveHasFloor || _LiveDelta(server, _liveFloor) > 0) _liveFloor = server;
            _liveHasFloor = true;
        }

        private bool _Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private bool _DependenciesReady()
        {
            return Utilities.IsValid(facade) && facade.isReady && Utilities.IsValid(clock) && Utilities.IsValid(library);
        }

        private int _CurrentOwnerId()
        {
            VRCPlayerApi owner = VRC.SDKBase.Networking.GetOwner(gameObject);
            return Utilities.IsValid(owner) ? owner.playerId : 0;
        }

        private int _LocalPlayerId()
        {
            VRCPlayerApi player = VRC.SDKBase.Networking.LocalPlayer;
            return Utilities.IsValid(player) ? player.playerId : 0;
        }

        private bool _IsLocalOwner()
        {
            return Utilities.IsValid(VRC.SDKBase.Networking.LocalPlayer) && VRC.SDKBase.Networking.IsOwner(gameObject);
        }

        private void _SetError(string message)
        {
            if (lastError == message) return;
            lastError = message;
            if (Utilities.IsValid(facade)) facade.SetNetworkError(message);
        }

        private void _ClearError()
        {
            if (lastError == "") return;
            lastError = "";
            facade.SetNetworkError("");
        }
    }
}
