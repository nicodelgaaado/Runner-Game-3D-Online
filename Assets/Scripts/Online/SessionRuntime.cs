using Fusion;

namespace RunnerGame.Online
{
    public static class SessionRuntime
    {
        public static NetworkRunner Runner { get; private set; }
        public static string SessionCode { get; private set; } = string.Empty;
        public static ShutdownReason? LastShutdownReason { get; private set; }

        public static bool HasSession => Runner != null && Runner.IsRunning && Runner.IsInSession;

        public static void SetSession(NetworkRunner runner, string sessionCode)
        {
            Runner = runner;
            SessionCode = sessionCode ?? string.Empty;
            LastShutdownReason = null;
        }

        public static void SetShutdownReason(ShutdownReason reason)
        {
            LastShutdownReason = reason;
        }

        public static void Clear()
        {
            Runner = null;
            SessionCode = string.Empty;
            LastShutdownReason = null;
        }
    }
}
