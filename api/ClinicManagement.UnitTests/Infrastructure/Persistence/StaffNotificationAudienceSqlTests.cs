using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using ClinicManagement.UnitTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Who a bell row is for (<c>clinic-pc-copy</c> D9) — compiled to SQL with no connection, <c>RecallQueryTranslationTests</c>'
/// technique. Both the list and the unread count must carry the role clause, or a secretary's badge counts an admin row
/// she cannot open; and nothing else in the repository may write its own audience predicate.
/// </summary>
public class StaffNotificationAudienceSqlTests
{
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void The_List_And_The_Unread_Count_Both_Check_The_Viewers_Role()
    {
        using var db = Context();
        var now = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

        var list = StaffNotificationRepository.VisibleQuery(db, Guid.NewGuid(), "local|u", now).ToQueryString();
        var unread = StaffNotificationRepository.UnreadQuery(db, Guid.NewGuid(), "local|u", now.AddDays(-1), now).ToQueryString();

        foreach (var sql in new[] { list, unread })
        {
            Assert.Contains("\"TargetRole\" IS NULL", sql);
            Assert.Contains("\"Users\"", sql);
            Assert.Contains("\"Role\"", sql);
            Assert.Contains("\"NotificationDismissals\"", sql);
        }
    }

    // The audience used to be written out twice, and « Tout effacer » left rows behind the day the copies differed.
    [Fact]
    public void The_Audience_Is_Written_Once()
    {
        var source = File.ReadAllText(Path.Combine(SolutionSources.Root().FullName,
            "ClinicManagement.Infrastructure", "Repositories", "StaffNotificationRepository.cs"));

        foreach (var clause in new[] { "n.TargetUserId == null", "n.TargetRole == null", "n.ActorUserId == null" })
        {
            Assert.Equal(1, source.Split(clause).Length - 1);
        }
    }
}
