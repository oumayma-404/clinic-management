using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Cameras.Commands;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// The camera registry (<c>features/camera-photo-import</c>) — what « pairing » a camera means: whose agenda its
/// photos belong to, how wrong its clock is, and where the next import starts.
///
/// <para>Two cases carry the risk. <b>Registration is an upsert</b>: the import screen registers every camera it
/// meets, so a second registration must hand back the camera already paired — with its doctor and its clock
/// correction — rather than overwrite them. And <b>the « last import » mark moves forward only</b>: another PC
/// importing an old card must not make the next import re-offer a week of photos.</para>
/// </summary>
public class ClinicCameraCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OurDoctor = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TheirDoctor = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly string Fingerprint = new('f', 64);

    private readonly Mock<IClinicCameraRepository> _cameras = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();

    public ClinicCameraCommandTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _doctors.Setup(d => d.GetByIdAsync(OurDoctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(OurDoctor, ClinicId, "Sarra", "Mansour", "General"));
        _doctors.Setup(d => d.GetByIdAsync(TheirDoctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(TheirDoctor, Guid.NewGuid(), "Autre", "Cabinet", "General"));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private ClinicCamera APairedCamera(Guid? clinicId = null)
    {
        var camera = ClinicCamera.Register(Guid.NewGuid(), clinicId ?? ClinicId, Fingerprint, "Canon", "Canon EOS 250D",
            "4521", new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));
        _cameras.Setup(c => c.GetByIdAsync(camera.Id, It.IsAny<CancellationToken>())).ReturnsAsync(camera);
        return camera;
    }

    private RegisterClinicCameraCommandHandler Register() =>
        new(_cameras.Object, _doctors.Object, _uow.Object, _resolver.Object);

    private UpdateClinicCameraCommandHandler Update() =>
        new(_cameras.Object, _doctors.Object, _uow.Object, _resolver.Object);

    private RecordCameraImportCommandHandler RecordImport() => new(_cameras.Object, _uow.Object, _resolver.Object);

    // ── Pairing ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_New_Camera_Is_Paired_With_The_Doctor_Named()
    {
        ClinicCamera? added = null;
        _cameras.Setup(c => c.AddAsync(It.IsAny<ClinicCamera>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicCamera, CancellationToken>((c, _) => added = c);

        var result = await Register().Handle(new RegisterClinicCameraCommand
        {
            Fingerprint = Fingerprint.ToUpperInvariant(),
            Make = "Canon",
            Model = "Canon EOS 250D",
            SerialSuffix = "4521",
            DoctorId = OurDoctor,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(added);
        Assert.Equal(Fingerprint, added!.Fingerprint);
        Assert.Equal(OurDoctor, added.DoctorId);
        Assert.Equal("Canon EOS 250D", result.Value!.Label);
    }

    [Fact]
    public async Task Meeting_A_Paired_Camera_Again_Changes_Nothing_About_It()
    {
        var paired = APairedCamera();
        paired.AssignDoctor(OurDoctor);
        paired.SetClockOffset(-62);
        _cameras.Setup(c => c.GetByFingerprintAsync(ClinicId, Fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paired);

        // Another PC registers it with no doctor: the pairing already made must stand.
        var result = await Register().Handle(new RegisterClinicCameraCommand
        {
            Fingerprint = Fingerprint, Make = "Canon", Model = "Canon EOS 250D",
        }, CancellationToken.None);

        Assert.Equal(paired.Id, result.Value!.Id);
        Assert.Equal(OurDoctor, result.Value.DoctorId);
        Assert.Equal(-62, result.Value.ClockOffsetMinutes);
        _cameras.Verify(c => c.AddAsync(It.IsAny<ClinicCamera>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Doctor_Of_Another_Cabinet_Cannot_Own_A_Camera()
    {
        var result = await Register().Handle(new RegisterClinicCameraCommand
        {
            Fingerprint = Fingerprint, Make = "Canon", Model = "EOS", DoctorId = TheirDoctor,
        }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.CameraDoctorUnknownCode, result.Code);
        _cameras.Verify(c => c.AddAsync(It.IsAny<ClinicCamera>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Fingerprint_That_Is_Not_A_Hash_Is_Refused()
    {
        var result = await Register().Handle(new RegisterClinicCameraCommand
        {
            Fingerprint = "Canon EOS 250D", Make = "Canon", Model = "EOS",
        }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.InvalidKeyCode, result.Code);
    }

    // ── The settings card ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_Edit_That_Omits_The_Doctor_Leaves_It_Alone()
    {
        var camera = APairedCamera();
        camera.AssignDoctor(OurDoctor);

        var result = await Update().Handle(
            new UpdateClinicCameraCommand { CameraId = camera.Id, Label = "Appareil du fauteuil 2", Version = 3 },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Appareil du fauteuil 2", camera.Label);
        Assert.Equal(OurDoctor, camera.DoctorId);
        _uow.Verify(u => u.SetExpectedVersion(camera, 3u), Times.Once);
    }

    [Fact]
    public async Task An_Explicit_Null_Doctor_Makes_It_The_Cabinets_Camera()
    {
        var camera = APairedCamera();
        camera.AssignDoctor(OurDoctor);

        await Update().Handle(
            new UpdateClinicCameraCommand { CameraId = camera.Id, DoctorId = null, Version = 3 },
            CancellationToken.None);

        Assert.Null(camera.DoctorId);
    }

    [Fact]
    public async Task Another_Cabinets_Camera_Reads_As_Not_Found()
    {
        var foreign = APairedCamera(Guid.NewGuid());

        var result = await Update().Handle(
            new UpdateClinicCameraCommand { CameraId = foreign.Id, Label = "Volé", Version = 1 },
            CancellationToken.None);

        Assert.Equal(CameraImportRefusals.CameraNotFoundCode, result.Code);
        Assert.NotEqual("Volé", foreign.Label);
    }

    // ── « Ranger » reporting back ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_Import_Moves_The_Mark_Forward_And_Remembers_The_Clock_Correction()
    {
        var camera = APairedCamera();
        var shot = ClinicClock.ToClinicLocal(DateTime.UtcNow.AddHours(-3));

        var result = await RecordImport().Handle(new RecordCameraImportCommand
        {
            CameraId = camera.Id,
            LatestCapturedAtLocal = DateTime.SpecifyKind(shot, DateTimeKind.Unspecified),
            ClockOffsetMinutes = 58,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(camera.LastImportedAt);
        Assert.Equal(58, camera.ClockOffsetMinutes);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Older_Card_Imported_Elsewhere_Does_Not_Move_The_Mark_Back()
    {
        var camera = APairedCamera();
        var recent = DateTime.UtcNow.AddHours(-1);
        camera.RecordImport(recent);

        await RecordImport().Handle(new RecordCameraImportCommand
        {
            CameraId = camera.Id,
            LatestCapturedAtLocal = DateTime.SpecifyKind(
                ClinicClock.ToClinicLocal(DateTime.UtcNow.AddDays(-6)), DateTimeKind.Unspecified),
        }, CancellationToken.None);

        Assert.Equal(recent, camera.LastImportedAt);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Latest_Shot_In_The_Future_Is_Ignored()
    {
        var camera = APairedCamera();

        await RecordImport().Handle(new RecordCameraImportCommand
        {
            CameraId = camera.Id,
            LatestCapturedAtLocal = DateTime.SpecifyKind(
                ClinicClock.ToClinicLocal(DateTime.UtcNow.AddDays(3)), DateTimeKind.Unspecified),
        }, CancellationToken.None);

        // A mark in the future would hide every photo taken before it from the next import.
        Assert.Null(camera.LastImportedAt);
    }

    [Fact]
    public async Task Forgetting_A_Camera_Already_Gone_Is_Not_An_Error()
    {
        var handler = new ForgetClinicCameraCommandHandler(_cameras.Object, _uow.Object, _resolver.Object);

        var result = await handler.Handle(new ForgetClinicCameraCommand { CameraId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _cameras.Verify(c => c.DeleteAsync(It.IsAny<ClinicCamera>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
