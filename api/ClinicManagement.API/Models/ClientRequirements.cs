namespace ClinicManagement.API.Models;

/// <summary>
/// What a native shell must be to keep talking to this server: the floor, the release the stores currently
/// carry, and where to get it. Serialized as-is by <c>GET /api/meta/client-requirements</c> (AC-28).
///
/// <para><b>One type answers both halves of the version floor, deliberately.</b> <c>ClientVersionMiddleware</c>
/// measures a caller with <see cref="IsBelowFloor"/> on this same object, so the floor a stale client is
/// <i>told</i> about is literally the floor it was refused by. Two readers of <c>Clients:*</c> would be free to
/// drift, and the drift surfaces as « mettez à jour vers 1.2.0 » on a build that already is 1.2.0.</para>
///
/// <para><b>An absent or unparseable floor means no floor</b>, and that direction is not an oversight: this
/// setting's failure mode has to be « nothing is refused », never « every shell is refused ». A typo in an
/// operator-owned file must not take the whole API off the air for the phones (AC-34).</para>
/// </summary>
public sealed record ClientRequirements(
    string MinimumShellVersion,
    string CurrentShellVersion,
    ClientStoreUrls StoreUrls)
{
    /// <summary>Where an operator states all of this. Read per request, so raising the floor needs no restart.</summary>
    private const string Section = "Clients";

    public static ClientRequirements Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);

        return new ClientRequirements(
            section["MinimumShellVersion"] ?? string.Empty,
            section["CurrentShellVersion"] ?? string.Empty,
            new ClientStoreUrls(
                section["StoreUrls:Android"] ?? string.Empty,
                section["StoreUrls:Ios"] ?? string.Empty));
    }

    /// <summary>
    /// Whether <paramref name="clientVersion"/> is older than the floor. <b>False for anything unreadable</b> —
    /// an absent header (every browser, and every server-side BFF hop), a malformed one, or an unset floor —
    /// which is AC-32: a client that says nothing about itself is accepted exactly as before.
    /// </summary>
    public bool IsBelowFloor(string? clientVersion) =>
        Version.TryParse(MinimumShellVersion, out var floor)
        && Version.TryParse(clientVersion, out var reported)
        && reported < floor;
}

/// <summary>Both listings, so a refused shell can name the right store for the phone it is running on.</summary>
public sealed record ClientStoreUrls(string Android, string Ios);
