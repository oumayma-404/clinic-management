using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Application.Features.Auth;

/// <summary>
/// How a refresh credential is stored for comparison (<c>hosted-security-hardening</c> FR-1.6).
///
/// <para><b>The token itself is never persisted — only a SHA-256 of it.</b> A <c>SessionFamily</c> row exists to
/// answer « is this the credential I last issued? », which a hash answers completely; storing the token would
/// mean a database copy handed the reader a live session per row, which is the opposite of what this table is
/// for.</para>
///
/// <para>Plain SHA-256 rather than a password hash, for <c>UserRecoveryCode</c>'s reason: the input is a signed
/// JWT with ~256 bits of unguessable material in its signature, so there is nothing to brute-force, and the
/// comparison must be <b>deterministic</b> because it is a lookup key.</para>
/// </summary>
public static class SessionCredential
{
    public static string Hash(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
}
