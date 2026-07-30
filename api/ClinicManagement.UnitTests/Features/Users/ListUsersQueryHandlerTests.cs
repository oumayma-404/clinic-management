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
}
