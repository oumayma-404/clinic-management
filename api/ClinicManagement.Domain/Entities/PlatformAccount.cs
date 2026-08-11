using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A vendor's console identity — <b>a separate population from <see cref="User"/></b> (FR-1, AC-1.1).
///
/// <para><b>Why a second entity rather than a role on <see cref="User"/>.</b> A <c>User</c> belongs to a clinic:
/// its <c>ClinicId</c> is non-nullable, it is what every tenant check reads, and the whole product's
/// authorization vocabulary is « which role inside this cabinet ». A console account belongs to <i>no</i> cabinet
/// and can look across all of them, so expressing it as a clinic role would mean either inventing a sentinel
/// clinic — the sentinel-value defect this codebase has already retired four times — or making <c>ClinicId</c>
/// nullable and re-auditing every read that assumes it is not. Two populations is also what makes AC-1.4
/// (« neither session works on the other's routes ») a property of the token rather than of a policy.</para>
///
/// <para><b>No <c>ClinicId</c>, and therefore outside the EF tenant filter by construction</b> — the same shape
/// as <c>ClinicSignup</c>, and for the same reason: a row that exists precisely because it belongs to no clinic
/// cannot be filtered by one. It needs no <c>TenantScopeFilterTests</c> entry, whose clinic-owned set is derived
/// from that very column.</para>
///
/// <para><b>The lockout mirrors <see cref="User"/>'s deliberately</b> (AC-1.5): the durable counter here is the
/// cross-source backstop, and the per-(account, source) brake is <c>ILoginAttemptTracker</c>, exactly as on the
/// clinic side. Two copies of the shape rather than one shared base class, because the numbers are a policy
/// decision per population and inheriting them would hide that they were ever chosen.</para>
///
/// <para>⚠️ <b>The TOTP secret arrives already encrypted and this type never decrypts it.</b> Domain references
/// nothing, so protection lives in <c>PlatformSecretProtector</c> (Infrastructure) and what is stored here is
/// opaque text. The consequence is stated in the spec's Dependencies: the deployment's Data Protection key ring
/// now gates <i>sign-in itself</i>, not only the reminder credentials it protected before.</para>
/// </summary>
public class PlatformAccount : AggregateRoot<Guid>
{
    /// <summary>
    /// Consecutive failed sign-ins that lock the account. The <b>durable, cross-source backstop</b>, not the
    /// primary brake — see the class remarks. Set to the same 50 <see cref="User.MaxFailedLoginAttempts"/> uses,
    /// for the same reason: a level one hostile source cannot reach on its own, so naming a console account
    /// cannot lock its owner out.
    /// </summary>
    public const int MaxFailedLoginAttempts = 50;

    /// <summary>How long the account stays locked once the backstop trips. Mirrors <see cref="User.LockoutDuration"/>.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<PlatformRecoveryCode> _recoveryCodes = new();

    private PlatformAccount() { } // For EF Core

    private PlatformAccount(Guid id, string email, string fullName, string passwordHash, bool mustChangePassword)
    {
        Id = id;
        Email = EmailNormalization.Normalize(email);
        FullName = fullName.Trim();
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>The stored address, always through <see cref="EmailNormalization"/> — the unique index is on that form.</summary>
    public string Email { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// True while the account still holds the one-time password the bootstrap command printed (AC-8.1).
    ///
    /// <para>It exists because « prints a one-time password » is otherwise not true of anything: the operator
    /// reads that password to someone, and without this flag it stays a valid credential for ever.
    /// <c>PlatformAccountStateMiddleware</c> refuses every console route but the password change while it is
    /// set, which is <c>LocalAuthEnforcementMiddleware</c>'s shape for the clinic side.</para>
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// The second factor's shared secret, <b>encrypted</b> (FR-1). Null until the bootstrap command issues one;
    /// non-null with <see cref="TotpEnrolledAt"/> still null is the « secret issued, not yet confirmed » state
    /// AC-1.3a describes, and the state a password-only sign-in must refuse with nothing attached (EC-2).
    /// </summary>
    public string? ProtectedTotpSecret { get; private set; }

    /// <summary>When the account confirmed its secret with a generated code. Null ⇒ enrolment is still owed.</summary>
    public DateTime? TotpEnrolledAt { get; private set; }

    /// <summary>Stamped into every console token and compared on each request, so a session can be revoked
    /// despite the JWT being stateless (AC-1.6). Bumped by a password change, a deactivation and a factor reset.</summary>
    public int TokenVersion { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTime? LockoutEnd { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<PlatformRecoveryCode> RecoveryCodes => _recoveryCodes;

    /// <summary>The factor is bound and usable — the only state a sign-in may complete from.</summary>
    public bool IsTotpEnrolled => TotpEnrolledAt is not null && !string.IsNullOrEmpty(ProtectedTotpSecret);

    /// <summary>How many recovery codes are still spendable — the figure the recovery response reports.</summary>
    public int UnusedRecoveryCodeCount => _recoveryCodes.Count(c => !c.IsUsed);

    /// <summary>
    /// Creates a console account. Only <c>PlatformAccountProvisioning</c> calls this, and only the bootstrap
    /// verb calls that — there is no web path to account creation (AC-8.1, AC-8.5).
    /// </summary>
    public static PlatformAccount Create(string email, string fullName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("L'adresse e-mail est obligatoire.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Le nom est obligatoire.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Le mot de passe est obligatoire.", nameof(passwordHash));
        }

        // MustChangePassword from birth: the verb prints the password for an operator to relay.
        return new PlatformAccount(Guid.NewGuid(), email, fullName, passwordHash, mustChangePassword: true);
    }

    /// <summary>
    /// Issues (or re-issues) the enrolment secret, leaving it <b>unconfirmed</b>. Re-issuing clears the previous
    /// enrolment and every recovery code and bumps <see cref="TokenVersion"/> — a lost factor recovered this way
    /// (AC-8.2) must not leave the old authenticator working or old codes spendable.
    /// </summary>
    public void IssueTotpSecret(string protectedSecret)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret))
        {
            throw new ArgumentException("Le secret du second facteur est obligatoire.", nameof(protectedSecret));
        }

        ProtectedTotpSecret = protectedSecret;
        TotpEnrolledAt = null;
        _recoveryCodes.Clear();
        TokenVersion++;
        Touch();
    }

    /// <summary>
    /// Confirms the issued secret with a code the caller generated from it, and binds the recovery codes shown
    /// once in the same response (AC-1.3a). Refuses a second enrolment — that is the spec's 409.
    /// </summary>
    public void CompleteTotpEnrolment(IEnumerable<string> recoveryCodes)
    {
        if (string.IsNullOrEmpty(ProtectedTotpSecret))
        {
            throw new InvalidOperationException("Aucun secret n'a été émis pour ce compte.");
        }

        if (IsTotpEnrolled)
        {
            throw new InvalidOperationException("Le second facteur est déjà enrôlé pour ce compte.");
        }

        _recoveryCodes.Clear();
        foreach (var code in recoveryCodes)
        {
            _recoveryCodes.Add(new PlatformRecoveryCode(Id, code));
        }

        TotpEnrolledAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Spends a recovery code if it matches an unused one, and reports whether it did.
    ///
    /// <para>⚠️ <b>The caller must persist the result whether or not the sign-in completes</b> (AC-1.3b). A code
    /// presented on an attempt that then fails for another reason has still been transmitted, so treating it as
    /// unspent would let it be replayed — which is exactly what a single-use credential must not allow.</para>
    /// </summary>
    public bool ConsumeRecoveryCode(string presentedCode)
    {
        if (string.IsNullOrWhiteSpace(presentedCode))
        {
            return false;
        }

        var hash = PlatformRecoveryCode.Hash(presentedCode);
        var match = _recoveryCodes.FirstOrDefault(c => !c.IsUsed && c.CodeHash == hash);

        if (match is null)
        {
            return false;
        }

        match.Consume();
        Touch();
        return true;
    }

    /// <summary>Replaces the password and revokes every live console session (AC-8.6).</summary>
    public void SetPassword(string passwordHash, bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Le mot de passe est obligatoire.", nameof(passwordHash));
        }

        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        TokenVersion++;
        Touch();
    }

    /// <summary>
    /// Re-hashes the same password after a successful sign-in when the hasher's parameters have moved on.
    /// ⚠️ Must <b>not</b> bump <see cref="TokenVersion"/>: this runs <i>during</i> a valid sign-in, so revoking
    /// there would log the user out for using the correct password — <see cref="User.UpgradePasswordHash"/>'s
    /// reasoning unchanged.
    /// </summary>
    public void UpgradePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return;
        }

        PasswordHash = passwordHash;
        Touch();
    }

    public bool IsLockedOut() => LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;

    public void RecordSuccessfulLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        Touch();
    }

    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }

        Touch();
    }

    /// <summary>Deactivates the account and revokes its live sessions, so the refusal lands on the very next
    /// request rather than at token expiry (AC-1.6, AC-8.5).</summary>
    public void Deactivate()
    {
        IsActive = false;
        TokenVersion++;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
