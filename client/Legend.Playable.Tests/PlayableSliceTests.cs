using Legend.ClientCore;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Playable;
using Legend.Playable.Audio;
using Legend.Playable.World;

namespace Legend.Playable.Tests;

public class PlayableSliceTests
{
    [Fact]
    public void Protocol_Stays_1_21_0()
    {
        Assert.Equal("1.21.0", ProtocolConstants.CurrentVersion);
    }

    [Fact]
    public void LaunchOptions_ParsePublicTrial()
    {
        var opt = LaunchOptions.Parse(new[]
        {
            "--host", "150.158.164.90", "--port", "7100", "--realm-id", "realm-classic-01",
            "--user", "hero", "--pass", "secret"
        });
        Assert.Equal("150.158.164.90", opt.Host);
        Assert.Equal(7100, opt.Port);
        Assert.Equal("realm-classic-01", opt.RealmId);
        Assert.Equal("hero", opt.Username);
        Assert.Equal("secret", opt.Password);
    }

    [Fact]
    public void LaunchOptions_DefaultPointsAtPublicTrial()
    {
        var opt = new LaunchOptions();
        Assert.Equal(LaunchOptions.DefaultHost, opt.Host);
        Assert.Equal(7100, opt.Port);
        Assert.Equal("realm-classic-01", opt.RealmId);
    }

    [Fact]
    public void LaunchOptions_RejectsBadPort()
    {
        Assert.Throws<FormatException>(() => LaunchOptions.Parse(new[] { "--port", "nope" }));
    }

    [Fact]
    public void ProceduralWav_HasRiffHeader()
    {
        byte[] wav = ProceduralWav.Attack();
        Assert.True(ProceduralWav.LooksLikeWav(wav));
        Assert.True(wav.Length > 44);
        Assert.True(ProceduralWav.LooksLikeWav(ProceduralWav.WorldBgm()));
        Assert.True(ProceduralWav.LooksLikeWav(ProceduralWav.LoginBgm()));
    }

    [Fact]
    public void NovicePlain_SafeZoneAndBlocked()
    {
        var map = MapCatalog.Get("novice_plain");
        Assert.Equal(64, map.Width);
        Assert.Equal("曙光平原（原创）", map.MapName);
        Assert.True(map.IsSafe(10, 10));
        Assert.True(map.IsSafe(5, 5));
        Assert.False(map.IsSafe(30, 30));
        Assert.True(map.IsBlocked(15, 15));
        Assert.True(map.IsPortal(30, 10));
        Assert.False(map.IsBlocked(10, 10));
    }

    [Fact]
    public void Grid_DirectionAndClickRoundTrip()
    {
        Assert.Equal(0, GridMath.DirFromDelta(0, 1));
        Assert.Equal(2, GridMath.DirFromDelta(1, 0));
        Assert.Equal(4, GridMath.DirFromDelta(0, -1));
        Assert.Equal(6, GridMath.DirFromDelta(-1, 0));
        var (gx, gy) = GridMath.ScreenToGrid(400, 300, 10, 12, 800, 600, 36);
        Assert.Equal(10, gx);
        Assert.Equal(12, gy);
        var (sx, sy) = GridMath.GridToScreen(10, 12, 10, 12, 800, 600, 36);
        Assert.Equal(400, sx);
        Assert.Equal(300, sy);
    }

    [Fact]
    public void NextStep_SlidesAroundBlockedDiagonal()
    {
        var step = GridMath.NextStep(0, 0, 2, 2, (x, y) => x == 1 && y == 1);
        Assert.Equal((1, 0), step);
    }

    [Fact]
    public void World_TabCycle_CombatFloat_AndGoal()
    {
        var world = new WorldState();
        world.SetMap("novice_plain");
        world.Upsert("self", "旅人", EntityKind.EntityPlayer, 10, 10, 4, 80, 100, true, true);
        world.Upsert("m2", "狼", EntityKind.EntityMonster, 12, 10, 6, 20, 20, false, false);
        world.Upsert("m1", "史莱姆", EntityKind.EntityMonster, 16, 10, 6, 20, 20, false, false);
        Assert.Equal("m1", world.CycleMonster(null));
        Assert.Equal("m2", world.CycleMonster("m1"));

        world.ApplyCombat(new CombatSync
        {
            TargetEntityId = "m2",
            Result = CombatResultKind.CombatHit,
            Damage = 7,
            TargetHp = 13,
            TargetMaxHp = 20
        });
        Assert.Equal(13, world.Get("m2")!.Hp);
        Assert.Single(world.Floats);
        Assert.Equal("-7", world.Floats[0].Text);

        world.SetGoal(12, 10);
        Assert.True(world.TryNextStep(out int tx, out int ty, out int dir));
        Assert.Equal(11, tx);
        Assert.Equal(10, ty);
        Assert.Equal(2, dir);
    }

    [Fact]
    public void Fsm_AbortEnter_ReturnsToCharSelect()
    {
        var fsm = new LegendSessionFsm();
        fsm.BeginConnect("127.0.0.1", 7100);
        fsm.MarkAuthenticated();
        fsm.BeginEnterWorld("旅人");
        fsm.AbortEnterWorld("进入失败");
        Assert.Equal(SessionPhase.CharSelect, fsm.Phase);
        Assert.Equal("进入失败", fsm.ErrorMessage);
    }

    [Fact]
    public void SmokeCheck_ExitsZero()
    {
        Assert.Equal(0, SmokeCheck.Run());
    }
}
