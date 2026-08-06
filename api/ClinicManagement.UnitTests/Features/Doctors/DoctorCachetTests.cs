using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Doctors.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Doctors;

/// <summary>
/// <see cref="UpdateDoctorProfileCommandHandler"/> — per-doctor cachet + ordre with own-or-admin authorization
/// (Part B, FR-3.1). An admin may edit any doctor in their clinic; a doctor may edit only their own record;
/// a non-admin editing someone else's record is rejected before storage is ever touched. The uploaded
/// content type is persisted verbatim (not hardcoded like the clinic-logo path).
/// </summary>
public class DoctorCachetTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private UpdateDoctorProfileCommandHandler Handler() =>
        new(_doctors.Object, _users.Object, _context.Object, _storage.Object, _uow.Object,
            NullLogger<UpdateDoctorProfileCommandHandler>.Instance);

    private User SetUpUser(string role)
    {
        var user = User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", role);
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private static Doctor DoctorIn(Guid clinicId, string? linkedUserId = null)
    {
        var doctor = new Doctor(Guid.NewGuid(), clinicId, "Amine", "Khelifi", "Chirurgien-dentiste");
        if (linkedUserId != null) doctor.LinkToUser(linkedUserId);
        return doctor;
    }

    // Echoes what the real backends return: the composed key, not the clinic-relative path handed in — so a
    // caller that stopped passing a clinic would be visible here rather than in production (US-5).
    private void SetUpUploadEcho() =>
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, Guid clinicId, string path, CancellationToken _) =>
                ClinicStorageKey.Compose(clinicId, path));

    private void SaveSucceeds() =>
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

    /// <summary>
    /// A byte sequence that starts with the real <b>PNG</b> signature.
    ///
    /// This used to be <c>{ 1, 2, 3 }</c>, which stopped working once the cachet upload began verifying magic
    /// bytes — a declared content type is trivially spoofable, so the handler checks the bytes actually start
    /// with a PNG or JPEG signature before trusting it. The fixture was never updated, leaving four tests red
    /// on an assertion (<c>result.IsSuccess</c>) that gave no hint of the cause. The production code was
    /// correct throughout; only the fixture was stale.
    ///
    /// A PNG signature satisfies the check for both declared types, because the handler asks "is this a valid
    /// PNG or JPEG", not "does the signature match the declared type".
    /// </summary>
    private static MemoryStream Image() =>
        new(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D });

    // [CACHET-1] An admin can set another doctor's ordre number + cachet.
    [Fact]
    public async Task Admin_Can_Set_Any_Doctors_Ordre_And_Cachet()
    {
        SetUpUser("admin");
        var target = DoctorIn(ClinicId, linkedUserId: "local|someone-else");
        _doctors.Setup(r => r.GetByIdAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        SetUpUploadEcho();
        SaveSucceeds();

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = target.Id,
            OrdreNumberCnomdt = "D-04-9",
            CachetStream = Image(),
            CachetContentType = "image/png"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("D-04-9", target.OrdreNumberCnomdt);
        // [US-5] The key is the doctor's path under their own clinic — the handler no longer writes the clinic
        // segment itself, so this is what proves it still reaches the storage.
        Assert.Equal($"clinics/{ClinicId}/doctors/{target.Id}/cachet", target.CachetStorageKey);
        _storage.Verify(s => s.UploadAsync(
            It.IsAny<Stream>(), "image/png", ClinicId, $"doctors/{target.Id}/cachet", It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [CACHET-2] A doctor can set their own cachet (DoctorId null → resolves to their own record).
    [Fact]
    public async Task Doctor_Can_Set_Own_Cachet()
    {
        var user = SetUpUser("doctor");
        var own = DoctorIn(ClinicId, linkedUserId: user.Id);
        _doctors.Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        SetUpUploadEcho();
        SaveSucceeds();

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = null,
            CachetStream = Image(),
            CachetContentType = "image/png"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(own.CachetStorageKey);
    }

    // [CACHET-3] A non-admin cannot set another doctor's cachet — rejected before storage is touched.
    [Fact]
    public async Task NonAdmin_Cannot_Set_Another_Doctors_Cachet()
    {
        SetUpUser("doctor");
        var foreign = DoctorIn(ClinicId, linkedUserId: "local|another-doctor");
        _doctors.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = foreign.Id,
            CachetStream = Image(),
            CachetContentType = "image/png"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _storage.Verify(s => s.UploadAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [CACHET-4] The uploaded content type is persisted verbatim (png / jpeg), not hardcoded.
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public async Task Cachet_Persists_Actual_ContentType(string contentType)
    {
        var user = SetUpUser("doctor");
        var own = DoctorIn(ClinicId, linkedUserId: user.Id);
        _doctors.Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        SetUpUploadEcho();
        SaveSucceeds();

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = null,
            CachetStream = Image(),
            CachetContentType = contentType
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(contentType, own.CachetContentType);
    }

    // [CACHET-5] Removing the cachet clears both fields and deletes the blob.
    [Fact]
    public async Task Remove_Cachet_Clears_Key_And_ContentType()
    {
        var user = SetUpUser("doctor");
        var own = DoctorIn(ClinicId, linkedUserId: user.Id);
        own.SetCachet("clinic/doctors/x/cachet", "image/png");
        _doctors.Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);
        SaveSucceeds();

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = null,
            RemoveCachet = true
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(own.CachetStorageKey);
        Assert.Null(own.CachetContentType);
        _storage.Verify(s => s.DeleteAsync("clinic/doctors/x/cachet", It.IsAny<CancellationToken>()), Times.Once);
    }

    // [CACHET-6] Bytes that are not actually a PNG/JPEG are refused, whatever the declared Content-Type says.
    // Nothing reaches storage. This guard shipped without a test — the fixture that would have covered it was
    // itself sending invalid bytes, so its failure read as noise rather than as coverage.
    [Fact]
    public async Task A_Spoofed_Content_Type_Is_Rejected_Before_Storage()
    {
        var user = SetUpUser("doctor");
        var own = DoctorIn(ClinicId, linkedUserId: user.Id);
        _doctors.Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(own);

        var result = await Handler().Handle(new UpdateDoctorProfileCommand
        {
            DoctorId = null,
            CachetStream = new MemoryStream(new byte[] { 0x3C, 0x73, 0x76, 0x67 }),   // "<svg"
            CachetContentType = "image/png"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(own.CachetStorageKey);
        _storage.Verify(
            s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
