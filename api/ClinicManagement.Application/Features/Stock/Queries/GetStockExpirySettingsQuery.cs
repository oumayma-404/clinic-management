using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Queries;

/// <summary>Reads the clinic's approaching-expiry window so the settings control can show what is stored.</summary>
public class GetStockExpirySettingsQuery : IRequest<Result<StockExpirySettingsDto>>
{
}

public class GetStockExpirySettingsQueryHandler
    : IRequestHandler<GetStockExpirySettingsQuery, Result<StockExpirySettingsDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetStockExpirySettingsQueryHandler(
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<StockExpirySettingsDto>> Handle(
        GetStockExpirySettingsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<StockExpirySettingsDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");

            var clinic = await _clinicRepository.GetByIdAsync(clinicResult.Value, cancellationToken);
            if (clinic == null)
                return Result<StockExpirySettingsDto>.Failure("Cabinet introuvable.");

            return Result<StockExpirySettingsDto>.Success(
                new StockExpirySettingsDto { LeadDays = clinic.StockExpiryLeadDays });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockExpirySettingsDto>.Failure(
                $"Erreur lors du chargement du délai d'alerte de péremption : {ex.Message}");
        }
    }
}
