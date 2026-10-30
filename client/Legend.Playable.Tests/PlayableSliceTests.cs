using Legend.ClientCore;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Playable;
using Legend.Playable.Audio;
using Legend.Playable.Data;
using Legend.Playable.Input;
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
        Assert.Equal("曙光镇外（原创，对照新手主城）", map.MapName);
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

    [Fact]
    public void ClassicChat_Prefixes()
    {
        var shout = ClassicChat.Parse("!你好", ChatChannel.ChatNearby, "");
        Assert.Equal(ChatChannel.ChatWorld, shout.Channel);
        Assert.Equal("你好", shout.Content);
        var guild = ClassicChat.Parse("!!集合", ChatChannel.ChatNearby, "");
        Assert.Equal(ChatChannel.ChatGuild, guild.Channel);
        var party = ClassicChat.Parse("!~跟上", ChatChannel.ChatNearby, "");
        Assert.Equal(ChatChannel.ChatParty, party.Channel);
        var whisper = ClassicChat.Parse("@阿福 在吗", ChatChannel.ChatNearby, "");
        Assert.Equal(ChatChannel.ChatWhisper, whisper.Channel);
        Assert.Equal("阿福", whisper.TargetName);
        Assert.Equal("在吗", whisper.Content);
    }

    [Fact]
    public void ClassicPointer_AndAttackMode()
    {
        Assert.Equal(ClickAction.Run, ClassicPointer.Resolve(right: true, shift: false, ctrl: false, alt: false, kind: null, doubleClick: false));
        Assert.Equal(ClickAction.Examine, ClassicPointer.Resolve(true, false, false, false, "monster", false));
        Assert.Equal(ClickAction.ForceAttack, ClassicPointer.Resolve(false, true, false, false, "monster", false));
        Assert.Equal(ClickAction.Dig, ClassicPointer.Resolve(false, false, false, true, null, false));
        Assert.Equal(AttackMode.GoodEvil, AttackModes.Next(AttackMode.Peace));
        Assert.False(AttackModes.CanAttack(AttackMode.Peace, isMonster: false, false, false, force: false));
        Assert.True(AttackModes.CanAttack(AttackMode.Peace, isMonster: true, false, false, force: false));
        Assert.True(AttackModes.CanAttack(AttackMode.Peace, isMonster: false, false, false, force: true));
    }

    [Fact]
    public void Catalog_HasClassSkills_AndSellRepairRules()
    {
        Assert.Contains(ContentCatalog.Skills, s => s.SkillId == "heal" && s.Kind == "heal");
        Assert.Contains(ContentCatalog.Skills, s => s.SkillId == "blink" && s.Kind == "teleport");
        Assert.Equal("基本剑术", ContentCatalog.FindSkill("basic_slash")!.DisplayName);
        Assert.Equal(8, ContentCatalog.DefaultBar("taoist").Count);
        var item = new ItemTemplate { ItemId = "item_hp_potion", SellPriceGold = 4 };
        Assert.Equal(4, PhaseLRules.SellUnitPrice(item, null));
        Assert.Equal(14, PhaseLRules.RepairCost(new[]
        {
            new EquippedItemState { Slot = "weapon", EnhanceLevel = 1 }
        }, ""));
        var quest = new QuestTemplate { PrerequisiteQuestId = "quest_slime_hunt" };
        Assert.False(PhaseLRules.PrerequisiteMet(quest, new[] { new QuestProgressState { QuestId = "quest_slime_hunt", Status = "active" } }));
        Assert.True(PhaseLRules.PrerequisiteMet(quest, new[] { new QuestProgressState { QuestId = "quest_slime_hunt", Status = "completed" } }));
    }

    [Fact]
    public void StarterRouteMaps_Load()
    {
        var marsh = MapCatalog.Get("marsh_den");
        var bone = MapCatalog.Get("bone_path");
        Assert.Equal("沼窟外廊（原创，对照兽穴入口）", marsh.MapName);
        Assert.True(marsh.IsPortal(34, 16));
        Assert.Equal("骨脊小径（原创，对照骨洞路径）", bone.MapName);
        Assert.Equal("骨片", ContentCatalog.ItemName("item_bone_chip"));
    }

    [Fact]
    public void PhaseM_PkWeightShieldAndDurability()
    {
        Assert.Equal("white", PhaseMRules.NameColor(0));
        Assert.Equal("yellow", PhaseMRules.NameColor(1));
        Assert.Equal("red", PhaseMRules.NameColor(3));
        Assert.False(PhaseMRules.AllowsPlayerTarget("peace", false, false, 0, force: false));
        Assert.True(PhaseMRules.AllowsPlayerTarget("peace", false, false, 0, force: true));
        Assert.True(PhaseMRules.AllowsPlayerTarget("good_evil", false, false, 3, force: false));
        Assert.False(PhaseMRules.AllowsPlayerTarget("good_evil", false, false, 1, force: false));
        Assert.False(PhaseMRules.AllowsPlayerTarget("guild", false, true, 0, force: false));
        Assert.True(PhaseMRules.AllowsPlayerTarget("all", true, true, 0, force: false));
        Assert.Equal(40, PhaseMRules.AbsorbDamage(100, 40));
        Assert.Equal(0, PhaseMRules.AbsorbDamage(10, 0));
        Assert.Equal((5, 4, 3), PhaseMRules.ApplyBroken(10, 8, 6, 0, 100));
        Assert.Equal((10, 8, 6), PhaseMRules.ApplyBroken(10, 8, 6, 40, 100));
        var items = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_hp_potion"] = new ItemTemplate { ItemId = "item_hp_potion", Weight = 1 }
        };
        int weight = PhaseMRules.BagWeight(new[] { new InventorySlot { ItemId = "item_hp_potion", Count = 4 } }, items);
        Assert.Equal(4, weight);
        Assert.True(weight < PhaseMRules.MaxCarry(1));
        Assert.Equal("oath_city", MapCatalog.Get("oath_city").MapId);
        Assert.Equal("沙门关（原创，对照沙巴克城门）", MapCatalog.Get("sand_gate").MapName);
        Assert.True(MapCatalog.Get("sand_gate").IsPortal(18, 15));
    }
}
