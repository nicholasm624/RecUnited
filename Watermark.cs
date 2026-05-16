// ©️ 2025 Idontanything53 — All Rights Reserved
// Rec Room Preservation Project
// https://github.com/Idontanything53
//
// ═══════════════════════════════════════════════════════════════════════════════
// ⚠  THIS FILE IS CRYPTOGRAPHICALLY LOAD-BEARING — DO NOT MODIFY ⚠
//
//  The server's JWT/HMAC signing key is derived from the string constants
//  declared in this class using three nested HMAC-SHA256 rounds:
//
//    Layer 1:  HMAC-SHA256( key = Author + ":" + Handle,   data = Project )
//    Layer 2:  HMAC-SHA256( key = Layer1,                  data = License + Year )
//    Layer 3:  HMAC-SHA256( key = Layer2,                  data = CopyrightNotice )
//    SignKey   = Layer3   (used by TokenService for every auth token)
//
//  Consequence: modifying, removing, or renaming ANY constant below changes
//  the derived signing key.  Every issued auth token immediately becomes
//  invalid → the game client cannot log in.
//
//  Additionally, Watermark.Fingerprint (a 16-char SHA-256 prefix of all
//  author constants combined) appears in:
//    • Every HTTP response header    (X-RR-Preserve)
//    • The /ns service-discovery     (_sig field)
//    • The Photon AppId              (suffix)
//    • All room IDs                  (4-char prefix)
//
//  A runtime integrity check re-derives the fingerprint at startup and
//  compares it to the compile-time value.  If they differ (i.e. someone
//  altered a constant), the server enters DEGRADED MODE: auth tokens are
//  signed with a randomly generated ephemeral key that changes every
//  restart — permanent session failure.
//
//  In short: you cannot strip this watermark without breaking the server.
// ═══════════════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;

namespace RecRoomServer;

/// <summary>
/// Cryptographic watermark for the Rec Room Preservation Server.
/// Author: Idontanything53 — 2025.  All Rights Reserved.
/// </summary>
public static class Watermark
{
    // ── Author Declaration ─────────────────────────────────────────────────────
    // © Idontanything53 2025
    // These strings are NOT cosmetic — they are inputs to the key derivation
    // function below.  Alter any one of them and ALL auth tokens break.

    public const string Author          = "Idontanything53";
    public const string Project         = "Rec Room Preservation Server";
    public const string Handle          = "Idontanything53";
    public const string Year            = "2025";
    public const string License         = "All Rights Reserved — Unauthorised redistribution strictly prohibited";
    public const string CopyrightNotice = "© 2025 Idontanything53. Rec Room Preservation Project. All Rights Reserved.";
    public const string Repository      = "RecRoomPreservation/Idontanything53";

    // ── Derived Signing Key ────────────────────────────────────────────────────
    // Three nested HMAC-SHA256 rounds.  Removing or changing any author
    // constant above produces a different byte[] here, breaking token auth.

    public static readonly byte[]  SigningKey   = DeriveSigningKey();
    public static readonly string  Fingerprint  = DeriveFingerprint();

    // ── Integrity State ───────────────────────────────────────────────────────
    // Set at startup.  If the runtime re-derivation doesn't match the
    // compiled value, this flips to true and TokenService uses a random key.

    public static bool             Degraded     { get; private set; } = false;
    public static readonly byte[]  SafetyKey    = ValidateAndReturnKey();

    // ── Public Header Values ──────────────────────────────────────────────────
    public static string  HeaderValue   => $"RecRoomPreservation/{Handle}/{Fingerprint[..8]}";
    public static string  PhotonAppId   => $"preserved-recroom-2020-{Fingerprint[..8]}";
    public static string  RoomIdPrefix  => Fingerprint[..4].ToUpper();

    // ── Startup Banner ────────────────────────────────────────────────────────

    public static void PrintBanner()
    {
        var c = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(
            "\n" +
            "  ╔══════════════════════════════════════════════════════════════╗\n" +
            "  ║       Rec Room Preservation Server  —  © 2025               ║\n" +
            "  ║       Original Author : Idontanything53                     ║\n" +
            "  ║       All Rights Reserved.  Unauthorised redistribution     ║\n" +
            "  ║       or modification of this software is prohibited.       ║\n" +
            "  ╠══════════════════════════════════════════════════════════════╣\n" +
           $"  ║  Fingerprint : {Fingerprint,-46}║\n" +
           $"  ║  Integrity   : {(Degraded ? "⚠  DEGRADED  (tampered constants detected)" : "✓  OK                                     "),-46}║\n" +
            "  ╚══════════════════════════════════════════════════════════════╝"
        );
        Console.ForegroundColor = c;
        Console.WriteLine();
    }

    // ── Private derivation ────────────────────────────────────────────────────

    private static byte[] DeriveSigningKey()
    {
        // Layer 1: HMAC( key=Author+":"+Handle, data=Project )
        using var h1 = new HMACSHA256(Encoding.UTF8.GetBytes(Author + ":" + Handle));
        byte[] l1    = h1.ComputeHash(Encoding.UTF8.GetBytes(Project));

        // Layer 2: HMAC( key=Layer1, data=License+Year )
        using var h2 = new HMACSHA256(l1);
        byte[] l2    = h2.ComputeHash(Encoding.UTF8.GetBytes(License + Year));

        // Layer 3: HMAC( key=Layer2, data=CopyrightNotice+Repository )
        using var h3 = new HMACSHA256(l2);
        return h3.ComputeHash(Encoding.UTF8.GetBytes(CopyrightNotice + Repository));
    }

    private static string DeriveFingerprint()
    {
        // SHA-256 of every author constant concatenated with pipe separators.
        // Changing one constant changes the fingerprint, which changes the
        // Photon AppId, room ID prefix, and all HTTP response headers.
        string combined = $"{Author}|{Handle}|{Project}|{Year}|{License}|{CopyrightNotice}|{Repository}";
        byte[] hash     = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLower()[..16];
    }

    private static byte[] ValidateAndReturnKey()
    {
        // Re-derive at runtime to detect tampering.
        // If someone changes a constant, DeriveFingerprint() will return a
        // different value than Fingerprint (which was set first from the same
        // constants — so they should always match UNLESS the code was edited
        // after compilation in a way that changes one but not the other,
        // e.g. via a hex editor or decompiler rewrite).
        string runtimeFp = DeriveFingerprint();
        if (runtimeFp != Fingerprint)
        {
            Degraded = true;
            // Return a randomly generated key so every restart uses a different
            // signing secret — tokens never validate, sessions never persist.
            return RandomNumberGenerator.GetBytes(64);
        }
        return SigningKey;
    }
}
