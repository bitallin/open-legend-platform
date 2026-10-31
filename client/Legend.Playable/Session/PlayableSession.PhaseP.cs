using System.Linq;
using Legend.ClientCore;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Playable.Session;

public sealed partial class PlayableSession
{
    private WorldBossInfo[] _bosses = Array.Empty<WorldBossInfo>();
    private readonly List<FriendInfo> _friends = new();
    private StallListingInfo[] _market = Array.Empty<StallListingInfo>();
    private readonly List<MailEntry> _mails = new();
    private int _mailCapacity;

    public IReadOnlyList<WorldBossInfo> Bosses => _bosses;
    public IReadOnlyList<FriendInfo> Friends => _friends;
    public IReadOnlyList<StallListingInfo> Market => _market;
    public IReadOnlyList<MailEntry> Mails => _mails;
    public int MailCapacity => _mailCapacity;
    public MailEntry? SelectedMail { get; private set; }
    public GuildInfoResp? GuildInfo { get; private set; }
    public MountStatusResp? Mounts { get; private set; }
    public FashionStatusResp? Fashions { get; private set; }
    public PetStatusResp? Pets { get; private set; }
    public int SocialRevision { get; private set; }
    public string CharacterId => _net.CharacterId ?? "";

    public async Task LeaveToCharSelectAsync()
    {
        if (Fsm.Phase != SessionPhase.InWorld)
            return;
        var resp = await _net.LeaveWorldAsync();
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "无法离开世界" : resp.Message);
        World.ClearActors(keepSelf: false);
        LastTalk = null;
        _npcEntityId = "";
        Fsm.ReturnToCharSelect("选择角色");
        await RefreshCharactersAsync();
    }

    public async Task RefreshBossesAsync()
    {
        var resp = await _net.WorldBossStatusAsync();
        if (resp.Success)
            _bosses = resp.Bosses.ToArray();
    }

    public void ApplyBossHp(WorldBossHpNotify hp)
    {
        var next = _bosses.ToArray();
        bool found = false;
        for (int i = 0; i < next.Length; i++)
        {
            if (!string.Equals(next[i].BossId, hp.BossId, StringComparison.OrdinalIgnoreCase))
                continue;
            next[i].Hp = hp.Hp;
            next[i].MaxHp = hp.MaxHp;
            next[i].Alive = hp.Hp > 0;
            next[i].HateTop.Clear();
            next[i].HateTop.AddRange(hp.HateTop);
            found = true;
        }
        if (!found)
        {
            next = next.Append(new WorldBossInfo
            {
                BossId = hp.BossId,
                DisplayName = hp.BossId,
                EntityId = hp.EntityId,
                Hp = hp.Hp,
                MaxHp = hp.MaxHp,
                Alive = hp.Hp > 0
            }).ToArray();
            next[^1].HateTop.AddRange(hp.HateTop);
        }
        _bosses = next;
    }

    public void ApplyBossSpawn(WorldBossSpawnNotify spawn)
    {
        var list = _bosses.Where(b => !string.Equals(b.BossId, spawn.BossId, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(new WorldBossInfo
        {
            BossId = spawn.BossId,
            DisplayName = spawn.DisplayName,
            MapId = spawn.MapId,
            X = spawn.X,
            Y = spawn.Y,
            EntityId = spawn.EntityId,
            Hp = spawn.MaxHp,
            MaxHp = spawn.MaxHp,
            Alive = true
        });
        _bosses = list.ToArray();
    }

    public async Task<string?> SetGuildRankAsync(string characterId, string rank)
    {
        var resp = await _net.GuildSetRankAsync(characterId, rank);
        SocialRevision++;
        if (resp.Success)
        {
            await RefreshGuildAsync();
            return null;
        }
        return string.IsNullOrWhiteSpace(resp.Message) ? "任命失败" : resp.Message;
    }

    public async Task RefreshFriendsAsync()
    {
        var resp = await _net.FriendListAsync();
        _friends.Clear();
        if (resp.Success)
            _friends.AddRange(resp.Friends);
        SocialRevision++;
    }

    public async Task<string?> AddFriendAsync(string name)
    {
        var resp = await _net.FriendAddAsync(name.Trim());
        await RefreshFriendsAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "添加失败" : resp.Message);
    }

    public async Task<string?> RemoveFriendAsync(string characterId)
    {
        var resp = await _net.FriendRemoveAsync(characterId);
        await RefreshFriendsAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "删除失败" : resp.Message);
    }

    public async Task RefreshMarketAsync()
    {
        var resp = await _net.StallBrowseAsync();
        _market = resp.Success ? resp.Listings.ToArray() : Array.Empty<StallListingInfo>();
        SocialRevision++;
    }

    public async Task<string?> StallListAsync(string itemId, int count, int price)
    {
        var resp = await _net.StallListAsync(itemId, count, price);
        if (resp.Success)
            SyncInventory();
        await RefreshMarketAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "上架失败" : resp.Message);
    }

    public async Task<string?> StallBuyAsync(string listingId)
    {
        var resp = await _net.StallBuyAsync(listingId);
        if (resp.Success)
            SyncInventory();
        await RefreshMarketAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "购买失败" : resp.Message);
    }

    public async Task<string?> StallCancelAsync(string listingId)
    {
        var resp = await _net.StallCancelAsync(listingId);
        if (resp.Success)
            SyncInventory();
        await RefreshMarketAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "下架失败" : resp.Message);
    }

    public async Task RefreshLooksAsync()
    {
        Mounts = await _net.MountStatusAsync();
        Fashions = await _net.FashionStatusAsync();
        Pets = await _net.PetStatusAsync();
        SocialRevision++;
    }

    public async Task<string?> SummonMountAsync(string mountId)
    {
        var resp = await _net.MountSummonAsync(mountId);
        if (resp.Success && World.Self != null)
        {
            World.Self.MountId = resp.ActiveMountId ?? mountId;
            World.Self.MoveIntervalMs = resp.MoveIntervalMs;
        }
        await RefreshLooksAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法上马" : resp.Message);
    }

    public async Task<string?> DismissMountAsync()
    {
        var resp = await _net.MountDismissAsync();
        if (resp.Success && World.Self != null)
        {
            World.Self.MountId = "";
            World.Self.MoveIntervalMs = resp.MoveIntervalMs;
        }
        await RefreshLooksAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法下马" : resp.Message);
    }

    public async Task<string?> EquipFashionAsync(string fashionId)
    {
        var resp = await _net.FashionEquipAsync(fashionId);
        if (resp.Success)
        {
            Atk = resp.EffectiveAtk;
            if (World.Self != null)
                World.Self.FashionId = resp.EquippedFashionId ?? "";
        }
        await RefreshLooksAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法换装" : resp.Message);
    }

    public async Task<string?> SummonPetAsync(string petId)
    {
        var resp = await _net.PetSummonAsync(petId);
        await RefreshLooksAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法召唤" : resp.Message);
    }

    public async Task<string?> DismissPetAsync()
    {
        var resp = await _net.PetDismissAsync();
        await RefreshLooksAsync();
        return resp.Success ? null : (string.IsNullOrWhiteSpace(resp.Message) ? "无法收回" : resp.Message);
    }

    private void ApplyLook(AppearanceSyncNotify look)
    {
        var actor = World.Get(look.EntityId);
        if (actor == null)
            return;
        actor.MountId = look.MountId ?? "";
        actor.FashionId = look.FashionId ?? "";
        if (look.MoveIntervalMs > 0)
            actor.MoveIntervalMs = look.MoveIntervalMs;
    }

    private void TouchTitle()
    {
        if (World.Self != null)
            World.Self.Title = PhasePRules.Title(Level, GuildRank);
    }


    public async Task RefreshMailsAsync()
    {
        var resp = await _net.MailListAsync();
        _mails.Clear();
        if (resp.Success)
        {
            _mails.AddRange(resp.Mails);
            _mailCapacity = resp.Capacity;
            if (SelectedMail is not null)
            {
                var keep = _mails.FirstOrDefault(m => m.MailId == SelectedMail.MailId);
                SelectedMail = keep;
            }
        }
        SocialRevision++;
    }

    public async Task ReadMailAsync(string mailId)
    {
        var resp = await _net.MailReadAsync(mailId);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "读信失败" : resp.Message);
        SelectedMail = resp.Mail;
        await RefreshMailsAsync();
    }

    public async Task ClaimMailAsync(string mailId)
    {
        var resp = await _net.MailClaimAsync(mailId);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "领取失败" : resp.Message);
        SelectedMail = resp.Mail;
        await RefreshMailsAsync();
    }

    public async Task DeleteMailAsync(string mailId)
    {
        var resp = await _net.MailDeleteAsync(mailId);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "删除失败" : resp.Message);
        if (SelectedMail?.MailId == mailId)
            SelectedMail = null;
        await RefreshMailsAsync();
    }

    public async Task SendMailAsync(string toName, string subject, string body, int attachGold = 0)
    {
        var resp = await _net.MailSendAsync(toName, subject, body, attachGold);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "发信失败" : resp.Message);
        await RefreshMailsAsync();
    }

}
