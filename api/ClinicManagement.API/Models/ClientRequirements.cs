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
    ClientStoreUrls StoreUrls,
    /// <summary>
    /// SHA-256 of the Windows setup at <c>StoreUrls.Windows</c>, uppercase hex, or empty when the operator has
    /// not stated one.
    ///
    /// <para>⚠️ <b>The desktop shell downloads that file and runs it ELEVATED</b> — « Mettre à jour
    /// maintenant » is one click and one UAC prompt — so it is the single most dangerous byte stream in this
    /// product. With a hash published the shell refuses a mismatch and deletes the file; with none it accepts
    /// what it was served, because an offline-LAN server predating this field must still be able to ship an
    /// update. Empty therefore means « unverifiable », never « verified ».</para>
    /// </summary>
    string WindowsSetupSha256,
    /// <summary>
    /// The Android shell's own floor and release (<c>android-in-app-update</c>).
    ///
    /// <para>⚠️ <b>Separate, because the shared pair above is the Windows one.</b> <c>CurrentShellVersion</c> is
    /// auto-filled from the Velopack package, so a phone compared its 1.1.1 against the desktop's 1.3.0, and
    /// raising <c>MinimumShellVersion</c> for the desktop would have refused every phone. The top-level fields keep
    /// their meaning for every client that already reads them; only an Android caller is measured by this.</para>
    /// </summary>
    AndroidShellRequirements Android)
{
    /// <summary>Where an operator states all of this. Read per request, so raising the floor needs no restart.</summary>
    private const string Section = "Clients";

    /// <summary>What <c>window.__clinicShell.platform</c> says on the Android shell, sent as <c>X-Client-Platform</c>.</summary>
    public const string AndroidPlatform = "android";

    public static ClientRequirements Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);

        return new ClientRequirements(
            section["MinimumShellVersion"] ?? string.Empty,
            section["CurrentShellVersion"] ?? string.Empty,
            new ClientStoreUrls(
                section["StoreUrls:Android"] ?? string.Empty,
                section["StoreUrls:Ios"] ?? string.Empty,
                section["StoreUrls:Windows"] ?? string.Empty),
            section["WindowsSetupSha256"] ?? string.Empty,
            new AndroidShellRequirements(
                section["Android:MinimumVersion"] ?? string.Empty,
                section["Android:CurrentVersion"] ?? string.Empty,
                section["Android:DownloadUrl"] ?? string.Empty,
                section["Android:Sha256"] ?? string.Empty));
    }

    /// <summary>
    /// Whether <paramref name="clientVersion"/> is older than the floor for <paramref name="platform"/>.
    /// <b>False for anything unreadable</b> — an absent header (every browser, and every server-side BFF hop), a
    /// malformed one, or an unset floor — which is AC-32: a client that says nothing about itself is accepted
    /// exactly as before.
    ///
    /// <para>⚠️ An Android caller is measured by <see cref="AndroidShellRequirements.MinimumVersion"/> <b>only</b>
    /// — never by the shared floor, or a desktop release would lock out every phone. Any other platform, and a
    /// caller naming none, keeps the shared floor.</para>
    /// </summary>
    public bool IsBelowFloor(string? clientVersion, string? platform) =>
        Version.TryParse(FloorFor(platform), out var floor)
        && Version.TryParse(clientVersion, out var reported)
        && reported < floor;

    private string FloorFor(string? platform) =>
        string.Equals(platform?.Trim(), AndroidPlatform, StringComparison.OrdinalIgnoreCase)
            ? Android.MinimumVersion
            : MinimumShellVersion;
}

/// <summary>
/// The Android shell's floor, its newest release, and the APK to fetch it from. Empty fields mean « nothing
/// stated »: no floor, no update to offer, no file to download.
///
/// <para><see cref="Sha256"/> is uppercase hex of the APK at <see cref="DownloadUrl"/>. The shell refuses to hand
/// Android a file whose hash differs; Android then re-checks the signature itself, so the hash is what catches a
/// truncated download rather than what proves origin.</para>
/// </summary>
public sealed record AndroidShellRequirements(
    string MinimumVersion,
    string CurrentVersion,
    string DownloadUrl,
    string Sha256);

/// <summary>
/// Where each client goes to update itself, so a refused shell can name the right destination for the machine it
/// is running on.
///
/// <para>⚠️ <b><see cref="Windows"/> is not a store listing and that is the point.</b> The desktop shell is
/// distributed as an installer the operator hosts, so this is a plain download URL rather than a marketplace
/// entry. It sits on this record anyway because the question the record answers — « where does *this* client get
/// a newer build? » — is one question with three answers, and a second endpoint for the desktop half would be a
/// second place for the floor and the current release to drift apart.</para>
///
/// <para>Empty means that platform has nowhere to send anyone yet, which every consumer must render as « no
/// download link » rather than as a broken one.</para>
/// </summary>
public sealed record ClientStoreUrls(string Android, string Ios, string Windows);
