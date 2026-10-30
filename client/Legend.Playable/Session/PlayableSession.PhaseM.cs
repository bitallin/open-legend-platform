using Legend.Contracts.Proto;

namespace Legend.Playable.Session;

public sealed partial class PlayableSession
{
    public string AttackModeWire { get; private set; } = "peace";
    public int PkKills { get; private set; }
    public string NameColor { get; private set; } = "white";
    public string TradeId { get; private set; } = "";
    public string TradePeer { get; private set; } = "";
    public TradeStateNotify? TradeState { get; private set; }
    public string SiegeNote { get; private set; } = "";

    public event Action? TradeChanged;
    public event Action<string>? SiegeChanged;

    public async Task<string?> SetAttackModeAsync(string mode)
    {
        var resp = await _net.SetAttackModeAsync(mode);
        if (resp.Success)
        {
            AttackModeWire = resp.Mode;
            PkKills = resp.PkKills;
            NameColor = resp.NameColor;
            if (World.Self != null)
                World.Self.NameColor = resp.NameColor;
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "无法切换攻击模式" : resp.Message;
    }

    public async Task<string?> IdentifyAsync(string itemId)
    {
        if (string.IsNullOrEmpty(_npcEntityId))
            return "先靠近商人";
        var resp = await _net.IdentifyAsync(_npcEntityId, itemId);
        if (resp.Success)
        {
            SyncInventory();
            Atk = _net.EffectiveAtk;
            Def = _net.EffectiveDef;
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "鉴定失败" : resp.Message;
    }

    public async Task<string?> TradeStartAsync(string name)
    {
        var resp = await _net.TradeStartAsync(name);
        if (resp.Success)
        {
            TradeId = resp.TradeId;
            TradePeer = name;
            TradeChanged?.Invoke();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "无法交易" : resp.Message;
    }

    public async Task<string?> TradeRespondAsync(bool accept)
    {
        if (TradeId.Length == 0)
            return "没有交易";
        var resp = await _net.TradeRespondAsync(TradeId, accept);
        if (!accept)
            TradeId = "";
        TradeChanged?.Invoke();
        return resp.Success ? null : resp.Message;
    }

    public async Task<string?> TradePutAsync(string itemId, int count, int gold)
    {
        var resp = await _net.TradePutAsync(TradeId, itemId, count, gold);
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "放入失败" : resp.Message);
    }

    public async Task<string?> TradeLockAsync(bool locked)
    {
        var resp = await _net.TradeLockAsync(TradeId, locked);
        return resp.Success ? null : resp.Message;
    }

    public async Task TradeCancelAsync()
    {
        if (TradeId.Length == 0)
            return;
        await _net.TradeCancelAsync(TradeId);
        TradeId = "";
        TradeState = null;
        TradeChanged?.Invoke();
    }

    public async Task<string?> SiegeStatusAsync()
    {
        var resp = await _net.SiegeStatusAsync();
        SiegeNote = string.IsNullOrWhiteSpace(resp.ScheduleNote) ? resp.Message : resp.ScheduleNote;
        SiegeChanged?.Invoke(resp.IsActive
            ? $"攻城中  城主 {resp.OwnerGuildName}"
            : $"未开战  城主 {resp.OwnerGuildName}  宣战 {resp.DeclaredGuildNames.Count}");
        return SiegeNote;
    }

    public async Task<string?> SiegeDeclareAsync()
    {
        var resp = await _net.SiegeDeclareAsync();
        if (resp.Success)
            SyncInventory();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "宣战失败" : resp.Message);
    }

    public async Task<string?> EnhanceAsync(string slot)
    {
        var frame = await _net.EnhanceAsync(slot);
        if (frame.Success)
        {
            SyncInventory();
            Atk = frame.EffectiveAtk;
            Def = frame.EffectiveDef;
            return frame.EnhanceSucceeded ? null : "强化失败，金币已消耗";
        }
        return string.IsNullOrWhiteSpace(frame.Message) ? "无法强化" : frame.Message;
    }

    public async Task<string?> GuildDepositAsync(string itemId)
    {
        var resp = await _net.GuildWarehouseDepositAsync(itemId, 1);
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "存入失败" : resp.Message;
    }

    public async Task<string?> TravelAsync(string mapId)
    {
        var resp = await _net.WorldTravelAsync(mapId);
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法传送" : resp.Message);
    }

    public async Task<string?> MineAsync()
    {
        var resp = await _net.MineAsync();
        if (resp.Success)
        {
            SyncInventory();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "挖矿失败" : resp.Message;
    }

    public async Task<string?> SiegeCaptureAsync()
    {
        var resp = await _net.SiegeCaptureAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "占领失败" : resp.Message);
    }
}
