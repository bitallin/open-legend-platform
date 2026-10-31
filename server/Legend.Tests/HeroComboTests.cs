using Legend.Contracts;
using Legend.Core;

namespace Legend.Tests;

public class HeroComboTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }

    [Fact]
    public void Heroes_And_Combos_SamplePack_Load()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.True(pack.Heroes.CreateStarterOnLogin);
        Assert.Equal("warrior", pack.Heroes.Starter.ClassId);
        Assert.True(pack.Combos.HejiRangeCells >= 1);
        Assert.Contains(pack.Combos.Combos, c => c.ComboId == "combo_ww");
        Assert.True(pack.Skills.ContainsKey("hero_combo"));
    }

    [Fact]
    public void CreateStarter_And_LoyaltyGates()
    {
        var cfg = new HeroesConfigFile
        {
            MaxLoyalty = 1000,
            CreateStarterOnLogin = true,
            Starter = new HeroStarterConfig
            {
                ClassId = "warrior",
                NamePrefix = "英",
                Loyalty = 800,
                Level = 1,
                Mode = "follow",
                Deployed = true
            },
            LoyaltyGates =
            {
                new HeroLoyaltyGateConfig { Below = 50, Action = HeroCombo.ActionDenySummon },
                new HeroLoyaltyGateConfig { Below = 100, Action = HeroCombo.ActionForceRest }
            }
        };
        var hero = HeroCombo.CreateStarter(cfg, "char_abcd");
        Assert.Equal("warrior", hero.ClassId);
        Assert.True(hero.Deployed);
        Assert.True(HeroCombo.CanSummon(cfg, hero, out _));
        Assert.Equal("follow", HeroCombo.EffectiveMode(cfg, hero));

        hero.Loyalty = 80;
        Assert.Equal("rest", HeroCombo.EffectiveMode(cfg, hero));
        hero.Loyalty = 40;
        Assert.False(HeroCombo.CanSummon(cfg, hero, out string reason));
        Assert.Contains("below", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindCombo_And_Execute_And_Damage()
    {
        var combos = new CombosConfigFile
        {
            HejiRangeCells = 3,
            Combos =
            {
                new ComboConfigItem
                {
                    ComboId = "combo_ww",
                    PlayerClassId = "warrior",
                    HeroClassId = "warrior",
                    SkillId = "hero_combo",
                    CooldownMs = 5000,
                    MpCostPlayer = 10,
                    MpCostHero = 10,
                    DamageMultiplier = 2.0
                }
            }
        };
        var found = HeroCombo.FindCombo(combos, "hero_combo", "warrior", "warrior");
        Assert.NotNull(found);
        Assert.Null(HeroCombo.FindCombo(combos, "hero_combo", "mage", "warrior"));

        var hero = new HeroSaveState
        {
            HeroId = "h1",
            ClassId = "warrior",
            Deployed = true,
            Mode = "follow",
            Hp = 50,
            Mp = 20,
            Atk = 10
        };
        Assert.True(HeroCombo.CanExecuteCombo(
            combos, found!, hero,
            "map1", 10, 10,
            "map1", 11, 10,
            heroAlive: true,
            playerMp: 30,
            heroMp: 20,
            nowUnixMs: 1000,
            playerReadyAt: 0,
            heroReadyAt: 0,
            reason: out _));

        hero.Mode = "rest";
        Assert.False(HeroCombo.CanExecuteCombo(
            combos, found!, hero,
            "map1", 10, 10,
            "map1", 11, 10,
            true, 30, 20, 1000, 0, 0, out string restReason));
        Assert.Contains("rest", restReason, StringComparison.OrdinalIgnoreCase);

        int dmg = HeroCombo.CalcComboDamage(20, 10, 5, 2.0);
        // floor((20+5)*2) - 5 = 50-5 = 45
        Assert.Equal(45, dmg);
    }

    [Fact]
    public void Combos_Loader_RejectsUnknownSkill()
    {
        string source = Path.GetDirectoryName(RepoFile("version-packs", "sample-classic", "manifest.json"))!;
        string dest = Path.Combine(Path.GetTempPath(), "legend-combo-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, dest);
        try
        {
            File.WriteAllText(Path.Combine(dest, "configs", "combos.json"), """
            {
              "hejiRangeCells": 3,
              "combos": [
                {
                  "comboId": "bad",
                  "playerClassId": "warrior",
                  "heroClassId": "warrior",
                  "skillId": "no_such_skill",
                  "cooldownMs": 1000,
                  "mpCostPlayer": 1,
                  "mpCostHero": 1,
                  "damageMultiplier": 1.0
                }
              ]
            }
            """);
            var ex = Assert.ThrowsAny<Exception>(() =>
                new VersionPackLoader().LoadFromManifest(Path.Combine(dest, "manifest.json")));
            Assert.Contains("no_such_skill", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = file.Replace(source, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
