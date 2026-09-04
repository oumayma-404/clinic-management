using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.ProcedureTypes.Commands;

/// <summary>
/// Backfill the current clinic's procedure menu with the starter set of common Tunisian dental procedures
/// (idempotent — skips any whose name already exists), and top up the intervals on the starter protocols a
/// clinic already has but has not edited.
/// </summary>
public class InitializeDefaultProcedureTypesCommand : IRequest<Result<CatalogueTopUp>>
{
}

/// <summary>
/// What the top-up actually did: acts added, and untouched starter protocols that gained their intervals.
/// <para>
/// Two numbers rather than one because they answer different questions and a clinic can legitimately see
/// « 0 added, 34 protocols updated ». Reporting only the first is what let a run that changed 34 protocols
/// say « Aucun nouvel acte à ajouter ».
/// </para>
/// </summary>
public record CatalogueTopUp(int Added, int ProtocolsUpdated);

public class InitializeDefaultProcedureTypesCommandHandler : IRequestHandler<InitializeDefaultProcedureTypesCommand, Result<CatalogueTopUp>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InitializeDefaultProcedureTypesCommandHandler> _logger;

    public InitializeDefaultProcedureTypesCommandHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<InitializeDefaultProcedureTypesCommandHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<CatalogueTopUp>> Handle(InitializeDefaultProcedureTypesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<CatalogueTopUp>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var existing = (await _procedureTypeRepository.GetFilteredAsync(
                clinicResult.Value, includeInactive: true, cancellationToken: cancellationToken)).Items;
            var existingByName = existing
                .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Materialised once: the add loop below consumes only the missing rows, and the interval top-up
            // needs the same starter definitions to compare the rest against.
            var starter = ProcedureTypeCatalogSeed.CreateFor(clinicResult.Value).ToList();

            var added = 0;
            foreach (var procedureType in starter)
            {
                if (existingByName.ContainsKey(procedureType.Name.Trim()))
                {
                    continue;
                }
                await _procedureTypeRepository.AddAsync(procedureType, cancellationToken);
                added++;
            }

            var protocolsUpdated = await TopUpIntervalsAsync(starter, existingByName, cancellationToken);

            if (added > 0 || protocolsUpdated > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Catalogue top-up for clinic {ClinicId}: {Added} acts added, {Updated} starter protocols gained intervals",
                clinicResult.Value, added, protocolsUpdated);
            return Result<CatalogueTopUp>.Success(new CatalogueTopUp(added, protocolsUpdated));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error seeding default procedure types");
            return Result<CatalogueTopUp>.Failure("Erreur lors du chargement des actes courants.");
        }
    }

    /// <summary>
    /// Give the intervals to the starter protocols a clinic already had. The seed is name-keyed and
    /// insert-only, so a rhythm added to a starter protocol reaches a brand-new clinic and no existing one —
    /// measured on this database as 50 acts carrying a protocol and 0 carrying an interval, which leaves the
    /// worklist unable to tell « pas encore due » from « oubliée » on every act it already sells.
    /// <para>
    /// ⚠️ Only a protocol that is still <b>exactly</b> the starter one is touched: same steps, same order, same
    /// chair times, and no interval of its own anywhere. Anything else is the clinic's own protocol — a step
    /// renamed, one dropped, a rhythm already typed — and overwriting it would silently discard a clinical
    /// decision to make a default fit. That is why this adds intervals and never removes or reorders anything.
    /// </para>
    /// </summary>
    private async Task<int> TopUpIntervalsAsync(
        IReadOnlyList<Domain.Entities.ProcedureType> starter,
        IReadOnlyDictionary<string, Domain.Entities.ProcedureType> existingByName,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        foreach (var definition in starter)
        {
            if (!definition.DefaultSteps.Any(s => s.MinDaysAfterPrevious.HasValue))
            {
                continue;
            }
            // Absent means it was just added above, already carrying its intervals.
            if (!existingByName.TryGetValue(definition.Name.Trim(), out var row))
            {
                continue;
            }
            if (!IsUntouchedStarterProtocol(row.DefaultSteps, definition.DefaultSteps))
            {
                continue;
            }

            row.SetDefaultSteps(definition.DefaultSteps);
            await _procedureTypeRepository.UpdateAsync(row, cancellationToken);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// True when <paramref name="stored"/> is the starter protocol with its intervals missing, and nothing else
    /// changed — the only shape safe to overwrite.
    /// </summary>
    private static bool IsUntouchedStarterProtocol(
        IReadOnlyList<ProcedureStepTemplate> stored,
        IReadOnlyList<ProcedureStepTemplate> definition)
    {
        if (stored.Count != definition.Count || stored.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < stored.Count; i++)
        {
            // A rhythm already on the row is the clinic's, whatever its value — leave the whole protocol alone.
            if (stored[i].MinDaysAfterPrevious.HasValue)
            {
                return false;
            }
            if (!string.Equals(stored[i].Label.Trim(), definition[i].Label.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (stored[i].DurationMinutes != definition[i].DurationMinutes)
            {
                return false;
            }
        }

        return true;
    }
}
