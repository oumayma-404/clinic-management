using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>Who an assertion speaks for (<c>clinic-pc-copy</c> D22).</summary>
public sealed record RelayAssertionClaims(
    string UserId, Guid ClinicId, Guid RelayId, int TokenVersion, DateTime IssuedAtUtc, DateTime ExpiresAtUtc);

/// <summary>
/// The prepared session's ticket (<c>clinic-pc-copy</c> D22): while online, a cabinet app gets one from the cloud for the
/// person signed in, and trades it with the PC de secours for a session there — so after a switch nobody signs in again.
/// The PC cannot check the cloud's tokens and must never mint them; this is the one thing it can check.
///
/// <para><c>ra1.&lt;payload&gt;.&lt;HMAC-SHA256&gt;</c>, base64url, keyed by a secret only the cloud and that PC know
/// (<see cref="ClinicManagement.Domain.Entities.ClinicRelay.AssertionKeyProtected"/>, sealed to the PC at its heartbeat).
/// It carries the account's token version, so a disabled account, a changed password or « déconnecter partout » on the
/// cloud — all of which bump it, and reach the PC with the copied account row — make every older ticket worthless.</para>
/// </summary>
public static class RelayAssertion
{
    private const string Version = "ra1";

    /// <summary>A day and a little: the app trades a fresh one daily, and a cut that starts at night must still find one.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(26);

    public static string Issue(byte[] key, RelayAssertionClaims claims)
    {
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Wire(
            claims.UserId, claims.ClinicId, claims.RelayId, claims.TokenVersion,
            new DateTimeOffset(claims.IssuedAtUtc).ToUnixTimeSeconds(),
            new DateTimeOffset(claims.ExpiresAtUtc).ToUnixTimeSeconds())));
        return $"{Version}.{payload}.{Encode(Mac(key, payload))}";
    }

    /// <summary>The claims, or null for anything not issued under this key, altered, malformed or expired.</summary>
    public static RelayAssertionClaims? Read(byte[] key, string? assertion, DateTime nowUtc)
    {
        var parts = assertion?.Split('.');
        if (parts is not { Length: 3 } || parts[0] != Version)
        {
            return null;
        }

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Decode(parts[2]), Mac(key, parts[1])))
            {
                return null;
            }

            var wire = JsonSerializer.Deserialize<Wire>(Decode(parts[1]));
            if (wire is null || string.IsNullOrEmpty(wire.U))
            {
                return null;
            }

            var claims = new RelayAssertionClaims(wire.U, wire.C, wire.R, wire.V,
                DateTimeOffset.FromUnixTimeSeconds(wire.I).UtcDateTime, DateTimeOffset.FromUnixTimeSeconds(wire.E).UtcDateTime);
            return claims.ExpiresAtUtc > nowUtc ? claims : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Mac(byte[] key, string payload) =>
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(Version + "." + payload));

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    private sealed record Wire(string U, Guid C, Guid R, int V, long I, long E);
}
