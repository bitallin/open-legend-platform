using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private sealed class WorldBossHateState
    {
        public string CharacterId { get; init; } = "";
        public string Name { get; set; } = "";
        public long Damage { get; set; }
        public long LastHitUnixMs { get; set; }
    }

    private sealed class WorldBossRuntime
    {
        public required WorldBossDefinition Def { get; init; }
        public string? EntityId { get; set; }
        public bool Alive { get; set; }
        public long NextSpawnUnixMs { get; set; }
        public ConcurrentDictionary<string, WorldBossHateState> Hate { get; } = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<string, WorldBossRuntime> _worldBosses = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _worldBossLock = new(1, 1);

    private WorldBossConfigFile WorldBossCfg => _versionPack.WorldBoss ?? new WorldBossConfigFile();

    private void InitWorldBossRuntimes()
    {
        foreach (var def in WorldBossCfg.Bosses)
        {
            if (string.IsNullOrWhiteSpace(def.BossId))
                continue;
            _worldBosses[def.BossId] = new WorldBossRuntime
            {
                Def = def,
                Alive = false,
                NextSpawnUnixMs = 0
            };
        }
        StructuredLogger.Info("GameWorldModule", $"World bosses configured: {_worldBosses.Count}");
    }

    private async Task BootstrapWorldBossesAsync(CancellationToken ct)
    {
        foreach (var runtime in _worldBosses.Values)
        {
            if (runtime.Def.SpawnOnStart)
                await SpawnWorldBossInternalAsync(runtime, reason: "spawn", ct);
            else if (runtime.Def.RespawnSeconds > 0)
                runtime.NextSpawnUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    + runtime.Def.RespawnSeconds * 1000L;
        }
    }

    private async Task TickWorldBossesAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var runtime in _worldBosses.Values.ToList())
        {
            if (runtime.Alive)
                continue;
            if (runtime.NextSpawnUnixMs <= 0)
                continue;
            if (now < runtime.NextSpawnUnixMs)
                continue;
            await SpawnWorldBossInternalAsync(runtime, reason: "respawn", ct);
        }
    }

    private async Task SpawnWorldBossInternalAsync(WorldBossRuntime runtime, string reason, CancellationToken ct)
    {
        await _worldBossLock.WaitAsync(ct);
        try
        {
            // Despawn existing entity if any
            if (!string.IsNullOrEmpty(runtime.EntityId) &&
                _monsters.TryGetValue(runtime.EntityId, out var old) && old.Alive)
            {
                old.Alive = false;
                old.NoRespawn = true;
                old.RespawnAtUnixMs = 0;
                await BroadcastNearAsync(old.MapId, old.X, old.Y, MsgId.EntityDespawn,
                    new EntityDespawn { EntityId = old.EntityId, Reason = "WorldBossDespawn", MapId = old.MapId }, ct);
            }

            if (!_versionPack.Monsters.TryGetValue(runtime.Def.MonsterId, out var tmpl))
            {
                StructuredLogger.Warn("GameWorldModule",
                    $"World boss '{runtime.Def.BossId}' missing monster template '{runtime.Def.MonsterId}'");
                return;
            }

            var monster = CreateMonster(tmpl, runtime.Def.MapId, runtime.Def.X, runtime.Def.Y);
            monster.WorldBossId = runtime.Def.BossId;
            monster.NoRespawn = true;
            monster.RespawnAtUnixMs = 0;
            if (!string.IsNullOrWhiteSpace(runtime.Def.DisplayName))
            {
                // Name is init-only on MonsterEntity — recreate via reflection? Name is `required string Name { get; init; }`
                // Can't change. DisplayName from template is fine; Def.DisplayName used in notifies.
            }

            runtime.EntityId = monster.EntityId;
            runtime.Alive = true;
            runtime.NextSpawnUnixMs = 0;
            runtime.Hate.Clear();

            await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntitySpawn,
                new EntitySpawn { Entity = monster.ToSnapshot(), Reason = "WorldBossSpawn" }, ct);

            var spawnNotify = new WorldBossSpawnNotify
            {
                BossId = runtime.Def.BossId,
                DisplayName = string.IsNullOrWhiteSpace(runtime.Def.DisplayName) ? tmpl.DisplayName : runtime.Def.DisplayName,
                MapId = runtime.Def.MapId,
                X = runtime.Def.X,
                Y = runtime.Def.Y,
                EntityId = monster.EntityId,
                MaxHp = monster.MaxHp,
                Reason = reason
            };
            await BroadcastWorldAsync(MsgId.WorldBossSpawnNotify, spawnNotify, ct);

            await _eventBus.PublishAsync(new WorldBossSpawnedEvent(
                runtime.Def.BossId,
                spawnNotify.DisplayName,
                runtime.Def.MapId,
                runtime.Def.X,
                runtime.Def.Y,
                monster.EntityId,
                reason), ct);

            StructuredLogger.Info("GameWorldModule",
                $"World boss spawned: {runtime.Def.BossId} entity={monster.EntityId} reason={reason}");
        }
        finally
        {
            _worldBossLock.Release();
        }
    }

    private bool TryGetWorldBossRuntime(MonsterEntity monster, out WorldBossRuntime? runtime)
    {
        runtime = null;
        if (string.IsNullOrEmpty(monster.WorldBossId))
            return false;
        return _worldBosses.TryGetValue(monster.WorldBossId, out runtime) && runtime != null;
    }


    private void AddHateDamage(WorldBossHateState state, int damage)
    {
        lock (state)
        {
            state.Damage += damage;
        }
    }

    private List<WorldBossHateEntry> BuildHateTop(WorldBossRuntime runtime, int topN)
    {
        int n = Math.Max(1, topN);
        return runtime.Hate.Values
            .OrderByDescending(h => h.Damage)
            .ThenByDescending(h => h.LastHitUnixMs)
            .Take(n)
            .Select((h, i) => new WorldBossHateEntry
            {
                CharacterId = h.CharacterId,
                Name = h.Name,
                Damage = h.Damage,
                Rank = i + 1
            })
            .ToList();
    }

    private WorldBossHateState? ResolveTopHate(WorldBossRuntime runtime)
    {
        return runtime.Hate.Values
            .OrderByDescending(h => h.Damage)
            .ThenByDescending(h => h.LastHitUnixMs)
            .FirstOrDefault();
    }

    private WorldBossInfo ToBossInfo(WorldBossRuntime runtime)
    {
        var def = runtime.Def;
        int hp = 0, maxHp = 0;
        string entityId = runtime.EntityId ?? "";
        if (!string.IsNullOrEmpty(runtime.EntityId) &&
            _monsters.TryGetValue(runtime.EntityId, out var m))
        {
            hp = m.Alive ? m.Hp : 0;
            maxHp = m.MaxHp;
            entityId = m.EntityId;
        }
        else if (_versionPack.Monsters.TryGetValue(def.MonsterId, out var tmpl))
        {
            maxHp = tmpl.MaxHp;
        }

        long nextUnix = 0;
        if (!runtime.Alive && runtime.NextSpawnUnixMs > 0)
            nextUnix = runtime.NextSpawnUnixMs / 1000;

        var info = new WorldBossInfo
        {
            BossId = def.BossId,
            DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.MonsterId : def.DisplayName,
            MapId = def.MapId,
            X = def.X,
            Y = def.Y,
            Alive = runtime.Alive,
            EntityId = entityId,
            Hp = hp,
            MaxHp = maxHp,
            NextSpawnUnix = nextUnix,
            ParticipateRadius = def.ParticipateRadius,
            RespawnSeconds = def.RespawnSeconds
        };
        foreach (var h in BuildHateTop(runtime, def.HateTopN))
            info.HateTop.Add(h);
        return info;
    }

    private async Task OnWorldBossDamagedAsync(
        WorldPlayerEntity attacker, MonsterEntity monster, int damage, bool killed, CancellationToken ct)
    {
        if (!TryGetWorldBossRuntime(monster, out var runtime) || runtime is null)
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hateState = runtime.Hate.GetOrAdd(attacker.CharacterId, _ => new WorldBossHateState
        {
            CharacterId = attacker.CharacterId,
            Name = attacker.Name
        });
        hateState.Name = attacker.Name;
        AddHateDamage(hateState, damage);
        hateState.LastHitUnixMs = now;

        var hpNotify = new WorldBossHpNotify
        {
            BossId = runtime.Def.BossId,
            EntityId = monster.EntityId,
            Hp = monster.Hp,
            MaxHp = monster.MaxHp,
            AttackerName = attacker.Name,
            Damage = damage
        };
        foreach (var h in BuildHateTop(runtime, runtime.Def.HateTopN))
            hpNotify.HateTop.Add(h);

        // Notify players on the boss map within participate radius (and attacker)
        var aoi = GetAoi(monster.MapId);
        foreach (var p in aoi.AllPlayers())
        {
            int d = CombatMath.Chebyshev(p.X, p.Y, monster.X, monster.Y);
            if (d <= Math.Max(1, runtime.Def.ParticipateRadius) ||
                string.Equals(p.CharacterId, attacker.CharacterId, StringComparison.Ordinal))
            {
                await p.Session.SendAsync(MsgId.WorldBossHpNotify, 0, hpNotify, ct);
            }
        }

        if (killed)
            await FinalizeWorldBossKillAsync(runtime, monster, attacker, ct);
    }

    private async Task FinalizeWorldBossKillAsync(
        WorldBossRuntime runtime, MonsterEntity monster, WorldPlayerEntity lastHitter, CancellationToken ct)
    {
        await _worldBossLock.WaitAsync(ct);
        try
        {
            runtime.Alive = false;
            monster.NoRespawn = true;
            monster.RespawnAtUnixMs = 0;

            int respawnSec = Math.Max(0, runtime.Def.RespawnSeconds);
            runtime.NextSpawnUnixMs = respawnSec <= 0
                ? 0
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + respawnSec * 1000L;

            // Kill credit = top hate (ties: most recent hit among equal damage). Fallback: last hitter.
            var top = ResolveTopHate(runtime);
            string creditCharId = top?.CharacterId ?? lastHitter.CharacterId;
            string creditName = top?.Name ?? lastHitter.Name;
            long topDmg = top?.Damage ?? 0;

            WorldPlayerEntity? creditPlayer = null;
            if (TryFindOnlinePlayerByCharacterId(creditCharId, out var online) && online != null)
                creditPlayer = online;

            int rewardGold = Math.Max(0, runtime.Def.RewardGold);
            var grantedBroadcastItems = new List<(string ItemId, int Count)>();
            var grantedAllItems = new List<(string ItemId, int Count)>();

            // Roll rare drops
            foreach (var drop in runtime.Def.RareDrops ?? new List<WorldBossRareDropConfig>())
            {
                if (string.IsNullOrWhiteSpace(drop.ItemId) || drop.Count <= 0)
                    continue;
                double roll;
                lock (Rng) roll = Rng.NextDouble();
                if (roll > Math.Clamp(drop.Chance, 0.0, 1.0))
                    continue;
                grantedAllItems.Add((drop.ItemId, drop.Count));
                if (drop.Broadcast)
                    grantedBroadcastItems.Add((drop.ItemId, drop.Count));
            }

            // Also roll normal loot table into inventory (no ground loot for world boss)
            if (_versionPack.LootTables.TryGetValue(monster.Template.LootTableId, out var table))
            {
                foreach (var entry in table.Entries)
                {
                    double roll;
                    lock (Rng) roll = Rng.NextDouble();
                    if (roll > entry.Chance)
                        continue;
                    int countMin = Math.Max(1, entry.CountMin);
                    int countMax = Math.Max(countMin, entry.CountMax);
                    int count;
                    lock (Rng) count = Rng.Next(countMin, countMax + 1);
                    grantedAllItems.Add((entry.ItemId, count));
                }
            }

            if (creditPlayer != null)
            {
                if (rewardGold > 0)
                {
                    AdjustGold(creditPlayer, rewardGold);
                    await _storage.Characters.SaveGoldAsync(creditPlayer.CharacterId, creditPlayer.Gold, ct);
                }

                int maxSlots = Math.Max(1, _versionPack.Mail.MaxInventorySlots);
                var pendingMail = new List<MailAttachmentItemRecord>();
                foreach (var (itemId, count) in grantedAllItems)
                {
                    bool hasStack = creditPlayer.Inventory.Any(s =>
                        string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
                    if (!hasStack && creditPlayer.Inventory.Count >= maxSlots)
                    {
                        pendingMail.Add(new MailAttachmentItemRecord { ItemId = itemId, Count = count });
                        continue;
                    }
                    InventoryHelper.AddItem(creditPlayer.Inventory, itemId, count);
                }
                await _storage.Characters.SaveInventoryAsync(
                    creditPlayer.CharacterId, InventoryHelper.Serialize(creditPlayer.Inventory), ct);

                var inv = new InventoryNotify
                {
                    CharacterId = creditPlayer.CharacterId,
                    Gold = creditPlayer.Gold,
                    Reason = "WorldBossReward"
                };
                foreach (var i in InventoryHelper.ToProto(creditPlayer.Inventory))
                    inv.Items.Add(i);
                await creditPlayer.Session.SendAsync(MsgId.InventoryNotify, 0, inv, ct);

                if (pendingMail.Count > 0)
                {
                    await SendMailAsync(
                        creditPlayer.CharacterId,
                        creditPlayer.Name,
                        MailCfg.SystemSenderName,
                        "世界Boss稀有掉落",
                        $"击杀 [{runtime.Def.DisplayName}] 的稀有掉落（背包已满，请领取附件）。",
                        0,
                        pendingMail,
                        ct);
                }

                await GrantExpFromKillAsync(creditPlayer, monster, ct);
            }
            else
            {
                // Offline credit: mail gold + items
                var ch = await _storage.Characters.GetByIdAsync(creditCharId, ct);
                string name = ch?.Name ?? creditName;
                var mailItems = grantedAllItems
                    .Select(g => new MailAttachmentItemRecord { ItemId = g.ItemId, Count = g.Count })
                    .ToList();
                await SendMailAsync(
                    creditCharId,
                    name,
                    MailCfg.SystemSenderName,
                    "世界Boss击杀奖励",
                    $"你以最高仇恨击杀了 [{runtime.Def.DisplayName}]，奖励已发放至邮件。",
                    rewardGold,
                    mailItems,
                    ct);
            }

            // Server-wide rare loot broadcast
            foreach (var (itemId, count) in grantedBroadcastItems)
            {
                string content =
                    $"【世界Boss】{creditName} 击杀了 {runtime.Def.DisplayName}，获得稀有掉落 {ItemDisplayName(itemId)}×{count}！";
                var lootBc = new LootBroadcastNotify
                {
                    NoticeType = "rare_loot",
                    BossId = runtime.Def.BossId,
                    KillerCharacterId = creditCharId,
                    KillerName = creditName,
                    ItemId = itemId,
                    ItemCount = count,
                    Gold = rewardGold,
                    Content = content
                };
                await BroadcastWorldAsync(MsgId.LootBroadcastNotify, lootBc, ct);
                await BroadcastWorldAsync(MsgId.ServerNotice, new ServerNotice
                {
                    NoticeType = "rare_loot",
                    Title = "世界Boss稀有掉落",
                    Content = content,
                    GrantedItemId = itemId,
                    GrantedItemCount = count
                }, ct);

                await _eventBus.PublishAsync(new RareLootBroadcastEvent(
                    runtime.Def.BossId, creditCharId, creditName, itemId, count, rewardGold, content), ct);
            }

            // If no broadcast items but we still want a kill announce when gold-only
            if (grantedBroadcastItems.Count == 0 && rewardGold > 0)
            {
                string content =
                    $"【世界Boss】{creditName} 击杀了 {runtime.Def.DisplayName}，获得 {rewardGold} 金！";
                await BroadcastWorldAsync(MsgId.ServerNotice, new ServerNotice
                {
                    NoticeType = "world_boss_kill",
                    Title = "世界Boss击杀",
                    Content = content
                }, ct);
            }

            await _eventBus.PublishAsync(new WorldBossKilledEvent(
                runtime.Def.BossId,
                runtime.Def.DisplayName,
                creditCharId,
                creditName,
                topDmg,
                monster.MapId,
                monster.X,
                monster.Y), ct);

            StructuredLogger.Info("GameWorldModule",
                $"World boss killed: {runtime.Def.BossId} credit={creditName} dmg={topDmg}");
        }
        finally
        {
            _worldBossLock.Release();
        }
    }

    private string ItemDisplayName(string itemId)
    {
        if (_versionPack.Items.TryGetValue(itemId, out var tmpl) && !string.IsNullOrWhiteSpace(tmpl.DisplayName))
            return tmpl.DisplayName;
        return itemId;
    }

    private async Task BroadcastWorldAsync(MsgId msgId, Google.Protobuf.IMessage msg, CancellationToken ct)
    {
        foreach (var p in EnumerateOnlinePlayers())
        {
            try
            {
                await p.Session.SendAsync(msgId, 0, msg, ct);
            }
            catch
            {
                // ignore disconnect races
            }
        }
    }

    private async Task HandleWorldBossStatusAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = WorldBossStatusReq.Parser.ParseFrom(frame.Payload);
        var resp = new WorldBossStatusResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        };

        if (!string.IsNullOrWhiteSpace(req.BossId))
        {
            if (!_worldBosses.TryGetValue(req.BossId, out var runtime))
            {
                await session.SendAsync(MsgId.WorldBossStatusResp, frame.Seq, new WorldBossStatusResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrWorldBossDenied,
                    Message = $"未知世界Boss: {req.BossId}"
                }, ct);
                return;
            }
            resp.Bosses.Add(ToBossInfo(runtime));
        }
        else
        {
            foreach (var runtime in _worldBosses.Values.OrderBy(r => r.Def.BossId, StringComparer.Ordinal))
                resp.Bosses.Add(ToBossInfo(runtime));
        }

        await session.SendAsync(MsgId.WorldBossStatusResp, frame.Seq, resp, ct);
    }

    private async Task HandleWorldBossControlAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = WorldBossControlReq.Parser.ParseFrom(frame.Payload);
        string action = (req.Action ?? "").Trim().ToLowerInvariant();
        string bossId = (req.BossId ?? "").Trim();

        if (string.IsNullOrEmpty(bossId) && _worldBosses.Count == 1)
            bossId = _worldBosses.Keys.First();

        if (string.IsNullOrEmpty(bossId) || !_worldBosses.TryGetValue(bossId, out var runtime))
        {
            await session.SendAsync(MsgId.WorldBossControlResp, frame.Seq, new WorldBossControlResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrWorldBossDenied,
                Message = string.IsNullOrEmpty(bossId) ? "请指定 bossId" : $"未知世界Boss: {bossId}"
            }, ct);
            return;
        }

        if (action is "force_spawn" or "spawn" or "refresh")
        {
            await SpawnWorldBossInternalAsync(runtime, reason: "force_spawn", ct);
            await session.SendAsync(MsgId.WorldBossControlResp, frame.Seq, new WorldBossControlResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = $"已强制刷新世界Boss [{runtime.Def.DisplayName}]",
                Boss = ToBossInfo(runtime)
            }, ct);
            return;
        }

        if (action is "despawn" or "kill")
        {
            await _worldBossLock.WaitAsync(ct);
            try
            {
                if (!string.IsNullOrEmpty(runtime.EntityId) &&
                    _monsters.TryGetValue(runtime.EntityId, out var m) && m.Alive)
                {
                    m.Alive = false;
                    m.Hp = 0;
                    m.NoRespawn = true;
                    m.RespawnAtUnixMs = 0;
                    await BroadcastNearAsync(m.MapId, m.X, m.Y, MsgId.EntityDespawn,
                        new EntityDespawn { EntityId = m.EntityId, Reason = "WorldBossDespawn", MapId = m.MapId }, ct);
                }
                runtime.Alive = false;
                runtime.Hate.Clear();
                runtime.NextSpawnUnixMs = 0;
            }
            finally
            {
                _worldBossLock.Release();
            }

            await session.SendAsync(MsgId.WorldBossControlResp, frame.Seq, new WorldBossControlResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "世界Boss已移除（测试）",
                Boss = ToBossInfo(runtime)
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.WorldBossControlResp, frame.Seq, new WorldBossControlResp
        {
            Success = false,
            ErrorCode = ProtocolErrorCode.ErrWorldBossDenied,
            Message = $"未知控制指令: {req.Action}",
            Boss = ToBossInfo(runtime)
        }, ct);
    }
}
