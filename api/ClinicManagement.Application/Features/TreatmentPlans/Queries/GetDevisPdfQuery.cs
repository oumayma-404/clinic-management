using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Queries;

/// <summary>Render the devis (quote) PDF for a treatment plan — a non-fiscal estimate.</summary>
public class GetDevisPdfQuery : IRequest<Result<DevisPdfResult>>
{
    public Guid Id { get; set; }
}

public class GetDevisPdfQueryHandler : IRequestHandler<GetDevisPdfQuery, Result<DevisPdfResult>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetDevisPdfQueryHandler> _logger;

    public GetDevisPdfQueryHandler(
        ITreatmentPlanRepository planRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IPdfGenerationService pdfGenerationService,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetDevisPdfQueryHandler> logger)
    {
        _planRepository = planRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _pdfGenerationService = pdfGenerationService;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<DevisPdfResult>> Handle(GetDevisPdfQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DevisPdfResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<DevisPdfResult>.Failure("Plan de traitement introuvable.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);

            var data = BuildPdfData(plan, clinic, patient?.GetFullName() ?? string.Empty);
            var bytes = await _pdfGenerationService.GenerateDevisPdfAsync(data, cancellationToken);

            var suffix = string.IsNullOrWhiteSpace(plan.Number) ? plan.Id.ToString("N")[..8] : plan.Number;
            return Result<DevisPdfResult>.Success(new DevisPdfResult
            {
                Content = bytes,
                FileName = $"devis-{suffix}.pdf"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating devis PDF for plan {PlanId}", request.Id);
            return Result<DevisPdfResult>.Failure("Erreur lors de la génération du devis.");
        }
    }

    private static DevisPdfData BuildPdfData(TreatmentPlan plan, Clinic? clinic, string patientName) => new()
    {
        ClinicName = clinic?.Name ?? string.Empty,
        ClinicAddress = clinic?.Address,
        ClinicPhone = clinic?.Phone,
        MatriculeFiscal = clinic?.MatriculeFiscal,
        PatientName = patientName,
        Number = plan.Number,
        Title = plan.Title,
        Date = plan.AcceptedDate ?? plan.CreatedAt,
        Status = plan.Status.ToString(),
        TotalPlanned = plan.TotalPlanned,
        Lines = plan.Items
            .Select(i => new DevisPdfLine
            {
                CodeActe = i.CodeActe,
                Designation = i.DesignationFr,
                Teeth = i.ToothNumbers.Count > 0 ? string.Join(", ", i.ToothNumbers) : string.Empty,
                PlannedCost = i.PlannedCost
            })
            .ToList(),
        Installments = plan.Installments
            .OrderBy(i => i.DueDate)
            .Select(i => new DevisPdfInstallment
            {
                DueDate = i.DueDate,
                Amount = i.Amount
            })
            .ToList()
    };
}
