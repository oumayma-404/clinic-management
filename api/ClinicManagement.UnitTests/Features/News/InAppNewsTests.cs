using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.News.Commands;
using ClinicManagement.Application.Features.News.Queries;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.News;

/// <summary>
/// « Nouveautés » in the app: which news an administrator sees, which count as unread, how reading moves the marker,
/// and the console's « Masquer / Afficher dans l'app ».
/// </summary>
public class InAppNewsTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime Joined = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPlatformNewsRepository> _news = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly List<PlatformNewsReadMarker> _addedMarkers = [];
    private readonly User _admin;
    private PlatformNewsReadMarker? _marker;
    private IReadOnlyList<PlatformNewsAppRow> _rows = [];

    public InAppNewsTests()
    {
        _admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "dr@cabinet.tn", "HASH", "Dr Sassi");
        typeof(User).GetProperty(nameof(User.CreatedAt))!.GetSetMethod(nonPublic: true)!.Invoke(_admin, new object[] { Joined });

        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_admin);
        _news.Setup(r => r.GetReadMarkerAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _marker);
        _news.Setup(r => r.AddReadMarkerAsync(It.IsAny<PlatformNewsReadMarker>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformNewsReadMarker, CancellationToken>((m, _) => _addedMarkers.Add(m))
            .Returns(Task.CompletedTask);
        _news.Setup(r => r.ListVisibleInAppAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _rows);
        _news.Setup(r => r.GetNewestVisibleInAppAtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rows.Count == 0 ? null : _rows.Max(r => r.CreatedAtUtc));
        _news.Setup(r => r.CountVisibleInAppAfterAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime after, CancellationToken _) => _rows.Count(r => r.CreatedAtUtc > after));
    }

    private static PlatformNewsAppRow Row(string subject, DateTime sentAtUtc) =>
        new(Guid.NewGuid(), subject, sentAtUtc, [new PlatformNewsAppItem("🚀", "Agenda plus rapide.")]);

    private GetNewsQueryHandler Feed() =>
        new(_context.Object, _users.Object, _news.Object, NullLogger<GetNewsQueryHandler>.Instance);

    private GetNewsSummaryQueryHandler Summary() =>
        new(_context.Object, _users.Object, _news.Object, NullLogger<GetNewsSummaryQueryHandler>.Instance);

    private MarkNewsReadCommandHandler MarkRead() =>
        new(_context.Object, _users.Object, _news.Object, _unitOfWork.Object, NullLogger<MarkNewsReadCommandHandler>.Instance);

    [Fact]
    public async Task With_No_Marker_Only_News_Sent_After_The_Account_Was_Created_Are_Unread()
    {
        _rows = [Row("Après", Joined.AddDays(10)), Row("Avant", Joined.AddDays(-30))];

        var feed = (await Feed().Handle(new GetNewsQuery(), CancellationToken.None)).Value!;

        Assert.Equal(1, feed.Unread);
        Assert.Equal(new[] { true, false }, feed.Entries.Select(e => e.IsUnread));
        Assert.Equal(new[] { "Après", "Avant" }, feed.Entries.Select(e => e.Subject));
    }

    [Fact]
    public async Task The_Marker_Decides_What_Is_Unread_Once_The_Admin_Has_Read_The_Page()
    {
        _rows = [Row("Nouvelle", Joined.AddDays(20)), Row("Lue", Joined.AddDays(10))];
        _marker = new PlatformNewsReadMarker(ClinicId, _admin.Id, Joined.AddDays(10));

        var feed = (await Feed().Handle(new GetNewsQuery(), CancellationToken.None)).Value!;
        var summary = (await Summary().Handle(new GetNewsSummaryQuery(), CancellationToken.None)).Value!;

        Assert.Equal(new[] { true, false }, feed.Entries.Select(e => e.IsUnread));
        Assert.Equal(1, summary.Unread);
    }

    [Fact]
    public async Task The_Day_Printed_Is_The_Tunisian_Day_Not_The_Utc_One()
    {
        _rows = [Row("Tard le soir", new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc))];

        var entry = Assert.Single((await Feed().Handle(new GetNewsQuery(), CancellationToken.None)).Value!.Entries);

        Assert.Equal("2026-11-01", entry.SentOn);
    }

    [Fact]
    public async Task Reading_Moves_The_Marker_To_The_Newest_News_Not_To_Now()
    {
        var newest = Joined.AddDays(20);
        _rows = [Row("Nouvelle", newest), Row("Ancienne", Joined.AddDays(5))];

        var result = await MarkRead().Handle(new MarkNewsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var marker = Assert.Single(_addedMarkers);
        Assert.Equal((ClinicId, _admin.Id, newest), (marker.ClinicId, marker.UserId, marker.ReadUpToUtc));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reading_Again_With_Nothing_New_Writes_Nothing()
    {
        _rows = [Row("Lue", Joined.AddDays(10))];
        _marker = new PlatformNewsReadMarker(ClinicId, _admin.Id, Joined.AddDays(10));

        var result = await MarkRead().Handle(new MarkNewsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_addedMarkers);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reading_With_No_News_At_All_Writes_Nothing()
    {
        var result = await MarkRead().Handle(new MarkNewsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reading_A_Newer_News_Advances_The_Existing_Marker()
    {
        _marker = new PlatformNewsReadMarker(ClinicId, _admin.Id, Joined.AddDays(10));
        _rows = [Row("Nouvelle", Joined.AddDays(30))];

        await MarkRead().Handle(new MarkNewsReadCommand(), CancellationToken.None);

        Assert.Empty(_addedMarkers);
        Assert.Equal(Joined.AddDays(30), _marker.ReadUpToUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Unknown_Caller_Is_Refused_Rather_Than_Shown_Every_News_As_Unread()
    {
        _context.Setup(c => c.GetUserId()).Returns("local|inconnu");

        Assert.True((await Feed().Handle(new GetNewsQuery(), CancellationToken.None)).IsFailure);
        Assert.True((await Summary().Handle(new GetNewsSummaryQuery(), CancellationToken.None)).IsFailure);
        Assert.True((await MarkRead().Handle(new MarkNewsReadCommand(), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public void A_Marker_Never_Moves_Backwards()
    {
        var marker = new PlatformNewsReadMarker(ClinicId, _admin.Id, Joined.AddDays(10));

        Assert.False(marker.Advance(Joined.AddDays(5)));
        Assert.Equal(Joined.AddDays(10), marker.ReadUpToUtc);
        Assert.True(marker.Advance(Joined.AddDays(11)));
    }

    private static PlatformNewsBroadcast Broadcast() =>
        PlatformNewsBroadcast.Create(Guid.NewGuid(), "Objet", [new PlatformNewsItemInput("🚀", "x")],
            [new PlatformNewsRecipientInput(ClinicId, "Cabinet", "dr@cabinet.tn", null)], Guid.NewGuid(), "Oumayma", Joined);

    [Fact]
    public void Hiding_And_Showing_Are_Each_A_No_Op_When_Already_Done()
    {
        var broadcast = Broadcast();

        Assert.True(broadcast.IsVisibleInApp);
        Assert.False(broadcast.ShowInApp());
        Assert.True(broadcast.HideFromApp(Joined));
        Assert.False(broadcast.HideFromApp(Joined.AddDays(1)));
        Assert.Equal(Joined, broadcast.HiddenFromAppAtUtc);
        Assert.True(broadcast.ShowInApp());
        Assert.True(broadcast.IsVisibleInApp);
    }

    private SetPlatformNewsVisibilityCommandHandler Visibility() =>
        new(_news.Object, _unitOfWork.Object, NullLogger<SetPlatformNewsVisibilityCommandHandler>.Instance);

    [Fact]
    public async Task The_Console_Hides_A_News_From_The_App_And_Saves_Once()
    {
        var broadcast = Broadcast();
        _news.Setup(r => r.GetAsync(broadcast.Id, It.IsAny<CancellationToken>())).ReturnsAsync(broadcast);

        var result = await Visibility().Handle(
            new SetPlatformNewsVisibilityCommand { BroadcastId = broadcast.Id, Visible = false }, CancellationToken.None);

        Assert.False(result.Value!.VisibleInApp);
        Assert.False(broadcast.IsVisibleInApp);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pressing_Twice_Is_Not_A_Refusal_And_Writes_Nothing()
    {
        var broadcast = Broadcast();
        _news.Setup(r => r.GetAsync(broadcast.Id, It.IsAny<CancellationToken>())).ReturnsAsync(broadcast);

        var result = await Visibility().Handle(
            new SetPlatformNewsVisibilityCommand { BroadcastId = broadcast.Id, Visible = true }, CancellationToken.None);

        Assert.True(result.Value!.VisibleInApp);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_News_Is_Refused_With_Its_Code()
    {
        var result = await Visibility().Handle(
            new SetPlatformNewsVisibilityCommand { BroadcastId = Guid.NewGuid(), Visible = false }, CancellationToken.None);

        Assert.Equal(SetPlatformNewsVisibilityCommandHandler.NotFoundCode, result.Code);
    }
}
