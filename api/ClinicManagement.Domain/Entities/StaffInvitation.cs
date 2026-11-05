using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A colleague's first password, chosen by them through a one-time link e-mailed when the administrator creates the
/// account (<c>guided-first-days</c> C) — on the online version only; a cabinet's own PC keeps the temporary password.
///
/// <para>Built like <see cref="PasswordResetRequest"/> (the SHA-256 of a CSPRNG token, a fixed-time compare, single
/// use, re-armed in place) and kept <b>separate</b> from it on purpose: that table has a unique <c>UserId</c> whose
/// one-hour lifetime its resend cooldown is computed from, and a seven-day invitation in the same row would break
/// both.</para>
///
/// <para>⚠️ <b>A required FK to <c>Users</c>, cascade</b>, so deleting an account — or a whole cabinet through the
/// console's purge — takes its invitation with it by the model, never by a list somebody has to remember.</para>
/// </summary>
public class StaffInvitation : AggregateRoot<Guid>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>« Renvoyer l'e-mail » again within this is refused, so a loop cannot mail one address over and over.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

    public string UserId { get; private set; } = string.Empty;

    /// <summary>The user id of the administrator who sent it — the e-mail's « {Admin} vous invite ».</summary>
    public string InvitedByUserId { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the link's token, lowercase hex. The token itself exists only in the e-mail.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private StaffInvitation() { } // For EF Core

    public static StaffInvitation Create(string userId, string invitedByUserId, string tokenHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Le compte invité est obligatoire.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(invitedByUserId))
        {
            throw new ArgumentException("L'administrateur qui invite est obligatoire.", nameof(invitedByUserId));
        }

        var invitation = new StaffInvitation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InvitedByUserId = invitedByUserId,
            CreatedAtUtc = nowUtc
        };

        invitation.Rearm(tokenHash, invitedByUserId, nowUtc);
        return invitation;
    }

    /// <summary>« Renvoyer l'e-mail »: a new token and a new week; the previous link dies with its hash.</summary>
    public void Rearm(string tokenHash, string invitedByUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Le jeton d'invitation est obligatoire.", nameof(tokenHash));
        }

        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        ExpiresAtUtc = nowUtc.Add(Lifetime);
        ConsumedAtUtc = null;
    }

    public bool IsUsable(DateTime nowUtc) => ConsumedAtUtc == null && ExpiresAtUtc > nowUtc;

    /// <summary>When the current link was sent — every arm sets the expiry a full <see cref="Lifetime"/> ahead.</summary>
    public DateTime LastIssuedAtUtc() => ExpiresAtUtc - Lifetime;

    public bool WasSentWithinCooldown(DateTime nowUtc) => nowUtc - LastIssuedAtUtc() < ResendCooldown;

    public void Consume(DateTime nowUtc) => ConsumedAtUtc = nowUtc;

    /// <summary>The same hash as a password-reset link: one algorithm for every one-time account link.</summary>
    public static string HashToken(string rawToken) => PasswordResetRequest.HashToken(rawToken);

    public static bool TokenHashMatches(string storedHash, string candidateHash) =>
        PasswordResetRequest.TokenHashMatches(storedHash, candidateHash);
}
