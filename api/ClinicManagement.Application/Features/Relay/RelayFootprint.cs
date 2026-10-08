namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// How much free space a PC de secours needs (AC-1.8): « the clinic's records and files twice over » — the copy, and
/// the room a re-copy needs beside it. One rule for the offer, the pairing code and the installer's <c>/NEEDBYTES=</c>.
/// </summary>
public static class RelayFootprint
{
    /// <summary>
    /// The records and the server it runs on, as one allowance: the rows of even a large cabinet are a few hundred
    /// MB, and PostgreSQL with the app is about as much again. An estimate on purpose — measuring the clinic's share of
    /// a shared database is not worth a query the offer runs at every start.
    /// </summary>
    public const long RecordsAllowanceBytes = 1L << 30;

    public static long NeedBytes(long hostedFileBytes) => 2 * (Math.Max(0, hostedFileBytes) + RecordsAllowanceBytes);
}
