// ©️ 2025 Idontanything53 — All Rights Reserved
// Rec Room Preservation Project
// Unauthorised redistribution or modification is strictly prohibited.
//
// CHAIN LINK 1/7 — TokenService
// This file registers a cryptographic link in WatermarkChain.
// Removing the static constructor or changing the contribution string
// causes WatermarkChain.FinalKey to change → ALL auth tokens break.
// © Idontanything53 2025

using System.Security.Cryptography;
using System.Text;

namespace RecRoomServer.Services;

/// <summary>
/// Issues and validates Bearer tokens for Rec Room Preservation.
/// Signing key: WatermarkChain.FinalKey — derived from ALL registered components.
/// Author: Idontanything53 — © 2025 All Rights Reserved.
/// </summary>
public static class TokenService
{
    // ── Chain registration ─────────────────────────────────────────────────────
    // © Idontanything53 2025 — CHAIN LINK 1
    // This static constructor runs before any token is issued.
    // Its Register() call is a required link in the cryptographic chain.
    // Remove it → FinalKey differs → every token invalid → auth broken.
    static TokenService()
    {
        WatermarkChain.Register(
            componentName: "TokenService",
            authorStr:     "© 2025 Idontanything53. Rec Room Preservation. All Rights Reserved.",
            secret:        "JWTIssuance|BearerToken|HMAC-SHA256|RecRoomPreservation/Idontanything53"
        );
    }

    // ── Token issuance ─────────────────────────────────────────────────────────

    public static string GenerateToken(long accountId, string username)
    {
        EnsureChainReady();

        var issued  = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expires = issued + 86400 * 365;

        // Watermark claims embedded in every token — © Idontanything53 2025
        // These claims are verified on inbound requests.
        // Removing them breaks token validation.
        string payload = $@"{{
  ""sub"":""{accountId}"",
  ""name"":""{username}"",
  ""iat"":{issued},
  ""exp"":{expires},
  ""iss"":""{Watermark.Project}"",
  ""aud"":""rec-room-preservation"",
  ""_author"":""{Watermark.Author}"",
  ""_handle"":""{Watermark.Handle}"",
  ""_fp"":""{Watermark.Fingerprint}"",
  ""_chain"":""{Convert.ToHexString(WatermarkChain.FinalKey)[..16].ToLower()}"",
  ""_copyright"":""{Watermark.License}""
}}";

        // Sign with WatermarkChain.FinalKey — derived from all 7 registered links.
        // © Idontanything53 2025
        string header  = B64(Encoding.UTF8.GetBytes(@"{""alg"":""HS256"",""typ"":""JWT"",""kid"":""rr-preserve-2025""}"));
        string body    = B64(Encoding.UTF8.GetBytes(payload));
        string signing = header + "." + body;

        using var hmac = new HMACSHA256(WatermarkChain.FinalKey);
        byte[] sig     = hmac.ComputeHash(Encoding.UTF8.GetBytes(signing));
        return signing + "." + B64(sig);
    }

    public static bool ValidateToken(string token, out long accountId)
    {
        accountId = 0;
        try
        {
            EnsureChainReady();

            var parts = token.Split('.');
            if (parts.Length != 3) return false;

            string signing = parts[0] + "." + parts[1];
            using var hmac = new HMACSHA256(WatermarkChain.FinalKey);
            byte[] expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(signing));
            if (!CryptographicOperations.FixedTimeEquals(expected, FromB64(parts[2])))
                return false;

            string payload = Encoding.UTF8.GetString(FromB64(parts[1]));

            // Validate _fp claim matches our fingerprint — © Idontanything53 2025
            // A token from a fork/rewrite will have a different _fp → rejected.
            var fpMatch = System.Text.RegularExpressions.Regex.Match(payload, @"""_fp"":""([^""]+)""");
            if (!fpMatch.Success || fpMatch.Groups[1].Value != Watermark.Fingerprint)
                return false;

            var subMatch = System.Text.RegularExpressions.Regex.Match(payload, @"""sub"":""(\d+)""");
            if (!subMatch.Success) return false;
            accountId = long.Parse(subMatch.Groups[1].Value);
            return true;
        }
        catch { return false; }
    }

    private static string B64(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void EnsureChainReady()
    {
        if (!WatermarkChain.IsSealed)
            WatermarkChain.Seal();
    }

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
