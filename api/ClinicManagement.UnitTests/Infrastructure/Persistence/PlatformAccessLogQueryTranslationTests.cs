using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Proves the journal's « Compte » filter options compile to SQL (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para><b>Why this exists: the query shipped, and threw on every single load.</b>
/// <c>GetRecordedActorsAsync</c> projected into the <c>PlatformAccessActor</c> record, then
/// <c>.Distinct().OrderBy(a =&gt; a.AccountEmail)</c> — which EF Core cannot translate, because the ordering is over a
/// custom type it has already materialised through <c>Distinct</c>. It builds, it passes every existing test, and the
/// provider throws at <b>request time</b>: « Journal illisible / il n'a pas pu être lu », for every console account,
/// on every page load. Found by opening the screen in Part 7 — the whole suite mocks this repository, so nothing in
/// it could have failed.</para>
///
/// <para>This is <c>RecallQueryTranslationTests</c>' arrangement and it exists for the identical reason: an
/// untranslatable LINQ expression is a <i>runtime</i> failure, and the only way to move it to build time is to ask
/// the provider to compile the production expression tree. <c>ToQueryString()</c> does exactly that and opens no
/// connection — the connection string below is never dialled.</para>
/// </summary>
public class PlatformAccessLogQueryTranslationTests
{
    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;

        return new ApplicationDbContext(options);
    }

    // The failure this test was written for: compiling the tree at all.
    [Fact]
    public void The_Recorded_Actors_Query_Translates_To_Sql()
    {
        using var db = Context();

        var sql = PlatformAccessEntryRepository.RecordedActorsQuery(db).ToQueryString();

        Assert.False(string.IsNullOrWhiteSpace(sql));
    }

    // The DISTINCT is the half that must stay in the database: it is what makes the filter's options one row per
    // account rather than one per recorded access, and a client-side Distinct over a whole ledger is the read this
    // console is built to avoid (EC-11).
    [Fact]
    public void The_Deduplication_Happens_In_The_Database()
    {
        using var db = Context();

        var sql = PlatformAccessEntryRepository.RecordedActorsQuery(db).ToQueryString();

        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PlatformAccessEntries", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The sort is deliberately **not** in this query, and asserting its absence is what stops the defect being
    /// reintroduced by somebody tidying the in-memory <c>OrderBy</c> back into the expression tree — which reads as
    /// an obvious improvement and is the exact line that broke the screen.
    /// </summary>
    [Fact]
    public void The_Ordering_Is_Deliberately_Left_Out_Of_The_Translated_Query()
    {
        using var db = Context();

        var sql = PlatformAccessEntryRepository.RecordedActorsQuery(db).ToQueryString();

        Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }
}
