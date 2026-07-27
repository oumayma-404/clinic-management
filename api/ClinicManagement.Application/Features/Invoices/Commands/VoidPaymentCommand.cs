using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Void a recorded payment — "this was never received". The row is kept and marked with a motif, the actor and
/// the moment; <c>AmountCollected</c> is recomputed from the live payments and the status walks back.
///
/// <para>
/// A void is a <b>correction</b>, not a refund. Money actually returned to the patient is an avoir
/// (<see cref="Domain.Entities.CreditNote"/>), a numbered fiscal document. Reversing a typo must not invent a
/// refund that never happened, and must not be able to take collected cash below what avoirs already credited.
/// </para>
/// <para>
/// Not reversible: to correct a correction, record the right payment again.
/// </para>
/// </summary>
public class VoidPaymentCommand : IRequest<Result<InvoiceDto>>
{
    public Guid InvoiceId { get; set; }
    public Guid PaymentId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class VoidPaymentCommandHandler : IRequestHandler<VoidPaymentCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VoidPaymentCommandHandler> _logger;

    public VoidPaymentCommandHandler(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<VoidPaymentCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(VoidPaymentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<InvoiceDto>.Failure("Le motif d'annulation du paiement est requis.");
            }

            // Tenant isolation: an invoice from another clinic reads as "not found".
            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicResult.Value)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            // The aggregate has no repository access, so the already-credited total is resolved here and
            // passed in. Without it a void could take collected cash below money an avoir already returned,
            // and the caisse would lose the same dinar twice.
            var creditedTotal = await _creditNoteRepository.GetTotalForInvoiceAsync(invoice.Id, cancellationToken);

            var actorUserId = _clinicContext.GetUserId();
            var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);

            invoice.VoidPayment(request.PaymentId, request.Reason, creditedTotal, actorUserId, actorName);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Voided payment {PaymentId} on invoice {InvoiceId}; collected is now {Collected}",
                request.PaymentId, invoice.Id, invoice.AmountCollected);

            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);
            return Result<InvoiceDto>.Success(invoice.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<InvoiceDto>.Failure(ex.Message);
        }
        catch (Exception)
        {
            return Result<InvoiceDto>.Failure("Erreur lors de l'annulation du paiement.");
        }
    }

    /// <summary>Best-effort name snapshot for the trail — a missing user must never block the correction.</summary>
    private async Task<string?> ResolveActorNameAsync(string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return null;
        }

        var user = await _userRepository.GetByAuth0SubAsync(actorUserId, cancellationToken);
        return user?.FullName ?? user?.Email;
    }
}
