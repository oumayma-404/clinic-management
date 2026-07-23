using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// The caisse (daily cash) summary for the clinic over [From, To): encaissements (collected invoice
/// payments) minus dépenses (recorded expenses), and the net. Both endpoints default to the current UTC
/// day when omitted. Clinic-scoped; all figures rounded to the millime.
/// </summary>
public class GetCaisseSummaryQuery : IRequest<Result<CaisseSummaryDto>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class GetCaisseSummaryQueryHandler : IRequestHandler<GetCaisseSummaryQuery, Result<CaisseSummaryDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCaisseSummaryQueryHandler> _logger;

    public GetCaisseSummaryQueryHandler(
        IInvoiceRepository invoiceRepository,
        IExpenseRepository expenseRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCaisseSummaryQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _expenseRepository = expenseRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<CaisseSummaryDto>> Handle(GetCaisseSummaryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<CaisseSummaryDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            // Default to the current UTC day when no range is supplied.
            var from = request.From ?? DateTime.UtcNow.Date;
            var to = request.To ?? from.AddDays(1);
            if (to <= from)
                return Result<CaisseSummaryDto>.Failure("La date de fin doit être postérieure à la date de début.");

            var cashIn = await _invoiceRepository.GetCollectedBetweenAsync(clinicId, from, to, cancellationToken);
            var cashOut = await _expenseRepository.GetTotalBetweenAsync(clinicId, from, to, cancellationToken);

            var dto = new CaisseSummaryDto
            {
                FromDate = from,
                ToDate = to,
                CashIn = InvoiceCalculator.RoundMoney(cashIn),
                CashOut = InvoiceCalculator.RoundMoney(cashOut),
                Net = InvoiceCalculator.RoundMoney(cashIn - cashOut)
            };

            return Result<CaisseSummaryDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building the caisse summary");
            return Result<CaisseSummaryDto>.Failure("Erreur lors du calcul de la caisse.");
        }
    }
}
