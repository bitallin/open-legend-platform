using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.UnitySdk
{
    /// <summary>
    /// Unity 2022 LTS pure C# TCP client (PC + Android). PumpMainThreadEvents() in Update().
    /// Flow: Handshake → Register/Login → CharList/Create/Select → EnterWorld → Move / Attack / Pickup / Talk / Shop / Quest.
    /// </summary>
    public sealed class LegendUnityNetworkClient : IDisposable
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private uint _seq;

        private readonly ConcurrentDictionary<uint, TaskCompletionSource<PacketFrame>> _pending = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        public string? SessionToken { get; private set; }
        public string? AccountId { get; private set; }
        public string? CharacterId { get; private set; }
        public EntitySnapshot? SelfEntity { get; private set; }
        public IReadOnlyList<CharacterSummary> LastCharList { get; private set; } = Array.Empty<CharacterSummary>();

        public event Action<EntitySnapshot>? OnSelfEnteredWorld;
        public event Action<EntitySnapshot>? OnEntityEnterAoi;
        public event Action<string, string>? OnEntityLeaveAoi;
        public event Action<MoveSync>? OnMoveSync;
        public event Action<ServerNotice>? OnServerNotice;
        public event Action<MapChangeNotify>? OnMapChange;
        public event Action<CombatSync>? OnCombatSync;
        public event Action<EntitySnapshot>? OnEntitySpawn;
        public event Action<string, string>? OnEntityDespawn; // entityId, reason
        public event Action<LootDropNotify>? OnLootDrop;
        public event Action<InventoryNotify>? OnInventoryNotify;
        public event Action<QuestProgressNotify>? OnQuestProgress;
        public event Action<ExpGainNotify>? OnExpGain;
        public event Action<LevelUpNotify>? OnLevelUp;
        public event Action<ChatNotify>? OnChatNotify;
        public event Action<StallSoldNotify>? OnStallSoldNotify;
        public event Action<SkillListNotify>? OnSkillListNotify;
        public event Action<SkillLevelUpNotify>? OnSkillLevelUpNotify;
        public event Action<BuffApplyNotify>? OnBuffApplyNotify;
        public event Action<BuffRemoveNotify>? OnBuffRemoveNotify;
        public event Action<SiegeStateNotify>? OnSiegeStateNotify;
        public int Gold { get; private set; }
        public int Level { get; private set; } = 1;
        public int Exp { get; private set; }
        public string GuildId { get; private set; } = string.Empty;
        public string GuildName { get; private set; } = string.Empty;
        public string GuildRank { get; private set; } = string.Empty;

        public void PumpMainThreadEvents()
        {
            while (_mainThreadActions.TryDequeue(out var action))
                action.Invoke();
        }

        public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, port).ConfigureAwait(false);
            _stream = _tcp.GetStream();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        }

        public async Task HandshakeAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.HandshakeReq, new HandshakeReq
            {
                ProtocolVersion = ProtocolConstants.CurrentVersion,
                ClientBuild = "unity-2022-lts",
                DeviceId = "unity-device"
            }, ct).ConfigureAwait(false);
            var hs = HandshakeResp.Parser.ParseFrom(frame.Payload);
            if (!hs.Accepted)
                throw new InvalidOperationException($"Handshake rejected: {hs.Message}");
        }

        public async Task RegisterAsync(string username, string password, string platform = "PC", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.RegisterReq, new RegisterReq
            {
                Username = username,
                Password = password,
                ClientPlatform = platform
            }, ct).ConfigureAwait(false);
            var resp = RegisterResp.Parser.ParseFrom(frame.Payload);
            if (!resp.Success && resp.ErrorCode != ProtocolErrorCode.ErrUsernameTaken)
                throw new InvalidOperationException($"Register failed: {resp.Message}");
        }

        public async Task LoginAsync(string username, string password, string platform = "PC", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.LoginReq, new LoginReq
            {
                Username = username,
                Password = password,
                ClientPlatform = platform
            }, ct).ConfigureAwait(false);
            var login = LoginResp.Parser.ParseFrom(frame.Payload);
            if (!login.Success)
                throw new InvalidOperationException($"Login failed: {login.Message}");
            SessionToken = login.SessionToken;
            AccountId = login.AccountId;
        }

        public async Task<IReadOnlyList<CharacterSummary>> CharListAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.CharListReq, new CharListReq
            {
                SessionToken = SessionToken ?? string.Empty
            }, ct).ConfigureAwait(false);
            var resp = CharListResp.Parser.ParseFrom(frame.Payload);
            if (!resp.Success)
                throw new InvalidOperationException(resp.Message);
            var list = new List<CharacterSummary>(resp.Characters);
            LastCharList = list;
            return list;
        }

        public async Task<CharacterSummary> CreateCharAsync(string name, string classId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.CreateCharReq, new CreateCharReq
            {
                SessionToken = SessionToken ?? string.Empty,
                Name = name,
                ClassId = classId
            }, ct).ConfigureAwait(false);
            var resp = CreateCharResp.Parser.ParseFrom(frame.Payload);
            if (!resp.Success)
                throw new InvalidOperationException($"CreateChar failed: {resp.Message}");
            return resp.Character;
        }

        public async Task DeleteCharAsync(string characterId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.DeleteCharReq, new DeleteCharReq
            {
                SessionToken = SessionToken ?? string.Empty,
                CharacterId = characterId
            }, ct).ConfigureAwait(false);
            var resp = DeleteCharResp.Parser.ParseFrom(frame.Payload);
            if (!resp.Success)
                throw new InvalidOperationException(resp.Message);
        }

        public async Task SelectCharAsync(string characterId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SelectCharReq, new SelectCharReq
            {
                SessionToken = SessionToken ?? string.Empty,
                CharacterId = characterId
            }, ct).ConfigureAwait(false);
            var resp = SelectCharResp.Parser.ParseFrom(frame.Payload);
            if (!resp.Success)
                throw new InvalidOperationException(resp.Message);
            CharacterId = resp.Character.CharacterId;
        }

        public async Task EnterWorldAsync(string realmId, string? characterId = null, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.EnterWorldReq, new EnterWorldReq
            {
                SessionToken = SessionToken ?? string.Empty,
                RealmId = realmId,
                CharacterId = characterId ?? CharacterId ?? string.Empty
            }, ct).ConfigureAwait(false);
            var enter = EnterWorldResp.Parser.ParseFrom(frame.Payload);
            if (!enter.Success)
                throw new InvalidOperationException($"EnterWorld failed: {enter.Message}");
            SelfEntity = enter.SelfEntity;
            Gold = enter.Gold;
            Level = enter.Level;
            Exp = enter.Exp;
            GuildId = enter.GuildId ?? string.Empty;
            GuildName = enter.GuildName ?? string.Empty;
            GuildRank = enter.GuildRank ?? string.Empty;
            _mainThreadActions.Enqueue(() =>
            {
                OnSelfEnteredWorld?.Invoke(enter.SelfEntity);
                foreach (var other in enter.NearbyEntities)
                    OnEntityEnterAoi?.Invoke(other);
            });
        }

        /// <summary>Convenience: register/login, create-or-select, enter world.</summary>
        public async Task ConnectLoginSelectEnterAsync(
            string host, int port,
            string username, string password,
            string characterName, string classId = "warrior",
            string platform = "PC", CancellationToken ct = default)
        {
            await ConnectAsync(host, port, ct).ConfigureAwait(false);
            await HandshakeAsync(ct).ConfigureAwait(false);
            await RegisterAsync(username, password, platform, ct).ConfigureAwait(false);
            await LoginAsync(username, password, platform, ct).ConfigureAwait(false);

            var chars = await CharListAsync(ct).ConfigureAwait(false);
            CharacterSummary? chosen = null;
            foreach (var c in chars)
            {
                if (c.Name == characterName) { chosen = c; break; }
            }
            if (chosen == null)
                chosen = await CreateCharAsync(characterName, classId, ct).ConfigureAwait(false);

            await SelectCharAsync(chosen.CharacterId, ct).ConfigureAwait(false);
            await EnterWorldAsync("realm-classic-01", chosen.CharacterId, ct).ConfigureAwait(false);
        }


        public async Task<(AttackResp Resp, CombatSync? Combat)> RequestAttackAsync(string targetEntityId, string skillId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.AttackReq, new AttackReq
            {
                TargetEntityId = targetEntityId,
                SkillId = skillId
            }, ct).ConfigureAwait(false);
            var resp = AttackResp.Parser.ParseFrom(frame.Payload);
            // CombatSync usually arrives as a push; caller may also wait on OnCombatSync.
            return (resp, null);
        }

        public async Task<PickupResp> RequestPickupAsync(string lootEntityId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.PickupReq, new PickupReq
            {
                LootEntityId = lootEntityId
            }, ct).ConfigureAwait(false);
            return PickupResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<TalkResp> RequestTalkAsync(string npcEntityId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.TalkReq, new TalkReq
            {
                NpcEntityId = npcEntityId
            }, ct).ConfigureAwait(false);
            var resp = TalkResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
                Gold = resp.Gold;
            return resp;
        }

        public async Task<ShopBuyResp> RequestShopBuyAsync(string npcEntityId, string itemId, int count = 1, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.ShopBuyReq, new ShopBuyReq
            {
                NpcEntityId = npcEntityId,
                ItemId = itemId,
                Count = count
            }, ct).ConfigureAwait(false);
            var resp = ShopBuyResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
                Gold = resp.Gold;
            return resp;
        }

        public async Task<QuestAcceptResp> RequestQuestAcceptAsync(string npcEntityId, string questId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.QuestAcceptReq, new QuestAcceptReq
            {
                NpcEntityId = npcEntityId,
                QuestId = questId
            }, ct).ConfigureAwait(false);
            return QuestAcceptResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<QuestTurnInResp> RequestQuestTurnInAsync(string npcEntityId, string questId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.QuestTurnInReq, new QuestTurnInReq
            {
                NpcEntityId = npcEntityId,
                QuestId = questId
            }, ct).ConfigureAwait(false);
            var resp = QuestTurnInResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
                Gold = resp.Gold;
            return resp;
        }

        public async Task<EquipResp> RequestEquipAsync(string itemId, string slot = "", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.EquipReq, new EquipReq
            {
                ItemId = itemId,
                Slot = slot
            }, ct).ConfigureAwait(false);
            return EquipResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<UnequipResp> RequestUnequipAsync(string slot, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.UnequipReq, new UnequipReq
            {
                Slot = slot
            }, ct).ConfigureAwait(false);
            return UnequipResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<EnhanceEquipResp> RequestEnhanceEquipAsync(string slot = "weapon", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.EnhanceEquipReq, new EnhanceEquipReq
            {
                Slot = slot
            }, ct).ConfigureAwait(false);
            var resp = EnhanceEquipResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
                Gold = resp.Gold;
            return resp;
        }

        public async Task<UseItemResp> RequestUseItemAsync(string itemId, int count = 1, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.UseItemReq, new UseItemReq
            {
                ItemId = itemId,
                Count = count
            }, ct).ConfigureAwait(false);
            var resp = UseItemResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success && SelfEntity != null)
            {
                SelfEntity.Hp = resp.Hp;
                SelfEntity.MaxHp = resp.MaxHp;
            }
            return resp;
        }

        
        public async Task<GuildCreateResp> RequestGuildCreateAsync(string name, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.GuildCreateReq, new GuildCreateReq { Name = name }, ct).ConfigureAwait(false);
            var resp = GuildCreateResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
            {
                GuildId = resp.GuildId;
                GuildName = resp.GuildName;
                GuildRank = resp.GuildRank;
                Gold = resp.Gold;
            }
            return resp;
        }

        public async Task<GuildJoinResp> RequestGuildJoinAsync(string guildName, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.GuildJoinReq, new GuildJoinReq { GuildName = guildName }, ct).ConfigureAwait(false);
            var resp = GuildJoinResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
            {
                GuildId = resp.GuildId;
                GuildName = resp.GuildName;
                GuildRank = resp.GuildRank;
            }
            return resp;
        }

        public async Task<GuildLeaveResp> RequestGuildLeaveAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.GuildLeaveReq, new GuildLeaveReq(), ct).ConfigureAwait(false);
            var resp = GuildLeaveResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
            {
                GuildId = string.Empty;
                GuildName = string.Empty;
                GuildRank = string.Empty;
            }
            return resp;
        }

        public async Task<GuildInfoResp> RequestGuildInfoAsync(string guildId = "", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.GuildInfoReq, new GuildInfoReq { GuildId = guildId ?? "" }, ct).ConfigureAwait(false);
            return GuildInfoResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task RequestChatAsync(ChatChannel channel, string content, string targetName = "", CancellationToken ct = default)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");
            uint seq = System.Threading.Interlocked.Increment(ref _seq);
            await PacketCodec.WriteFrameAsync(_stream, MsgId.ChatReq, seq, new ChatReq
            {
                Channel = channel,
                Content = content,
                TargetName = targetName ?? ""
            }, ct).ConfigureAwait(false);
        }

        public Task RequestWhisperAsync(string targetName, string content, CancellationToken ct = default)
            => RequestChatAsync(ChatChannel.ChatWhisper, content, targetName, ct);

        public async Task<PartyInviteResp> RequestPartyInviteAsync(string targetName, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.PartyInviteReq, new PartyInviteReq { TargetName = targetName }, ct).ConfigureAwait(false);
            return PartyInviteResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<PartyAcceptResp> RequestPartyAcceptAsync(string partyId, bool accept = true, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.PartyAcceptReq, new PartyAcceptReq { PartyId = partyId, Accept = accept }, ct).ConfigureAwait(false);
            return PartyAcceptResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<PartyLeaveResp> RequestPartyLeaveAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.PartyLeaveReq, new PartyLeaveReq(), ct).ConfigureAwait(false);
            return PartyLeaveResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<PartyKickResp> RequestPartyKickAsync(string targetCharacterId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.PartyKickReq, new PartyKickReq { TargetCharacterId = targetCharacterId }, ct).ConfigureAwait(false);
            return PartyKickResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<FriendAddResp> RequestFriendAddAsync(string friendName, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.FriendAddReq, new FriendAddReq { FriendName = friendName }, ct).ConfigureAwait(false);
            return FriendAddResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<FriendRemoveResp> RequestFriendRemoveAsync(string friendCharacterId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.FriendRemoveReq, new FriendRemoveReq { FriendCharacterId = friendCharacterId }, ct).ConfigureAwait(false);
            return FriendRemoveResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<FriendListResp> RequestFriendListAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.FriendListReq, new FriendListReq(), ct).ConfigureAwait(false);
            return FriendListResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<StallListResp> RequestStallListAsync(string itemId, int count, int unitPriceGold, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.StallListReq, new StallListReq
            {
                ItemId = itemId,
                Count = count,
                UnitPriceGold = unitPriceGold
            }, ct).ConfigureAwait(false);
            return StallListResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<StallBrowseResp> RequestStallBrowseAsync(string itemIdFilter = "", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.StallBrowseReq, new StallBrowseReq
            {
                ItemIdFilter = itemIdFilter ?? ""
            }, ct).ConfigureAwait(false);
            return StallBrowseResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<StallBuyResp> RequestStallBuyAsync(string listingId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.StallBuyReq, new StallBuyReq
            {
                ListingId = listingId
            }, ct).ConfigureAwait(false);
            var resp = StallBuyResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success)
                Gold = resp.Gold;
            return resp;
        }

        public async Task<StallCancelResp> RequestStallCancelAsync(string listingId, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.StallCancelReq, new StallCancelReq
            {
                ListingId = listingId
            }, ct).ConfigureAwait(false);
            return StallCancelResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<LearnSkillResp> RequestLearnSkillAsync(string itemId, string skillId = "", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.LearnSkillReq, new LearnSkillReq
            {
                ItemId = itemId ?? "",
                SkillId = skillId ?? ""
            }, ct).ConfigureAwait(false);
            return LearnSkillResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<SkillListResp> RequestSkillListAsync(CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SkillListReq, new SkillListReq(), ct).ConfigureAwait(false);
            return SkillListResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<SiegeDeclareResp> RequestSiegeDeclareAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SiegeDeclareReq, new SiegeDeclareReq { CastleId = castleId ?? "sabuk_castle" }, ct).ConfigureAwait(false);
            var resp = SiegeDeclareResp.Parser.ParseFrom(frame.Payload);
            if (resp.Success) Gold = resp.NewGold;
            return resp;
        }

        public async Task<SiegeStatusResp> RequestSiegeStatusAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SiegeStatusReq, new SiegeStatusReq { CastleId = castleId ?? "sabuk_castle" }, ct).ConfigureAwait(false);
            return SiegeStatusResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<SiegeControlResp> RequestSiegeControlAsync(string action, string castleId = "sabuk_castle", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SiegeControlReq, new SiegeControlReq { CastleId = castleId ?? "sabuk_castle", Action = action ?? "start" }, ct).ConfigureAwait(false);
            return SiegeControlResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<SiegeCaptureResp> RequestSiegeCaptureAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.SiegeCaptureReq, new SiegeCaptureReq { CastleId = castleId ?? "sabuk_castle" }, ct).ConfigureAwait(false);
            return SiegeCaptureResp.Parser.ParseFrom(frame.Payload);
        }

        public async Task<MoveSync> RequestMoveAsync(int toX, int toY, int direction = 4, CancellationToken ct = default)
        {
            // Ignore stale MoveSync if a portal already changed our map.
            string? mapBefore = SelfEntity?.MapId;
            var frame = await SendRequestAsync(MsgId.MoveReq, new MoveReq
            {
                ToX = toX, ToY = toY, Direction = direction
            }, ct).ConfigureAwait(false);
            var sync = MoveSync.Parser.ParseFrom(frame.Payload);
            if (sync.Accepted && SelfEntity != null &&
                (mapBefore is null || string.Equals(SelfEntity.MapId, mapBefore, System.StringComparison.Ordinal)))
            {
                SelfEntity.X = sync.ToX;
                SelfEntity.Y = sync.ToY;
                SelfEntity.InSafeZone = sync.InSafeZone;
            }
            return sync;
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            if (_stream == null) return;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frameOpt = await PacketCodec.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                    if (frameOpt == null) break;
                    var frame = frameOpt.Value;
                    if (frame.Seq != 0 && _pending.TryRemove(frame.Seq, out var tcs))
                    {
                        if (frame.MsgId == MsgId.MoveSync)
                        {
                            var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnMoveSync?.Invoke(sync));
                        }
                        tcs.TrySetResult(frame);
                        continue;
                    }
                    switch (frame.MsgId)
                    {
                        case MsgId.MoveSync:
                            var ms = MoveSync.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnMoveSync?.Invoke(ms));
                            break;
                        case MsgId.EntityEnterAoi:
                            var en = EntityEnterAoi.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntityEnterAoi?.Invoke(en.Entity));
                            break;
                        case MsgId.EntityLeaveAoi:
                            var lv = EntityLeaveAoi.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntityLeaveAoi?.Invoke(lv.EntityId, lv.Reason));
                            break;
                        case MsgId.ServerNotice:
                            var sn = ServerNotice.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnServerNotice?.Invoke(sn));
                            break;
                        case MsgId.MapChangeNotify:
                            var mc = MapChangeNotify.Parser.ParseFrom(frame.Payload);
                            if (SelfEntity != null && mc.EntityId == SelfEntity.EntityId)
                            {
                                SelfEntity.MapId = mc.ToMapId;
                                SelfEntity.X = mc.ToX;
                                SelfEntity.Y = mc.ToY;
                                SelfEntity.InSafeZone = mc.InSafeZone;
                            }
                            _mainThreadActions.Enqueue(() => OnMapChange?.Invoke(mc));
                            break;
                        case MsgId.CombatSync:
                            var cs = CombatSync.Parser.ParseFrom(frame.Payload);
                            if (SelfEntity != null && cs.TargetEntityId == SelfEntity.EntityId)
                            {
                                SelfEntity.Hp = cs.TargetHp;
                                SelfEntity.MaxHp = cs.TargetMaxHp;
                            }
                            _mainThreadActions.Enqueue(() => OnCombatSync?.Invoke(cs));
                            break;
                        case MsgId.EntitySpawn:
                            var esp = EntitySpawn.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntitySpawn?.Invoke(esp.Entity));
                            break;
                        case MsgId.EntityDespawn:
                            var ed = EntityDespawn.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntityDespawn?.Invoke(ed.EntityId, ed.Reason));
                            break;
                        case MsgId.LootDropNotify:
                            var ld = LootDropNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnLootDrop?.Invoke(ld));
                            break;
                        case MsgId.InventoryNotify:
                            var inv = InventoryNotify.Parser.ParseFrom(frame.Payload);
                            Gold = inv.Gold;
                            _mainThreadActions.Enqueue(() => OnInventoryNotify?.Invoke(inv));
                            break;
                        case MsgId.QuestProgressNotify:
                            var qp = QuestProgressNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnQuestProgress?.Invoke(qp));
                            break;
                        case MsgId.ExpGainNotify:
                            var eg = ExpGainNotify.Parser.ParseFrom(frame.Payload);
                            Exp = eg.TotalExp;
                            Level = eg.Level;
                            _mainThreadActions.Enqueue(() => OnExpGain?.Invoke(eg));
                            break;
                        case MsgId.LevelUpNotify:
                            var lu = LevelUpNotify.Parser.ParseFrom(frame.Payload);
                            Level = lu.NewLevel;
                            Exp = lu.Exp;
                            if (SelfEntity != null)
                            {
                                SelfEntity.Hp = lu.Hp;
                                SelfEntity.MaxHp = lu.EffectiveMaxHp;
                            }
                            _mainThreadActions.Enqueue(() => OnLevelUp?.Invoke(lu));
                            break;
                        case MsgId.SkillListNotify:
                            var skl = SkillListNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnSkillListNotify?.Invoke(skl));
                            break;
                        case MsgId.SkillLevelUpNotify:
                            var sku = SkillLevelUpNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnSkillLevelUpNotify?.Invoke(sku));
                            break;
                        case MsgId.BuffApplyNotify:
                            var ba = BuffApplyNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnBuffApplyNotify?.Invoke(ba));
                            break;
                        case MsgId.BuffRemoveNotify:
                            var br = BuffRemoveNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnBuffRemoveNotify?.Invoke(br));
                            break;
                        case MsgId.SiegeStateNotify:
                            var ssn = SiegeStateNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnSiegeStateNotify?.Invoke(ssn));
                            break;
                        case MsgId.ChatNotify:
                            var chat = ChatNotify.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnChatNotify?.Invoke(chat));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                foreach (var kv in _pending)
                    kv.Value.TrySetException(ex);
            }
        }

        private async Task<PacketFrame> SendRequestAsync(MsgId msgId, IMessage message, CancellationToken ct)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");
            uint seq = Interlocked.Increment(ref _seq);
            var tcs = new TaskCompletionSource<PacketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[seq] = tcs;
            using (ct.Register(() => { if (_pending.TryRemove(seq, out var r)) r.TrySetCanceled(ct); }))
            {
                await PacketCodec.WriteFrameAsync(_stream, msgId, seq, message, ct).ConfigureAwait(false);
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _tcp?.Dispose();
            _cts?.Dispose();
        }
    }

    /// <summary>Abstract input: PC keyboard or mobile virtual stick.</summary>
    public interface ILegendMoveInput
    {
        /// <summary>Normalized desired grid delta for this frame (e.g. -1..1).</summary>
        void GetMoveIntent(out int dx, out int dy);
    }

    public sealed class KeyboardWasdInput : ILegendMoveInput
    {
        public Func<bool>? W, A, S, D;
        public void GetMoveIntent(out int dx, out int dy)
        {
            dx = 0; dy = 0;
            if (A != null && A()) dx -= 1;
            if (D != null && D()) dx += 1;
            if (S != null && S()) dy -= 1;
            if (W != null && W()) dy += 1;
        }
    }

    public sealed class VirtualJoystickInput : ILegendMoveInput
    {
        public float AxisX, AxisY;
        public float DeadZone = 0.25f;
        public void GetMoveIntent(out int dx, out int dy)
        {
            dx = Math.Abs(AxisX) < DeadZone ? 0 : (AxisX > 0 ? 1 : -1);
            dy = Math.Abs(AxisY) < DeadZone ? 0 : (AxisY > 0 ? 1 : -1);
        }
    }
}
