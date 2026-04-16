using Unity.Services.Multiplayer;

namespace RunnerGame.Online
{
    public static class SessionRuntime
    {
        public static ISession Session { get; private set; }

        public static bool HasSession => Session != null;

        public static void SetSession(ISession session)
        {
            Session = session;
        }

        public static void Clear()
        {
            Session = null;
        }
    }
}
