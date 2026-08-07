using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class User : AggregateRoot<string> // Using Auth0 sub as ID (Cloud) or "local|{guid}" (Local mode)
{
    // Consecutive failed logins that trigger a temporary lockout (Local mode, AC-3.4).
    //
    // This is the DURABLE, CROSS-SOURCE BACKSTOP, not the primary brake. It used to be 5, which made the
    // account itself the unit of lockout — so anyone who could reach the login endpoint could burn five
    // attempts against every staff account in turn and keep the whole clinic, admin included, locked out
    // indefinitely (audit section 2, finding 5). The primary brake is now per (account, source) via
    // ILoginAttemptTracker at 5 attempts, so a hostile host locks out only itself.
    //
    // This counter is therefore raised to a level a single source cannot reach on its own — it exists to stop
    // a genuinely DISTRIBUTED guessing attack, and to survive the restart that clears the in-memory
    // per-source counters. Reaching it requires several distinct sources or a long sustained effort, both of
    // which the per-IP rate limiter also throttles.
    public const int MaxFailedLoginAttempts = 50;
    // How long an account stays locked after too many failed attempts.
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const string RoleAdmin = "admin";
    public const string RoleDoctor = "doctor";
    public const string RoleSecretary = "secretary";

    /// <summary>
    /// The closed set of roles a clinic user may hold — the single authority for "is this a real role".
    /// <para>
    /// Until now the set existed only as a comment on <see cref="Role"/> and as three literals repeated across
    /// the authorization policies and the two self-registration commands, so nothing validated a role coming
    /// from a request (audit adjacent defect A-11): any string, empty included, was accepted and the account
    /// silently matched no policy at all.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> AssignableRoles = new[] { RoleAdmin, RoleDoctor, RoleSecretary };

    public Guid ClinicId { get; private set; }
    public string Role { get; private set; } // one of AssignableRoles
    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Local-mode credential fields. All are inert in Cloud mode (PasswordHash stays null).
    public string? PasswordHash { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// Stamped into every issued token and compared on each request, so a token can be revoked even though
    /// the JWT itself is stateless (security-hardening US-5 / AC-5.1).
    ///
    /// <para>Before this, a <b>voluntary</b> password change left every existing token valid for its full
    /// remaining lifetime — if a token had been stolen, the user's natural reaction (change my password) did
    /// nothing. Bumping this invalidates them immediately.</para>
    ///
    /// <para>Bumped by <see cref="SetPassword"/> (which every password path funnels through: voluntary change,
    /// admin reset, and the offline CLI recovery) and by <see cref="Deactivate"/>. Deliberately <b>not</b>
    /// bumped by <see cref="UpgradePasswordHash"/> — see that method.</para>
    /// </summary>
    public int TokenVersion { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockoutEnd { get; private set; }

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;

    private User() { } // For EF Core

    public User(
        string id, // Auth0 sub
        Guid clinicId,
        string role,
        string? email = null,
        string? fullName = null)
    {
        Id = id;
        ClinicId = clinicId;
        Role = role;
        Email = email;
        FullName = fullName;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Factory for a Local-mode account (offline email + password). Mints a stable
    /// <c>local|{guid}</c> id so it never collides with an Auth0 <c>sub</c>.
    /// </summary>
    public static User CreateLocalUser(
        Guid clinicId,
        string role,
        string email,
        string passwordHash,
        string fullName,
        bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required for a local user.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required for a local user.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required for a local user.", nameof(fullName));

        var user = new User($"local|{Guid.NewGuid()}", clinicId, role, EmailNormalization.Normalize(email), fullName.Trim())
        {
            PasswordHash = passwordHash,
            IsActive = true,
            MustChangePassword = mustChangePassword
        };
        return user;
    }

    /// <summary>
    /// Factory for a <b>self-registered</b> Local account — someone who typed the clinic code into
    /// <c>POST /api/auth/register</c>. Identical to <see cref="CreateLocalUser"/> except that the account is
    /// created <b>inactive</b>, so it exists but cannot log in until an admin activates it from « Utilisateurs ».
    ///
    /// <para><b>Why a separate factory and not an <c>isActive: false</c> argument.</b> The two paths that mint a
    /// local account are first-run <c>setup</c> (the owner, who must be able to log in immediately — a pending
    /// first admin would lock the clinic out of itself before it had anyone to approve them) and this one. A
    /// defaulted boolean would put the security-relevant difference in a parameter a caller can omit; a named
    /// factory puts it in the call site's own text.</para>
    ///
    /// <para><b>Why this was needed.</b> The clinic code is a <b>6-character</b> secret over a 36-symbol
    /// alphabet, shown on a settings screen and known to everyone who ever worked at the practice — and it was
    /// the <em>only</em> thing between a stranger and a working account that reads every patient record.
    /// Rotating the code (which the product supports) does not retract an account already minted with it.
    /// Approval is the missing step: the code decides which clinic you are asking to join, an admin decides
    /// whether you join it.</para>
    /// </summary>
    public static User CreateSelfRegistered(
        Guid clinicId,
        string role,
        string email,
        string passwordHash,
        string fullName)
    {
        var user = CreateLocalUser(clinicId, role, email, passwordHash, fullName);
        user.IsActive = false;
        return user;
    }

    /// <summary>
    /// True when this account has never been able to log in — it is <b>awaiting an admin's approval</b> rather
    /// than having been switched off. Read by « Utilisateurs » so the badge can say « En attente d'activation »
    /// instead of « Désactivé », and to count how many people are waiting.
    ///
    /// <para>Derived rather than stored, so I5 needs no column on this table: an account that is inactive
    /// <em>and</em> has never recorded a login is, by construction, one nobody has let in yet. A staff member
    /// who has logged in before and is now inactive was deactivated on purpose, and reads as such. The one
    /// blurred case — an admin deactivating an account before its owner's first login — resolves in the honest
    /// direction: it still cannot log in, and « Activer » is still the button that fixes it.</para>
    /// </summary>
    public bool IsPendingActivation => !IsActive && LastLoginAt == null;

    /// <summary>
    /// Move this account to another clinic role.
    /// <para>
    /// Replaces the former <c>Update(string role, string? email = null, string? fullName = null)</c>, which had
    /// **no caller** and was a live trap: the one-argument call every role-change site would naturally have made
    /// assigned the two defaulted nulls, silently wiping the user's email and full name (adjacent defect A-11).
    /// A role change touches the role and nothing else.
    /// </para>
    /// <para>
    /// Returns <c>false</c> when the account already holds that role, so the caller can distinguish a no-op
    /// from a real change rather than bumping <see cref="TokenVersion"/> — and logging the user out — for a
    /// re-selection of the role they already had.
    /// </para>
    /// </summary>
    public bool ChangeRole(string role)
    {
        var normalized = NormalizeRole(role)
            ?? throw new ArgumentException(
                "Rôle invalide. Les rôles autorisés sont : admin, doctor, secretary.", nameof(role));

        if (Role.Equals(normalized, StringComparison.Ordinal))
        {
            return false;
        }

        Role = normalized;
        // The JWT is stateless and carries the role, so without this the OLD role stays live for the token's
        // whole remaining lifetime — a demoted admin keeps every admin surface until it expires, and a promoted
        // one cannot reach any of them (AC-P2.27). Same reason SetPassword/Deactivate bump it.
        TokenVersion++;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// The canonical spelling of <paramref name="role"/>, or <c>null</c> when it is not a real role. Callers that
    /// must reject rather than throw (request validation) use this; <see cref="ChangeRole"/> throws.
    /// </summary>
    public static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var trimmed = role.Trim();
        return AssignableRoles.FirstOrDefault(r => r.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Promote this user to the clinic "admin" role, preserving email/full name (Cloud admin backfill).</summary>
    public void PromoteToAdmin()
    {
        Role = RoleAdmin;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>True for accounts that authenticate with a local email+password (Local mode).</summary>
    public bool IsLocalAccount() => PasswordHash != null;

    public bool IsLockedOut() => LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;

    /// <summary>Sets a new password hash and (optionally) forces a change at next login.</summary>
    public void SetPassword(string passwordHash, bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        // AC-5.2: every existing token for this account stops working now. This is the single choke point
        // all four password paths go through — voluntary change, admin reset, offline CLI recovery, first set.
        TokenVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-stores the password under an upgraded hash format after a successful verification,
    /// without altering the forced-change flag or lockout state (unlike <see cref="SetPassword"/>).
    ///
    /// <para><b>Must not bump <see cref="TokenVersion"/>.</b> This runs <i>during a successful login</i>, so
    /// bumping here would invalidate the token that same login is about to issue — logging the user straight
    /// back out, on every sign-in whose stored hash needs upgrading. The password has not changed; only its
    /// storage format has, so no session should be revoked (AC-5.11).</para>
    /// </summary>
    public void UpgradePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a successful login: clears lockout state and stamps the login time.</summary>
    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a failed login; locks the account once the attempt threshold is crossed.</summary>
    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        // AC-5.2: revoke the account's tokens outright rather than relying solely on the per-request
        // IsActive check, so the account is dead even on a path that does not reload it.
        TokenVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearPasswordChangeRequirement()
    {
        MustChangePassword = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsDoctor() => Role.Equals(RoleDoctor, StringComparison.OrdinalIgnoreCase);
    public bool IsSecretary() => Role.Equals(RoleSecretary, StringComparison.OrdinalIgnoreCase);
    public bool IsAdmin() => Role.Equals(RoleAdmin, StringComparison.OrdinalIgnoreCase);
}
