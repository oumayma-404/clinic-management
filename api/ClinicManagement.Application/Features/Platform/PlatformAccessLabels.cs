using System.Globalization;
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
        _ => action.ToString()
    };

    /// <summary>
    /// « août 2026 » for a trend bucket.
    ///
    /// <para>⚠️ Explicitly <c>fr-FR</c>, never the ambient culture: this runs in a container whose culture is
    /// whatever the base image sets, so the invariant one would render « August 2026 » into a French screen.</para>
    /// </summary>
    public static string Month(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", French);

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
}
