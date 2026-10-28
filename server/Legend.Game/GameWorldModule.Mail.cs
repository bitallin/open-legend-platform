using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly SemaphoreSlim _mailGate = new(1, 1);

    private MailConfig MailCfg => _versionPack.Mail ?? new MailConfig();

    /// <summary>
    /// Send a system (or named) mail with optional gold/item attachments.
    /// Capacity: evict oldest claimed/empty first; if still full, refuse.
    /// Online recipients get MailNotify. Rule: unclaimed attachments forbid delete.
    /// </summary>
    internal async Task<(bool Ok, string? Error, MailRecord? Mail)> SendMailAsync(
        string ownerCharacterId,
        string ownerNameHint,
        string fromName,
        string subject,
        string body,
        int attachGold,
        IEnumerable<MailAttachmentItemRecord>? attachItems,
        CancellationToken ct)
    {
        int capacity = Math.Max(1, MailCfg.MaxMailsPerCharacter);
        await _mailGate.WaitAsync(ct);
        try
        {
            int count = await _storage.Mails.CountByOwnerAsync(ownerCharacterId, ct);
            if (count >= capacity)
            {
                await _storage.Mails.EvictOldestClaimedEmptyAsync(ownerCharacterId, capacity, ct);
                count = await _storage.Mails.CountByOwnerAsync(ownerCharacterId, ct);
                if (count >= capacity)
                    return (false, "邮箱已满", null);
            }

            var items = (attachItems ?? Array.Empty<MailAttachmentItemRecord>())
                .Where(i => !string.IsNullOrWhiteSpace(i.ItemId) && i.Count > 0)
                .ToList();
            var mail = new MailRecord
            {
                MailId = Guid.NewGuid().ToString("N"),
                OwnerCharacterId = ownerCharacterId,
                FromName = string.IsNullOrWhiteSpace(fromName) ? MailCfg.SystemSenderName : fromName.Trim(),
                Subject = subject ?? "",
                Body = body ?? "",
                AttachGold = Math.Max(0, attachGold),
                AttachItemsJson = MailItemsJson.Serialize(items),
                IsRead = false,
                Claimed = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            mail = await _storage.Mails.InsertAsync(mail, ct);

            var online = EnumerateOnlinePlayers().FirstOrDefault(
                p => string.Equals(p.CharacterId, ownerCharacterId, StringComparison.Ordinal));
            if (online != null)
            {
                await online.Session.SendAsync(MsgId.MailNotify, 0, new MailNotify { Mail = ToProtoMail(mail) }, ct);
            }

            await _eventBus.PublishAsync(new MailSentEvent(
                ownerCharacterId,
                ownerNameHint ?? online?.Name ?? "",
                mail.MailId,
                mail.FromName,
                mail.Subject,
                mail.AttachGold), ct);

            return (true, null, mail);
        }
        finally
        {
            _mailGate.Release();
        }
    }

    private async Task HandleMailListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.MailListResp, frame.Seq, new MailListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        var mails = await _storage.Mails.ListByOwnerAsync(player.CharacterId, ct);
        var resp = new MailListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            Capacity = Math.Max(1, MailCfg.MaxMailsPerCharacter)
        };
        foreach (var m in mails)
            resp.Mails.Add(ToProtoMail(m));
        await session.SendAsync(MsgId.MailListResp, frame.Seq, resp, ct);
    }

    private async Task HandleMailReadAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = MailReadReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.MailReadResp, frame.Seq, new MailReadResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string mailId = (req.MailId ?? "").Trim();
        var (ok, err) = await _storage.Mails.MarkReadAsync(mailId, player.CharacterId, ct);
        if (!ok)
        {
            await session.SendAsync(MsgId.MailReadResp, frame.Seq, new MailReadResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMailNotFound,
                Message = err ?? "邮件不存在"
            }, ct);
            return;
        }

        var mail = await _storage.Mails.GetByIdAsync(mailId, ct);
        await session.SendAsync(MsgId.MailReadResp, frame.Seq, new MailReadResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            Mail = mail is null ? null : ToProtoMail(mail)
        }, ct);
    }

    private async Task HandleMailClaimAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = MailClaimAttachmentReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, new MailClaimAttachmentResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string mailId = (req.MailId ?? "").Trim();
        await _mailGate.WaitAsync(ct);
        try
        {
            var peek = await _storage.Mails.GetByIdAsync(mailId, ct);
            if (peek is null || !string.Equals(peek.OwnerCharacterId, player.CharacterId, StringComparison.Ordinal))
            {
                await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, new MailClaimAttachmentResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrMailNotFound,
                    Message = "邮件不存在"
                }, ct);
                return;
            }
            if (peek.Claimed)
            {
                await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, new MailClaimAttachmentResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrMailAlreadyClaimed,
                    Message = "附件已领取",
                    Mail = ToProtoMail(peek),
                    Gold = player.Gold
                }, ct);
                return;
            }

            var items = MailItemsJson.Parse(peek.AttachItemsJson);
            int maxSlots = Math.Max(1, MailCfg.MaxInventorySlots);
            lock (_combatLock)
            {
                int needNew = 0;
                foreach (var it in items)
                {
                    bool exists = player.Inventory.Any(s =>
                        string.Equals(s.ItemId, it.ItemId, StringComparison.OrdinalIgnoreCase));
                    if (!exists) needNew++;
                }
                if (player.Inventory.Count + needNew > maxSlots)
                {
                    // fail without claiming
                    goto BagFull;
                }
            }

            var (ok, err, snapshot) = await _storage.Mails.TryClaimAsync(mailId, player.CharacterId, ct);
            if (!ok || snapshot is null)
            {
                await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, new MailClaimAttachmentResp
                {
                    Success = false,
                    ErrorCode = string.Equals(err, "附件已领取", StringComparison.Ordinal)
                        ? ProtocolErrorCode.ErrMailAlreadyClaimed
                        : ProtocolErrorCode.ErrMailNotFound,
                    Message = err ?? "领取失败"
                }, ct);
                return;
            }

            int claimedGold = Math.Max(0, snapshot.AttachGold);
            lock (_combatLock)
            {
                player.Gold += claimedGold;
                foreach (var it in MailItemsJson.Parse(snapshot.AttachItemsJson))
                    InventoryHelper.AddItem(player.Inventory, it.ItemId, it.Count);
            }
            await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
            await _storage.Characters.SaveInventoryAsync(
                player.CharacterId, InventoryHelper.Serialize(player.Inventory), ct);

            var after = await _storage.Mails.GetByIdAsync(mailId, ct) ?? snapshot;
            after.Claimed = true;
            after.IsRead = true;

            var resp = new MailClaimAttachmentResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "领取成功",
                Mail = ToProtoMail(after),
                Gold = player.Gold
            };
            foreach (var inv in InventoryHelper.ToProto(player.Inventory))
                resp.Inventory.Add(inv);
            await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, resp, ct);

            await session.SendAsync(MsgId.InventoryNotify, 0, new InventoryNotify
            {
                CharacterId = player.CharacterId,
                Reason = "mail_claim",
                Gold = player.Gold
            }.Also(n => { foreach (var i in InventoryHelper.ToProto(player.Inventory)) n.Items.Add(i); }), ct);

            await _eventBus.PublishAsync(new MailClaimedEvent(
                session, player.Name, player.CharacterId, mailId, claimedGold), ct);
            return;

        BagFull:
            await session.SendAsync(MsgId.MailClaimAttachmentResp, frame.Seq, new MailClaimAttachmentResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMailBagFull,
                Message = "背包已满，无法领取附件",
                Mail = ToProtoMail(peek),
                Gold = player.Gold
            }, ct);
        }
        finally
        {
            _mailGate.Release();
        }
    }

    private async Task HandleMailDeleteAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = MailDeleteReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.MailDeleteResp, frame.Seq, new MailDeleteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string mailId = (req.MailId ?? "").Trim();
        var (ok, err) = await _storage.Mails.DeleteAsync(mailId, player.CharacterId, allowUnclaimedAttachment: false, ct);
        if (!ok)
        {
            bool unclaimed = err != null && err.Contains("未领取", StringComparison.Ordinal);
            await session.SendAsync(MsgId.MailDeleteResp, frame.Seq, new MailDeleteResp
            {
                Success = false,
                ErrorCode = unclaimed ? ProtocolErrorCode.ErrMailAttachUnclaimed : ProtocolErrorCode.ErrMailNotFound,
                Message = err ?? "删除失败"
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.MailDeleteResp, frame.Seq, new MailDeleteResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已删除"
        }, ct);
    }

    private async Task HandleMailSendAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = MailSendReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.MailSendResp, frame.Seq, new MailSendResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string toName = (req.ToCharacterName ?? "").Trim();
        if (string.IsNullOrEmpty(toName))
        {
            await session.SendAsync(MsgId.MailSendResp, frame.Seq, new MailSendResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrMailDenied,
                Message = "收件人不能为空"
            }, ct);
            return;
        }

        var target = await _storage.Characters.GetByNameAsync(toName, ct);
        if (target is null)
        {
            await session.SendAsync(MsgId.MailSendResp, frame.Seq, new MailSendResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "收件人不存在"
            }, ct);
            return;
        }

        var items = new List<MailAttachmentItemRecord>();
        if (!string.IsNullOrWhiteSpace(req.AttachItemId) && req.AttachItemCount > 0)
            items.Add(new MailAttachmentItemRecord { ItemId = req.AttachItemId.Trim(), Count = req.AttachItemCount });

        var (ok, err, mail) = await SendMailAsync(
            target.CharacterId,
            target.Name,
            player.Name,
            req.Subject ?? "",
            req.Body ?? "",
            Math.Max(0, req.AttachGold),
            items,
            ct);

        await session.SendAsync(MsgId.MailSendResp, frame.Seq, new MailSendResp
        {
            Success = ok,
            ErrorCode = ok ? ProtocolErrorCode.ErrNone : ProtocolErrorCode.ErrMailFull,
            Message = ok ? "发送成功" : (err ?? "发送失败"),
            MailId = mail?.MailId ?? ""
        }, ct);
    }

    private static MailEntry ToProtoMail(MailRecord m)
    {
        var entry = new MailEntry
        {
            MailId = m.MailId,
            FromName = m.FromName ?? "",
            Subject = m.Subject ?? "",
            Body = m.Body ?? "",
            CreatedAtUnixMs = m.CreatedAt.ToUnixTimeMilliseconds(),
            IsRead = m.IsRead,
            Claimed = m.Claimed,
            HasUnclaimedAttachment = m.HasUnclaimedAttachment,
            Attachments = new MailAttachment { Gold = Math.Max(0, m.AttachGold) }
        };
        foreach (var it in MailItemsJson.Parse(m.AttachItemsJson))
            entry.Attachments.Items.Add(new MailAttachmentItem { ItemId = it.ItemId, Count = it.Count });
        return entry;
    }
}
