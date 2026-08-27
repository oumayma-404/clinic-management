using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.DTOs;

/// <summary>One fournisseur, as read by the client.</summary>
public class SupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }

    /// <summary>What was typed, verbatim — a foreign number is stored and shown as entered (EC-1).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// The same number as deliverable Tunisian E.164, or <c>null</c> when it is not one.
    /// <para>
    /// ⚠️ <b>This is what decides whether a WhatsApp action exists</b> (AC-3), and it is resolved <b>server-side</b>
    /// through <see cref="PhoneNumber.ToE164"/> rather than re-derived in the browser: the client already has a
    /// mirror of that rule in <c>lib/phone.ts</c>, and a third copy deciding whether a link appears is how a
    /// number becomes callable on one screen and not on another.
    /// </para>
    /// </summary>
    public string? PhoneE164 { get; set; }

    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Stock articles pointing here. Part of AC-4's refusal, and the list's « N articles liés ».</summary>
    public int LinkedItemCount { get; set; }

    /// <summary>Bons de prothèse pointing here — counted apart, so a refusal can name where to look.</summary>
    public int LinkedLabOrderCount { get; set; }

    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>One page of fournisseurs, plus the catégorie options the pickers and the filter read.</summary>
public class SupplierPageDto
{
    public List<SupplierDto> Items { get; set; } = new();

    /// <summary>
    /// The canonical suggestions unioned with the clinic's own, clinic-wide and deliberately ignoring the current
    /// filters: they are the options for the filter itself, so narrowing them to the active view would make the
    /// control report itself back.
    /// </summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Only the catégories this cabinet has actually filed a fournisseur under — the set the <b>filter</b> reads.
    /// <para>
    /// ⚠️ <b>Deliberately not <see cref="Categories"/>.</b> That one is the <i>form's</i> suggestion list, and it
    /// carries the twelve canonical labels whether or not the cabinet has ever used one. Rendered as filter chips
    /// it produced twelve controls over three rows on a practice with four fournisseurs, nine of which could only
    /// ever answer « aucun résultat » — and ten rows of chrome above the first row at 390&#160;px. A filter offers
    /// what narrowing is possible; a form offers what filing is sensible. They are different questions and this is
    /// the second answer.
    /// </para>
    /// </summary>
    public List<string> CategoriesInUse { get; set; } = new();

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public static class SupplierMappingExtensions
{
    /// <summary>
    /// Maps a supplier, with the usage counts resolved by the caller's batched read — passed in rather than
    /// counted here, because a DTO layer issuing a query per row is the companion-read defect `list-pagination`
    /// documents.
    /// </summary>
    public static SupplierDto ToDto(this Supplier supplier, SupplierUsage usage = default) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        Category = supplier.Category,
        PhoneNumber = supplier.PhoneNumber,
        PhoneE164 = PhoneNumber.ToE164(supplier.PhoneNumber),
        Address = supplier.Address,
        Notes = supplier.Notes,
        IsActive = supplier.IsActive,
        LinkedItemCount = usage.StockItems,
        LinkedLabOrderCount = usage.LabOrders,
        Version = supplier.Version,
        CreatedAt = supplier.CreatedAt,
        UpdatedAt = supplier.UpdatedAt,
    };
}
