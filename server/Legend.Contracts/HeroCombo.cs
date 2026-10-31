namespace Legend.Contracts;

/// <summary>Hero loyalty / summon gates and combo eligibility (docs/site/05, 15). No MsgId handlers here.</summary>
public static class HeroCombo
{
    public const string ActionDenySummon = "deny_summon";
    public const string ActionForceRest = "force_rest";

    public static HeroSaveState CreateStarter(HeroesConfigFile cfg, string ownerCharacterId)
    {
        var starter = cfg.Starter ?? new HeroStarterConfig();
        string classId = string.IsNullOrWhiteSpace(starter.ClassId) ? "warrior" : starter.ClassId;
        string prefix = string.IsNullOrWhiteSpace(starter.NamePrefix) ? "英" : starter.NamePrefix;
        int loyalty = Math.Clamp(starter.Loyalty, 0, Math.Max(1, cfg.MaxLoyalty));
        return new HeroSaveState
        {
            HeroId = "hero_" + ownerCharacterId,
            Name = prefix + ownerCharacterId[^Math.Min(4, ownerCharacterId.Length)..],
            ClassId = classId,
            Level = Math.Max(1, starter.Level),
            Loyalty = loyalty,
            Deployed = starter.Deployed,
            Mode = string.IsNullOrWhiteSpace(starter.Mode) ? "follow" : starter.Mode,
            Skills = new List<string> { "hero_combo" },
            Hp = 100,
            MaxHp = 100,
            Mp = 50,
            MaxMp = 50,
            Atk = 8,
            Def = 3
        };
    }

    public static bool CanSummon(HeroesConfigFile cfg, HeroSaveState hero, out string reason)
    {
        reason = "";
        if (hero is null || string.IsNullOrWhiteSpace(hero.HeroId))
        {
            reason = "no hero";
            return false;
        }
        foreach (var gate in (cfg.LoyaltyGates ?? new()).OrderBy(g => g.Below))
        {
            if (hero.Loyalty < gate.Below
                && string.Equals(gate.Action, ActionDenySummon, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"loyalty {hero.Loyalty} below {gate.Below}";
                return false;
            }
        }
        return true;
    }

    public static string EffectiveMode(HeroesConfigFile cfg, HeroSaveState hero)
    {
        string mode = string.IsNullOrWhiteSpace(hero.Mode) ? "follow" : hero.Mode;
        foreach (var gate in (cfg.LoyaltyGates ?? new()).OrderByDescending(g => g.Below))
        {
            if (hero.Loyalty < gate.Below
                && string.Equals(gate.Action, ActionForceRest, StringComparison.OrdinalIgnoreCase))
                return "rest";
        }
        return mode;
    }

    public static ComboConfigItem? FindCombo(
        CombosConfigFile combos,
        string skillId,
        string playerClassId,
        string heroClassId)
    {
        foreach (var c in combos.Combos ?? new())
        {
            if (!string.Equals(c.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(c.PlayerClassId, playerClassId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(c.HeroClassId, heroClassId, StringComparison.OrdinalIgnoreCase))
                continue;
            return c;
        }
        return null;
    }

    public static bool CanExecuteCombo(
        CombosConfigFile combos,
        ComboConfigItem combo,
        HeroSaveState? hero,
        string playerMapId,
        int playerX,
        int playerY,
        string heroMapId,
        int heroX,
        int heroY,
        bool heroAlive,
        int playerMp,
        int heroMp,
        long nowUnixMs,
        long playerReadyAt,
        long heroReadyAt,
        out string reason)
    {
        reason = "";
        if (hero is null || !hero.Deployed)
        {
            reason = "hero not deployed";
            return false;
        }
        if (!heroAlive || hero.Hp <= 0)
        {
            reason = "hero dead";
            return false;
        }
        // Caller should set Mode via EffectiveMode when applying loyalty gates.
        if (string.Equals(hero.Mode, "rest", StringComparison.OrdinalIgnoreCase))
        {
            reason = "hero resting";
            return false;
        }
        if (!string.Equals(playerMapId, heroMapId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "different map";
            return false;
        }
        int range = Math.Max(1, combos.HejiRangeCells);
        int dist = Math.Max(Math.Abs(playerX - heroX), Math.Abs(playerY - heroY));
        if (dist > range)
        {
            reason = "out of heji range";
            return false;
        }
        if (nowUnixMs < playerReadyAt)
        {
            reason = "player combo cooling";
            return false;
        }
        if (nowUnixMs < heroReadyAt)
        {
            reason = "hero combo cooling";
            return false;
        }
        if (playerMp < combo.MpCostPlayer)
        {
            reason = "player mp";
            return false;
        }
        if (heroMp < combo.MpCostHero)
        {
            reason = "hero mp";
            return false;
        }
        return true;
    }

    public static int CalcComboDamage(int playerAtk, int heroAtk, int targetDef, double multiplier)
    {
        int raw = (int)Math.Floor((playerAtk + Math.Max(0, heroAtk) / 2.0) * multiplier);
        return Math.Max(1, raw - Math.Max(0, targetDef));
    }
}
