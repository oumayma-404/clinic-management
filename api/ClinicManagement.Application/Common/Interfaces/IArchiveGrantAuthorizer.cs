namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Turns an archive device grant's secret into the cabinet it may pull (<c>clinic-archive-auto-copy</c>).
///
/// <para>⚠️ <b>Why a grant is exchanged for an ordinary access token rather than being a second way in.</b> Every
/// downstream piece of the archive path — the <c>AdminOnly</c> policy, <c>IClinicContext</c>, the EF tenant filter,
/// the access ledger, the <c>LastArchiveDownloadedAtUtc</c> stamp — reads a JWT's claims. A parallel credential
/// that skipped all of that would need each of them re-implemented for the unattended case, and the half somebody
/// forgot would be a hole. So the grant buys a normal short-lived token for the account that issued it, and
/// nothing after that point knows the request began differently.</para>
///
/// <para>⚠️ It follows that a grant is <b>only as alive as the account behind it</b>: deactivate that admin or drop
/// their role and the exchange refuses. That is deliberate — an issuing admin who leaves the practice should not
/// leave a machine able to pull the cabinet's whole record for the rest of its life.</para>
/// </summary>
public interface IArchiveGrantAuthorizer
{
    /// <summary>
    /// Validates <paramref name="secret"/> and stamps the grant's last use, or returns null.
    ///
    /// <para>Null covers unknown, revoked, and a still-valid grant whose issuing account is gone or no longer an
    /// administrator — one refusal for all of them, so a caller learns nothing about which grants exist (AC-3).</para>
    /// </summary>
    Task<ArchiveGrantPrincipal?> AuthorizeAsync(string? secret, CancellationToken cancellationToken = default);
}

/// <summary>Who a valid grant turns out to be. <paramref name="ClinicId"/> is what the caller compares against.</summary>
public record ArchiveGrantPrincipal(Guid GrantId, Guid ClinicId, string UserId);
