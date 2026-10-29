using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly ConcurrentDictionary<string, PetEntity> _pets = new(StringComparer.Ordinal);

    private PetDefinition? FindPet(string petId) =>
        _versionPack.Pets.Pets.FirstOrDefault(p =>
            string.Equals(p.PetId, petId, StringComparison.OrdinalIgnoreCase));

    private static string MakePetEntityId(string characterId) =>
        "pet_" + characterId;

    private void LoadPetsOntoPlayer(WorldPlayerEntity player, AppearanceJson.State state)
    {
        player.OwnedPets = new HashSet<string>(
            (state.OwnedPets ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        player.ActivePetId = string.Empty;
        player.ActivePetEntityId = string.Empty;
        if (!string.IsNullOrWhiteSpace(state.ActivePetId)
            && player.OwnedPets.Contains(state.ActivePetId)
            && FindPet(state.ActivePetId) != null)
        {
            player.ActivePetId = state.ActivePetId;
        }
    }

    private void MergePetsIntoAppearanceState(WorldPlayerEntity player, AppearanceJson.State state)
    {
        state.OwnedPets = player.OwnedPets.ToList();
        state.ActivePetId = player.ActivePetId ?? "";
    }

    private Task PersistPetsAsync(WorldPlayerEntity player, CancellationToken ct)
        => PersistAppearanceAsync(player, ct);

    private async Task BroadcastPetSyncAsync(
        WorldPlayerEntity owner, PetEntity? pet, string reason, CancellationToken ct)
    {
        var notify = new PetSyncNotify
        {
            OwnerEntityId = owner.EntityId,
            OwnerCharacterId = owner.CharacterId,
            PetId = owner.ActivePetId ?? "",
            PetEntityId = pet?.EntityId ?? owner.ActivePetEntityId ?? "",
            X = pet?.X ?? 0,
            Y = pet?.Y ?? 0,
            Reason = reason
        };
        await owner.Session.SendAsync(MsgId.PetSyncNotify, 0, notify, ct);
        await BroadcastNearAsync(owner.MapId, owner.X, owner.Y, MsgId.PetSyncNotify, notify, ct);
    }

    private async Task HandlePetStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.PetStatusResp, frame.Seq, new PetStatusResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        var resp = new PetStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ActivePetId = player.ActivePetId ?? "",
            ActivePetEntityId = player.ActivePetEntityId ?? ""
        };
        foreach (var def in _versionPack.Pets.Pets)
        {
            bool owned = player.OwnedPets.Contains(def.PetId);
            resp.Pets.Add(new PetInfo
            {
                PetId = def.PetId,
                DisplayName = def.DisplayName,
                AssistDamagePercent = def.AssistDamagePercent,
                AssistCooldownMs = def.AssistCooldownMs,
                FollowDistance = def.FollowDistance,
                Owned = owned,
                Active = owned && string.Equals(player.ActivePetId, def.PetId, StringComparison.OrdinalIgnoreCase)
            });
        }
        await session.SendAsync(MsgId.PetStatusResp, frame.Seq, resp, ct);
    }

    private async Task HandlePetSummonAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PetSummonReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.PetSummonResp, frame.Seq, new PetSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }
        if (player.IsDead || player.Hp <= 0)
        {
            await session.SendAsync(MsgId.PetSummonResp, frame.Seq, new PetSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "Cannot summon while dead."
            }, ct);
            return;
        }

        string petId = (req.PetId ?? "").Trim();
        var def = FindPet(petId);
        if (def is null)
        {
            await session.SendAsync(MsgId.PetSummonResp, frame.Seq, new PetSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPetDenied,
                Message = "Unknown pet."
            }, ct);
            return;
        }
        if (!player.OwnedPets.Contains(def.PetId))
        {
            await session.SendAsync(MsgId.PetSummonResp, frame.Seq, new PetSummonResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPetDenied,
                Message = "Pet not owned."
            }, ct);
            return;
        }

        // Replace existing active pet if any.
        if (!string.IsNullOrEmpty(player.ActivePetEntityId))
            await DespawnPetEntityAsync(player, "replace", persist: false, ct);

        var pet = SpawnPetEntity(player, def);
        player.ActivePetId = def.PetId;
        player.ActivePetEntityId = pet.EntityId;
        await PersistPetsAsync(player, ct);

        await session.SendAsync(MsgId.PetSummonResp, frame.Seq, new PetSummonResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ActivePetId = player.ActivePetId,
            PetEntityId = pet.EntityId,
            PetX = pet.X,
            PetY = pet.Y
        }, ct);

        await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntitySpawn,
            new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "PetSummon" }, ct);
        await session.SendAsync(MsgId.EntitySpawn, 0,
            new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "PetSummon" }, ct);
        await BroadcastPetSyncAsync(player, pet, "summon", ct);
        await _eventBus.PublishAsync(new PetChangedEvent(
            player.CharacterId, player.Name, def.PetId, pet.EntityId, "summon", "summon"), ct);
    }

    private async Task HandlePetDismissAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.PetDismissResp, frame.Seq, new PetDismissResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(player.ActivePetId))
        {
            await session.SendAsync(MsgId.PetDismissResp, frame.Seq, new PetDismissResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPetDenied,
                Message = "No active pet."
            }, ct);
            return;
        }

        await DespawnPetEntityAsync(player, "dismiss", persist: true, ct);
        await session.SendAsync(MsgId.PetDismissResp, frame.Seq, new PetDismissResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        }, ct);
        // PetChangedEvent already published by DespawnPetEntityAsync(reason=dismiss).
    }

    private PetEntity SpawnPetEntity(WorldPlayerEntity owner, PetDefinition def)
    {
        string eid = MakePetEntityId(owner.CharacterId);
        // Place adjacent to owner (prefer +1 x).
        int px = owner.X + 1;
        int py = owner.Y;
        var aoi = GetAoi(owner.MapId);
        if (!aoi.IsWalkable(px, py))
        {
            px = owner.X;
            py = owner.Y;
        }

        var pet = new PetEntity
        {
            EntityId = eid,
            PetId = def.PetId,
            DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.PetId : def.DisplayName,
            OwnerCharacterId = owner.CharacterId,
            OwnerEntityId = owner.EntityId,
            MapId = owner.MapId,
            X = px,
            Y = py,
            FollowDistance = Math.Max(1, def.FollowDistance),
            AssistDamagePercent = Math.Max(0, def.AssistDamagePercent),
            AssistCooldownMs = Math.Max(200, def.AssistCooldownMs),
            AssistRange = Math.Max(1, def.AssistRange),
            Alive = true
        };
        _pets[eid] = pet;
        return pet;
    }

    /// <summary>
    /// Death rule: owner death auto-recalls the pet (despawn + clear ActivePetId).
    /// Owner must re-summon after revive. Documented in README.
    /// </summary>
    private async Task DespawnPetEntityAsync(
        WorldPlayerEntity owner, string reason, bool persist, CancellationToken ct)
    {
        string? eid = owner.ActivePetEntityId;
        PetEntity? pet = null;
        if (!string.IsNullOrEmpty(eid) && _pets.TryRemove(eid, out pet))
        {
            await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntityDespawn,
                new EntityDespawn { EntityId = pet.EntityId, Reason = reason, MapId = pet.MapId }, ct);
            try
            {
                await owner.Session.SendAsync(MsgId.EntityDespawn, 0,
                    new EntityDespawn { EntityId = pet.EntityId, Reason = reason, MapId = pet.MapId }, ct);
            }
            catch { /* session may be gone */ }
        }
        else if (!string.IsNullOrEmpty(eid))
            _pets.TryRemove(eid, out _);

        string oldPetId = owner.ActivePetId ?? "";
        owner.ActivePetId = string.Empty;
        owner.ActivePetEntityId = string.Empty;
        if (persist)
            await PersistPetsAsync(owner, ct);
        await BroadcastPetSyncAsync(owner, pet, reason, ct);
        if (!string.IsNullOrEmpty(oldPetId) && reason is "owner_death" or "disconnect" or "dismiss")
        {
            await _eventBus.PublishAsync(new PetChangedEvent(
                owner.CharacterId, owner.Name, oldPetId, eid ?? "", "dismiss", reason), ct);
        }
    }

    /// <summary>After a successful owner hit on a monster, set assist target and try an assist strike.</summary>
    private async Task TryPetAssistAfterOwnerAttackAsync(
        WorldPlayerEntity owner, MonsterEntity monster, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(owner.ActivePetEntityId))
            return;
        if (!_pets.TryGetValue(owner.ActivePetEntityId, out var pet) || !pet.Alive)
            return;
        if (!string.Equals(pet.MapId, monster.MapId, StringComparison.OrdinalIgnoreCase))
            return;

        pet.AssistTargetEntityId = monster.EntityId;
        await TryPetAssistHitAsync(owner, pet, monster, ct);
    }

    private async Task TryPetAssistHitAsync(
        WorldPlayerEntity owner, PetEntity pet, MonsterEntity monster, CancellationToken ct)
    {
        if (!monster.Alive || monster.Hp <= 0)
            return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now < pet.NextAssistUnixMs)
            return;
        int dist = CombatMath.Chebyshev(pet.X, pet.Y, monster.X, monster.Y);
        if (dist > pet.AssistRange)
            return;

        pet.NextAssistUnixMs = now + pet.AssistCooldownMs;
        int raw = Math.Max(1, (int)Math.Floor(owner.Atk * (pet.AssistDamagePercent / 100.0)));
        int damage = CombatMath.CalcDamage(raw, monster.Template.Def, 1.0);
        // Light assist: clamp to at least 1, prefer modest chip damage.
        damage = Math.Max(1, damage);
        monster.Hp = Math.Max(0, monster.Hp - damage);
        bool killed = monster.Hp <= 0;

        var combat = new CombatSync
        {
            AttackerEntityId = pet.EntityId,
            TargetEntityId = monster.EntityId,
            SkillId = "pet_assist",
            Result = killed ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
            Damage = damage,
            TargetHp = monster.Hp,
            TargetMaxHp = monster.MaxHp,
            TargetDied = killed,
            MapId = pet.MapId
        };
        await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.CombatSync, combat, ct);
        await owner.Session.SendAsync(MsgId.CombatSync, 0, combat, ct);

        if (killed)
        {
            long kn = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            monster.Alive = false;
            monster.RespawnAtUnixMs = kn + Math.Max(1000, monster.Template.RespawnMs);
            await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntityDespawn,
                new EntityDespawn { EntityId = monster.EntityId, Reason = "Killed", MapId = monster.MapId }, ct);
            // Credit kill to owner (same as owner last-hit path).
            await GrantExpFromKillAsync(owner, monster, ct);
            await DropLootAsync(monster, owner, ct);
            pet.AssistTargetEntityId = null;
        }
    }

    private async Task TickPetsAsync(CancellationToken ct)
    {
        foreach (var pet in _pets.Values.ToList())
        {
            if (!pet.Alive)
                continue;
            var aoi = GetAoi(pet.MapId);
            if (!aoi.TryGetEntity(pet.OwnerEntityId, out var owner) || owner is null)
            {
                // Owner gone from map — despawn orphan pet entity without clearing persisted ActivePetId
                // (disconnect path handles persist). Here just remove runtime entity.
                if (_pets.TryRemove(pet.EntityId, out _))
                {
                    await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntityDespawn,
                        new EntityDespawn { EntityId = pet.EntityId, Reason = "orphan", MapId = pet.MapId }, ct);
                }
                continue;
            }

            // Follow: step toward owner when beyond follow distance.
            // If far behind (dist > 8) or different map, snap next to owner so assist stays usable.
            int distOwner = CombatMath.Chebyshev(pet.X, pet.Y, owner.X, owner.Y);
            bool mapMismatch = !string.Equals(pet.MapId, owner.MapId, StringComparison.OrdinalIgnoreCase);
            if (mapMismatch || distOwner > 8)
            {
                string oldMap = pet.MapId;
                int ox = pet.X, oy = pet.Y;
                pet.MapId = owner.MapId;
                int px = owner.X + 1, py = owner.Y;
                if (!GetAoi(pet.MapId).IsWalkable(px, py))
                { px = owner.X; py = owner.Y; }
                pet.X = px;
                pet.Y = py;
                await BroadcastNearAsync(oldMap, ox, oy, MsgId.EntityDespawn,
                    new EntityDespawn { EntityId = pet.EntityId, Reason = mapMismatch ? "map_follow" : "catch_up", MapId = oldMap }, ct);
                await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntitySpawn,
                    new EntitySpawn { Entity = pet.ToSnapshot(), Reason = mapMismatch ? "PetFollowMap" : "PetCatchUp" }, ct);
                try
                {
                    await owner.Session.SendAsync(MsgId.EntitySpawn, 0,
                        new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "PetCatchUp" }, ct);
                }
                catch { }
            }
            else if (distOwner > pet.FollowDistance)
            {
                int nx = pet.X + Math.Sign(owner.X - pet.X);
                int ny = pet.Y + Math.Sign(owner.Y - pet.Y);
                if ((nx != pet.X || ny != pet.Y) && aoi.IsWalkable(nx, ny))
                {
                    int ox = pet.X, oy = pet.Y;
                    pet.X = nx;
                    pet.Y = ny;
                    var sync = new MoveSync
                    {
                        EntityId = pet.EntityId,
                        FromX = ox, FromY = oy, ToX = nx, ToY = ny,
                        Direction = 4, Accepted = true, Reason = "PetFollow"
                    };
                    await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.MoveSync, sync, ct);
                    await owner.Session.SendAsync(MsgId.MoveSync, 0, sync, ct);
                }
            }

            // Ongoing assist vs last target.
            if (!string.IsNullOrEmpty(pet.AssistTargetEntityId)
                && _monsters.TryGetValue(pet.AssistTargetEntityId, out var mon)
                && mon.Alive)
            {
                await TryPetAssistHitAsync(owner, pet, mon, ct);
            }
        }
    }

    private async Task ResummonPetOnEnterAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(player.ActivePetId))
            return;
        var def = FindPet(player.ActivePetId);
        if (def is null || !player.OwnedPets.Contains(def.PetId))
        {
            player.ActivePetId = string.Empty;
            player.ActivePetEntityId = string.Empty;
            return;
        }
        var pet = SpawnPetEntity(player, def);
        player.ActivePetEntityId = pet.EntityId;
        await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntitySpawn,
            new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "PetEnter" }, ct);
        await player.Session.SendAsync(MsgId.EntitySpawn, 0,
            new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "PetEnter" }, ct);
        await BroadcastPetSyncAsync(player, pet, "summon", ct);
    }

    /// <summary>Unlock pet from inventory item (kind pet_unlock).</summary>
    private async Task<(bool Ok, string Message)> TryUnlockPetItemAsync(
        WorldPlayerEntity player, ItemTemplate tmpl, string itemId, CancellationToken ct)
    {
        if (!string.Equals(tmpl.Kind, "pet_unlock", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(tmpl.UnlockPetId))
            return (false, "not_pet");

        string pid = tmpl.UnlockPetId.Trim();
        if (FindPet(pid) is null)
            return (false, "Unknown pet unlock.");
        if (player.OwnedPets.Contains(pid))
            return (false, "Pet already owned.");
        if (!InventoryHelper.TryRemoveItem(player.Inventory, itemId, 1))
            return (false, "Item not in inventory.");
        player.OwnedPets.Add(pid);
        await _storage.Characters.SaveInventoryAsync(
            player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
        await PersistPetsAsync(player, ct);
        return (true, $"Unlocked pet {pid}");
    }
}
