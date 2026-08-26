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
        // « Forfait de rappels », never « forfait » alone: the console already has a `Plan`/« forfait » vocabulary for
        // the subscription, and a journal row a vendor reads at speed must not be ambiguous about which one moved.
        PlatformAccessAction.GrantedMessagingAllowance => "Forfait de rappels enregistré",
        PlatformAccessAction.CancelledMessagingAllowance => "Forfait de rappels annulé",
        // Names the *account*, not the cabinet, because it is the only row here that acts on one person — and the
        // row carries their address beside it. « Second facteur réinitialisé » rather than « 2FA désactivée »: the
        // factor is not switched off, it is cleared so its owner can enrol a new one, and the second wording would
        // read on the journal as the vendor having lowered a cabinet's protection.
        PlatformAccessAction.SecondFactorReset => "Second facteur d'un compte réinitialisé",
        PlatformAccessAction.PasswordReset => "Mot de passe d'un compte réinitialisé",
        _ => action.ToString()
    };

}
