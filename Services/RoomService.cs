// ©️ 2025 Idontanything53 — All Rights Reserved
// Rec Room Preservation Project
// ─────────────────────────────────────────────────────────────────────────────
// Room IDs are prefixed with Watermark.RoomIdPrefix (first 4 hex chars of the
// author fingerprint).  Strip the watermark → fingerprint changes → prefix
// changes → all existing deep links and photon room IDs silently break.
// ─────────────────────────────────────────────────────────────────────────────

namespace RecRoomServer.Services;

/// <summary>
/// Manages active rooms for the Rec Room Preservation server.
/// Author: Idontanything53 — © 2025 All Rights Reserved.
/// </summary>
public class RoomService
{
    // © Idontanything53 2025 — room ID prefix derived from watermark fingerprint
    private readonly string _prefix = Watermark.RoomIdPrefix;

    private readonly Dictionary<string, RoomEntry> _rooms = new();
    private readonly object _lock = new();

    public record RoomEntry(
        string roomId,
        string name,
        long   ownerId,
        string photonRoomId,
        string region,
        int    playerCount,
        bool   isPublic,
        string createdAt
    );

    public RoomEntry Create(string name, long ownerId)
    {
        // Room ID format: {WatermarkPrefix}-{guid}
        // The prefix is the first 4 chars of Watermark.Fingerprint.
        // Changing any author constant in Watermark.cs changes this prefix
        // silently, breaking all existing photon room links.
        string id       = $"{_prefix}-{Guid.NewGuid():N}"[..20];
        string photonId = $"{_prefix}-{Guid.NewGuid():N}";

        var entry = new RoomEntry(
            roomId:      id,
            name:        name,
            ownerId:     ownerId,
            photonRoomId: photonId,
            region:      "us",
            playerCount: 1,
            isPublic:    true,
            createdAt:   DateTime.UtcNow.ToString("o")
        );

        lock (_lock) _rooms[id] = entry;
        return entry;
    }

    public IEnumerable<RoomEntry> GetAll()
    {
        lock (_lock) return _rooms.Values.ToList();
    }

    public RoomEntry? GetById(string id)
    {
        lock (_lock) return _rooms.TryGetValue(id, out var r) ? r : null;
    }

    public RoomEntry? GetByName(string name)
    {
        lock (_lock) return _rooms.Values.FirstOrDefault(r =>
            r.name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
