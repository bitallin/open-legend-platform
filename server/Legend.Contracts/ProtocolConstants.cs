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
    ServerNotice = 3001
}

public static class ProtocolConstants
{
    public const string CurrentVersion = "1.3.0";
    public const string MinCompatibleVersion = "1.3.0";
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
