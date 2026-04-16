using System;
using Unity.Netcode;
using UnityEngine;

namespace RunnerGame.Online
{
    public enum RaceRoundPhase : byte
    {
        WaitingForPlayers = 0,
        Countdown = 1,
        Racing = 2,
        RoundResult = 3,
        MatchComplete = 4
    }

    public enum RunnerSpawnSlot : int
    {
        None = -1,
        Red = 0,
        Blue = 1
    }

    public struct RunnerInputState : INetworkSerializable, IEquatable<RunnerInputState>
    {
        public bool MoveHeld;
        public int InputSequence;

        public RunnerInputState(bool moveHeld, int inputSequence)
        {
            MoveHeld = moveHeld;
            InputSequence = inputSequence;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MoveHeld);
            serializer.SerializeValue(ref InputSequence);
        }

        public bool Equals(RunnerInputState other)
        {
            return MoveHeld == other.MoveHeld && InputSequence == other.InputSequence;
        }

        public override bool Equals(object obj)
        {
            return obj is RunnerInputState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MoveHeld, InputSequence);
        }
    }

    public struct RunnerMotorSnapshot : INetworkSerializable, IEquatable<RunnerMotorSnapshot>
    {
        public float PathState;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public bool UseGravity;
        public bool Climbing;
        public bool Falling;
        public int ReceivedInputSequence;
        public int ProcessedInputSequence;
        public int Revision;

        public RunnerMotorSnapshot(
            float pathState,
            Vector3 position,
            Quaternion rotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            bool useGravity,
            bool climbing,
            bool falling,
            int receivedInputSequence,
            int processedInputSequence,
            int revision)
        {
            PathState = pathState;
            Position = position;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
            UseGravity = useGravity;
            Climbing = climbing;
            Falling = falling;
            ReceivedInputSequence = receivedInputSequence;
            ProcessedInputSequence = processedInputSequence;
            Revision = revision;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PathState);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref LinearVelocity);
            serializer.SerializeValue(ref AngularVelocity);
            serializer.SerializeValue(ref UseGravity);
            serializer.SerializeValue(ref Climbing);
            serializer.SerializeValue(ref Falling);
            serializer.SerializeValue(ref ReceivedInputSequence);
            serializer.SerializeValue(ref ProcessedInputSequence);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(RunnerMotorSnapshot other)
        {
            return PathState.Equals(other.PathState)
                && Position.Equals(other.Position)
                && Rotation.Equals(other.Rotation)
                && LinearVelocity.Equals(other.LinearVelocity)
                && AngularVelocity.Equals(other.AngularVelocity)
                && UseGravity == other.UseGravity
                && Climbing == other.Climbing
                && Falling == other.Falling
                && ReceivedInputSequence == other.ReceivedInputSequence
                && ProcessedInputSequence == other.ProcessedInputSequence
                && Revision == other.Revision;
        }

        public override bool Equals(object obj)
        {
            return obj is RunnerMotorSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(PathState);
            hash.Add(Position);
            hash.Add(Rotation);
            hash.Add(LinearVelocity);
            hash.Add(AngularVelocity);
            hash.Add(UseGravity);
            hash.Add(Climbing);
            hash.Add(Falling);
            hash.Add(ReceivedInputSequence);
            hash.Add(ProcessedInputSequence);
            hash.Add(Revision);
            return hash.ToHashCode();
        }
    }

    public struct RaceRoundState : INetworkSerializable, IEquatable<RaceRoundState>
    {
        public const ulong NoWinner = ulong.MaxValue;

        public int LevelIndex;
        public RaceRoundPhase Phase;
        public double RoundStartServerTime;
        public ulong WinnerClientId;

        public RaceRoundState(int levelIndex, RaceRoundPhase phase, double roundStartServerTime, ulong winnerClientId = NoWinner)
        {
            LevelIndex = levelIndex;
            Phase = phase;
            RoundStartServerTime = roundStartServerTime;
            WinnerClientId = winnerClientId;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LevelIndex);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref RoundStartServerTime);
            serializer.SerializeValue(ref WinnerClientId);
        }

        public bool Equals(RaceRoundState other)
        {
            return LevelIndex == other.LevelIndex
                && Phase == other.Phase
                && RoundStartServerTime.Equals(other.RoundStartServerTime)
                && WinnerClientId == other.WinnerClientId;
        }

        public override bool Equals(object obj)
        {
            return obj is RaceRoundState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(LevelIndex, (int)Phase, RoundStartServerTime, WinnerClientId);
        }
    }
}
