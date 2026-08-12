using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One single-use recovery code for a clinic <see cref="User"/>'s second factor
/// (<c>hosted-security-hardening</c> FR-1.4).
///
/// <para><b>Stored so it can be checked, never read back</b> — a SHA-256 and nothing else, so a database copy
/// yields no usable credential. The codes are shown exactly once, at enrolment.</para>
///
/// <para>⚠️ <b>This is <see cref="PlatformRecoveryCode"/>'s twin, deliberately copied rather than shared.</b>
/// Two reasons, and the second is structural. (1) The numbers — alphabet, length, count per enrolment — are a
/// <i>policy decision per population</i>, and a shared base would make changing the vendor's console codes
/// silently change every clinic's. (2) The FK shapes differ and cannot be unified: <see cref="User"/> is keyed by
/// <c>string</c> (an Auth0 <c>sub</c> or <c>local|{guid}</c>) while <see cref="PlatformAccount"/> is keyed by
/// <c>Guid</c>, so a common base would need a generic parameter threaded through EF's configuration for no gain.
/// `PlatformAccount`'s own class note already records this reasoning for the account pair.</para>
///
/// <para>⚠️ <b>A plain SHA-256 rather than PBKDF2</b>, for the reason its twin states: iterated hashing exists to
/// make a low-entropy human-chosen secret expensive to guess, and this is 20 characters of
/// <see cref="RandomNumberGenerator"/> over a 32-symbol alphabet — 100 bits, nothing to brute-force. It must also
/// be <b>deterministic</b>, because the aggregate compares a presented code against its own rows, and a per-row
/// salt would drag a password hasher into Domain, which references nothing.</para>
///
/// <para>⚠️ <b>Consumed, not deleted.</b> « Ce code a déjà servi » and « ce code n'existe pas » are the same
/// refusal to the caller and different facts to whoever reads the account afterwards.</para>
/// </summary>
public class UserRecoveryCode : Entity<Guid>
{
    /// <summary>No <c>0</c>/<c>O</c>/<c>1</c>/<c>I</c> — these are printed and typed back months later.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>20 × log2(32) = 100 bits, which is what makes the plain hash above sound.</summary>
    public const int Length = 20;

    /// <summary>How many codes an enrolment issues. Enough that losing a phone is survivable more than once.</summary>
    public const int CountPerEnrolment = 8;

    private UserRecoveryCode() { } // For EF Core

    public UserRecoveryCode(string userId, string code)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Un code de récupération doit appartenir à un compte.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Un code de récupération ne peut pas être vide.", nameof(code));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        CodeHash = Hash(code);
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>The owning account. A <c>string</c>, because <see cref="User"/>'s key is.</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Hex SHA-256 of the normalised code. The code itself exists only in the enrolment response.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    public bool IsUsed { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public User User { get; private set; } = null!;

    /// <summary>
    /// The stored form: upper-cased and stripped of separators before hashing, so a code written on paper as
    /// <c>abcd efgh</c> still matches. Called by the ctor <i>and</i> by the comparison, which is what keeps
    /// « how it was stored » and « how it is checked » one answer.
    /// </summary>
    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    /// <summary>
    /// A fresh code. Beside <see cref="Alphabet"/>, <see cref="Length"/> and <see cref="Hash"/> so « how one is
    /// minted » and « how one is stored » cannot drift into two files — the entropy claim above is only
    /// checkable with both halves in view.
    /// </summary>
    public static string NewCode()
    {
        var chars = new char[Length];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Upper-case, spaces and dashes removed. Public so the presentation layer can format freely.</summary>
    public static string Normalize(string code) =>
        code.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Trim()
            .ToUpperInvariant();

    /// <summary>
    /// Marks this code spent. A second call <b>throws</b>: « consume » is the whole guarantee, and succeeding
    /// twice would make a used code reusable.
    /// </summary>
    internal void Consume()
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Ce code de récupération a déjà été utilisé.");
        }

        IsUsed = true;
        UsedAt = DateTime.UtcNow;
    }
}
