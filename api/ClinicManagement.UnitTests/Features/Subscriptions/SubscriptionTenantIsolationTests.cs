using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// Tenant isolation for the entitlement and its ledger — the per-feature layer every clinic-scoped feature here
/// carries.
///
/// <para><b>What this holds that <c>TenantScopeFilterTests</c> does not.</b> That guard is derived over the whole
/// model and would have covered these two tables the moment they were configured, which is exactly why it must not
/// be edited — but it cannot say <i>which</i> tables those are, so a filter silently dropped from one of them
/// would leave it green over the twenty that remain. These two are named here, and the SQL they generate is
/// asserted directly.</para>
///
/// <para>⚠️ <b>The reason this matters more here than for most tables.</b> The gate reads the entitlement on the
/// write path of <b>every</b> request in the hosted deployment. An unfiltered read there would be the widest
/// cross-clinic read in the product, and it would present not as an error but as one practice being granted or
/// refused writes on the strength of another's payment.</para>
/// </summary>
public class SubscriptionTenantIsolationTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class Scope : ICurrentClinicProvider
    {
        public bool IsSystemWide { get; init; }
        public Guid? ClinicId { get; init; }
    }

    /// <summary>Never connected to — <c>ToQueryString()</c> only needs the provider configured, not reachable.</summary>
    private static ApplicationDbContext Context(ICurrentClinicProvider? provider) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options, provider);

    private static Guid ScopedClinicIn(string sql)
    {
        var match = Regex.Match(sql, @"@__ef_filter__ScopedClinicId_\d+='(?<v>[0-9a-fA-F-]{36})'");
        Assert.True(match.Success, $"The clinic filter's parameter is not in the generated SQL:\n{sql}");
        return Guid.Parse(match.Groups["v"].Value);
    }

    [Fact]
    public void Both_Entitlement_Tables_Are_Scoped_To_The_Callers_Clinic()
    {
        using var db = Context(new Scope { IsSystemWide = false, ClinicId = ClinicA });

        Assert.Equal(ClinicA, ScopedClinicIn(db.ClinicSubscriptions.ToQueryString()));
        Assert.Equal(ClinicA, ScopedClinicIn(db.SubscriptionPeriods.ToQueryString()));
    }

    // The US-2 inversion, on these two tables specifically: a path that established no scope reads NOTHING rather
    // than every cabinet's entitlement. Here that is the difference between a forgotten scope refusing writes and
    // a forgotten scope handing them out.
    [Fact]
    public void An_Unset_Scope_Reads_No_Entitlement_At_All()
    {
        using var db = Context(new Scope { IsSystemWide = false, ClinicId = null });

        Assert.Equal(Guid.Empty, ScopedClinicIn(db.ClinicSubscriptions.ToQueryString()));
        Assert.Equal(Guid.Empty, ScopedClinicIn(db.SubscriptionPeriods.ToQueryString()));
    }

    // The vendor's verbs and the warning job legitimately read every cabinet — and must SAY SO. This is the only
    // state that lifts the filter.
    [Fact]
    public void Only_A_System_Wide_Scope_Reads_Every_Cabinets_Entitlement()
    {
        using var db = Context(new Scope { IsSystemWide = true, ClinicId = null });

        Assert.Contains("ef_filter__IsSystemWide", db.ClinicSubscriptions.ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("ef_filter__IsSystemWide", db.SubscriptionPeriods.ToQueryString(), StringComparison.Ordinal);
    }

    // The invariant the filters rest on: the rows a door stages carry the CABINET's id, never the caller's. The
    // filter compares that column to the scoped clinic, so a row with the wrong id is either invisible to its own
    // practice or visible to another — and both failures are silent.
    [Fact]
    public void Provisioning_Stamps_The_Cabinets_Own_Id_On_Both_Rows()
    {
        var forA = SubscriptionProvisioning.CreateForNewClinic(
            ClinicA, requiresSubscription: true, new DateTime(2026, 8, 10), trialDays: 30,
            new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));
        var forB = SubscriptionProvisioning.CreateForNewClinic(
            ClinicB, requiresSubscription: true, new DateTime(2026, 8, 10), trialDays: 30,
            new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(ClinicA, forA.Subscription.ClinicId);
        Assert.Equal(ClinicA, forA.OpeningEntry.ClinicId);
        Assert.Equal(ClinicB, forB.Subscription.ClinicId);
        Assert.Equal(ClinicB, forB.OpeningEntry.ClinicId);
    }

    // A fold must never mix cabinets. `RecomputeFrom` is the one write path to EndsOn, so a ledger handed to the
    // wrong entitlement would set one practice's date from another's payments — refused loudly rather than folded.
    [Fact]
    public void An_Entitlement_Refuses_To_Fold_Another_Cabinets_Ledger()
    {
        var forA = SubscriptionProvisioning.CreateForNewClinic(
            ClinicA, requiresSubscription: true, new DateTime(2026, 8, 10), trialDays: 30,
            new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));
        var forB = SubscriptionProvisioning.CreateForNewClinic(
            ClinicB, requiresSubscription: true, new DateTime(2026, 8, 10), trialDays: 30,
            new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            forA.Subscription.RecomputeFrom(new[] { forA.OpeningEntry, forB.OpeningEntry }));

        Assert.Contains("autre cabinet", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The clinic-owned column is what enrols these tables in the derived guard, so its absence would be the silent
    /// way out of every assertion above — <c>TenantScopeFilterTests</c> derives « clinic-owned » from this very
    /// property, and a table without it is simply never asked about.
    /// </summary>
    [Fact]
    public void Both_Tables_Carry_A_Clinic_Column_So_The_Derived_Guard_Sees_Them()
    {
        using var db = Context(null);

        Assert.NotNull(db.Model.FindEntityType(typeof(ClinicSubscription))!.FindProperty("ClinicId"));
        Assert.NotNull(db.Model.FindEntityType(typeof(SubscriptionPeriod))!.FindProperty("ClinicId"));
    }
}
