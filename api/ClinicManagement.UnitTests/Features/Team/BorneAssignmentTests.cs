using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// « Ajouter une borne » from the admin's own computer: she picks another signed-in device, the borne waits with no
/// secret, and that device claims it on its next status read — minting its own secret, so it never crosses her machine.
/// A waiting borne is nobody's punch point, a device that signed out can never be chosen, and choosing again replaces.
/// </summary>
public class BorneAssignmentTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<ISessionFamilyRepository> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly TenantScope _scope = new(NullLogger<TenantScope>.Instance);
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");
    private readonly List<AttendanceTerminal> _added = new();
    private readonly List<AttendanceTerminal> _cabinetBornes = new();
    private readonly List<User> _roster = new();
    private readonly Mock<INotificationGenerator> _bell = new();
    private readonly User _admin;
    private readonly User _ines;
    private readonly SessionFamily _adminPc;
    private readonly SessionFamily _receptionPc;

    public BorneAssignmentTests()
    {
        _clinic.SetAttendanceEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _terminals.Setup(r => r.AddAsync(It.IsAny<AttendanceTerminal>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceTerminal, CancellationToken>((t, _) => _added.Add(t));
        _terminals.Setup(r => r.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _cabinetBornes.ToList());
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _bell.Setup(b => b.AttendanceNoticeAsync(It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _scope.UseClinic(ClinicId);

        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _adminPc = Session(_admin, "Edge sur Windows");
        _receptionPc = Session(_ines, "Chrome sur Windows");
        SignedInAs(_admin, _adminPc);
    }

    private User Account(string role, string name, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        if ((clinicId ?? ClinicId) == ClinicId) _roster.Add(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    /// <summary>A live session with its account loaded, the shape <c>GetLiveForClinicAsync</c> returns.</summary>
    private SessionFamily Session(User owner, string? label, DateTime? expiresAtUtc = null)
    {
        var session = new SessionFamily(owner.Id, $"hash-{Guid.NewGuid():N}", expiresAtUtc ?? DateTime.UtcNow.AddHours(12), label);
        typeof(SessionFamily).GetProperty(nameof(SessionFamily.User))!.SetValue(session, owner);
        _sessions.Setup(r => r.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        return session;
    }

    private void SignedInAs(User user, SessionFamily session)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _context.Setup(c => c.GetSessionFamilyId()).Returns(session.Id);
    }

    private AttendanceTerminal Waiting(SessionFamily session, string name = "Accueil")
    {
        var terminal = AttendanceTerminal.AssignToDevice(
            Guid.NewGuid(), ClinicId, name, session.Id, "Ines Gharbi · Chrome sur Windows", _admin.Id, DateTime.UtcNow.AddMinutes(-1));
        _terminals.Setup(r => r.FindPendingForSessionAsync(ClinicId, session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(terminal);
        return terminal;
    }

    private AttendanceTerminal Claimed(Guid clinicId, string name, string secret)
    {
        var terminal = AttendanceTerminal.Register(Guid.NewGuid(), clinicId, name, SessionCredential.Hash(secret), "admin", DateTime.UtcNow.AddDays(-3));
        _terminals.Setup(r => r.FindActiveBySecretHashAcrossClinicsAsync(SessionCredential.Hash(secret), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminal);
        return terminal;
    }

    private AttendanceNotifier Notifier() =>
        new(_bell.Object, _users.Object, _terminals.Object, _scope, NullLogger<AttendanceNotifier>.Instance);

    private RegisterAttendanceTerminalCommandHandler Register() =>
        new(_terminals.Object, _users.Object, _clinics.Object, _context.Object, _sessions.Object, Notifier(), _uow.Object);

    private ClaimBorneCommandHandler Claim() =>
        new(_terminals.Object, _users.Object, _context.Object, _scope, Notifier(), _uow.Object);

    private GetBorneDevicesQueryHandler Devices() =>
        new(_sessions.Object, _users.Object, _clinics.Object, _context.Object);

    // ---- the entity ----------------------------------------------------------------------------------------

    [Fact]
    public void A_Borne_Chosen_For_A_Device_Waits_With_No_Secret_Until_That_Device_Claims_It()
    {
        var terminal = AttendanceTerminal.AssignToDevice(
            Guid.NewGuid(), ClinicId, "  Accueil  ", _receptionPc.Id, "Ines Gharbi · Chrome sur Windows", _admin.Id, DateTime.UtcNow);

        Assert.True(terminal.IsActive);
        Assert.True(terminal.IsPending);
        Assert.Null(terminal.SecretHash);
        Assert.Equal("Accueil", terminal.Name);
        Assert.Equal(_receptionPc.Id, terminal.PendingSessionFamilyId);

        terminal.Claim("HASH", DateTime.UtcNow);

        Assert.False(terminal.IsPending);
        Assert.Equal("HASH", terminal.SecretHash);
        Assert.Null(terminal.PendingSessionFamilyId);
        Assert.Throws<InvalidOperationException>(() => terminal.Claim("OTHER", DateTime.UtcNow));
    }

    [Fact]
    public void A_Retired_Borne_Is_No_Longer_Waiting_And_Cannot_Be_Claimed()
    {
        var terminal = Waiting(_receptionPc);
        terminal.Revoke(DateTime.UtcNow);

        Assert.False(terminal.IsPending);
        Assert.Throws<InvalidOperationException>(() => terminal.Claim("HASH", DateTime.UtcNow));
    }

    // ---- choosing the device -------------------------------------------------------------------------------

    [Fact]
    public async Task Choosing_Another_Signed_In_Device_Leaves_A_Waiting_Borne_And_No_Secret_On_The_Admins_Machine()
    {
        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = _receptionPc.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(string.Empty, result.Value!.Secret);
        var added = Assert.Single(_added);
        Assert.True(added.IsPending);
        Assert.Null(added.SecretHash);
        Assert.Equal(_receptionPc.Id, added.PendingSessionFamilyId);
        Assert.Equal("Ines Gharbi · Chrome sur Windows", added.DeviceDescription);
        Assert.True(result.Value.Terminal.IsPending);
        Assert.Equal("Ines Gharbi · Chrome sur Windows", result.Value.Terminal.DeviceDescription);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Choosing_Her_Own_Device_From_The_List_Makes_It_A_Borne_At_Once()
    {
        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Bureau", DeviceSessionId = _adminPc.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", result.Value!.Secret);
        var added = Assert.Single(_added);
        Assert.False(added.IsPending);
        Assert.Equal(SessionCredential.Hash(result.Value.Secret), added.SecretHash);
    }

    [Fact]
    public async Task A_Device_With_No_Name_Is_Described_As_Such()
    {
        var unnamed = Session(_ines, null);

        await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = unnamed.Id }, default);

        Assert.Equal($"Ines Gharbi · {AttendanceRules.UnnamedDevice}", Assert.Single(_added).DeviceDescription);
    }

    [Fact]
    public async Task Choosing_The_Same_Device_Again_Replaces_The_Borne_Still_Waiting_For_It()
    {
        var earlier = Waiting(_receptionPc, "Accueil");

        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Réception", DeviceSessionId = _receptionPc.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(earlier.IsActive);
        Assert.Equal("Réception", Assert.Single(_added).Name);
    }

    public static TheoryData<string> GoneDevices => new() { "ended", "expired", "unknown", "other-clinic", "deactivated" };

    [Theory]
    [MemberData(nameof(GoneDevices))]
    public async Task A_Device_That_Is_No_Longer_Signed_In_Here_Cannot_Be_Chosen(string why)
    {
        Guid sessionId;
        switch (why)
        {
            case "ended":
                _receptionPc.End("logout");
                sessionId = _receptionPc.Id;
                break;
            case "expired":
                sessionId = Session(_ines, "Chrome sur Windows", DateTime.UtcNow.AddMinutes(-1)).Id;
                break;
            case "unknown":
                sessionId = Guid.NewGuid();
                break;
            case "other-clinic":
                sessionId = Session(Account(User.RoleSecretary, "Sana Ailleurs", OtherClinicId), "Chrome sur Windows").Id;
                break;
            default:
                _ines.Deactivate();
                sessionId = _receptionPc.Id;
                break;
        }

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = sessionId }, default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceRules.DeviceGoneCode, result.Code);
        Assert.Equal(AttendanceRules.DeviceGone, result.Error);
        Assert.Empty(_added);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Secretary_Cannot_Choose_A_Device_Either()
    {
        SignedInAs(_ines, _receptionPc);

        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = _adminPc.Id }, default);

        Assert.Equal(AttendanceRules.AdminOnly, result.Error);
        Assert.Empty(_added);
    }

    // ---- the chosen device picks it up ---------------------------------------------------------------------

    [Fact]
    public async Task The_Chosen_Device_Claims_Its_Borne_And_Mints_Its_Own_Secret()
    {
        var waiting = Waiting(_receptionPc);
        SignedInAs(_ines, _receptionPc);

        var result = await Claim().Handle(new ClaimBorneCommand(), default);

        Assert.True(result.IsSuccess, result.Error);
        var secret = result.Value!.Secret;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", secret);
        Assert.Equal(SessionCredential.Hash(secret), waiting.SecretHash);
        Assert.False(waiting.IsPending);
        Assert.Equal("Accueil", result.Value.Terminal.Name);
        Assert.False(result.Value.Terminal.IsPending);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Device_That_Was_Already_A_Borne_Retires_The_Old_One_When_It_Claims_The_New_One()
    {
        Waiting(_receptionPc);
        var old = Claimed(ClinicId, "Ancienne borne", "old-secret");
        SignedInAs(_ines, _receptionPc);

        var result = await Claim().Handle(new ClaimBorneCommand { CurrentSecret = "old-secret" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(old.IsActive);
    }

    [Fact]
    public async Task Another_Cabinets_Borne_Cookie_Is_Never_Retired_By_A_Claim_Here()
    {
        Waiting(_receptionPc);
        var foreign = Claimed(OtherClinicId, "Ailleurs", "foreign-secret");
        SignedInAs(_ines, _receptionPc);

        var result = await Claim().Handle(new ClaimBorneCommand { CurrentSecret = "foreign-secret" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(foreign.IsActive);
    }

    [Fact]
    public async Task A_Device_Nobody_Chose_Has_Nothing_To_Claim()
    {
        SignedInAs(_ines, _receptionPc);

        var result = await Claim().Handle(new ClaimBorneCommand(), default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceRules.BorneNotWaitingCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Token_Naming_No_Session_Has_Nothing_To_Claim()
    {
        Waiting(_receptionPc);
        _context.Setup(c => c.GetUserId()).Returns(_ines.Id);
        _context.Setup(c => c.GetSessionFamilyId()).Returns((Guid?)null);

        var result = await Claim().Handle(new ClaimBorneCommand(), default);

        Assert.Equal(AttendanceRules.BorneNotWaitingCode, result.Code);
    }

    // ---- the list the admin picks from ---------------------------------------------------------------------

    [Fact]
    public async Task The_Devices_Are_The_Cabinets_Live_Sessions_With_Hers_Marked_And_The_Demo_Left_Out()
    {
        var demo = Session(_admin, StartDemoSessionCommandHandler.DeviceLabel);
        _sessions.Setup(r => r.GetLiveForClinicAsync(ClinicId, It.IsAny<DateTime>(), GetBorneDevicesQueryHandler.MaxDevices, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionFamily> { _receptionPc, demo, _adminPc });

        var result = await Devices().Handle(new GetBorneDevicesQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Collection(result.Value!,
            d =>
            {
                Assert.Equal(_receptionPc.Id, d.SessionId);
                Assert.Equal("Ines Gharbi", d.PersonName);
                Assert.Equal("Chrome sur Windows", d.DeviceLabel);
                Assert.False(d.IsThisDevice);
            },
            d =>
            {
                Assert.Equal(_adminPc.Id, d.SessionId);
                Assert.True(d.IsThisDevice);
            });
    }

    [Fact]
    public async Task Only_An_Admin_Sees_The_Cabinets_Devices()
    {
        SignedInAs(_ines, _receptionPc);

        var result = await Devices().Handle(new GetBorneDevicesQuery(), default);

        Assert.Equal(AttendanceRules.AdminOnly, result.Error);
        _sessions.Verify(r => r.GetLiveForClinicAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- the settings list ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_Borne_Waiting_For_A_Device_That_Signed_Out_Says_So_In_The_List()
    {
        var stillThere = Waiting(_receptionPc, "Accueil");
        var otherPc = Session(_ines, "Firefox sur Windows");
        var gone = Waiting(otherPc, "Salle 2");
        otherPc.End("logout");
        var working = Claimed(ClinicId, "Bureau", "s-bureau");
        _terminals.Setup(r => r.ListAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceTerminal> { stillThere, gone, working });
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<User>.Unpaged(new List<User> { _admin, _ines }));

        var result = await new GetAttendanceSettingsQueryHandler(
            _terminals.Object, new Mock<IAttendanceEntryRepository>().Object, new Mock<IStaffLeaveRequestRepository>().Object,
            _users.Object, _clinics.Object, _context.Object, _sessions.Object).Handle(new GetAttendanceSettingsQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        var rows = result.Value!.Terminals.ToDictionary(t => t.Name);
        Assert.True(rows["Accueil"].IsPending);
        Assert.False(rows["Accueil"].DeviceSignedOut);
        Assert.True(rows["Salle 2"].DeviceSignedOut);
        Assert.False(rows["Bureau"].IsPending);
        Assert.False(rows["Bureau"].DeviceSignedOut);
    }

    // ---- names, replacements and phones --------------------------------------------------------------------

    private AttendanceTerminal LiveInCabinet(string name)
    {
        var borne = Claimed(ClinicId, name, $"s-{name}");
        _terminals.Setup(r => r.GetByIdAsync(borne.Id, It.IsAny<CancellationToken>())).ReturnsAsync(borne);
        _cabinetBornes.Add(borne);
        return borne;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Name_Another_Live_Borne_Has_Is_Refused_For_Either_Device(bool chooseAnotherDevice)
    {
        LiveInCabinet("Accueil");

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand
        {
            Name = " accueil ",
            DeviceSessionId = chooseAnotherDevice ? _receptionPc.Id : null,
        }, default);

        Assert.Equal(AttendanceRules.BorneNameTakenCode, result.Code);
        Assert.Empty(_added);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Borne_Being_Replaced_Does_Not_Hold_Its_Own_Name()
    {
        var dead = LiveInCabinet("Accueil");

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand
        {
            Name = "Accueil", ReplacingTerminalId = dead.Id, DeviceSessionId = _receptionPc.Id,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(dead.IsActive);
        Assert.True(Assert.Single(_added).IsPending);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Choosing_The_Same_Device_Again_Frees_The_Name_It_Was_Waiting_Under()
    {
        var earlier = Waiting(_receptionPc, "Accueil");
        _cabinetBornes.Add(earlier);

        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = _receptionPc.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(earlier.IsActive);
    }

    [Fact]
    public async Task Another_Cabinets_Borne_Is_Never_Retired_As_The_One_Replaced()
    {
        var foreign = Claimed(OtherClinicId, "Ailleurs", "foreign-secret");
        _terminals.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand
        {
            Name = "Accueil", ReplacingTerminalId = foreign.Id, DeviceSessionId = _receptionPc.Id,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(foreign.IsActive);
    }

    [Fact]
    public async Task A_Tracked_Persons_Phone_Cannot_Be_The_Borne()
    {
        var phone = Session(_ines, "Chrome sur Android");

        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = phone.Id }, default);

        Assert.Equal(AttendanceRules.BorneDevicePhoneCode, result.Code);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task An_Untracked_Doctors_Tablet_Labelled_Android_Can_Be_The_Borne()
    {
        var doctor = Account(User.RoleDoctor, "Dr Karim Hamdi");
        var tablet = Session(doctor, "Application android");

        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = tablet.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_Borne_Registered_On_The_Admins_Own_Device_Is_Live_At_Once_And_Asks_For_Missing_Schemas()
    {
        var withSchema = Account(User.RoleSecretary, "Leila Trabelsi");
        withSchema.SetAttendanceSchema("HASH:0-1-2-5");

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil" }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value!.Terminal.ActivatedAt);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceSchemaToCreate, It.IsAny<string>(),
            It.IsAny<string>(), _ines.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceSchemaToCreate, It.IsAny<string>(),
            It.IsAny<string>(), withSchema.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceSchemaToCreate, It.IsAny<string>(),
            It.IsAny<string>(), _admin.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Borne_Waiting_For_Its_Device_Announces_Nothing_Yet()
    {
        var result = await Register().Handle(
            new RegisterAttendanceTerminalCommand { Name = "Accueil", DeviceSessionId = _receptionPc.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Terminal.ActivatedAt);
        _bell.Verify(b => b.AttendanceNoticeAsync(It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Claiming_Tells_The_Admins_The_Owner_And_Whoever_Has_No_Schema()
    {
        var waiting = Waiting(_receptionPc);
        SignedInAs(_ines, _receptionPc);

        var result = await Claim().Handle(new ClaimBorneCommand(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(waiting.ActivatedAtUtc);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceBorneInstalled, "Borne installée",
            "La borne « Accueil » est installée sur l'appareil de Ines Gharbi.", null, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceBorneInstalled, It.IsAny<string>(),
            "Dr Salma Ben Youssef a fait de cet appareil la borne « Accueil ».", _ines.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceSchemaToCreate, It.IsAny<string>(),
            It.IsAny<string>(), _ines.Id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Notice_That_Fails_Never_Fails_The_Claim()
    {
        Waiting(_receptionPc);
        SignedInAs(_ines, _receptionPc);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));

        var result = await Claim().Handle(new ClaimBorneCommand(), default);

        Assert.True(result.IsSuccess, result.Error);
    }

    // ---- the devices list says what each device is ---------------------------------------------------------

    [Fact]
    public async Task The_Devices_Say_What_They_Are_Once_Per_Person_And_Never_A_Tracked_Persons_Phone()
    {
        var inesPhone = Session(_ines, "Safari sur iOS");
        var receptionAgain = Session(_ines, "Chrome sur Windows");
        var adminPhone = Session(_admin, "Chrome sur Android");
        _sessions.Setup(r => r.GetLiveForClinicAsync(ClinicId, It.IsAny<DateTime>(), GetBorneDevicesQueryHandler.MaxDevices, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionFamily> { receptionAgain, inesPhone, _receptionPc, adminPhone, _adminPc });

        var result = await Devices().Handle(new GetBorneDevicesQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Collection(result.Value!,
            d =>
            {
                Assert.Equal(receptionAgain.Id, d.SessionId);
                Assert.Equal(AttendanceRules.DeviceKindPc, d.Kind);
            },
            d =>
            {
                Assert.Equal(adminPhone.Id, d.SessionId);
                Assert.Equal(AttendanceRules.DeviceKindPhone, d.Kind);
            },
            d =>
            {
                Assert.Equal(_adminPc.Id, d.SessionId);
                Assert.True(d.IsThisDevice);
            });
    }

    [Fact]
    public async Task Her_Own_Session_Stays_This_Device_Even_When_An_Older_One_Shares_Its_Name()
    {
        var newer = Session(_admin, "Edge sur Windows");
        _sessions.Setup(r => r.GetLiveForClinicAsync(ClinicId, It.IsAny<DateTime>(), GetBorneDevicesQueryHandler.MaxDevices, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionFamily> { newer, _adminPc });

        var row = Assert.Single((await Devices().Handle(new GetBorneDevicesQuery(), default)).Value!);

        Assert.Equal(_adminPc.Id, row.SessionId);
        Assert.True(row.IsThisDevice);
    }

    // ---- a waiting borne whose session ends ------------------------------------------------------------------

    [Fact]
    public async Task Signing_Out_The_Device_A_Borne_Waits_For_Tells_The_Admins()
    {
        var anonymous = new TenantScope(NullLogger<TenantScope>.Instance);
        Waiting(_receptionPc);
        var notifier = new AttendanceNotifier(_bell.Object, _users.Object, _terminals.Object, anonymous, NullLogger<AttendanceNotifier>.Instance);

        await notifier.SessionEndedAsync(_receptionPc, default);

        Assert.Equal(ClinicId, anonymous.ClinicId);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceBorneNotReceived, "Borne non reçue",
            It.Is<string>(m => m.Contains("Ines Gharbi · Chrome sur Windows")), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signing_Out_A_Device_Nobody_Chose_Says_Nothing()
    {
        await Notifier().SessionEndedAsync(_receptionPc, default);

        _bell.Verify(b => b.AttendanceNoticeAsync(It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Sign_Out_Ends_The_Session_And_Then_Asks_About_The_Borne()
    {
        var credential = "credential-of-the-reception-pc";
        var session = new SessionFamily(_ines.Id, SessionCredential.Hash(credential), DateTime.UtcNow.AddHours(12), "Chrome sur Windows");
        _terminals.Setup(r => r.FindPendingForSessionAsync(ClinicId, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AttendanceTerminal.AssignToDevice(Guid.NewGuid(), ClinicId, "Accueil", session.Id, null, _admin.Id, DateTime.UtcNow));
        _sessions.Setup(r => r.GetByCredentialAsync(SessionCredential.Hash(credential), It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var handler = new EndSessionCommandHandler(_sessions.Object, Notifier(), _uow.Object, NullLogger<EndSessionCommandHandler>.Instance);

        var result = await handler.Handle(new EndSessionCommand { RefreshToken = credential }, default);

        Assert.True(result.IsSuccess);
        Assert.False(session.IsLive);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceBorneNotReceived, It.IsAny<string>(),
            It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- the controllers -----------------------------------------------------------------------------------

    [Fact]
    public async Task Claiming_Puts_The_New_Secret_In_The_Borne_Cookie()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ClaimBorneCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisteredTerminalDto>.Success(new RegisteredTerminalDto
            {
                Terminal = new AttendanceTerminalDto { Name = "Accueil" },
                Secret = "fresh-secret",
            }));
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{BorneCookie.Name}=old-secret";
        var controller = new PointageController(mediator.Object) { ControllerContext = new ControllerContext { HttpContext = http } };

        var result = (await controller.ClaimBorne(default)).Result;

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Accueil", Assert.IsType<AttendanceTerminalDto>(ok.Value).Name);
        var cookie = http.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.Contains($"{BorneCookie.Name}=fresh-secret", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("path=/api/pointage", cookie);
        mediator.Verify(m => m.Send(It.Is<ClaimBorneCommand>(c => c.CurrentSecret == "old-secret"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AttendanceRules.BorneNameTakenCode, StatusCodes.Status409Conflict)]
    [InlineData(AttendanceRules.BorneDevicePhoneCode, StatusCodes.Status400BadRequest)]
    public async Task A_Borne_Refusal_Keeps_Its_Code_And_Status(string code, int status)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RegisterAttendanceTerminalCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisteredTerminalDto>.Failure("Refus.", code));
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("admin-1");
        var stepUp = new Mock<IStepUpConfirmations>();
        stepUp.Setup(s => s.Consume("admin-1", AttendanceRules.RegisterTerminalStepUpAction, "token")).Returns(true);
        var controller = new TeamController(mediator.Object, context.Object, stepUp.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = (await controller.RegisterTerminal(new TeamController.RegisterTerminalRequest("Accueil", "token"), default)).Result;

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(AttendanceRules.BorneLastWhilePresentCode, StatusCodes.Status409Conflict)]
    [InlineData(AttendanceRules.NotFoundCode, StatusCodes.Status404NotFound)]
    public async Task Retiring_The_Last_Borne_While_Someone_Is_In_Is_A_Conflict(string code, int status)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RevokeAttendanceTerminalCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTerminalDto>.Failure("Refus.", code));
        var controller = new TeamController(mediator.Object, new Mock<IClinicContext>().Object, new Mock<IStepUpConfirmations>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = (await controller.RevokeTerminal(Guid.NewGuid(), default)).Result;

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(AttendanceRules.SchemaPasswordRequiredCode, StatusCodes.Status400BadRequest)]
    [InlineData(AttendanceRules.SchemaPasswordWrongCode, StatusCodes.Status400BadRequest)]
    [InlineData(ClinicAuthRefusals.TooManyAttempts, StatusCodes.Status429TooManyRequests)]
    public async Task A_Refused_Schema_Change_Keeps_Its_Code_And_Status(string code, int status)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SetMyAttendanceSchemaCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendancePersonSettingDto>.Failure("Refus.", code));
        var controller = new TeamController(mediator.Object, new Mock<IClinicContext>().Object, new Mock<IStepUpConfirmations>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = (await controller.SetMySchema(new SetMyAttendanceSchemaCommand(), default)).Result;

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Unlocking_Sends_The_Account_From_The_Route()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<UnlockAttendanceSchemaCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendancePersonSettingDto>.Success(new AttendancePersonSettingDto { UserId = "local|ines" }));
        var controller = new TeamController(mediator.Object, new Mock<IClinicContext>().Object, new Mock<IStepUpConfirmations>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = (await controller.UnlockSchema("local|ines", default)).Result;

        Assert.IsType<OkObjectResult>(result);
        mediator.Verify(m => m.Send(It.Is<UnlockAttendanceSchemaCommand>(c => c.UserId == "local|ines"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_To_Claim_Is_A_404_And_Sets_No_Cookie()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ClaimBorneCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisteredTerminalDto>.Failure(AttendanceRules.BorneNotWaiting, AttendanceRules.BorneNotWaitingCode));
        var http = new DefaultHttpContext();
        var controller = new PointageController(mediator.Object) { ControllerContext = new ControllerContext { HttpContext = http } };

        var result = (await controller.ClaimBorne(default)).Result;

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Empty(http.Response.Headers.SetCookie.ToString());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("own-secret", true)]
    public async Task Registering_Sets_The_Cookie_Only_For_The_Admins_Own_Device(string secret, bool cookieExpected)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RegisterAttendanceTerminalCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RegisteredTerminalDto>.Success(new RegisteredTerminalDto
            {
                Terminal = new AttendanceTerminalDto { Name = "Accueil", IsPending = !cookieExpected },
                Secret = secret,
            }));
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("admin-1");
        var stepUp = new Mock<IStepUpConfirmations>();
        stepUp.Setup(s => s.Consume("admin-1", AttendanceRules.RegisterTerminalStepUpAction, "token")).Returns(true);
        var http = new DefaultHttpContext();
        var controller = new TeamController(mediator.Object, context.Object, stepUp.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        var device = Guid.NewGuid();

        var result = (await controller.RegisterTerminal(
            new TeamController.RegisterTerminalRequest("Accueil", "token", DeviceSessionId: cookieExpected ? null : device), default)).Result;

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(cookieExpected, http.Response.Headers.SetCookie.ToString().Contains(BorneCookie.Name, StringComparison.Ordinal));
        mediator.Verify(m => m.Send(
            It.Is<RegisterAttendanceTerminalCommand>(c => c.DeviceSessionId == (cookieExpected ? null : device)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
