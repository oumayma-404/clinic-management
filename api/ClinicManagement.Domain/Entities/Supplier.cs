using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A fournisseur — any outside contact the cabinet orders from or sends work to, and can call.
/// <para>
/// <b>Deliberately broader than a stockroom supplier.</b> The prothésiste who makes the crowns, the laboratory
/// that reads a biopsy, the dépôt that delivers the composite and the technician who services the fauteuil are
/// one kind of record: a name, a category and a number somebody needs to reach. So <c>StockItem.SupplierId</c>
/// and <c>LabWorkOrder.SupplierId</c> both point here, rather than each carrying its own free-text name — which
/// is what they did, and is why neither « Stock faible » nor a bon de prothèse en retard could answer
/// « qui est-ce que j'appelle ? ».
/// </para>
/// <para>
/// <b>Why this is an aggregate and not the strings it replaced.</b> <c>StockItem.Supplier</c> and
/// <c>LabWorkOrder.Prosthetist</c> were free text: a name on a row, spelled three ways across four records, with
/// no number behind it. « Stock faible » therefore told a dentist <i>what</i> had run out and had no answer at all
/// to « commander chez qui ? », which is the only question that alert leads to.
/// </para>
/// <para>
/// ⚠️ <b><see cref="PhoneNumber"/> is a plain string, not the <c>ValueObjects.PhoneNumber</c>.</b> A supplier
/// number that is not a deliverable Tunisian one is <b>stored</b> (EC-1) — a dépôt with a French or Italian number
/// is a real supplier, and refusing the save to enforce a format would lose the only way to reach them. What a
/// non-Tunisian number loses is the WhatsApp action, which the read side derives and the screen explains.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Address"/> is one free-text line, deliberately not the <c>Address</c> value object.</b> That VO
/// requires street, city, state and zip; a supplier is recorded as « Zone industrielle, Ben Arous » or as nothing
/// at all, so the VO could not represent the ordinary case and would refuse the common one.
/// </para>
/// </summary>
public class Supplier : AggregateRoot<Guid>
{
    public const int MaxNameLength = 200;

    public Guid ClinicId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Open text, canonicalised through <see cref="SupplierCategories.Normalize"/> on every write.</summary>
    public string? Category { get; private set; }

    public string? PhoneNumber { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// Whether this supplier is offered in the pickers. ⚠️ <b>Deactivating hides it from a picker and erases
    /// nothing</b> (AC-4, EC-4): an article already linked to it keeps rendering its name and keeps its WhatsApp
    /// action, because the delivery it came from really did happen.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Supplier() { } // For EF Core

    public Supplier(
        Guid id,
        Guid clinicId,
        string name,
        string? category = null,
        string? phoneNumber = null,
        string? address = null,
        string? notes = null)
    {
        Id = id;
        ClinicId = clinicId;
        Name = RequireName(name);
        Category = SupplierCategories.Normalize(category);
        PhoneNumber = Blank(phoneNumber);
        Address = Blank(address);
        Notes = Blank(notes);
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Everything the edit form owns. Only the nom is required (AC-1).</summary>
    public void Update(string name, string? category, string? phoneNumber, string? address, string? notes)
    {
        Name = RequireName(name);
        Category = SupplierCategories.Normalize(category);
        PhoneNumber = Blank(phoneNumber);
        Address = Blank(address);
        Notes = Blank(notes);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Returns false when the flag already held that value, so a no-op writes no <c>UpdatedAt</c>.</summary>
    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Le nom du fournisseur est requis.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Le nom du fournisseur ne peut pas dépasser {MaxNameLength} caractères.", nameof(name));
        }

        return trimmed;
    }

    // Blank and absent are the same fact about an optional field, and storing "" would make « aucune adresse »
    // and « une adresse vide » two states the read side would have to tell apart forever.
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
