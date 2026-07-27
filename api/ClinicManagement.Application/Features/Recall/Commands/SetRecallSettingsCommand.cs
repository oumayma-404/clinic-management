using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall.Commands;

/// <summary>Sets the clinic's recall interval in months (1–60). Admin-editable in the settings UI.</summary>
public class SetRecallSettingsCommand : IRequest<Result<RecallSettingsDto>>
{
    public int IntervalMonths { get; set; }
}

public class SetRecallSettingsCommandHandler : IRequestHandler<SetRecallSettingsCommand, Result<RecallSettingsDto>>
{
    private readonly IClinicRepository _clinicRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public SetRecallSettingsCommandHandler(
        IClinicRepository clinicRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _clinicRepository = clinicRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RecallSettingsDto>> Handle(SetRecallSettingsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<RecallSettingsDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");

            var clinic = await _clinicRepository.GetByIdAsync(clinicResult.Value, cancellationToken);
            if (clinic == null)
                return Result<RecallSettingsDto>.Failure("Cabinet introuvable.");

            clinic.SetRecallIntervalMonths(request.IntervalMonths);
            await _clinicRepository.UpdateAsync(clinic, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RecallSettingsDto>.Success(new RecallSettingsDto { IntervalMonths = clinic.RecallIntervalMonths });
        }
        catch (ArgumentException ex)
        {
            return Result<RecallSettingsDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<RecallSettingsDto>.Failure($"Erreur lors de l'enregistrement des paramètres de relance : {ex.Message}");
        }
    }
}
