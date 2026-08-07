using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Users.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

public class ListUsersQueryHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();

    private ListUsersQueryHandler Handler() => new(_users.Object, _context.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    // [AC-5.1] Admin sees the clinic users with their status.
    [Fact]
    public async Task Handle_Should_Return_Users_With_Status_For_Admin()
    {
        var admin = Local("admin");
        var doctor = Local("doctor");
        doctor.Deactivate();
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { admin, doctor }).AsPage());

        var result = await Handler().Handle(new ListUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var deactivated = result.Value!.Items.Single(u => u.Role == "doctor");
        Assert.False(deactivated.IsActive);
        Assert.True(result.Value!.Items.Single(u => u.Role == "admin").IsActive);
    }

    // [AC-5.4] A non-admin cannot list users.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        var secretary = Local("secretary");
        _context.Setup(c => c.GetUserId()).Returns(secretary.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(secretary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(secretary);

        var result = await Handler().Handle(new ListUsersQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _users.Verify(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }
    // ---------------------------------------------------------------- I5: the pending count

    /// <summary>
    /// [I5] The « N en attente » figure is counted over the <b>whole clinic</b>, not over the loaded page.
    ///
    /// <para>That is the reason it is a separate repository call rather than a <c>Count</c> over the rows already
    /// fetched: with a page of 25, an admin whose two pending colleagues sort onto page 2 would be told
    /// « 0 en attente » — precisely the case the number exists for, since nobody gets in until someone notices.
    /// The mock returns a page containing no pending user at all, so a page-derived implementation would report 0
    /// and fail here.</para>
    /// </summary>
    [Fact]
    public async Task Handle_Reports_The_Pending_Count_Over_The_Whole_Clinic_Not_The_Page()
    {
        var admin = Local("admin");
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { admin }).AsPage());
        _users.Setup(r => r.CountPendingActivationAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var result = await Handler().Handle(new ListUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.PendingActivationCount);
        Assert.Single(result.Value.Items);
    }

    // [I5] The count is not narrowed by the search term. An admin filtering for one name must still learn that
    // somebody else cannot log in — so the clinic id is passed and the term is not.
    [Fact]
    public async Task Handle_Does_Not_Narrow_The_Pending_Count_By_The_Search_Term()
    {
        var admin = Local("admin");
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { admin }).AsPage());
        _users.Setup(r => r.CountPendingActivationAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await Handler().Handle(
            new ListUsersQuery { SearchTerm = "quelqu'un d'autre" }, CancellationToken.None);

        Assert.Equal(3, result.Value!.PendingActivationCount);
        _users.Verify(r => r.CountPendingActivationAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [I5] Each row carries the distinction the badge renders: never-let-in vs switched-off-after-use.
    [Fact]
    public async Task Handle_Projects_IsPendingActivation_Per_Row()
    {
        var admin = Local("admin");
        var pending = User.CreateSelfRegistered(ClinicId, "secretary", "new@clinic.com", "HASH", "Newcomer");
        var retired = Local("doctor");
        retired.RecordSuccessfulLogin();
        retired.Deactivate();

        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { admin, pending, retired }).AsPage());
        _users.Setup(r => r.CountPendingActivationAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Handler().Handle(new ListUsersQuery(), CancellationToken.None);

        var items = result.Value!.Items;
        Assert.True(items.Single(u => u.Email == "new@clinic.com").IsPendingActivation);
        // Inactive, but deliberately so — the row must not read « en attente d'activation ».
        var retiredDto = items.Single(u => u.Email == "doctor@clinic.com");
        Assert.False(retiredDto.IsActive);
        Assert.False(retiredDto.IsPendingActivation);
        Assert.False(items.Single(u => u.Role == "admin").IsPendingActivation);
    }

    // [I5] The paging envelope survives the new wrapper DTO — the shared pager reads these five fields, so a
    // wrapper that dropped them would render « page 1 sur 0 » over a full table.
    [Fact]
    public async Task Handle_Preserves_The_Paging_Envelope()
    {
        var admin = Local("admin");
        _context.Setup(c => c.GetUserId()).Returns(admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<User>(new[] { admin }, page: 2, pageSize: 1, totalCount: 5));

        var result = await Handler().Handle(new ListUsersQuery { Page = 2, PageSize = 1 }, CancellationToken.None);

        var page = result.Value!;
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(5, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.True(page.HasNextPage);
    }

}
