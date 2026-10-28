using Legend.Contracts;

namespace Legend.Game;

/// <summary>Applies VersionPack levels.json growth to a player's base stats.</summary>
public static class LevelCurve
{
    public static void ApplyBaseStats(WorldPlayerEntity player, ClassConfigItem cls, LevelsConfigFile levels)
    {
        int level = Math.Max(1, player.Level);
        int n = Math.Max(0, level - 1);
        var growth = levels.GetGrowth(cls.ClassId);
        player.BaseAtk = cls.BaseAtk + n * growth.AtkPerLevel;
        player.BaseDef = cls.BaseDef + n * growth.DefPerLevel;
        player.BaseMaxHp = cls.BaseHp + n * growth.HpPerLevel;
        player.BaseMaxMp = cls.BaseMp + n * growth.MpPerLevel;
        player.MaxMp = Math.Max(0, player.BaseMaxMp);
        if (player.Mp > player.MaxMp)
            player.Mp = player.MaxMp;
    }

    public static int ExpToNext(LevelsConfigFile levels, int level)
        => levels.GetExpToNext(level);

    public static int MaxLevel(LevelsConfigFile levels)
        => levels.MaxLevel > 0 ? levels.MaxLevel : 10;
}
