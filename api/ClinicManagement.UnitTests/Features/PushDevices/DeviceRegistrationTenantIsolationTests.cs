using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.PushDevices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PushDevices;

/// <summary>
/// Tenant isolation for the push registry (<c>mobile-native-shells</c> Part 6, AC-41, AC-53).
///
/// <para><b>This table's isolation is genuinely two-sided, which is why it needs its own class rather than a copy
/// of the usual « another clinic's row reads as not-found » shape.</b> The token is <b>globally unique</b>, so the
/// registration lookup deliberately crosses clinics — refusing would turn a rebind into a unique-index violation,
/// i.e. a 500 for the ordinary shared-tablet case. What must hold is that crossing it only ever moves the row
/// <i>to</i> the caller, and never lets the caller read, keep or retire another clinic's binding.</para>
/// </summary>
public class DeviceRegistrationTenantIsolationTests
{
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string UserA = "local|user-a";
    private const string UserB = "local|user-b";
    private const string Token = "shared-tablet-token";

    private sealed class Harness
    {
        public Mock<IDeviceRegistrationRepository> Devices { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();
        public Mock<IClinicContext> Context { get; } = new();
        public List<DeviceRegistration> Added { get; } = new();
        public List<DeviceRegistration> Updated { get; } = new();

        public Harness(Guid callerClinic, string callerUser, DeviceRegistration? existing = null)
        {
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(callerClinic));
            Context.Setup(c => c.GetUserId()).Returns(callerUser);

            Devices.Setup(d => d.GetByTokenAcrossClinicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            Devices.Setup(d => d.AddAsync(It.IsAny<DeviceRegistration>(), It.IsAny<CancellationToken>()))
                .Callback<DeviceRegistration, CancellationToken>((d, _) => Added.Add(d))
                .Returns(Task.CompletedTask);
            Devices.Setup(d => d.UpdateAsync(It.IsAny<DeviceRegistration>(), It.IsAny<CancellationToken>()))
                .Callback<DeviceRegistration, CancellationToken>((d, _) => Updated.Add(d))
                .Returns(Task.CompletedTask);
        }

        private Mock<IUnitOfWork> UnitOfWork()
        {
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            return unitOfWork;
        }

        public RegisterPushDeviceCommandHandler Register(bool pushSupported = true)
        {
            var availability = new Mock<IOsPushAvailability>();
            availability.Setup(a => a.SupportsPush(It.IsAny<DevicePlatform>())).Returns(pushSupported);
            availability.Setup(a => a.UnavailableReason(It.IsAny<DevicePlatform>()))
                .Returns(pushSupported ? null : "Notifications système indisponibles sur cette installation.");

            return new RegisterPushDeviceCommandHandler(
                Resolver.Object, Context.Object, Devices.Object, UnitOfWork().Object, availability.Object,
                NullLogger<RegisterPushDeviceCommandHandler>.Instance);
        }

        public DeletePushDeviceCommandHandler Delete() =>
            new(Resolver.Object, Context.Object, Devices.Object, UnitOfWork().Object,
                NullLogger<DeletePushDeviceCommandHandler>.Instance);
    }

    private static DeviceRegistration Existing(Guid clinicId, string userId) =>
        DeviceRegistration.Create(clinicId, userId, DevicePlatform.Android, Token, "1.0.0", DateTime.UtcNow);

    private static RegisterPushDeviceCommand Command() => new()
    {
        Platform = DevicePlatform.Android,
        Token = Token,
        ShellVersion = "1.1.0"
    };

    // ---- AC-41: rebinding is a write, never a conflict --------------------------------------------

    // [AC-41] The shared reception tablet: the colleague signs in and the OS hands the app the same token. No 409,
    // and the row MOVES rather than a second one being created — otherwise the person who signed out keeps
    // receiving notifications on a device somebody else is holding.
    [Fact]
    public async Task Registering_A_Token_Bound_To_Another_User_Rebinds_It_Without_A_Conflict()
    {
        var existing = Existing(ClinicA, UserA);
        var harness = new Harness(ClinicA, UserB, existing);

        var result = await harness.Register().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ReboundFromAnotherUser);
        Assert.Empty(harness.Added);
        Assert.Equal(UserB, Assert.Single(harness.Updated).UserId);
    }

    // [AC-41] A token already bound to the caller is a refresh, not a rebind — the shell cannot tell them apart and
    // sends the same call at every sign-in.
    [Fact]
    public async Task Registering_A_Token_Bound_To_The_Caller_Is_A_Refresh()
    {
        var existing = Existing(ClinicA, UserA);
        var harness = new Harness(ClinicA, UserA, existing);

        var result = await harness.Register().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.ReboundFromAnotherUser);
        Assert.Equal("1.1.0", Assert.Single(harness.Updated).ShellVersion);
    }

    // [AC-53] The cross-clinic case, and the one that matters most: a practitioner working at two practices carries
    // one phone. Registering there moves the row to the CALLER's clinic — it never leaves the device answering to
    // the other practice, and the caller learns nothing about it beyond their own registration.
    [Fact]
    public async Task Registering_A_Token_Held_By_Another_Clinic_Moves_It_To_The_Callers_Clinic()
    {
        var existing = Existing(ClinicB, UserB);
        var harness = new Harness(ClinicA, UserA, existing);

        var result = await harness.Register().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rebound = Assert.Single(harness.Updated);
        Assert.Equal(ClinicA, rebound.ClinicId);
        Assert.Equal(UserA, rebound.UserId);
    }

    // ---- AC-53: deregistration must NOT cross clinics ---------------------------------------------

    // [AC-53] The asymmetry with registration, stated as a test because it looks like an inconsistency and is not:
    // registration crosses clinics because the write must not collide, while deregistration crossing them would let
    // any caller silently unsubscribe another clinic's device.
    [Fact]
    public async Task Deregistering_Another_Clinics_Device_Changes_Nothing()
    {
        var existing = Existing(ClinicB, UserB);
        var harness = new Harness(ClinicA, UserA, existing);

        var result = await harness.Delete().Handle(
            new DeletePushDeviceCommand { Token = Token }, CancellationToken.None);

        // Success, not a 404: sign-out is fired while the session is already being torn down, so a refusal it
        // cannot act on would only produce a French error on the way out of the app.
        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Updated);
        Assert.True(existing.IsActive);
    }

    // A colleague who has since signed in on the same shared tablet owns that registration now, so the previous
    // user's late sign-out must not unsubscribe them.
    [Fact]
    public async Task Deregistering_A_Token_Since_Rebound_To_A_Colleague_Changes_Nothing()
    {
        var existing = Existing(ClinicA, UserB);
        var harness = new Harness(ClinicA, UserA, existing);

        var result = await harness.Delete().Handle(
            new DeletePushDeviceCommand { Token = Token }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Updated);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task Deregistering_The_Callers_Own_Device_Deactivates_It()
    {
        var existing = Existing(ClinicA, UserA);
        var harness = new Harness(ClinicA, UserA, existing);

        var result = await harness.Delete().Handle(
            new DeletePushDeviceCommand { Token = Token }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(harness.Updated).IsActive);
    }

    // ---- AC-42: an unsupported platform is refused, never queued ---------------------------------

    // [AC-42] No row is created. A registration in a queue nothing can drain is worse than a refusal: the shell
    // believes it is subscribed and the only symptom is that notifications never arrive.
    [Fact]
    public async Task An_Unsupported_Platform_Is_Refused_In_French_And_No_Row_Is_Created()
    {
        var harness = new Harness(ClinicA, UserA);

        var result = await harness.Register(pushSupported: false).Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("notifications", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Added);
        Assert.Empty(harness.Updated);
    }

    // [US-2] The Unset-scope case. With no clinic resolvable the handler refuses before touching the registry — the
    // query filters would return nothing anyway, and a handler that read on regardless would be relying on that.
    [Fact]
    public async Task With_No_Clinic_In_Scope_Nothing_Is_Registered()
    {
        var harness = new Harness(ClinicA, UserA);
        harness.Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Utilisateur introuvable."));

        var result = await harness.Register().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(harness.Added);
        harness.Devices.Verify(
            d => d.GetByTokenAcrossClinicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
