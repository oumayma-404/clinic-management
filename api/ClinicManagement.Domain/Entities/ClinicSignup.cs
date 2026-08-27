using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A visitor's request to create a clinic on this hosted backend, held until they prove they control the email
/// address they gave. Nothing real exists until then: no <see cref="Clinic"/>, no <see cref="User"/>, no
/// <see cref="Doctor"/>, no catalogue — one row, and the verification consumes it and provisions all of that
/// through the same <c>LocalClinicProvisioning</c> the operator's <c>provision-clinic</c> verb uses.
///
/// <para><b>It carries no <c>ClinicId</c>, and that is structural rather than an omission</b>: there is no clinic
/// yet. So it falls outside the EF tenant query filter by construction and needs no entry in
/// <c>TenantScopeFilterTests</c>' clinic-owned set — which is derived from the presence of that very column.</para>
///
/// <para><b>Nothing here is a secret in plaintext.</b> <see cref="PasswordHash"/> is PBKDF2 from the moment the
/// form is submitted, and <see cref="TokenHash"/> is the SHA-256 of a token that exists nowhere but the email —
/// so a database dump yields no usable link and no password (AC-5, AC-11).</para>
/// </summary>
public class ClinicSignup : AggregateRoot<Guid>
{
    /// <summary>How long a verification link stays usable. Long enough for an email read the next morning.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    // The widths the caller must refuse *before* a row is built. They mirror ClinicSignupConfiguration, except
    // MaxEmailLength — the narrower `User.Email` (200), or a link is accepted that provisioning can never store.
    public const int MaxClinicNameLength = 200;
    public const int MaxFullNameLength = 200;
    public const int MaxEmailLength = 200;
    public const int MaxPhoneLength = 50;
    public const int MaxAddressLength = 500;
    public const int MaxCityLength = 100;
    public const int MaxDoctorInfoJsonLength = 2000;

    public string ClinicName { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    /// <summary>Lowercased and trimmed, matching <see cref="User.CreateLocalUser"/>'s own normalisation.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>PBKDF2 from submission. The plaintext password is never stored and never logged.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    /// <summary>The optional practitioner block, serialized by the caller. Null for an admin-only account.</summary>
    public string? DoctorInfoJson { get; private set; }

    /// <summary>
    /// The clinic's opening hours as collected by the onboarding wizard, in the same
    /// <c>[{ day, enabled, from, to }]</c> shape <see cref="Clinic.WorkingHoursJson"/> stores — carried verbatim and
    /// normalised at provisioning by the one serializer, never parsed here.
    ///
    /// <para><b>It is persisted rather than asked for after verification</b> because the wizard is now the signup
    /// form itself: the visitor fills all three steps in one sitting and the emailed link only confirms. A row that
    /// dropped this would silently discard the step the visitor just completed, and « Horaires » would have to be
    /// re-entered in Paramètres by somebody who had already answered it.</para>
    /// </summary>
    public string? WorkingHoursJson { get; private set; }

    /// <summary>SHA-256 (hex) of the raw token. The raw token exists only in the email that was sent.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Non-null once the token was spent. Single-use: a second attempt is refused (AC-9).</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>How many verification emails this row has been the subject of, for operator diagnosis.</summary>
    public int EmailSendAttempts { get; private set; }

    private ClinicSignup() { } // For EF Core

    public static ClinicSignup Create(
        string clinicName,
        string fullName,
        string email,
        string passwordHash,
        string tokenHash,
        DateTime nowUtc,
        string? phone = null,
        string? address = null,
        string? city = null,
        string? doctorInfoJson = null,
        string? workingHoursJson = null)
    {
        var signup = new ClinicSignup
        {
            Id = Guid.NewGuid(),
            Email = NormalizeEmail(email),
            CreatedAtUtc = nowUtc
        };

        signup.Renew(
            clinicName, fullName, passwordHash, tokenHash, nowUtc,
            phone, address, city, doctorInfoJson, workingHoursJson);
        return signup;
    }

    /// <summary>
    /// Re-arms this row with a fresh token and a fresh copy of the submitted details — what a second signup for
    /// the same address does (AC-6).
    ///
    /// <para>One row per address, deliberately: two live tokens for one email would mean the earlier one still
    /// provisions a clinic after the visitor corrected their practice's name in the second attempt. Re-arming
    /// also covers « an expired pending row is replaced » — the previous token stops working the moment its
    /// hash is overwritten.</para>
    /// </summary>
    public void Renew(
        string clinicName,
        string fullName,
        string passwordHash,
        string tokenHash,
        DateTime nowUtc,
        string? phone = null,
        string? address = null,
        string? city = null,
        string? doctorInfoJson = null,
        string? workingHoursJson = null)
    {
        if (string.IsNullOrWhiteSpace(clinicName))
        {
            throw new ArgumentException("Le nom du cabinet est obligatoire.", nameof(clinicName));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Le nom complet est obligatoire.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Le mot de passe est obligatoire.", nameof(passwordHash));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Le jeton de vérification est obligatoire.", nameof(tokenHash));
        }

        ClinicName = clinicName.Trim();
        FullName = fullName.Trim();
        PasswordHash = passwordHash;
        Phone = Blank(phone);
        Address = Blank(address);
        City = Blank(city);
        DoctorInfoJson = Blank(doctorInfoJson);
        WorkingHoursJson = Blank(workingHoursJson);
        TokenHash = tokenHash;
        ExpiresAtUtc = nowUtc.Add(TokenLifetime);
        ConsumedAtUtc = null;
    }

    /// <summary>
    /// Rotates the token on a row that is <b>still usable</b>, leaving every submitted detail alone.
    ///
    /// <para>This is what a resend must do, and <see cref="Renew"/> is not: <c>Renew</c> overwrites
    /// <see cref="PasswordHash"/>, so an anonymous second submission for an address somebody else is mid-signup on
    /// would replace their password with the sender's — and the victim's own inbox would then provision the clinic
    /// against it. The first submission for an address owns its credentials; only an expired or consumed row is
    /// re-armed wholesale.</para>
    /// </summary>
    public void Reissue(string tokenHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Le jeton de vérification est obligatoire.", nameof(tokenHash));
        }

        TokenHash = tokenHash;
        ExpiresAtUtc = nowUtc.Add(TokenLifetime);
    }

    /// <summary>
    /// When this row's current token was issued, derived from the expiry so no column has to carry it. Read as the
    /// per-recipient cooldown, so one address cannot be mailed on every request the limiter allows. A method, not
    /// a property: a get-only property with no backing field is one EF's model builder would try to map.
    /// </summary>
    public DateTime LastIssuedAtUtc() => ExpiresAtUtc - TokenLifetime;

    /// <summary>Records that a verification email is about to be sent for this row.</summary>
    public void RecordEmailSendAttempt() => EmailSendAttempts++;

    /// <summary>Is this row still worth anything at <paramref name="nowUtc"/>?</summary>
    public bool IsUsable(DateTime nowUtc) => ConsumedAtUtc == null && ExpiresAtUtc > nowUtc;

    /// <summary>
    /// Spends the token. Called for a successful provision <b>and</b> when the address has become an account
    /// since signup (AC-10) — in both cases the link has done all it will ever do, and leaving it live would
    /// let the second case be retried indefinitely.
    /// </summary>
    public void Consume(DateTime nowUtc) => ConsumedAtUtc = nowUtc;

    /// <summary>
    /// The stored form of a raw token: SHA-256, lowercase hex. Here rather than in a handler because the write
    /// side and the read side must hash identically or no link ever verifies.
    /// </summary>
    public static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

    /// <summary>
    /// Constant-time comparison of two stored hashes (AC-11).
    ///
    /// <para>⚠️ <b>Read what it does and does not buy.</b> The row reaching it came from an indexed equality
    /// lookup on this same hash, so it cannot return false and the timing-variable work (the index probe in
    /// PostgreSQL) already happened. It is kept because AC-11 requires the decision to rest on a constant-time
    /// compare and because a future lookup that stops being an equality search would need it — not because it
    /// currently hides anything a guess could measure. What actually protects the token is that only its SHA-256
    /// is stored, so a database dump yields no usable link.</para>
    /// </summary>
    public static bool TokenHashMatches(string storedHash, string candidateHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedHash),
            Encoding.UTF8.GetBytes(candidateHash));

    /// <summary>The stored form of an address — <see cref="EmailNormalization"/>'s, or the « already an account »
    /// check would answer against a different spelling from the one <c>User</c> holds.</summary>
    public static string NormalizeEmail(string email) => EmailNormalization.Normalize(email);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
