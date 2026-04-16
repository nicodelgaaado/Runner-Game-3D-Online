using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace RunnerGame.Online
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(RunnerMotor))]
    [RequireComponent(typeof(RunnerInputAdapter))]
    [RequireComponent(typeof(RunnerPresentation))]
    [RequireComponent(typeof(LocalPlayerCameraBinder))]
    public class RunnerNetworkPlayer : NetworkBehaviour
    {
        private const string RedCollisionLayerName = "RedCollision";
        private const string BlueCollisionLayerName = "BlueCollision";
        private const float GenericStrongForce = 1000000f;
        private const float SpikeForce = 100000f;
        private const float PredictedHitGraceDuration = 0.2f;
        private const int MaxBufferedInputs = 1024;
        private const int MaxReplayInputsPerReconciliation = 8;

        private readonly NetworkVariable<RunnerSpawnSlot> spawnSlot = new(
            RunnerSpawnSlot.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<RaceRoundState> roundState = new(
            new RaceRoundState(1, RaceRoundPhase.WaitingForPlayers, 0d),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> movingState = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> fallingState = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> climbingState = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> winnerState = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> loserState = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<RunnerMotorSnapshot> authoritativeMotorSnapshot = new(
            default,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        private readonly SortedDictionary<int, RunnerInputState> pendingOwnerInputs = new();

        private RunnerMotor motor;
        private RunnerInputAdapter inputAdapter;
        private RunnerPresentation presentation;
        private LocalPlayerCameraBinder cameraBinder;
        private NetworkTransform networkTransform;
        private bool rawOwnerMoveHeld;
        private RunnerInputState localOwnerInputState;
        private RunnerInputState serverAuthoritativeInputState;
        private RunnerMotorSnapshot latestAuthoritativeSnapshot;
        private bool hasAuthoritativeSnapshot;
        private bool hasPendingAuthoritativeSnapshot;
        private bool predictedMovingState;
        private bool predictedFallingState;
        private bool predictedClimbingState;
        private bool predictedHazardLock;
        private bool awaitingAuthoritativeBootstrap;
        private bool warnedPendingBufferGrowth;
        private float predictedHazardGraceRemaining;
        private bool respawning;
        private int snapshotRevision;
        private int nextLocalInputSequence;
        private int lastAppliedAuthoritativeRevision = -1;
        private int lastReceivedInputSequence = -1;
        private int lastAppliedInputSequence = -1;
        private int lastProcessedInputSequence = -1;
        private int lastSampledLocalInputTick = -1;
        private int lastPredictedSimulationTick = -1;
        private int lastAuthoritativeSimulationTick = -1;
        private int lastReplayWindowSize;
        private float lastReplayPositionError;
        private float lastReplayPathError;
        private bool lastReplayWindowClamped;

        public static RunnerNetworkPlayer LocalPlayer { get; private set; }

        public RunnerSpawnSlot SpawnSlot => spawnSlot.Value;
        public RaceRoundState SharedRoundState => roundState.Value;
        public int LocalInputSequence => localOwnerInputState.InputSequence;
        public int ReceivedInputSequence => lastReceivedInputSequence;
        public int ProcessedInputSequence => lastProcessedInputSequence;
        public int PendingInputCount => pendingOwnerInputs.Count;
        public int UnackedInputGap => Mathf.Max(0, LocalInputSequence - ReceivedInputSequence);
        public int ReplayWindowSize => lastReplayWindowSize;
        public float ReplayPositionError => lastReplayPositionError;
        public float ReplayPathError => lastReplayPathError;
        public bool ReplayWindowClamped => lastReplayWindowClamped;
        public bool AwaitingAuthoritativeSnapshot => UsesOwnerPrediction && awaitingAuthoritativeBootstrap;
        public bool IsOwnerRole => IsOwner;
        public bool IsServerRole => IsServer;
        public ulong OwnerId => OwnerClientId;
        public ulong ServerId => NetworkManager.Singleton != null ? NetworkManager.ServerClientId : ulong.MaxValue;
        public float LocalPositionY => motor != null ? motor.Rigidbody.position.y : 0f;
        public bool HasGroundSupport => motor != null && motor.HasGroundSupport;
        public string GroundSupportColliderName => motor != null ? motor.LastSupportColliderName : "n/a";
        public string GroundSupportStatus => motor != null ? motor.LastSupportStatus : "n/a";
        public float GroundSupportNormalY => motor != null ? motor.LastSupportNormalY : 0f;
        public bool HasAuthoritativeSnapshot => hasAuthoritativeSnapshot;
        public float AuthoritativePositionY => latestAuthoritativeSnapshot.Position.y;
        private bool UsesOwnerPrediction => IsOwner && !IsServer;

        private void Awake()
        {
            motor = GetComponent<RunnerMotor>();
            inputAdapter = GetComponent<RunnerInputAdapter>();
            presentation = GetComponent<RunnerPresentation>();
            cameraBinder = GetComponent<LocalPlayerCameraBinder>();
            networkTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            spawnSlot.OnValueChanged += HandleSpawnSlotChanged;
            roundState.OnValueChanged += HandleRoundStateChanged;
            movingState.OnValueChanged += HandleStateChanged;
            fallingState.OnValueChanged += HandleStateChanged;
            climbingState.OnValueChanged += HandleStateChanged;
            winnerState.OnValueChanged += HandleStateChanged;
            loserState.OnValueChanged += HandleStateChanged;
            authoritativeMotorSnapshot.OnValueChanged += HandleMotorSnapshotChanged;

            if (IsOwner)
            {
                LocalPlayer = this;
                cameraBinder.enabled = true;
                cameraBinder.Bind(transform, spawnSlot.Value);
            }
            else
            {
                inputAdapter.enabled = false;
                cameraBinder.enabled = false;
            }

            if (networkTransform != null)
            {
                networkTransform.enabled = !UsesOwnerPrediction;
            }

            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: IsServer, isPredictedOwnerInstance: UsesOwnerPrediction);
            ResetPredictionState();

            ConfigurePlayerCollisionLayers();
            IgnoreOtherPlayerCollisions();
            RefreshPresentation();

            StartCoroutine(RegisterWithRaceManagerWhenReady());
        }

        public override void OnNetworkDespawn()
        {
            spawnSlot.OnValueChanged -= HandleSpawnSlotChanged;
            roundState.OnValueChanged -= HandleRoundStateChanged;
            movingState.OnValueChanged -= HandleStateChanged;
            fallingState.OnValueChanged -= HandleStateChanged;
            climbingState.OnValueChanged -= HandleStateChanged;
            winnerState.OnValueChanged -= HandleStateChanged;
            loserState.OnValueChanged -= HandleStateChanged;
            authoritativeMotorSnapshot.OnValueChanged -= HandleMotorSnapshotChanged;

            if (IsOwner && cameraBinder != null)
            {
                cameraBinder.Release();
            }

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }

            if (networkTransform != null)
            {
                networkTransform.enabled = true;
            }

            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: false, isPredictedOwnerInstance: false);
            ResetPredictionState();
        }

        private void Update()
        {
            if (IsOwner && inputAdapter != null)
            {
                rawOwnerMoveHeld = inputAdapter.Capture().MoveHeld;
            }

            RefreshPresentation();
        }

        private void FixedUpdate()
        {
            LevelCourseDefinition currentCourse = NetworkRaceManager.Instance != null
                ? NetworkRaceManager.Instance.GetCurrentCourse()
                : null;

            if (IsOwner)
            {
                TickLocalOwner(currentCourse);
            }

            if (!IsServer || respawning || NetworkRaceManager.Instance == null)
            {
                return;
            }

            TickAuthoritativeServerLoop(currentCourse);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (TryHandlePredictedOwnerCollision(collision))
            {
                return;
            }

            if (!IsServer || respawning || NetworkRaceManager.Instance == null || NetworkRaceManager.Instance.RoundState.Phase != RaceRoundPhase.Racing)
            {
                return;
            }

            Vector3 collisionNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;
            if (!TryGetHazardForce(collision.collider, collisionNormal, out Vector3 force, out bool enableGravity))
            {
                return;
            }

            motor.ApplyHit(force, enableGravity);
            movingState.Value = false;
            fallingState.Value = true;
            climbingState.Value = false;
            AdvanceSnapshotRevision();
            PublishAuthoritativeSnapshot(climbing: false, falling: true, processedInputSequence: GetServerProcessedInputSequence());
            StartRespawnServer();
        }

        public void AssignSpawnSlotServer(RunnerSpawnSlot slot)
        {
            if (IsServer)
            {
                spawnSlot.Value = slot;
            }
        }

        public void ResetForRoundServer(LevelCourseDefinition course)
        {
            if (!IsServer)
            {
                return;
            }

            respawning = false;
            movingState.Value = false;
            fallingState.Value = false;
            climbingState.Value = false;
            winnerState.Value = false;
            loserState.Value = false;
            motor.ResetForLevel(course);
            AdvanceSnapshotRevision();
            PublishAuthoritativeSnapshot(climbing: false, falling: false, processedInputSequence: GetServerProcessedInputSequence());
        }

        public void ApplyRoundResultServer(bool winner, bool loser)
        {
            if (!IsServer)
            {
                return;
            }

            movingState.Value = false;
            fallingState.Value = false;
            climbingState.Value = false;
            winnerState.Value = winner;
            loserState.Value = loser;
            AdvanceSnapshotRevision();
            PublishAuthoritativeSnapshot(climbing: false, falling: false, processedInputSequence: GetServerProcessedInputSequence());
        }

        public void SetRoundStateServer(RaceRoundState value)
        {
            if (IsServer)
            {
                roundState.Value = value;
            }
        }

        private void StartRespawnServer()
        {
            if (!IsServer || respawning)
            {
                return;
            }

            StartCoroutine(RespawnServerRoutine());
        }

        private IEnumerator RespawnServerRoutine()
        {
            respawning = true;
            yield return new WaitForSeconds(2f);

            LevelCourseDefinition currentCourse = NetworkRaceManager.Instance != null ? NetworkRaceManager.Instance.GetCurrentCourse() : null;
            if (currentCourse != null)
            {
                motor.ResetForLevel(currentCourse);
            }

            movingState.Value = false;
            fallingState.Value = false;
            climbingState.Value = false;
            AdvanceSnapshotRevision();
            PublishAuthoritativeSnapshot(climbing: false, falling: false, processedInputSequence: GetServerProcessedInputSequence());
            respawning = false;
        }

        private IEnumerator RegisterWithRaceManagerWhenReady()
        {
            while (NetworkRaceManager.Instance == null)
            {
                yield return null;
            }

            NetworkRaceManager.Instance.RegisterPlayer(this);
        }

        private void HandleSpawnSlotChanged(RunnerSpawnSlot _, RunnerSpawnSlot current)
        {
            ConfigurePlayerCollisionLayers();
            if (IsOwner && cameraBinder != null)
            {
                cameraBinder.Bind(transform, current);
            }

            RefreshPresentation();
        }

        private void HandleStateChanged<T>(T _, T __)
        {
            RefreshPresentation();
        }

        private void HandleRoundStateChanged(RaceRoundState previous, RaceRoundState current)
        {
            if (UsesOwnerPrediction
                && current.Phase == RaceRoundPhase.Racing
                && (previous.Phase != current.Phase || previous.LevelIndex != current.LevelIndex))
            {
                BeginPredictionBootstrap();
            }

            RefreshPresentation();
        }

        private void HandleMotorSnapshotChanged(RunnerMotorSnapshot _, RunnerMotorSnapshot current)
        {
            latestAuthoritativeSnapshot = current;
            lastReceivedInputSequence = current.ReceivedInputSequence;
            hasAuthoritativeSnapshot = true;
            hasPendingAuthoritativeSnapshot = true;

            if (UsesOwnerPrediction && current.Revision != lastAppliedAuthoritativeRevision)
            {
                BeginPredictionBootstrap();
            }
        }

        private void RefreshPresentation()
        {
            bool moving = UsesOwnerPrediction ? predictedMovingState : movingState.Value;
            bool falling = UsesOwnerPrediction ? predictedFallingState : fallingState.Value;
            bool climbing = UsesOwnerPrediction ? predictedClimbingState : climbingState.Value;

            presentation.UpdateAnimationState(
                spawnSlot.Value,
                moving,
                falling,
                climbing,
                winnerState.Value,
                loserState.Value);
        }

        private void ConfigurePlayerCollisionLayers()
        {
            int redCollisionLayer = LayerMask.NameToLayer(RedCollisionLayerName);
            int blueCollisionLayer = LayerMask.NameToLayer(BlueCollisionLayerName);

            if (redCollisionLayer >= 0 && blueCollisionLayer >= 0)
            {
                Physics.IgnoreLayerCollision(redCollisionLayer, blueCollisionLayer, true);
                gameObject.layer = spawnSlot.Value == RunnerSpawnSlot.Blue ? blueCollisionLayer : redCollisionLayer;
            }
        }

        private void IgnoreOtherPlayerCollisions()
        {
            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            foreach (RunnerNetworkPlayer other in FindObjectsByType<RunnerNetworkPlayer>(FindObjectsInactive.Exclude))
            {
                if (other == this)
                {
                    continue;
                }

                Collider otherCollider = other.GetComponent<Collider>();
                if (otherCollider != null)
                {
                    Physics.IgnoreCollision(ownCollider, otherCollider, true);
                }
            }
        }

        private static bool TryGetHazardForce(Collider hazardCollider, Vector3 collisionNormal, out Vector3 force, out bool enableGravity)
        {
            enableGravity = false;
            force = Vector3.zero;

            ObstacleManager obstacleManager = LegacySceneAdapter.ObstacleManager ?? Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);
            if (obstacleManager == null || !obstacleManager.TryGetOnlineHazardResponse(hazardCollider, out ObstacleManager.OnlineHazardResponseKind responseKind))
            {
                return false;
            }

            switch (responseKind)
            {
                case ObstacleManager.OnlineHazardResponseKind.SpikeBackUpLeft:
                    force = new Vector3(-1f, 1f, -1f) * SpikeForce;
                    return true;
                case ObstacleManager.OnlineHazardResponseKind.SpikeUpForward:
                    force = new Vector3(0f, 1f, 1f) * SpikeForce;
                    return true;
                case ObstacleManager.OnlineHazardResponseKind.SpikeBackUp:
                    force = new Vector3(-1f, 1f, 0f) * SpikeForce;
                    return true;
                case ObstacleManager.OnlineHazardResponseKind.GenericStrong:
                    force = collisionNormal * GenericStrongForce;
                    return true;
                case ObstacleManager.OnlineHazardResponseKind.GenericStrongWithGravity:
                    force = collisionNormal * GenericStrongForce;
                    enableGravity = true;
                    return true;
                default:
                    return false;
            }
        }

        private void TickLocalOwner(LevelCourseDefinition currentCourse)
        {
            int currentLocalTick = GetLocalTick();
            if (currentLocalTick < 0)
            {
                return;
            }

            if (UsesOwnerPrediction)
            {
                TickPredictedOwner(currentCourse, currentLocalTick);
                return;
            }

            TickHostOwnerInput(currentCourse, currentLocalTick);
        }

        private void TickHostOwnerInput(LevelCourseDefinition currentCourse, int currentLocalTick)
        {
            if (currentLocalTick <= lastSampledLocalInputTick)
            {
                return;
            }

            for (int tick = lastSampledLocalInputTick + 1; tick <= currentLocalTick; tick++)
            {
                if (ShouldSampleOwnerInput(currentCourse))
                {
                    CaptureLocalOwnerInput();
                    serverAuthoritativeInputState = localOwnerInputState;
                    lastReceivedInputSequence = localOwnerInputState.InputSequence;
                }

                lastSampledLocalInputTick = tick;
            }
        }

        private void TickPredictedOwner(LevelCourseDefinition currentCourse, int currentLocalTick)
        {
            if (predictedHazardGraceRemaining > 0f)
            {
                predictedHazardGraceRemaining = Mathf.Max(0f, predictedHazardGraceRemaining - GetPredictedDeltaTime());
            }

            if (NetworkRaceManager.Instance == null)
            {
                predictedMovingState = false;
                predictedClimbingState = false;
                return;
            }

            TryApplyPendingAuthoritativeSnapshot(currentCourse);

            if (awaitingAuthoritativeBootstrap)
            {
                predictedMovingState = false;
                predictedClimbingState = false;
                lastReplayWindowSize = 0;
                lastReplayWindowClamped = false;

                if (!predictedHazardLock && hasAuthoritativeSnapshot)
                {
                    predictedFallingState = latestAuthoritativeSnapshot.Falling;
                }

                lastSampledLocalInputTick = currentLocalTick;
                lastPredictedSimulationTick = currentLocalTick;

                return;
            }

            int startTick = Mathf.Max(lastSampledLocalInputTick, lastPredictedSimulationTick) + 1;
            if (startTick > currentLocalTick)
            {
                return;
            }

            for (int tick = startTick; tick <= currentLocalTick; tick++)
            {
                if (!ShouldSampleOwnerInput(currentCourse))
                {
                    predictedMovingState = false;
                    predictedClimbingState = false;

                    if (!predictedHazardLock && hasAuthoritativeSnapshot)
                    {
                        predictedFallingState = latestAuthoritativeSnapshot.Falling;
                    }

                    lastSampledLocalInputTick = tick;
                    lastPredictedSimulationTick = tick;
                    continue;
                }

                CaptureLocalOwnerInput();

                if (predictedFallingState)
                {
                    predictedMovingState = false;
                    predictedClimbingState = false;
                    lastSampledLocalInputTick = tick;
                    lastPredictedSimulationTick = tick;
                    continue;
                }

                bool climbing = motor.Tick(currentCourse, localOwnerInputState.MoveHeld, null, GetPredictedDeltaTime());
                predictedClimbingState = climbing;
                predictedMovingState = localOwnerInputState.MoveHeld && !climbing;
                predictedFallingState = predictedHazardLock || motor.Rigidbody.linearVelocity.y < motor.FallingThreshold;

                if (predictedFallingState)
                {
                    predictedMovingState = false;
                }

                lastSampledLocalInputTick = tick;
                lastPredictedSimulationTick = tick;
            }
        }

        private bool ShouldSampleOwnerInput(LevelCourseDefinition currentCourse)
        {
            bool isLocallyFalling = UsesOwnerPrediction ? predictedFallingState : fallingState.Value;
            return IsOwner
                && currentCourse != null
                && NetworkRaceManager.Instance != null
                && NetworkRaceManager.Instance.RoundState.Phase == RaceRoundPhase.Racing
                && !awaitingAuthoritativeBootstrap
                && !respawning
                && !isLocallyFalling;
        }

        private void CaptureLocalOwnerInput()
        {
            localOwnerInputState = new RunnerInputState(rawOwnerMoveHeld, nextLocalInputSequence++);

            if (!UsesOwnerPrediction)
            {
                return;
            }

            SubmitOwnerInputRpc(localOwnerInputState);
            pendingOwnerInputs[localOwnerInputState.InputSequence] = localOwnerInputState;
            if (pendingOwnerInputs.Count > MaxBufferedInputs && !warnedPendingBufferGrowth)
            {
                warnedPendingBufferGrowth = true;
                Debug.LogWarning($"RunnerNetworkPlayer pending input buffer grew to {pendingOwnerInputs.Count} unacknowledged inputs for owner {OwnerClientId}.", this);
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitOwnerInputRpc(RunnerInputState inputState)
        {
            if (!IsServer)
            {
                return;
            }

            if (inputState.InputSequence < serverAuthoritativeInputState.InputSequence)
            {
                return;
            }

            serverAuthoritativeInputState = inputState;
            lastReceivedInputSequence = inputState.InputSequence;
        }

        private void TickAuthoritativeServerLoop(LevelCourseDefinition currentCourse)
        {
            int currentServerTick = GetServerTick();
            if (currentServerTick < 0 || currentServerTick <= lastAuthoritativeSimulationTick)
            {
                return;
            }

            for (int tick = lastAuthoritativeSimulationTick + 1; tick <= currentServerTick; tick++)
            {
                TickAuthoritativeServer(currentCourse);
                lastAuthoritativeSimulationTick = tick;
            }
        }

        private void TickAuthoritativeServer(LevelCourseDefinition currentCourse)
        {
            if (currentCourse == null || NetworkRaceManager.Instance.RoundState.Phase != RaceRoundPhase.Racing)
            {
                movingState.Value = false;
                climbingState.Value = false;
                PublishAuthoritativeSnapshot(climbing: false, falling: false, processedInputSequence: GetServerProcessedInputSequence());
                return;
            }

            RunnerInputState authoritativeInput = serverAuthoritativeInputState;
            lastReceivedInputSequence = authoritativeInput.InputSequence;

            lastAppliedInputSequence = authoritativeInput.InputSequence;
            bool climbing = motor.Tick(currentCourse, authoritativeInput.MoveHeld, () => NetworkRaceManager.Instance.NotifyPlayerFinishedServer(this), GetAuthoritativeDeltaTime());
            climbingState.Value = climbing;
            movingState.Value = authoritativeInput.MoveHeld && !climbing;

            bool falling = motor.Rigidbody.linearVelocity.y < motor.FallingThreshold;
            fallingState.Value = falling;
            if (falling)
            {
                AdvanceSnapshotRevision();
                PublishAuthoritativeSnapshot(climbing, falling: true, processedInputSequence: authoritativeInput.InputSequence);
                StartRespawnServer();
                return;
            }

            PublishAuthoritativeSnapshot(climbing, falling: false, processedInputSequence: authoritativeInput.InputSequence);
        }

        private void TryApplyPendingAuthoritativeSnapshot(LevelCourseDefinition currentCourse)
        {
            if (!UsesOwnerPrediction || !hasAuthoritativeSnapshot || !hasPendingAuthoritativeSnapshot)
            {
                return;
            }

            bool revisionChanged = latestAuthoritativeSnapshot.Revision != lastAppliedAuthoritativeRevision;
            bool shouldWaitForHazardConfirmation = predictedHazardLock
                && !revisionChanged
                && !latestAuthoritativeSnapshot.Falling
                && predictedHazardGraceRemaining > 0f;

            if (shouldWaitForHazardConfirmation)
            {
                return;
            }

            if (awaitingAuthoritativeBootstrap && !revisionChanged)
            {
                hasPendingAuthoritativeSnapshot = false;
                return;
            }

            Vector3 previousPosition = motor.Rigidbody.position;
            float previousPathState = motor.PathState;
            int processedInputSequence = latestAuthoritativeSnapshot.ProcessedInputSequence;

            DropAcknowledgedInputs(processedInputSequence);
            if (revisionChanged)
            {
                pendingOwnerInputs.Clear();
                warnedPendingBufferGrowth = false;
                lastReplayWindowSize = 0;
                lastReplayWindowClamped = false;
            }
            else
            {
                lastReplayWindowSize = 0;
                lastReplayWindowClamped = false;
            }

            motor.HardResetFromSnapshot(latestAuthoritativeSnapshot);

            predictedMovingState = false;
            predictedClimbingState = latestAuthoritativeSnapshot.Climbing;
            predictedFallingState = latestAuthoritativeSnapshot.Falling;

            if (!revisionChanged
                && currentCourse != null
                && NetworkRaceManager.Instance != null
                && NetworkRaceManager.Instance.RoundState.Phase == RaceRoundPhase.Racing)
            {
                foreach (RunnerInputState replayInput in BuildReplayWindow(processedInputSequence))
                {
                    if (predictedFallingState)
                    {
                        continue;
                    }

                    bool replayClimbing = motor.Tick(currentCourse, replayInput.MoveHeld, null, GetPredictedDeltaTime());
                    predictedClimbingState = replayClimbing;
                    predictedMovingState = replayInput.MoveHeld && !replayClimbing;
                    predictedFallingState = motor.Rigidbody.linearVelocity.y < motor.FallingThreshold;
                }
            }

            if (predictedFallingState)
            {
                predictedMovingState = false;
            }

            lastReplayPositionError = Vector3.Distance(previousPosition, motor.Rigidbody.position);
            lastReplayPathError = Mathf.Abs(previousPathState - motor.PathState);
            lastProcessedInputSequence = processedInputSequence;
            lastAppliedAuthoritativeRevision = latestAuthoritativeSnapshot.Revision;
            predictedHazardLock = false;
            predictedHazardGraceRemaining = 0f;
            awaitingAuthoritativeBootstrap = false;
            hasPendingAuthoritativeSnapshot = false;
        }

        private void DropAcknowledgedInputs(int processedInputSequence)
        {
            if (pendingOwnerInputs.Count == 0)
            {
                warnedPendingBufferGrowth = false;
                return;
            }

            List<int> acknowledgedSequences = null;
            foreach (KeyValuePair<int, RunnerInputState> pendingInput in pendingOwnerInputs)
            {
                if (pendingInput.Key > processedInputSequence)
                {
                    break;
                }

                acknowledgedSequences ??= new List<int>();
                acknowledgedSequences.Add(pendingInput.Key);
            }

            if (acknowledgedSequences != null)
            {
                foreach (int acknowledgedSequence in acknowledgedSequences)
                {
                    pendingOwnerInputs.Remove(acknowledgedSequence);
                }
            }

            if (pendingOwnerInputs.Count <= MaxBufferedInputs)
            {
                warnedPendingBufferGrowth = false;
            }
        }

        private List<RunnerInputState> BuildReplayWindow(int processedInputSequence)
        {
            List<RunnerInputState> replayWindow = new();
            if (pendingOwnerInputs.Count == 0)
            {
                lastReplayWindowSize = 0;
                lastReplayWindowClamped = false;
                return replayWindow;
            }

            int newestSequence = localOwnerInputState.InputSequence;
            int minimumSequenceToKeep = processedInputSequence + 1;
            if (newestSequence - processedInputSequence > MaxReplayInputsPerReconciliation)
            {
                minimumSequenceToKeep = newestSequence - MaxReplayInputsPerReconciliation + 1;
                lastReplayWindowClamped = true;
            }

            List<int> staleSequences = null;
            foreach (KeyValuePair<int, RunnerInputState> pendingInput in pendingOwnerInputs)
            {
                if (pendingInput.Key < minimumSequenceToKeep)
                {
                    staleSequences ??= new List<int>();
                    staleSequences.Add(pendingInput.Key);
                    continue;
                }

                if (pendingInput.Key <= processedInputSequence)
                {
                    continue;
                }

                replayWindow.Add(pendingInput.Value);
            }

            if (staleSequences != null)
            {
                foreach (int staleSequence in staleSequences)
                {
                    pendingOwnerInputs.Remove(staleSequence);
                }
            }

            lastReplayWindowSize = replayWindow.Count;
            return replayWindow;
        }

        private bool TryHandlePredictedOwnerCollision(Collision collision)
        {
            if (!UsesOwnerPrediction
                || respawning
                || predictedHazardLock
                || NetworkRaceManager.Instance == null
                || NetworkRaceManager.Instance.RoundState.Phase != RaceRoundPhase.Racing)
            {
                return false;
            }

            Vector3 collisionNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;
            if (!TryGetHazardForce(collision.collider, collisionNormal, out Vector3 force, out bool enableGravity))
            {
                return false;
            }

            motor.ApplyHit(force, enableGravity);
            predictedHazardLock = true;
            predictedHazardGraceRemaining = PredictedHitGraceDuration;
            predictedMovingState = false;
            predictedClimbingState = false;
            predictedFallingState = true;
            return true;
        }

        private void PublishAuthoritativeSnapshot(bool climbing, bool falling, int processedInputSequence)
        {
            if (!IsServer)
            {
                return;
            }

            authoritativeMotorSnapshot.Value = motor.CaptureSnapshot(
                climbing,
                falling,
                GetServerReceivedInputSequence(),
                processedInputSequence,
                snapshotRevision);
        }

        private void AdvanceSnapshotRevision()
        {
            snapshotRevision++;
        }

        private float GetPredictedDeltaTime()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null ? networkManager.LocalTime.FixedDeltaTime : Time.fixedDeltaTime;
        }

        private int GetLocalTick()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null ? networkManager.LocalTime.Tick : -1;
        }

        private float GetAuthoritativeDeltaTime()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null ? networkManager.ServerTime.FixedDeltaTime : Time.fixedDeltaTime;
        }

        private int GetServerTick()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null ? networkManager.ServerTime.Tick : -1;
        }

        private int GetServerProcessedInputSequence()
        {
            return lastAppliedInputSequence;
        }

        private int GetServerReceivedInputSequence()
        {
            return lastReceivedInputSequence;
        }

        private void BeginPredictionBootstrap()
        {
            if (!UsesOwnerPrediction)
            {
                return;
            }

            awaitingAuthoritativeBootstrap = true;
            predictedMovingState = false;
            predictedClimbingState = false;
            predictedFallingState = false;
            lastReplayWindowSize = 0;
            lastReplayWindowClamped = false;

            // Stop the server from continuing to consume the last pre-reset move intent
            // while the owner is waiting for the next authoritative revision snapshot.
            RunnerInputState bootstrapInputState = new RunnerInputState(false, localOwnerInputState.InputSequence);
            localOwnerInputState = bootstrapInputState;
            if (IsSpawned)
            {
                SubmitOwnerInputRpc(bootstrapInputState);
            }
        }

        private void ResetPredictionState()
        {
            int currentLocalTick = GetLocalTick();
            int currentServerTick = GetServerTick();

            rawOwnerMoveHeld = false;
            localOwnerInputState = new RunnerInputState(false, -1);
            serverAuthoritativeInputState = new RunnerInputState(false, -1);
            latestAuthoritativeSnapshot = default;
            hasAuthoritativeSnapshot = false;
            hasPendingAuthoritativeSnapshot = false;
            predictedMovingState = false;
            predictedFallingState = false;
            predictedClimbingState = false;
            predictedHazardLock = false;
            awaitingAuthoritativeBootstrap = UsesOwnerPrediction;
            warnedPendingBufferGrowth = false;
            predictedHazardGraceRemaining = 0f;
            nextLocalInputSequence = 0;
            lastAppliedAuthoritativeRevision = -1;
            lastReceivedInputSequence = -1;
            lastAppliedInputSequence = -1;
            lastProcessedInputSequence = -1;
            lastSampledLocalInputTick = currentLocalTick;
            lastPredictedSimulationTick = currentLocalTick;
            lastAuthoritativeSimulationTick = currentServerTick;
            lastReplayWindowSize = 0;
            lastReplayPositionError = 0f;
            lastReplayPathError = 0f;
            lastReplayWindowClamped = false;
            pendingOwnerInputs.Clear();
            snapshotRevision = 0;
        }
    }
}
