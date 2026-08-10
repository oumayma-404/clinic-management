using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The EF Core global query filter under each of the three tenant-scope states (multi-tenant-cloud US-2) — the
/// change that turns a fail-open backstop into an isolation layer.
///
/// <para><b>Why it asserts SQL and not rows.</b> Nothing in this project touches a database and no in-memory
/// provider is referenced, so <c>ToQueryString()</c> is the only way to see what the filter actually does — the
/// same technique, and the same reason, as <c>RecallQueryTranslationTests</c>. It is enough: Npgsql prints the
/// filter's two parameter values above the statement, so « refuses », « scopes » and « lifts » are each directly
/// observable. No connection is opened.</para>
///
/// <para><b>Every case is derived from the model, never from a list of entity names.</b> The filtered set is read
/// off <c>db.Model</c>, so a 22nd clinic-owned root is covered the day it is configured. The one hand-written
/// list here is <see cref="UnfilteredByDesign"/> — the clinic-owned tables that are deliberately <i>not</i>
/// filtered — and it is asserted to be exactly right in both directions: a new unfiltered root fails, and so does
/// an exemption for a table that has since been filtered. The count is left to the dictionary rather than repeated
/// in prose, which is how this said « three » over four entries.</para>
/// </summary>
public class TenantScopeFilterTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>
    /// Clinic-owned tables with no query filter. Each entry is a decision, not an omission — the guard below
    /// fails if this stops matching the model exactly.
    /// </summary>
    private static readonly Dictionary<string, string> UnfilteredByDesign = new()
    {
        ["User"] = "auth, setup and join resolve a user before any clinic is in scope; filtering it breaks onboarding",
        ["Clinic"] = "same — a clinic is looked up by code before the caller belongs to it",
        ["AuditEntry"] = "ClinicId is nullable (a job or verb mutates rows with no clinic), so a filter would hide "
                         + "exactly the unattributed rows an owner needs; GetAuditEntriesQuery filters explicitly",
        // ⚠️ The reason is the NULLABLE ClinicId, not the cross-clinic dispatcher: DocumentEmail is filtered and its
        // dispatcher declares UseSystemWide too, so « drained cross-clinic » cannot be what exempts a table.
        ["Notification"] = "ClinicId is nullable (legacy and recall rows), so a filter would hide exactly the "
                           + "unattributed rows; all four reachable reads take a clinicId explicitly",
        // platform-console Part 2. These two are the VENDOR's measurements ABOUT a cabinet, not the cabinet's own
        // data: written by ClinicActivityCounterJob and read only by the console, both of which are cross-cabinet
        // by definition and declare UseSystemWide. No clinic-facing surface reads them at all, so a per-clinic
        // filter would guard a door nobody uses — while making the one legitimate reader depend on lifting it.
        ["ClinicActivityDay"] = "vendor-console counters: no clinic-facing read exists, and both writers and the "
                                + "only reader are cross-cabinet by construction",
        ["ClinicActivitySnapshot"] = "same — and the portfolio LEFT JOINs it across every cabinet, which is the "
                                     + "read the console exists to serve"
    };

    private sealed class Scope : ICurrentClinicProvider
    {
        public bool IsSystemWide { get; init; }
        public Guid? ClinicId { get; init; }
    }

    private static ApplicationDbContext Context(ICurrentClinicProvider? provider) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options, provider);

    private static ApplicationDbContext Unset() => Context(new Scope { IsSystemWide = false, ClinicId = null });

    private static IReadOnlyList<Type> FilteredRoots(ApplicationDbContext db) =>
        db.Model.GetEntityTypes()
            .Where(e => e.GetQueryFilter() is not null)
            .Select(e => e.ClrType)
            .Distinct()
            .OrderBy(t => t.Name)
            .ToList();

    private static string SqlFor(ApplicationDbContext db, Type clrType)
    {
        var set = typeof(DbContext).GetMethods()
            .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0)
            .MakeGenericMethod(clrType)
            .Invoke(db, null)!;

        return ((IQueryable)set).ToQueryString();
    }

    private static bool SystemWideParameter(string sql) =>
        bool.Parse(Match(sql, @"@__ef_filter__IsSystemWide_\d+='(?<v>True|False)'"));

    private static Guid ScopedClinicParameter(string sql) =>
        Guid.Parse(Match(sql, @"@__ef_filter__ScopedClinicId_\d+='(?<v>[0-9a-fA-F-]{36})'"));

    private static string Match(string sql, string pattern)
    {
        var match = Regex.Match(sql, pattern);
        Assert.True(match.Success, $"The filter's parameters are not in the generated SQL:\n{sql}");
        return match.Groups["v"].Value;
    }

    // [US-2] The whole security change, over every filtered root at once: with no scope set, the comparison is
    // against Guid.Empty, which no row carries — so the read returns nothing instead of every clinic's rows.
    [Fact]
    public void An_Unset_Scope_Refuses_Every_Clinic_Owned_Read()
    {
        using var db = Unset();
        var roots = FilteredRoots(db);

        Assert.NotEmpty(roots);
        foreach (var root in roots)
        {
            var sql = SqlFor(db, root);

            Assert.False(SystemWideParameter(sql), $"{root.Name}: an unset scope must not read as system-wide.");
            Assert.Equal(Guid.Empty, ScopedClinicParameter(sql));
        }
    }

    [Fact]
    public void A_Clinic_Scope_Scopes_Every_Clinic_Owned_Read() // [US-2]
    {
        using var db = Context(new Scope { IsSystemWide = false, ClinicId = ClinicA });

        foreach (var root in FilteredRoots(db))
        {
            var sql = SqlFor(db, root);

            Assert.False(SystemWideParameter(sql));
            Assert.Equal(ClinicA, ScopedClinicParameter(sql));
        }
    }

    // [US-2] SystemWide is the only state that lifts the filter — which is why a job must declare it and why
    // nothing else may.
    [Fact]
    public void A_System_Wide_Scope_Lifts_Every_Filter()
    {
        using var db = Context(new Scope { IsSystemWide = true, ClinicId = null });

        foreach (var root in FilteredRoots(db))
        {
            Assert.True(SystemWideParameter(SqlFor(db, root)), $"{root.Name}: SystemWide must return every row.");
        }
    }

    // [US-2] The design-time factory and hand-constructed contexts pass no provider at all. That has to keep
    // reading everything, or `dotnet ef` and half this test project stop working — it is a different case from
    // Unset, which is a scope that exists and says nothing.
    [Fact]
    public void No_Provider_At_All_Still_Reads_Everything()
    {
        using var db = Context(null);

        foreach (var root in FilteredRoots(db))
        {
            Assert.True(SystemWideParameter(SqlFor(db, root)));
        }
    }

    // [US-2] Derived coverage: a clinic-owned table is filtered unless it is one of the four named decisions.
    // Asserted in both directions, so this fails on a new unfiltered root AND on a stale exemption.
    [Fact]
    public void Every_Clinic_Owned_Table_Is_Either_Filtered_Or_A_Named_Decision()
    {
        using var db = Unset();

        var clinicOwned = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("ClinicId") is not null || e.ClrType == typeof(Clinic))
            .ToList();

        var unfiltered = clinicOwned
            .Where(e => e.GetQueryFilter() is null)
            .Select(e => e.ClrType.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(UnfilteredByDesign.Keys.OrderBy(n => n), unfiltered);
    }

    // [US-2] The silent edge the plan names: onboarding runs with NO clinic in scope — `auth/mode`, login,
    // register, POST /clinics, POST /clinics/join and user-status all reach a principal who has no clinic yet.
    // They survive an Unset scope only because these two tables carry no filter. Assert it, don't assume it.
    [Fact]
    public void User_And_Clinic_Are_Unfiltered_So_Onboarding_Survives_An_Unset_Scope()
    {
        using var db = Unset();

        Assert.DoesNotContain("ef_filter", db.Users.ToQueryString());
        Assert.DoesNotContain("ef_filter", db.Clinics.ToQueryString());
    }
}
