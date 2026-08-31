using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Stock.Commands;

/// <summary>
/// Sets the clinic's approaching-expiry window in days (0–365, <b>0 = alerte désactivée</b>) — the first caller
/// <c>Clinic.SetStockExpiryLeadDays</c> has ever had (AC-20).
///
/// <para>The setter, the column and both readers (<c>StockExpiryJob</c>, <c>DashboardAlertsReader</c>) all shipped
/// together and correct, with nothing able to reach them: every clinic ran on the 30-day default for the life of
/// the product, and a practice stocking nothing perishable had a daily notification it could not switch off. This
/// is the repo's recurring shape — a setting that ships without a caller — and it is why the range guard being
/// wrong (1–365, refusing the one value meaning "off") went unnoticed for just as long.</para>
///
/// <para>Modelled on <c>SetRecallSettingsCommand</c>: same admin-only clinic-wide configuration, same shape.</para>
/// </summary>
public class SetStockExpirySettingsCommand : IRequest<Result<StockExpirySettingsDto>>
{
    public int LeadDays { get; set; }
}

public class SetStockExpirySettingsCommandHandler
    : IRequestHandler<SetStockExpirySettingsCommand, Result<StockExpirySettingsDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public SetStockExpirySettingsCommandHandler(
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StockExpirySettingsDto>> Handle(
        SetStockExpirySettingsCommand request,
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

            clinic.SetStockExpiryLeadDays(request.LeadDays);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StockExpirySettingsDto>.Success(
                new StockExpirySettingsDto { LeadDays = clinic.StockExpiryLeadDays });
        }
        catch (ArgumentException ex)
        {
            return Result<StockExpirySettingsDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StockExpirySettingsDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
