using Legend.ClientCore;

namespace Legend.Tests;

public class PhaseGClientUiTests
{
    [Theory]
    [InlineData(SessionPhase.Disconnected, ClientUiMode.Login)]
    [InlineData(SessionPhase.Login, ClientUiMode.Login)]
    [InlineData(SessionPhase.Connecting, ClientUiMode.Login)]
    [InlineData(SessionPhase.Error, ClientUiMode.Login)]
    [InlineData(SessionPhase.CharSelect, ClientUiMode.CharSelect)]
    [InlineData(SessionPhase.EnteringWorld, ClientUiMode.CharSelect)]
    [InlineData(SessionPhase.InWorld, ClientUiMode.InWorld)]
    [InlineData(SessionPhase.Reconnecting, ClientUiMode.Reconnecting)]
    public void UiMode_Maps_From_Phase(SessionPhase phase, ClientUiMode expected)
    {
        Assert.Equal(expected, ClientUiModeResolver.FromPhase(phase));
    }

    [Fact]
    public void Protocol_Still_1_21_0()
    {
        Assert.Equal("1.21.0", Legend.Contracts.ProtocolConstants.CurrentVersion);
    }
}
