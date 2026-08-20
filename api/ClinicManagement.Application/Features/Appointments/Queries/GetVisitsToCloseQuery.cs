using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// « À clôturer » — the séances whose slot has passed and which still owe one of the three answers a clinic's
/// records depend on: est-il venu, qu'a-t-on fait, combien a-t-il payé.
///
/// <para><b>Derived, never stored.</b> There is no task table: a visit is open because a record is <i>absent</i>.
/// See <see cref="VisitClosureRules"/> for why, and for the cascade that lets a row ask one question at a time.</para>
///
/// <para><b>A query, not a command</b>, and that is mechanical as well as correct: <c>RealtimeBroadcastBehavior</c>
/// derives its key from the namespace, so this must not live under <c>.Commands</c> — a read that broadcast
/// « appointments » on every page load would make every open browser in the clinic refetch its agenda.</para>
///
/// <para><b>It stays under <c>Features/Appointments</c> for the same mechanism.</b> A <c>Features/VisitClosure</c>
/// folder would emit a <c>visitclosure</c> realtime key that <c>web/lib/realtime/clinic-hub.ts</c> does not
/// declare, and <c>RealtimeResourceResolverTests</c> compares the two sets in <b>both</b> directions.</para>
/// </summary>
public class GetVisitsToCloseQuery : IRequest<Result<PagedResult<VisitToCloseDto>>>
{
    /// <summary>
    /// How many clinic-local days back to look, including today. Clamped by <see cref="VisitClosureReader"/>.
    ///
    /// <para>A window at all, rather than « tout ce qui reste ouvert », because on the day this ships a clinic's
    /// whole history is unlinked: nothing has ever closed a visit. An unbounded first screen would be several
    /// thousand rows, which is the same as no screen.</para>
    /// </summary>
    public int? Days { get; set; }

    /// <summary>Optional practitioner filter — a dentist closing their own day in a two-chair practice.</summary>
    public Guid? DoctorId { get; set; }

    /// <summary>Null reads everything. See <c>PageRequest</c> on why that is a first-class case.</summary>
    public PageRequest? Paging { get; set; }
}

public class GetVisitsToCloseQueryHandler
    : IRequestHandler<GetVisitsToCloseQuery, Result<PagedResult<VisitToCloseDto>>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetVisitsToCloseQueryHandler> _logger;

    public GetVisitsToCloseQueryHandler(
        IAppointmentRepository appointmentRepository,
        IDentalRecordRepository dentalRecordRepository,
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetVisitsToCloseQueryHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _invoiceRepository = invoiceRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _patientRepository = patientRepository;
        _doctorRepository = doctorRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<VisitToCloseDto>>> Handle(
        GetVisitsToCloseQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<VisitToCloseDto>>.Failure(
                    clinicResult.Error ?? ErrorMessages.Generic);
            }

            var clinicId = clinicResult.Value;

            var open = await VisitClosureReader.ReadAsync(
                clinicId,
                request.Days,
                request.DoctorId,
                DateTime.UtcNow,
                _appointmentRepository,
                _dentalRecordRepository,
                _invoiceRepository,
                _treatmentPlanRepository,
                cancellationToken);

            var patientIds = open
                .Select(o => o.Appointment.PatientId!.Value)
                .Distinct()
                .ToList();

            // Batched, not one GetByIdAsync per row: that is the over-fetch « Créances » was fixed for.
            var patients = patientIds.Count == 0
                ? new Dictionary<Guid, Patient>()
                : await _patientRepository.GetByIdsAsync(clinicId, patientIds, cancellationToken);

            // Paged in memory, deliberately, and for the reason « Créances » and the « extrait de caisse » are:
            // the exact end-of-slot test and the three-way gap rule cannot be expressed in SQL, so no single
            // query knows a row's position in the list. The WINDOW is what bounds the work — see the reader.
            // The practitioner's name comes from `DoctorId`, not from the unpopulated `DoctorName` snapshot —
            // see `AppointmentDoctorNames`. One roster read per request, bounded by the clinic's own staff.
            var roster = await AppointmentDoctorNames.ResolveRosterAsync(
                _doctorRepository, clinicId, cancellationToken);

            var page = PagedResult<OpenVisit>.FromSource(open, request.Paging);

            return Result<PagedResult<VisitToCloseDto>>.Success(page.Map(o => Map(o, patients, roster)));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to read the visits awaiting closure");
            return Result<PagedResult<VisitToCloseDto>>.Failure(ErrorMessages.Generic);
        }
    }

    private static VisitToCloseDto Map(
        OpenVisit open,
        IReadOnlyDictionary<Guid, Patient> patients,
        IReadOnlyDictionary<Guid, string> roster)
    {
        var a = open.Appointment;
        var patientId = a.PatientId!.Value;

        // A patient the batch could not resolve is named honestly rather than left blank: an empty cell on a
        // worklist is indistinguishable from a rendering fault, and the visit still needs closing.
        var patientName = patients.TryGetValue(patientId, out var patient)
            ? $"{patient.FirstName} {patient.LastName}".Trim()
            : "Patient introuvable";

        return new VisitToCloseDto
        {
            AppointmentId = a.Id,
            PatientId = patientId,
            PatientName = patientName,
            AppointmentDateTime = a.AppointmentDateTime,
            DurationMinutes = (int)a.Duration.TotalMinutes,
            DoctorId = a.DoctorId,
            DoctorName = AppointmentDoctorNames.For(a.DoctorId, a.DoctorName, roster),
            Procedures = a.Procedures
                .Select(p => p.ProcedureName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList(),
            Status = a.Status.ToString(),
            PresenceAnswered = open.State.PresenceAnswered,
            FicheRecorded = open.State.FicheRecorded,
            BillingSettled = open.State.BillingSettled,
            // Never null on this list: only open visits reach it, and an open visit has a next step by definition.
            NextStep = (open.State.NextStep ?? VisitClosureStep.Presence).ToString(),
            DentalRecordId = open.DentalRecordId,
            InvoiceId = open.Invoice?.InvoiceId,
            InvoiceNumber = open.Invoice?.Number,
            NothingToBillReason = a.NothingToBillReason,
            NothingToBillAtUtc = a.NothingToBillAtUtc,
        };
    }
}
