using Legend.ClientCore;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Playable.Audio;
using Legend.Playable.World;

namespace Legend.Playable;

/// <summary>Headless checks used by <c>--smoke</c> and unit tests. No window, no network.</summary>
public static class SmokeCheck
{
    public static int Run()
    {
        string version = string.Concat(ProtocolConstants.CurrentVersion);
        if (!string.Equals(version, "1.21.0", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("protocol mismatch " + version);
            return 1;
        }

        var parsed = LaunchOptions.Parse(new[]
        {
            "--host", "150.158.164.90", "--port", "7100", "--realm-id", "realm-classic-01"
        });
        if (parsed.Host != "150.158.164.90" || parsed.Port != 7100 || parsed.RealmId != "realm-classic-01")
            return 1;

        foreach (var wav in new[]
                 {
                     ProceduralWav.UiClick(), ProceduralWav.Attack(), ProceduralWav.Hit(),
                     ProceduralWav.Pickup(), ProceduralWav.LoginBgm(), ProceduralWav.WorldBgm()
                 })
        {
            if (!ProceduralWav.LooksLikeWav(wav))
                return 1;
        }

        using var audio = new PlayableAudio();
        var map = MapCatalog.Get("novice_plain");
        if (map.Width != 64 || !map.IsSafe(10, 10) || !map.IsBlocked(15, 15) || map.IsSafe(30, 30))
            return 1;
        if (!MapCatalog.Get("dark_forest").MapId.Equals("dark_forest", StringComparison.Ordinal))
            return 1;

        if (GridMath.DirFromDelta(0, 1) != 0 || GridMath.DirFromDelta(1, 0) != 2)
            return 1;
        var (gx, gy) = GridMath.ScreenToGrid(400, 300, 10, 10, 800, 600, 36);
        if (gx != 10 || gy != 10)
            return 1;

        var world = new WorldState();
        world.SetMap("novice_plain");
        world.Upsert("self", "旅人", EntityKind.EntityPlayer, 10, 10, 4, 100, 100, true, true);
        world.Upsert("m2", "史莱姆", EntityKind.EntityMonster, 14, 12, 4, 30, 30, false, false);
        world.Upsert("m1", "史莱姆", EntityKind.EntityMonster, 20, 20, 4, 30, 30, false, false);
        if (world.CycleMonster(null) != "m1")
            return 1;
        world.SetGoal(12, 10);
        if (!world.TryNextStep(out int tx, out int ty, out int dir) || tx != 11 || ty != 10 || dir != 2)
            return 1;

        var fsm = new LegendSessionFsm();
        fsm.BeginConnect("h", 1);
        fsm.MarkAuthenticated();
        fsm.BeginEnterWorld("旅人");
        fsm.AbortEnterWorld("fail");
        if (fsm.Phase != SessionPhase.CharSelect)
            return 1;

        Console.WriteLine("SMOKE_OK protocol=1.21.0 host=150.158.164.90:7100");
        return 0;
    }
}
