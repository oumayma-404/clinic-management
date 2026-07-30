using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>List the clinic's treatment plans, filtered by patient / status / created-date range.</summary>
public class GetTreatmentPlansQuery : IRequest<Result<PagedResult<TreatmentPlanDto>>>
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

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }
}

public class GetTreatmentPlansQueryHandler : IRequestHandler<GetTreatmentPlansQuery, Result<PagedResult<TreatmentPlanDto>>>
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

    public async Task<Result<PagedResult<TreatmentPlanDto>>> Handle(GetTreatmentPlansQuery request, CancellationToken cancellationToken)
    {
        try
        {
            TreatmentPlanStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<TreatmentPlanStatus>(request.Status, ignoreCase: true, out var parsed))
                {
                    return Result<PagedResult<TreatmentPlanDto>>.Failure("Statut de plan invalide.");
                }
                status = parsed;
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<TreatmentPlanDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var page = await _planRepository.GetFilteredAsync(
                clinicId,
                request.PatientId,
                status,
                request.From,
                request.To,
                request.AcceptedFrom,
                request.AcceptedTo,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);
            var plans = page.Items;

            // Patient names for the plans on this page only, via the batched read — mirrors GetInvoicesQuery.
            // It used to load every patient of the clinic with their flags and history collections, which would
            // have left this endpoint unbounded no matter how small the page of plans was.
            // `GetByIdsAsync` includes archived patients: this resolves names, it is not a picker, and an
            // archived patient's devis must still show who they belong to.
            var names = (await _patientRepository.GetByIdsAsync(
                    clinicId,
                    plans.Select(p => p.PatientId).Distinct().ToList(),
                    cancellationToken))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetFullName());

            // Derived scheduling + devis→facture read-back for the whole page: two batched queries total,
            // never one per plan or per patient.
            var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
                plans, clinicId, _appointmentRepository, _invoiceRepository, DateTime.UtcNow, cancellationToken);

            var dtos = page.Map(p => p.ToDto(names.TryGetValue(p.PatientId, out var name) ? name : null, workflow));

            return Result<PagedResult<TreatmentPlanDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error listing treatment plans");
            return Result<PagedResult<TreatmentPlanDto>>.Failure("Erreur lors du chargement des plans de traitement.");
        }
    }
}
