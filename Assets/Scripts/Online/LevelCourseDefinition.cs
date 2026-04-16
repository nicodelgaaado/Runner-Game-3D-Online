using System;
using PathCreation;
using UnityEngine;

namespace RunnerGame.Online
{
    [Serializable]
    public class LevelCourseDefinition
    {
        public int LevelIndex;
        public PathCreator PathCreator;
        public float FinishDistance;
        public Vector3 StartFacingEuler;
        public bool HasClimbSegment;
        public float ClimbStartDistance;

        public Quaternion StartRotation => Quaternion.Euler(StartFacingEuler);
    }

    [Serializable]
    public struct RunnerSpawnConfig
    {
        public RunnerSpawnSlot Slot;
        public string DisplayName;
    }
}
