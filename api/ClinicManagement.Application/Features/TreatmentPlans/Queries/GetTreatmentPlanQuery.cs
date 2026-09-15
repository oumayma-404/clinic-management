using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>Get a single treatment plan (with items + installments). Tenant-checked.</summary>
public class GetTreatmentPlanQuery : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
}

public class GetTreatmentPlanQueryHandler : IRequestHandler<GetTreatmentPlanQuery, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetTreatmentPlanQueryHandler> _logger;

    public GetTreatmentPlanQueryHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        IDentalRecordRepository dentalRecordRepository,
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetTreatmentPlanQueryHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _invoiceRepository = invoiceRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _doctorRepository = doctorRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(GetTreatmentPlanQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);

            var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
                new[] { plan }, clinicResult.Value, _appointmentRepository, _invoiceRepository,
                // The record repository fills `TreatedToothNumbers` — the teeth the act's earlier séances marked.
                // The doctor repository fills `DoctorName` - the practitioner the devis is attributed to (M6).
                DateTime.UtcNow, cancellationToken, _dentalRecordRepository, _doctorRepository);

            var dto = plan.ToDto(patient?.GetFullName(), workflow);

            /*
             * S7 — « cet acte est déjà sur un autre devis ». One extra read, bounded to this patient's plans,
             * and only when this devis itself carries debt: a Draft, a cancelled or a written-off one produces
             * no second claim, so there is nothing to warn about and nothing to read.
             *
             * ⚠️ A notice, never a refusal — a second opinion legitimately re-quotes. See
             * `DuplicateActDetection` for why it keys on the procedure + the tooth and not on the désignation.
             */
            if (PlanBillingRules.CarriesDebt(plan.Status))
            {
                var siblings = await _planRepository.GetFilteredAsync(
                    clinicResult.Value, patientId: plan.PatientId, paging: null,
                    cancellationToken: cancellationToken);

                dto.DuplicateActs = DuplicateActDetection.Find(plan, siblings.Items)
                    .Select(d => new DuplicateActDto
                    {
                        ItemId = d.ItemId,
                        DesignationFr = d.DesignationFr,
                        ToothNumbers = d.ToothNumbers.ToList(),
                        OtherPlanId = d.OtherPlanId,
                        OtherPlanNumber = d.OtherPlanNumber,
                        OtherPlanTitle = d.OtherPlanTitle,
                    })
                    .ToList();
            }

            return Result<TreatmentPlanDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error loading treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du chargement du plan de traitement.");
        }
    }
}
