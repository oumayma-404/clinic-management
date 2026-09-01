using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// « Patients à compléter » and « fiches masquées » are <b>complements</b>, decided in SQL.
///
/// <para><b>Why this exists, and why nothing else in the suite could have caught it.</b> The flag was
/// <c>includeDismissedReview</c> and it <i>widened</i> the read: with it set, the query returned every pending
/// record **plus** the dismissed ones. So « voir les fiches masquées » listed all four patients à compléter and
/// offered « Réafficher » on the three nobody had masked — a control that undoes nothing, on the one screen whose
/// entire claim is that a dismissal is reversible. Every layer reported success. A mocked repository applies no
/// predicate, so no handler test can see which rows come back; it was found by opening the screen.</para>
///
/// <para>⚠️ <b>And it had to be fixed in SQL, not by narrowing the page.</b> Filtering an already-cut page of 20
/// answers a different question — « the masked ones among these 20 » — and reports « aucune fiche masquée » for a
/// practice whose masked records sit on page 2. The same reasoning <c>flaggedOnly</c> and
/// <c>pendingCalendarReviewOnly</c> already carry.</para>
///
/// <para>It touches no database: <c>ToQueryString()</c> asks the Npgsql provider to compile the expression tree
/// and hand back SQL. Nothing is dialled — the provider is needed only because SQL generation is
/// provider-specific. Same technique and same reason as <see cref="RecallQueryTranslationTests"/>, and it
/// compiles the <b>production</b> expression (<see cref="PatientRepository.PendingReviewQuery"/>) rather than a
/// copy, so it cannot keep passing after the repository changes.</para>
/// </summary>
public class PendingReviewComplementTests
{
    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more.
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string Sql(ApplicationDbContext db, bool dismissedReviewOnly) =>
        PatientRepository.PendingReviewQuery(
                db.Patients.Where(p => p.ClinicId == Guid.Empty), dismissedReviewOnly)
            .ToQueryString();

    /// <summary>
    /// The ordinary view asks for records with **no** dismissal; the hidden view asks for the ones that have one.
    /// Asserted as SQL rather than as rows, because the defect was that the second predicate was absent
    /// altogether — and an absent predicate is invisible to anything that does not read the generated query.
    /// </summary>
    [Fact]
    public void The_Two_Views_Ask_Opposite_Questions_About_The_Dismissal()
    {
        using var db = Context();

        var pending = Sql(db, dismissedReviewOnly: false);
        var masked = Sql(db, dismissedReviewOnly: true);

        Assert.Contains("CalendarReviewDismissedAtUtc", pending);
        Assert.Contains("CalendarReviewDismissedAtUtc", masked);

        // The whole point: one side excludes what the other selects. `include` made them overlap.
        Assert.Contains("IS NULL", pending);
        Assert.Contains("IS NOT NULL", masked);
        Assert.DoesNotContain("IS NOT NULL", pending.Replace("CalendarImportPendingReviewSince\" IS NOT NULL", ""));
        Assert.NotEqual(pending, masked);
    }

    /// <summary>
    /// Both sides stay inside the pending-review list. A hidden view that dropped the review stamp would list
    /// every patient the practice ever dismissed anything about, and this tab is about one import's leftovers.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_Views_Stay_Inside_The_Pending_Review_List(bool dismissedReviewOnly)
    {
        using var db = Context();

        Assert.Contains("CalendarImportPendingReviewSince", Sql(db, dismissedReviewOnly));
    }

    /// <summary>
    /// ⚠️ The repository parameter is named for <b>narrowing</b>. Renaming it back to an « include » spelling is
    /// exactly the change that reintroduces the defect, and since the parameter is optional a caller passing it
    /// positionally would not break — so the name is what has to be pinned.
    /// </summary>
    [Fact]
    public void The_Repository_Parameter_Is_Named_For_Narrowing_Not_Widening()
    {
        var names = typeof(IPatientRepository)
            .GetMethod(nameof(IPatientRepository.GetByClinicIdAsync))!
            .GetParameters()
            .Select(p => p.Name!)
            .ToArray();

        Assert.Contains("dismissedReviewOnly", names);
        Assert.DoesNotContain("includeDismissedReview", names);
    }
}
