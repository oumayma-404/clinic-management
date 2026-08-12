using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Session families — one device's chain of refresh credentials (<c>hosted-security-hardening</c> FR-1.6).
/// </summary>
public interface ISessionFamilyRepository
{
    /// <summary>
    /// The family a presented credential belongs to, matched on the current <b>or</b> the immediate predecessor.
    ///
    /// <para>⚠️ <b>Must find an ENDED family too.</b> Returning only live ones would make a replayed credential
    /// indistinguishable from an unknown one, and the caller could no longer tell « this device's session was
    /// already stopped » from « this token was never ours » — which is the whole signal.</para>
    /// </summary>
    Task<SessionFamily?> GetByCredentialAsync(string credentialHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// The family a token NAMES, whether or not the credential it carries still matches either stored hash.
    ///
    /// <para>⚠️ This is the lookup replay detection actually uses, and why the family id is a claim: a
    /// credential three generations old hashes to nothing, so without it « stolen credential » and « unknown
    /// token » would be the same answer and the device's session could never be ended.</para>
    /// </summary>
    Task<SessionFamily?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every live family of one account, for « vos autres appareils restent connectés ».</summary>
    Task<IReadOnlyList<SessionFamily>> GetLiveForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task AddAsync(SessionFamily family, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops families whose credential lifetime has run out.
    ///
    /// <para>⚠️ <b>Never deletes a live one</b>, whatever its age: a family is live precisely because a device is
    /// still using it, and pruning by age alone would sign working users out on a schedule.</para>
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
