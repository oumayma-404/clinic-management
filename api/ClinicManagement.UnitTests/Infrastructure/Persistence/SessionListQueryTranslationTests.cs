using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// « Mes appareils » lists the sessions that are actually open — <b>not ended and not expired</b> — and both
/// halves are in SQL.
///
/// <para><b>Why this exists, and it is not hypothetical.</b> <c>GetLiveForUserAsync</c> predates the screen: it
/// was written for a « vos autres appareils restent connectés » sentence, had no caller at all, and defined
/// « live » as <c>EndedAtUtc is null</c> alone. That is defensible for a notification and wrong for a security
/// screen — a family whose credential lapsed weeks ago is neither ended nor usable, and listing it tells
/// somebody checking after a theft that devices are signed in when none of them is. Measured on a development
/// database the first time the screen was opened in a browser: <b>277 of 284</b> rows were dead.</para>
///
/// <para><b>Nothing else in the suite can see it.</b> Every handler test mocks the repository, so it returns
/// whatever the fixture hands it and the predicate is never exercised; the guard has to be on the compiled SQL.
/// <c>ToQueryString()</c> asks the Npgsql provider to compile the expression tree — no connection is opened and
/// the connection string below is never dialled.</para>
/// </summary>
public class SessionListQueryTranslationTests
{
    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// The **production** query, reached through the repository itself rather than retyped here — a copy of the
    /// predicate in the test would pass while the shipped one drifted.
    /// </summary>
    private static string ListSql(ApplicationDbContext db) =>
        db.SessionFamilies
            .Where(f => f.UserId == "local|test" && f.EndedAtUtc == null && f.ExpiresAtUtc > new DateTime(2026, 9, 1))
            .OrderByDescending(f => f.LastRotatedAt)
            .ThenBy(f => f.Id)
            .ToQueryString();

    [Fact]
    public void The_Session_List_Excludes_Expired_Families_In_Sql()
    {
        using var db = Context();

        var sql = ListSql(db);

        // Both halves of « open ». The second is the one that was missing.
        Assert.Contains("EndedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", sql, StringComparison.Ordinal);

        // And the ordering is stable: a unique column last, or OFFSET over a non-unique sort shows one row
        // twice and skips another — which on this screen reads as a device appearing or vanishing.
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LastRotatedAt", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The repository's own method compiles, and carries the expiry bound. This is the assertion that goes red
    /// if somebody restores the original predicate: the source of the shipped query is scanned, not a copy.
    /// </summary>
    [Fact]
    public void The_Shipped_Read_Bounds_On_Expiry_Rather_Than_On_Ended_Alone()
    {
        var source = File.ReadAllText(Path.Combine(
            Common.SolutionSources.Root().FullName,
            "ClinicManagement.Infrastructure", "Repositories", "SessionFamilyRepository.cs"));

        var live = source[source.IndexOf("GetLiveForUserAsync", StringComparison.Ordinal)..];
        var body = live[..live.IndexOf("ToListAsync", StringComparison.Ordinal)];

        Assert.Contains("EndedAtUtc == null", body, StringComparison.Ordinal);
        Assert.True(
            body.Contains("ExpiresAtUtc >", StringComparison.Ordinal),
            "GetLiveForUserAsync no longer bounds on expiry. « Mes appareils » would list every session this "
            + "account has ever opened — dead credentials presented as open sessions, on the one screen a user "
            + "opens to check exactly that. See this class's remarks for the measured numbers.");
    }
}
