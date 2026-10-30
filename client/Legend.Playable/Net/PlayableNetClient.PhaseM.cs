using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Playable.Net;

public sealed partial class PlayableNetClient
{
    public async Task<SetAttackModeResp> SetAttackModeAsync(string mode, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SetAttackModeReq, new SetAttackModeReq { Mode = mode }, ct).ConfigureAwait(false);
        return SetAttackModeResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<IdentifyResp> IdentifyAsync(string npcId, string itemId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.IdentifyReq, new IdentifyReq
        {
            NpcEntityId = npcId,
            ItemId = itemId
        }, ct).ConfigureAwait(false);
        var resp = IdentifyResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            Gold = resp.Gold;
            Replace(Equipment, resp.Equipment);
        }
        return resp;
    }

    public async Task<TradeStartResp> TradeStartAsync(string name, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TradeStartReq, new TradeStartReq { TargetName = name }, ct).ConfigureAwait(false);
        return TradeStartResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<TradeRespondResp> TradeRespondAsync(string tradeId, bool accept, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TradeRespondReq, new TradeRespondReq { TradeId = tradeId, Accept = accept }, ct).ConfigureAwait(false);
        return TradeRespondResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<TradePutResp> TradePutAsync(string tradeId, string itemId, int count, int gold, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TradePutReq, new TradePutReq
        {
            TradeId = tradeId,
            ItemId = itemId,
            Count = count,
            Gold = gold
        }, ct).ConfigureAwait(false);
        return TradePutResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<TradeLockResp> TradeLockAsync(string tradeId, bool locked, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TradeLockReq, new TradeLockReq { TradeId = tradeId, Locked = locked }, ct).ConfigureAwait(false);
        return TradeLockResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<TradeCancelResp> TradeCancelAsync(string tradeId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TradeCancelReq, new TradeCancelReq { TradeId = tradeId }, ct).ConfigureAwait(false);
        return TradeCancelResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SiegeStatusResp> SiegeStatusAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeStatusReq, new SiegeStatusReq(), ct).ConfigureAwait(false);
        return SiegeStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SiegeDeclareResp> SiegeDeclareAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeDeclareReq, new SiegeDeclareReq(), ct).ConfigureAwait(false);
        var resp = SiegeDeclareResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.NewGold;
        return resp;
    }

    public async Task<SiegeCaptureResp> SiegeCaptureAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeCaptureReq, new SiegeCaptureReq(), ct).ConfigureAwait(false);
        return SiegeCaptureResp.Parser.ParseFrom(frame.Payload);
    }
}
