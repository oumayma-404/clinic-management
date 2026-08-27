using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly ApplicationDbContext _context;

    public SupplierRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<PagedResult<Supplier>> GetFilteredAsync(
        Guid clinicId,
        string? searchTerm = null,
        string? category = null,
        bool includeInactive = false,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Suppliers.Where(s => s.ClinicId == clinicId);

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmed = category.Trim();
            query = query.Where(s => s.Category == trimmed);
        }

        // Nom, catégorie, téléphone and adresse: what somebody actually has in mind when they open this list —
        // « dentalex », « prothèse », or the number on a delivery note they are holding.
        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(s =>
                EF.Functions.ILike(SqlSearch.Unaccent(s.Name)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(s.Category)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(s.PhoneNumber)!, pattern, SqlSearch.EscapeString) ||
                // ⚠️ And the same number with its grouping spaces removed. « 98 456 321 » is how a delivery note
                // spaces it and « 98456321 » is what is read off a phone screen — the second found nothing,
                // because the pattern spanned a stored space. The search label sells « par … téléphone », so the
                // realistic input has to work; `replace` translates to SQL, so this stays a database question.
                EF.Functions.ILike(
                    s.PhoneNumber!.Replace(" ", "").Replace("-", "").Replace(".", ""),
                    pattern,
                    SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(s.Address)!, pattern, SqlSearch.EscapeString));
        }

        // `.ThenBy(Id)` is not decoration: OFFSET over a non-unique sort can show one supplier on two pages and
        // skip another, which reads as a record having vanished.
        return await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Supplier>> GetByIdsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Supplier>();
        }

        // Deliberately no IsActive term: this resolves links that already exist, and a deactivated supplier must
        // keep rendering its name and its WhatsApp action wherever an article or a bon still names it (EC-4).
        var distinct = ids.Distinct().ToList();
        return await _context.Suppliers
            .Where(s => s.ClinicId == clinicId && distinct.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
    }

    public async Task<Supplier?> FindByNameAsync(
        Guid clinicId, string name, Guid? excludingId = null, CancellationToken cancellationToken = default)
    {
        // Exact-match on the folded form, not a LIKE: « Dental » must not report « Dentalex » as a duplicate.
        // ToLikePattern would wrap it in %…%, so the pattern is built here without the wildcards.
        var normalized = SearchTerm.Normalize(name);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var escaped = normalized
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        var query = _context.Suppliers.Where(s => s.ClinicId == clinicId
            && EF.Functions.ILike(SqlSearch.Unaccent(s.Name)!, escaped, SqlSearch.EscapeString));

        if (excludingId is { } id)
        {
            query = query.Where(s => s.Id != id);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(
        Guid clinicId, bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        // Sorted in SQL so the picker's order does not depend on which page happened to load.
        //
        // ⚠️ `activeOnly` exists because the two consumers ask DIFFERENT questions. The FORM's suggestion list
        // wants every label the practice has ever filed a contact under — a category does not stop being one
        // because a dépôt closed (ProcedureTypeRepository.GetCategoriesAsync' precedent). The FILTER's chips want
        // only what can still narrow the default list, which excludes deactivated rows: retiring the only
        // laboratory of a kind left a chip on the toolbar whose every click answered « Aucun fournisseur pour ces
        // filtres ». `suppliers-table.tsx` makes « a filter offers what narrowing is possible » its own rule.
        var scoped = _context.Suppliers
            .Where(s => s.ClinicId == clinicId && s.Category != null && s.Category != "");

        if (activeOnly)
        {
            scoped = scoped.Where(s => s.IsActive);
        }

        return await scoped
            .Select(s => s.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, SupplierUsage>> GetUsageAsync(
        Guid clinicId, IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken = default)
    {
        if (supplierIds.Count == 0)
        {
            return new Dictionary<Guid, SupplierUsage>();
        }

        var distinct = supplierIds.Distinct().ToList();

        var stock = await _context.StockItems
            .Where(i => i.ClinicId == clinicId && i.SupplierId != null && distinct.Contains(i.SupplierId!.Value))
            .GroupBy(i => i.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lab = await _context.LabWorkOrders
            .Where(o => o.ClinicId == clinicId && o.SupplierId != null && distinct.Contains(o.SupplierId!.Value))
            .GroupBy(o => o.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var usage = new Dictionary<Guid, SupplierUsage>();
        foreach (var row in stock)
        {
            usage[row.SupplierId] = new SupplierUsage(row.Count, 0);
        }

        foreach (var row in lab)
        {
            usage.TryGetValue(row.SupplierId, out var existing);
            usage[row.SupplierId] = existing with { LabOrders = row.Count };
        }

        return usage;
    }

    public async Task<SupplierUsage> GetUsageAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var stock = await _context.StockItems.CountAsync(i => i.SupplierId == supplierId, cancellationToken);
        var lab = await _context.LabWorkOrders.CountAsync(o => o.SupplierId == supplierId, cancellationToken);
        return new SupplierUsage(stock, lab);
    }

    public async Task<Supplier> AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        await _context.Suppliers.AddAsync(supplier, cancellationToken);
        return supplier;
    }

    public Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        // Only attach a DETACHED instance. A tracked one already carries its real original values, including the
        // xmin concurrency token; calling Update() on it re-marks every property modified, and on a never-loaded
        // detached one the token reads as 0, producing "WHERE xmin = 0" and a 409 for a conflict that never was.
        if (_context.Entry(supplier).State == EntityState.Detached)
        {
            _context.Suppliers.Update(supplier);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        _context.Suppliers.Remove(supplier);
        return Task.CompletedTask;
    }
}
