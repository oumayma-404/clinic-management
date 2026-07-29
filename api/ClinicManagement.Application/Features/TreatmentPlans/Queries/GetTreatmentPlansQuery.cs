using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>List the clinic's treatment plans, filtered by patient / status / created-date range.</summary>
public class GetTreatmentPlansQuery : IRequest<Result<List<TreatmentPlanDto>>>
{
    public Guid? PatientId { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>
    /// Optional bounds on <c>AcceptedDate</c> — deliberately separate from <see cref="From"/>/<see cref="To"/>,
    /// which bound the creation date. The dashboard's « Devis acceptés » KPI counts by acceptance, so its
    /// drill-through has to filter by the same date or the list would not contain the devis the card counted.
    /// </summary>
    public DateTime? AcceptedFrom { get; set; }
    public DateTime? AcceptedTo { get; set; }
}

public class GetTreatmentPlansQueryHandler : IRequestHandler<GetTreatmentPlansQuery, Result<List<TreatmentPlanDto>>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetTreatmentPlansQueryHandler> _logger;

    public GetTreatmentPlansQueryHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetTreatmentPlansQueryHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<TreatmentPlanDto>>> Handle(GetTreatmentPlansQuery request, CancellationToken cancellationToken)
    {
        try
        {
            TreatmentPlanStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<TreatmentPlanStatus>(request.Status, ignoreCase: true, out var parsed))
                {
                    return Result<List<TreatmentPlanDto>>.Failure("Statut de plan invalide.");
                }
                status = parsed;
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<TreatmentPlanDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plans = (await _planRepository.GetFilteredAsync(
                clinicId, request.PatientId, status, request.From, request.To,
                request.AcceptedFrom, request.AcceptedTo, cancellationToken)).ToList();

            // One query for patient names, mapped by id (a clinic's patient set is small) — mirrors
            // GetInvoicesQuery rather than a GetByIdAsync per distinct patient.
            // includeArchived: this resolves NAMES, it is not a picker. An archived patient's devis must still
            // show who they belong to.
            var patients = await _patientRepository.GetByClinicIdAsync(
                clinicId, includeArchived: true, cancellationToken: cancellationToken);
            var names = patients.ToDictionary(p => p.Id, p => p.GetFullName());

            // Derived scheduling + devis→facture read-back for the whole page: two batched queries total,
            // never one per plan or per patient.
            var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
                plans, clinicId, _appointmentRepository, _invoiceRepository, DateTime.UtcNow, cancellationToken);

            var dtos = plans
                .Select(p => p.ToDto(names.TryGetValue(p.PatientId, out var name) ? name : null, workflow))
                .ToList();

            return Result<List<TreatmentPlanDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error listing treatment plans");
            return Result<List<TreatmentPlanDto>>.Failure("Erreur lors du chargement des plans de traitement.");
        }
    }
}
