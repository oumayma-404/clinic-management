namespace ClinicManagement.Application.Common;

/// <summary>
/// Canonical, client-facing error strings shared by the two halves of the <c>{ "error": "&lt;message&gt;" }</c>
/// contract: the API-layer <c>ApiControllerBase</c> failure helper and the Application-layer
/// <c>ExceptionMiddleware</c>. Kept in one place so the generic fallback can't drift between them.
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// Generic, internals-free message for an unhandled failure (never leaks details to the client).
    /// French, like the rest of the user-facing product — this string is shown verbatim in the UI.
    /// </summary>
    public const string Generic = "Une erreur est survenue lors du traitement de votre demande.";

    /// <summary>
    /// Shown when someone else changed the same record while this user was editing it. Deliberately says what
    /// happened and what to do — the previous behaviour was a silent last-write-wins, so the loser never knew
    /// their change had been discarded.
    /// </summary>
    public const string Conflict =
        "Cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie. "
        + "Rechargez pour voir la version à jour, puis appliquez à nouveau votre modification.";

    /// <summary>Escalated wording after a second consecutive conflict on the same edit.</summary>
    public const string RepeatedConflict =
        "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement "
        + "dessus en même temps — coordonnez-vous avant de réessayer.";

    /// <summary>
    /// AC-P1.14/1.18: the database's appointment exclusion constraint refused an overlapping booking.
    /// <para>
    /// Distinct wording from <see cref="Conflict"/> on purpose. Both surface as a 409, but they are different
    /// events and telling a user to "reload and re-apply your change" would be wrong advice here — the slot is
    /// genuinely taken, so the remedy is a different time, not a retry.
    /// </para>
    /// </summary>
    public const string SlotAlreadyBooked =
        "Ce créneau vient d'être réservé pour ce praticien par quelqu'un d'autre. "
        + "Choisissez un autre horaire.";
}
