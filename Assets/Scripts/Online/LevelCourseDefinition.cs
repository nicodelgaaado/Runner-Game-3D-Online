using System;
using PathCreation;
using UnityEngine;

namespace RunnerGame.Online
{
    [Serializable]
    public class LevelCourseDefinition
    {
        private const float DefaultOnlineLaneHalfWidth = 0.9f;

        public int LevelIndex;
        public PathCreator PathCreator;
        public float FinishDistance;
        public Vector3 StartFacingEuler;
        public bool HasClimbSegment;
        public float ClimbStartDistance;
        public float OnlineLaneHalfWidth = DefaultOnlineLaneHalfWidth;

        public Quaternion StartRotation => Quaternion.Euler(StartFacingEuler);

        public Vector3 GetOnlinePathPosition(float distance, RunnerSpawnSlot slot)
        {
            return GetOnlinePathPosition(distance, slot, out _);
        }

        public Vector3 GetOnlinePathPosition(float distance, RunnerSpawnSlot slot, out Vector3 horizontalForward)
        {
            horizontalForward = GetOnlineForward(distance);
            if (PathCreator == null || PathCreator.path == null)
            {
                return Vector3.zero;
            }

            Vector3 centerlinePosition = PathCreator.path.GetPointAtDistance(distance);
            Vector3 lateralDirection = Vector3.Cross(Vector3.up, horizontalForward);
            if (lateralDirection.sqrMagnitude < 0.0001f)
            {
                lateralDirection = Vector3.right;
            }

            lateralDirection.Normalize();
            return centerlinePosition + (lateralDirection * GetSignedLaneOffset(slot));
        }

        public Vector3 GetOnlineForward(float distance)
        {
            if (PathCreator != null && PathCreator.path != null)
            {
                Vector3 tangent = Vector3.ProjectOnPlane(PathCreator.path.GetDirectionAtDistance(distance), Vector3.up);
                if (tangent.sqrMagnitude > 0.0001f)
                {
                    return tangent.normalized;
                }
            }

            Vector3 fallbackForward = Vector3.ProjectOnPlane(StartRotation * Vector3.forward, Vector3.up);
            return fallbackForward.sqrMagnitude > 0.0001f
                ? fallbackForward.normalized
                : Vector3.forward;
        }

        private float GetSignedLaneOffset(RunnerSpawnSlot slot)
        {
            float laneHalfWidth = Mathf.Abs(OnlineLaneHalfWidth);
            return slot switch
            {
                RunnerSpawnSlot.Red => -laneHalfWidth,
                RunnerSpawnSlot.Blue => laneHalfWidth,
                _ => 0f
            };
        }
    }

    [Serializable]
    public struct RunnerSpawnConfig
    {
        public RunnerSpawnSlot Slot;
        public string DisplayName;
    }
}
