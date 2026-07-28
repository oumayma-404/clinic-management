namespace ClinicManagement.Application.Common;

/// <summary>
/// The clinic's wall clock. Tunisia is <b>UTC+1 all year</b> (no DST since 2008).
/// <para>
/// Created for P1 — working hours are expressed in clinic-local time while appointments are stored as UTC
/// instants, so enforcing one against the other needs a conversion that cannot be guessed. It also replaces
/// the <b>two byte-identical private copies</b> of <c>ResolveTunisiaTimeZone()</c> that had been copy-pasted
/// into separate query handlers (adjacent defect <b>A-21</b>), and is the single helper P6's local-day work
/// builds on.
/// </para>
/// <para>
/// ⚠️ <see cref="StartOfLocalDayUtc"/> and <see cref="EndOfLocalDayUtc"/> return an <b>explicit UTC instant</b>,
/// never a bare local <c>DateTime</c>. <c>ApplicationDbContext</c> treats <see cref="DateTimeKind.Unspecified"/>
/// as UTC on write, so handing a local value to a query would silently reinterpret it as UTC and shift every
/// boundary by an hour.
/// </para>
/// </summary>
public static class ClinicClock
{
    /// <summary>Fallback offset when the host has no tz database entry (bare containers, some Windows SKUs).</summary>
    private static readonly TimeSpan TunisiaOffset = TimeSpan.FromHours(1);

    private static readonly Lazy<TimeZoneInfo?> Tunisia = new(() =>
    {
        // IANA first (Linux/macOS containers), then the Windows id. Both are tried because the same binary
        // runs on a Windows clinic PC and in a Linux Cloud container.
        foreach (var id in new[] { "Africa/Tunis", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return null;
    });

    /// <summary>The clinic-local wall-clock time for a UTC instant.</summary>
    public static DateTime ToClinicLocal(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var zone = Tunisia.Value;
        return zone != null
            ? TimeZoneInfo.ConvertTimeFromUtc(asUtc, zone)
            : asUtc + TunisiaOffset;
    }

    /// <summary>The UTC instant a clinic-local wall-clock time corresponds to.</summary>
    public static DateTime ToUtc(DateTime clinicLocal)
    {
        var unspecified = DateTime.SpecifyKind(clinicLocal, DateTimeKind.Unspecified);
        var zone = Tunisia.Value;
        return zone != null
            ? TimeZoneInfo.ConvertTimeToUtc(unspecified, zone)
            : DateTime.SpecifyKind(unspecified - TunisiaOffset, DateTimeKind.Utc);
    }

    /// <summary>Today's date in the clinic's zone.</summary>
    public static DateTime ClinicToday(DateTime? nowUtc = null) =>
        ToClinicLocal(nowUtc ?? DateTime.UtcNow).Date;

    /// <summary>The year the clinic is currently in — the authority for a document's number sequence.</summary>
    public static int ClinicYear(DateTime? nowUtc = null) => ClinicToday(nowUtc).Year;

    /// <summary>Midnight of a clinic-local day, as a UTC instant.</summary>
    public static DateTime StartOfLocalDayUtc(DateTime clinicLocalDate) => ToUtc(clinicLocalDate.Date);

    /// <summary>The exclusive end of a clinic-local day, as a UTC instant.</summary>
    public static DateTime EndOfLocalDayUtc(DateTime clinicLocalDate) => ToUtc(clinicLocalDate.Date.AddDays(1));
}
