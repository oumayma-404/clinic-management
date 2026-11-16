using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.PhotoImport.Queries;

/// <summary>One booked visit, in clinic wall-clock — what a burst of photos is matched against.</summary>
public sealed record PhotoImportVisitDto(
    Guid AppointmentId,
    Guid PatientId,
    string PatientName,
    Guid? DoctorId,
    DateTime Start,
    DateTime ExpectedEnd,
    int? LateArrivalMinutes,
    string Status);

/// <summary>When a fiche was first saved, in clinic wall-clock — the evidence of who was in the chair.</summary>
public sealed record PhotoImportFicheDto(
    Guid DentalRecordId,
    Guid PatientId,
    string PatientName,
    Guid? AppointmentId,
    Guid? DoctorId,
    DateTime SavedAt);

public sealed record PhotoImportDoctorDto(Guid Id, string Name);

/// <summary>
/// Everything the browser's matcher needs for a date range, in one read (<c>features/camera-photo-import</c>).
///
/// <para>⚠️ <b>Every time is clinic wall-clock with no offset</b>, because a camera's EXIF time is one: comparing the two
/// needs no timezone arithmetic in the browser, and the one place that knows Tunisia is UTC+1 does the conversion.
/// <see cref="ClinicUtcOffsetMinutes"/> exists for the rare camera that writes <c>OffsetTimeOriginal</c>.</para>
/// </summary>
public sealed record PhotoImportContextDto(
    IReadOnlyList<PhotoImportVisitDto> Visits,
    IReadOnlyList<PhotoImportFicheDto> Fiches,
    IReadOnlyList<PhotoImportDoctorDto> Doctors,
    Guid? MyDoctorId,
    int ClinicUtcOffsetMinutes);

public class GetPhotoImportContextQuery : IRequest<Result<PhotoImportContextDto>>
{
    /// <summary>The first clinic day, <c>YYYY-MM-DD</c>.</summary>
    public string FromDay { get; set; } = string.Empty;

    /// <summary>The last clinic day, inclusive, <c>YYYY-MM-DD</c>.</summary>
    public string ToDay { get; set; } = string.Empty;

    /// <summary>One practitioner's agenda, or null for every visit of the cabinet.</summary>
    public Guid? DoctorId { get; set; }
}

public class GetPhotoImportContextQueryHandler : IRequestHandler<GetPhotoImportContextQuery, Result<PhotoImportContextDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDentalRecordRepository _records;
    private readonly IDoctorRepository _doctors;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;

    public GetPhotoImportContextQueryHandler(
        IAppointmentRepository appointments,
        IDentalRecordRepository records,
        IDoctorRepository doctors,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext)
    {
        _appointments = appointments;
        _records = records;
        _doctors = doctors;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PhotoImportContextDto>> Handle(
        GetPhotoImportContextQuery request, CancellationToken cancellationToken)
    {
        try
        {
            if (!TryParseDay(request.FromDay, out var fromDay) || !TryParseDay(request.ToDay, out var toDay) || toDay < fromDay)
            {
                return Result<PhotoImportContextDto>.Failure(
                    CameraImportRefusals.RangeInvalid(), CameraImportRefusals.RangeInvalidCode);
            }

            if ((toDay - fromDay).TotalDays + 1 > CameraImportRefusals.MaxRangeDays)
            {
                return Result<PhotoImportContextDto>.Failure(
                    CameraImportRefusals.RangeTooLong(), CameraImportRefusals.RangeTooLongCode);
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<PhotoImportContextDto>.Failure(clinic.Error ?? "Unable to resolve current clinic");
            }

            var fromUtc = ClinicClock.StartOfLocalDayUtc(fromDay);
            var toUtcExclusive = ClinicClock.EndOfLocalDayUtc(toDay);

            var visitRows = await _appointments.GetPhotoImportVisitsAsync(
                clinic.Value, fromUtc, toUtcExclusive, request.DoctorId, cancellationToken);
            var ficheRows = await _records.GetFicheSavesAsync(clinic.Value, fromUtc, toUtcExclusive, cancellationToken);

            var visits = visitRows.Select(ToDto).ToList();
            var visitIds = visits.Select(v => v.AppointmentId).ToHashSet();

            // With one agenda chosen, a fiche counts when it documents one of its visits, when it names that
            // practitioner, or when it names nobody (a walk-in charted by reception could be anyone's).
            var fiches = ficheRows
                .Where(row => request.DoctorId is null
                              || (row.AppointmentId is { } appointment && visitIds.Contains(appointment))
                              || row.DoctorId == request.DoctorId
                              || (row.DoctorId is null && row.AppointmentId is null))
                .Select(row => new PhotoImportFicheDto(
                    row.DentalRecordId,
                    row.PatientId,
                    row.PatientName,
                    row.AppointmentId,
                    row.DoctorId,
                    WallClock(row.SavedAtUtc)))
                .ToList();

            var roster = await _doctors.GetByClinicIdAsync(clinic.Value, cancellationToken);
            var doctors = roster
                .Where(doctor => doctor.IsActive)
                .OrderBy(doctor => doctor.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(doctor => doctor.Id)
                .Select(doctor => new PhotoImportDoctorDto(doctor.Id, doctor.FullName))
                .ToList();

            Guid? myDoctorId = null;
            var userId = _clinicContext.GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                var mine = await _doctors.GetByUserIdAsync(userId, cancellationToken);
                if (mine is not null && mine.ClinicId == clinic.Value && mine.IsActive)
                {
                    myDoctorId = mine.Id;
                }
            }

            var offset = (int)Math.Round((ClinicClock.ToClinicLocal(fromUtc) - fromUtc).TotalMinutes);

            return Result<PhotoImportContextDto>.Success(
                new PhotoImportContextDto(visits, fiches, doctors, myDoctorId, offset));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PhotoImportContextDto>.Failure(ErrorMessages.Generic, ex);
        }
    }

    private static PhotoImportVisitDto ToDto(PhotoImportVisitRow row) => new(
        row.AppointmentId,
        row.PatientId,
        row.PatientName,
        row.DoctorId,
        WallClock(row.StartUtc),
        WallClock(Appointment.ExpectedEnd(row.StartUtc, row.Duration, row.LateArrivalMinutes)),
        row.LateArrivalMinutes,
        row.Status.ToString());

    /// <summary>Clinic wall-clock with <c>Unspecified</c> kind, so it serialises with no offset.</summary>
    private static DateTime WallClock(DateTime utc) =>
        DateTime.SpecifyKind(ClinicClock.ToClinicLocal(utc), DateTimeKind.Unspecified);

    private static bool TryParseDay(string? value, out DateTime day) =>
        DateTime.TryParseExact(
            value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
