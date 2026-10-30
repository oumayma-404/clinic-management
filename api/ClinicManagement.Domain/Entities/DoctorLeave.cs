using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A practitioner's congé: whole clinic-local days, <see cref="StartDate"/> to <see cref="EndDate"/> inclusive, during
/// which they are out of the cabinet. Booking them inside it is warned like a closed day, never refused outright.
/// </summary>
public class DoctorLeave : AggregateRoot<Guid>
{
    /// <summary>Longest congé accepted — a mistyped year is the error this catches.</summary>
    public const int MaxDays = 366;

    public const int MaxNoteLength = 200;

    public Guid ClinicId { get; private set; }
    public Guid DoctorId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>Optional motif (« Vacances », « Formation »…).</summary>
    public string? Note { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    /// <summary>Touches the inclusive day window — the repository's SQL overlap test, for a congé already loaded.</summary>
    public bool Covers(DateOnly fromDay, DateOnly toDay) => StartDate <= toDay && fromDay <= EndDate;

    private DoctorLeave() { } // For EF Core

    public DoctorLeave(Guid id, Guid clinicId, Guid doctorId, DateOnly startDate, DateOnly endDate, string? note)
    {
        Id = id;
        ClinicId = clinicId;
        DoctorId = doctorId;
        Apply(startDate, endDate, note);
        CreatedAt = DateTime.UtcNow;
    }

    public void Change(DateOnly startDate, DateOnly endDate, string? note)
    {
        Apply(startDate, endDate, note);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The French refusal for a period this type would not accept, or null when it is valid.</summary>
    public static string? RefusalFor(DateOnly startDate, DateOnly endDate, string? note)
    {
        // A JSON body without the dates binds them to 01/01/0001 rather than failing.
        if (startDate == default || endDate == default)
            return "Les dates du congé sont requises.";
        if (endDate < startDate)
            return "La date de fin du congé doit être postérieure ou égale à sa date de début.";
        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
            return $"Un congé ne peut pas dépasser {MaxDays} jours — vérifiez l'année saisie.";
        if (NormalizeNote(note) is { Length: > MaxNoteLength })
            return $"Le motif du congé ne peut pas dépasser {MaxNoteLength} caractères.";
        return null;
    }

    private void Apply(DateOnly startDate, DateOnly endDate, string? note)
    {
        if (RefusalFor(startDate, endDate, note) is { } refusal)
            throw new ArgumentException(refusal);

        StartDate = startDate;
        EndDate = endDate;
        Note = NormalizeNote(note);
    }

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
