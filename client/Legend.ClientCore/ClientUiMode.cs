namespace Legend.ClientCore
{
    /// <summary>Which top-level UI shell the client should show for a session phase.</summary>
    public enum ClientUiMode
    {
        Login,
        CharSelect,
        InWorld,
        Reconnecting,
        Hidden
    }

    public static class ClientUiModeResolver
    {
        public static ClientUiMode FromPhase(SessionPhase phase) => phase switch
        {
            SessionPhase.Disconnected or SessionPhase.Login or SessionPhase.Connecting or SessionPhase.Error
                => ClientUiMode.Login,
            SessionPhase.CharSelect or SessionPhase.EnteringWorld
                => ClientUiMode.CharSelect,
            SessionPhase.InWorld
                => ClientUiMode.InWorld,
            SessionPhase.Reconnecting
                => ClientUiMode.Reconnecting,
            _ => ClientUiMode.Hidden
        };
    }
}
