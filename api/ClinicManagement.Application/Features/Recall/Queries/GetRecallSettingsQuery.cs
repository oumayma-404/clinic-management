using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall.Queries;

/// <summary>Returns the clinic's recall configuration (interval in months). Clinic-scoped.</summary>
public class GetRecallSettingsQuery : IRequest<Result<RecallSettingsDto>>
{
}

public class GetRecallSettingsQueryHandler : IRequestHandler<GetRecallSettingsQuery, Result<RecallSettingsDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetRecallSettingsQueryHandler(
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<RecallSettingsDto>> Handle(GetRecallSettingsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<RecallSettingsDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");

            var clinic = await _clinicRepository.GetByIdAsync(clinicResult.Value, cancellationToken);
            if (clinic == null)
                return Result<RecallSettingsDto>.Failure("Cabinet introuvable.");

            return Result<RecallSettingsDto>.Success(new RecallSettingsDto { IntervalMonths = clinic.RecallIntervalMonths });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<RecallSettingsDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
