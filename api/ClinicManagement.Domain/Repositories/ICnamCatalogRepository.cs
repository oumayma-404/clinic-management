using ClinicManagement.Domain.Entities;

using ClinicManagement.Domain.Common;
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
    /// <summary>
    /// The nomenclature list. <paramref name="category"/> and <paramref name="searchTerm"/> (code, désignation,
    /// lettre clé) are both matched in SQL — they used to be applied in the handler over the mapped DTOs, which
    /// with a page would have filtered rows out of an already-cut window.
    /// <paramref name="paging"/> of null returns every match, which the BS1 editor's act picker needs.
    /// </summary>
    Task<PagedResult<CnamNomenclatureEntry>> GetAllAsync(
        bool includeInactive = false,
        string? category = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);
    Task<bool> CodeActeExistsAsync(string codeActe, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<CnamNomenclatureEntry> AddAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default);

    // ── Valeurs de la lettre clé (VLC) ──────────────────────────────────────────────────────────
    Task<IEnumerable<CnamLetterValue>> GetAllLetterValuesAsync(CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CnamLetterValue?> GetLetterValueByCleAsync(string lettreCle, CancellationToken cancellationToken = default);
    Task UpdateLetterValueAsync(CnamLetterValue value, CancellationToken cancellationToken = default);
}
