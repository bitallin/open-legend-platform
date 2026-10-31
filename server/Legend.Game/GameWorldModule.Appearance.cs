using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private int BaseMoveIntervalMs =>
        _versionPack.Mounts.DefaultMoveIntervalMs > 0
            ? _versionPack.Mounts.DefaultMoveIntervalMs
            : 300;

    private MountDefinition? FindMount(string mountId) =>
        _versionPack.Mounts.Mounts.FirstOrDefault(m =>
            string.Equals(m.MountId, mountId, StringComparison.OrdinalIgnoreCase));

    private FashionDefinition? FindFashion(string fashionId) =>
        _versionPack.Fashions.Fashions.FirstOrDefault(f =>
            string.Equals(f.FashionId, fashionId, StringComparison.OrdinalIgnoreCase));

    private int ComputeMoveIntervalMs(WorldPlayerEntity player)
    {
        int baseMs = BaseMoveIntervalMs;
        if (string.IsNullOrWhiteSpace(player.ActiveMountId))
            return baseMs;
        var m = FindMount(player.ActiveMountId);
        if (m is null)
            return baseMs;
        int bonus = Math.Max(0, m.MoveSpeedBonusPercent);
        // +50% speed => interval * 100/(100+50)
        return Math.Max(50, (int)Math.Round(baseMs * 100.0 / (100.0 + bonus)));
    }

    private void ApplyFashionBonus(WorldPlayerEntity player)
    {
        player.FashionAtkBonus = 0;
        if (string.IsNullOrWhiteSpace(player.EquippedFashionId))
            return;
        var f = FindFashion(player.EquippedFashionId);
        if (f != null)
            player.FashionAtkBonus = Math.Max(0, f.AtkBonus);
    }

    private void LoadAppearanceOntoPlayer(WorldPlayerEntity player, string? appearanceJson)
    {
        var state = AppearanceJson.Parse(appearanceJson);
        player.OwnedMounts = new HashSet<string>(
            state.OwnedMounts.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        player.OwnedFashions = new HashSet<string>(
            state.OwnedFashions.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        // Only restore active mount if still owned and defined.
        player.ActiveMountId = string.Empty;
        if (!string.IsNullOrWhiteSpace(state.ActiveMountId)
            && player.OwnedMounts.Contains(state.ActiveMountId)
            && FindMount(state.ActiveMountId) != null)
        {
            player.ActiveMountId = state.ActiveMountId;
        }

        player.EquippedFashionId = string.Empty;
        if (!string.IsNullOrWhiteSpace(state.EquippedFashionId)
            && player.OwnedFashions.Contains(state.EquippedFashionId)
            && FindFashion(state.EquippedFashionId) != null)
        {
            player.EquippedFashionId = state.EquippedFashionId;
        }

        player.Storage = (state.Storage ?? new())
            .Where(s => !string.IsNullOrWhiteSpace(s.ItemId) && s.Count > 0)
            .Select(s => new InventorySlot { ItemId = s.ItemId, Count = s.Count, Identified = s.Identified })
            .ToList();
        player.PkKills = Math.Max(0, state.PkKills);
        player.AttackMode = string.IsNullOrWhiteSpace(state.AttackMode) ? "peace" : state.AttackMode;
        player.Gender = state.Gender ?? "";
        player.MentorName = state.MentorName ?? "";
        player.Blacklist = state.Blacklist ?? new();
        player.Wallet = new Dictionary<string, long>(
            (state.Wallet ?? new Dictionary<string, long>()).Where(kv => !string.IsNullOrWhiteSpace(kv.Key)),
            StringComparer.OrdinalIgnoreCase);
        player.RecycleDaily = new Dictionary<string, RecycleDailyJson.Entry>(
            (state.RecycleDaily ?? new Dictionary<string, RecycleDailyJson.Entry>()).Where(kv => !string.IsNullOrWhiteSpace(kv.Key)),
            StringComparer.OrdinalIgnoreCase);
        player.InternalLevel = Math.Max(1, state.InternalLevel);
        player.InternalExp = Math.Max(0, state.InternalExp);
        player.UnlockedMeridians = (state.UnlockedMeridians ?? new())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        player.TalentUnlocked = new Dictionary<string, List<string>>(
            (state.TalentUnlocked ?? new Dictionary<string, List<string>>())
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .ToDictionary(
                    kv => kv.Key,
                    kv => (kv.Value ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var zodiacAllowed = new HashSet<string>(
            (_versionPack.EquipmentSlots.Zodiac?.SlotIds ?? new List<string>()),
            StringComparer.OrdinalIgnoreCase);
        player.ZodiacSlots = (state.ZodiacSlots ?? new())
            .Where(z => !string.IsNullOrWhiteSpace(z.Slot)
                        && !string.IsNullOrWhiteSpace(z.ItemId)
                        && (zodiacAllowed.Count == 0 || zodiacAllowed.Contains(z.Slot)))
            .Select(z => new EquippedItemState
            {
                Slot = z.Slot,
                ItemId = z.ItemId,
                EnhanceLevel = z.EnhanceLevel,
                ArtifactLevel = z.ArtifactLevel,
                Durability = z.Durability > 0 ? z.Durability : PhaseMRules.DurabilityMax,
                MaxDurability = z.MaxDurability > 0 ? z.MaxDurability : PhaseMRules.DurabilityMax,
                Identified = z.Identified
            })
            .ToList();
        player.Heroes = (state.Heroes ?? new())
            .Where(h => !string.IsNullOrWhiteSpace(h.HeroId))
            .Select(h => new HeroSaveState
            {
                HeroId = h.HeroId,
                Name = h.Name ?? "",
                ClassId = h.ClassId ?? "",
                Level = Math.Max(1, h.Level),
                Loyalty = Math.Max(0, h.Loyalty),
                Deployed = h.Deployed,
                Mode = string.IsNullOrWhiteSpace(h.Mode) ? "follow" : h.Mode,
                Skills = (h.Skills ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Hp = h.Hp,
                MaxHp = Math.Max(1, h.MaxHp),
                Mp = h.Mp,
                MaxMp = Math.Max(0, h.MaxMp),
                Atk = h.Atk,
                Def = h.Def
            })
            .ToList();
        if (player.Heroes.Count == 0 && _versionPack.Heroes.CreateStarterOnLogin)
        {
            var starter = HeroCombo.CreateStarter(_versionPack.Heroes, player.CharacterId);
            player.Heroes.Add(starter);
        }
        player.ActiveHero = player.Heroes.FirstOrDefault(h => h.Deployed);
        if (player.ActiveHero is not null)
        {
            player.ActiveHero.Mode = HeroCombo.EffectiveMode(_versionPack.Heroes, player.ActiveHero);
            if (string.Equals(player.ActiveHero.Mode, "rest", StringComparison.OrdinalIgnoreCase))
            {
                player.ActiveHero.Deployed = false;
                player.ActiveHero = null;
            }
            else
            {
                player.ActiveHeroX = player.X + 1;
                player.ActiveHeroY = player.Y;
            }
        }
        CurrencyWallet.EnsureGoldMirror(player.Wallet, player.Gold);
        ApplyFashionBonus(player);
        player.MoveIntervalMs = ComputeMoveIntervalMs(player);
        player.LastMoveUnixMs = 0;
        LoadPetsOntoPlayer(player, state);
    }

    private async Task PersistAppearanceAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        var state = new AppearanceJson.State
        {
            OwnedMounts = player.OwnedMounts.ToList(),
            ActiveMountId = player.ActiveMountId ?? "",
            OwnedFashions = player.OwnedFashions.ToList(),
            EquippedFashionId = player.EquippedFashionId ?? "",
            OwnedPets = player.OwnedPets.ToList(),
            ActivePetId = player.ActivePetId ?? "",
            Storage = player.Storage ?? new(),
            PkKills = player.PkKills,
            AttackMode = string.IsNullOrWhiteSpace(player.AttackMode) ? "peace" : player.AttackMode,
            Gender = player.Gender ?? "",
            MentorName = player.MentorName ?? "",
            Blacklist = player.Blacklist ?? new(),
            Wallet = player.Wallet ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            RecycleDaily = player.RecycleDaily ?? new Dictionary<string, RecycleDailyJson.Entry>(StringComparer.OrdinalIgnoreCase),
            InternalLevel = player.InternalLevel,
            InternalExp = player.InternalExp,
            UnlockedMeridians = player.UnlockedMeridians ?? new(),
            TalentUnlocked = player.TalentUnlocked ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            ZodiacSlots = player.ZodiacSlots ?? new(),
            Heroes = SyncHeroesForPersist(player)
        };
        await _storage.Characters.SaveAppearanceAsync(
            player.CharacterId, AppearanceJson.Serialize(state), ct);
    }

    private async Task BroadcastAppearanceSyncAsync(
        WorldPlayerEntity player, string reason, CancellationToken ct)
    {
        var notify = new AppearanceSyncNotify
        {
            EntityId = player.EntityId,
            CharacterId = player.CharacterId,
            Name = player.Name,
            MountId = player.ActiveMountId ?? "",
            FashionId = player.EquippedFashionId ?? "",
            MoveIntervalMs = player.MoveIntervalMs,
            Reason = reason
        };
        await player.Session.SendAsync(MsgId.AppearanceSyncNotify, 0, notify, ct);
        await BroadcastNearAsync(
            player.MapId, player.X, player.Y, MsgId.AppearanceSyncNotify, notify, ct);
    }

    private async Task HandleMountStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.MountStatusResp, frame.Seq, new MountStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        var resp = new MountStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ActiveMountId = player.ActiveMountId ?? "",
            MoveIntervalMs = player.MoveIntervalMs,
            BaseMoveIntervalMs = BaseMoveIntervalMs
        };
        foreach (var def in _versionPack.Mounts.Mounts)
        {
            bool owned = player.OwnedMounts.Contains(def.MountId);
            resp.Mounts.Add(new MountInfo
            {
                MountId = def.MountId,
                DisplayName = def.DisplayName,
                MoveSpeedBonusPercent = def.MoveSpeedBonusPercent,
                Owned = owned,
                Active = owned && string.Equals(player.ActiveMountId, def.MountId, StringComparison.OrdinalIgnoreCase)
            });
        }
        await session.SendAsync(MsgId.MountStatusResp, frame.Seq, resp, ct);
    }

    private async Task HandleMountSummonAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = MountSummonReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.MountSummonResp, frame.Seq, new MountSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (player.IsDead)
        {
            await session.SendAsync(MsgId.MountSummonResp, frame.Seq, new MountSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMountDenied,
                Message = "Cannot summon while dead."
            }, ct);
            return;
        }

        string mountId = (req.MountId ?? "").Trim();
        var def = FindMount(mountId);
        if (def is null)
        {
            await session.SendAsync(MsgId.MountSummonResp, frame.Seq, new MountSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMountDenied,
                Message = "Unknown mount."
            }, ct);
            return;
        }

        if (!player.OwnedMounts.Contains(mountId))
        {
            await session.SendAsync(MsgId.MountSummonResp, frame.Seq, new MountSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMountDenied,
                Message = "Mount not owned."
            }, ct);
            return;
        }

        player.ActiveMountId = def.MountId;
        player.MoveIntervalMs = ComputeMoveIntervalMs(player);
        await PersistAppearanceAsync(player, ct);

        await session.SendAsync(MsgId.MountSummonResp, frame.Seq, new MountSummonResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Mounted.",
            ActiveMountId = player.ActiveMountId,
            MoveIntervalMs = player.MoveIntervalMs
        }, ct);

        await BroadcastAppearanceSyncAsync(player, "mount_summon", ct);
        await _eventBus.PublishAsync(new MountChangedEvent(
            player.CharacterId, player.Name, player.ActiveMountId, "summon", player.MoveIntervalMs), ct);
    }

    private async Task HandleMountDismissAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.MountDismissResp, frame.Seq, new MountDismissResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        player.ActiveMountId = string.Empty;
        player.MoveIntervalMs = ComputeMoveIntervalMs(player);
        await PersistAppearanceAsync(player, ct);

        await session.SendAsync(MsgId.MountDismissResp, frame.Seq, new MountDismissResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Dismounted.",
            MoveIntervalMs = player.MoveIntervalMs
        }, ct);

        await BroadcastAppearanceSyncAsync(player, "mount_dismiss", ct);
        await _eventBus.PublishAsync(new MountChangedEvent(
            player.CharacterId, player.Name, "", "dismiss", player.MoveIntervalMs), ct);
    }

    private async Task HandleFashionStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.FashionStatusResp, frame.Seq, new FashionStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        var resp = new FashionStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            EquippedFashionId = player.EquippedFashionId ?? "",
            EffectiveAtk = player.Atk
        };
        foreach (var def in _versionPack.Fashions.Fashions)
        {
            bool owned = player.OwnedFashions.Contains(def.FashionId);
            resp.Fashions.Add(new FashionInfo
            {
                FashionId = def.FashionId,
                DisplayName = def.DisplayName,
                AtkBonus = def.AtkBonus,
                Owned = owned,
                Equipped = owned && string.Equals(player.EquippedFashionId, def.FashionId, StringComparison.OrdinalIgnoreCase)
            });
        }
        await session.SendAsync(MsgId.FashionStatusResp, frame.Seq, resp, ct);
    }

    private async Task HandleFashionEquipAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = FashionEquipReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.FashionEquipResp, frame.Seq, new FashionEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string fashionId = (req.FashionId ?? "").Trim();
        if (string.IsNullOrEmpty(fashionId))
        {
            // unequip
            player.EquippedFashionId = string.Empty;
            ApplyFashionBonus(player);
            EquipmentHelper.RecalculateStats(player, _versionPack);
            await PersistAppearanceAsync(player, ct);

            await session.SendAsync(MsgId.FashionEquipResp, frame.Seq, new FashionEquipResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "Unequipped.",
                EquippedFashionId = "",
                EffectiveAtk = player.Atk
            }, ct);
            await BroadcastAppearanceSyncAsync(player, "fashion_unequip", ct);
            await _eventBus.PublishAsync(new FashionChangedEvent(
                player.CharacterId, player.Name, "", false, player.Atk), ct);
            return;
        }

        var def = FindFashion(fashionId);
        if (def is null)
        {
            await session.SendAsync(MsgId.FashionEquipResp, frame.Seq, new FashionEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFashionDenied,
                Message = "Unknown fashion."
            }, ct);
            return;
        }

        if (!player.OwnedFashions.Contains(fashionId))
        {
            await session.SendAsync(MsgId.FashionEquipResp, frame.Seq, new FashionEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFashionDenied,
                Message = "Fashion not owned."
            }, ct);
            return;
        }

        player.EquippedFashionId = def.FashionId;
        ApplyFashionBonus(player);
        EquipmentHelper.RecalculateStats(player, _versionPack);
        await PersistAppearanceAsync(player, ct);

        await session.SendAsync(MsgId.FashionEquipResp, frame.Seq, new FashionEquipResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Equipped.",
            EquippedFashionId = player.EquippedFashionId,
            EffectiveAtk = player.Atk
        }, ct);
        await BroadcastAppearanceSyncAsync(player, "fashion_equip", ct);
        await _eventBus.PublishAsync(new FashionChangedEvent(
            player.CharacterId, player.Name, player.EquippedFashionId, true, player.Atk), ct);
    }

    /// <summary>Unlock mount/fashion from inventory item (kind mount_unlock / fashion_unlock).</summary>
    private async Task<(bool Ok, string Message)> TryUnlockAppearanceItemAsync(
        WorldPlayerEntity player, ItemTemplate tmpl, string itemId, CancellationToken ct)
    {
        bool isMount = string.Equals(tmpl.Kind, "mount_unlock", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(tmpl.UnlockMountId);
        bool isFashion = string.Equals(tmpl.Kind, "fashion_unlock", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(tmpl.UnlockFashionId);

        if (!isMount && !isFashion)
            return (false, "not_appearance");

        if (isMount)
        {
            string mid = tmpl.UnlockMountId.Trim();
            if (FindMount(mid) is null)
                return (false, "Unknown mount unlock.");
            if (player.OwnedMounts.Contains(mid))
                return (false, "Mount already owned.");
            if (!InventoryHelper.TryRemoveItem(player.Inventory, itemId, 1))
                return (false, "Item not in inventory.");
            player.OwnedMounts.Add(mid);
            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
            await PersistAppearanceAsync(player, ct);
            await BroadcastAppearanceSyncAsync(player, "mount_unlock", ct);
            return (true, $"Unlocked mount {mid}");
        }
        else
        {
            string fid = tmpl.UnlockFashionId.Trim();
            if (FindFashion(fid) is null)
                return (false, "Unknown fashion unlock.");
            if (player.OwnedFashions.Contains(fid))
                return (false, "Fashion already owned.");
            if (!InventoryHelper.TryRemoveItem(player.Inventory, itemId, 1))
                return (false, "Item not in inventory.");
            player.OwnedFashions.Add(fid);
            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
            await PersistAppearanceAsync(player, ct);
            await BroadcastAppearanceSyncAsync(player, "fashion_unlock", ct);
            return (true, $"Unlocked fashion {fid}");
        }
    }

    private static List<HeroSaveState> SyncHeroesForPersist(WorldPlayerEntity player)
    {
        var list = (player.Heroes ?? new()).ToList();
        if (player.ActiveHero is not null)
        {
            int idx = list.FindIndex(h =>
                string.Equals(h.HeroId, player.ActiveHero.HeroId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = player.ActiveHero;
            else
                list.Add(player.ActiveHero);
        }
        return list;
    }
}
