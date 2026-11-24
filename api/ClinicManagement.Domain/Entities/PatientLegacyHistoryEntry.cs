using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One line of a patient's history as the cabinet's PREVIOUS software recorded it — a consultation, an ordonnance,
/// a radio, a diagnostic — brought over when the cabinet switched (<c>features/old-software-history</c>).
///
/// <para>⚠️ <b>Read-only by design, and never a fiche.</b> A fiche de soins creates money, chart marks and debts;
/// a visit from 2022 must create none of them. So this carries text only: no act, no tooth, no price, no status,
/// and no mutator — a line is written once by the import and read back as the old software said it.</para>
///
/// <para>⚠️ <see cref="Category"/> is the old software's own section word (« Consultation », « Ordonnance »…),
/// stored and shown as is. No enum: a second old software will have other sections, and a key/label pair is a
/// mirror nothing here needs.</para>
/// </summary>
public class PatientLegacyHistoryEntry : Entity<Guid>, IAuditable
{
    public const int MaxCategoryLength = 60;
    public const int MaxTitleLength = 300;
    public const int MaxDetailsLength = 4000;

    public Guid PatientId { get; private set; }

    /// <summary>The patient's clinic, denormalised so the row carries its own tenant filter (like every clinical child).</summary>
    public Guid ClinicId { get; private set; }

    /// <summary>The day the old software dated it. Absent when it gave none (a diagnostic block, for instance).</summary>
    public DateOnly? Date { get; private set; }

    public string Category { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Details { get; private set; }

    /// <summary>The line's place in the old record, so lines of one day read in the order the PDF gave them.</summary>
    public int Position { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private PatientLegacyHistoryEntry() { } // For EF Core

    public PatientLegacyHistoryEntry(
        Guid id, Guid patientId, Guid clinicId, DateOnly? date, string category, string title, string? details, int position)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("La catégorie est requise.", nameof(category));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Le titre est requis.", nameof(title));
        if (category.Trim().Length > MaxCategoryLength)
            throw new ArgumentException($"La catégorie dépasse {MaxCategoryLength} caractères.", nameof(category));
        if (title.Trim().Length > MaxTitleLength)
            throw new ArgumentException($"Le titre dépasse {MaxTitleLength} caractères.", nameof(title));
        if (details is not null && details.Trim().Length > MaxDetailsLength)
            throw new ArgumentException($"Le détail dépasse {MaxDetailsLength} caractères.", nameof(details));

        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
        Date = date;
        Category = category.Trim();
        Title = title.Trim();
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        Position = position;
        CreatedAt = DateTime.UtcNow;
    }
}
