using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly List<GroundHazard> _grounds = new();
    private bool _siegeOpenedBySchedule;
    private readonly object _groundGate = new();
    private readonly ConcurrentDictionary<(string Map, int X, int Y), byte> _breaches = new();
    private readonly ConcurrentDictionary<string, long> _poisonUntil = new();
    private readonly ConcurrentDictionary<string, byte> _guildWars = new(StringComparer.Ordinal);

    private sealed class GroundHazard
    {
        public string MapId { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public int Radius { get; init; } = 1;
        public int Damage { get; init; } = 1;
        public string OwnerId { get; init; } = "";
        public string SkillId { get; init; } = "";
        public long ExpiresAt { get; init; }
        public long NextTick { get; set; }
    }

    private bool GateBlocks(string mapId, int x, int y)
    {
        if (!_versionPack.Maps.TryGetValue(mapId, out var map))
            return false;
        var portal = map.FindPortalAt(x, y);
        if (portal is null || !string.Equals(portal.PortalId, _versionPack.Siege.GatePortalId, StringComparison.OrdinalIgnoreCase))
            return false;
        string gateId = string.IsNullOrWhiteSpace(_versionPack.Siege.GateMonsterId) ? "castle_gate" : _versionPack.Siege.GateMonsterId;
        return _monsters.Values.Any(m =>
            m.Alive &&
            string.Equals(m.MapId, mapId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.MonsterId, gateId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task OnStructureDestroyedAsync(MonsterEntity monster, CancellationToken ct)
    {
        if (!string.Equals(monster.MonsterId, _versionPack.Siege.GateMonsterId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(monster.MonsterId, "castle_wall", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.Equals(monster.MonsterId, "castle_wall", StringComparison.OrdinalIgnoreCase))
        {
            _breaches.TryAdd((monster.MapId, monster.X, monster.Y), 0);
            _breaches.TryAdd((monster.MapId, monster.X + 1, monster.Y), 0);
        }
        var notice = new ServerNotice
        {
            NoticeType = "gate",
            Title = "城门",
            Content = string.Equals(monster.MonsterId, "castle_wall", StringComparison.OrdinalIgnoreCase)
                ? "城墙被打出缺口。"
                : "沙门城门被打破，可以进入皇宫区。"
        };
        foreach (var p in EnumerateOnlinePlayers())
        {
            if (string.Equals(p.MapId, monster.MapId, StringComparison.OrdinalIgnoreCase))
                await p.Session.SendAsync(MsgId.ServerNotice, 0, notice, ct);
        }
    }

    private async Task PlaceGroundFireAsync(WorldPlayerEntity attacker, int x, int y, SkillConfigItem skill, CancellationToken ct)
    {
        if (skill.GroundDurationMs <= 0)
            return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int damage = Math.Max(1, CombatMath.CalcDamage(attacker.Atk, 0, Math.Max(0.4, skill.DamageMultiplier * 0.45)));
        var hazard = new GroundHazard
        {
            MapId = attacker.MapId,
            X = x,
            Y = y,
            Radius = skill.AoeRadius <= 0 ? 1 : skill.AoeRadius,
            Damage = damage,
            OwnerId = attacker.CharacterId,
            SkillId = skill.SkillId,
            ExpiresAt = now + skill.GroundDurationMs,
            NextTick = now + Math.Max(400, skill.GroundTickMs)
        };
        lock (_groundGate)
            _grounds.Add(hazard);
        var notify = new GroundEffectNotify
        {
            MapId = hazard.MapId,
            X = hazard.X,
            Y = hazard.Y,
            Radius = hazard.Radius,
            DurationMs = skill.GroundDurationMs,
            SkillId = skill.SkillId
        };
        await BroadcastNearAsync(attacker.MapId, x, y, MsgId.GroundEffectNotify, notify, ct);
        await attacker.Session.SendAsync(MsgId.GroundEffectNotify, 0, notify, ct);
    }

    private async Task TickGroundsAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<GroundHazard> due;
        lock (_groundGate)
        {
            _grounds.RemoveAll(g => g.ExpiresAt <= now);
            due = _grounds.Where(g => g.NextTick <= now).ToList();
            foreach (var g in due)
                g.NextTick = now + 1000;
        }
        foreach (var g in due)
        {
            var owner = FindOnlineByCharacterId(g.OwnerId);
            foreach (var monster in _monsters.Values.ToList())
            {
                if (!monster.Alive || !string.Equals(monster.MapId, g.MapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (CombatMath.Chebyshev(g.X, g.Y, monster.X, monster.Y) > g.Radius)
                    continue;
                monster.Hp = Math.Max(0, monster.Hp - g.Damage);
                bool killed = monster.Hp <= 0;
                if (owner != null)
                    await SendCombatAsync(owner, monster.EntityId, monster.Hp, monster.MaxHp, g.SkillId, g.Damage, killed, ct);
                if (!killed)
                    continue;
                monster.Alive = false;
                monster.RespawnAtUnixMs = now + Math.Max(1000, monster.Template.RespawnMs);
                await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntityDespawn,
                    new EntityDespawn { EntityId = monster.EntityId, Reason = "Killed", MapId = monster.MapId }, ct);
                if (owner != null)
                {
                    await DropLootAsync(monster, owner, ct);
                    await GrantExpFromKillAsync(owner, monster, ct);
                    await UpdateQuestKillProgressAsync(owner, monster.MonsterId, ct);
                }
                await OnStructureDestroyedAsync(monster, ct);
            }
        }
    }

    private async Task TickSiegeScheduleAsync(CancellationToken ct)
    {
        int open = _versionPack.Siege.OpenHour;
        int close = _versionPack.Siege.CloseHour;
        if (open < 0)
            return;
        bool window = PhaseNRules.InHourWindow(DateTime.UtcNow.Hour, open, close);
        if (window && !_siegeIsActive && _siegeDeclaredGuilds.Count > 0)
        {
            _siegeIsActive = true;
            _siegeOpenedBySchedule = true;
            await BroadcastSiegeStateNotifyAsync("schedule_start", ct);
        }
        else if (!window && _siegeIsActive && _siegeOpenedBySchedule)
        {
            _siegeOpenedBySchedule = false;
            await FinishSiegeAsync(ct);
        }
    }

    private async Task ApplyDeathDropAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        if (_siegeIsActive)
            return;
        var rules = _versionPack.Levels;
        int loss = PhaseNRules.DeathGoldLoss(player.Gold, rules.DeathGoldPercent);
        if (loss > 0)
        {
            AdjustGold(player, -(loss));
            await DropGoldPileAsync(player, loss, ct);
        }
        int maxItems = Math.Max(0, rules.DeathMaxItems);
        int chance = Math.Clamp(rules.DeathItemChancePercent, 0, 100);
        var candidates = player.Inventory.Where(s => s.Count > 0 && !s.ItemId.Contains("skillbook", StringComparison.OrdinalIgnoreCase)).ToList();
        int dropped = 0;
        foreach (var slot in candidates)
        {
            if (dropped >= maxItems)
                break;
            int roll;
            lock (Rng) roll = Rng.Next(100);
            if (roll >= chance)
                continue;
            if (!InventoryHelper.TryRemoveItem(player.Inventory, slot.ItemId, 1))
                continue;
            await SpawnLootAsync(player.MapId, player.X, player.Y, slot.ItemId, 1, player.CharacterId, ct);
            dropped++;
        }
        if (loss > 0 || dropped > 0)
        {
            await PersistEconomyAsync(player, ct);
            await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
            {
                NoticeType = "death_drop",
                Title = "死亡掉落",
                Content = loss > 0 ? $"掉落了 {loss} 金币" : "掉落了一件物品"
            }, ct);
        }
    }

    private async Task DropGoldPileAsync(WorldPlayerEntity player, int gold, CancellationToken ct)
    {
        await SpawnLootAsync(player.MapId, player.X, player.Y, "item_copper_coin", Math.Max(1, gold / 5), player.CharacterId, ct);
    }

    private async Task SpawnLootAsync(string mapId, int x, int y, string itemId, int count, string ownerId, CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int lootId = Interlocked.Increment(ref _lootSeq);
        var loot = new LootEntity
        {
            EntityId = $"loot_{lootId}",
            MapId = mapId,
            ItemId = itemId,
            Count = Math.Max(1, count),
            X = x,
            Y = y,
            OwnerCharacterId = ownerId,
            FreeForAllUnixMs = now + 15000,
            ExpireUnixMs = now + 60000
        };
        _loots[loot.EntityId] = loot;
        var notify = new LootDropNotify
        {
            Loot = loot.ToSnapshot(),
            SourceEntityId = ownerId,
            KillerCharacterId = ownerId
        };
        await BroadcastNearAsync(mapId, x, y, MsgId.LootDropNotify, notify, ct);
        await BroadcastNearAsync(mapId, x, y, MsgId.EntitySpawn, new EntitySpawn { Entity = loot.ToSnapshot(), Reason = "DeathDrop" }, ct);
    }

    private async Task<bool> TryRandomScrollAsync(IClientSession session, WorldPlayerEntity player, GridAoiManager fromAoi, CancellationToken ct)
    {
        var map = fromAoi.Map;
        int tx = map.SpawnX;
        int ty = map.SpawnY;
        for (int i = 0; i < 30; i++)
        {
            int x, y;
            lock (Rng)
            {
                x = Rng.Next(1, Math.Max(2, map.Width - 1));
                y = Rng.Next(1, Math.Max(2, map.Height - 1));
            }
            if (map.IsBlocked(x, y) || map.IsInSafeZone(x, y))
                continue;
            tx = x;
            ty = y;
            break;
        }
        var portal = new PortalConfig
        {
            PortalId = "random",
            TargetMapId = map.MapId,
            TargetX = tx,
            TargetY = ty
        };
        await TransferViaPortalAsync(session, player, fromAoi, portal, ct);
        return true;
    }

    private async Task HandleWorldTravelAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = WorldTravelReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out var aoi) || player is null || aoi is null)
        {
            await session.SendAsync(MsgId.WorldTravelResp, frame.Seq, new WorldTravelResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!player.InSafeZone && !aoi.Map.IsInSafeZone(player.X, player.Y))
        {
            await session.SendAsync(MsgId.WorldTravelResp, frame.Seq, new WorldTravelResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Message = "只能在安全区使用世界地图传送"
            }, ct);
            return;
        }
        string dest = (req.MapId ?? "").Trim();
        if (!PhaseNRules.WorldLinked(player.MapId, dest) || !_versionPack.Maps.ContainsKey(dest))
        {
            await session.SendAsync(MsgId.WorldTravelResp, frame.Seq, new WorldTravelResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Message = "这张地图和当前城镇不相邻"
            }, ct);
            return;
        }
        if (string.Equals(dest, "dark_forest", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(player.MapId, "sand_gate", StringComparison.OrdinalIgnoreCase) &&
            GateBlocks(player.MapId, FindGateCell(player.MapId).X, FindGateCell(player.MapId).Y))
        {
            await session.SendAsync(MsgId.WorldTravelResp, frame.Seq, new WorldTravelResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Message = "城门还没破，不能进入皇宫"
            }, ct);
            return;
        }
        var home = _versionPack.GetMap(dest);
        await TransferViaPortalAsync(session, player, aoi, new PortalConfig
        {
            PortalId = "world",
            TargetMapId = home.MapId,
            TargetX = home.SpawnX,
            TargetY = home.SpawnY
        }, ct);
        await session.SendAsync(MsgId.WorldTravelResp, frame.Seq, new WorldTravelResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = home.MapName
        }, ct);
    }

    private (int X, int Y) FindGateCell(string mapId)
    {
        if (_versionPack.Maps.TryGetValue(mapId, out var map))
        {
            var portal = map.Portals.FirstOrDefault(p => string.Equals(p.PortalId, _versionPack.Siege.GatePortalId, StringComparison.OrdinalIgnoreCase));
            if (portal != null)
                return (portal.X, portal.Y);
        }
        return (18, 15);
    }

    private async Task HandleMineAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = MineReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.MineResp, frame.Seq, new MineResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        bool onVein = string.Equals(player.MapId, "bone_path", StringComparison.OrdinalIgnoreCase)
                      && CombatMath.Chebyshev(player.X, player.Y, 28, 8) <= 1;
        if (!onVein)
        {
            await session.SendAsync(MsgId.MineResp, frame.Seq, new MineResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrLootDenied,
                Message = "这里没有矿脉"
            }, ct);
            return;
        }
        InventoryHelper.AddItem(player.Inventory, "item_rough_ore", 1);
        await PersistEconomyAsync(player, ct);
        var resp = new MineResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "挖到粗矿"
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);
        await session.SendAsync(MsgId.MineResp, frame.Seq, resp, ct);
    }

    private async Task<bool> TryChatUtilityAsync(WorldPlayerEntity player, string content, ChatChannel channel, CancellationToken ct)
    {
        if (content.StartsWith("宣战", StringComparison.Ordinal) && channel == ChatChannel.ChatGuild)
        {
            if (!string.Equals(player.GuildRank, "leader", StringComparison.OrdinalIgnoreCase))
            {
                await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "guild", Title = "宣战", Content = "只有会长能宣战" }, ct);
                return true;
            }
            string name = content["宣战".Length..].Trim();
            var other = await _storage.Guilds.GetByNameAsync(name, ct);
            if (other is null)
            {
                await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "guild", Title = "宣战", Content = "没有这个行会" }, ct);
                return true;
            }
            DeclareGuildWar(player.GuildId, other.GuildId);
            await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "guild", Title = "宣战", Content = "已向 " + other.Name + " 宣战，双方可互相攻击" }, ct);
            return true;
        }
        if (content.StartsWith("/屏蔽", StringComparison.Ordinal))
        {
            string name = content["/屏蔽".Length..].Trim();
            if (name.Length == 0)
            {
                await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "social", Title = "屏蔽", Content = "用法：/屏蔽 角色名" }, ct);
                return true;
            }
            player.Blacklist ??= new();
            if (!player.Blacklist.Contains(name, StringComparer.OrdinalIgnoreCase))
                player.Blacklist.Add(name);
            await PersistAppearanceAsync(player, ct);
            await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "social", Title = "屏蔽", Content = "已屏蔽 " + name }, ct);
            return true;
        }
        if (content.StartsWith("/拜师", StringComparison.Ordinal))
        {
            string name = content["/拜师".Length..].Trim();
            var mentor = FindOnlineByName(name);
            if (mentor is null || mentor.CharacterId == player.CharacterId)
            {
                await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "social", Title = "师徒", Content = "师父不在线" }, ct);
                return true;
            }
            player.MentorName = mentor.Name;
            await PersistAppearanceAsync(player, ct);
            await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "social", Title = "师徒", Content = "你拜了 " + mentor.Name + " 为师" }, ct);
            await mentor.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "social", Title = "师徒", Content = player.Name + " 拜你为师" }, ct);
            return true;
        }
        if (channel == ChatChannel.ChatGuild && content.StartsWith("公告", StringComparison.Ordinal))
        {
            if (!string.Equals(player.GuildRank, "leader", StringComparison.OrdinalIgnoreCase))
            {
                await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "guild", Title = "公告", Content = "只有会长能改公告" }, ct);
                return true;
            }
            string text = content["公告".Length..].Trim().TrimStart(':', '：', ' ');
            if (text.Length == 0)
                return true;
            var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
            if (guild != null)
            {
                var map = GuildRanksJson.Parse(guild.RanksJson);
                map["__notice"] = text.Length > 80 ? text[..80] : text;
                await _storage.Guilds.SaveRanksAsync(guild.GuildId, GuildRanksJson.Serialize(map), ct);
                guild.RanksJson = GuildRanksJson.Serialize(map);
            }
            await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice { NoticeType = "guild", Title = "公告", Content = "行会公告已更新" }, ct);
            return true;
        }
        return false;
    }

    internal static string ReadGuildNotice(string? ranksJson)
    {
        var map = GuildRanksJson.Parse(ranksJson);
        return map.TryGetValue("__notice", out var text) ? text : "";
    }

    private bool IsBreached(string mapId, int x, int y) => _breaches.ContainsKey((mapId, x, y));

    private static bool IsHidden(WorldPlayerEntity player)
        => player.ActiveBuffs.Any(b => string.Equals(b.BuffId, "buff_hide", StringComparison.OrdinalIgnoreCase));

    private bool GuildsAtWar(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.Equals(a, b, StringComparison.Ordinal))
            return false;
        string key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        return _guildWars.ContainsKey(key);
    }

    private void DeclareGuildWar(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return;
        string key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        _guildWars[key] = 0;
    }

    private void PoisonMonster(MonsterEntity monster, string debuffId)
    {
        if (!string.Equals(debuffId, "debuff_poison", StringComparison.OrdinalIgnoreCase))
            return;
        _poisonUntil[monster.EntityId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 8000;
    }

    private async Task TickPoisonAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kv in _poisonUntil.ToArray())
        {
            if (kv.Value <= now || !_monsters.TryGetValue(kv.Key, out var monster) || !monster.Alive)
            {
                _poisonUntil.TryRemove(kv.Key, out _);
                continue;
            }
            monster.Hp = Math.Max(0, monster.Hp - 4);
            if (monster.Hp > 0)
                continue;
            monster.Alive = false;
            monster.RespawnAtUnixMs = now + Math.Max(1000, monster.Template.RespawnMs);
            await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntityDespawn,
                new EntityDespawn { EntityId = monster.EntityId, Reason = "Killed", MapId = monster.MapId }, ct);
            _poisonUntil.TryRemove(kv.Key, out _);
        }
    }

    private async Task KnockMonsterAsync(WorldPlayerEntity attacker, MonsterEntity monster, CancellationToken ct)
    {
        int nx = monster.X + Math.Sign(monster.X - attacker.X);
        int ny = monster.Y + Math.Sign(monster.Y - attacker.Y);
        if (nx == monster.X && ny == monster.Y)
            nx += 1;
        if (!_versionPack.Maps.TryGetValue(monster.MapId, out var map) || map.IsBlocked(nx, ny))
            return;
        int ox = monster.X;
        int oy = monster.Y;
        monster.X = nx;
        monster.Y = ny;
        await BroadcastNearAsync(monster.MapId, nx, ny, MsgId.MoveSync, new MoveSync
        {
            EntityId = monster.EntityId,
            FromX = ox,
            FromY = oy,
            ToX = nx,
            ToY = ny,
            Direction = 4,
            Accepted = true,
            Reason = "Repulse"
        }, ct);
    }

    private async Task StrikeLineAsync(
        IClientSession session,
        WorldPlayerEntity attacker,
        int targetX,
        int targetY,
        SkillConfigItem skill,
        double dmgMult,
        CancellationToken ct)
    {
        int dx = Math.Sign(targetX - attacker.X);
        int dy = Math.Sign(targetY - attacker.Y);
        if (dx == 0 && dy == 0)
            return;
        for (int i = 1; i <= Math.Max(1, skill.Range); i++)
        {
            int cx = attacker.X + dx * i;
            int cy = attacker.Y + dy * i;
            if (skill.GroundDurationMs > 0)
                await PlaceGroundFireAsync(attacker, cx, cy, skill, ct);
            foreach (var other in _monsters.Values.ToList())
            {
                if (!other.Alive || other.X != cx || other.Y != cy)
                    continue;
                if (!string.Equals(other.MapId, attacker.MapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                await HitSplashedMonsterAsync(session, attacker, other, skill, dmgMult, ct);
            }
        }
    }

    private async Task ShareBuffWithPartyAsync(WorldPlayerEntity attacker, string buffId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(attacker.PartyId) || string.IsNullOrEmpty(buffId))
            return;
        if (!_maps.TryGetValue(attacker.MapId, out var aoi))
            return;
        foreach (var other in aoi.AllPlayers())
        {
            if (other.EntityId == attacker.EntityId || other.PartyId != attacker.PartyId)
                continue;
            if (CombatMath.Chebyshev(attacker.X, attacker.Y, other.X, other.Y) > 6)
                continue;
            await ApplyBuffAsync(other, buffId, ct);
        }
    }
}
