using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// « Premiers pas » (<c>guided-first-days</c> B): when the card shows, and when each of its seven steps ticks.
/// Every step ticks from the cabinet's own data — the cases below are the claims the card makes.
/// </summary>
public class GetGettingStartedQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<ISecondFactorPolicy> _policy = new();

    private readonly User _admin;
    private readonly Clinic _clinic;
    private readonly List<User> _staff;

    public GetGettingStartedQueryTests()
    {
        _admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "admin@clinic.tn", "HASH", "Dr Geller");
        _clinic = ClinicCreatedAt(DateTime.UtcNow.AddDays(-3));
        _staff = [_admin];

        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_admin);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_staff));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _clinic);
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
    }

    private GetGettingStartedQueryHandler Handler() => new(
        _context.Object, _users.Object, _clinics.Object, _patients.Object, _appointments.Object,
        _records.Object, _policy.Object, NullLogger<GetGettingStartedQueryHandler>.Instance);

    private async Task<GettingStartedDto> Read()
    {
        var result = await Handler().Handle(new GetGettingStartedQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static bool Done(GettingStartedDto dto, string key) => dto.Steps.Single(s => s.Key == key).Done;

    /// <summary>The cabinet's creation date is stamped by its constructor, so an old cabinet is built this way.</summary>
    private static Clinic ClinicCreatedAt(DateTime createdAtUtc)
    {
        var clinic = new Clinic(ClinicId, "Cabinet Dr Geller", city: "Tunis");
        typeof(Clinic).GetProperty(nameof(Clinic.CreatedAt))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(clinic, new object[] { createdAtUtc });
        return clinic;
    }

    private void SetUpEverythingDone()
    {
        _admin.IssueTotpSecret("protected:A");
        _admin.CompleteTotpEnrolment([UserRecoveryCode.NewCode()]);
        _admin.RecordPhoneAppSignIn(User.PhoneAppSurfaces.Android);
        _staff.Add(User.CreateLocalUser(ClinicId, User.RoleSecretary, "amel@clinic.tn", "HASH", "Amel"));
        _patients.Setup(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _appointments.Setup(r => r.CountByClinicIdAsync(ClinicId, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _records.Setup(r => r.AnyForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinic.SetLetterhead("clinics/x/letterhead/header.png", null);
    }

    [Fact]
    public async Task A_New_Cabinet_Sees_All_Seven_Steps_Undone_In_Order()
    {
        var dto = await Read();

        Assert.True(dto.Visible);
        Assert.Equal(GettingStartedKeys.All, dto.Steps.Select(s => s.Key));
        Assert.All(dto.Steps, step => Assert.False(step.Done));
    }

    [Fact]
    public async Task Each_Step_Ticks_From_Its_Own_Fact()
    {
        SetUpEverythingDone();
        // One left undone keeps the card up so the others can be read.
        _records.Setup(r => r.AnyForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var dto = await Read();

        Assert.True(dto.Visible);
        Assert.True(Done(dto, GettingStartedKeys.SecurityCode));
        Assert.True(Done(dto, GettingStartedKeys.Secretary));
        Assert.True(Done(dto, GettingStartedKeys.PhoneApp));
        Assert.True(Done(dto, GettingStartedKeys.Patients));
        Assert.True(Done(dto, GettingStartedKeys.Appointment));
        Assert.False(Done(dto, GettingStartedKeys.DentalRecord));
        Assert.True(Done(dto, GettingStartedKeys.Letterhead));
    }

    // A pending request or a switched-off colleague is not a secretary who can work yet.
    [Fact]
    public async Task An_Inactive_Second_Account_Does_Not_Tick_The_Secretary()
    {
        var pending = User.CreateSelfRegistered(ClinicId, User.RoleSecretary, "amel@clinic.tn", "HASH", "Amel");
        _staff.Add(pending);

        var dto = await Read();

        Assert.False(Done(dto, GettingStartedKeys.Secretary));
        Assert.DoesNotContain(dto.PhoneApp, person => person.Name == "Amel");
    }

    [Fact]
    public async Task The_Security_Step_Is_Absent_Where_No_Second_Factor_Is_Required()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(false);

        var dto = await Read();

        Assert.DoesNotContain(dto.Steps, step => step.Key == GettingStartedKeys.SecurityCode);
        Assert.Equal(6, dto.Steps.Count);
    }

    [Fact]
    public async Task All_Done_Hides_The_Card()
    {
        SetUpEverythingDone();

        var dto = await Read();

        Assert.False(dto.Visible);
        Assert.Empty(dto.Steps);
    }

    // After its first month a cabinet never sees the card again — and pays for no count to learn that.
    [Fact]
    public async Task An_Older_Cabinet_Is_Hidden_Without_Counting_Anything()
    {
        var old = ClinicCreatedAt(DateTime.UtcNow.AddDays(-GettingStartedRules.VisibleDays - 1));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(old);

        var dto = await Read();

        Assert.False(dto.Visible);
        _patients.Verify(r => r.CountByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _appointments.Verify(r => r.CountByClinicIdAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AppointmentStatus?>(),
            It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _records.Verify(r => r.AnyForClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.GetByClinicIdAsync(
            It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Doctor_Is_Shown_Nothing()
    {
        var doctor = User.CreateLocalUser(ClinicId, User.RoleDoctor, "doc@clinic.tn", "HASH", "Dr Ross");
        _context.Setup(c => c.GetUserId()).Returns(doctor.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(doctor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);

        var dto = await Read();

        Assert.False(dto.Visible);
        _clinics.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The counts are asked for the caller's own cabinet, read off their account row — never anyone else's.
    [Fact]
    public async Task Every_Count_Is_Asked_For_The_Callers_Own_Cabinet()
    {
        await Read();

        _patients.Verify(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        _patients.Verify(r => r.CountByClinicIdAsync(
            It.Is<Guid>(id => id != ClinicId), It.IsAny<CancellationToken>()), Times.Never);
        _records.Verify(r => r.AnyForClinicAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(r => r.CountByClinicIdAsync(
            ClinicId, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Phone_List_Names_Each_Active_Account_And_Its_First_Phone_Sign_In()
    {
        var secretary = User.CreateLocalUser(ClinicId, User.RoleSecretary, "amel@clinic.tn", "HASH", "Amel");
        var at = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
        secretary.RecordPhoneAppSignIn(User.PhoneAppSurfaces.IosHomeScreen, at);
        _staff.Add(secretary);

        var dto = await Read();

        var amel = Assert.Single(dto.PhoneApp, person => person.Name == "Amel");
        Assert.Equal(User.PhoneAppSurfaces.IosHomeScreen, amel.Platform);
        Assert.Equal(at, amel.FirstSignInAt);
        Assert.False(amel.IsYou);
        var me = Assert.Single(dto.PhoneApp, person => person.Name == "Dr Geller");
        Assert.Null(me.Platform);
        Assert.True(me.IsYou);
        // The step is the administrator's own: a colleague's phone does not tick it.
        Assert.False(Done(dto, GettingStartedKeys.PhoneApp));
    }

    [Fact]
    public void The_Window_Is_Thirty_Days()
    {
        var created = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(GettingStartedRules.IsWithinFirstDays(created, created.AddDays(29)));
        Assert.False(GettingStartedRules.IsWithinFirstDays(created, created.AddDays(30)));
    }
}
