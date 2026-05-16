// ©️ 2025 Idontanything53 — All Rights Reserved
// Rec Room Preservation Project
// Unauthorised redistribution or modification is strictly prohibited.
//
// ═══════════════════════════════════════════════════════════════════════════════
// ⚠  WATERMARK CHAIN — DO NOT MODIFY ANY CONTRIBUTION STRING ⚠
//
//  Every major component of this server MUST register a cryptographic
//  "link" in this chain via WatermarkChain.Register(...) before the chain
//  is sealed.  The final signing key (WatermarkChain.FinalKey) is derived
//  from the ORDERED XOR of SHA-256 hashes of all registered contributions.
//
//  The consequences of tampering:
//
//    • Remove a Register() call from ANY file
//      → that file's link is missing → chain produces a different hash
//      → FinalKey changes → ALL auth tokens become permanently invalid.
//
//    • Change the contribution string in ANY file (e.g. change the author)
//      → that link's hash changes → chain produces a different hash
//      → FinalKey changes → ALL auth tokens become permanently invalid.
//
//    • Add a new file but forget to register it
//      → chain is a different length → FinalKey changes → auth breaks.
//
//    • Rearrange the Register() call order
//      → chain XOR is in a different order → FinalKey changes → auth breaks.
//
//  The only way to produce a valid FinalKey is to have EXACTLY the correct
//  set of contribution strings, registered in the correct order.  This means
//  you cannot strip the watermark from any single file in isolation — you
//  must understand and consistently replicate the entire chain across all
//  files simultaneously.  In practice, this requires rewriting the server
//  from scratch.
//
//  © 2025 Idontanything53.  Rec Room Preservation Project.  All Rights Reserved.
// ═══════════════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;

namespace RecRoomServer;

/// <summary>
/// Cryptographic watermark chain for the Rec Room Preservation Server.
/// Every server component registers a link; the final key is their combined hash.
/// Author: Idontanything53 — © 2025 All Rights Reserved.
/// </summary>
public static class WatermarkChain
{
    // ── Chain state ────────────────────────────────────────────────────────────
    // Links are registered by each component.  Seal() is called once at startup.
    // After sealing, FinalKey is the definitive signing key for this session.

    private static readonly List<(string Component, byte[] Hash)> _links = new();
    private static bool   _sealed = false;
    private static byte[] _finalKey = Array.Empty<byte>();

    // ── Expected link count ───────────────────────────────────────────────────
    // If the chain is sealed with fewer links than this, it enters degraded mode.
    // Changing this number is not enough — the contribution hashes must also match.
    // © Idontanything53 2025
    private const int ExpectedLinkCount = 7;

    // ── Public API ─────────────────────────────────────────────────────────────

    public static bool   IsSealed   => _sealed;
    public static bool   Degraded   => _degraded;
    public static byte[] FinalKey   => _sealed ? _finalKey : throw new InvalidOperationException("Chain not sealed.");
    public static int    LinkCount  => _links.Count;

    private static bool _degraded = false;

    /// <summary>
    /// Called by each server component to contribute its link to the chain.
    /// The contribution is: SHA-256( componentName + "|" + authorStr + "|" + secret ).
    /// MUST be called before WatermarkChain.Seal().
    /// © Idontanything53 2025
    /// </summary>
    public static void Register(string componentName, string authorStr, string secret)
    {
        if (_sealed) throw new InvalidOperationException("Cannot register links after the chain is sealed.");
        string input = $"{componentName}|{authorStr}|{secret}|{Watermark.CopyrightNotice}|{Watermark.Fingerprint}";
        byte[] hash  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        _links.Add((componentName, hash));
    }

    /// <summary>
    /// Seals the chain and computes FinalKey.
    /// Must be called exactly once, after all components have registered.
    /// FinalKey = HMAC-SHA256( key=Watermark.SigningKey, data=XOR(all link hashes) )
    /// © Idontanything53 2025
    /// </summary>
    public static void Seal()
    {
        if (_sealed) throw new InvalidOperationException("Chain already sealed.");
        _sealed = true;

        if (_links.Count != ExpectedLinkCount)
        {
            _degraded = true;
            // Wrong number of links → use random ephemeral key → auth permanently broken
            _finalKey = RandomNumberGenerator.GetBytes(64);
            return;
        }

        // XOR all link hashes together (order-dependent because we apply sequentially)
        // © Idontanything53 2025 — do not reorder the Register() calls
        byte[] accumulated = new byte[32];
        for (int i = 0; i < _links.Count; i++)
        {
            byte[] linkHash = _links[i].Hash;
            // Rotate-left by i bits before XOR to make order matter
            byte[] rotated = RotateLeft(linkHash, i);
            for (int j = 0; j < 32; j++)
                accumulated[j] ^= rotated[j];
        }

        // Final key = HMAC-SHA256( key=Watermark.SigningKey, data=accumulated )
        using var hmac = new HMACSHA256(Watermark.SigningKey);
        _finalKey = hmac.ComputeHash(accumulated);
    }

    public static void PrintChainStatus()
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  Chain Links ({_links.Count}/{ExpectedLinkCount}):");
        foreach (var (name, hash) in _links)
            Console.WriteLine($"    ✓  {name,-30}  {Convert.ToHexString(hash)[..12].ToLower()}…");
        Console.ForegroundColor = _degraded ? ConsoleColor.Red : ConsoleColor.Green;
        Console.WriteLine($"  Chain Status : {(_degraded ? "⚠  DEGRADED — wrong link count or tampered constants" : "✓  INTACT")}");
        Console.ResetColor();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static byte[] RotateLeft(byte[] data, int bits)
    {
        // Bit-rotate a 32-byte array left by `bits` positions.
        // Makes the XOR accumulation order-dependent.
        // © Idontanything53 2025
        bits = bits % 256;
        if (bits == 0) return (byte[])data.Clone();
        int byteShift = bits / 8;
        int bitShift  = bits % 8;
        var result    = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            int src  = (i + byteShift) % data.Length;
            int src2 = (src + 1)       % data.Length;
            result[i] = (byte)((data[src] << bitShift) | (data[src2] >> (8 - bitShift)));
        }
        return result;
    }
}
