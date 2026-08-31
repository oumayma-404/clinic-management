using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.DentalActs.Queries;

/// <summary>
/// The indicative reimbursement estimate for <b>every act of one bulletin</b>, in one round trip (AC-P6.15).
///
/// <para><b>Why a batch sibling of <see cref="GetReimbursementEstimateQuery"/> rather than reusing it.</b> The BS1
/// editor shows an estimate per act row, live as the cotation is typed, plus a total. That is what made the
/// frontend reimplement the calculator — its own <c>CHILD_RATE</c>/<c>ADULT_RATE</c> and its own multiplication,
/// a second authority over a reimbursement figure that will drift the first time CNAM moves a rate. Calling the
/// single-act endpoint per row per keystroke is not a usable substitute; one call for the whole table is.</para>
///
/// <para>The VLC set is read <b>once</b> for the batch instead of once per act — the single-act query's
/// <c>GetLetterValueByCleAsync</c> per item would be one query per row.</para>
///
/// <para>Still editor-only: never persisted, never printed on the BS1 PDF (AC-P6.16, and
/// <c>cnam-nomenclature-lookup</c> AC-5).</para>
/// </summary>
public class GetReimbursementEstimatesQuery : IRequest<Result<List<ReimbursementEstimateDto>>>
{
    /// <summary>One entry per act row, in the caller's order — the response is aligned to it by index.</summary>
    public List<ReimbursementEstimateItem> Items { get; set; } = new();

    public DateTime? PatientDateOfBirth { get; set; }

    /// <summary>Fallback care date for items that carry none (the bulletin's own date).</summary>
    public DateTime? CareDate { get; set; }
}

/// <summary>
/// One act to estimate. <see cref="CareDate"/> is per-item because the rate turns on the patient's age
/// <b>at the care date</b>, and a bulletin's acts can legitimately straddle a birthday.
/// </summary>
public class ReimbursementEstimateItem
{
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public DateTime? CareDate { get; set; }
}

public class GetReimbursementEstimatesQueryHandler
    : IRequestHandler<GetReimbursementEstimatesQuery, Result<List<ReimbursementEstimateDto>>>
{
    /// <summary>
    /// A bulletin has a handful of acts. The cap is here so a crafted request cannot turn one editor keystroke
    /// into an unbounded computation, not because any real bulletin approaches it.
    /// </summary>
    private const int MaxItems = 100;

    private readonly ICnamCatalogRepository _repository;
    private readonly ILogger<GetReimbursementEstimatesQueryHandler> _logger;

    public GetReimbursementEstimatesQueryHandler(
        ICnamCatalogRepository repository,
        ILogger<GetReimbursementEstimatesQueryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<List<ReimbursementEstimateDto>>> Handle(
        GetReimbursementEstimatesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Items.Count > MaxItems)
            {
                return Result<List<ReimbursementEstimateDto>>.Failure(
                    $"Trop d'actes à estimer (maximum {MaxItems}).");
            }

            if (request.Items.Count == 0)
            {
                return Result<List<ReimbursementEstimateDto>>.Success(new List<ReimbursementEstimateDto>());
            }

            // The clinic's own day, not the UTC one, when neither the item nor the bulletin supplies a care date
            // (AC-P6.4): the rate turns on the patient's age that day, so on a birthday the UTC date is a year out
            // for the first hour of every Tunisian day.
            var fallbackCareDate = request.CareDate ?? ClinicClock.ClinicToday();

            var vlcByCle = (await _repository.GetAllLetterValuesAsync(cancellationToken))
                .GroupBy(v => v.LettreCle.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First().Value);

            var estimates = request.Items
                .Select(item =>
                {
                    var careDate = item.CareDate ?? fallbackCareDate;
                    decimal? vlc = vlcByCle.TryGetValue((item.LettreCle ?? string.Empty).ToUpperInvariant(), out var v)
                        ? v
                        : null;

                    return new ReimbursementEstimateDto
                    {
                        Estimate = CnamReimbursementCalculator.Estimate(
                            item.Coefficient, vlc, request.PatientDateOfBirth, careDate),
                        RateApplied = CnamReimbursementCalculator.RateForPatient(
                            request.PatientDateOfBirth, careDate),
                        UnavailableReason = CnamReimbursementCalculator.UnavailableReason(item.Coefficient, vlc),
                    };
                })
                .ToList();

            return Result<List<ReimbursementEstimateDto>>.Success(estimates);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error computing CNAM reimbursement estimates");
            return Result<List<ReimbursementEstimateDto>>.Failure(
                "Erreur lors du calcul de l'estimation du remboursement.");
        }
    }
}
