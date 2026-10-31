using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Playable.Net;

public sealed partial class PlayableNetClient
{
    public event Action<WorldBossHpNotify>? OnBossHp;
    public event Action<WorldBossSpawnNotify>? OnBossSpawn;
    public event Action<AppearanceSyncNotify>? OnAppearance;

    public async Task<LeaveWorldResp> LeaveWorldAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.LeaveWorldReq, new LeaveWorldReq(), ct).ConfigureAwait(false);
        return LeaveWorldResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<WorldBossStatusResp> WorldBossStatusAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.WorldBossStatusReq, new WorldBossStatusReq(), ct).ConfigureAwait(false);
        return WorldBossStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<GuildSetRankResp> GuildSetRankAsync(string characterId, string rank, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.GuildSetRankReq, new GuildSetRankReq
        {
            TargetCharacterId = characterId,
            Rank = rank
        }, ct).ConfigureAwait(false);
        return GuildSetRankResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FriendListResp> FriendListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendListReq, new FriendListReq(), ct).ConfigureAwait(false);
        return FriendListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FriendAddResp> FriendAddAsync(string name, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendAddReq, new FriendAddReq { FriendName = name }, ct).ConfigureAwait(false);
        return FriendAddResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FriendRemoveResp> FriendRemoveAsync(string characterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendRemoveReq, new FriendRemoveReq
        {
            FriendCharacterId = characterId
        }, ct).ConfigureAwait(false);
        return FriendRemoveResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<StallListResp> StallListAsync(string itemId, int count, int unitPrice, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallListReq, new StallListReq
        {
            ItemId = itemId,
            Count = count,
            UnitPriceGold = unitPrice
        }, ct).ConfigureAwait(false);
        var resp = StallListResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            ReplaceInventory(resp.Inventory);
        return resp;
    }

    public async Task<StallBrowseResp> StallBrowseAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallBrowseReq, new StallBrowseReq(), ct).ConfigureAwait(false);
        return StallBrowseResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<StallBuyResp> StallBuyAsync(string listingId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallBuyReq, new StallBuyReq { ListingId = listingId }, ct).ConfigureAwait(false);
        var resp = StallBuyResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            Gold = resp.Gold;
            ReplaceInventory(resp.Inventory);
        }
        return resp;
    }

    public async Task<StallCancelResp> StallCancelAsync(string listingId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallCancelReq, new StallCancelReq { ListingId = listingId }, ct).ConfigureAwait(false);
        var resp = StallCancelResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            ReplaceInventory(resp.Inventory);
        return resp;
    }

    public async Task<MountStatusResp> MountStatusAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MountStatusReq, new MountStatusReq(), ct).ConfigureAwait(false);
        return MountStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MountSummonResp> MountSummonAsync(string mountId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MountSummonReq, new MountSummonReq { MountId = mountId }, ct).ConfigureAwait(false);
        return MountSummonResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MountDismissResp> MountDismissAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MountDismissReq, new MountDismissReq(), ct).ConfigureAwait(false);
        return MountDismissResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FashionStatusResp> FashionStatusAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FashionStatusReq, new FashionStatusReq(), ct).ConfigureAwait(false);
        return FashionStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FashionEquipResp> FashionEquipAsync(string fashionId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FashionEquipReq, new FashionEquipReq { FashionId = fashionId }, ct).ConfigureAwait(false);
        var resp = FashionEquipResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            EffectiveAtk = resp.EffectiveAtk;
        return resp;
    }

    public async Task<PetStatusResp> PetStatusAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PetStatusReq, new PetStatusReq(), ct).ConfigureAwait(false);
        return PetStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PetSummonResp> PetSummonAsync(string petId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PetSummonReq, new PetSummonReq { PetId = petId }, ct).ConfigureAwait(false);
        return PetSummonResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PetDismissResp> PetDismissAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PetDismissReq, new PetDismissReq(), ct).ConfigureAwait(false);
        return PetDismissResp.Parser.ParseFrom(frame.Payload);
    }


    public async Task<MailListResp> MailListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailListReq, new MailListReq(), ct).ConfigureAwait(false);
        return MailListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailReadResp> MailReadAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailReadReq, new MailReadReq { MailId = mailId ?? "" }, ct).ConfigureAwait(false);
        return MailReadResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailClaimAttachmentResp> MailClaimAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailClaimAttachmentReq, new MailClaimAttachmentReq { MailId = mailId ?? "" }, ct).ConfigureAwait(false);
        var resp = MailClaimAttachmentResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            Gold = resp.Gold;
            ReplaceInventory(resp.Inventory);
        }
        return resp;
    }

    public async Task<MailDeleteResp> MailDeleteAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailDeleteReq, new MailDeleteReq { MailId = mailId ?? "" }, ct).ConfigureAwait(false);
        return MailDeleteResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailSendResp> MailSendAsync(string toName, string subject, string body, int attachGold = 0, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailSendReq, new MailSendReq
        {
            ToCharacterName = toName ?? "",
            Subject = subject ?? "",
            Body = body ?? "",
            AttachGold = attachGold
        }, ct).ConfigureAwait(false);
        return MailSendResp.Parser.ParseFrom(frame.Payload);
    }

}
