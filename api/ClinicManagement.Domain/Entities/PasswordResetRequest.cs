using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One account's live request to replace a password it can no longer supply, held until the person proves they
/// control the address the account is registered under.
///
/// <para><b>Why this exists at all.</b> Every other way back into a local account needs a second human: an
/// administrator running <c>ResetUserPasswordCommand</c>, the vendor's console, or somebody with shell access
/// running <c>reset-admin-password</c>. The recovery codes are not one — <see cref="User.ConsumeRecoveryCode"/>
/// is reached only after <c>RedeemRecoveryCodeCommand</c> has verified the password, deliberately, so a code
/// cannot be spent by a stranger guessing. That left the ordinary case — one person, one forgotten password —
/// with no path its owner could take alone.</para>
///
/// <para><b>It carries no <c>ClinicId</c>, and that is structural rather than an omission.</b> The endpoints
/// behind it are anonymous, so no tenant scope is ever established, and an <c>Unset</c> scope reads zero rows
/// with no error — indistinguishable from « no such request ». Omitting the column puts this table outside the
/// EF tenant query filter <i>by construction</i> and outside <c>TenantScopeFilterTests</c>' clinic-owned set,
/// which is derived from the presence of that very column. <see cref="ClinicSignup"/> is outside it for the same
/// mechanical reason and a different human one.</para>
///
/// <para><b>Nothing here is a secret in plaintext.</b> <see cref="TokenHash"/> is the SHA-256 of a token that
/// exists nowhere but the email that was sent, so a database dump yields no usable link. The row holds no
/// password at all, in either form — which is what lets <see cref="Rearm"/> be simpler than
/// <c>ClinicSignup.Reissue</c>; see that method.</para>
/// </summary>
public class PasswordResetRequest : AggregateRoot<Guid>
{
    /// <summary>
    /// How long a reset link stays usable.
    ///
    /// <para><b>One hour, against <see cref="ClinicSignup.TokenLifetime"/>'s twenty-four</b>, and the asymmetry is
    /// the point: a signup link creates something that does not exist yet, while this one replaces the credential
    /// of an account already holding patient records. The person is at their keyboard waiting for it, so an hour
    /// covers a mail queue and a walk to another room without leaving a live key to a real account sitting in an
    /// inbox overnight.</para>
    /// </summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    /// <summary>Mirrors <c>User.Email</c>'s own width, or a request could be accepted for an address no account
    /// can hold.</summary>
    public const int MaxEmailLength = 200;

    /// <summary>
    /// The account this request will re-credential. <c>User.Id</c> is a string (an Auth0 sub, or
    /// <c>local|{guid}</c>), so this is too.
    /// </summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>
    /// The address the link was sent to, normalised by <see cref="EmailNormalization"/> — the same spelling
    /// <c>User</c> holds, or the lookup that finds this row would answer against a different one.
    ///
    /// <para>Stored even though <see cref="UserId"/> already identifies the account, because it records <i>where
    /// the link actually went</i>. An address changed after the request was raised would otherwise make « which
    /// mailbox was trusted here? » unanswerable.</para>
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the raw token. The raw token exists only in the email that was sent.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Non-null once the token was spent. Single-use: a second attempt with the same link is refused.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>How many reset emails this row has been the subject of, for operator diagnosis.</summary>
    public int EmailSendAttempts { get; private set; }

    private PasswordResetRequest() { } // For EF Core

    public static PasswordResetRequest Create(string userId, string email, string tokenHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Le compte cible est obligatoire.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("L'adresse e-mail est obligatoire.", nameof(email));
        }

        var request = new PasswordResetRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = NormalizeEmail(email),
            CreatedAtUtc = nowUtc
        };

        request.Rearm(tokenHash, nowUtc);
        return request;
    }

    /// <summary>
    /// Re-arms this row with a fresh token, whatever state it was in — what a second « j'ai oublié mon mot de
    /// passe » for the same account does.
    ///
    /// <para><b>One row per account, and re-arming is unconditional — unlike <see cref="ClinicSignup"/>, which
    /// needed two methods to avoid one.</b> That entity carries the visitor's <c>PasswordHash</c>, so an
    /// anonymous second submission for an address somebody else was mid-signup on would have replaced their
    /// password with the sender's. This row carries no credential of any kind: the new password is chosen at the
    /// end of the flow by whoever holds the token, so re-arming can only ever <i>invalidate the previous link</i>
    /// and hand the next one to the same mailbox. There is nothing here for a stranger's request to overwrite.</para>
    ///
    /// <para>⚠️ It clears <see cref="ConsumedAtUtc"/> deliberately: a spent row must be reusable, or a person who
    /// resets their password once could never do it again. Single-use is a property of a <i>token</i>, and the
    /// token is what this replaces.</para>
    /// </summary>
    public void Rearm(string tokenHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Le jeton de réinitialisation est obligatoire.", nameof(tokenHash));
        }

        TokenHash = tokenHash;
        ExpiresAtUtc = nowUtc.Add(TokenLifetime);
        ConsumedAtUtc = null;
    }

    /// <summary>
    /// When this row's current token was issued, derived from the expiry so no column has to carry it. Read as the
    /// per-account cooldown, so one address cannot be mailed on every request the rate limiter allows. A method,
    /// not a property: a get-only property with no backing field is one EF's model builder would try to map.
    /// </summary>
    public DateTime LastIssuedAtUtc() => ExpiresAtUtc - TokenLifetime;

    /// <summary>Records that a reset email is about to be sent for this row.</summary>
    public void RecordEmailSendAttempt() => EmailSendAttempts++;

    /// <summary>Is this row's token still worth anything at <paramref name="nowUtc"/>?</summary>
    public bool IsUsable(DateTime nowUtc) => ConsumedAtUtc == null && ExpiresAtUtc > nowUtc;

    /// <summary>
    /// Spends the token.
    ///
    /// <para>⚠️ <b>The row is kept, not deleted.</b> Its retention is what lets the opportunistic purge report an
    /// honest « nothing outstanding », and what stops a replayed link reading as « unknown token » when it is in
    /// fact « already used ». The distinction never reaches the user — both refusals are the same French sentence,
    /// so a link cannot be probed for whether it once worked — but it does reach the operator's logs.</para>
    /// </summary>
    public void Consume(DateTime nowUtc) => ConsumedAtUtc = nowUtc;

    /// <summary>
    /// The stored form of a raw token: SHA-256, lowercase hex. Here rather than in a handler because the write
    /// side and the read side must hash identically or no link ever verifies.
    /// </summary>
    public static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

    /// <summary>
    /// Constant-time comparison of two stored hashes.
    ///
    /// <para>⚠️ <b>Read what it does and does not buy</b> — <see cref="ClinicSignup.TokenHashMatches"/> states it
    /// in full, and this is the same situation: the row reaching it came from an indexed equality lookup on this
    /// same hash, so the timing-variable work already happened in PostgreSQL. It is here so the decision rests on
    /// a constant-time compare rather than on <c>==</c>, and so a future lookup that stops being an equality
    /// search does not quietly become a measurable one. What actually protects the token is that only its SHA-256
    /// is stored.</para>
    /// </summary>
    public static bool TokenHashMatches(string storedHash, string candidateHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedHash),
            Encoding.UTF8.GetBytes(candidateHash));

    /// <summary>The stored form of an address — <see cref="EmailNormalization"/>'s, the same one <c>User</c>
    /// uses.</summary>
    public static string NormalizeEmail(string email) => EmailNormalization.Normalize(email);
}
