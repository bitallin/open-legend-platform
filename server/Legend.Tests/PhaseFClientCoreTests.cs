using Legend.ClientCore;

namespace Legend.Tests;

public class PhaseFClientCoreTests
{
    [Fact]
    public void Fsm_LoginFlow_Transitions()
    {
        var fsm = new LegendSessionFsm();
        Assert.Equal(SessionPhase.Disconnected, fsm.Phase);

        Assert.True(fsm.BeginConnect("127.0.0.1", 7100));
        Assert.Equal(SessionPhase.Connecting, fsm.Phase);

        fsm.MarkAuthenticated();
        Assert.Equal(SessionPhase.CharSelect, fsm.Phase);

        fsm.BeginEnterWorld("旅人");
        Assert.Equal(SessionPhase.EnteringWorld, fsm.Phase);

        fsm.MarkInWorld("map_village");
        Assert.Equal(SessionPhase.InWorld, fsm.Phase);
        Assert.Contains("map_village", fsm.StatusMessage);
    }

    [Fact]
    public void Fsm_DisconnectFromWorld_OffersReconnect()
    {
        var fsm = new LegendSessionFsm();
        fsm.BeginConnect("h", 1);
        fsm.MarkAuthenticated();
        fsm.BeginEnterWorld("A");
        fsm.MarkInWorld("m");
        fsm.MarkDisconnected(expectReconnect: true);
        Assert.Equal(SessionPhase.Reconnecting, fsm.Phase);
        Assert.True(fsm.CanRetry);
    }

    [Fact]
    public void Fsm_Fail_SetsError()
    {
        var fsm = new LegendSessionFsm();
        fsm.BeginConnect("h", 1);
        fsm.Fail("boom");
        Assert.Equal(SessionPhase.Error, fsm.Phase);
        Assert.Equal("boom", fsm.ErrorMessage);
        Assert.True(fsm.CanRetry);
    }

    [Fact]
    public void ChatLogBuffer_RespectsCapacity()
    {
        var buf = new ChatLogBuffer(3);
        buf.Add("a");
        buf.Add("b");
        buf.Add("c");
        buf.Add("d");
        Assert.Equal(3, buf.Lines.Count);
        Assert.DoesNotContain("a", buf.Lines);
        Assert.Contains("d", buf.Lines);
    }

    [Fact]
    public void InventoryViewModel_ReplaceFiltersEmpty()
    {
        var inv = new InventoryViewModel();
        inv.Replace(new[] { ("hp_potion", 3), ("", 1), ("ore", 0), ("sword", 1) }, 99);
        Assert.Equal(2, inv.Items.Count);
        Assert.Equal(99, inv.Gold);
        Assert.Equal("hp_potion", inv.Items[0].ItemId);
        Assert.Equal(3, inv.Items[0].Count);
    }

    [Fact]
    public void ProtocolConstants_UnitySdk_Is_1_21_0()
    {
        // Guard: ClientCore tests sit next to Contracts — keep server/Unity version aligned.
        Assert.Equal("1.21.0", Legend.Contracts.ProtocolConstants.CurrentVersion);
    }
}
