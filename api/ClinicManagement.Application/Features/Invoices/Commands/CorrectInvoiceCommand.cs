using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Begin correcting an issued note d'honoraires: raise a <b>draft copy</b> of it, pointed at the note it will
/// replace. Nothing about the original changes here.
///
/// <para><b>Why a draft first, and not the whole correction in one go.</b> The replacement has to be editable —
/// that is the entire point, since the mistakes this reaches (wrong patient, wrong line, a note with no fiche
/// behind it) are exactly the ones the fiche cannot express. But voiding the original's payments up front would
/// take real money out of la caisse for as long as the dentist spends editing, and out of it permanently if they
/// walk away. So the money stays where it is: the original keeps its payments and its status until
/// <c>IssueInvoiceCommand</c> puts the replacement into the world, and that single transaction is where the
/// predecessor is voided, cancelled, and its payments carried across at their original dates.</para>
///
/// <para><b>Correcting is not an avoir.</b> An avoir records money going <i>back to the patient</i>. A mis-keyed
/// amount gave nothing back, so an avoir there states a refund that never happened — which is why every refusal
/// in this area used to send the dentist somewhere that told the wrong story. See <c>Invoice.CanBeCorrected</c>.
/// Where money really is handed back, <c>CreateCreditNoteCommand</c> is still the right document.</para>
/// </summary>
public class CorrectInvoiceCommand : IRequest<Result<InvoiceDto>>
{
    /// <summary>The issued note to correct.</summary>
    public Guid Id { get; set; }

    /// <summary>Why it was wrong. Ends up on the predecessor's cancellation and on every voided payment.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class CorrectInvoiceCommandHandler : IRequestHandler<CorrectInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CorrectInvoiceCommandHandler> _logger;

    public CorrectInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CorrectInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(CorrectInvoiceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<InvoiceDto>.Failure("Le motif de la correction est requis.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var original = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (original == null || original.ClinicId != clinicId)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            if (original.Status == Domain.Enums.InvoiceStatus.Draft)
            {
                // A draft is already editable in place — sending it round this loop would spend a number for
                // nothing and leave a cancelled shell behind.
                return Result<InvoiceDto>.Failure(
                    "Ce brouillon n'a pas besoin d'être corrigé : modifiez-le directement.");
            }

            if (!original.CanBeCorrected)
            {
                return Result<InvoiceDto>.Failure(
                    original.SupersededByInvoiceId is not null
                        ? "Cette note a déjà été corrigée : corrigez la note qui l'a remplacée."
                        : "Cette note est annulée : elle ne peut plus être corrigée.");
            }

            var replacement = new Invoice(
                Guid.NewGuid(),
                original.ClinicId,
                original.PatientId,
                original.DentalRecordId,
                original.AppointmentId,
                original.TreatmentPlanId);

            replacement.SetDoctor(original.DoctorId);
            replacement.SetLines(original.Lines
                .OrderBy(l => l.Designation)
                .Select(l => (l.Designation, l.Quantity, l.UnitPriceHt, l.DentalRecordId, l.DentalActCodeId, l.CodeActe)));
            replacement.MarkSupersedes(original.Id, request.Reason);

            await _invoiceRepository.AddAsync(replacement, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Opened correction draft {DraftId} for invoice {InvoiceId} ({Number})",
                replacement.Id, original.Id, original.Number);

            var patient = await _patientRepository.GetByIdAsync(replacement.PatientId, cancellationToken);
            return Result<InvoiceDto>.Success(replacement.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error opening a correction for invoice {InvoiceId}", request.Id);
            return Result<InvoiceDto>.Failure("Erreur lors de l'ouverture de la correction.");
        }
    }
}
