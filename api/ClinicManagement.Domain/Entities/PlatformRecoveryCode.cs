using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One single-use recovery code for a <see cref="PlatformAccount"/>'s second factor (FR-1, AC-8.2).
///
/// <para><b>Stored so it can be checked, never read back.</b> The row holds a SHA-256 of the code and nothing
/// else, so a database copy yields no usable credential — which is the same promise the password hash makes and
/// the reason the codes are shown exactly once, at enrolment.</para>
///
/// <para>⚠️ <b>A plain SHA-256 rather than PBKDF2, deliberately.</b> Iterated hashing exists to make a
/// <i>low-entropy human-chosen</i> secret expensive to guess. A recovery code here is 20 characters from a
/// 32-symbol alphabet minted by <see cref="RandomNumberGenerator"/> — 100 bits — so there is nothing to
/// brute-force and the iteration count would buy nothing. It also has to be a <b>deterministic</b> hash: the
/// aggregate itself compares a presented code against its rows (<see cref="PlatformAccount.ConsumeRecoveryCode"/>),
/// and a per-row salt would drag a password hasher into the Domain, which references nothing.</para>
///
/// <para>⚠️ <b>Consumed, not deleted</b> (AC-1.3b). « Cette code a déjà servi » and « ce code n'existe pas » are
/// the same refusal to the caller, but they are different facts to whoever is reading the account afterwards —
/// and a deleted row cannot say a code was ever spent.</para>
/// </summary>
public class PlatformRecoveryCode : Entity<Guid>
{
    /// <summary>The alphabet recovery codes are minted from — no <c>0</c>/<c>O</c>/<c>1</c>/<c>I</c>, because an
    /// operator reads these onto paper and types them back months later.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Characters per code. 20 × log2(32) = 100 bits, which is what makes the plain hash above sound.</summary>
    public const int Length = 20;

    /// <summary>How many codes an enrolment issues. Enough that losing a phone is survivable more than once.</summary>
    public const int CountPerEnrolment = 8;

    private PlatformRecoveryCode() { } // For EF Core

    public PlatformRecoveryCode(Guid platformAccountId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Un code de récupération ne peut pas être vide.", nameof(code));
        }

        Id = Guid.NewGuid();
        PlatformAccountId = platformAccountId;
        CodeHash = Hash(code);
        CreatedAt = DateTime.UtcNow;
    }

    public Guid PlatformAccountId { get; private set; }

    /// <summary>Hex SHA-256 of the normalised code. The code itself exists only in the enrolment response.</summary>
    public string CodeHash { get; private set; } = string.Empty;

    public bool IsUsed { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public PlatformAccount Account { get; private set; } = null!;

    /// <summary>
    /// The stored form of a recovery code: upper-cased and stripped of separators before hashing, so a code
    /// read aloud and typed back as <c>abcd efgh</c> still matches. Called by the ctor and by the comparison,
    /// which is what keeps « how it was stored » and « how it is checked » one answer.
    /// </summary>
    public static string Hash(string code)
    {
        var normalised = Normalize(code);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    /// <summary>
    /// A fresh code. Lives here beside <see cref="Alphabet"/>, <see cref="Length"/> and <see cref="Hash"/> so
    /// « how one is minted » and « how one is stored » cannot drift into two files — the entropy claim in the
    /// class remarks is only checkable if both halves are in view.
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

    /// <summary>Upper-case, with spaces and dashes removed. Public so the presentation layer can format freely.</summary>
    public static string Normalize(string code) =>
        code.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Trim()
            .ToUpperInvariant();

    /// <summary>
    /// Marks this code spent. Idempotent-hostile on purpose: a second call throws, because « consume » is the
    /// whole guarantee and silently succeeding twice would make a used code reusable.
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
