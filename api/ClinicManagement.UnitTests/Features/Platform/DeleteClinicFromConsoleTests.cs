using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console deletes a cabinet (<c>clinic-account-removal</c>) — the product's only irreversible write, so most
/// of this file asserts what must <b>not</b> happen.
///
/// <para><b>⚠️ The two load-bearing cases are
/// <see cref="A_Refused_Deletion_Removes_No_ROW_AND_NO_FILE"/> and
/// <see cref="A_Failed_Purge_Rolls_Back_And_Leaves_The_Files_Alone"/>.</b> Every refusal happens before the
/// transaction opens and the blob sweep happens after the commit, so the failure mode this guards is a cabinet
/// that keeps its rows and loses its radiographs — which no amount of « the deletion worked » testing can
/// see.</para>
///
/// <para>⚠️ The plan itself belongs to <c>ClinicPurgePlanTests</c>; <c>IClinicPurge</c> is mocked here, because
/// what this file is about is the <i>order</i> of a handler nothing can undo.</para>
/// </summary>
public class DeleteClinicFromConsoleTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-2222-2222-2222-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Test 3";
    private const string AdminEmail = "o.benkhalifa+t3@exemple.tn";
    private const string Motif = "Cabinet de test créé pour essayer le produit — adresse à libérer";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<IClinicPurge> _purge = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly List<string> _calls = new();

    public DeleteClinicFromConsoleTests()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(SubscriptionVendorHarness.ClinicId, ClinicName, city: "Tunis"));

        GivenTheStaff(User.CreateLocalUser(
            SubscriptionVendorHarness.ClinicId, User.RoleAdmin, AdminEmail, "HASH", "Oumayma Ben Khalifa"));

        _purge
            .Setup(p => p.PurgeAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Census)
            .Callback(() => _calls.Add("purge"));

        _storage
            .Setup(s => s.DeleteByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6)
            .Callback(() => _calls.Add("files"));

        _harness.UnitOfWork
            .Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("begin"))
            .Returns(Task.CompletedTask);

        _harness.UnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("save"))
            .ReturnsAsync(1);

        _harness.UnitOfWork
            .Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("commit"))
            .Returns(Task.CompletedTask);

        _harness.UnitOfWork
            .Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("rollback"))
            .Returns(Task.CompletedTask);
    }

    private static ClinicPurgeCensus Census => new(
        new[]
        {
            new ClinicPurgeTally(nameof(Patient), "\"Patients\"", 12),
            new ClinicPurgeTally(nameof(Appointment), "\"Appointments\"", 34),
            new ClinicPurgeTally(nameof(ClinicSignup), "\"ClinicSignups\"", 1),
            new ClinicPurgeTally(nameof(User), "\"Users\"", 2),
        },
        FileBytes: 12_400_000);

    // ------------------------------------------------------------------ harness

    private void GivenTheStaff(params User[] staff) =>
        _harness.Users
            .Setup(u => u.GetByClinicIdAsync(
                SubscriptionVendorHarness.ClinicId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<User>(staff, staff.Length, 1, staff.Length));

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private DeleteClinicFromConsoleCommandHandler Handler(ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _purge.Object, _storage.Object, _ledger,
            new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<DeleteClinicFromConsoleCommandHandler>.Instance);

    private static DeleteClinicFromConsoleCommand Delete(
        string? name = ClinicName, string? reason = Motif) =>
        new() { ClinicId = SubscriptionVendorHarness.ClinicId, ConfirmationName = name, Reason = reason };

    private void AssertNothingWasTouched()
    {
        _purge.Verify(
            p => p.PurgeAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.DeleteByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _harness.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ what it exists to do

    [Fact]
    public async Task Deleting_Removes_The_Rows_Frees_The_Address_And_Records_Who_Did_It()
    {
        var result = await Handler().Handle(Delete(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClinicName, result.Value!.ClinicName);
        Assert.Equal(49, result.Value.RowsDeleted);
        Assert.Equal(6, result.Value.FilesDeleted);
        Assert.Equal(new[] { AdminEmail }, result.Value.FreedEmails);

        // The journal row is the only thing that outlives the cabinet, so it has to carry both the name and the why.
        var entry = Assert.Single(_ledger.Rows);
        Assert.Equal(PlatformAccessAction.DeletedClinic, entry.Action);
        Assert.Equal(ClinicName, entry.ClinicName);
        Assert.Equal(AccountEmail, entry.AccountEmail);
        Assert.Equal(Motif, entry.Reason);
    }

    [Fact]
    public async Task The_Addresses_Reach_The_Purge_Because_Two_Tables_Have_No_Other_Key()
    {
        await Handler().Handle(Delete(), CancellationToken.None);

        // A pending signup and a password-reset request are found by address alone. Called without them the
        // deletion succeeds, reports a plausible total, and leaves the practice's name and the administrator's
        // address behind — with the address still spoken for as far as a human can tell.
        _purge.Verify(
            p => p.PurgeAsync(
                SubscriptionVendorHarness.ClinicId,
                It.Is<IReadOnlyCollection<string>>(a => a.Contains(AdminEmail)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_Journal_Row_Is_Staged_Before_The_Save_And_The_Files_Go_After_The_Commit()
    {
        await Handler().Handle(Delete(), CancellationToken.None);

        // The order IS the contract: staged before the save, so a cabinet gone with no record of who removed it is
        // unreachable; swept after the commit, so a refused deletion cannot have taken the radiographs.
        Assert.Equal(new[] { "begin", "purge", "save", "commit", "files" }, _calls);
    }

    [Fact]
    public async Task An_Account_With_No_Password_Frees_No_Address()
    {
        // `Users.Email` is unique only where `PasswordHash` is present, and the read that refuses a signup carries
        // the same term — so listing such an account would promise an address nothing was holding.
        var doctor = User.CreateLocalUser(
            SubscriptionVendorHarness.ClinicId, User.RoleDoctor, AdminEmail, "HASH", "Oumayma");
        var cloudish = new User(
            "auth0|xyz", SubscriptionVendorHarness.ClinicId, User.RoleSecretary, "reception@exemple.tn", "Salma");

        GivenTheStaff(doctor, cloudish);

        var result = await Handler().Handle(Delete(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { AdminEmail }, result.Value!.FreedEmails);
    }

    // ------------------------------------------------------------------ what must NOT happen

    [Fact]
    public async Task A_Refused_Deletion_Removes_No_ROW_AND_NO_FILE()
    {
        foreach (var refused in new[]
                 {
                     Delete(name: "Cabinet Test 4"),
                     Delete(name: null),
                     Delete(reason: "   "),
                     Delete(reason: null),
                 })
        {
            var result = await Handler().Handle(refused, CancellationToken.None);

            Assert.True(result.IsFailure);
            AssertNothingWasTouched();
        }
    }

    [Fact]
    public async Task A_Mis_Typed_Name_And_A_Missing_Motif_Are_Told_Apart_By_Code()
    {
        var mismatch = await Handler().Handle(Delete(name: "Cabinet Test 4"), CancellationToken.None);
        var noReason = await Handler().Handle(Delete(reason: null), CancellationToken.None);

        Assert.Equal(ClinicDeletionRefusals.NameMismatchCode, mismatch.Code);
        Assert.Equal(ClinicDeletionRefusals.ReasonRequiredCode, noReason.Code);

        // Neither refusal spells the expected name out: the point of typing it is that the vendor reads the row
        // they are about to destroy.
        Assert.DoesNotContain(ClinicName, mismatch.Error);
    }

    [Fact]
    public async Task An_Unknown_Cabinet_Is_A_Named_Refusal_And_Not_A_Silent_Success()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await Handler().Handle(Delete(), CancellationToken.None);

        Assert.Equal(ClinicDeletionRefusals.UnknownClinicCode, result.Code);
        AssertNothingWasTouched();
    }

    [Fact]
    public async Task A_Failed_Purge_Rolls_Back_And_Leaves_The_Files_Alone()
    {
        _purge
            .Setup(p => p.PurgeAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("des lignes sont restées"));

        var result = await Handler().Handle(Delete(), CancellationToken.None);

        Assert.True(result.IsFailure);
        // Said in the refusal, because « la suppression a échoué » on its own leaves a vendor wondering whether
        // the cabinet is now half gone.
        Assert.Contains("rien n'a été supprimé", result.Error);
        Assert.Contains("rollback", _calls);
        _storage.Verify(s => s.DeleteByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_ledger.Rows);
    }

    [Fact]
    public async Task Files_That_Cannot_Be_Cleared_Do_Not_Un_Delete_The_Cabinet()
    {
        _storage
            .Setup(s => s.DeleteByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("MinIO unreachable"));

        var result = await Handler().Handle(Delete(), CancellationToken.None);

        // The rows are committed by then. An unremovable blob is wasted bytes, not a wrong record — and reporting
        // failure here would send the vendor back to delete a cabinet that no longer exists.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.FilesDeleted);
        Assert.Single(_ledger.Rows);
    }

    [Fact]
    public async Task An_Undeclared_Cross_Clinic_Scope_Throws_Rather_Than_Deleting_Nothing_Quietly()
    {
        // EC-12: an unset scope reads zero rows with no error, which here would report every cabinet in the
        // deployment as unknown — and, worse, a census of zero for one that is about to be emptied.
        var undeclared = new TenantScope(NullLogger<TenantScope>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(
            () => Handler(undeclared).Handle(Delete(), CancellationToken.None));

        AssertNothingWasTouched();
    }

    // ------------------------------------------------------------------ the typed name

    [Theory]
    [InlineData("Cabinet Test 3")]
    [InlineData("  Cabinet Test 3  ")]
    [InlineData("cabinet test 3")]
    [InlineData("Cabinet  Test   3")]
    public void A_Name_Read_Off_The_Panel_Is_Accepted_However_It_Was_Typed(string typed) =>
        Assert.True(ClinicDeletionRefusals.NamesTheClinic(typed, ClinicName));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Cabinet Test")]
    [InlineData("Cabinet Test 33")]
    public void Anything_That_Does_Not_Name_This_Cabinet_Is_Refused(string? typed) =>
        Assert.False(ClinicDeletionRefusals.NamesTheClinic(typed, ClinicName));
}
