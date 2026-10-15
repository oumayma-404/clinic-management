using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>The staff-chat reads compile to SQL (no connection) — an untranslatable expression would fail on the request, not here.</summary>
public class StaffMessageQuerySqlTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Keys = { "clinic", "dm:a:b" };

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void A_Page_Is_Keyset_Paged_On_SentAt_Then_Id()
    {
        using var db = Context();

        var sql = StaffMessageRepository.PageQuery(db, ClinicId, "clinic", Nine, Guid.NewGuid()).ToQueryString();

        Assert.Contains("ORDER BY s.\"SentAt\" DESC, s.\"Id\" DESC", sql);
        Assert.Contains("s.\"Id\" <", sql);
    }

    [Fact]
    public void The_Latest_Message_Per_Thread_Is_One_Query()
    {
        using var db = Context();

        var sql = StaffMessageRepository.LatestQuery(db, ClinicId, Keys).ToQueryString();

        Assert.Contains("ROW_NUMBER()", sql);
        Assert.Contains("PARTITION BY", sql);
    }

    [Fact]
    public void Unread_Excludes_Own_Messages_And_What_The_Marker_Already_Covers()
    {
        using var db = Context();

        var sql = StaffMessageRepository.UnreadQuery(db, ClinicId, "local|v", Keys, Nine)
            .GroupBy(m => new { m.ConversationKey, m.SenderUserId })
            .Select(g => new StaffUnreadCount(g.Key.ConversationKey, g.Key.SenderUserId, g.Count(), g.Count(m => m.IsUrgent), g.Max(m => m.SentAt)))
            .ToQueryString();

        Assert.Contains("\"SenderUserId\" <>", sql);
        Assert.Contains("\"StaffChatReadMarkers\"", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public void Quick_Message_Usage_Counts_One_Senders_Sends_Since_A_Moment_Per_Key() // AC-2.2
    {
        using var db = Context();

        var sql = StaffMessageRepository.UsageQuery(db, ClinicId, "local|v", Nine).ToQueryString();

        Assert.Contains("\"SenderUserId\" =", sql);
        Assert.Contains("\"SentAt\" >=", sql);
        Assert.Contains("\"QuickMessageKey\" IS NOT NULL", sql);
        Assert.Contains("GROUP BY s.\"QuickMessageKey\"", sql);
        Assert.Contains("count(*)", sql);
    }
}
