using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the per-clinic CNAM <b>valeurs de la lettre clé</b> (VLC) — the dinar value per lettre clé.
/// Per-clinic reference data (has <c>ClinicId</c>, clinic-filtered). Mutations only stage
/// changes — the caller commits via <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface ICnamCatalogRepository
{
    // ── Valeurs de la lettre clé (VLC) ──────────────────────────────────────────────────────────
    Task<IEnumerable<CnamLetterValue>> GetAllLetterValuesAsync(CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByCleAsync(string lettreCle, CancellationToken cancellationToken = default);
    Task UpdateLetterValueAsync(CnamLetterValue value, CancellationToken cancellationToken = default);
}
