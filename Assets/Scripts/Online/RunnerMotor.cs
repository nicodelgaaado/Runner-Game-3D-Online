using System;
using UnityEngine;

namespace RunnerGame.Online
{
    [RequireComponent(typeof(Rigidbody))]
    public class RunnerMotor : MonoBehaviour
    {
        private const float RigidbodyDrag = 3f;
        private const float RigidbodyAngularDrag = 10f;
        private const float WalkableSurfaceMinUpDot = 0.55f;
        private const float SupportProbeUpDistance = 0.75f;
        private const float SupportProbeDownDistance = 2.5f;
        private const float SupportProbeHalfHeight = 0.05f;
        private const float SupportProbeInset = 0.05f;
        private const float GroundContactSkin = 0.02f;
        [SerializeField] private float speed = 12f;
        [SerializeField] private float fallingThreshold = -10f;

        private Rigidbody cachedRigidbody;
        private Collider cachedCollider;
        private readonly RaycastHit[] supportProbeHits = new RaycastHit[8];
        private float pathState;
        private bool lastSupportResolved;
        private float lastSupportNormalY;
        private string lastSupportColliderName = "n/a";
        private string lastSupportStatus = "n/a";

        public Rigidbody Rigidbody => cachedRigidbody != null ? cachedRigidbody : cachedRigidbody = GetComponent<Rigidbody>();
        private Collider PhysicsCollider => cachedCollider != null ? cachedCollider : cachedCollider = GetComponent<Collider>();
        public float FallingThreshold => fallingThreshold;
        public float Speed => speed;
        public float PathState => pathState;
        public bool HasGroundSupport => lastSupportResolved;
        public float LastSupportNormalY => lastSupportNormalY;
        public string LastSupportColliderName => lastSupportColliderName;
        public string LastSupportStatus => lastSupportStatus;

        private void Awake()
        {
            ConfigureRigidbody();
        }

        public void ConfigureRigidbody()
        {
            Rigidbody.linearDamping = RigidbodyDrag;
            Rigidbody.angularDamping = RigidbodyAngularDrag;
            ConfigureNetworkRolePresentation(isAuthoritativeInstance: false, isPredictedOwnerInstance: false);
            Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        }

        public void ConfigureNetworkRolePresentation(bool isAuthoritativeInstance, bool isPredictedOwnerInstance)
        {
            bool requiresPhysicsSimulation = isAuthoritativeInstance || isPredictedOwnerInstance;
            Rigidbody.isKinematic = !requiresPhysicsSimulation;
            Rigidbody.interpolation = requiresPhysicsSimulation ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            Rigidbody.collisionDetectionMode = requiresPhysicsSimulation
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.ContinuousSpeculative;

            if (!requiresPhysicsSimulation)
            {
                ClearPhysicsMotion();
            }
        }

        public void ResetForLevel(LevelCourseDefinition course)
        {
            pathState = 0f;
            Rigidbody.useGravity = true;
            ClearPhysicsMotion();
            Rigidbody.position = course.PathCreator.path.GetPointAtDistance(0f);
            Rigidbody.rotation = course.StartRotation;
            SetSupportDebug(resolved: false, status: "Reset", colliderName: "n/a", normalY: 0f);
        }

        public bool Tick(LevelCourseDefinition course, bool moveHeld, Action onFinish, float deltaTime)
        {
            if (!moveHeld)
            {
                return false;
            }

            pathState += speed * deltaTime;
            if (pathState > course.FinishDistance)
            {
                onFinish?.Invoke();
                return false;
            }

            Vector3 pathPosition = course.PathCreator.path.GetPointAtDistance(pathState);
            Vector3 nextPosition = course.PathCreator.path.GetPointAtDistance(Mathf.Min(pathState * 1.01f, course.FinishDistance));
            if (course.HasClimbSegment && pathState > course.ClimbStartDistance)
            {
                ClearPhysicsMotion();
                Rigidbody.useGravity = false;
                Rigidbody.MovePosition(pathPosition);
                SetSupportDebug(resolved: false, status: "Climb", colliderName: "n/a", normalY: 1f);
                return true;
            }

            Vector3 currentPosition = Rigidbody.position;
            bool hasSupport = TryResolveGroundedTarget(pathPosition, currentPosition, out Vector3 targetPosition);
            Vector3 targetForward = Vector3.ProjectOnPlane(nextPosition - pathPosition, Vector3.up);

            Rigidbody.useGravity = true;
            if (!hasSupport)
            {
                return false;
            }

            if (targetPosition.y >= currentPosition.y && Rigidbody.linearVelocity.y < 0f)
            {
                Vector3 linearVelocity = Rigidbody.linearVelocity;
                linearVelocity.y = 0f;
                Rigidbody.linearVelocity = linearVelocity;
            }

            Rigidbody.MovePosition(targetPosition);
            if (targetForward.sqrMagnitude > 0.0001f)
            {
                Rigidbody.MoveRotation(Quaternion.LookRotation(targetForward.normalized, Vector3.up));
            }

            return false;
        }

        public void ApplyHit(Vector3 force, bool enableGravity)
        {
            Rigidbody.angularVelocity = Vector3.zero;
            if (enableGravity)
            {
                Rigidbody.useGravity = true;
            }

            Rigidbody.AddForce(force);
        }

        public void ClearPhysicsMotion()
        {
            Rigidbody.linearVelocity = Vector3.zero;
            Rigidbody.angularVelocity = Vector3.zero;
        }

        private bool TryResolveGroundedTarget(Vector3 pathPosition, Vector3 currentPosition, out Vector3 groundedTargetPosition)
        {
            groundedTargetPosition = default;
            SetSupportDebug(resolved: false, status: "Unsupported", colliderName: "n/a", normalY: 0f);

            ObstacleManager obstacleManager = LegacySceneAdapter.ObstacleManager ?? UnityEngine.Object.FindAnyObjectByType<ObstacleManager>(FindObjectsInactive.Include);
            float pivotToBottomOffset = GetPivotToBottomOffset();
            Vector3 rootPosition = new Vector3(pathPosition.x, currentPosition.y, pathPosition.z);
            Vector3 castCenter = GetSupportProbeCenter(rootPosition, pivotToBottomOffset);
            Vector3 halfExtents = GetSupportProbeHalfExtents();
            float castDistance = SupportProbeUpDistance + SupportProbeDownDistance;

            int hitCount = Physics.BoxCastNonAlloc(
                castCenter,
                halfExtents,
                Vector3.down,
                supportProbeHits,
                Rigidbody.rotation,
                castDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
            {
                return false;
            }

            RaycastHit bestHit = default;
            float nearestDistance = float.MaxValue;
            bool rejectedBySlope = false;
            float steepestRejectedNormalY = -1f;
            string rejectedColliderName = "n/a";

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = supportProbeHits[i];
                if (!IsValidGroundHit(hit.collider, obstacleManager))
                {
                    continue;
                }

                float upDot = Vector3.Dot(hit.normal.normalized, Vector3.up);
                if (upDot < WalkableSurfaceMinUpDot)
                {
                    if (upDot > steepestRejectedNormalY)
                    {
                        steepestRejectedNormalY = upDot;
                        rejectedColliderName = hit.collider.name;
                        rejectedBySlope = true;
                    }

                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    bestHit = hit;
                }
            }

            if (nearestDistance == float.MaxValue)
            {
                if (rejectedBySlope)
                {
                    SetSupportDebug(resolved: false, status: "Steep", colliderName: rejectedColliderName, normalY: steepestRejectedNormalY);
                }

                return false;
            }

            groundedTargetPosition = new Vector3(pathPosition.x, bestHit.point.y + pivotToBottomOffset + GroundContactSkin, pathPosition.z);
            SetSupportDebug(resolved: true, status: "Grounded", colliderName: bestHit.collider.name, normalY: bestHit.normal.normalized.y);
            return true;
        }

        private Vector3 GetSupportProbeCenter(Vector3 rootPosition, float pivotToBottomOffset)
        {
            float footHeight = rootPosition.y - pivotToBottomOffset + SupportProbeHalfHeight;
            return new Vector3(rootPosition.x, footHeight + SupportProbeUpDistance, rootPosition.z);
        }

        private Vector3 GetSupportProbeHalfExtents()
        {
            Collider physicsCollider = PhysicsCollider;
            Vector3 boundsExtents = physicsCollider != null ? physicsCollider.bounds.extents : Vector3.one * 0.5f;
            float halfExtentX = Mathf.Max(0.05f, boundsExtents.x - SupportProbeInset);
            float halfExtentZ = Mathf.Max(0.05f, boundsExtents.z - SupportProbeInset);
            return new Vector3(halfExtentX, SupportProbeHalfHeight, halfExtentZ);
        }

        private bool IsValidGroundHit(Collider hitCollider, ObstacleManager obstacleManager)
        {
            if (hitCollider == null || hitCollider == PhysicsCollider || hitCollider.isTrigger)
            {
                return false;
            }

            if (hitCollider.GetComponentInParent<RunnerNetworkPlayer>() != null)
            {
                return false;
            }

            if (obstacleManager != null && obstacleManager.TryGetOnlineHazardResponse(hitCollider, out _))
            {
                return false;
            }

            return true;
        }

        private float GetPivotToBottomOffset()
        {
            Collider physicsCollider = PhysicsCollider;
            if (physicsCollider == null)
            {
                return 0f;
            }

            return Rigidbody.position.y - physicsCollider.bounds.min.y;
        }

        private void SetSupportDebug(bool resolved, string status, string colliderName, float normalY)
        {
            lastSupportResolved = resolved;
            lastSupportStatus = status;
            lastSupportColliderName = string.IsNullOrWhiteSpace(colliderName) ? "n/a" : colliderName;
            lastSupportNormalY = normalY;
        }
    }
}
