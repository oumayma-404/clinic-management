using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The French wording the console's own reads carry, in one place — <c>AuditLabels</c>' counterpart for the vendor
/// surface.
///
/// <para>Server-side for that file's reason: the values are CLR enum names and month numbers, and a client that
/// translated them would be keeping a second copy of a map that grows with Parts 4–6.</para>
/// </summary>
public static class PlatformAccessLabels
{
    /// <summary>
    /// What a ledger row says happened. An unmapped member falls through to its own name rather than to
    /// « Inconnu » — a member added in a later part then reads as slightly technical instead of disappearing.
    /// </summary>
    public static string Action(PlatformAccessAction action) => action switch
    {
        PlatformAccessAction.ViewedClinic => "Fiche cabinet consultée",
        PlatformAccessAction.GrantedPeriod => "Paiement enregistré",
        PlatformAccessAction.CancelledPeriod => "Période annulée",
        // « Cabinet suspendu », not « Abonnement suspendu »: AC-6.3 forbids presenting suspension as a payment
        // state, and the journal is read by whoever asks why a practice cannot record work.
        PlatformAccessAction.Suspended => "Cabinet suspendu",
        PlatformAccessAction.Unsuspended => "Suspension levée",
        // The fall-through below is for a member a LATER part adds; this one arrives with the write that
        // produces it, and it is the heaviest row in the ledger — the only console action that writes a
        // practice's clinical records.
        PlatformAccessAction.RestoredClinic => "Cabinet restauré",
        _ => action.ToString()
    };

}
