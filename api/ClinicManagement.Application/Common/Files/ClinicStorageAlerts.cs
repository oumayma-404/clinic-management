namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// When a cabinet is warned that its hosted storage is filling up (<c>features/camera-photo-import</c>, phase 3) —
/// pure, so the levels can be read and tested without a repository.
///
/// <para>Two levels, 80 % and 95 %, each announced once per crossing; the warning is withdrawn — and therefore
/// re-armed — only below 75 %, so a cabinet hovering at 80 % is not told the same thing every time a file comes and
/// goes.</para>
/// </summary>
public static class ClinicStorageAlerts
{
    public const int WarnPercent = 80;
    public const int UrgentPercent = 95;
    public const int RearmPercent = 75;

    /// <summary>What the bell should say: 95, 80, « clear » (−1), or « leave it as it is » (0, inside the hysteresis band).</summary>
    public static int LevelFor(long usedBytes, long quotaBytes)
    {
        if (quotaBytes <= 0)
        {
            return -1;
        }

        // Integer arithmetic, so 8 Go sur 10 Go is exactly the boundary the vendor and the cabinet read.
        var scaled = usedBytes * 100;
        if (scaled >= quotaBytes * UrgentPercent)
        {
            return UrgentPercent;
        }

        if (scaled >= quotaBytes * WarnPercent)
        {
            return WarnPercent;
        }

        return scaled < quotaBytes * RearmPercent ? -1 : 0;
    }
}
