using System.Buffers.Binary;
using Google.Protobuf;

namespace Legend.Contracts;

public enum MsgId : ushort
{
    HandshakeReq = 1001,
    HandshakeResp = 1002,
    LoginReq = 1003,
    LoginResp = 1004,
    RealmListReq = 1005,
    RealmListResp = 1006,
    PingReq = 1007,
    PongResp = 1008,
    RegisterReq = 1009,
    RegisterResp = 1010,
    CharListReq = 1101,
    CharListResp = 1102,
    CreateCharReq = 1103,
    CreateCharResp = 1104,
    DeleteCharReq = 1105,
    DeleteCharResp = 1106,
    SelectCharReq = 1107,
    SelectCharResp = 1108,
    EnterWorldReq = 2001,
    EnterWorldResp = 2002,
    MoveReq = 2003,
    MoveSync = 2004,
    EntityEnterAoi = 2005,
    EntityLeaveAoi = 2006,
    MapChangeNotify = 2007,
    AttackReq = 2101,
    AttackResp = 2102,
    CombatSync = 2103,
    EntitySpawn = 2104,
    EntityDespawn = 2105,
    LootDropNotify = 2106,
    PickupReq = 2107,
    PickupResp = 2108,
    InventoryNotify = 2109,
    TalkReq = 2201,
    TalkResp = 2202,
    ShopBuyReq = 2203,
    ShopBuyResp = 2204,
    QuestAcceptReq = 2205,
    QuestAcceptResp = 2206,
    QuestTurnInReq = 2207,
    QuestTurnInResp = 2208,
    QuestProgressNotify = 2209,
    EquipReq = 2301,
    EquipResp = 2302,
    UnequipReq = 2303,
    UnequipResp = 2304,
    EnhanceEquipReq = 2305,
    EnhanceEquipResp = 2306,
    UseItemReq = 2401,
    UseItemResp = 2402,
    ExpGainNotify = 2403,
    LevelUpNotify = 2404,
    GuildCreateReq = 2501,
    GuildCreateResp = 2502,
    GuildJoinReq = 2503,
    GuildJoinResp = 2504,
    GuildLeaveReq = 2505,
    GuildLeaveResp = 2506,
    GuildInfoReq = 2507,
    GuildInfoResp = 2508,
    ChatReq = 2509,
    ChatNotify = 2510,
    StallListReq = 2601,
    StallListResp = 2602,
    StallBrowseReq = 2603,
    StallBrowseResp = 2604,
    StallBuyReq = 2605,
    StallBuyResp = 2606,
    StallCancelReq = 2607,
    StallCancelResp = 2608,
    StallSoldNotify = 2609,
    PartyInviteReq = 2701,
    PartyInviteResp = 2702,
    PartyAcceptReq = 2703,
    PartyAcceptResp = 2704,
    PartyLeaveReq = 2705,
    PartyLeaveResp = 2706,
    PartyKickReq = 2707,
    PartyKickResp = 2708,
    PartyInfoNotify = 2709,
    PartyInviteNotify = 2710,
    FriendAddReq = 2711,
    FriendAddResp = 2712,
    FriendRemoveReq = 2713,
    FriendRemoveResp = 2714,
    FriendListReq = 2715,
    FriendListResp = 2716,
    LearnSkillReq = 2801,
    LearnSkillResp = 2802,
    SkillListReq = 2803,
    SkillListResp = 2804,
    SkillListNotify = 2805,
    SkillLevelUpNotify = 2806,
    BuffApplyNotify = 2807,
    BuffRemoveNotify = 2808,
    SiegeDeclareReq = 2901,
    SiegeDeclareResp = 2902,
    SiegeStatusReq = 2903,
    SiegeStatusResp = 2904,
    SiegeStateNotify = 2905,
    SiegeControlReq = 2906,
    SiegeControlResp = 2907,
    SiegeCaptureReq = 2908,
    SiegeCaptureResp = 2909,
    ServerNotice = 3001,
    MailListReq = 3101,
    MailListResp = 3102,
    MailReadReq = 3103,
    MailReadResp = 3104,
    MailClaimAttachmentReq = 3105,
    MailClaimAttachmentResp = 3106,
    MailDeleteReq = 3107,
    MailDeleteResp = 3108,
    MailNotify = 3109,
    MailSendReq = 3110,
    MailSendResp = 3111,
    DungeonEnterReq = 3201,
    DungeonEnterResp = 3202,
    DungeonProgressNotify = 3203,
    DungeonCompleteNotify = 3204,
    DungeonLeaveReq = 3205,
    DungeonLeaveResp = 3206,
    DungeonStatusReq = 3207,
    DungeonStatusResp = 3208,
    DungeonPartyEnterNotify = 3209
}

public static class ProtocolConstants
{
    public const string CurrentVersion = "1.13.0";
    public const string MinCompatibleVersion = "1.13.0";
    public const int HeaderSize = 10;
    public const int MaxPayloadSize = 1024 * 1024;

    public static bool IsVersionCompatible(string clientVersion)
    {
        if (string.IsNullOrWhiteSpace(clientVersion))
            return false;

        var parts = clientVersion.Trim().Split('.');
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], out int clientMajor) ||
            !int.TryParse(parts[1], out int clientMinor))
            return false;

        var serverParts = CurrentVersion.Split('.');
        int serverMajor = int.Parse(serverParts[0]);
        int minMinor = int.Parse(MinCompatibleVersion.Split('.')[1]);

        return clientMajor == serverMajor && clientMinor >= minMinor;
    }
}

public readonly record struct PacketFrame(MsgId MsgId, uint Seq, byte[] Payload);

public static class PacketCodec
{
    public static byte[] Encode(MsgId msgId, uint seq, IMessage message)
        => EncodeRaw(msgId, seq, message.ToByteArray());

    public static byte[] EncodeRaw(MsgId msgId, uint seq, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ProtocolConstants.MaxPayloadSize)
            throw new InvalidOperationException($"Payload size {payload.Length} exceeds max {ProtocolConstants.MaxPayloadSize}");

        byte[] buffer = new byte[ProtocolConstants.HeaderSize + payload.Length];
        Span<byte> span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), (ushort)msgId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(6, 4), seq);
        payload.CopyTo(span.Slice(ProtocolConstants.HeaderSize));
        return buffer;
    }

    public static async Task WriteFrameAsync(Stream stream, MsgId msgId, uint seq, IMessage message, CancellationToken ct = default)
    {
        byte[] packet = Encode(msgId, seq, message);
        await stream.WriteAsync(packet, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<PacketFrame?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[ProtocolConstants.HeaderSize];
        int read = await ReadExactAsync(stream, header, ct);
        if (read == 0)
            return null;
        if (read < ProtocolConstants.HeaderSize)
            throw new EndOfStreamException("Incomplete packet header received.");

        uint payloadLen = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        ushort rawMsgId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(6, 4));

        if (payloadLen > ProtocolConstants.MaxPayloadSize)
            throw new InvalidDataException($"Invalid payload length: {payloadLen}");

        byte[] payload = new byte[payloadLen];
        if (payloadLen > 0)
        {
            int payloadRead = await ReadExactAsync(stream, payload, ct);
            if (payloadRead < payloadLen)
                throw new EndOfStreamException("Incomplete packet payload received.");
        }

        return new PacketFrame((MsgId)rawMsgId, seq, payload);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (n == 0)
                return totalRead;
            totalRead += n;
        }
        return totalRead;
    }
}
