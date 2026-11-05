using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Demo;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// « Entrer dans la démo » (demo-cabinet US-1, FR-3): which dentist a visitor enters as, and that each visitor gets a
/// session of their own that writes nothing but itself.
/// </summary>
public class StartDemoSessionCommandTests
{
    private static readonly DateTime Older = new(2026, 11, 2, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 11, 3, 2, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IDemoPolicy> _demo = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionFamilyRepository> _families = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditActorProvider> _actor = new();
    private readonly List<SessionFamily> _added = new();

    public StartDemoSessionCommandTests()
    {
        _demo.SetupGet(d => d.IssuesDemoSessions).Returns(true);
        _demo.SetupGet(d => d.SessionLifetime).Returns(TimeSpan.FromHours(2));
        _families.Setup(f => f.AddAsync(It.IsAny<SessionFamily>(), It.IsAny<CancellationToken>()))
            .Callback<SessionFamily, CancellationToken>((family, _) => _added.Add(family))
            .Returns(Task.CompletedTask);
        _auth.Setup(a => a.GenerateRefreshToken(It.IsAny<User>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
            .Returns((User _, Guid? _, bool _, DateTime end) => new LocalAuthToken($"refresh-{Guid.NewGuid()}", end));
        _auth.Setup(a => a.GenerateToken(It.IsAny<User>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .Returns((User _, Guid? _, DateTime end) => new LocalAuthToken("access", end));
    }

    private StartDemoSessionCommandHandler Handler() => new(
        _demo.Object,
        new DemoCabinetLocator(_clinics.Object, _users.Object),
        _families.Object,
        _auth.Object,
        _unitOfWork.Object,
        _actor.Object,
        NullLogger<StartDemoSessionCommandHandler>.Instance);

    private static Clinic ClinicCreatedAt(DateTime createdAt)
    {
        var clinic = new Clinic(Guid.NewGuid(), "Cabinet Dentaire Démo");
        typeof(Clinic).GetProperty(nameof(Clinic.CreatedAt))!.SetValue(clinic, createdAt);
        return clinic;
    }

    private static User Member(Clinic clinic, string role, string email, bool mustChangePassword = false) =>
        User.CreateLocalUser(clinic.Id, role, email, "HASH", "Monica Geller", mustChangePassword);

    private void Arrange(params (Clinic Clinic, User[] Members)[] cabinets)
    {
        _clinics.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cabinets.Select(c => c.Clinic));
        foreach (var (clinic, members) in cabinets)
        {
            _users.Setup(u => u.GetByClinicIdAsync(clinic.Id, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(PagedResult<User>.Unpaged(members));
        }
    }

    [Fact] // [US-1] the newest cabinet with an enterable dentist-admin wins
    public async Task A_Visitor_Enters_The_Newest_Ready_Cabinet()
    {
        var yesterday = ClinicCreatedAt(Older);
        var today = ClinicCreatedAt(Newer);
        var todaysAdmin = Member(today, User.RoleAdmin, "monica+today@demo.apexa.tn");
        Arrange((yesterday, new[] { Member(yesterday, User.RoleAdmin, "monica+yday@demo.apexa.tn") }), (today, new[] { todaysAdmin }));

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(todaysAdmin.Id, result.Value!.User!.Id);
    }

    [Fact] // [FR-7] a cabinet still being built (admin switched off) is never entered
    public async Task A_Cabinet_Whose_Admin_Is_Off_Is_Skipped_For_The_Previous_One()
    {
        var yesterday = ClinicCreatedAt(Older);
        var building = ClinicCreatedAt(Newer);
        var yesterdaysAdmin = Member(yesterday, User.RoleAdmin, "monica+yday@demo.apexa.tn");
        var buildingAdmin = Member(building, User.RoleAdmin, "monica+new@demo.apexa.tn");
        buildingAdmin.Deactivate();
        Arrange((yesterday, new[] { yesterdaysAdmin }), (building, new[] { buildingAdmin, Member(building, User.RoleDoctor, "chandler@demo.apexa.tn") }));

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.Equal(yesterdaysAdmin.Id, result.Value!.User!.Id);
    }

    [Fact] // [FR-3] a forced password change would trap every visitor, so such an admin is not entered as
    public async Task An_Admin_Who_Must_Change_Password_Is_Never_Entered()
    {
        var clinic = ClinicCreatedAt(Newer);
        Arrange((clinic, new[] { Member(clinic, User.RoleAdmin, "monica@demo.apexa.tn", mustChangePassword: true) }));

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DemoRefusals.RebuildingCode, result.Code);
        Assert.Empty(_added);
    }

    [Fact] // [FR-7] no cabinet yet: « La démo se met à jour », nothing written
    public async Task No_Cabinet_Answers_Rebuilding()
    {
        Arrange();

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.Equal(DemoRefusals.RebuildingCode, result.Code);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact] // the anonymous door stays shut wherever the capability is off, even if the controller were bypassed
    public async Task Nothing_Is_Minted_Off_The_Demo()
    {
        _demo.SetupGet(d => d.IssuesDemoSessions).Returns(false);

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _clinics.VerifyNoOtherCalls();
        Assert.Empty(_added);
    }

    [Fact] // [AC-1.4] two visitors, two families: one leaving never signs the other out
    public async Task Each_Visitor_Gets_A_Family_Of_Their_Own()
    {
        var clinic = ClinicCreatedAt(Newer);
        Arrange((clinic, new[] { Member(clinic, User.RoleAdmin, "monica@demo.apexa.tn") }));

        var first = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);
        var second = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(2, _added.Select(f => f.Id).Distinct().Count());
        Assert.All(_added, f => Assert.False(f.IsTrusted));
        Assert.NotEqual(first.Value!.RefreshToken, second.Value!.RefreshToken);
    }

    [Fact] // [FR-3] the credential dies with the session, and the shared dentist row is never written
    public async Task The_Session_Ends_At_A_Fixed_Time_And_Writes_Only_Itself()
    {
        var clinic = ClinicCreatedAt(Newer);
        var admin = Member(clinic, User.RoleAdmin, "monica@demo.apexa.tn");
        Arrange((clinic, new[] { admin }));

        var result = await Handler().Handle(new StartDemoSessionCommand(), CancellationToken.None);

        var family = Assert.Single(_added);
        Assert.Equal(family.CreatedAt + TimeSpan.FromHours(2), result.Value!.RefreshExpiresAt);
        _auth.Verify(a => a.GenerateToken(admin, family.Id, family.CreatedAt + TimeSpan.FromHours(2)), Times.Once);
        Assert.False(result.Value.MustChangePassword);
        Assert.Null(admin.LastLoginAt);
        _users.Verify(u => u.Update(It.IsAny<User>()), Times.Never);
        _actor.Verify(a => a.AuthenticatedAs(admin.Id, admin.Email), Times.Once);
    }
}
