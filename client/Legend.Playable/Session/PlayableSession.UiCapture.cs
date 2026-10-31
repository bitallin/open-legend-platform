using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.Playable.World;

namespace Legend.Playable.Session;

public sealed partial class PlayableSession
{
    /// <summary>Offline chrome seed for <c>--ui-capture</c>. No network.</summary>
    public void SeedUiCapture()
    {
        Fsm.BeginConnect(Host, Port);
        Fsm.MarkLoginReady();
        Fsm.MarkAuthenticated();
        Fsm.BeginEnterWorld("抽检武者");
        Fsm.MarkInWorld("novice_plain");
        World.SetMap("novice_plain");
        Level = 42;
        ClassId = "warrior";
        Mp = 200;
        MaxMp = 300;
        ExpToNext = 10000;
        GuildName = "石金先锋";
        GuildRank = "leader";
        PendingPartyId = "party-capture-01";
        PendingInviter = "铁血队长";
        World.Upsert("self-capture", "抽检武者", EntityKind.EntityPlayer, 20, 20, 0, 800, 1000, isSelf: true, inSafeZone: true);
        if (World.Self != null)
            World.Self.ClassId = "warrior";

        var guild = new GuildInfoResp
        {
            Success = true,
            GuildId = "guild-capture",
            GuildName = "石金先锋",
            LeaderCharacterId = "self-capture",
            Notice = "本周沙巴克战备 · 近满员请清理挂名"
        };
        for (int i = 0; i < 198; i++)
        {
            string rank = i == 0 ? "leader" : i < 3 ? "vice_leader" : i < 8 ? "warehouse_admin" : "member";
            guild.Members.Add(new GuildMemberInfo
            {
                CharacterId = i == 0 ? "self-capture" : $"m{i:D3}",
                Name = i == 0 ? "抽检武者" : $"成员{i:D3}",
                Rank = rank
            });
        }
        GuildInfo = guild;

        _mails.Clear();
        var attach = new MailAttachment { Gold = 50000 };
        attach.Items.Add(new MailAttachmentItem { ItemId = "item_hp_potion", Count = 10 });
        attach.Items.Add(new MailAttachmentItem { ItemId = "item_iron_sword", Count = 1 });
        var mail = new MailEntry
        {
            MailId = "mail-capture-1",
            FromName = "系统",
            Subject = "沙巴克战利补给",
            Body = "负重充足可一键提取附件。绑/非绑示意见格内 Caption。",
            CreatedAtUnixMs = DateTimeOffset.UtcNow.AddHours(-5).ToUnixTimeMilliseconds(),
            IsRead = false,
            Claimed = false,
            HasUnclaimedAttachment = true,
            Attachments = attach
        };
        _mails.Add(mail);
        SelectedMail = mail;
        _mailCapacity = 50;

        _market = Array.Empty<StallListingInfo>();

        Assist.Settings.Enabled = true;
        Assist.TryStart(new AssistWorldView
        {
            MapId = "novice_plain",
            SelfX = 20,
            SelfY = 20,
            Hp = 800,
            MaxHp = 1000,
            Mp = 200,
            MaxMp = 300,
            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, out _);
    }

    /// <summary>Clear pending party invite chrome for subsequent capture shots.</summary>
    public void ClearPendingPartyForCapture()
    {
        PendingPartyId = "";
        PendingInviter = "";
    }
}
