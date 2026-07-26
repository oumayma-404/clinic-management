using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetTreatmentPlanQueryHandler> _logger;

    public GetTreatmentPlanQueryHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetTreatmentPlanQueryHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _invoiceRepository = invoiceRepository;
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
                DateTime.UtcNow, cancellationToken);

            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName(), workflow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du chargement du plan de traitement.");
        }
    }
}
