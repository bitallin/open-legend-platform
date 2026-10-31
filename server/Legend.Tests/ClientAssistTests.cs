using Legend.ClientCore;
using Legend.Contracts;
using Legend.Core;

namespace Legend.Tests;

public class ClientAssistTests
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
    public void ClientAssist_And_PickupFilters_SamplePack_Load_DefaultOff()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.False(pack.ClientAssist.Enabled);
        Assert.True(pack.ClientAssist.SearchRangeCells >= 1);
        Assert.Contains(pack.ClientAssist.Skills, s => s.SkillId == "basic_slash");
        Assert.Contains(pack.PickupFilters.Profiles, p => p.DefaultForAssist);
        Assert.False(pack.Maps.Values.First().IsNoHang);
    }

    [Fact]
    public void ClientAssistEngine_DefaultOff_TryStart_NoHang_Rejected()
    {
        string json = File.ReadAllText(RepoFile("version-packs", "sample-classic", "configs", "client_assist.json"));
        var settings = ClientAssistSettings.FromJson(json);
        Assert.False(settings.Enabled);

        var engine = new ClientAssistEngine(settings);
        Assert.False(engine.TryStart(new AssistWorldView { MapNoHang = true, MapId = "m" }, out string reason));
        Assert.Contains("noHang", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(engine.Running);
    }

    [Fact]
    public void ClientAssistEngine_Approaches_Then_Attacks_And_PicksUp()
    {
        var settings = new ClientAssistSettings
        {
            Mode = "anchor",
            AnchorRadiusCells = 20,
            SearchRangeCells = 8,
            MaxPullCount = 2,
            GlobalCastGapMs = 0,
            Skills =
            {
                new AssistSkillSetting { SkillId = "basic_slash", Enabled = true, Priority = 1 }
            },
            Pickup = new AssistPickupSetting
            {
                Enabled = true,
                PickupRangeCells = 3,
                DelayMs = 0,
                PauseAttackWhilePicking = true,
                PriorityItemIds = { "item_copper_coin" }
            }
        };
        var filters = new PickupFilterSettings
        {
            Profiles =
            {
                new PickupFilterProfileSetting
                {
                    ProfileId = "default",
                    DefaultForAssist = true,
                    Rules =
                    {
                        new PickupFilterRuleSetting
                        {
                            ItemId = "item_copper_coin",
                            AssistPickup = true,
                            Priority = 10
                        }
                    }
                }
            }
        };
        var engine = new ClientAssistEngine(settings, filters);
        Assert.True(engine.TryStart(new AssistWorldView { SelfX = 5, SelfY = 5, MapId = "m", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 }, out _));

        var approach = engine.Tick(new AssistWorldView
        {
            SelfX = 5, SelfY = 5, MapId = "m", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            NowUnixMs = 1000,
            Monsters = new List<AssistEntity>
            {
                new AssistEntity { EntityId = "mob1", X = 8, Y = 5, Alive = true, Kind = "monster" }
            }
        });
        Assert.Equal(AssistCommandKind.Move, approach.Kind);
        Assert.Equal(6, approach.ToX);
        Assert.Equal(5, approach.ToY);

        var attack = engine.Tick(new AssistWorldView
        {
            SelfX = 7, SelfY = 5, MapId = "m", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            NowUnixMs = 2000,
            LearnedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "basic_slash" },
            Monsters = new List<AssistEntity>
            {
                new AssistEntity { EntityId = "mob1", X = 8, Y = 5, Alive = true, Kind = "monster" }
            }
        });
        Assert.Equal(AssistCommandKind.Attack, attack.Kind);
        Assert.Equal("mob1", attack.TargetEntityId);
        Assert.Equal("basic_slash", attack.SkillId);

        var pickup = engine.Tick(new AssistWorldView
        {
            SelfX = 5, SelfY = 5, MapId = "m", MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            NowUnixMs = 3000,
            Loot = new List<AssistLoot>
            {
                new AssistLoot { EntityId = "loot1", ItemId = "item_copper_coin", X = 5, Y = 5 }
            }
        });
        Assert.Equal(AssistCommandKind.Pickup, pickup.Kind);
        Assert.Equal("loot1", pickup.TargetEntityId);
    }

    [Fact]
    public void ClientAssistEngine_Protect_UsesHpPotion_Then_EmergencyScroll()
    {
        var settings = new ClientAssistSettings
        {
            EmergencyScrollHpPercent = 15,
            EmergencyScrollItemId = "item_town_scroll",
            HpPotions =
            {
                new AssistPotionSetting { ItemId = "item_hp_potion", HpPercent = 40, Priority = 1, MinIntervalMs = 0 }
            }
        };
        var engine = new ClientAssistEngine(settings);
        Assert.True(engine.TryStart(new AssistWorldView { MapId = "m", MaxHp = 100, Hp = 100 }, out _));

        var pot = engine.Tick(new AssistWorldView
        {
            MapId = "m", MaxHp = 100, Hp = 30, MaxMp = 50, Mp = 50, NowUnixMs = 1,
            InventoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["item_hp_potion"] = 1,
                ["item_town_scroll"] = 1
            }
        });
        Assert.Equal(AssistCommandKind.UseItem, pot.Kind);
        Assert.Equal("item_hp_potion", pot.ItemId);

        var scroll = engine.Tick(new AssistWorldView
        {
            MapId = "m", MaxHp = 100, Hp = 10, MaxMp = 50, Mp = 50, NowUnixMs = 2,
            InventoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["item_town_scroll"] = 1
            }
        });
        Assert.Equal(AssistCommandKind.UseItem, scroll.Kind);
        Assert.Equal("item_town_scroll", scroll.ItemId);
    }

    [Fact]
    public void ClientAssistEngine_NoAutoPickup_SkipsLoot()
    {
        var settings = new ClientAssistSettings
        {
            Pickup = new AssistPickupSetting
            {
                Enabled = true,
                PickupRangeCells = 3,
                PriorityItemIds = { "item_copper_coin" }
            },
            Skills = { new AssistSkillSetting { SkillId = "basic_slash", Enabled = true, Priority = 1 } }
        };
        var engine = new ClientAssistEngine(settings);
        Assert.True(engine.TryStart(new AssistWorldView { MapId = "m", SelfX = 1, SelfY = 1 }, out _));
        var cmd = engine.Tick(new AssistWorldView
        {
            MapId = "m", SelfX = 1, SelfY = 1, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            MapNoAutoPickup = true, NowUnixMs = 10,
            Loot = new List<AssistLoot> { new AssistLoot { EntityId = "l", ItemId = "item_copper_coin", X = 1, Y = 1 } }
        });
        Assert.NotEqual(AssistCommandKind.Pickup, cmd.Kind);
    }
}
