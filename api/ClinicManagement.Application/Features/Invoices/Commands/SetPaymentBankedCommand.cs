using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Invoices.Commands;

/// <summary>
/// Mark a cheque received against a note d'honoraires as taken to the bank, or take that mark back (Group B).
///
/// <para>
/// <b>It is a tracking state, not a money movement.</b> No total moves: la caisse counts a cheque on the day it
/// was received and not on the day it cleared, so nothing here touches <c>AmountCollected</c>, the invoice's
/// status, the patient's solde or the dashboard. What it changes is the one question « chèques à encaisser »
/// could not previously be asked — <i>which of the cheques in my drawer have I actually banked?</i> — for which
/// the only prior answer was to void the payment, i.e. to assert money had never been received.
/// </para>
/// <para>
/// <b>Reversible on purpose.</b> A cheque returned unpaid by the bank is the ordinary case, not an edge one, and
/// both directions land in the audit ledger because the aggregate is touched either way.
/// </para>
/// </summary>
public class SetPaymentBankedCommand : IRequest<Result<InvoiceDto>>
{
    public Guid InvoiceId { get; set; }
    public Guid PaymentId { get; set; }

    /// <summary>True to record it as banked, false to clear the mark.</summary>
    public bool Banked { get; set; }
}

public class SetPaymentBankedCommandHandler : IRequestHandler<SetPaymentBankedCommand, Result<InvoiceDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetPaymentBankedCommandHandler> _logger;

    public SetPaymentBankedCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<SetPaymentBankedCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceDto>> Handle(
        SetPaymentBankedCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Tenant isolation: an invoice from another clinic reads as "not found".
            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicResult.Value)
            {
                return Result<InvoiceDto>.Failure("Facture introuvable.");
            }

            var actorUserId = _clinicContext.GetUserId();
            var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);

            invoice.SetPaymentBanked(request.PaymentId, request.Banked, actorUserId, actorName);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Cheque payment {PaymentId} on invoice {InvoiceId} marked banked={Banked}",
                request.PaymentId, invoice.Id, request.Banked);

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
            return Result<InvoiceDto>.Failure("Erreur lors de la mise à jour de l'encaissement du chèque.");
        }
    }

    /// <summary>Best-effort name snapshot for the trail — a missing user must never block the mark.</summary>
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
