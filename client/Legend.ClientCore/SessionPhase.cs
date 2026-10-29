namespace Legend.ClientCore
{
    /// <summary>High-level client session phases for the Unity MVP login → world flow.</summary>
    public enum SessionPhase
    {
        Disconnected = 0,
        Connecting = 1,
        Login = 2,
        CharSelect = 3,
        EnteringWorld = 4,
        InWorld = 5,
        Reconnecting = 6,
        Error = 7
    }
}
