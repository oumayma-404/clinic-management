using System;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// The pure rules of camera photo import (<c>features/camera-photo-import</c>): what a capture key may look like,
/// when a camera's clock is believed, how a photo becomes a camera file, how a clinic PC's original is recorded, how
/// a file changes patient, and how the camera registry keeps its « last import » mark.
/// </summary>
public class CameraImportDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string Key = new('a', 64);
    private static readonly string OtherKey = new('b', 64);

    // ── PhotoCaptureFacts ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    public void A_Capture_Key_Is_A_Sha256(string? value, bool expected) =>
        Assert.Equal(expected, PhotoCaptureFacts.IsCaptureKey(value));

    [Fact]
    public void The_Key_Is_Normalised_To_Lower_Case()
    {
        var facts = PhotoCaptureFacts.Create(null, new string('A', 64), null, Now);
        Assert.Equal(new string('a', 64), facts.CaptureKey);
    }

    [Fact]
    public void A_Malformed_Key_Throws_As_The_Backstop() =>
        Assert.Throws<ArgumentException>(() => PhotoCaptureFacts.Create(null, "nope", null, Now));

    [Fact]
    public void A_Clock_Never_Set_Records_No_Time()
    {
        // A camera reset by a dead battery writes 2000-01-01: the photo files, the time is not believed.
        var facts = PhotoCaptureFacts.Create(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), Key, null, Now);
        Assert.Null(facts.CapturedAtUtc);
    }

    [Fact]
    public void A_Time_Far_In_The_Future_Is_Not_Believed()
    {
        var facts = PhotoCaptureFacts.Create(Now.AddHours(2), Key, null, Now);
        Assert.Null(facts.CapturedAtUtc);
    }

    [Fact]
    public void A_Camera_Running_A_Few_Minutes_Fast_Is_Still_Believed()
    {
        var facts = PhotoCaptureFacts.Create(Now.AddMinutes(20), Key, null, Now);
        Assert.Equal(Now.AddMinutes(20), facts.CapturedAtUtc);
    }

    [Fact]
    public void A_Long_Model_Is_Clipped_And_A_Blank_One_Is_Dropped()
    {
        Assert.Equal(PhotoCaptureFacts.MaxCameraModelLength,
            PhotoCaptureFacts.Create(null, Key, new string('x', 250), Now).CameraModel!.Length);
        Assert.Null(PhotoCaptureFacts.Create(null, Key, "   ", Now).CameraModel);
    }

    // ── CameraCaptureInput (the wire → the facts) ───────────────────────────────────────────────────────

    [Fact]
    public void Wall_Clock_Time_Becomes_Utc_Through_The_Clinic_Clock()
    {
        var local = new DateTime(2026, 11, 12, 9, 14, 3);
        var result = new CameraCaptureInput(Key, local, null, "Canon EOS 250D").ToFacts(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClinicClock.ToUtc(local), result.Value!.CapturedAtUtc);
        Assert.Equal("Canon EOS 250D", result.Value.CameraModel);
    }

    [Fact]
    public void A_Camera_That_Wrote_Its_Own_Offset_Is_Converted_Exactly()
    {
        // 09:14 at +02:00 is 07:14 UTC whatever the clinic's own zone is.
        var result = new CameraCaptureInput(Key, new DateTime(2026, 11, 12, 9, 14, 0), 120, null).ToFacts(Now);

        Assert.Equal(new DateTime(2026, 11, 12, 7, 14, 0, DateTimeKind.Utc), result.Value!.CapturedAtUtc);
    }

    [Fact]
    public void No_Key_Is_An_Ordinary_Upload()
    {
        var result = CameraCaptureInput.None.ToFacts(Now);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void A_Malformed_Key_Is_Refused_With_A_Code()
    {
        var result = new CameraCaptureInput("not-a-hash", null, null, null).ToFacts(Now);
        Assert.True(result.IsFailure);
        Assert.Equal(CameraImportRefusals.InvalidKeyCode, result.Code);
    }

    [Fact]
    public void An_Impossible_Offset_Is_Refused()
    {
        var result = new CameraCaptureInput(Key, new DateTime(2026, 11, 12, 9, 0, 0), 24 * 60, null).ToFacts(Now);
        Assert.Equal(CameraImportRefusals.InvalidTimeCode, result.Code);
    }

    // ── PatientFile ─────────────────────────────────────────────────────────────────────────────────────

    private static PatientFile AHostedFile(Guid? patientId = null) => new(
        Guid.NewGuid(), patientId ?? Guid.NewGuid(), Guid.NewGuid(), "IMG_4521.jpg", "clinics/x/k", "image/jpeg",
        1_400_000, FileType.Scan);

    [Fact]
    public void An_Ordinary_File_Is_An_Upload_With_No_Capture_Facts()
    {
        var file = AHostedFile();
        Assert.Equal(FileOrigin.Upload, file.Origin);
        Assert.Null(file.CaptureKey);
        Assert.Null(file.CapturedAt);
    }

    [Fact]
    public void Recording_A_Capture_Makes_It_A_Camera_Photo()
    {
        var file = AHostedFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(Now.AddHours(-2), Key, "NIKON Z 50", Now));

        Assert.Equal(FileOrigin.Camera, file.Origin);
        Assert.Equal(Key, file.CaptureKey);
        Assert.Equal(Now.AddHours(-2), file.CapturedAt);
        Assert.Equal("NIKON Z 50", file.CameraModel);
    }

    [Fact]
    public void A_Coffre_File_Cannot_Be_A_Camera_Photo()
    {
        var file = PatientFile.RegisterInVault(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "scan.dcm", "application/dicom", 10, FileType.Scan, Key);

        Assert.Throws<InvalidOperationException>(() =>
            file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, Now)));
    }

    [Fact]
    public void Only_A_Camera_Photo_Has_An_Original_At_The_Cabinet() =>
        Assert.Throws<InvalidOperationException>(() => AHostedFile().RecordOriginal(Key, 8_000_000, "PC ACCUEIL", Now));

    [Fact]
    public void An_Identical_Original_Report_Changes_Nothing()
    {
        var file = AHostedFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, Now));

        Assert.True(file.RecordOriginal(OtherKey.ToUpperInvariant(), 8_000_000, " PC ACCUEIL ", Now));
        Assert.Equal(OtherKey, file.OriginalSha256);
        Assert.Equal("PC ACCUEIL", file.OriginalHeldBy);

        // The Windows app re-reports after a restart — the second identical report must not age the row.
        Assert.False(file.RecordOriginal(OtherKey, 8_000_000, "PC ACCUEIL", Now.AddDays(1)));
        Assert.Equal(Now, file.OriginalKeptAt);
    }

    [Theory]
    [InlineData("nope", 10, "PC")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0, "PC")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 10, "  ")]
    public void A_Malformed_Original_Is_Refused(string sha, long size, string holder)
    {
        var file = AHostedFile();
        file.RecordCameraCapture(PhotoCaptureFacts.Create(null, Key, null, Now));

        Assert.Throws<ArgumentException>(() => file.RecordOriginal(sha, size, holder, Now));
    }

    [Fact]
    public void Moving_To_Another_Patient_Takes_The_Folder_Given()
    {
        var file = AHostedFile();
        var target = Guid.NewGuid();
        var folder = Guid.NewGuid();

        file.MoveToPatient(target, folder);

        Assert.Equal(target, file.PatientId);
        Assert.Equal(folder, file.FolderId);
    }

    [Fact]
    public void Moving_To_The_Same_Patient_Is_Refused()
    {
        var patient = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => AHostedFile(patient).MoveToPatient(patient, null));
    }

    // ── ClinicCamera ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Canon", "Canon EOS 250D", "Canon EOS 250D")]
    [InlineData("NIKON CORPORATION", "NIKON Z 50", "NIKON Z 50")]
    [InlineData("FUJIFILM", "X-T30", "FUJIFILM X-T30")]
    [InlineData("", "EOS R7", "EOS R7")]
    [InlineData("Canon", "", "Canon")]
    public void A_Camera_Is_Named_After_What_It_Says_About_Itself(string make, string model, string expected) =>
        Assert.Equal(expected, ClinicCamera.DefaultLabel(make, model));

    [Fact]
    public void A_Camera_Needs_A_Make_Or_A_Model() =>
        Assert.Throws<ArgumentException>(() =>
            ClinicCamera.Register(Guid.NewGuid(), Guid.NewGuid(), Key, " ", " ", null, Now));

    [Fact]
    public void The_Last_Import_Moves_Forward_Only()
    {
        var camera = ClinicCamera.Register(Guid.NewGuid(), Guid.NewGuid(), Key, "Canon", "Canon EOS 90D", "1234", Now);

        Assert.True(camera.RecordImport(Now));
        // Another PC importing an older card afterwards must not move the mark back.
        Assert.False(camera.RecordImport(Now.AddDays(-1)));
        Assert.Equal(Now, camera.LastImportedAt);
        Assert.True(camera.RecordImport(Now.AddHours(1)));
    }

    [Fact]
    public void A_Clock_Off_By_More_Than_A_Day_Is_Not_Corrected_Here()
    {
        var camera = ClinicCamera.Register(Guid.NewGuid(), Guid.NewGuid(), Key, "Canon", "EOS", null, Now);

        camera.SetClockOffset(-62);
        Assert.Equal(-62, camera.ClockOffsetMinutes);
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.SetClockOffset(ClinicCamera.MaxClockOffsetMinutes + 1));
    }

    [Fact]
    public void An_Empty_Doctor_Means_Nobody()
    {
        var camera = ClinicCamera.Register(Guid.NewGuid(), Guid.NewGuid(), Key, "Canon", "EOS", null, Now);
        camera.AssignDoctor(Guid.Empty);
        Assert.Null(camera.DoctorId);
    }
}
