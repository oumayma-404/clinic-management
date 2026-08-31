using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// « Exporter » on the agenda — the same filtered list as a CSV, <b>and recorded</b>.
///
/// <para>The agenda's CSV carries the patient's name beside the acts of the séance and the appointment's
/// free-text notes, so it is clinical content leaving the building in bulk. It had no rate limit and no audit
/// row: nothing could answer « who took the agenda, and for which dates? ».</para>
///
/// <para>⚠️ <b>No step-up here, unlike the patient roster, and the asymmetry is deliberate.</b> The roster is the
/// cabinet's identified medical dataset — date de naissance, adresse, identifiant CNAM, antécédents, allergies —
/// and is exported occasionally; the agenda is a date range somebody prints to see who is coming tomorrow, and
/// putting a password prompt in front of a daily operation is how people learn to type one without reading it.
/// What the agenda gets is the half that costs nobody anything: it is bounded and it is attributable.</para>
///
/// <para>Shape and reasoning otherwise identical to <c>ExportPatientsQuery</c> — a Query that writes, reusing the
/// list read rather than repeating its filters, so only the export path records.</para>
/// </summary>
public class ExportAppointmentsQuery : IRequest<Result<IReadOnlyList<AppointmentDto>>>
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? DoctorId { get; set; }
}

public class ExportAppointmentsQueryHandler
    : IRequestHandler<ExportAppointmentsQuery, Result<IReadOnlyList<AppointmentDto>>>
{
    private readonly ISender _sender;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _auditActor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExportAppointmentsQueryHandler> _logger;

    public ExportAppointmentsQueryHandler(
        ISender sender,
        ICurrentClinicResolver clinicResolver,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider auditActor,
        IUnitOfWork unitOfWork,
        ILogger<ExportAppointmentsQueryHandler> logger)
    {
        _sender = sender;
        _clinicResolver = clinicResolver;
        _auditEntries = auditEntries;
        _auditActor = auditActor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> Handle(
        ExportAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var clinicId = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicId.IsFailure)
        {
            return Result<IReadOnlyList<AppointmentDto>>.Failure(clinicId.Error!);
        }

        var rows = await _sender.Send(
            new GetAppointmentsQuery
            {
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                DoctorId = request.DoctorId,
            },
            cancellationToken);

        if (rows.IsFailure)
        {
            return Result<IReadOnlyList<AppointmentDto>>.Failure(rows.Error!, rows.Code);
        }

        var items = rows.Value!.ToList();

        try
        {
            await ListExportLedger.RecordAsync(
                _auditEntries,
                _unitOfWork,
                _auditActor.Current,
                clinicId.Value,
                ListExportLedger.AppointmentEntityType,
                "Agenda",
                items.Count,
                DescribeFilters(request),
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Refused an agenda CSV export for clinic {ClinicId}: the ledger row failed.", clinicId.Value);
            return Result<IReadOnlyList<AppointmentDto>>.Failure(
                ListExportLedger.UnrecordableMessage, ListExportLedger.UnrecordableCode);
        }

        return Result<IReadOnlyList<AppointmentDto>>.Success(items);
    }

    /// <summary>
    /// Which narrowing was in force. <b>The dates ARE recorded</b>, unlike the roster's search term — a date
    /// range is not a patient's name, and « which dates were taken » is the whole substance of what an agenda
    /// export was. A practitioner filter records only that one was applied, not which.
    /// </summary>
    private static string DescribeFilters(ExportAppointmentsQuery request)
    {
        var parts = new List<string>();

        if (request.StartDate.HasValue || request.EndDate.HasValue)
        {
            var from = request.StartDate?.ToString("yyyy-MM-dd") ?? "début";
            var to = request.EndDate?.ToString("yyyy-MM-dd") ?? "fin";
            parts.Add($"du {from} au {to}");
        }

        if (request.DoctorId.HasValue)
        {
            parts.Add("un praticien");
        }

        return parts.Count == 0 ? "sans filtre (tout l'agenda)" : string.Join(", ", parts);
    }
}
