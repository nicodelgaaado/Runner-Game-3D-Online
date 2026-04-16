using Fusion;

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

    public enum RunnerSpawnSlot : byte
    {
        None = 0,
        Red = 1,
        Blue = 2
    }

    public struct RunnerInputState : INetworkInput
    {
        public NetworkBool MoveHeld;

        public RunnerInputState(bool moveHeld)
        {
            MoveHeld = moveHeld;
        }
    }

    public struct RaceRoundState : INetworkStruct
    {
        public int LevelIndex;
        public RaceRoundPhase Phase;
        public int RoundStartTick;
        public PlayerRef WinnerPlayer;

        public RaceRoundState(int levelIndex, RaceRoundPhase phase, int roundStartTick, PlayerRef winnerPlayer = default)
        {
            LevelIndex = levelIndex;
            Phase = phase;
            RoundStartTick = roundStartTick;
            WinnerPlayer = winnerPlayer;
        }

        public static RaceRoundState WaitingForPlayers =>
            new(1, RaceRoundPhase.WaitingForPlayers, 0, PlayerRef.None);
    }
}
