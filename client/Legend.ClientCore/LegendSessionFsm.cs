using System;

namespace Legend.ClientCore
{
    /// <summary>
    /// Pure state machine for Legend client session flow.
    /// Network I/O lives outside; Unity / tests drive transitions via these methods.
    /// </summary>
    public sealed class LegendSessionFsm
    {
        public SessionPhase Phase { get; private set; } = SessionPhase.Disconnected;
        public string StatusMessage { get; private set; } = "Idle";
        public string ErrorMessage { get; private set; } = "";
        public bool CanRetry => Phase is SessionPhase.Error or SessionPhase.Disconnected or SessionPhase.Reconnecting;

        public event Action<SessionPhase, SessionPhase>? PhaseChanged;

        public void Reset()
        {
            Transition(SessionPhase.Disconnected, "Idle");
            ErrorMessage = "";
        }

        public bool BeginConnect(string host, int port)
        {
            if (Phase is not (SessionPhase.Disconnected or SessionPhase.Error or SessionPhase.Reconnecting or SessionPhase.Login))
                return false;
            ErrorMessage = "";
            Transition(SessionPhase.Connecting, $"Connecting {host}:{port}…");
            return true;
        }

        public void MarkLoginReady()
        {
            if (Phase is SessionPhase.Connecting or SessionPhase.Reconnecting)
                Transition(SessionPhase.Login, "Enter account");
        }

        public void MarkAuthenticated()
        {
            if (Phase is SessionPhase.Connecting or SessionPhase.Login or SessionPhase.Reconnecting)
                Transition(SessionPhase.CharSelect, "Select or create character");
        }

        public void BeginEnterWorld(string characterName)
        {
            if (Phase != SessionPhase.CharSelect)
                return;
            Transition(SessionPhase.EnteringWorld, $"Entering world as {characterName}…");
        }

        public void MarkInWorld(string mapId)
        {
            if (Phase is SessionPhase.EnteringWorld or SessionPhase.CharSelect)
                Transition(SessionPhase.InWorld, $"In world ({mapId})");
        }

        /// <summary>Enter failed or cancelled; stay logged in on the character screen.</summary>
        public void AbortEnterWorld(string message)
        {
            if (Phase != SessionPhase.EnteringWorld)
                return;
            ErrorMessage = message ?? "";
            Transition(SessionPhase.CharSelect, string.IsNullOrWhiteSpace(message) ? "Select or create character" : message);
        }

        public void MarkDisconnected(bool expectReconnect)
        {
            if (Phase == SessionPhase.Disconnected)
                return;
            if (expectReconnect)
                Transition(SessionPhase.Reconnecting, "Disconnected — tap Reconnect");
            else
                Transition(SessionPhase.Disconnected, "Disconnected");
        }

        public void Fail(string error)
        {
            ErrorMessage = error ?? "Unknown error";
            Transition(SessionPhase.Error, "Error");
        }

        public void SetStatus(string status) => StatusMessage = status ?? "";

        private void Transition(SessionPhase next, string status)
        {
            var prev = Phase;
            Phase = next;
            StatusMessage = status;
            if (prev != next)
                PhaseChanged?.Invoke(prev, next);
        }
    }
}
