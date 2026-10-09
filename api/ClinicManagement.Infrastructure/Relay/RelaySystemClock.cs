using System.Runtime.InteropServices;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// The machine's wall clock, which a PC de secours sets from the cloud's (<c>clinic-pc-copy</c> D20b, FR-3, EC-10): the
/// server reads <c>DateTime.UtcNow</c> in hundreds of places — dates, the caisse's day, authenticator codes — so the
/// one clock is corrected rather than shifted in each.
/// </summary>
public interface IRelaySystemClock
{
    /// <summary>Sets the machine's clock to <paramref name="utc"/>; false with the reason in French when it cannot.</summary>
    bool TrySet(DateTime utc, out string? error);
}

/// <summary>
/// Windows: <c>SetSystemTime</c>, after enabling <c>SeSystemtimePrivilege</c> on the process token. The service runs as
/// LocalSystem, which holds it; an ordinary account does not, and that is reported rather than retried.
/// </summary>
public sealed class WindowsSystemClock : IRelaySystemClock
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x0002;
    private const int NotAllAssigned = 1300;

    public bool TrySet(DateTime utc, out string? error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "ce système ne permet pas au PC de secours de régler l'heure.";
            return false;
        }

        if (!TryEnablePrivilege(out error))
        {
            return false;
        }

        utc = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc;
        var time = new SystemTime
        {
            Year = (ushort)utc.Year, Month = (ushort)utc.Month, DayOfWeek = (ushort)utc.DayOfWeek, Day = (ushort)utc.Day,
            Hour = (ushort)utc.Hour, Minute = (ushort)utc.Minute, Second = (ushort)utc.Second, Milliseconds = (ushort)utc.Millisecond,
        };
        if (!SetSystemTime(ref time))
        {
            error = $"Windows a refusé de changer l'heure (code {Marshal.GetLastWin32Error()}).";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Enables the right to set the clock on this process; false (and why) when the account does not hold it.</summary>
    public static bool TryEnablePrivilege(out string? error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "ce système ne permet pas au PC de secours de régler l'heure.";
            return false;
        }

        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            error = $"Windows n'a pas ouvert les droits du service (code {Marshal.GetLastWin32Error()}).";
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeSystemtimePrivilege", out var luid))
            {
                error = $"Windows ne connaît pas le droit de régler l'heure (code {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
            var adjusted = AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
            var code = Marshal.GetLastWin32Error();
            if (!adjusted || code == NotAllAssigned)
            {
                error = "le service du PC de secours n'a pas le droit de régler l'heure de Windows.";
                return false;
            }

            error = null;
            return true;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemTime(ref SystemTime time);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returned);
}

/// <summary>D20b's rule, pure: when an answer says the PC's clock is wrong, and by how much.</summary>
public static class RelayClockRules
{
    /// <summary>Within this the clocks agree (EC-10's bell row uses the same figure).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    /// <summary>An answer slower than this says too little about when it was written: no correction from it.</summary>
    public static readonly TimeSpan MaxRoundTrip = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far the PC's clock is from the cloud's, as one answer tells it: the cloud's time when it answered, plus half the
    /// round trip, minus the PC's time when the answer arrived. Null when the round trip is too long to say.
    /// </summary>
    public static TimeSpan? Offset(DateTime cloudUtc, DateTime localAtReceiptUtc, TimeSpan roundTrip) =>
        roundTrip < TimeSpan.Zero || roundTrip > MaxRoundTrip
            ? null
            : cloudUtc + roundTrip / 2 - localAtReceiptUtc;

    public static bool IsWrong(TimeSpan offset) => offset.Duration() > Tolerance;
}
