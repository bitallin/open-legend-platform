using Legend.ClientCore;
using Xunit;

namespace Legend.Tests;

public class MovePredictionTests
{
    [Fact]
    public void Begin_StoresGridOnly_AndMarksPending()
    {
        var p = new MovePrediction();
        p.Begin(10, 20, 11, 20);
        Assert.True(p.HasPending);
        Assert.Equal(10, p.AnchorX);
        Assert.Equal(20, p.AnchorY);
        Assert.Equal(11, p.PredictedX);
        Assert.Equal(20, p.PredictedY);
    }

    [Fact]
    public void Resolve_Accepted_KeepsServerCell_ClearsPending()
    {
        var p = new MovePrediction();
        p.Begin(10, 20, 11, 20);
        var r = p.Resolve(accepted: true, serverX: 11, serverY: 20);
        Assert.False(p.HasPending);
        Assert.False(r.RolledBack);
        Assert.True(r.HadPending);
        Assert.Equal(11, r.GridX);
        Assert.Equal(20, r.GridY);
    }

    [Fact]
    public void Resolve_Rejected_RollsBackToServerCell()
    {
        var p = new MovePrediction();
        p.Begin(10, 20, 11, 20);
        var r = p.Resolve(accepted: false, serverX: 10, serverY: 20);
        Assert.False(p.HasPending);
        Assert.True(r.RolledBack);
        Assert.Equal(10, r.GridX);
        Assert.Equal(20, r.GridY);
    }
}
