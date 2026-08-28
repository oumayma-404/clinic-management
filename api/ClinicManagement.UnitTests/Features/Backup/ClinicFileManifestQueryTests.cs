using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Backup;

/// <summary>
/// The cabinet-wide file manifest (<c>patient-file-mirror</c>) — the read a machine keeping a browsable copy
/// walks to work out what it is missing.
///
/// <para>⚠️ <b>AC-1 is asserted on the argument the handler passes, not on what comes back.</b> Nothing in this
/// project touches a database, so the EF query filter is not in play here and a test that only checked the
/// returned rows would pass against a handler that read every clinic on the platform. What can be checked — and
/// is the thing that would actually break — is that the clinic reaching the repository is the one resolved from
/// the caller's own account row, never a claim and never absent.</para>
/// </summary>
public class ClinicFileManifestQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IPatientFileRepository> _files = new();

    private GetClinicFileManifestQueryHandler Handler() =>
        new(_users.Object, _context.Object, _files.Object);

    private static User Local(string role, Guid clinicId) =>
        User.CreateLocalUser(clinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void AsCaller(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private void ReturnsNothing() =>
        _files.Setup(f => f.GetClinicManifestPageAsync(
                It.IsAny<Guid>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<ClinicFileManifestRow>.Unpaged(new List<ClinicFileManifestRow>()));

    // [AC-1] The clinic read is the caller's own, resolved from the account row.
    [Fact]
    public async Task Handle_Should_Read_Only_The_Callers_Own_Clinic()
    {
        AsCaller(Local("admin", ClinicId));
        ReturnsNothing();

        var result = await Handler().Handle(new GetClinicFileManifestQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _files.Verify(
            f => f.GetClinicManifestPageAsync(ClinicId, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _files.Verify(
            f => f.GetClinicManifestPageAsync(OtherClinicId, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [AC-1] An admin of another cabinet reads THEIR files, never this one's — the same handler, a different
    // account row. This is the direct assertion the spec asks for rather than a reliance on the ambient filter.
    [Fact]
    public async Task Handle_Should_Follow_The_Account_Row_Across_Clinics()
    {
        AsCaller(Local("admin", OtherClinicId));
        ReturnsNothing();

        await Handler().Handle(new GetClinicFileManifestQuery(), CancellationToken.None);

        _files.Verify(
            f => f.GetClinicManifestPageAsync(OtherClinicId, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _files.Verify(
            f => f.GetClinicManifestPageAsync(ClinicId, It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // A clinic-wide listing is gated like the archive, not like the per-patient file list.
    [Theory]
    [InlineData("doctor")]
    [InlineData("secretary")]
    public async Task Handle_Should_Refuse_A_Non_Admin_Without_Reading_Anything(string role)
    {
        AsCaller(Local(role, ClinicId));
        ReturnsNothing();

        var result = await Handler().Handle(new GetClinicFileManifestQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _files.Verify(
            f => f.GetClinicManifestPageAsync(
                It.IsAny<Guid>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // An unresolvable caller must not fall through to an unscoped read. `Unset` reads zero rows with no error,
    // which is indistinguishable from a cabinet with no files — so the refusal has to happen here.
    [Fact]
    public async Task Handle_Should_Refuse_When_The_Caller_Cannot_Be_Resolved()
    {
        _context.Setup(c => c.GetUserId()).Returns((string?)null);
        ReturnsNothing();

        var result = await Handler().Handle(new GetClinicFileManifestQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _files.Verify(
            f => f.GetClinicManifestPageAsync(
                It.IsAny<Guid>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [AC-2 / the paging convention] Both nulls mean "read everything" — the first-class unpaged case, not a
    // very large page.
    [Fact]
    public async Task Handle_Should_Pass_No_Paging_When_Neither_Bound_Is_Supplied()
    {
        AsCaller(Local("admin", ClinicId));
        ReturnsNothing();

        await Handler().Handle(new GetClinicFileManifestQuery(), CancellationToken.None);

        _files.Verify(
            f => f.GetClinicManifestPageAsync(ClinicId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Forward_The_Requested_Page()
    {
        AsCaller(Local("admin", ClinicId));
        ReturnsNothing();

        await Handler().Handle(
            new GetClinicFileManifestQuery { Page = 3, PageSize = 200 }, CancellationToken.None);

        _files.Verify(
            f => f.GetClinicManifestPageAsync(
                ClinicId,
                It.Is<PageRequest?>(p => p != null && p.Value.Page == 3 && p.Value.PageSize == 200),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The manifest must never carry a storage key: it is the one field that would turn a listing into a second,
    // unguarded way to name an object in the store. A reflection guard, so adding one fails here rather than in
    // a review.
    [Fact]
    public void Manifest_Entry_Should_Expose_No_Storage_Key_Or_Clinic_Id()
    {
        var names = typeof(ClinicFileManifestEntryDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(names, n => n.Contains("StorageKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("ClinicId", StringComparison.OrdinalIgnoreCase));
    }
}
