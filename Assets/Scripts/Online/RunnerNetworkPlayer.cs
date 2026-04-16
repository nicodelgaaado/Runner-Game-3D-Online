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

        private RunnerMotor motor;
        private RunnerInputAdapter inputAdapter;
        private RunnerPresentation presentation;
        private LocalPlayerCameraBinder cameraBinder;
        private RaceRoundState lastObservedRoundState;
        private RunnerSpawnSlot lastVisualSlot = RunnerSpawnSlot.None;
        private bool finishReportedForRound;

        public static RunnerNetworkPlayer LocalPlayer { get; private set; }

        public RunnerSpawnSlot SpawnSlot
        {
            get
            {
                if (Runner == null || Object == null)
                {
                    return RunnerSpawnSlot.None;
                }

                PlayerRef owner = OwnerPlayer;
                if (!Runner.IsPlayerValid(owner))
                {
                    return RunnerSpawnSlot.None;
                }

                return owner == Runner.GetMasterClient() ? RunnerSpawnSlot.Blue : RunnerSpawnSlot.Red;
            }
        }

        public PlayerRef OwnerPlayer => Object != null ? Object.StateAuthority : PlayerRef.None;
        public bool IsLocalControlled => Object != null && Object.StateAuthority == Runner.LocalPlayer;
        public bool IsSceneAuthorityRole => Runner != null && Runner.IsSceneAuthority;
        public float LocalPositionY => motor != null ? motor.Rigidbody.position.y : 0f;
        public bool HasGroundSupport => motor != null && motor.HasGroundSupport;
        public string GroundSupportColliderName => motor != null ? motor.LastSupportColliderName : "n/a";
        public string GroundSupportStatus => motor != null ? motor.LastSupportStatus : "n/a";
        public float GroundSupportNormalY => motor != null ? motor.LastSupportNormalY : 0f;
        public float ReplicatedPathState => PathStateValue;
        public bool IsRespawning => RespawnTimer.IsRunning;

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

            if (IsLocalControlled)
            {
                LocalPlayer = this;
                cameraBinder.enabled = true;
            }
            else
            {
                cameraBinder.enabled = false;
            }

            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: HasStateAuthority, isPredictedOwnerInstance: false);
            ConfigurePlayerCollisionLayers();
            IgnoreOtherPlayerCollisions();
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
        }

        public override void FixedUpdateNetwork()
        {
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

            bool climbing = motor.Tick(currentCourse, moveHeld, NotifyFinishedIfNeeded, Runner.DeltaTime);
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
            motor.ResetForLevel(course);
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

            finishReportedForRound = true;
            MovingState = false;
            ClimbingState = false;
            NetworkRaceManager.Instance.RPC_ReportPlayerFinished(OwnerPlayer, motor.PathState);
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
            NetworkRotation = motor.Rigidbody.rotation;
        }

        private void ApplyRemoteTransform()
        {
            motor.ConfigureNetworkRolePresentation(isAuthoritativeInstance: false, isPredictedOwnerInstance: false);
            transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);
            motor.Rigidbody.position = NetworkPosition;
            motor.Rigidbody.rotation = NetworkRotation;
        }

        private void ConfigurePlayerCollisionLayers()
        {
            int redCollisionLayer = LayerMask.NameToLayer(RedCollisionLayerName);
            int blueCollisionLayer = LayerMask.NameToLayer(BlueCollisionLayerName);

            if (redCollisionLayer >= 0 && blueCollisionLayer >= 0)
            {
                Physics.IgnoreLayerCollision(redCollisionLayer, blueCollisionLayer, true);
                gameObject.layer = SpawnSlot == RunnerSpawnSlot.Blue ? blueCollisionLayer : redCollisionLayer;
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
    }
}
