namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Encrypts a clinic's Google Calendar OAuth refresh token at rest (<c>hosted-security-hardening</c> FR-3.4).
/// It was the <b>last</b> credential in this database held in the clear.
///
/// <para>⚠️ <see cref="TryUnprotect"/> returns a <c>bool</c> and not a nullable, for
/// <see cref="IUserSecretProtector"/>'s reason: a nullable is one <c>??</c> away from a caller silently
/// substituting something else, and here the something else would be « sync this clinic's calendar with no
/// credential », i.e. a silent stop rather than a stated one (FR-3.3).</para>
///
/// <para>⚠️ Its own purpose string, so a calendar token is not decryptable by the code that reads a second
/// factor or a reminder credential. The purpose feeds the key derivation, so the framework enforces it.</para>
/// </summary>
public interface IGoogleTokenProtector
{
    string Protect(string refreshToken);

    bool TryUnprotect(string protectedRefreshToken, out string refreshToken);
}
