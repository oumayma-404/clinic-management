namespace ClinicManagement.Application.Features.WaitingList;

/// <summary>
/// The two free-text lengths a salle-d'attente entry is allowed, and the French refusals for exceeding them.
///
/// <para>⚠️ <b>Only PostgreSQL used to know these numbers.</b> The columns are <c>varchar(200)</c> and
/// <c>varchar(1000)</c>, the input had no <c>maxLength</c> and neither command checked, so a « Créneau souhaité »
/// of 201 characters reached the database and came back as an EF sentence — « An error occurred while saving the
/// entity changes. » — inside a French toast. A limit the database enforces and nothing else states is a limit
/// the user meets as a crash.</para>
///
/// <para>Stated here so the two commands and the two inputs quote the same figures. Keep them equal to
/// <c>WaitingListEntryConfiguration</c>'s <c>HasMaxLength</c>.</para>
/// </summary>
public static class WaitingListLimits
{
    public const int DesiredTimeframeMaxLength = 200;
    public const int NoteMaxLength = 1000;

    public const string DesiredTimeframeTooLong =
        "Le créneau souhaité ne peut pas dépasser 200 caractères.";

    public const string NoteTooLong = "La note ne peut pas dépasser 1000 caractères.";

    /// <summary>The French refusal for whichever field is too long, or null when both fit.</summary>
    public static string? Refuse(string? desiredTimeframe, string? note)
    {
        if (desiredTimeframe?.Trim().Length > DesiredTimeframeMaxLength)
        {
            return DesiredTimeframeTooLong;
        }

        return note?.Trim().Length > NoteMaxLength ? NoteTooLong : null;
    }
}
