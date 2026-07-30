using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.CnamNomenclature;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Default <see cref="ICnamBillingCalculator"/>: resolves each line's catalog act (coefficient + lettre clé)
/// and the lettre-clé value (VLC) from the global CNAM catalog, applies the per-act
/// <see cref="CnamReimbursementCalculator"/>, and aggregates a capped reimbursable/out-of-pocket split.
/// The (small) catalog is loaded once per instance (the service is scoped → once per request), so computing
/// the split for several documents in one request does not re-hit the DB per document.
/// </summary>
public class CnamBillingCalculator : ICnamBillingCalculator
{
    private readonly IDentalActCodeRepository _actRepository;
    private readonly ICnamCatalogRepository _catalogRepository;

    private Dictionary<Guid, DentalActCode>? _actsById;
    private Dictionary<string, decimal>? _vlcByLettreCle;

    public CnamBillingCalculator(IDentalActCodeRepository actRepository, ICnamCatalogRepository catalogRepository)
    {
        _actRepository = actRepository;
        _catalogRepository = catalogRepository;
    }

    public async Task<CnamSplit> ComputeAsync(
        IReadOnlyCollection<CnamBillingLine> lines,
        decimal documentTotal,
        DateTime? patientDateOfBirth,
        DateTime careDate,
        CancellationToken cancellationToken = default)
    {
        documentTotal = InvoiceCalculator.RoundMoney(Math.Max(0m, documentTotal));
        if (lines.Count == 0 || documentTotal <= 0m)
        {
            return new CnamSplit(0m, documentTotal);
        }

        await EnsureCatalogLoadedAsync(cancellationToken);

        var reimbursable = 0m;
        foreach (var line in lines)
        {
            if (line.DentalActCodeId is null || !_actsById!.TryGetValue(line.DentalActCodeId.Value, out var act))
            {
                continue; // free-text line or unknown act → fully out-of-pocket
            }

            if (act.Coefficient is null)
            {
                continue; // no cotation defined → indicative estimate unavailable → out-of-pocket
            }

            decimal? vlc = _vlcByLettreCle!.TryGetValue(act.LettreCle, out var value) ? value : null;
            var estimate = CnamReimbursementCalculator.Estimate(act.Coefficient.Value, vlc, patientDateOfBirth, careDate);
            if (estimate is null)
            {
                continue; // lettre clé has no VLC → estimate omitted → out-of-pocket
            }

            // Never reimburse more than what was actually charged for the line.
            reimbursable += Math.Min(estimate.Value, Math.Max(0m, line.Amount));
        }

        // Cap the total reimbursable at the document total so the split always sums to the total.
        reimbursable = InvoiceCalculator.RoundMoney(Math.Clamp(reimbursable, 0m, documentTotal));
        var outOfPocket = InvoiceCalculator.RoundMoney(documentTotal - reimbursable);
        return new CnamSplit(reimbursable, outOfPocket);
    }

    private async Task EnsureCatalogLoadedAsync(CancellationToken cancellationToken)
    {
        if (_actsById is not null && _vlcByLettreCle is not null)
        {
            return;
        }

        // Unpaged: this builds the by-id lookup the whole calculation resolves against, so it needs every act.
        var acts = await _actRepository.GetAllAsync(
            includeInactive: true, cancellationToken: cancellationToken);
        _actsById = acts.Items.ToDictionary(a => a.Id);

        var letterValues = await _catalogRepository.GetAllLetterValuesAsync(cancellationToken);
        _vlcByLettreCle = letterValues
            .GroupBy(v => v.LettreCle)
            .ToDictionary(g => g.Key, g => g.First().Value);
    }
}
