using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

/// <summary>
/// M2 combat + M3 NPC shop/quests (authoritative):
/// - Safe zone: ALL attacks forbidden (PvE and PvP). Monsters spawn outside safe zones.
/// - PvP: disabled (only monsters are valid Attack targets).
/// - Loot ownership: killer-only until FreeForAll (15s), then anyone; despawn at 60s.
/// - Player death: revive at current map spawn with full HP/MP.
/// - NPC talk/buy/quest: Chebyshev range; gold + quest_json persisted.
/// </summary>
public sealed partial class GameWorldModule : IGameModule
{
    private readonly IEventBus _eventBus;
    private LoadedVersionPack _versionPack;
    private readonly ILegendStorage _storage;
    private readonly IAntiCheatProbe _antiCheatProbe;
    private readonly ConcurrentDictionary<string, GridAoiManager> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MonsterEntity> _monsters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LootEntity> _loots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, NpcEntity> _npcs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _combatLock = new();
    private CancellationTokenSource? _aiCts;
    private Task? _aiTask;
    private int _monsterSeq;
    private int _lootSeq;
    private static readonly Random Rng = new();

    public const int LootOwnerMs = 15_000;
    public const int LootDespawnMs = 60_000;
    public const int PickupRange = 2;
    public const int NpcInteractRange = 3;

    public GameWorldModule(
        IEventBus eventBus,
        LoadedVersionPack versionPack,
        ILegendStorage storage,
        IAntiCheatProbe? antiCheatProbe = null)
    {
        _eventBus = eventBus;
        _versionPack = versionPack;
        _storage = storage;
        _antiCheatProbe = antiCheatProbe ?? new DefaultAntiCheatProbe(maxStepDistance: 2);

        foreach (var map in versionPack.Maps.Values)
            _maps[map.MapId] = new GridAoiManager(map);
    }

    public string ModuleName => "GameWorldModule";
    public int OnlineCount => _maps.Values.Sum(m => m.OnlineCount);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        SpawnInitialMonsters();
        SpawnNpcs();
        await LoadSiegeStateAsync(ct);
        InitWorldBossRuntimes();
        await BootstrapWorldBossesAsync(ct);
        _aiCts = new CancellationTokenSource();
        _aiTask = Task.Run(() => AiLoopAsync(_aiCts.Token), _aiCts.Token);
    }

    public bool CanHandle(MsgId msgId) => msgId is
        MsgId.EnterWorldReq or MsgId.MoveReq or MsgId.AttackReq or MsgId.PickupReq or
        MsgId.TalkReq or MsgId.ShopBuyReq or MsgId.ShopSellReq or MsgId.RepairReq or
        MsgId.StorageListReq or MsgId.StorageDepositReq or MsgId.StorageWithdrawReq or
        MsgId.QuestAcceptReq or MsgId.QuestTurnInReq or
        MsgId.EquipReq or MsgId.UnequipReq or MsgId.EnhanceEquipReq or MsgId.UseItemReq or
        MsgId.GuildCreateReq or MsgId.GuildJoinReq or MsgId.GuildLeaveReq or MsgId.GuildInfoReq or
        MsgId.ChatReq or
        MsgId.StallListReq or MsgId.StallBrowseReq or MsgId.StallBuyReq or MsgId.StallCancelReq or
        MsgId.PartyInviteReq or MsgId.PartyAcceptReq or MsgId.PartyLeaveReq or MsgId.PartyKickReq or
        MsgId.FriendAddReq or MsgId.FriendRemoveReq or MsgId.FriendListReq or
        MsgId.LearnSkillReq or MsgId.SkillListReq or
        MsgId.SiegeDeclareReq or MsgId.SiegeStatusReq or MsgId.SiegeControlReq or MsgId.SiegeCaptureReq or
        MsgId.MailListReq or MsgId.MailReadReq or MsgId.MailClaimAttachmentReq or MsgId.MailDeleteReq or MsgId.MailSendReq or
        MsgId.DungeonEnterReq or MsgId.DungeonLeaveReq or MsgId.DungeonStatusReq or
        MsgId.WorldBossStatusReq or MsgId.WorldBossControlReq or
        MsgId.MountStatusReq or MsgId.MountSummonReq or MsgId.MountDismissReq or
        MsgId.FashionStatusReq or MsgId.FashionEquipReq or
        MsgId.PetStatusReq or MsgId.PetSummonReq or MsgId.PetDismissReq or
        MsgId.GuildWarehouseListReq or MsgId.GuildWarehouseDepositReq or MsgId.GuildWarehouseWithdrawReq or MsgId.GuildWarehouseClearReq or
        MsgId.GuildSetRankReq or MsgId.GuildWarehouseListAuditReq or
        MsgId.PaymentCreateOrderReq or MsgId.PaymentOrderStatusReq;

    private GridAoiManager GetAoi(string mapId)
    {
        if (_maps.TryGetValue(mapId, out var aoi))
            return aoi;
        if (_instanceMapConfigs.TryGetValue(mapId, out var instMap))
            return _maps.GetOrAdd(mapId, _ => new GridAoiManager(instMap));
        var map = _versionPack.GetMap(mapId);
        return _maps.GetOrAdd(map.MapId, _ => new GridAoiManager(map));
    }

    private void SpawnInitialMonsters()
    {
        foreach (var spawn in _versionPack.MonsterSpawns)
        {
            if (!_versionPack.Monsters.TryGetValue(spawn.MonsterId, out var tmpl))
                continue;
            var map = _versionPack.GetMap(spawn.MapId);
            int count = Math.Max(1, spawn.Count);
            for (int i = 0; i < count; i++)
            {
                int x = spawn.X + (i % 3);
                int y = spawn.Y + (i / 3);
                if (map.IsBlocked(x, y) || map.IsInSafeZone(x, y))
                {
                    x = spawn.X;
                    y = spawn.Y;
                }
                CreateMonster(tmpl, spawn.MapId, x, y);
            }
        }
        StructuredLogger.Info("GameWorldModule", $"Spawned {_monsters.Count} monsters from version pack");
    }

    private MonsterEntity CreateMonster(MonsterTemplate tmpl, string mapId, int x, int y)
    {
        int id = Interlocked.Increment(ref _monsterSeq);
        var m = new MonsterEntity
        {
            EntityId = $"mob_{tmpl.MonsterId}_{id}",
            MonsterId = tmpl.MonsterId,
            Name = tmpl.DisplayName,
            MapId = mapId,
            Template = tmpl,
            SpawnX = x,
            SpawnY = y,
            X = x,
            Y = y,
            Hp = tmpl.MaxHp,
            MaxHp = tmpl.MaxHp,
            Alive = true
        };
        _monsters[m.EntityId] = m;
        return m;
    }


    private void SpawnNpcs()
    {
        foreach (var tmpl in _versionPack.Npcs.Values)
        {
            var npc = new NpcEntity
            {
                EntityId = tmpl.NpcId.StartsWith("npc_", StringComparison.OrdinalIgnoreCase)
                    ? tmpl.NpcId
                    : $"npc_{tmpl.NpcId}",
                Template = tmpl
            };
            _npcs[npc.EntityId] = npc;
        }
        StructuredLogger.Info("GameWorldModule", $"Spawned {_npcs.Count} NPCs from version pack");
    }

    public async Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default)
    {
        switch (frame.MsgId)
        {
            case MsgId.EnterWorldReq:
                await HandleEnterWorldAsync(session, frame, ct);
                break;
            case MsgId.MoveReq:
                await HandleMoveAsync(session, frame, ct);
                break;
            case MsgId.AttackReq:
                await HandleAttackAsync(session, frame, ct);
                break;
            case MsgId.PickupReq:
                await HandlePickupAsync(session, frame, ct);
                break;
            case MsgId.TalkReq:
                await HandleTalkAsync(session, frame, ct);
                break;
            case MsgId.ShopBuyReq:
                await HandleShopBuyAsync(session, frame, ct);
                break;
            case MsgId.ShopSellReq:
                await HandleShopSellAsync(session, frame, ct);
                break;
            case MsgId.RepairReq:
                await HandleRepairAsync(session, frame, ct);
                break;
            case MsgId.StorageListReq:
                await HandleStorageListAsync(session, frame, ct);
                break;
            case MsgId.StorageDepositReq:
                await HandleStorageDepositAsync(session, frame, ct);
                break;
            case MsgId.StorageWithdrawReq:
                await HandleStorageWithdrawAsync(session, frame, ct);
                break;
            case MsgId.QuestAcceptReq:
                await HandleQuestAcceptAsync(session, frame, ct);
                break;
            case MsgId.QuestTurnInReq:
                await HandleQuestTurnInAsync(session, frame, ct);
                break;
            case MsgId.EquipReq:
                await HandleEquipAsync(session, frame, ct);
                break;
            case MsgId.UnequipReq:
                await HandleUnequipAsync(session, frame, ct);
                break;
            case MsgId.EnhanceEquipReq:
                await HandleEnhanceEquipAsync(session, frame, ct);
                break;
            case MsgId.UseItemReq:
                await HandleUseItemAsync(session, frame, ct);
                break;
            case MsgId.GuildCreateReq:
                await HandleGuildCreateAsync(session, frame, ct);
                break;
            case MsgId.GuildJoinReq:
                await HandleGuildJoinAsync(session, frame, ct);
                break;
            case MsgId.GuildLeaveReq:
                await HandleGuildLeaveAsync(session, frame, ct);
                break;
            case MsgId.GuildInfoReq:
                await HandleGuildInfoAsync(session, frame, ct);
                break;
            case MsgId.ChatReq:
                await HandleChatAsync(session, frame, ct);
                break;
            case MsgId.StallListReq:
                await HandleStallListAsync(session, frame, ct);
                break;
            case MsgId.StallBrowseReq:
                await HandleStallBrowseAsync(session, frame, ct);
                break;
            case MsgId.StallBuyReq:
                await HandleStallBuyAsync(session, frame, ct);
                break;
            case MsgId.StallCancelReq:
                await HandleStallCancelAsync(session, frame, ct);
                break;
            case MsgId.PartyInviteReq:
                await HandlePartyInviteAsync(session, frame, ct);
                break;
            case MsgId.PartyAcceptReq:
                await HandlePartyAcceptAsync(session, frame, ct);
                break;
            case MsgId.PartyLeaveReq:
                await HandlePartyLeaveAsync(session, frame, ct);
                break;
            case MsgId.PartyKickReq:
                await HandlePartyKickAsync(session, frame, ct);
                break;
            case MsgId.FriendAddReq:
                await HandleFriendAddAsync(session, frame, ct);
                break;
            case MsgId.FriendRemoveReq:
                await HandleFriendRemoveAsync(session, frame, ct);
                break;
            case MsgId.FriendListReq:
                await HandleFriendListAsync(session, frame, ct);
                break;
            case MsgId.LearnSkillReq:
                await HandleLearnSkillAsync(session, frame, ct);
                break;
            case MsgId.SkillListReq:
                await HandleSkillListAsync(session, frame, ct);
                break;
            case MsgId.SiegeDeclareReq:
                await HandleSiegeDeclareAsync(session, frame, ct);
                break;
            case MsgId.SiegeStatusReq:
                await HandleSiegeStatusAsync(session, frame, ct);
                break;
            case MsgId.SiegeControlReq:
                await HandleSiegeControlAsync(session, frame, ct);
                break;
            case MsgId.SiegeCaptureReq:
                await HandleSiegeCaptureAsync(session, frame, ct);
                break;
            case MsgId.MailListReq:
                await HandleMailListAsync(session, frame, ct);
                break;
            case MsgId.MailReadReq:
                await HandleMailReadAsync(session, frame, ct);
                break;
            case MsgId.MailClaimAttachmentReq:
                await HandleMailClaimAsync(session, frame, ct);
                break;
            case MsgId.MailDeleteReq:
                await HandleMailDeleteAsync(session, frame, ct);
                break;
            case MsgId.MailSendReq:
                await HandleMailSendAsync(session, frame, ct);
                break;
            case MsgId.DungeonEnterReq:
                await HandleDungeonEnterAsync(session, frame, ct);
                break;
            case MsgId.DungeonLeaveReq:
                await HandleDungeonLeaveAsync(session, frame, ct);
                break;
            case MsgId.DungeonStatusReq:
                await HandleDungeonStatusAsync(session, frame, ct);
                break;
            case MsgId.WorldBossStatusReq:
                await HandleWorldBossStatusAsync(session, frame, ct);
                break;
            case MsgId.WorldBossControlReq:
                await HandleWorldBossControlAsync(session, frame, ct);
                break;
            case MsgId.MountStatusReq:
                await HandleMountStatusAsync(session, frame, ct);
                break;
            case MsgId.MountSummonReq:
                await HandleMountSummonAsync(session, frame, ct);
                break;
            case MsgId.MountDismissReq:
                await HandleMountDismissAsync(session, frame, ct);
                break;
            case MsgId.FashionStatusReq:
                await HandleFashionStatusAsync(session, frame, ct);
                break;
            case MsgId.FashionEquipReq:
                await HandleFashionEquipAsync(session, frame, ct);
                break;
            case MsgId.PetStatusReq:
                await HandlePetStatusAsync(session, frame, ct);
                break;
            case MsgId.PetSummonReq:
                await HandlePetSummonAsync(session, frame, ct);
                break;
            case MsgId.PetDismissReq:
                await HandlePetDismissAsync(session, frame, ct);
                break;
            case MsgId.GuildWarehouseListReq:
                await HandleGuildWarehouseListAsync(session, frame, ct);
                break;
            case MsgId.GuildWarehouseDepositReq:
                await HandleGuildWarehouseDepositAsync(session, frame, ct);
                break;
            case MsgId.GuildWarehouseWithdrawReq:
                await HandleGuildWarehouseWithdrawAsync(session, frame, ct);
                break;
            case MsgId.GuildWarehouseClearReq:
                await HandleGuildWarehouseClearAsync(session, frame, ct);
                break;
            case MsgId.GuildSetRankReq:
                await HandleGuildSetRankAsync(session, frame, ct);
                break;
            case MsgId.GuildWarehouseListAuditReq:
                await HandleGuildWarehouseListAuditAsync(session, frame, ct);
                break;
            case MsgId.PaymentCreateOrderReq:
                await HandlePaymentCreateOrderAsync(session, frame, ct);
                break;
            case MsgId.PaymentOrderStatusReq:
                await HandlePaymentOrderStatusAsync(session, frame, ct);
                break;
        }
    }

    private async Task HandleEnterWorldAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = EnterWorldReq.Parser.ParseFrom(frame.Payload);
        var sess = await _storage.Sessions.GetSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid or expired session token."
            }, ct);
            return;
        }

        string characterId = !string.IsNullOrWhiteSpace(req.CharacterId)
            ? req.CharacterId
            : sess.CharacterId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(characterId))
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "未选择角色，请先 SelectChar。"
            }, ct);
            return;
        }

        var character = await _storage.Characters.GetByIdAsync(characterId, ct);
        if (character is null || character.AccountId != sess.AccountId)
        {
            await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, new EnterWorldResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "角色不存在或不属于当前账号。"
            }, ct);
            return;
        }

        string mapBeforeSanitize = character.MapId;
        SanitizeCharacterMapIfStaleInstance(character);
        if (!string.Equals(mapBeforeSanitize, character.MapId, StringComparison.OrdinalIgnoreCase))
        {
            await _storage.Characters.SavePositionAsync(
                character.CharacterId, character.MapId, character.X, character.Y, character.Hp, character.Mp, ct);
        }

        if (!string.IsNullOrEmpty(session.EntityId))
        {
            foreach (var aoi in _maps.Values)
            {
                if (aoi.TryGetEntity(session.EntityId, out _))
                {
                    var oldViewers = aoi.RemoveEntity(session.EntityId);
                    var leave = new EntityLeaveAoi { EntityId = session.EntityId, Reason = "Reenter" };
                    foreach (var v in oldViewers)
                        await v.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leave, ct);
                }
            }
        }

        string entityId = $"ent_{character.CharacterId}";
        session.AccountId = sess.AccountId;
        session.SessionToken = req.SessionToken;
        session.EntityId = entityId;
        session.CharacterId = character.CharacterId;
        session.CharacterName = character.Name;
        session.ClassId = character.ClassId;

        await _storage.Sessions.BindCharacterAsync(req.SessionToken, character.CharacterId, ct);

        var map = ResolveMapConfig(character.MapId);
        int x = character.X;
        int y = character.Y;
        if (map.IsBlocked(x, y))
        {
            x = map.SpawnX;
            y = map.SpawnY;
        }

        if (!_versionPack.Classes.TryGetValue(character.ClassId, out var cls))
        {
            cls = new ClassConfigItem
            {
                ClassId = character.ClassId,
                BaseAtk = 15,
                BaseHp = character.MaxHp > 0 ? character.MaxHp : 120,
                BaseMp = character.MaxMp > 0 ? character.MaxMp : 40
            };
        }

        var entity = new WorldPlayerEntity
        {
            EntityId = entityId,
            CharacterId = character.CharacterId,
            Name = character.Name,
            ClassId = character.ClassId,
            MapId = map.MapId,
            Session = session,
            X = x,
            Y = y,
            Direction = 4,
            Level = Math.Max(1, character.Level),
            Exp = Math.Max(0, character.Exp),
            Hp = Math.Max(1, character.Hp),
            Mp = Math.Max(0, character.Mp),
            Inventory = InventoryHelper.Parse(character.InventoryJson),
            Gold = character.Gold,
            Quests = QuestHelper.Parse(character.QuestJson),
            Equipment = EquipmentHelper.Parse(character.EquipmentJson),
            GuildId = character.GuildId ?? string.Empty,
            LearnedSkills = SkillHelper.Parse(character.SkillsJson),
            DungeonProgress = DungeonProgressJson.Parse(character.DungeonProgressJson)
        };
        LoadAppearanceOntoPlayer(entity, character.AppearanceJson);
        EnsureStarterSkills(entity);
        await PersistSkillsAsync(entity, ct);
        if (!string.IsNullOrEmpty(entity.GuildId))
        {
            var guild = await _storage.Guilds.GetByIdAsync(entity.GuildId, ct);
            if (guild != null)
            {
                entity.GuildName = guild.Name;
                entity.GuildRank = string.Equals(guild.LeaderCharacterId, character.CharacterId, StringComparison.Ordinal)
                    ? "leader" : "member";
            }
            else
            {
                entity.GuildId = string.Empty;
            }
        }
        LevelCurve.ApplyBaseStats(entity, cls, _versionPack.Levels);
        ApplyCastleLordBuffOnEnterIfEligible(entity);
        EquipmentHelper.RecalculateStats(entity, _versionPack.Items, _versionPack.Buffs);
        if (entity.Hp > entity.MaxHp) entity.Hp = entity.MaxHp;
        if (entity.Mp > entity.MaxMp) entity.Mp = entity.MaxMp;

        var aoiMgr = GetAoi(map.MapId);
        var nearbyPlayers = aoiMgr.AddOrUpdateEntity(entity);

        var resp = new EnterWorldResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            MapId = map.MapId,
            SelfEntity = entity.ToSnapshot(),
            Message = $"Entered map '{map.MapName}' ({map.MapId})",
            InSafeZone = entity.InSafeZone,
            EffectiveAtk = entity.Atk,
            EffectiveDef = entity.Def,
            EffectiveMaxHp = entity.MaxHp,
            Level = entity.Level,
            Exp = entity.Exp,
            ExpToNext = LevelCurve.ExpToNext(_versionPack.Levels, entity.Level),
            EffectiveMaxMp = entity.MaxMp,
            GuildId = entity.GuildId,
            GuildName = entity.GuildName,
            GuildRank = entity.GuildRank,
            SiegeOwnerGuildId = _siegeOwnerGuildId,
            SiegeOwnerGuildName = _siegeOwnerGuildName,
            IsCastleLordGuild = IsCastleLordGuild(entity.GuildId)
        };
        {
            var mod = await _storage.Moderation.GetByAccountIdAsync(character.AccountId, ct);
            if (mod is not null && mod.IsMuteActive())
            {
                resp.IsMuted = true;
                resp.MuteExpiresUnixMs = mod.MuteExpiresUnixMs;
            }
        }
        foreach (var other in nearbyPlayers)
            resp.NearbyEntities.Add(other.ToSnapshot());
        foreach (var snap in EnumerateNearbyNonPlayers(map.MapId, x, y))
            resp.NearbyEntities.Add(snap);
        foreach (var item in InventoryHelper.ToProto(entity.Inventory))
            resp.Inventory.Add(item);
        resp.Gold = entity.Gold;
        foreach (var q in entity.Quests)
        {
            _versionPack.Quests.TryGetValue(q.QuestId, out var qt);
            resp.Quests.Add(QuestHelper.ToProto(q, qt));
        }
        foreach (var eq in EquipmentHelper.ToProto(entity.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);
        foreach (var sk in entity.LearnedSkills)
        {
            _versionPack.Skills.TryGetValue(sk.SkillId, out var skCfg);
            resp.Skills.Add(SkillHelper.ToProto(sk, skCfg));
        }
        foreach (var ab in entity.ActiveBuffs)
        {
            _versionPack.Buffs.TryGetValue(ab.BuffId, out var bCfg);
            resp.Buffs.Add(BuffHelper.ToProto(ab, bCfg));
        }

        await session.SendAsync(MsgId.EnterWorldResp, frame.Seq, resp, ct);
        await SendSkillListNotifyAsync(entity, "enter", ct);

        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearbyPlayers)
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);

        StructuredLogger.Info("GameWorldModule",
            $"Player '{character.Name}' entered '{map.MapId}' at ({x},{y}) safe={entity.InSafeZone}",
            connId: session.ConnectionId, playerId: entityId);

        await _eventBus.PublishAsync(new PlayerEnteredWorldEvent(
            session, entityId, character.Name, character.ClassId, map.MapId, x, y), ct);

        await ResummonPetOnEnterAsync(entity, ct);
    }

    private IEnumerable<EntitySnapshot> EnumerateNearbyNonPlayers(string mapId, int x, int y)
    {
        int view = 16;
        foreach (var m in _monsters.Values)
        {
            if (!m.Alive || !string.Equals(m.MapId, mapId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CombatMath.Chebyshev(x, y, m.X, m.Y) <= view)
                yield return m.ToSnapshot();
        }
        foreach (var loot in _loots.Values)
        {
            if (!string.Equals(loot.MapId, mapId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CombatMath.Chebyshev(x, y, loot.X, loot.Y) <= view)
                yield return loot.ToSnapshot();
        }
        foreach (var npc in _npcs.Values)
        {
            if (!string.Equals(npc.MapId, mapId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CombatMath.Chebyshev(x, y, npc.X, npc.Y) <= view)
                yield return npc.ToSnapshot();
        }
        foreach (var pet in _pets.Values)
        {
            if (!pet.Alive || !string.Equals(pet.MapId, mapId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (CombatMath.Chebyshev(x, y, pet.X, pet.Y) <= view)
                yield return pet.ToSnapshot();
        }
    }

    private async Task HandleMoveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetPlayer(session, out var entity, out var aoi) || entity is null || aoi is null)
        {
            await RejectMove(session, frame, null, "Player has not entered the world.", ct);
            return;
        }

        if (entity.IsDead)
        {
            await RejectMove(session, frame, entity, "Cannot move while dead.", ct);
            return;
        }

        var req = MoveReq.Parser.ParseFrom(frame.Payload);
        int fromX = entity.X;
        int fromY = entity.Y;
        int toX = req.ToX;
        int toY = req.ToY;

        if (!aoi.IsWalkable(toX, toY))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX, FromY = fromY, ToX = fromX, ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = $"Target cell ({toX},{toY}) is blocked or out of map bounds.",
                InSafeZone = entity.InSafeZone
            }, ct);
            return;
        }

        if (!_antiCheatProbe.ValidateMove(entity.EntityId, fromX, fromY, toX, toY, out string acReason))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX, FromY = fromY, ToX = fromX, ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveBlocked,
                Reason = acReason,
                InSafeZone = entity.InSafeZone
            }, ct);
            return;
        }

        if (!TryValidateMoveInterval(entity, out string speedReason))
        {
            await session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
            {
                EntityId = entity.EntityId,
                FromX = fromX, FromY = fromY, ToX = fromX, ToY = fromY,
                Direction = entity.Direction,
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrMoveTooFast,
                Reason = speedReason,
                InSafeZone = entity.InSafeZone
            }, ct);
            return;
        }

        var (leftViewers, enteredViewers, currentViewers) = aoi.MoveEntity(entity.EntityId, toX, toY, req.Direction);

        var moveSync = new MoveSync
        {
            EntityId = entity.EntityId,
            FromX = fromX, FromY = fromY, ToX = toX, ToY = toY,
            Direction = req.Direction,
            Accepted = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Reason = "OK",
            InSafeZone = entity.InSafeZone
        };

        await session.SendAsync(MsgId.MoveSync, frame.Seq, moveSync, ct);
        foreach (var viewer in currentViewers)
            await viewer.Session.SendAsync(MsgId.MoveSync, 0, moveSync, ct);

        if (enteredViewers.Count > 0)
        {
            var myEnter = new EntityEnterAoi { Entity = entity.ToSnapshot() };
            foreach (var newViewer in enteredViewers)
            {
                await newViewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, myEnter, ct);
                await session.SendAsync(MsgId.EntityEnterAoi, 0, new EntityEnterAoi { Entity = newViewer.ToSnapshot() }, ct);
            }
        }

        if (leftViewers.Count > 0)
        {
            var myLeave = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = "OutOfAoiRange" };
            foreach (var oldViewer in leftViewers)
            {
                await oldViewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, myLeave, ct);
                await session.SendAsync(MsgId.EntityLeaveAoi, 0, new EntityLeaveAoi { EntityId = oldViewer.EntityId, Reason = "OutOfAoiRange" }, ct);
            }
        }

        // Reveal monsters/loot newly in view
        foreach (var snap in EnumerateNearbyNonPlayers(entity.MapId, toX, toY))
        {
            if (CombatMath.Chebyshev(fromX, fromY, snap.X, snap.Y) > 16)
                await session.SendAsync(MsgId.EntitySpawn, 0, new EntitySpawn { Entity = snap, Reason = "AoiReveal" }, ct);
        }

        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

        await _eventBus.PublishAsync(new PlayerMovedEvent(
            entity.EntityId, entity.Name, entity.MapId, fromX, fromY, toX, toY), ct);

        var portal = aoi.Map.FindPortalAt(toX, toY);
        if (portal is not null)
            await TransferViaPortalAsync(session, entity, aoi, portal, ct);
    }

    private async Task HandleAttackAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = AttackReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var attacker, out var aoi) || attacker is null || aoi is null)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (attacker.IsDead || attacker.Hp <= 0)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "You are dead."
            }, ct);
            return;
        }

        // Safe zone: complete attack ban
        if (attacker.InSafeZone || aoi.Map.IsInSafeZone(attacker.X, attacker.Y))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSafeZone,
                Message = "安全区内禁止攻击。"
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.SkillId) ||
            !_versionPack.Skills.TryGetValue(req.SkillId, out var skill))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                Message = $"未知技能: {req.SkillId}"
            }, ct);
            return;
        }

        if (skill.AllowedClasses.Count > 0 &&
            !skill.AllowedClasses.Any(c => string.Equals(c, attacker.ClassId, StringComparison.OrdinalIgnoreCase)))
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                Message = $"职业 {attacker.ClassId} 无法使用技能 {skill.SkillId}"
            }, ct);
            return;
        }

        if (!HasLearnedSkill(attacker, skill.SkillId, out var learned) || learned is null)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSkillNotLearned,
                Message = $"尚未学会技能: {skill.SkillId}"
            }, ct);
            return;
        }

        int skillLevel = Math.Max(1, learned.Level);
        double dmgMult = SkillHelper.EffectiveMultiplier(skill, skillLevel);
        int cooldownMs = SkillHelper.EffectiveCooldownMs(skill, skillLevel);
        if (skill.CastTimeMs > 0)
            cooldownMs = Math.Max(cooldownMs, skill.CastTimeMs);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (attacker.SkillReadyAtUnixMs.TryGetValue(skill.SkillId, out long readyAt) && now < readyAt)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrSkillCooldown,
                Message = "技能冷却中",
                NextReadyUnixMs = readyAt,
                RemainingMp = attacker.Mp
            }, ct);
            return;
        }

        if (attacker.Mp < skill.MpCost)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                Message = "MP 不足",
                RemainingMp = attacker.Mp
            }, ct);
            return;
        }

        if (PhaseLRules.IsSupportSkill(skill.Kind))
        {
            await ResolveSupportSkillAsync(session, frame, attacker, aoi, skill, skillLevel, cooldownMs, now, ct);
            return;
        }

        if (!_monsters.TryGetValue(req.TargetEntityId, out var monster) || !monster.Alive ||
            !string.Equals(monster.MapId, attacker.MapId, StringComparison.OrdinalIgnoreCase))
        {
            if (await TryHandleSiegePvpAttackAsync(session, frame, attacker, aoi, req, skill, dmgMult, cooldownMs, now, ct))
                return;

            // PvP disabled outside siege zone: reject player targets and unknown
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidTarget,
                Message = "无效目标（M2 仅支持攻击怪物）。"
            }, ct);
            return;
        }

        int dist = CombatMath.Chebyshev(attacker.X, attacker.Y, monster.X, monster.Y);
        if (dist > skill.Range)
        {
            await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrOutOfRange,
                Message = $"目标超出技能距离 ({dist}>{skill.Range})"
            }, ct);
            return;
        }

        attacker.Mp -= skill.MpCost;
        long nextReady = now + cooldownMs;
        attacker.SkillReadyAtUnixMs[skill.SkillId] = nextReady;

        int damage = CombatMath.CalcDamage(attacker.Atk, monster.Template.Def, dmgMult);
        monster.Hp = Math.Max(0, monster.Hp - damage);
        monster.TargetEntityId = attacker.EntityId;
        bool killed = monster.Hp <= 0;

        var combat = new CombatSync
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = monster.EntityId,
            SkillId = skill.SkillId,
            Result = killed ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
            Damage = damage,
            TargetHp = monster.Hp,
            TargetMaxHp = monster.MaxHp,
            TargetDied = killed,
            MapId = attacker.MapId
        };

        await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            RemainingMp = attacker.Mp,
            NextReadyUnixMs = nextReady
        }, ct);

        await BroadcastNearAsync(attacker.MapId, attacker.X, attacker.Y, MsgId.CombatSync, combat, ct);
        // Also send to attacker if not already in viewers list via Broadcast (Broadcast includes all near)
        await session.SendAsync(MsgId.CombatSync, 0, combat, ct);

        if (!killed)
            await TryPetAssistAfterOwnerAttackAsync(attacker, monster, ct);

        await _storage.Characters.SavePositionAsync(
            attacker.CharacterId, attacker.MapId, attacker.X, attacker.Y, attacker.Hp, attacker.Mp, ct);

        await GrantSkillExpAsync(attacker, skill, skill.SkillExpPerCast + (killed ? skill.SkillExpPerKill : 0), ct);
        if (!string.IsNullOrEmpty(skill.ApplyBuffOnCast))
            await ApplyBuffAsync(attacker, skill.ApplyBuffOnCast, ct);
        // Debuffs on monsters are not tracked in M9 player-buff state machine; poison DoT reserved for player targets.

        if (killed)
        {
            monster.Alive = false;
            monster.RespawnAtUnixMs = now + Math.Max(1000, monster.Template.RespawnMs);
            await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntityDespawn,
                new EntityDespawn { EntityId = monster.EntityId, Reason = "Killed", MapId = monster.MapId }, ct);

            bool isWorldBoss = !string.IsNullOrEmpty(monster.WorldBossId);
            if (isWorldBoss)
            {
                await OnWorldBossDamagedAsync(attacker, monster, damage, killed: true, ct);
            }
            else
            {
                await DropLootAsync(monster, attacker, ct);
                await GrantExpFromKillAsync(attacker, monster, ct);
            }

            await _eventBus.PublishAsync(new MonsterKilledEvent(
                session, attacker.EntityId, attacker.Name, attacker.CharacterId,
                monster.EntityId, monster.Name, monster.MonsterId, monster.MapId, monster.X, monster.Y), ct);

            await UpdateQuestKillProgressAsync(attacker, monster.MonsterId, ct);
            if (monster.NoRespawn || !string.IsNullOrEmpty(monster.InstanceId))
                monster.RespawnAtUnixMs = 0;
            await OnDungeonMonsterKilledAsync(attacker, monster, ct);
        }
        else if (!string.IsNullOrEmpty(monster.WorldBossId))
        {
            await OnWorldBossDamagedAsync(attacker, monster, damage, killed: false, ct);
        }
    }

    private async Task DropLootAsync(MonsterEntity monster, WorldPlayerEntity killer, CancellationToken ct)
    {
        if (!_versionPack.LootTables.TryGetValue(monster.Template.LootTableId, out var table))
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

            int lootId = Interlocked.Increment(ref _lootSeq);
            var loot = new LootEntity
            {
                EntityId = $"loot_{lootId}",
                MapId = monster.MapId,
                ItemId = entry.ItemId,
                Count = count,
                X = monster.X,
                Y = monster.Y,
                OwnerCharacterId = killer.CharacterId,
                FreeForAllUnixMs = now + LootOwnerMs,
                ExpireUnixMs = now + LootDespawnMs
            };
            _loots[loot.EntityId] = loot;

            var notify = new LootDropNotify
            {
                Loot = loot.ToSnapshot(),
                SourceEntityId = monster.EntityId,
                KillerCharacterId = killer.CharacterId
            };
            await BroadcastNearAsync(loot.MapId, loot.X, loot.Y, MsgId.LootDropNotify, notify, ct);
            await BroadcastNearAsync(loot.MapId, loot.X, loot.Y, MsgId.EntitySpawn,
                new EntitySpawn { Entity = loot.ToSnapshot(), Reason = "LootDrop" }, ct);
        }
    }

    private async Task HandlePickupAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PickupReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.PickupResp, frame.Seq, new PickupResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (!_loots.TryGetValue(req.LootEntityId, out var loot) ||
            !string.Equals(loot.MapId, player.MapId, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.PickupResp, frame.Seq, new PickupResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrLootDenied,
                Message = "掉落不存在。"
            }, ct);
            return;
        }

        if (CombatMath.Chebyshev(player.X, player.Y, loot.X, loot.Y) > PickupRange)
        {
            await session.SendAsync(MsgId.PickupResp, frame.Seq, new PickupResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrOutOfRange,
                Message = "距离太远，无法拾取。"
            }, ct);
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!string.IsNullOrEmpty(loot.OwnerCharacterId) &&
            now < loot.FreeForAllUnixMs &&
            !string.Equals(loot.OwnerCharacterId, player.CharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.PickupResp, frame.Seq, new PickupResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrLootDenied,
                Message = "该掉落归属击杀者（保护期内不可拾取）。"
            }, ct);
            return;
        }

        if (!_loots.TryRemove(loot.EntityId, out _))
        {
            await session.SendAsync(MsgId.PickupResp, frame.Seq, new PickupResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrLootDenied,
                Message = "掉落已被拾取。"
            }, ct);
            return;
        }

        InventoryHelper.AddItem(player.Inventory, loot.ItemId, loot.Count);
        string invJson = InventoryHelper.Serialize(player.Inventory);
        await _storage.Characters.SaveInventoryAsync(player.CharacterId, invJson, ct);

        var picked = new InventoryItem { ItemId = loot.ItemId, Count = loot.Count };
        var resp = new PickupResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Picked = picked
        };
        foreach (var item in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(item);

        await session.SendAsync(MsgId.PickupResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "Pickup",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await BroadcastNearAsync(loot.MapId, loot.X, loot.Y, MsgId.EntityDespawn,
            new EntityDespawn { EntityId = loot.EntityId, Reason = "Picked", MapId = loot.MapId }, ct);

        await _eventBus.PublishAsync(new LootPickedEvent(
            session, player.Name, player.CharacterId, loot.ItemId, loot.Count, loot.EntityId, loot.MapId), ct);
    }

    private async Task TransferViaPortalAsync(
        IClientSession session,
        WorldPlayerEntity entity,
        GridAoiManager fromAoi,
        PortalConfig portal,
        CancellationToken ct)
    {
        if (!_versionPack.Maps.ContainsKey(portal.TargetMapId) &&
            !string.Equals(portal.TargetMapId, _versionPack.Map.MapId, StringComparison.OrdinalIgnoreCase))
        {
            StructuredLogger.Warn("GameWorldModule", $"Portal {portal.PortalId} target map '{portal.TargetMapId}' missing");
            return;
        }

        string fromMapId = entity.MapId;
        var leaveViewers = fromAoi.RemoveEntity(entity.EntityId);
        var leaveMsg = new EntityLeaveAoi { EntityId = entity.EntityId, Reason = "Portal" };
        foreach (var v in leaveViewers)
            await v.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);

        var toMap = _versionPack.GetMap(portal.TargetMapId);
        int tx = portal.TargetX;
        int ty = portal.TargetY;
        if (toMap.IsBlocked(tx, ty))
        {
            tx = toMap.SpawnX;
            ty = toMap.SpawnY;
        }

        entity.MapId = toMap.MapId;
        entity.X = tx;
        entity.Y = ty;

        var toAoi = GetAoi(toMap.MapId);
        var nearby = toAoi.AddOrUpdateEntity(entity);

        await _storage.Characters.SavePositionAsync(
            entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);

        var notify = new MapChangeNotify
        {
            EntityId = entity.EntityId,
            FromMapId = fromMapId,
            ToMapId = toMap.MapId,
            ToX = tx,
            ToY = ty,
            PortalId = portal.PortalId,
            InSafeZone = entity.InSafeZone
        };
        foreach (var n in nearby)
            notify.NearbyEntities.Add(n.ToSnapshot());
        foreach (var snap in EnumerateNearbyNonPlayers(toMap.MapId, tx, ty))
            notify.NearbyEntities.Add(snap);

        await session.SendAsync(MsgId.MapChangeNotify, 0, notify, ct);

        var enterNotice = new EntityEnterAoi { Entity = entity.ToSnapshot() };
        foreach (var viewer in nearby)
            await viewer.Session.SendAsync(MsgId.EntityEnterAoi, 0, enterNotice, ct);

        StructuredLogger.Info("GameWorldModule",
            $"Portal '{portal.PortalId}': {entity.Name} {fromMapId}-> {toMap.MapId} ({tx},{ty})",
            connId: session.ConnectionId, playerId: entity.EntityId);
    }

    private async Task AiLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(400, ct);
                await TickMonstersAsync(ct);
                await TickPetsAsync(ct);
                await TickWorldBossesAsync(ct);
                await TickLootExpiryAsync(ct);
                await TickPlayerBuffsAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StructuredLogger.Warn("GameWorldModule", $"AI tick error: {ex.Message}");
            }
        }
    }

    private async Task TickMonstersAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var monster in _monsters.Values.ToList())
        {
            if (!monster.Alive)
            {
                if (monster.NoRespawn)
                    continue;
                if (monster.RespawnAtUnixMs > 0 && now >= monster.RespawnAtUnixMs)
                {
                    monster.Alive = true;
                    monster.Hp = monster.MaxHp;
                    monster.X = monster.SpawnX;
                    monster.Y = monster.SpawnY;
                    monster.TargetEntityId = null;
                    monster.RespawnAtUnixMs = 0;
                    await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.EntitySpawn,
                        new EntitySpawn { Entity = monster.ToSnapshot(), Reason = "Respawn" }, ct);
                }
                continue;
            }

            var aoi = GetAoi(monster.MapId);
            WorldPlayerEntity? target = null;
            if (!string.IsNullOrEmpty(monster.TargetEntityId) &&
                aoi.TryGetEntity(monster.TargetEntityId, out var t) && t != null && !t.IsDead)
            {
                target = t;
            }
            else
            {
                // aggro nearest player in range (outside safe zone)
                int best = int.MaxValue;
                foreach (var p in aoi.AllPlayers())
                {
                    if (p.IsDead || p.InSafeZone) continue;
                    int d = CombatMath.Chebyshev(monster.X, monster.Y, p.X, p.Y);
                    if (d <= monster.Template.AggroRange && d < best)
                    {
                        best = d;
                        target = p;
                    }
                }
                monster.TargetEntityId = target?.EntityId;
            }

            if (target is null)
            {
                // leash home for chase AI
                if (string.Equals(monster.Template.Ai, "chase", StringComparison.OrdinalIgnoreCase) &&
                    (monster.X != monster.SpawnX || monster.Y != monster.SpawnY))
                {
                    int nx = monster.X + Math.Sign(monster.SpawnX - monster.X);
                    int ny = monster.Y + Math.Sign(monster.SpawnY - monster.Y);
                    if (aoi.IsWalkable(nx, ny) && !aoi.Map.IsInSafeZone(nx, ny))
                    {
                        int ox = monster.X, oy = monster.Y;
                        monster.X = nx;
                        monster.Y = ny;
                        await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.MoveSync, new MoveSync
                        {
                            EntityId = monster.EntityId,
                            FromX = ox, FromY = oy, ToX = nx, ToY = ny,
                            Direction = 4, Accepted = true, Reason = "AI"
                        }, ct);
                    }
                }
                continue;
            }

            int dist = CombatMath.Chebyshev(monster.X, monster.Y, target.X, target.Y);
            if (string.Equals(monster.Template.Ai, "chase", StringComparison.OrdinalIgnoreCase) &&
                dist > monster.Template.AttackRange)
            {
                int nx = monster.X + Math.Sign(target.X - monster.X);
                int ny = monster.Y + Math.Sign(target.Y - monster.Y);
                if (aoi.IsWalkable(nx, ny) && !aoi.Map.IsInSafeZone(nx, ny))
                {
                    int ox = monster.X, oy = monster.Y;
                    monster.X = nx;
                    monster.Y = ny;
                    await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.MoveSync, new MoveSync
                    {
                        EntityId = monster.EntityId,
                        FromX = ox, FromY = oy, ToX = nx, ToY = ny,
                        Direction = 4, Accepted = true, Reason = "AI"
                    }, ct);
                }
                dist = CombatMath.Chebyshev(monster.X, monster.Y, target.X, target.Y);
            }

            if (dist <= monster.Template.AttackRange && now >= monster.NextAttackUnixMs)
            {
                if (target.InSafeZone)
                {
                    monster.TargetEntityId = null;
                    continue;
                }

                monster.NextAttackUnixMs = now + monster.Template.AttackCooldownMs;
                int dmg = CombatMath.CalcDamage(monster.Template.Atk, target.Def, 1.0);
                target.Hp = Math.Max(0, target.Hp - dmg);
                bool died = target.Hp <= 0;
                var sync = new CombatSync
                {
                    AttackerEntityId = monster.EntityId,
                    TargetEntityId = target.EntityId,
                    SkillId = "monster_melee",
                    Result = died ? CombatResultKind.CombatKill : CombatResultKind.CombatHit,
                    Damage = dmg,
                    TargetHp = target.Hp,
                    TargetMaxHp = target.MaxHp,
                    TargetDied = died,
                    MapId = monster.MapId
                };
                await BroadcastNearAsync(monster.MapId, monster.X, monster.Y, MsgId.CombatSync, sync, ct);
                await target.Session.SendAsync(MsgId.CombatSync, 0, sync, ct);

                if (died)
                {
                    await ClearBuffsOnDeathAsync(target, ct);
                    // M16 death rule: auto-recall pet on owner death; must re-summon after revive.
                    if (!string.IsNullOrEmpty(target.ActivePetId))
                        await DespawnPetEntityAsync(target, "owner_death", persist: true, ct);
                    await RevivePlayerAsync(target, aoi, ct);
                }
                else
                    await _storage.Characters.SavePositionAsync(
                        target.CharacterId, target.MapId, target.X, target.Y, target.Hp, target.Mp, ct);
            }
        }
    }

    private async Task RevivePlayerAsync(WorldPlayerEntity player, GridAoiManager aoi, CancellationToken ct)
    {
        player.IsDead = false;
        player.Hp = player.MaxHp;
        player.Mp = player.MaxMp;
        int sx = aoi.Map.SpawnX;
        int sy = aoi.Map.SpawnY;
        int ox = player.X, oy = player.Y;
        var (leftViewers, _, _) = aoi.MoveEntity(player.EntityId, sx, sy, 4);
        await _storage.Characters.SavePositionAsync(
            player.CharacterId, player.MapId, player.X, player.Y, player.Hp, player.Mp, ct);

        await player.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
        {
            NoticeType = "REVIVE",
            Title = "复活",
            Content = $"你已在出生点 ({sx},{sy}) 满血复活。"
        }, ct);

        var sync = new MoveSync
        {
            EntityId = player.EntityId,
            FromX = ox, FromY = oy, ToX = sx, ToY = sy,
            Direction = 4, Accepted = true, Reason = "Revive",
            InSafeZone = player.InSafeZone
        };
        await player.Session.SendAsync(MsgId.MoveSync, 0, sync, ct);
        foreach (var v in aoi.QueryPlayersNear(sx, sy, player.EntityId))
            await v.Session.SendAsync(MsgId.MoveSync, 0, sync, ct);
        foreach (var lv in leftViewers)
            await lv.Session.SendAsync(MsgId.MoveSync, 0, sync, ct);
    }

    private async Task TickLootExpiryAsync(CancellationToken ct)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var loot in _loots.Values.ToList())
        {
            if (now < loot.ExpireUnixMs)
                continue;
            if (_loots.TryRemove(loot.EntityId, out _))
            {
                await BroadcastNearAsync(loot.MapId, loot.X, loot.Y, MsgId.EntityDespawn,
                    new EntityDespawn { EntityId = loot.EntityId, Reason = "Expired", MapId = loot.MapId }, ct);
            }
        }
    }


    private async Task UpdateQuestKillProgressAsync(WorldPlayerEntity player, string monsterId, CancellationToken ct)
    {
        bool changed = false;
        foreach (var q in player.Quests)
        {
            if (!string.Equals(q.Status, "active", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_versionPack.Quests.TryGetValue(q.QuestId, out var tmpl))
                continue;
            if (!string.Equals(tmpl.ObjectiveType, "kill", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(tmpl.TargetMonsterId, monsterId, StringComparison.OrdinalIgnoreCase))
                continue;

            q.Progress = Math.Min(tmpl.TargetCount, q.Progress + 1);
            if (q.Progress >= tmpl.TargetCount)
                q.Status = "ready";
            changed = true;

            await player.Session.SendAsync(MsgId.QuestProgressNotify, 0, new QuestProgressNotify
            {
                CharacterId = player.CharacterId,
                Quest = QuestHelper.ToProto(q, tmpl),
                Reason = "Kill"
            }, ct);
        }

        if (changed)
            await _storage.Characters.SaveQuestsAsync(player.CharacterId, QuestHelper.Serialize(player.Quests), ct);
    }

    private bool TryGetNpc(string npcEntityId, out NpcEntity npc)
    {
        if (!string.IsNullOrEmpty(npcEntityId) && _npcs.TryGetValue(npcEntityId, out npc!))
            return true;
        if (!string.IsNullOrEmpty(npcEntityId) && !npcEntityId.StartsWith("npc_", StringComparison.OrdinalIgnoreCase))
        {
            if (_npcs.TryGetValue("npc_" + npcEntityId, out npc!))
                return true;
        }
        npc = null!;
        return false;
    }

    private async Task PersistEconomyAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        await _storage.Characters.SaveInventoryAsync(player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
        await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
        await _storage.Characters.SaveQuestsAsync(player.CharacterId, QuestHelper.Serialize(player.Quests), ct);
    }

    private async Task HandleTalkAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = TalkReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.TalkResp, frame.Seq, new TalkResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc))
        {
            await session.SendAsync(MsgId.TalkResp, frame.Seq, new TalkResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUnknownNpc,
                Message = "Unknown NPC"
            }, ct);
            return;
        }
        if (!string.Equals(player.MapId, npc.MapId, StringComparison.OrdinalIgnoreCase) ||
            CombatMath.Chebyshev(player.X, player.Y, npc.X, npc.Y) > NpcInteractRange)
        {
            await session.SendAsync(MsgId.TalkResp, frame.Seq, new TalkResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNpcTooFar,
                Message = "Too far from NPC"
            }, ct);
            return;
        }

        var resp = new TalkResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            NpcId = npc.NpcId,
            NpcName = npc.Name,
            Dialogue = npc.Template.Dialogue,
            ShopId = npc.Template.ShopId ?? "",
            Gold = player.Gold
        };

        if (!string.IsNullOrEmpty(npc.Template.ShopId) &&
            _versionPack.Shops.TryGetValue(npc.Template.ShopId, out var shop))
        {
            foreach (var o in shop.Offers)
            {
                resp.ShopOffers.Add(new ShopOffer
                {
                    ItemId = o.ItemId,
                    PriceGold = o.PriceGold,
                    Stock = o.Stock
                });
            }
        }

        foreach (var qid in npc.Template.QuestIds)
        {
            if (!_versionPack.Quests.TryGetValue(qid, out var qt))
                continue;
            var existing = player.Quests.FirstOrDefault(q =>
                string.Equals(q.QuestId, qid, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                resp.Quests.Add(QuestHelper.ToProto(existing, qt));
            }
            else
            {
                resp.Quests.Add(new QuestProgress
                {
                    QuestId = qt.QuestId,
                    Title = qt.Title,
                    Description = qt.Description,
                    Status = QuestStatus.QuestNone,
                    Progress = 0,
                    TargetCount = qt.TargetCount,
                    TargetMonsterId = qt.TargetMonsterId
                });
            }
        }

        await session.SendAsync(MsgId.TalkResp, frame.Seq, resp, ct);
        await _eventBus.PublishAsync(new NpcTalkedEvent(
            session, player.Name, player.CharacterId, npc.NpcId, npc.Name, npc.MapId), ct);
    }

    private async Task HandleShopBuyAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = ShopBuyReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc))
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUnknownNpc,
                Message = "Unknown NPC"
            }, ct);
            return;
        }
        if (!string.Equals(player.MapId, npc.MapId, StringComparison.OrdinalIgnoreCase) ||
            CombatMath.Chebyshev(player.X, player.Y, npc.X, npc.Y) > NpcInteractRange)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNpcTooFar,
                Message = "Too far from NPC"
            }, ct);
            return;
        }
        if (string.IsNullOrEmpty(npc.Template.ShopId) ||
            !_versionPack.Shops.TryGetValue(npc.Template.ShopId, out var shop))
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "NPC has no shop"
            }, ct);
            return;
        }

        int count = Math.Max(1, req.Count);
        var offer = shop.Offers.FirstOrDefault(o =>
            string.Equals(o.ItemId, req.ItemId, StringComparison.OrdinalIgnoreCase));
        if (offer is null)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "Item not sold here"
            }, ct);
            return;
        }
        if (offer.Stock == 0)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrShopDenied,
                Message = "Out of stock"
            }, ct);
            return;
        }
        if (offer.Stock > 0 && count > offer.Stock)
            count = offer.Stock;

        long costLong = (long)offer.PriceGold * count;
        if (costLong < 0 || costLong > int.MaxValue || costLong > player.Gold)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = "Not enough gold",
                Gold = player.Gold
            }, ct);
            return;
        }
        int cost = (int)costLong;

        lock (_combatLock)
        {
            if (cost > player.Gold)
            {
                cost = -1;
            }
            else
            {
                player.Gold -= cost;
                InventoryHelper.AddItem(player.Inventory, offer.ItemId, count);
                if (offer.Stock > 0)
                    offer.Stock -= count;
            }
        }
        if (cost < 0)
        {
            await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, new ShopBuyResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = "Not enough gold",
                Gold = player.Gold
            }, ct);
            return;
        }

        await PersistEconomyAsync(player, ct);

        var purchased = new InventoryItem { ItemId = offer.ItemId, Count = count };
        var resp = new ShopBuyResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            Purchased = purchased,
            Gold = player.Gold
        };
        foreach (var item in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(item);

        await session.SendAsync(MsgId.ShopBuyResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "ShopBuy",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new ShopBoughtEvent(
            session, player.Name, player.CharacterId, npc.NpcId, offer.ItemId, count, cost, player.Gold), ct);
    }

    private async Task HandleQuestAcceptAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = QuestAcceptReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUnknownNpc,
                Message = "Unknown NPC"
            }, ct);
            return;
        }
        if (!string.Equals(player.MapId, npc.MapId, StringComparison.OrdinalIgnoreCase) ||
            CombatMath.Chebyshev(player.X, player.Y, npc.X, npc.Y) > NpcInteractRange)
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNpcTooFar,
                Message = "Too far from NPC"
            }, ct);
            return;
        }
        if (!_versionPack.Quests.TryGetValue(req.QuestId, out var tmpl))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Unknown quest"
            }, ct);
            return;
        }
        if (!PhaseLRules.PrerequisiteMet(tmpl, player.Quests))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "先完成前置任务"
            }, ct);
            return;
        }
        if (!string.Equals(tmpl.AcceptNpcId, npc.NpcId, StringComparison.OrdinalIgnoreCase) &&
            !npc.Template.QuestIds.Contains(tmpl.QuestId, StringComparer.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "This NPC does not offer that quest"
            }, ct);
            return;
        }

        var existing = player.Quests.FirstOrDefault(q =>
            string.Equals(q.QuestId, tmpl.QuestId, StringComparison.OrdinalIgnoreCase));
        if (existing != null && !string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Quest already in progress",
                Quest = QuestHelper.ToProto(existing, tmpl)
            }, ct);
            return;
        }
        if (existing != null && string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Quest already completed"
            }, ct);
            return;
        }

        var state = new QuestProgressState
        {
            QuestId = tmpl.QuestId,
            Status = "active",
            Progress = 0
        };
        player.Quests.Add(state);
        await _storage.Characters.SaveQuestsAsync(player.CharacterId, QuestHelper.Serialize(player.Quests), ct);

        var proto = QuestHelper.ToProto(state, tmpl);
        await session.SendAsync(MsgId.QuestAcceptResp, frame.Seq, new QuestAcceptResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Quest accepted",
            Quest = proto
        }, ct);
        await session.SendAsync(MsgId.QuestProgressNotify, 0, new QuestProgressNotify
        {
            CharacterId = player.CharacterId,
            Quest = proto,
            Reason = "Accept"
        }, ct);

        await _eventBus.PublishAsync(new QuestAcceptedEvent(
            session, player.Name, player.CharacterId, tmpl.QuestId, tmpl.Title), ct);
    }

    private async Task HandleQuestTurnInAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = QuestTurnInReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }
        if (!TryGetNpc(req.NpcEntityId, out var npc))
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUnknownNpc,
                Message = "Unknown NPC"
            }, ct);
            return;
        }
        if (!string.Equals(player.MapId, npc.MapId, StringComparison.OrdinalIgnoreCase) ||
            CombatMath.Chebyshev(player.X, player.Y, npc.X, npc.Y) > NpcInteractRange)
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNpcTooFar,
                Message = "Too far from NPC"
            }, ct);
            return;
        }
        if (!_versionPack.Quests.TryGetValue(req.QuestId, out var tmpl))
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Unknown quest"
            }, ct);
            return;
        }
        if (!string.Equals(tmpl.TurnInNpcId, npc.NpcId, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Wrong turn-in NPC"
            }, ct);
            return;
        }

        var state = player.Quests.FirstOrDefault(q =>
            string.Equals(q.QuestId, tmpl.QuestId, StringComparison.OrdinalIgnoreCase));
        if (state is null ||
            (!string.Equals(state.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
             state.Progress < tmpl.TargetCount))
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Quest objectives not complete"
            }, ct);
            return;
        }
        if (string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, new QuestTurnInResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrQuestDenied,
                Message = "Already turned in"
            }, ct);
            return;
        }

        state.Status = "completed";
        state.Progress = tmpl.TargetCount;
        player.Gold += Math.Max(0, tmpl.RewardGold);
        foreach (var reward in tmpl.RewardItems)
        {
            if (reward.Count > 0 && !string.IsNullOrEmpty(reward.ItemId))
                InventoryHelper.AddItem(player.Inventory, reward.ItemId, reward.Count);
        }

        await PersistEconomyAsync(player, ct);

        var resp = new QuestTurnInResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Quest complete",
            Quest = QuestHelper.ToProto(state, tmpl),
            GoldReward = tmpl.RewardGold,
            Gold = player.Gold
        };
        foreach (var r in tmpl.RewardItems)
            resp.ItemRewards.Add(new InventoryItem { ItemId = r.ItemId, Count = r.Count });
        foreach (var item in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(item);

        await session.SendAsync(MsgId.QuestTurnInResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.QuestProgressNotify, 0, new QuestProgressNotify
        {
            CharacterId = player.CharacterId,
            Quest = resp.Quest,
            Reason = "Complete"
        }, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "QuestReward",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new QuestCompletedEvent(
            session, player.Name, player.CharacterId, tmpl.QuestId, tmpl.Title, tmpl.RewardGold), ct);
    }


    private async Task BroadcastNearAsync(string mapId, int x, int y, MsgId msgId, Google.Protobuf.IMessage msg, CancellationToken ct)
    {
        var aoi = GetAoi(mapId);
        foreach (var p in aoi.QueryPlayersNear(x, y))
            await p.Session.SendAsync(msgId, 0, msg, ct);
        // Also players slightly farther for combat visibility — QueryPlayersNear uses AOI cells;
        // expand by also notifying all on same map within chebyshev 16
        foreach (var p in aoi.AllPlayers())
        {
            if (CombatMath.Chebyshev(p.X, p.Y, x, y) <= 16)
                await p.Session.SendAsync(msgId, 0, msg, ct);
        }
    }

    private bool TryGetPlayer(IClientSession session, out WorldPlayerEntity? entity, out GridAoiManager? aoi)
    {
        entity = null;
        aoi = null;
        if (string.IsNullOrEmpty(session.EntityId))
            return false;
        foreach (var kv in _maps)
        {
            if (kv.Value.TryGetEntity(session.EntityId, out var e) && e != null)
            {
                entity = e;
                aoi = kv.Value;
                return true;
            }
        }
        return false;
    }

    private static Task RejectMove(IClientSession session, PacketFrame frame, WorldPlayerEntity? entity, string reason, CancellationToken ct)
        => session.SendAsync(MsgId.MoveSync, frame.Seq, new MoveSync
        {
            EntityId = entity?.EntityId ?? string.Empty,
            Accepted = false,
            ErrorCode = ProtocolErrorCode.ErrInvalidToken,
            Reason = reason
        }, ct);

    public async Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(session.EntityId))
            return;

        foreach (var aoi in _maps.Values)
        {
            if (!aoi.TryGetEntity(session.EntityId, out var entity) || entity is null)
                continue;

            await _storage.Characters.SavePositionAsync(
                entity.CharacterId, entity.MapId, entity.X, entity.Y, entity.Hp, entity.Mp, ct);
            await _storage.Characters.SaveInventoryAsync(
                entity.CharacterId, InventoryHelper.Serialize(entity.Inventory), ct);
            await _storage.Characters.SaveEquipmentAsync(
                entity.CharacterId, EquipmentHelper.Serialize(entity.Equipment), ct);

            await CleanupPartyOnDisconnectAsync(entity, ct);
            await OnDungeonPlayerDisconnectedAsync(entity, ct);
            if (!string.IsNullOrEmpty(entity.ActivePetEntityId))
            {
                // Despawn AOI entity but keep ActivePetId so re-enter re-summons.
                string? eid = entity.ActivePetEntityId;
                if (!string.IsNullOrEmpty(eid) && _pets.TryRemove(eid, out var petDisc))
                {
                    await BroadcastNearAsync(petDisc.MapId, petDisc.X, petDisc.Y, MsgId.EntityDespawn,
                        new EntityDespawn { EntityId = petDisc.EntityId, Reason = "disconnect", MapId = petDisc.MapId }, ct);
                }
                entity.ActivePetEntityId = string.Empty;
                await PersistAppearanceAsync(entity, ct);
            }

            var viewers = aoi.RemoveEntity(session.EntityId);
            var leaveMsg = new EntityLeaveAoi { EntityId = session.EntityId, Reason = "Disconnected" };
            foreach (var viewer in viewers)
                await viewer.Session.SendAsync(MsgId.EntityLeaveAoi, 0, leaveMsg, ct);
            break;
        }

        if (!string.IsNullOrEmpty(session.AccountId))
            await _storage.Sessions.SetOnlineAsync(session.AccountId, false, ct);
    }

    private async Task PersistEquipmentAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        await _storage.Characters.SaveInventoryAsync(
            player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
        await _storage.Characters.SaveEquipmentAsync(
            player.CharacterId, EquipmentHelper.Serialize(player.Equipment), ct);
        await _storage.Characters.SavePositionAsync(
            player.CharacterId, player.MapId, player.X, player.Y, player.Hp, player.Mp, ct);
    }

    private async Task HandleEquipAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = EquipReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.ItemId) ||
            !_versionPack.Items.TryGetValue(req.ItemId, out var tmpl) ||
            !string.Equals(tmpl.Kind, "equipment", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tmpl.Slot) ||
            !EquipmentHelper.ValidSlots.Contains(tmpl.Slot))
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = "Item is not equippable"
            }, ct);
            return;
        }

        string targetSlot = string.IsNullOrWhiteSpace(req.Slot)
            ? tmpl.Slot.ToLowerInvariant()
            : req.Slot.Trim().ToLowerInvariant();

        if (!string.Equals(targetSlot, tmpl.Slot, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = $"Item '{tmpl.ItemId}' belongs to slot '{tmpl.Slot}', not '{targetSlot}'"
            }, ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tmpl.RequiredClass) &&
            !string.Equals(tmpl.RequiredClass, player.ClassId, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = $"Requires class '{tmpl.RequiredClass}'"
            }, ct);
            return;
        }

        if (player.Level < tmpl.RequiredLevel)
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = $"Requires level {tmpl.RequiredLevel}"
            }, ct);
            return;
        }

        if (!InventoryHelper.TryRemoveItem(player.Inventory, tmpl.ItemId, 1))
        {
            await session.SendAsync(MsgId.EquipResp, frame.Seq, new EquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = "Item not in inventory"
            }, ct);
            return;
        }

        var existingSlot = player.Equipment.FirstOrDefault(e =>
            string.Equals(e.Slot, targetSlot, StringComparison.OrdinalIgnoreCase));
        int preservedEnhance = 0;
        if (existingSlot != null)
        {
            if (!string.IsNullOrEmpty(existingSlot.ItemId))
                InventoryHelper.AddItem(player.Inventory, existingSlot.ItemId, 1);
            preservedEnhance = existingSlot.EnhanceLevel;
            existingSlot.ItemId = tmpl.ItemId;
        }
        else
        {
            player.Equipment.Add(new EquippedItemState
            {
                Slot = targetSlot,
                ItemId = tmpl.ItemId,
                EnhanceLevel = 0
            });
        }

        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await PersistEquipmentAsync(player, ct);

        var resp = new EquipResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Equipped",
            EffectiveAtk = player.Atk,
            EffectiveDef = player.Def,
            EffectiveMaxHp = player.MaxHp
        };
        foreach (var eq in EquipmentHelper.ToProto(player.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);
        foreach (var inv in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(inv);

        await session.SendAsync(MsgId.EquipResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "Equip",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

        await _eventBus.PublishAsync(new ItemEquippedEvent(
            session, player.Name, player.CharacterId, targetSlot, tmpl.ItemId, preservedEnhance,
            player.Atk, player.Def, player.MaxHp), ct);
    }

    private async Task HandleUnequipAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = UnequipReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.UnequipResp, frame.Seq, new UnequipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }

        string slot = (req.Slot ?? string.Empty).Trim().ToLowerInvariant();
        var existingSlot = player.Equipment.FirstOrDefault(e =>
            string.Equals(e.Slot, slot, StringComparison.OrdinalIgnoreCase));
        if (existingSlot is null || string.IsNullOrEmpty(existingSlot.ItemId))
        {
            await session.SendAsync(MsgId.UnequipResp, frame.Seq, new UnequipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEquipDenied,
                Message = "Slot is empty"
            }, ct);
            return;
        }

        InventoryHelper.AddItem(player.Inventory, existingSlot.ItemId, 1);
        player.Equipment.Remove(existingSlot);

        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await PersistEquipmentAsync(player, ct);

        var resp = new UnequipResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "Unequipped",
            EffectiveAtk = player.Atk,
            EffectiveDef = player.Def,
            EffectiveMaxHp = player.MaxHp
        };
        foreach (var eq in EquipmentHelper.ToProto(player.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);
        foreach (var inv in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(inv);

        await session.SendAsync(MsgId.UnequipResp, frame.Seq, resp, ct);
        await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
        {
            CharacterId = player.CharacterId,
            Reason = "Unequip",
            Gold = player.Gold
        }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);
    }

    private async Task HandleEnhanceEquipAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = EnhanceEquipReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.EnhanceEquipResp, frame.Seq, new EnhanceEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world"
            }, ct);
            return;
        }

        string slot = string.IsNullOrWhiteSpace(req.Slot)
            ? "weapon"
            : req.Slot.Trim().ToLowerInvariant();

        var equipped = player.Equipment.FirstOrDefault(e =>
            string.Equals(e.Slot, slot, StringComparison.OrdinalIgnoreCase));
        if (equipped is null || string.IsNullOrEmpty(equipped.ItemId))
        {
            await session.SendAsync(MsgId.EnhanceEquipResp, frame.Seq, new EnhanceEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEnhanceDenied,
                Message = $"No equipment in slot '{slot}'"
            }, ct);
            return;
        }

        int targetLevel = equipped.EnhanceLevel + 1;
        if (!_versionPack.EnhanceRules.TryGetValue(targetLevel, out var rule))
        {
            await session.SendAsync(MsgId.EnhanceEquipResp, frame.Seq, new EnhanceEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrEnhanceDenied,
                Message = $"Max enhance level (+{equipped.EnhanceLevel}) reached"
            }, ct);
            return;
        }

        if (player.Gold < rule.CostGold)
        {
            await session.SendAsync(MsgId.EnhanceEquipResp, frame.Seq, new EnhanceEquipResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = $"Need {rule.CostGold} gold, have {player.Gold}",
                Gold = player.Gold,
                EffectiveAtk = player.Atk,
                EffectiveDef = player.Def,
                EffectiveMaxHp = player.MaxHp
            }, ct);
            return;
        }

        player.Gold -= rule.CostGold;
        bool succeeded;
        if (rule.SuccessRate >= 1.0)
        {
            succeeded = true;
        }
        else if (rule.SuccessRate <= 0.0)
        {
            succeeded = false;
        }
        else
        {
            double roll;
            lock (Rng) roll = Rng.NextDouble();
            succeeded = roll <= rule.SuccessRate;
        }

        if (succeeded)
            equipped.EnhanceLevel = targetLevel;

        EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
        await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
        await _storage.Characters.SaveEquipmentAsync(
            player.CharacterId, EquipmentHelper.Serialize(player.Equipment), ct);

        var updatedProto = EquipmentHelper.ToSlotProto(equipped, _versionPack.Items);
        var resp = new EnhanceEquipResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = succeeded ? $"Enhanced to +{equipped.EnhanceLevel}" : "Enhance roll failed",
            EnhanceSucceeded = succeeded,
            UpdatedSlot = updatedProto,
            Gold = player.Gold,
            EffectiveAtk = player.Atk,
            EffectiveDef = player.Def,
            EffectiveMaxHp = player.MaxHp
        };
        foreach (var eq in EquipmentHelper.ToProto(player.Equipment, _versionPack.Items))
            resp.Equipment.Add(eq);

        await session.SendAsync(MsgId.EnhanceEquipResp, frame.Seq, resp, ct);

        await _eventBus.PublishAsync(new EquipmentEnhancedEvent(
            session, player.Name, player.CharacterId, slot, equipped.ItemId, equipped.EnhanceLevel,
            succeeded, rule.CostGold, player.Atk), ct);
    }


    private async Task GrantExpFromKillAsync(WorldPlayerEntity killer, MonsterEntity monster, CancellationToken ct)
    {
        int reward = Math.Max(0, monster.Template.ExpReward);
        if (reward <= 0)
            return;

        var recipients = GetPartyExpShareRecipients(killer);
        var cfg = PartyCfg;
        int pool = reward;
        if (recipients.Count > 1 && cfg.ExpBonusPercent > 0)
            pool = reward + (reward * cfg.ExpBonusPercent) / 100;
        int share = Math.Max(1, pool / recipients.Count);
        int remainder = pool - share * recipients.Count;

        for (int i = 0; i < recipients.Count; i++)
        {
            int gained = share + (i == 0 ? remainder : 0); // remainder to killer (index 0)
            await ApplyExpGainAsync(recipients[i], gained, monster.MonsterId, ct);
        }
    }

    private async Task ApplyExpGainAsync(WorldPlayerEntity player, int reward, string source, CancellationToken ct)
    {
        int maxLv = LevelCurve.MaxLevel(_versionPack.Levels);
        if (player.Level >= maxLv)
        {
            await player.Session.SendAsync(MsgId.ExpGainNotify, 0, new ExpGainNotify
            {
                GainedExp = 0,
                TotalExp = player.Exp,
                Level = player.Level,
                ExpToNext = 0,
                Source = source
            }, ct);
            return;
        }

        player.Exp += reward;
        await player.Session.SendAsync(MsgId.ExpGainNotify, 0, new ExpGainNotify
        {
            GainedExp = reward,
            TotalExp = player.Exp,
            Level = player.Level,
            ExpToNext = LevelCurve.ExpToNext(_versionPack.Levels, player.Level),
            Source = source
        }, ct);

        bool leveled = false;
        int oldLevel = player.Level;
        while (player.Level < maxLv)
        {
            int need = LevelCurve.ExpToNext(_versionPack.Levels, player.Level);
            if (need <= 0 || player.Exp < need)
                break;
            player.Exp -= need;
            player.Level++;
            leveled = true;
        }

        if (leveled)
        {
            if (!_versionPack.Classes.TryGetValue(player.ClassId, out var cls))
            {
                cls = new ClassConfigItem
                {
                    ClassId = player.ClassId,
                    BaseAtk = player.BaseAtk,
                    BaseDef = player.BaseDef,
                    BaseHp = player.BaseMaxHp,
                    BaseMp = player.BaseMaxMp
                };
            }
            LevelCurve.ApplyBaseStats(player, cls, _versionPack.Levels);
            EquipmentHelper.RecalculateStats(player, _versionPack.Items, _versionPack.Buffs);
            player.Hp = player.MaxHp;
            player.Mp = player.MaxMp;

            var notify = new LevelUpNotify
            {
                OldLevel = oldLevel,
                NewLevel = player.Level,
                Exp = player.Exp,
                ExpToNext = LevelCurve.ExpToNext(_versionPack.Levels, player.Level),
                BaseAtk = player.BaseAtk,
                BaseDef = player.BaseDef,
                BaseMaxHp = player.BaseMaxHp,
                BaseMaxMp = player.BaseMaxMp,
                EffectiveAtk = player.Atk,
                EffectiveDef = player.Def,
                EffectiveMaxHp = player.MaxHp,
                Hp = player.Hp,
                Mp = player.Mp
            };
            await player.Session.SendAsync(MsgId.LevelUpNotify, 0, notify, ct);

            await _eventBus.PublishAsync(new PlayerLeveledUpEvent(
                player.Session, player.Name, player.CharacterId, oldLevel, player.Level,
                player.Exp, player.Atk, player.Def, player.MaxHp), ct);
        }

        await _storage.Characters.SaveLevelExpAsync(
            player.CharacterId, player.Level, player.Exp, player.MaxHp, player.MaxMp, ct);
        await _storage.Characters.SavePositionAsync(
            player.CharacterId, player.MapId, player.X, player.Y, player.Hp, player.Mp, ct);
    }

    private async Task HandleUseItemAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = UseItemReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetPlayer(session, out var player, out _) || player is null)
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (player.IsDead || player.Hp <= 0)
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrDead,
                Message = "Cannot use items while dead."
            }, ct);
            return;
        }

        string itemId = (req.ItemId ?? string.Empty).Trim();
        int count = req.Count <= 0 ? 1 : req.Count;
        if (count != 1)
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                Message = "Only count=1 is supported."
            }, ct);
            return;
        }

        if (!_versionPack.Items.TryGetValue(itemId, out var tmpl))
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                Message = "Unknown item."
            }, ct);
            return;
        }

        bool isSkillbook = string.Equals(tmpl.Kind, "skillbook", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(tmpl.TeachesSkillId);
        bool isBuffScroll = string.Equals(tmpl.Kind, "consumable", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(tmpl.AppliesBuffId);
        bool isHeal = string.Equals(tmpl.Kind, "consumable", StringComparison.OrdinalIgnoreCase)
            && (tmpl.HealHp > 0 || tmpl.HealMp > 0);
        bool isAppearanceUnlock =
            (string.Equals(tmpl.Kind, "mount_unlock", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tmpl.UnlockMountId))
            || (string.Equals(tmpl.Kind, "fashion_unlock", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tmpl.UnlockFashionId));
        bool isPetUnlock =
            string.Equals(tmpl.Kind, "pet_unlock", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tmpl.UnlockPetId);

        if (isPetUnlock)
        {
            var (okPet, msgPet) = await TryUnlockPetItemAsync(player, tmpl, itemId, ct);
            if (!okPet)
            {
                await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                    Message = msgPet
                }, ct);
                return;
            }
            int remPet = player.Inventory.Where(s =>
                string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Count);
            var petResp = new UseItemResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = msgPet,
                ItemId = itemId,
                RemainingCount = remPet,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                Mp = player.Mp,
                MaxMp = player.MaxMp
            };
            foreach (var slot in InventoryHelper.ToProto(player.Inventory))
                petResp.Inventory.Add(slot);
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, petResp, ct);
            return;
        }

        if (isAppearanceUnlock)
        {
            var (okUnlock, msgUnlock) = await TryUnlockAppearanceItemAsync(player, tmpl, itemId, ct);
            if (!okUnlock)
            {
                await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                    Message = msgUnlock
                }, ct);
                return;
            }
            int remUnlock = player.Inventory.Where(s =>
                string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Count);
            var unlockResp = new UseItemResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = msgUnlock,
                ItemId = itemId,
                RemainingCount = remUnlock,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                Mp = player.Mp,
                MaxMp = player.MaxMp
            };
            foreach (var slot in InventoryHelper.ToProto(player.Inventory))
                unlockResp.Inventory.Add(slot);
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, unlockResp, ct);
            return;
        }

        if (!isSkillbook && !isBuffScroll && !isHeal)
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                Message = "Item is not usable."
            }, ct);
            return;
        }

        if (isSkillbook)
        {
            var (ok, err, state) = await TryLearnSkillAsync(player, tmpl.TeachesSkillId, consumeItemId: itemId, ct);
            if (!ok)
            {
                await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                    Message = MessageFor(err)
                }, ct);
                return;
            }
            int remBook = player.Inventory.Where(s =>
                string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Count);
            var bookResp = new UseItemResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = $"Learned {tmpl.TeachesSkillId}",
                ItemId = itemId,
                RemainingCount = remBook,
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                Mp = player.Mp,
                MaxMp = player.MaxMp
            };
            foreach (var slot in InventoryHelper.ToProto(player.Inventory))
                bookResp.Inventory.Add(slot);
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, bookResp, ct);
            await SendSkillListNotifyAsync(player, "learn", ct);
            return;
        }

        if (!InventoryHelper.TryRemoveItem(player.Inventory, itemId, 1))
        {
            await session.SendAsync(MsgId.UseItemResp, frame.Seq, new UseItemResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrUseItemDenied,
                Message = "Item not in inventory."
            }, ct);
            return;
        }

        int healHp = isHeal ? tmpl.HealHp : 0;
        int healMp = isHeal ? tmpl.HealMp : 0;
        int beforeHp = player.Hp;
        int beforeMp = player.Mp;
        player.Hp = Math.Min(player.MaxHp, player.Hp + healHp);
        player.Mp = Math.Min(player.MaxMp, player.Mp + healMp);
        int actualHp = player.Hp - beforeHp;
        int actualMp = player.Mp - beforeMp;

        await _storage.Characters.SaveInventoryAsync(
            player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);
        await _storage.Characters.SavePositionAsync(
            player.CharacterId, player.MapId, player.X, player.Y, player.Hp, player.Mp, ct);

        if (isBuffScroll)
            await ApplyBuffAsync(player, tmpl.AppliesBuffId, ct);

        int remaining = player.Inventory.Where(s =>
            string.Equals(s.ItemId, itemId, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Count);

        var resp = new UseItemResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK",
            ItemId = itemId,
            RemainingCount = remaining,
            Hp = player.Hp,
            MaxHp = player.MaxHp,
            Mp = player.Mp,
            MaxMp = player.MaxMp
        };
        foreach (var slot in InventoryHelper.ToProto(player.Inventory))
            resp.Inventory.Add(slot);

        await session.SendAsync(MsgId.UseItemResp, frame.Seq, resp, ct);

        if (actualHp > 0 || actualMp > 0)
        {
            await session.SendAsync(MsgId.CombatSync, 0, new CombatSync
            {
                AttackerEntityId = player.EntityId,
                TargetEntityId = player.EntityId,
                SkillId = "use_item",
                Damage = 0,
                TargetHp = player.Hp,
                TargetMaxHp = player.MaxHp,
                Result = CombatResultKind.CombatHeal,
                TargetDied = false
            }, ct);
        }

        await _eventBus.PublishAsync(new ItemUsedEvent(
            session, player.Name, player.CharacterId, itemId, actualHp, actualMp, player.Hp, player.Mp), ct);
    }

    public async ValueTask DisposeAiAsync()
    {
        if (_aiCts != null)
        {
            await _aiCts.CancelAsync();
            if (_aiTask != null)
            {
                try { await _aiTask; } catch { }
            }
            _aiCts.Dispose();
        }
    }
}

internal static class ProtoAlsoExt
{
    public static T Also<T>(this T obj, Action<T> act)
    {
        act(obj);
        return obj;
    }
}
