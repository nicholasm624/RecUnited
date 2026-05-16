using System.Buffers.Binary;
using System.Text;

namespace RecRoomServer.Photon;

/// <summary>
/// Photon 4 binary protocol constants and serialization helpers.
/// The game uses Photon PUN v1 (May 2020 era) over UDP.
/// </summary>
public static class PhotonProtocol
{
    // ── Packet header layout (12 bytes, big-endian) ─────────────────────────
    public const int PacketHeaderSize  = 12;
    public const int CommandHeaderSize = 12;

    // ── Command type codes ───────────────────────────────────────────────────
    public const byte CmdAck           = 1;
    public const byte CmdConnect       = 2;
    public const byte CmdVerifyConnect = 3;
    public const byte CmdDisconnect    = 4;
    public const byte CmdPing          = 5;
    public const byte CmdSendReliable  = 6;
    public const byte CmdSendUnreliable= 7;

    // ── Message type codes (first byte inside SendReliable payload) ──────────
    public const byte MsgOperationRequest  = 0x02;
    public const byte MsgOperationResponse = 0x03;
    public const byte MsgEvent             = 0x04;
    public const byte MsgInternalOperationRequest  = 0xF0;
    public const byte MsgInternalOperationResponse = 0xF1;
    public const byte MsgInitRequest  = 0xF3;

    // ── Operation codes ──────────────────────────────────────────────────────
    public const byte OpAuthenticate = 230;
    public const byte OpGetRegions   = 220;
    public const byte OpJoinLobby    = 229;
    public const byte OpCreateRoom   = 227;
    public const byte OpJoinRoom     = 226;
    public const byte OpJoinRandom   = 225;
    public const byte OpLeave        = 254;
    public const byte OpRaiseEvent   = 253;
    public const byte OpSetProperties= 252;
    public const byte OpGetProperties= 251;

    // ── Photon GpType codes ──────────────────────────────────────────────────
    public const byte TypeNull       = 42;   // '*'
    public const byte TypeBoolean    = 111;  // 'o'
    public const byte TypeByte       = 98;   // 'b'
    public const byte TypeShort      = 107;  // 'k'
    public const byte TypeInt        = 105;  // 'i'
    public const byte TypeLong       = 108;  // 'l'
    public const byte TypeFloat      = 102;  // 'f'
    public const byte TypeDouble     = 100;  // 'd'
    public const byte TypeString     = 115;  // 's'
    public const byte TypeByteArray  = 120;  // 'x'
    public const byte TypeStringArray= 97;   // 'a'
    public const byte TypeIntArray   = 110;  // 'n'
    public const byte TypeHashtable  = 104;  // 'h'
    public const byte TypeArray      = 121;  // 'y'
    public const byte TypeObjectArray= 122;  // 'z'

    // ── Return codes ─────────────────────────────────────────────────────────
    public const short RcOk          = 0;
    public const short RcNotFound    = -3;

    // ════════════════════════════════════════════════════════════════════════
    // Packet / Command builders
    // ════════════════════════════════════════════════════════════════════════

    public static byte[] BuildPacket(ushort peerId, byte[] commandData)
    {
        uint ts = (uint)(Environment.TickCount & 0x7FFFFFFF);
        var buf = new byte[PacketHeaderSize + commandData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), peerId);
        buf[2] = 0;                    // flags
        buf[3] = 1;                    // command count
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), ts);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), 0); // challenge
        commandData.CopyTo(buf, PacketHeaderSize);
        return buf;
    }

    public static byte[] BuildCommand(byte type, byte channel, byte flags,
                                       int seqNum, byte[] payload)
    {
        int totalLen = CommandHeaderSize + payload.Length;
        var buf = new byte[totalLen];
        buf[0] = type;
        buf[1] = channel;
        buf[2] = flags;
        buf[3] = 0; // reserved
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(4), totalLen);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(8), seqNum);
        payload.CopyTo(buf, CommandHeaderSize);
        return buf;
    }

    public static byte[] BuildAck(int reliableSeq, uint sentTime)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0), reliableSeq);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), sentTime);
        return BuildCommand(CmdAck, 0, 0, 0, payload);
    }

    public static byte[] BuildVerifyConnect()
    {
        // VerifyConnect carries an empty-ish 44-byte payload
        return BuildCommand(CmdVerifyConnect, 0, 0, 0, new byte[44]);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Operation response serializer
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Serialize a Photon operation response into wire bytes.</summary>
    public static byte[] SerializeOpResponse(byte opCode, short returnCode,
                                              Dictionary<byte, object?> parameters)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms, Encoding.UTF8, true);

        w.Write(MsgOperationResponse); // message type
        w.Write(opCode);

        // return code (big-endian short)
        w.Write(BEShort(returnCode));

        // debug message = null
        w.Write(TypeNull);

        // parameter count (big-endian short)
        w.Write(BEShort((short)parameters.Count));

        foreach (var (key, value) in parameters)
        {
            w.Write(key);
            WriteValue(w, value);
        }

        return ms.ToArray();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Type encoder
    // ════════════════════════════════════════════════════════════════════════

    public static void WriteValue(BinaryWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.Write(TypeNull);
                break;

            case bool b:
                w.Write(TypeBoolean);
                w.Write(b ? (byte)1 : (byte)0);
                break;

            case byte by:
                w.Write(TypeByte);
                w.Write(by);
                break;

            case short s:
                w.Write(TypeShort);
                w.Write(BEShort(s));
                break;

            case int i:
                w.Write(TypeInt);
                w.Write(BEInt(i));
                break;

            case long l:
                w.Write(TypeLong);
                w.Write(BELong(l));
                break;

            case float f:
                w.Write(TypeFloat);
                w.Write(BEFloat(f));
                break;

            case string s:
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                w.Write(TypeString);
                w.Write(BEShort((short)bytes.Length));
                w.Write(bytes);
                break;
            }

            case string[] arr:
            {
                w.Write(TypeStringArray);
                w.Write(BEShort((short)arr.Length));
                foreach (var s in arr)
                {
                    var bytes = Encoding.UTF8.GetBytes(s);
                    w.Write(BEShort((short)bytes.Length));
                    w.Write(bytes);
                }
                break;
            }

            case byte[] bytes:
                w.Write(TypeByteArray);
                w.Write(BEInt(bytes.Length));
                w.Write(bytes);
                break;

            case Dictionary<object, object> ht:
            {
                w.Write(TypeHashtable);
                w.Write(BEShort((short)ht.Count));
                foreach (var (k, v) in ht)
                {
                    WriteValue(w, k);
                    WriteValue(w, v);
                }
                break;
            }

            default:
                // Fallback: serialize as string
                var fallback = Encoding.UTF8.GetBytes(value.ToString()!);
                w.Write(TypeString);
                w.Write(BEShort((short)fallback.Length));
                w.Write(fallback);
                break;
        }
    }

    // ── Big-endian helpers ───────────────────────────────────────────────────

    private static byte[] BEShort(short v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        return b;
    }

    private static byte[] BEInt(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        return b;
    }

    private static byte[] BELong(long v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        return b;
    }

    private static byte[] BEFloat(float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(b, v);
        return b;
    }

    // ── Packet parser ────────────────────────────────────────────────────────

    public static bool TryParsePacketHeader(ReadOnlySpan<byte> data,
        out ushort peerId, out byte flags, out byte cmdCount,
        out uint timestamp, out uint challenge)
    {
        peerId = 0; flags = 0; cmdCount = 0; timestamp = 0; challenge = 0;
        if (data.Length < PacketHeaderSize) return false;
        peerId    = BinaryPrimitives.ReadUInt16BigEndian(data[0..]);
        flags     = data[2];
        cmdCount  = data[3];
        timestamp = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        challenge = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        return true;
    }

    public static bool TryParseCommandHeader(ReadOnlySpan<byte> data, int offset,
        out byte type, out byte channel, out byte cmdFlags,
        out int length, out int seqNum)
    {
        type = 0; channel = 0; cmdFlags = 0; length = 0; seqNum = 0;
        if (data.Length < offset + CommandHeaderSize) return false;
        type    = data[offset];
        channel = data[offset + 1];
        cmdFlags= data[offset + 2];
        length  = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
        seqNum  = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 8)..]);
        return true;
    }
}
