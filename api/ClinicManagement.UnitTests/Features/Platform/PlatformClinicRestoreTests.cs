using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The vendor puts a cabinet back that no longer exists (<c>clinic-data-archive-and-restore</c> AC-6, AC-9, DEV-4)
/// — the only path that works when the practice's own accounts are gone too.
///
/// <para><b>The load-bearing case is <see cref="The_Cabinet_Is_Restored_At_The_Archives_Own_Clinic_Id"/>.</b>
/// Provisioning a cabinet first and restoring into it compiles, reads correctly and is wrong: the archive's own
/// <c>Clinic</c> row would then be « présent mais différent » and — correctly, per AC-4 — <b>skipped</b>, so the
/// practice would come back with its patients and its money but a blank name, no billing settings and no working
/// hours. Nothing else in the suite can see that; every other assertion would still pass.</para>
///
/// <para><b>The second is <see cref="A_Cabinet_That_Is_Still_Live_Is_Refused"/>.</b> That is not the cabinet path's
/// clinic-id check under another name: here the archive belongs to exactly the right practice and the practice is
/// <i>already there</i>, at which point its own admin can restore it with their own eyes on the result, and the
/// vendor minting a second administrator into a working cabinet is the wrong move whatever the archive says.</para>
/// </summary>
public class PlatformClinicRestoreTests
{
    private static readonly Guid ArchivedClinic = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ConsoleAccount = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string ClinicName = "Cabinet Ben Ali";
    private const string AdminEmail = "proprietaire@cabinet.tn";

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPolicy> _policy = new();
    private readonly Mock<ILocalAuthService> _localAuth = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly FakeBlobStore _blobs = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    /// <summary>
    /// ⚠️ The <c>Clinic</c> entry carries a real row at the archived id, because the console door now checks that
    /// before it stages anything: the manifest's clinic id is the file's *claim*, while the row that lands comes
    /// from this entry's own <c>Id</c>, and nothing used to tie the two together.
    /// </summary>
    private readonly FakeArchiveStore _store = new FakeArchiveStore()
        .Table("Clinic", $"[{{\"Id\":\"dddddddd-dddd-dddd-dddd-dddddddddddd\"}}]", rows: 1,
            outcome: new ClinicArchiveTableOutcome(1, 0, 0))
        .Table("Patient", "[]", rows: 3, outcome: new ClinicArchiveTableOutcome(3, 0, 0));

    private readonly List<User> _created = new();
    private readonly List<ClinicSubscription> _entitlements = new();

    public PlatformClinicRestoreTests()
    {
        _policy.SetupGet(p => p.RequiresSubscription).Returns(true);
        _policy.SetupGet(p => p.TrialDays).Returns(30);

        _localAuth.Setup(a => a.GenerateTemporaryPassword()).Returns("Temp-9x4Kq2");
        _localAuth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("HASH");

        _users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => _created.Add(user))
            .Returns(Task.CompletedTask);

        _subscriptions.Setup(s => s.AddAsync(It.IsAny<ClinicSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicSubscription, CancellationToken>((entitlement, _) => _entitlements.Add(entitlement))
            .Returns(Task.CompletedTask);
        _subscriptions.Setup(s => s.AddEntryAsync(It.IsAny<SubscriptionPeriod>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Gone when the console looks, back once its rows have been restored — which is the whole shape of DEV-4.
        _clinics.SetupSequence(c => c.GetByIdAsync(ArchivedClinic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null)
            .ReturnsAsync(new Clinic(ArchivedClinic, ClinicName, city: "Tunis"));
    }

    // ------------------------------------------------------------------ harness

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);

        return scope;
    }

    private readonly FakeAuditEntryRepository _auditEntries = new();

    private RestoreClinicFromArchiveCommandHandler Handler(ITenantScope? scope = null) => new(
        _clinics.Object, _users.Object, _subscriptions.Object, _ledger,
        new FakePlatformSession { AccountId = ConsoleAccount, Email = "vendeur@editeur.tn" },
        _policy.Object, _store, _blobs, _localAuth.Object, _unitOfWork, new ProcessAuditActorProvider(),
        _auditEntries, scope ?? SystemWideScope(),
        NullLogger<RestoreClinicFromArchiveCommandHandler>.Instance);

    /// <summary>A real archive of the vanished cabinet, written by the packager the download uses.</summary>
    private async Task<MemoryStream> ArchiveAsync()
    {
        var buffer = new MemoryStream();

        await ClinicArchivePackager.WriteAsync(
            buffer, ArchivedClinic, ClinicName, _store, _blobs, NullLogger.Instance, CancellationToken.None);

        buffer.Position = 0;
        _store.Calls.Clear();

        return buffer;
    }

    private RestoreClinicFromArchiveCommand Command(Stream? archive) => new()
    {
        Archive = archive,
        AdminEmail = AdminEmail,
        AdminFullName = "Dr Ben Ali",
    };

    // ------------------------------------------------------------------ AC-6 / DEV-4

    // [AC-6] The console's exception to the mismatch rule: it does not compare the archive against a cabinet, it
    // RE-CREATES the cabinet at the archive's own clinic id — which is what makes every restored row point at it.
    [Fact]
    public async Task The_Cabinet_Is_Restored_At_The_Archives_Own_Clinic_Id()
    {
        var result = await Handler().Handle(Command(await ArchiveAsync()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ArchivedClinic, result.Value!.ClinicId);
        Assert.Equal(ClinicName, result.Value.ClinicName);
        Assert.All(_store.RestoredIntoClinics, id => Assert.Equal(ArchivedClinic, id));
        // The Clinic row is restored like any other, and first — not provisioned, which would leave the practice
        // with its patients and a blank name.
        Assert.Equal("restore:Clinic", _store.Calls.First(c => c.StartsWith("restore:", StringComparison.Ordinal)));
    }

    // Only what an archive deliberately does NOT carry is created afterwards: the administrator (password hashes
    // do not travel in a file on a laptop) and the entitlement (the vendor's money, never the cabinet's).
    [Fact]
    public async Task An_Administrator_And_An_Entitlement_Are_Created_For_The_Restored_Cabinet()
    {
        var result = await Handler().Handle(Command(await ArchiveAsync()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var admin = Assert.Single(_created);
        Assert.Equal(ArchivedClinic, admin.ClinicId);
        Assert.Equal(User.RoleAdmin, admin.Role);
        Assert.True(admin.MustChangePassword);
        Assert.Equal("Temp-9x4Kq2", result.Value!.OneTimePassword);
        Assert.Equal(AdminEmail, result.Value.AdminEmail);

        Assert.Equal(ArchivedClinic, Assert.Single(_entitlements).ClinicId);
    }

    // [FR-5] The console records what it did, in its own ledger, in the same transaction as the rows — an
    // unattributable restore of a whole practice is the last thing that should succeed unrecorded.
    [Fact]
    public async Task The_Restore_Is_Recorded_In_The_Consoles_Own_Journal()
    {
        await Handler().Handle(Command(await ArchiveAsync()), CancellationToken.None);

        var entry = Assert.Single(_ledger.Rows);

        Assert.Equal(PlatformAccessAction.RestoredClinic, entry.Action);
        Assert.Equal(ArchivedClinic, entry.ClinicId);
        Assert.Equal(ClinicName, entry.ClinicName);
        Assert.Equal(ConsoleAccount, entry.PlatformAccountId);
    }

    // [US-7] What the console gets back carries the restore's per-entity counts as NAMED rows. The report's own
    // dictionaries would reach PlatformReadShape as `Key`/`Value`, and pre-approving those two names on this
    // surface would admit any future dictionary — including one whose values are patient names.
    [Fact]
    public async Task The_Response_Reports_What_Was_Restored_Per_Entity()
    {
        var result = await Handler().Handle(Command(await ArchiveAsync()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var patients = Assert.Single(result.Value!.Tables, t => t.Entity == "Patient");
        Assert.Equal(3, patients.Restored);
        Assert.Equal(0, patients.Conflicts);
        Assert.Contains(result.Value.Tables, t => t.Entity == "Clinic");
    }

    // ------------------------------------------------------------------ the refusals

    // A cabinet that is still live is a conflict with the current state, not a malformed request — and its own
    // admin can restore it from « Paramètres ». Refused by code, with nothing written.
    [Fact]
    public async Task A_Cabinet_That_Is_Still_Live_Is_Refused()
    {
        var archive = await ArchiveAsync();

        _clinics.Setup(c => c.GetByIdAsync(ArchivedClinic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ArchivedClinic, ClinicName, city: "Tunis"));

        var result = await Handler().Handle(Command(archive), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.ClinicExistsCode, result.Code);
        Assert.Contains(ClinicName, result.Error!, StringComparison.Ordinal);
        AssertNothingWasWritten();
    }

    // An archive that puts back no Clinic row leaves nothing to hang an administrator or an entitlement off —
    // modelled here as the cabinet still being absent once the rows have landed. Refused rather than invented: a
    // cabinet whose own record we made up is not the cabinet the practice archived.
    [Fact]
    public async Task An_Archive_That_Puts_Back_No_Cabinet_Record_Cannot_Re_Create_One()
    {
        _clinics.Reset();
        _clinics.Setup(c => c.GetByIdAsync(ArchivedClinic, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await Handler().Handle(Command(await ArchiveAsync()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, result.Code);
        Assert.Empty(_created);
        Assert.Empty(_ledger.Rows);
    }

    // The archive is an unencrypted zip the practice holds, so by the time it comes back it is untrusted input:
    // the manifest's clinic id is a *claim*, while the row that lands comes from `data/Clinic.json`'s own `Id`,
    // and nothing tied the two together. A hand-edited manifest therefore drove the live-cabinet guard on one id
    // and the insert on another — and the « no Clinic row » refusal below only fired AFTER the commit, leaving a
    // practice's patients and money back under an id nothing points at, no administrator, no entitlement, and
    // every retry answered « ce cabinet existe toujours ». Refused before anything is staged.
    [Fact]
    public async Task An_Archive_Whose_Cabinet_Row_Is_Not_The_One_It_Announces_Is_Refused_Before_Anything_Is_Staged()
    {
        var store = new FakeArchiveStore()
            .Table("Clinic", "[{\"Id\":\"eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee\"}]", rows: 1,
                outcome: new ClinicArchiveTableOutcome(1, 0, 0));

        var buffer = new MemoryStream();
        await ClinicArchivePackager.WriteAsync(
            buffer, ArchivedClinic, ClinicName, store, _blobs, NullLogger.Instance, CancellationToken.None);
        buffer.Position = 0;
        store.Calls.Clear();

        var handler = new RestoreClinicFromArchiveCommandHandler(
            _clinics.Object, _users.Object, _subscriptions.Object, _ledger,
            new FakePlatformSession { AccountId = ConsoleAccount, Email = "vendeur@editeur.tn" },
            _policy.Object, store, _blobs, _localAuth.Object, _unitOfWork, new ProcessAuditActorProvider(),
            _auditEntries, SystemWideScope(),
            NullLogger<RestoreClinicFromArchiveCommandHandler>.Instance);

        var result = await handler.Handle(Command(buffer), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, result.Code);
        Assert.DoesNotContain(store.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
        Assert.Empty(_created);
        Assert.Empty(_ledger.Rows);
    }

    // Caught here rather than at the partial unique index on the lowercased email, which would surface as a 500
    // after the cabinet's rows had already been written.
    [Fact]
    public async Task An_Address_That_Already_Has_An_Account_Is_Refused_Before_Anything_Is_Written()
    {
        var archive = await ArchiveAsync();

        _users.Setup(u => u.GetByEmailAsync(AdminEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(User.CreateLocalUser(ArchivedClinic, User.RoleAdmin, AdminEmail, "HASH", "Quelqu'un"));

        var result = await Handler().Handle(Command(archive), CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
    }

    // The restored cabinet needs somebody to sign in as, so both fields are mandatory — and nothing is read from
    // the file before that is known.
    [Theory]
    [InlineData(null, "Dr Ben Ali")]
    [InlineData(AdminEmail, "  ")]
    public async Task The_New_Administrators_Identity_Is_Mandatory(string? email, string? fullName)
    {
        var command = new RestoreClinicFromArchiveCommand
        {
            Archive = await ArchiveAsync(),
            AdminEmail = email,
            AdminFullName = fullName,
        };

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasWritten();
    }

    [Fact]
    public async Task A_File_That_Is_Not_An_Archive_Is_Refused()
    {
        var result = await Handler().Handle(
            Command(new MemoryStream(new byte[] { 9, 9, 9, 9 })), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicArchiveFormat.InvalidCode, result.Code);
        AssertNothingWasWritten();
    }

    // [EC-12] A handler reached with no declared cross-clinic scope THROWS rather than reading zero rows: here an
    // undeclared scope would report a live cabinet as absent and re-create it on top of itself.
    [Fact]
    public async Task An_Undeclared_Tenant_Scope_Stops_The_Restore()
    {
        var archive = await ArchiveAsync();
        var handler = Handler(new TenantScope(NullLogger<TenantScope>.Instance));

        await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(Command(archive), CancellationToken.None));

        AssertNothingWasWritten();
    }

    private void AssertNothingWasWritten()
    {
        Assert.DoesNotContain(_store.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
        Assert.Equal(0, _unitOfWork.Saves);
        Assert.Empty(_created);
        Assert.Empty(_entitlements);
        Assert.Empty(_ledger.Rows);
    }
}
