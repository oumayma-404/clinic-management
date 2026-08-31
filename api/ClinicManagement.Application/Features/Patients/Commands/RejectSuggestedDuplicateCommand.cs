using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// « Non, ce n'est pas le même patient. » — <c>calendar-import-duplicate-merge</c> AC-15.
///
/// <para>⚠️ It clears the suggestion and <b>keeps the review stamp</b>. The two are independent: the fiche is still
/// a name read off a calendar with no birth date and no address, so it stays on « Patients à compléter » with its
/// own action. Clearing both would make « Non » quietly mean « cette fiche est complète ».</para>
///
/// <para>⚠️ The refusal is <b>durable</b>, which is why it is a write and not a browser-side dismissal: the import
/// runs every fifteen minutes, and a suggestion the practice has already declined must not come back. Nothing
/// re-stamps it — the import only ever stamps a record it is creating.</para>
/// </summary>
public class RejectSuggestedDuplicateCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class RejectSuggestedDuplicateCommandHandler : IRequestHandler<RejectSuggestedDuplicateCommand, Result>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RejectSuggestedDuplicateCommandHandler> _logger;

    public RejectSuggestedDuplicateCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<RejectSuggestedDuplicateCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(RejectSuggestedDuplicateCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                throw new NotFoundException("Patient introuvable.");
            }

            // Idempotent: a second « Non » (two tabs, a double tap) has nothing to do and is not an error.
            if (patient.CalendarImportSuggestedDuplicateId.HasValue)
            {
                patient.RejectCalendarImportSuggestion();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException && ex is not NotFoundException)
        {
            _logger.LogError(ex, "Unhandled failure rejecting a calendar-import duplicate suggestion");
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
