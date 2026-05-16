using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using static RecRoomServer.Photon.PhotonProtocol;

namespace RecRoomServer.Photon;

/// <summary>
/// UDP server that implements the Photon 4 name-server and master-server protocols.
/// Handles client connection handshake, authentication, region queries, and room management.
/// </summary>
public class PhotonServer : IDisposable
{
    public enum ServerMode { NameServer, MasterServer }

    private readonly ILogger<PhotonServer>  _log;
    private readonly int                    _port;
    private readonly ServerMode             _mode;
    private readonly string                 _masterAddress;
    private readonly int                    _masterPort;

    private UdpClient?  _udp;
    private bool        _running;


    // peer state keyed by endpoint
    private readonly Dictionary<string, PeerState> _peers = new();

    private class PeerState
    {
        public ushort PeerId;
        public int    OutSeq;
        public int    InSeq;
    }

    public PhotonServer(int port, ServerMode mode, string masterAddress, int masterPort,
                        ILogger<PhotonServer> log)
    {
        _port          = port;
        _mode          = mode;
        _masterAddress = masterAddress;
        _masterPort    = masterPort;
        _log           = log;
    }

    public void Start()
    {
        _udp     = new UdpClient(_port);
        _running = true;
        Task.Run(ReceiveLoopAsync);
        _log.LogInformation("Photon {Mode} listening UDP :{Port}", _mode, _port);
    }

    public void Stop()
    {
        _running = false;
        _udp?.Close();
    }

    public void Dispose() => Stop();

    // ── Receive loop ─────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync()
    {
        while (_running)
        {
            try
            {
                var result = await _udp!.ReceiveAsync();
                _ = Task.Run(() => HandlePacket(result.Buffer, result.RemoteEndPoint));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { _log.LogError(ex, "UDP receive error"); }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint remote)
    {
        if (!TryParsePacketHeader(data, out var peerId, out _, out var cmdCount,
                                   out var timestamp, out _)) return;

        var key   = remote.ToString();
        var state = _peers.TryGetValue(key, out var s)
            ? s : (_peers[key] = new PeerState { PeerId = peerId });

        int offset = PacketHeaderSize;
        for (int i = 0; i < cmdCount; i++)
        {
            if (!TryParseCommandHeader(data, offset, out var type, out var channel,
                                        out _, out var length, out var seqNum)) break;

            int payloadStart  = offset + CommandHeaderSize;
            int payloadLength = length  - CommandHeaderSize;
            var payload       = payloadLength > 0
                ? data.AsSpan(payloadStart, payloadLength).ToArray()
                : Array.Empty<byte>();

            offset += Math.Max(length, CommandHeaderSize);
            state.InSeq = seqNum;

            HandleCommand(remote, state, type, channel, seqNum, timestamp, payload);
        }
    }

    private void HandleCommand(IPEndPoint remote, PeerState state,
                                byte type, byte channel,
                                int seqNum, uint timestamp, byte[] payload)
    {
        switch (type)
        {
            case CmdConnect:
                _log.LogInformation("[{Mode}] Client connected: {Ep}", _mode, remote);
                var ack = BuildAck(seqNum, timestamp);
                var vc  = BuildVerifyConnect();
                SendPacket(remote, state, CombineBytes(ack, vc), 2);
                break;

            case CmdPing:
                var pAck  = BuildAck(seqNum, timestamp);
                var pong  = BuildCommand(CmdPing, channel, 0, 0, new byte[4]);
                SendPacket(remote, state, CombineBytes(pAck, pong), 2);
                break;

            case CmdSendReliable when payload.Length > 0:
                var rAck = BuildAck(seqNum, timestamp);
                SendPacket(remote, state, rAck, 1);
                HandleMessage(remote, state, payload);
                break;

            case CmdSendUnreliable when payload.Length > 0:
                HandleMessage(remote, state, payload);
                break;

            case CmdDisconnect:
                _log.LogInformation("[{Mode}] Client disconnected: {Ep}", _mode, remote);
                _peers.Remove(remote.ToString());
                break;
        }
    }

    // ── Message dispatcher ────────────────────────────────────────────────────

    private void HandleMessage(IPEndPoint remote, PeerState state, byte[] payload)
    {
        if (payload.Length < 2) return;
        byte msgType = payload[0];
        byte opCode  = payload[1];

        _log.LogDebug("[{Mode}] Op {Op} from {Ep}", _mode, opCode, remote);

        switch (msgType)
        {
            case MsgOperationRequest:
            case MsgInternalOperationRequest:
            case MsgInitRequest:
                DispatchOperation(remote, state, opCode, payload.AsSpan(2));
                break;
        }
    }

    private void DispatchOperation(IPEndPoint remote, PeerState state,
                                    byte opCode, ReadOnlySpan<byte> paramData)
    {
        switch (opCode)
        {
            case OpAuthenticate:
                RespondAuthenticate(remote, state);
                break;

            case OpGetRegions:
                RespondGetRegions(remote, state);
                break;

            case OpJoinLobby:
                RespondOk(remote, state, OpJoinLobby);
                break;

            case OpCreateRoom:
            case OpJoinRoom:
            case OpJoinRandom:
                RespondJoinRoom(remote, state, opCode);
                break;

            case OpLeave:
                RespondOk(remote, state, OpLeave);
                break;

            default:
                // Unknown op → generic OK
                RespondOk(remote, state, opCode);
                break;
        }
    }

    // ── Operation responses ───────────────────────────────────────────────────

    private void RespondAuthenticate(IPEndPoint remote, PeerState state)
    {
        var masterAddr = $"{_masterAddress}:{_masterPort}";
        var resp = SerializeOpResponse(OpAuthenticate, RcOk, new()
        {
            // 0xC8 = masterAddress, 0xC9 = cluster/region, 0xD4 = userId
            [0xC8] = (object)masterAddr,
            [0xC9] = "us",
            [0xD4] = "10000001",
        });
        SendReliableOp(remote, state, resp);
        _log.LogInformation("[{Mode}] Authenticated client {Ep} → {Master}", _mode, remote, masterAddr);
    }

    private void RespondGetRegions(IPEndPoint remote, PeerState state)
    {
        var masterAddr = $"{_masterAddress}:{_masterPort}";
        var resp = SerializeOpResponse(OpGetRegions, RcOk, new()
        {
            // 0xC8 = region codes array, 0xC9 = master server addresses array
            [0xC8] = (object)new string[] { "us" },
            [0xC9] = new string[] { masterAddr },
        });
        SendReliableOp(remote, state, resp);
        _log.LogInformation("[{Mode}] Sent regions to {Ep}", _mode, remote);
    }

    private void RespondJoinRoom(IPEndPoint remote, PeerState state, byte opCode)
    {
        var roomName = $"preserved-{Guid.NewGuid().ToString("N")[..8]}";
        var resp = SerializeOpResponse(opCode, RcOk, new()
        {
            [0xFF] = (object)roomName,   // room name
            [0xFE] = 10,                 // max players
            [0xFD] = true,               // is new room
        });
        SendReliableOp(remote, state, resp);
        _log.LogInformation("[{Mode}] Created/Joined room '{Room}' for {Ep}", _mode, roomName, remote);
    }

    private void RespondOk(IPEndPoint remote, PeerState state, byte opCode)
    {
        var resp = SerializeOpResponse(opCode, RcOk, new());
        SendReliableOp(remote, state, resp);
    }

    // ── Send helpers ──────────────────────────────────────────────────────────

    private void SendReliableOp(IPEndPoint remote, PeerState state, byte[] opData)
    {
        state.OutSeq++;
        var cmd = BuildCommand(CmdSendReliable, 0, 1, state.OutSeq, opData);
        SendPacket(remote, state, cmd, 1);
    }

    private void SendPacket(IPEndPoint remote, PeerState state, byte[] cmdData, int cmdCount)
    {
        // Build a manual packet header so we can set the right commandCount
        var ts  = (uint)(Environment.TickCount & 0x7FFFFFFF);
        var buf = new byte[PacketHeaderSize + cmdData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0), state.PeerId);
        buf[2] = 0;
        buf[3] = (byte)cmdCount;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), ts);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(8), 0);
        cmdData.CopyTo(buf, PacketHeaderSize);
        _udp!.Send(buf, buf.Length, remote);
    }

    private static byte[] CombineBytes(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        int offset = 0;
        foreach (var a in arrays) { a.CopyTo(result, offset); offset += a.Length; }
        return result;
    }
}
