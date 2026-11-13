using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Application.Features.PhotoImport.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.PhotoImport;

/// <summary>
/// The two reads behind « Importer les photos » (<c>features/camera-photo-import</c>). The context read's value is
/// what it hands the browser's matcher — clinic wall-clock times, late arrivals folded into the expected end, and the
/// fiche save times that tell who was really in the chair — so most cases assert the shape of that answer.
/// The visit SQL itself (cancelled / absent / blocked slots excluded) is out of this suite's reach: a mocked
/// repository applies no predicate, so what is held here is that every argument reaches it verbatim.
/// </summary>
public class PhotoImportQueriesTests
{
    private static readonly Guid ClinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid DoctorA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DoctorB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IPatientFileRepository> _files = new();

    public PhotoImportQueriesTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _doctors.Setup(d => d.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Doctor(DoctorA, ClinicId, "Sarra", "Mansour", "General"),
                new Doctor(DoctorB, ClinicId, "Karim", "Ayari", "General"),
            });
        _appointments.Setup(a => a.GetPhotoImportVisitsAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PhotoImportVisitRow>());
        _records.Setup(r => r.GetFicheSavesAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FicheSaveRow>());
    }

    private GetPhotoImportContextQueryHandler ContextHandler() =>
        new(_appointments.Object, _records.Object, _doctors.Object, _resolver.Object, _context.Object);

    [Theory]
    [InlineData("", "2026-11-12")]
    [InlineData("12/11/2026", "2026-11-12")]
    [InlineData("2026-11-13", "2026-11-12")]
    public async Task An_Unreadable_Range_Is_Refused(string from, string to)
    {
        var result = await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = from, ToDay = to }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.RangeInvalidCode, result.Code);
    }

    [Fact]
    public async Task A_Range_Longer_Than_A_Month_Is_Refused()
    {
        var result = await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = "2026-10-01", ToDay = "2026-11-12" }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.RangeTooLongCode, result.Code);
    }

    [Fact]
    public async Task Visits_Come_Back_In_Wall_Clock_With_The_Late_Arrival_In_The_Expected_End()
    {
        var appointment = Guid.NewGuid();
        // 09:00 Tunis = 08:00 UTC; a 30-minute visit with a 15-minute late arrival is expected to end at 09:45.
        _appointments.Setup(a => a.GetPhotoImportVisitsAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PhotoImportVisitRow(appointment, Guid.NewGuid(), "Amira Ben Salah", DoctorA,
                    new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30), 15,
                    AppointmentStatus.Completed),
            });

        var result = await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = "2026-11-12", ToDay = "2026-11-12" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var visit = Assert.Single(result.Value!.Visits);
        Assert.Equal(new DateTime(2026, 11, 12, 9, 0, 0), visit.Start);
        Assert.Equal(DateTimeKind.Unspecified, visit.Start.Kind);
        Assert.Equal(new DateTime(2026, 11, 12, 9, 45, 0), visit.ExpectedEnd);
        Assert.Equal("Completed", visit.Status);
        Assert.Equal(60, result.Value.ClinicUtcOffsetMinutes);
    }

    [Fact]
    public async Task The_Range_Covers_The_Clinics_Days_And_The_Doctor_Reaches_The_Read()
    {
        DateTime? from = null;
        DateTime? to = null;
        Guid? doctor = null;
        _appointments.Setup(a => a.GetPhotoImportVisitsAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, DateTime, Guid?, CancellationToken>((_, f, t, d, _) => (from, to, doctor) = (f, t, d))
            .ReturnsAsync(Array.Empty<PhotoImportVisitRow>());

        await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = "2026-11-12", ToDay = "2026-11-13", DoctorId = DoctorB },
            CancellationToken.None);

        // A clinic day starts an hour before UTC midnight, and the second day's end is exclusive.
        Assert.Equal(new DateTime(2026, 11, 11, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 11, 13, 23, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(DoctorB, doctor);
    }

    [Fact]
    public async Task With_One_Agenda_Chosen_Only_Its_Fiches_And_Unattributed_Walk_Ins_Count()
    {
        var visitOfA = Guid.NewGuid();
        _appointments.Setup(a => a.GetPhotoImportVisitsAsync(
                ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), DoctorA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PhotoImportVisitRow(visitOfA, Guid.NewGuid(), "Amira", DoctorA,
                    new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30), null,
                    AppointmentStatus.Completed),
            });
        var saved = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        _records.Setup(r => r.GetFicheSavesAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FicheSaveRow(Guid.NewGuid(), Guid.NewGuid(), "Of A's visit", visitOfA, null, saved),
                new FicheSaveRow(Guid.NewGuid(), Guid.NewGuid(), "B's patient", Guid.NewGuid(), DoctorB, saved),
                new FicheSaveRow(Guid.NewGuid(), Guid.NewGuid(), "A walk-in of A", null, DoctorA, saved),
                new FicheSaveRow(Guid.NewGuid(), Guid.NewGuid(), "A walk-in of nobody", null, null, saved),
            });

        var result = await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = "2026-11-12", ToDay = "2026-11-12", DoctorId = DoctorA },
            CancellationToken.None);

        Assert.Equal(
            new[] { "A walk-in of A", "A walk-in of nobody", "Of A's visit" },
            result.Value!.Fiches.Select(f => f.PatientName).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task The_Signed_In_Doctor_Is_Named_So_Their_Agenda_Is_The_Default()
    {
        _context.Setup(c => c.GetUserId()).Returns("local|abc");
        var mine = new Doctor(DoctorB, ClinicId, "Karim", "Ayari", "General");
        _doctors.Setup(d => d.GetByUserIdAsync("local|abc", It.IsAny<CancellationToken>())).ReturnsAsync(mine);

        var result = await ContextHandler().Handle(
            new GetPhotoImportContextQuery { FromDay = "2026-11-12", ToDay = "2026-11-12" }, CancellationToken.None);

        Assert.Equal(DoctorB, result.Value!.MyDoctorId);
        Assert.Equal(2, result.Value.Doctors.Count);
    }

    // ── Known shots ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Keys_Are_Normalised_And_Deduplicated_Before_The_Read()
    {
        IReadOnlyCollection<string>? asked = null;
        _files.Setup(f => f.GetImportedCapturesAsync(ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<string>, CancellationToken>((_, keys, _) => asked = keys)
            .ReturnsAsync(Array.Empty<ImportedCaptureRow>());

        var handler = new GetImportedCaptureKeysQueryHandler(_files.Object, _resolver.Object);
        await handler.Handle(new GetImportedCaptureKeysQuery
        {
            CaptureKeys = new[] { new string('A', 64), new string('a', 64), "garbage", new string('b', 64) },
        }, CancellationToken.None);

        Assert.Equal(new[] { new string('a', 64), new string('b', 64) }, asked!.ToArray());
    }

    [Fact]
    public async Task More_Keys_Than_One_Request_Carries_Are_Refused()
    {
        var keys = Enumerable.Range(0, CameraImportRefusals.MaxKeysPerRequest + 1)
            .Select(i => i.ToString("x64"))
            .ToList();

        var handler = new GetImportedCaptureKeysQueryHandler(_files.Object, _resolver.Object);
        var result = await handler.Handle(new GetImportedCaptureKeysQuery { CaptureKeys = keys }, CancellationToken.None);

        Assert.Equal(CameraImportRefusals.TooManyKeysCode, result.Code);
    }
}
