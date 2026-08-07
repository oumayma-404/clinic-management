using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Proves the bounded relance read is actually <b>in SQL</b> (AC-P4.41).
///
/// <para><b>Why this exists.</b> The rest of the suite tests the handler against a mocked repository, so it
/// would pass just as happily if <c>GetRecallCandidatesAsync</c> pulled every row into memory and filtered
/// there — the whole finding (§ 9.6) is about the read, not the result. Worse, a LINQ expression the provider
/// cannot translate does not fail at build time: EF Core throws at <b>runtime</b>, on the request, so the
/// relance page would break for a real clinic and nowhere else. The optional owned <c>PhoneNumber</c> and the
/// correlated <c>MAX</c> over appointments are exactly the shapes that get rejected.</para>
///
/// <para><b>It still touches no database.</b> <c>ToQueryString()</c> asks the Npgsql provider to compile the
/// expression tree and hand back SQL; no connection is opened, and the connection string below is never dialled.
/// The provider is required only because SQL generation is provider-specific.</para>
/// </summary>
public class RecallQueryTranslationTests
{
    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;

        // No ICurrentClinicProvider → the global query filters are inactive (fail-open), which is the same
        // posture a background job runs under. The repository scopes by clinic explicitly regardless.
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Compiles the <b>production</b> expression tree — <see cref="PatientRepository.RecallCandidateQuery"/> is
    /// the very method <c>GetRecallCandidatesAsync</c> executes, so this cannot drift away from what ships.
    /// </summary>
    private static string RecallSql(ApplicationDbContext db, Guid clinicId, DateTime anchorBound, DateTime now) =>
        PatientRepository.RecallCandidateQuery(db, clinicId, anchorBound, now).ToQueryString();

    // [AC-P4.41] It compiles to SQL at all — the runtime-only failure this test exists to move to build time.
    [Fact]
    public void The_Recall_Candidate_Query_Translates_To_Sql()
    {
        using var db = Context();

        var sql = RecallSql(db, Guid.NewGuid(), DateTime.UtcNow.AddMonths(-6), DateTime.UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    // [AC-P4.41] …and every filter is IN that SQL rather than applied afterwards in C#. This is the assertion
    // that would have failed before the change, when the handler read every patient and every appointment.
    [Fact]
    public void Every_Filter_Is_Pushed_Down()
    {
        using var db = Context();

        var sql = RecallSql(db, Guid.NewGuid(), DateTime.UtcNow.AddMonths(-6), DateTime.UtcNow);

        // The future-booking exclusion is an EXISTS subquery, so no appointment row is ever materialised.
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        // The last completed visit is a correlated aggregate, not a client-side GroupBy over the whole table.
        Assert.Contains("MAX(", sql, StringComparison.OrdinalIgnoreCase);
        // Archived exclusion and the snooze (AC-P4.43) reach the database.
        Assert.Contains("IsArchived", sql, StringComparison.Ordinal);
        Assert.Contains("RecallSnoozedUntil", sql, StringComparison.Ordinal);
        // The optional owned PhoneNumber projects as its own column — the shape most likely to be rejected.
        Assert.Contains("PhoneNumber", sql, StringComparison.Ordinal);
    }
}
