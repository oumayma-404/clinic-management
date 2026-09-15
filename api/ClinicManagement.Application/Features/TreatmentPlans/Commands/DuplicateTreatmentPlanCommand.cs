using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Dupliquer ce devis » (S1) — the same protocol for the second implant, for the other side of the mouth, or
/// for the sibling who is having the same treatment.
///
/// <para>
/// ⚠️ <b>The copy is a <c>Draft</c> and it takes no number.</b> That is the whole reason this is safe to offer:
/// a devis number is gapless, per-clinic-per-year and released only by a cancellation carrying a motif, so a
/// duplicate that numbered itself would spend one on a proposal nobody has made yet. The copy carries no
/// échéancier, no acceptance date, no revision count, no payment and no fiche link — only what the dentist
/// would otherwise retype: the acts, their fees, their teeth, their procedures and their séances.
/// </para>
/// <para>
/// ⚠️ <b>The remise is copied and the billing marks are not.</b> A remise is part of what was quoted; a
/// <c>BilledOnInvoiceId</c> names a note d'honoraires that bills the <i>original</i> act, and carrying it onto
/// a new plan would hold the copy's line at 0 against a document that has never heard of it. Nothing sets it on
/// the new plan, so each copied act owns its own fee.
/// </para>
/// <para>
/// The patient may be changed in the same press — « la même chose pour sa sœur » — and defaults to the source's
/// when it is not, which is the common case.
/// </para>
/// </summary>
public class DuplicateTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    /// <summary>The devis to copy.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Who the copy is for. Omitted (or empty) keeps the source's patient, which is the ordinary case.
    /// </summary>
    public Guid? PatientId { get; set; }

    /// <summary>
    /// The copy's title. Blank falls back to « {titre d'origine} (copie) », so a duplicate is never
    /// indistinguishable from what it was copied from in a list.
    /// </summary>
    public string? Title { get; set; }
}

public class DuplicateTreatmentPlanCommandHandler
    : IRequestHandler<DuplicateTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DuplicateTreatmentPlanCommandHandler> _logger;

    public DuplicateTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DuplicateTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        DuplicateTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var source = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (source == null || source.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            /*
             * ⚠️ Only the ACTIVE acts are copied. A parked one is work the patient declined; carrying it into a
             * fresh proposal would quote for exactly what was put aside, and the copy has no « Remettre au
             * devis » history to explain where it came from.
             */
            var sourceItems = source.ActiveItems.OrderBy(i => i.SequenceNumber).ToList();
            if (sourceItems.Count == 0)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Ce devis ne contient aucun acte actif : il n'y a rien à dupliquer.");
            }

            var targetPatientId = request.PatientId is Guid pid && pid != Guid.Empty ? pid : source.PatientId;

            // Tenant-checked even when it is the source's own patient: one comparison, and it is what stops a
            // crafted request filing a devis under another practice's patient.
            var patient = await _patientRepository.GetByIdAsync(targetPatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Patient introuvable.");
            }

            var title = string.IsNullOrWhiteSpace(request.Title)
                ? $"{source.Title} (copie)"
                : request.Title.Trim();

            var copy = new TreatmentPlan(Guid.NewGuid(), clinicId, targetPatientId, title, source.Notes);
            copy.SetDoctor(source.DoctorId);

            copy.AddItems(sourceItems.Select(i => new TreatmentPlanItemInput(
                // ⚠️ `null`, never the source act's id — `AddItems` ignores it, but passing one states an
                // identity the copy does not have and would be read as such by the next person here.
                null,
                i.DesignationFr,
                i.PlannedCost,
                i.ProcedureTypeId,
                i.ToothNumbers.ToList())));

            /*
             * The remise and the séances, per act, matched by POSITION — both lists come from the same ordered
             * source, and `AddItems` appends in order, so index N of the copy is index N of the source. Matching
             * on the désignation instead would put one act's protocol on another whenever a devis quotes the
             * same act twice, which is the ordinary shape of a bridge.
             */
            var copied = copy.Items.OrderBy(i => i.SequenceNumber).ToList();
            for (var index = 0; index < sourceItems.Count; index++)
            {
                var from = sourceItems[index];
                var to = copied[index];

                if (from.DiscountAmount > 0m)
                {
                    to.SetDiscount(from.DiscountAmount);
                }

                // ⚠️ FOUR arguments. `MinDaysAfterPrevious` defaults to null, so a three-argument copy
                // compiles, reads correctly and erases the interval from every séance of the act — the defect
                // `StartTreatmentStepsTests` scans for. (The comment sits here rather than inside the call
                // because that scan splits the argument list on commas and counts a comment's own.)
                if (from.Steps.Count > 0)
                {
                    copy.SetItemSteps(to.Id, from.Steps.Select(s => new TreatmentPlanItemStepInput(
                        null,
                        s.Label,
                        s.EstimatedDurationMinutes,
                        s.MinDaysAfterPrevious)));
                }
            }

            // The discounts moved the acts' net cost after `AddItems` computed the total; nothing else on a
            // Draft depends on it, but leaving `TotalPlanned` gross would quote the copy above what it costs.
            copy.RespreadScheduleToTotal(ClinicClock.ClinicToday());

            await _planRepository.AddAsync(copy, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Duplicated treatment plan {SourceId} into {CopyId} for patient {PatientId}",
                source.Id, copy.Id, targetPatientId);

            return Result<TreatmentPlanDto>.Success(copy.ToDto(patient.GetFullName()));
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
            _logger.LogError(ex, "Error duplicating treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la duplication du devis.");
        }
    }
}
