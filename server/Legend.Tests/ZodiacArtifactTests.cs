using Legend.Contracts;
using Legend.Core;
using Legend.Game;
using Legend.Storage;

namespace Legend.Tests;

public class ZodiacArtifactTests
{
    [Fact]
    public void EquipmentSlots_And_Artifacts_SamplePack_Load()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        Assert.Contains(pack.EquipmentSlots.ExtraSlots, s => s.Id == "artifact_main");
        Assert.Equal("zodiac", pack.EquipmentSlots.Zodiac.ContainerId);
        Assert.Equal(12, pack.EquipmentSlots.Zodiac.SlotIds.Count);
        Assert.True(pack.ArtifactUpgrades.ContainsKey("item_artifact_seal"));
        Assert.True(pack.ArtifactUpgrades["item_artifact_seal"].ContainsKey(1));
        Assert.True(pack.Items.ContainsKey("item_zodiac_rat"));
        Assert.Contains("zodiac", pack.Items["item_zodiac_rat"].AllowedContainers);
        Assert.Equal("artifact", pack.Items["item_artifact_seal"].Kind);
        Assert.Equal("artifact_main", pack.Items["item_artifact_seal"].Slot);
        Assert.True(EquipmentHelper.IsValidEquipSlot("artifact_main", pack.EquipmentSlots));
        Assert.False(EquipmentHelper.IsValidEquipSlot("not_a_slot", pack.EquipmentSlots));
    }

    [Fact]
    public void AppearanceJson_Persists_ZodiacSlots()
    {
        var state = new AppearanceJson.State
        {
            ZodiacSlots = new List<EquippedItemState>
            {
                new() { Slot = "zodiac_01", ItemId = "item_zodiac_rat", Identified = true }
            }
        };
        string json = AppearanceJson.Serialize(state);
        var round = AppearanceJson.Parse(json);
        Assert.Single(round.ZodiacSlots);
        Assert.Equal("zodiac_01", round.ZodiacSlots[0].Slot);
        Assert.Equal("item_zodiac_rat", round.ZodiacSlots[0].ItemId);
    }

    [Fact]
    public void RecalculateStats_Includes_Zodiac_And_ArtifactLevel()
    {
        var pack = new VersionPackLoader().LoadFromManifest(
            RepoFile("version-packs", "sample-classic", "manifest.json"));
        var player = new WorldPlayerEntity
        {
            EntityId = "e1",
            CharacterId = "c1",
            Name = "t",
            ClassId = "warrior",
            MapId = "novice_plain",
            Session = null!,
            X = 1,
            Y = 1,
            BaseAtk = 10,
            BaseDef = 5,
            BaseMaxHp = 100,
            Hp = 100,
            MaxHp = 100,
            Atk = 10,
            Def = 5
        };
        player.ZodiacSlots.Add(new EquippedItemState
        {
            Slot = "zodiac_01",
            ItemId = "item_zodiac_rat",
            Identified = true,
            Durability = 100,
            MaxDurability = 100
        });
        EquipmentHelper.RecalculateStats(player, pack);
        // base 10 + zodiac rat atk 3
        Assert.Equal(13, player.Atk);
        Assert.Equal(6, player.Def);
        Assert.Equal(110, player.MaxHp);

        player.Equipment.Add(new EquippedItemState
        {
            Slot = "artifact_main",
            ItemId = "item_artifact_seal",
            ArtifactLevel = 1,
            Identified = true,
            Durability = 100,
            MaxDurability = 100
        });
        EquipmentHelper.RecalculateStats(player, pack);
        // + artifact L1 atk 5, maxHp 30
        Assert.Equal(18, player.Atk);
        Assert.Equal(140, player.MaxHp);

        player.Equipment[0].ArtifactLevel = 2;
        EquipmentHelper.RecalculateStats(player, pack);
        Assert.Equal(23, player.Atk); // 10+3+10
        Assert.Equal(170, player.MaxHp); // 100+10+60
    }

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
}
