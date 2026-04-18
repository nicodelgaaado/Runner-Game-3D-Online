using Fusion;
using UnityEngine;

namespace RunnerGame.Online
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
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
        private const float RespawnDelaySeconds = 2f;

        [Networked] private float PathStateValue { get; set; }
        [Networked] private Vector3 NetworkPosition { get; set; }
        [Networked] private Quaternion NetworkRotation { get; set; }
        [Networked] private NetworkBool MovingState { get; set; }
        [Networked] private NetworkBool FallingState { get; set; }
        [Networked] private NetworkBool ClimbingState { get; set; }
        [Networked] private TickTimer RespawnTimer { get; set; }
        [Networked] private PlayerRef ReplicatedOwnerPlayer { get; set; }
        [Networked] private RunnerSpawnSlot ReplicatedSpawnSlot { get; set; }

        private RunnerMotor motor;
        private RunnerInputAdapter inputAdapter;
        private RunnerPresentation presentation;
        private LocalPlayerCameraBinder cameraBinder;
        private RaceRoundState lastObservedRoundState;
        private RunnerSpawnSlot lastVisualSlot = RunnerSpawnSlot.None;
        private bool finishReportedForRound;
        private bool initialPlacementApplied;
        private bool warnedMissingReplicatedSlot;
        private bool warnedMissingVisualPrototype;

        public static RunnerNetworkPlayer LocalPlayer { get; private set; }

        public RunnerSpawnSlot SpawnSlot => ReplicatedSpawnSlot;
        public PlayerRef OwnerPlayer => ReplicatedOwnerPlayer;
        public PlayerRef ReplicatedOwnerPlayerRef => ReplicatedOwnerPlayer;
        public RunnerSpawnSlot ReplicatedSpawnSlotValue => ReplicatedSpawnSlot;
        public PlayerRef InputAuthorityPlayer => Object != null ? Object.InputAuthority : PlayerRef.None;
        public PlayerRef StateAuthorityPlayer => Object != null ? Object.StateAuthority : PlayerRef.None;
        public bool IsLocalControlled => Object != null && Runner != null && (HasInputAuthority || Object.InputAuthority == Runner.LocalPlayer);
        public bool IsSceneAuthorityRole => Runner != null && Runner.IsSceneAuthority;
        public float LocalPositionY => motor != null ? motor.Rigidbody.position.y : 0f;
        public bool HasGroundSupport => motor != null && motor.HasGroundSupport;
        public string GroundSupportColliderName => motor != null ? motor.LastSupportColliderName : "n/a";
        public string GroundSupportStatus => motor != null ? motor.LastSupportStatus : "n/a";
        public float GroundSupportNormalY => motor != null ? motor.LastSupportNormalY : 0f;
        public float ReplicatedPathState => PathStateValue;
        public bool IsRespawning => RespawnTimer.IsRunning;
        public bool HasActiveCameraBinding => cameraBinder != null && cameraBinder.HasActiveCameraBinding;

        private void Awake()
        {
            motor = GetComponent<RunnerMotor>();
            inputAdapter = GetComponent<RunnerInputAdapter>();
            presentation = GetComponent<RunnerPresentation>();
            cameraBinder = GetComponent<LocalPlayerCameraBinder>();
        }

        public override void Spawned()
        {
            lastObservedRoundState = RaceRoundState.WaitingForPlayers;
            initialPlacementApplied = false;
            EnsureReplicatedIdentityInitialized();

            if (IsLocalControlled)
            {
                LocalPlayer = this;
                cameraBinder.enabled = true;
                PlayerRef owner = OwnerPlayer;
                if (!Runner.IsPlayerValid(owner) && Runner != null && Runner.IsPlayerValid(Runner.LocalPlayer))
                {
                    owner = Runner.LocalPlayer;
                }

                if (Runner.IsPlayerValid(owner))
                {
                    Runner.SetPlayerObject(owner, Object);
                }
            }
            else
            {
                cameraBinder.enabled = false;
            }

            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: HasStateAuthority, isPredictedOwnerInstance: false);
            ConfigurePlayerCollisionLayers();
            IgnoreOtherPlayerCollisions();
            TryApplyInitialPlacement();
            RefreshPresentation();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (LocalPlayer == this)
            {
                cameraBinder.Release();
                LocalPlayer = null;
            }

            lastVisualSlot = RunnerSpawnSlot.None;
            initialPlacementApplied = false;
        }

        public override void FixedUpdateNetwork()
        {
            EnsureReplicatedIdentityInitialized();
            TryApplyInitialPlacement();

            if (NetworkRaceManager.Instance == null)
            {
                return;
            }

            RaceRoundState roundState = NetworkRaceManager.Instance.RoundState;
            if (roundState.LevelIndex != lastObservedRoundState.LevelIndex || roundState.Phase != lastObservedRoundState.Phase)
            {
                HandleRoundStateChanged(roundState);
                lastObservedRoundState = roundState;
            }

            if (!HasStateAuthority)
            {
                return;
            }

            LevelCourseDefinition currentCourse = NetworkRaceManager.Instance.GetCurrentCourse();
            if (currentCourse == null)
            {
                return;
            }

            if (RespawnTimer.IsRunning && RespawnTimer.Expired(Runner))
            {
                ResetForCurrentRound(currentCourse);
            }

            if (RespawnTimer.IsRunning || roundState.Phase != RaceRoundPhase.Racing)
            {
                MovingState = false;
                ClimbingState = false;
                SyncNetworkStateFromMotor();
                return;
            }

            RunnerInputState inputState = default;
            bool moveHeld = GetInput(out inputState) && inputState.MoveHeld;

            bool climbing = motor.Tick(currentCourse, SpawnSlot, moveHeld, NotifyFinishedIfNeeded, Runner.DeltaTime);
            ClimbingState = climbing;
            MovingState = moveHeld && !climbing;

            bool falling = motor.Rigidbody.linearVelocity.y < motor.FallingThreshold;
            if (falling)
            {
                StartRespawn();
            }

            FallingState = falling || RespawnTimer.IsRunning;
            if (FallingState)
            {
                MovingState = false;
                ClimbingState = false;
            }

            SyncNetworkStateFromMotor();
        }

        public override void Render()
        {
            if (!HasStateAuthority)
            {
                ApplyRemoteTransform();
            }

            RunnerSpawnSlot slot = SpawnSlot;
            if (slot != lastVisualSlot)
            {
                presentation.ApplyVisuals(slot);
                if (IsLocalControlled)
                {
                    cameraBinder.Bind(transform, slot);
                }

                lastVisualSlot = slot;
                ConfigurePlayerCollisionLayers();
            }

            ReportPresentationWarnings(slot);
            RefreshPresentation();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!HasStateAuthority || NetworkRaceManager.Instance == null || NetworkRaceManager.Instance.RoundState.Phase != RaceRoundPhase.Racing || RespawnTimer.IsRunning)
            {
                return;
            }

            Vector3 collisionNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;
            if (!TryGetHazardForce(collision.collider, collisionNormal, out Vector3 force, out bool enableGravity))
            {
                return;
            }

            motor.ApplyHit(force, enableGravity);
            FallingState = true;
            MovingState = false;
            ClimbingState = false;
            StartRespawn();
            SyncNetworkStateFromMotor();
        }

        private void RefreshPresentation()
        {
            bool isWinner = NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsPlayerWinner(this);
            bool isLoser = NetworkRaceManager.Instance != null && NetworkRaceManager.Instance.IsPlayerLoser(this);

            presentation.UpdateAnimationState(
                SpawnSlot,
                MovingState,
                FallingState,
                ClimbingState,
                isWinner,
                isLoser);
        }

        private void HandleRoundStateChanged(RaceRoundState roundState)
        {
            finishReportedForRound = false;

            if (roundState.Phase == RaceRoundPhase.Racing && NetworkRaceManager.Instance != null)
            {
                LevelCourseDefinition course = NetworkRaceManager.Instance.GetCurrentCourse();
                if (course != null)
                {
                    ResetForCurrentRound(course);
                }
            }
            else if (HasStateAuthority)
            {
                motor.ClearPhysicsMotion();
                MovingState = false;
                ClimbingState = false;
                FallingState = false;
                SyncNetworkStateFromMotor();
            }
        }

        private void ResetForCurrentRound(LevelCourseDefinition course)
        {
            EnsureReplicatedIdentityInitialized();
            ApplyCourseStartPlacement(course);
            RespawnTimer = TickTimer.None;
            MovingState = false;
            FallingState = false;
            ClimbingState = false;
            PathStateValue = 0f;
            finishReportedForRound = false;
            SyncNetworkStateFromMotor();
        }

        private void NotifyFinishedIfNeeded()
        {
            if (finishReportedForRound || NetworkRaceManager.Instance == null)
            {
                return;
            }

            PlayerRef owner = OwnerPlayer;
            if (!Runner.IsPlayerValid(owner))
            {
                return;
            }

            finishReportedForRound = true;
            MovingState = false;
            ClimbingState = false;
            NetworkRaceManager.Instance.RPC_ReportPlayerFinished(owner, motor.PathState);
        }

        private void StartRespawn()
        {
            if (!RespawnTimer.IsRunning)
            {
                RespawnTimer = TickTimer.CreateFromSeconds(Runner, RespawnDelaySeconds);
            }
        }

        private void SyncNetworkStateFromMotor()
        {
            PathStateValue = motor.PathState;
            NetworkPosition = motor.Rigidbody.position;
            NetworkRotation = SanitizeQuaternion(motor.Rigidbody.rotation);
        }

        private void ApplyRemoteTransform()
        {
            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: false, isPredictedOwnerInstance: false);
            Quaternion sanitizedRotation = SanitizeQuaternion(NetworkRotation, transform.rotation);
            transform.SetPositionAndRotation(NetworkPosition, sanitizedRotation);
            motor.Rigidbody.position = NetworkPosition;
            motor.Rigidbody.rotation = sanitizedRotation;
        }

        private void ConfigurePlayerCollisionLayers()
        {
            int redCollisionLayer = LayerMask.NameToLayer(RedCollisionLayerName);
            int blueCollisionLayer = LayerMask.NameToLayer(BlueCollisionLayerName);
            RunnerSpawnSlot slot = SpawnSlot;

            if (slot != RunnerSpawnSlot.None && redCollisionLayer >= 0 && blueCollisionLayer >= 0)
            {
                Physics.IgnoreLayerCollision(redCollisionLayer, blueCollisionLayer, true);
                gameObject.layer = slot == RunnerSpawnSlot.Blue ? blueCollisionLayer : redCollisionLayer;
            }
        }

        private void IgnoreOtherPlayerCollisions()
        {
            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            foreach (RunnerNetworkPlayer other in UnityEngine.Object.FindObjectsByType<RunnerNetworkPlayer>(FindObjectsInactive.Exclude))
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

            ObstacleManager obstacleManager = LegacySceneAdapter.ObstacleManager ?? UnityEngine.Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);
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

        private void TryApplyInitialPlacement()
        {
            if (initialPlacementApplied || !HasStateAuthority)
            {
                return;
            }

            EnsureReplicatedIdentityInitialized();
            LevelCourseDefinition course = ResolveCurrentCourse();
            if (course == null || course.PathCreator == null)
            {
                return;
            }

            ApplyCourseStartPlacement(course);
            if (!initialPlacementApplied)
            {
                return;
            }

            SyncNetworkStateFromMotor();
        }

        private LevelCourseDefinition ResolveCurrentCourse()
        {
            if (NetworkRaceManager.Instance != null)
            {
                LevelCourseDefinition activeCourse = NetworkRaceManager.Instance.GetCurrentCourse();
                if (activeCourse != null)
                {
                    return activeCourse;
                }

                return FindCourseByLevel(NetworkRaceManager.Instance.RoundState.LevelIndex);
            }

            return FindCourseByLevel(RaceRoundState.WaitingForPlayers.LevelIndex);
        }

        private static LevelCourseDefinition FindCourseByLevel(int levelIndex)
        {
            foreach (LevelCourseDefinition course in LegacySceneAdapter.BuildCourses())
            {
                if (course != null && course.LevelIndex == levelIndex)
                {
                    return course;
                }
            }

            return null;
        }

        private void ApplyCourseStartPlacement(LevelCourseDefinition course)
        {
            if (course == null || SpawnSlot == RunnerSpawnSlot.None)
            {
                return;
            }

            motor.ResetForLevel(course, SpawnSlot);
            initialPlacementApplied = true;
        }

        private void EnsureReplicatedIdentityInitialized()
        {
            if (!HasStateAuthority || Runner == null || Object == null)
            {
                return;
            }

            if (Runner.IsPlayerValid(ReplicatedOwnerPlayer) && ReplicatedSpawnSlot != RunnerSpawnSlot.None)
            {
                return;
            }

            PlayerRef resolvedOwner = ResolveAuthoritativeOwner();
            if (!Runner.IsPlayerValid(resolvedOwner))
            {
                return;
            }

            ReplicatedOwnerPlayer = resolvedOwner;
            ReplicatedSpawnSlot = ResolveSlotForOwner(resolvedOwner);
        }

        private void ReportPresentationWarnings(RunnerSpawnSlot slot)
        {
            if (!Debug.isDebugBuild)
            {
                return;
            }

            if (slot == RunnerSpawnSlot.None)
            {
                if (!warnedMissingReplicatedSlot)
                {
                    Debug.LogWarning(
                        $"[RunnerNetworkPlayer] Rendered without replicated slot. object={name} owner={OwnerPlayer} inputAuthority={InputAuthorityPlayer} stateAuthority={StateAuthorityPlayer} hasStateAuthority={HasStateAuthority} hasInputAuthority={HasInputAuthority}");
                    warnedMissingReplicatedSlot = true;
                }
            }
            else
            {
                warnedMissingReplicatedSlot = false;
            }

            if (slot != RunnerSpawnSlot.None && presentation != null && !presentation.HasActiveVisual)
            {
                if (!warnedMissingVisualPrototype)
                {
                    Debug.LogWarning(
                        $"[RunnerNetworkPlayer] Missing visual prototype for slot {slot}. object={name} owner={OwnerPlayer} inputAuthority={InputAuthorityPlayer} stateAuthority={StateAuthorityPlayer}");
                    warnedMissingVisualPrototype = true;
                }
            }
            else
            {
                warnedMissingVisualPrototype = false;
            }
        }

        private PlayerRef ResolveAuthoritativeOwner()
        {
            if (Runner == null || Object == null)
            {
                return PlayerRef.None;
            }

            if (HasInputAuthority && Runner.IsPlayerValid(Runner.LocalPlayer))
            {
                return Runner.LocalPlayer;
            }

            PlayerRef inputAuthority = Object.InputAuthority;
            if (Runner.IsPlayerValid(inputAuthority))
            {
                return inputAuthority;
            }

            PlayerRef stateAuthority = Object.StateAuthority;
            if (Runner.IsPlayerValid(stateAuthority))
            {
                return stateAuthority;
            }

            if (stateAuthority.IsMasterClient)
            {
                PlayerRef masterClient = Runner.GetMasterClient();
                if (Runner.IsPlayerValid(masterClient))
                {
                    return masterClient;
                }
            }

            return PlayerRef.None;
        }

        private RunnerSpawnSlot ResolveSlotForOwner(PlayerRef owner)
        {
            if (Runner == null || !Runner.IsPlayerValid(owner))
            {
                return RunnerSpawnSlot.None;
            }

            PlayerRef masterClient = Runner.GetMasterClient();
            if (!Runner.IsPlayerValid(masterClient))
            {
                return RunnerSpawnSlot.None;
            }

            return owner == masterClient ? RunnerSpawnSlot.Blue : RunnerSpawnSlot.Red;
        }

        private static Quaternion SanitizeQuaternion(Quaternion rotation)
        {
            return SanitizeQuaternion(rotation, Quaternion.identity);
        }

        private static Quaternion SanitizeQuaternion(Quaternion rotation, Quaternion fallback)
        {
            float magnitude = Mathf.Sqrt((rotation.x * rotation.x) + (rotation.y * rotation.y) + (rotation.z * rotation.z) + (rotation.w * rotation.w));
            if (magnitude < 0.0001f || float.IsNaN(magnitude) || float.IsInfinity(magnitude))
            {
                return fallback;
            }

            float inverseMagnitude = 1f / magnitude;
            return new Quaternion(
                rotation.x * inverseMagnitude,
                rotation.y * inverseMagnitude,
                rotation.z * inverseMagnitude,
                rotation.w * inverseMagnitude);
        }
    }
}
