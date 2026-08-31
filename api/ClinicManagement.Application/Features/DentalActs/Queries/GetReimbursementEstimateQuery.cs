using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.DentalActs.Queries;

// Authoritative indicative reimbursement estimate for a single act (FR-5.5): coefficient × VLC × age-rate.
// Any authenticated user (editor aid). The VLC value is resolved from the global admin-managed set; a
// lettre clé with no VLC yields a null estimate (omitted "—"). Never persisted / never printed (R-9).
public class GetReimbursementEstimateQuery : IRequest<Result<ReimbursementEstimateDto>>
{
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public DateTime? PatientDateOfBirth { get; set; }
    public DateTime? CareDate { get; set; }
}

public class GetReimbursementEstimateQueryHandler
    : IRequestHandler<GetReimbursementEstimateQuery, Result<ReimbursementEstimateDto>>
{
    private readonly ICnamCatalogRepository _repository;
    private readonly ILogger<GetReimbursementEstimateQueryHandler> _logger;

    public GetReimbursementEstimateQueryHandler(
        ICnamCatalogRepository repository,
        ILogger<GetReimbursementEstimateQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<ReimbursementEstimateDto>> Handle(
        GetReimbursementEstimateQuery request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.LettreCle))
            {
                return Result<ReimbursementEstimateDto>.Failure("La lettre clé est obligatoire.");
            }

            // The clinic's day, not the UTC one (AC-P6.4) — the rate turns on the patient's age at the care date.
            var careDate = request.CareDate ?? ClinicClock.ClinicToday();
            var vlcRow = await _repository.GetLetterValueByCleAsync(request.LettreCle, cancellationToken);
            decimal? vlc = vlcRow?.Value;

            var estimate = CnamReimbursementCalculator.Estimate(
                request.Coefficient, vlc, request.PatientDateOfBirth, careDate);

            return Result<ReimbursementEstimateDto>.Success(new ReimbursementEstimateDto
            {
                Estimate = estimate,
                RateApplied = CnamReimbursementCalculator.RateForPatient(request.PatientDateOfBirth, careDate),
                UnavailableReason = CnamReimbursementCalculator.UnavailableReason(request.Coefficient, vlc),
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error computing CNAM reimbursement estimate");
            return Result<ReimbursementEstimateDto>.Failure(
                "Erreur lors du calcul de l'estimation du remboursement.");
        }
    }
}
