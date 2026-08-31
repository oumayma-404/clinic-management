using System;
using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// A device grant stops authorising once nothing has used it for a long time.
///
/// <para><b>What this closes.</b> <c>IsUsable</c> was <c>RevokedAtUtc == null || RevokedAtUtc &gt; nowUtc</c> and
/// nothing else — there was no expiry of any kind. Exchanging this secret mints an ordinary clinic
/// <b>administrator</b> access token with the whole API surface, so the secret sitting on a practice's Windows PC
/// was a <b>permanent</b> admin credential: anything that read that disk once owned the cabinet until somebody
/// remembered to revoke by hand, and nobody revokes a credential they have forgotten exists.</para>
///
/// <para>⚠️ <b>Most of these cases assert that a working installation is NOT interrupted</b>, and that is where
/// the risk actually sits. The window runs from the last <i>use</i>, so the poste that copies renews itself by
/// working; an absolute lifetime would stop a healthy practice's unattended copy on a date nobody chose, which is
/// precisely the silent-backup-failure `clinic-recovery-points` exists to prevent. Getting this wrong in the
/// tight direction is worse than the hole it closes.</para>
/// </summary>
public class ClinicArchiveGrantExpiryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Created = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ClinicArchiveGrant AGrant() =>
        ClinicArchiveGrant.Create(ClinicId, "Poste de l'accueil", ClinicArchiveGrant.NewSecret().Hash, "user-1", Created);

    [Fact]
    public void A_fresh_grant_authorises()
    {
        Assert.True(AGrant().IsUsable(Created.AddDays(1)));
    }

    // The case the whole design turns on: a poste that copies every night must never be interrupted, however
    // long the practice keeps using it.
    [Fact]
    public void A_grant_in_regular_use_never_expires()
    {
        var grant = AGrant();
        var now = Created;

        // Two years of nightly copies.
        for (var day = 0; day < 730; day++)
        {
            now = now.AddDays(1);
            Assert.True(grant.IsUsable(now), $"refused on day {day} of ordinary use");
            grant.MarkUsed(now);
        }
    }

    // A weekly cadence — the slowest the shell offers — must also stay comfortably inside the window.
    [Fact]
    public void A_weekly_cadence_is_comfortably_inside_the_window()
    {
        var grant = AGrant();
        var now = Created;

        for (var week = 0; week < 52; week++)
        {
            now = now.AddDays(7);
            Assert.True(grant.IsUsable(now), $"refused on week {week} of ordinary use");
            grant.MarkUsed(now);
        }
    }

    // THE case this exists for: a machine that stopped presenting the secret — decommissioned, sold, or a copy
    // of the file taken off a disk — goes dead on its own.
    [Fact]
    public void A_grant_nothing_has_used_for_ninety_days_stops_authorising()
    {
        var grant = AGrant();

        Assert.True(grant.IsUsable(Created + ClinicArchiveGrant.IdleLifetime.Subtract(TimeSpan.FromMinutes(1))));
        Assert.False(grant.IsUsable(Created + ClinicArchiveGrant.IdleLifetime.Add(TimeSpan.FromMinutes(1))));
    }

    // The window runs from the last USE, not from creation — otherwise a grant issued a year ago and used this
    // morning would be refused, which is the interruption this design exists to avoid.
    [Fact]
    public void The_window_runs_from_the_last_use_not_from_creation()
    {
        var grant = AGrant();
        var muchLater = Created.AddYears(1);

        Assert.False(grant.IsUsable(muchLater));

        grant.MarkUsed(muchLater);

        Assert.True(grant.IsUsable(muchLater.AddDays(1)));
    }

    // Revocation still wins, and still wins immediately — expiry is an addition to that rule, not a replacement.
    [Fact]
    public void Revocation_still_ends_a_grant_that_is_otherwise_fresh()
    {
        var grant = AGrant();
        grant.Revoke(Created.AddDays(1));

        Assert.False(grant.IsUsable(Created.AddDays(2)));
    }

    [Fact]
    public void The_expiry_it_reports_matches_the_rule_it_enforces()
    {
        var grant = AGrant();

        Assert.Equal(Created + ClinicArchiveGrant.IdleLifetime, grant.ExpiresAtUtc);

        grant.MarkUsed(Created.AddDays(10));

        Assert.Equal(Created.AddDays(10) + ClinicArchiveGrant.IdleLifetime, grant.ExpiresAtUtc);
    }
}
