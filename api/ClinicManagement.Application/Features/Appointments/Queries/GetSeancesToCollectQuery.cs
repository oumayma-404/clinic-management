using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// « À encaisser » — the day's séances whose fiche is saved and whose money the desk still has to take
/// (`seance-au-fauteuil`). The dentist enters the acts; the secretary takes the money: this is her list.
///
/// <para>Rows come from <see cref="VisitClosureReader.ReadForCollectionAsync"/>, so they leave out exactly what
/// « À clôturer » never asks money for. What this adds is the amount: the fiche's total while nothing is billed, the
/// balance once a note is issued, and a DRAFT note named as such — a draft holds no money and must be issued from
/// the patient's « Factures », never billed a second time from here. A séance carried by a devis is listed only when
/// an échéance of that devis is due now — the same « À relancer » figure « Reste à payer » shows, read through the
/// same <c>PatientDebtLines</c>, so the two screens cannot disagree.</para>
///
/// <para>Under <c>Features/Appointments</c> and a query, for <see cref="GetVisitsToCloseQuery"/>'s two mechanical
/// reasons (no new realtime key, no broadcast on read).</para>
/// </summary>
public sealed class GetSeancesToCollectQuery : IRequest<Result<IReadOnlyList<SeanceToCollectDto>>>
{
    /// <summary>The clinic-local day, <c>YYYY-MM-DD</c>; absent means today in Tunis.</summary>
    public string? Day { get; set; }
}

public sealed class GetSeancesToCollectQueryHandler
    : IRequestHandler<GetSeancesToCollectQuery, Result<IReadOnlyList<SeanceToCollectDto>>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetSeancesToCollectQueryHandler> _logger;

    public GetSeancesToCollectQueryHandler(
        IAppointmentRepository appointmentRepository,
        IDentalRecordRepository dentalRecordRepository,
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetSeancesToCollectQueryHandler> logger)
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

    public async Task<Result<IReadOnlyList<SeanceToCollectDto>>> Handle(
        GetSeancesToCollectQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IReadOnlyList<SeanceToCollectDto>>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var clinicId = clinicResult.Value;
            var day = ClinicClock.ClinicToday();
            if (!string.IsNullOrWhiteSpace(request.Day))
            {
                if (!DateOnly.TryParseExact(request.Day, "yyyy-MM-dd", out var parsed))
                {
                    return Result<IReadOnlyList<SeanceToCollectDto>>.Failure("Date invalide.");
                }
                day = parsed.ToDateTime(TimeOnly.MinValue);
            }

            var visits = await VisitClosureReader.ReadForCollectionAsync(
                clinicId, day, _appointmentRepository, _dentalRecordRepository, _invoiceRepository,
                _treatmentPlanRepository, cancellationToken);
            if (visits.Count == 0)
            {
                return Result<IReadOnlyList<SeanceToCollectDto>>.Success(Array.Empty<SeanceToCollectDto>());
            }

            var patientIds = visits.Select(v => v.Appointment.PatientId!.Value).Distinct().ToList();
            var patients = await _patientRepository.GetByIdsAsync(clinicId, patientIds, cancellationToken);
            var roster = await AppointmentDoctorNames.ResolveRosterAsync(_doctorRepository, clinicId, cancellationToken);

            // The devis séances: what is due now on their devis, one read of the patients' notes and devis.
            var treatmentDue = await ReadTreatmentDueAsync(
                clinicId, visits.Where(v => v.CarriedByPlan).ToList(), cancellationToken);

            var rows = new List<SeanceToCollectDto>();
            foreach (var visit in visits)
            {
                if (visit.CarriedByPlan)
                {
                    if (!treatmentDue.TryGetValue(visit.Appointment.Id, out var due))
                    {
                        continue;
                    }
                    patients.TryGetValue(visit.Appointment.PatientId!.Value, out var carriedPatient);
                    rows.Add(new SeanceToCollectDto
                    {
                        AppointmentId = visit.Appointment.Id,
                        PatientId = visit.Appointment.PatientId!.Value,
                        PatientName = carriedPatient is null
                            ? string.Empty
                            : $"{carriedPatient.FirstName} {carriedPatient.LastName}".Trim(),
                        StartUtc = visit.Appointment.AppointmentDateTime,
                        PractitionerName = AppointmentDoctorNames.For(
                            visit.Appointment.DoctorId, visit.Appointment.DoctorName, roster),
                        DentalRecordId = visit.DentalRecordId,
                        Kind = SeanceCollectionKinds.Treatment,
                        AmountDue = due.Amount,
                        TreatmentPlanId = due.PlanId,
                        TreatmentPlanNumber = due.PlanNumber,
                    });
                    continue;
                }

                var kind = SeanceCollectionKinds.Unbilled;
                var amountDue = visit.FicheCost;
                string? invoiceNumber = null;
                if (visit.InvoiceId is { } invoiceId)
                {
                    var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);
                    if (invoice is null || invoice.ClinicId != clinicId || invoice.Status == InvoiceStatus.Cancelled)
                    {
                        continue;
                    }
                    if (invoice.Status == InvoiceStatus.Draft)
                    {
                        kind = SeanceCollectionKinds.DraftNote;
                        amountDue = invoice.TotalTtc;
                    }
                    else
                    {
                        // Paid in full: nothing for the desk.
                        if (invoice.Outstanding <= 0m)
                        {
                            continue;
                        }
                        kind = SeanceCollectionKinds.NoteBalance;
                        amountDue = invoice.Outstanding;
                    }
                    invoiceNumber = invoice.Number;
                }

                patients.TryGetValue(visit.Appointment.PatientId!.Value, out var patient);
                rows.Add(new SeanceToCollectDto
                {
                    AppointmentId = visit.Appointment.Id,
                    PatientId = visit.Appointment.PatientId!.Value,
                    PatientName = patient is null ? string.Empty : $"{patient.FirstName} {patient.LastName}".Trim(),
                    StartUtc = visit.Appointment.AppointmentDateTime,
                    PractitionerName = AppointmentDoctorNames.For(visit.Appointment.DoctorId, visit.Appointment.DoctorName, roster),
                    DentalRecordId = visit.DentalRecordId,
                    Kind = kind,
                    AmountDue = amountDue,
                    InvoiceId = visit.InvoiceId,
                    InvoiceNumber = invoiceNumber,
                });
            }

            return Result<IReadOnlyList<SeanceToCollectDto>>.Success(rows
                .OrderBy(r => visits.First(v => v.Appointment.Id == r.AppointmentId).Appointment.ExpectedEndUtc)
                .ThenBy(r => r.AppointmentId)
                .ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to read the séances to collect");
            return Result<IReadOnlyList<SeanceToCollectDto>>.Failure(ErrorMessages.Generic);
        }
    }

    /// <summary>
    /// For each devis séance, what is due NOW on its devis — <c>PatientDebtLine.DueNow</c>, « À relancer »'s figure:
    /// a late agreed échéance, or a finished treatment left unpaid. An échéance nobody agreed to is not due, so a
    /// séance-by-séance treatment with nothing late is not listed: the dentist decides that money at the chair.
    /// One row per devis, on its first séance of the day, so two séances of one treatment do not ask twice.
    /// </summary>
    private async Task<Dictionary<Guid, (decimal Amount, Guid PlanId, string? PlanNumber)>> ReadTreatmentDueAsync(
        Guid clinicId,
        IReadOnlyList<VisitClosureReader.CollectibleVisit> carried,
        CancellationToken cancellationToken)
    {
        var due = new Dictionary<Guid, (decimal, Guid, string?)>();
        if (carried.Count == 0)
        {
            return due;
        }

        var patientIds = carried.Select(v => v.Appointment.PatientId!.Value).Distinct().ToList();
        var invoicesByPatient = (await _invoiceRepository.GetByPatientIdsAsync(clinicId, patientIds, cancellationToken))
            .ToLookup(i => i.PatientId);
        var plansByPatient = (await _treatmentPlanRepository.GetByPatientIdsAsync(clinicId, patientIds, cancellationToken))
            .ToLookup(p => p.PatientId);
        var clinicToday = ClinicClock.ClinicToday();
        var claimedPlans = new HashSet<Guid>();

        foreach (var visit in carried.OrderBy(v => v.Appointment.ExpectedEndUtc).ThenBy(v => v.Appointment.Id))
        {
            var patientId = visit.Appointment.PatientId!.Value;
            var plans = plansByPatient[patientId].ToList();
            var (liveInvoices, debtPlans) = PatientDebtSelection.Select(invoicesByPatient[patientId], plans);
            var lines = PatientDebtLines.Project(liveInvoices, debtPlans, clinicToday);

            var itemIds = visit.Appointment.LinkedTreatmentPlanItemIds.ToHashSet();
            var visitPlans = plans.Where(p => p.Items.Any(i => itemIds.Contains(i.Id))).Select(p => p.Id).ToHashSet();
            var line = lines.FirstOrDefault(l =>
                l.Kind == PatientDebtLines.TreatmentPlanKind
                && visitPlans.Contains(l.DocumentId)
                && !claimedPlans.Contains(l.DocumentId)
                && l.DueNow > 0m);
            if (line is null)
            {
                continue;
            }

            claimedPlans.Add(line.DocumentId);
            due[visit.Appointment.Id] = (line.DueNow, line.DocumentId, line.Number);
        }

        return due;
    }
}
