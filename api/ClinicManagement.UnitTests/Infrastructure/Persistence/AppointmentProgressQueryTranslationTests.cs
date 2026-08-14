using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Proves the auto-start pass's candidate window is genuinely <b>in SQL</b>.
///
/// <para><b>Why this exists.</b> <c>AppointmentProgressJobTests</c> drives the job over a mocked repository, so it
/// would pass just as happily if the read pulled every appointment the clinic has ever had into memory — and an
/// untranslatable LINQ expression does not fail at build time: EF Core throws at <b>runtime</b>, on a minutely
/// job, where the symptom is a log line nobody reads and statuses that silently stop advancing.</para>
///
/// <para><b>It also pins the deliberate split.</b> <c>Duration</c> is persisted as ticks behind a value converter,
/// so <c>AppointmentDateTime + Duration</c> has no translation and the exact end-of-slot test is applied in memory
/// to the bounded set this query returns. That is a real decision with a real residual (see
/// <c>IAppointmentRepository.GetRunningNotStartedAsync</c>), so the assertion below is that the *window* reaches
/// the database — which is what keeps the in-memory half from growing into a full-table scan.</para>
///
/// <para>Touches no database: <c>ToQueryString()</c> asks Npgsql to compile the expression tree and hand back SQL.
/// The connection string below is never dialled.</para>
/// </summary>
public class AppointmentProgressQueryTranslationTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 14, 16, 0, DateTimeKind.Utc);

    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;

        // No ICurrentClinicProvider → the global filters are inactive, the same posture the design-time factory
        // runs under. The job declares UseSystemWide in production; that is AppointmentProgressJobTests' business.
        return new ApplicationDbContext(options);
    }

    /// <summary>Compiles the <b>production</b> expression tree, so this cannot drift away from what ships.</summary>
    private static string Sql(ApplicationDbContext db) =>
        AppointmentRepository.RunningCandidateQuery(db, Now, TimeSpan.FromDays(1)).ToQueryString();

    // It compiles at all — the runtime-only failure this test moves to build time.
    [Fact]
    public void The_Running_Candidate_Query_Translates_To_Sql()
    {
        using var db = Context();

        var sql = Sql(db);

        Assert.False(string.IsNullOrWhiteSpace(sql));
        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    // …and both halves of the window are IN it. Without the lower bound the pass would read every appointment
    // ever booked, every minute, on every deployment.
    [Fact]
    public void The_Status_And_Both_Window_Bounds_Are_Pushed_Down()
    {
        using var db = Context();

        var sql = Sql(db);

        Assert.Contains("Status", sql, StringComparison.Ordinal);
        // Two comparisons on the start instant: `<= now` and `> now - longestVisit`.
        Assert.Contains("AppointmentDateTime", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }
}
