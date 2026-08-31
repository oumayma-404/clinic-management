using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A named, revocable credential that lets one machine pull this cabinet's archive unattended
/// (<c>clinic-archive-auto-copy</c>).
///
/// <para><b>Why it exists.</b> <c>GET /api/backup/archive</c> requires a step-up confirmation that lives five
/// minutes and is spent on use, so a scheduled copy is impossible without a human at the keyboard — and the only
/// remedy the product offered for « aucune archive n'est sortie du cabinet » was an admin remembering to click a
/// button. This is the credential a desktop shell holds so the copy happens on its own.</para>
///
/// <para>⚠️ <b>It deliberately relaxes the guard the step-up exists for</b>, whose stated case is « an unlocked
/// machine with an admin session open must not be enough to take a practice's whole record out ». That trade is
/// only defensible because of where the archive lands: the folder on that same machine already holds every
/// previous copy, so re-authenticating to fetch the next one protects nothing an attacker at that keyboard does
/// not already have. It follows that a grant is worth exactly as much as the machine holding it — hence a label,
/// a last-used stamp and one-click revocation, so an owner can answer « which machines can pull my record? ».</para>
///
/// <para>⚠️ <b>It authorises the download and nothing else.</b> The archive and the restore are separate step-up
/// actions precisely so one confirmation cannot become the other, and a grant must not collapse that: presented
/// to the restore endpoint it is not recognised at all. Reading a cabinet's record out is recoverable; writing
/// one back over it is not.</para>
///
/// <para>⚠️ <b>Excluded from the archive</b> (<c>ClinicArchiveScope.Excluded</c>), beside <see cref="User"/> and
/// for the same reason: credentials do not travel in a file on a laptop. A grant inside the archive would also be
/// re-created by a restore, silently re-arming a machine the owner had revoked.</para>
/// </summary>
public class ClinicArchiveGrant : AggregateRoot<Guid>
{
    /// <summary>Bytes of CSPRNG behind the secret — <see cref="ClinicSignup"/>'s token size.</summary>
    private const int SecretBytes = 32;

    /// <summary>The cabinet whose archive this grant pulls. Never null: a grant with no cabinet authorises nothing.</summary>
    public Guid ClinicId { get; private set; }

    /// <summary>What the owner calls the machine holding it — « Portable du Dr Ben Salah ». For revocation, so it is required.</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the secret. The plaintext exists once, in the response that created it, and nowhere else.</summary>
    public string SecretHash { get; private set; } = string.Empty;

    /// <summary>Who issued it, so the audit answers « qui a autorisé ce poste ? ». A <see cref="User"/> key, which
    /// is the account's auth subject rather than a Guid.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When it last pulled an archive. Null until it has — which is itself worth seeing in the list.</summary>
    public DateTime? LastUsedAtUtc { get; private set; }

    /// <summary>Set once and never cleared: revocation is permanent, and re-arming a machine means issuing a new grant.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    private ClinicArchiveGrant() { }

    public static ClinicArchiveGrant Create(
        Guid clinicId, string label, string secretHash, string createdByUserId, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Label = label.Trim(),
            SecretHash = secretHash,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
        };

    /// <summary>A CSPRNG secret and its hash. The caller shows the first once and persists only the second.</summary>
    public static (string Secret, string Hash) NewSecret()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
        return (secret, HashSecret(secret));
    }

    /// <summary>
    /// SHA-256, not PBKDF2, and the difference is deliberate: this secret is 32 bytes of CSPRNG rather than
    /// something a person chose, so it has nothing to brute-force and a slow hash would only cost a scheduled
    /// pull. <see cref="ClinicSignup.TokenHash"/> takes the same view of the same kind of value.
    /// </summary>
    public static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>
    /// How long a grant may sit unused before it stops authorising anything.
    ///
    /// <para><b>Why a grant has to expire at all.</b> Exchanging this secret mints an ordinary clinic
    /// <b>administrator</b> access token with the whole API surface — not a token scoped to the archive — so the
    /// secret sitting on a practice's Windows PC is, in effect, a standing admin credential. With
    /// <c>RevokedAtUtc</c> as the only end, it was <b>permanent</b>: anything that reads that disk once owns the
    /// cabinet until somebody notices and revokes by hand, and nobody revokes a credential they have forgotten
    /// exists.</para>
    ///
    /// <para>⚠️ <b>Idle, not absolute — and the distinction is what makes it safe to ship.</b> The window runs
    /// from the last <i>use</i>, so the poste that actually copies (nightly, or weekly) renews itself simply by
    /// working and nobody is ever interrupted. What dies is a grant nothing has presented for three months: a
    /// decommissioned PC, a laptop that left the practice, a copy of the file taken off a disk. An absolute
    /// lifetime would instead stop a working installation on a date nobody chose, which is how a security control
    /// gets switched off rather than renewed.</para>
    ///
    /// <para>⚠️ <b>Ninety days is deliberately generous.</b> The shortest cadence the shell offers is far inside
    /// it, and the cost of being wrong in the tight direction — a practice's unattended copy silently stopping —
    /// is exactly the failure `clinic-recovery-points` exists to prevent.</para>
    /// </summary>
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// When this grant stops authorising by disuse. Derived rather than stored, so no column and no migration:
    /// the two instants it needs are already here.
    /// </summary>
    public DateTime ExpiresAtUtc => (LastUsedAtUtc ?? CreatedAtUtc) + IdleLifetime;

    /// <summary>Whether this grant may still authorise a pull, at <paramref name="nowUtc"/>.</summary>
    public bool IsUsable(DateTime nowUtc) =>
        (RevokedAtUtc == null || RevokedAtUtc > nowUtc)
        && ExpiresAtUtc > nowUtc;

    public void MarkUsed(DateTime nowUtc) => LastUsedAtUtc = nowUtc;

    /// <summary>Idempotent: revoking twice is not an error, and the first moment is the one that counts.</summary>
    public void Revoke(DateTime nowUtc) => RevokedAtUtc ??= nowUtc;
}
