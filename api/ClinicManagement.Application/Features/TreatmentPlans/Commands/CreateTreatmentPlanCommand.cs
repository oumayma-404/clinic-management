using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Create a treatment plan (devis) with its act lines + optional installment schedule, and **accept it
/// immediately** — it gets its number and is live from the moment it is created.
/// <para>
/// There is no separate « Accepter le devis » step any more. A dentist writing a plan has already agreed it with
/// the patient sitting in front of them, so the draft stage was a second confirmation of a decision already made,
/// and it silently held the plan out of « Solde patient » and « Créances » until someone remembered to press a
/// button. Corrections happen afterwards through <c>AmendTreatmentPlanCommand</c>, which can revise an act's fee
/// **in place** (keeping its id, so appointment and fiche links survive) as well as add and remove acts.
/// </para>
/// <para>
/// Two consequences worth knowing. (a) A **number is consumed** per created plan — the sequence stays gapless, so
/// a plan created by mistake is <c>Cancel</c>led (its number kept, motif recorded), never deleted;
/// <c>CanBeDeleted</c> is Draft-only and no new plan is a Draft. (b) <c>Accept</c> requires at least one act and
/// auto-creates a single lump-sum échéance when no schedule was supplied, so an empty plan is now refused at
/// creation rather than saved and left unusable.
/// </para>
/// </summary>
public class CreateTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PatientId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<TreatmentPlanItemRequest> Items { get; set; } = new();
    public List<InstallmentRequest> Installments { get; set; } = new();
}

public class CreateTreatmentPlanCommandHandler : IRequestHandler<CreateTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalActCodeRepository _dentalActRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateTreatmentPlanCommandHandler> _logger;

    public CreateTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IDentalActCodeRepository dentalActRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CreateTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _dentalActRepository = dentalActRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(CreateTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Patient introuvable.");
            }

            var plan = new TreatmentPlan(Guid.NewGuid(), clinicId, request.PatientId, request.Title, request.Notes);
            var items = await TreatmentPlanItemPricing.ResolveAsync(request.Items, clinicId, _dentalActRepository, cancellationToken);
            plan.SetItems(items);
            plan.SetInstallments(request.Installments.Select(i => (i.DueDate, i.Amount)));

            // Numbered, accepted and committed in one go, through the same helper the legacy accept path uses.
            // The insert and the acceptance share a transaction by construction — there is a single
            // SaveChanges — so a numbering collision can never leave a saved-but-unnumbered plan behind.
            var accepted = await DevisNumbering.AcceptAndSaveAsync(
                plan, clinicId, _planRepository, _unitOfWork,
                ct => _planRepository.AddAsync(plan, ct),
                _logger, cancellationToken);
            if (accepted.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(accepted.Error!);
            }

            _logger.LogInformation(
                "Created treatment plan {PlanId} for patient {PatientId}, accepted as {Number}",
                plan.Id, plan.PatientId, plan.Number);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error creating treatment plan");
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la création du plan de traitement.");
        }
    }
}
