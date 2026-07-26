using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the per-clinic CNAM catalog: nomenclature entries + valeurs de la lettre clé (VLC).
/// Both are per-clinic reference data (have <c>ClinicId</c>, clinic-filtered). Mutations only stage
/// changes — the caller commits via <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface ICnamCatalogRepository
{
    // ── Nomenclature entries ────────────────────────────────────────────────────────────────────
    Task<CnamNomenclatureEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CnamNomenclatureEntry>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<bool> CodeActeExistsAsync(string codeActe, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<CnamNomenclatureEntry> AddAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default);

    // ── Valeurs de la lettre clé (VLC) ──────────────────────────────────────────────────────────
    Task<IEnumerable<CnamLetterValue>> GetAllLetterValuesAsync(CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByCleAsync(string lettreCle, CancellationToken cancellationToken = default);
    Task UpdateLetterValueAsync(CnamLetterValue value, CancellationToken cancellationToken = default);
}
