using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>What a copy attempt did. French, because every one of these reaches a screen.</summary>
public sealed record ArchiveCopyOutcome(bool Succeeded, string Message, string? Path = null);

/// <summary>
/// Fetches the cabinet's archive onto this machine, unattended (<c>clinic-archive-auto-copy</c>).
///
/// <para>⚠️ <b>This is the shell's first authenticated HTTP call, and the first time it holds a credential.</b>
/// Everything before it was a viewer: the WebView2 control carried the session and this process never saw one.
/// The grant is deliberately not a password — it authorises one action on one cabinet, it is revocable from
/// « Paramètres » and it appears in that list with its last use, so an owner can answer « which machines can pull
/// my record? » and act on the answer.</para>
///
/// <para>⚠️ <b>No custom certificate validation, deliberately.</b> `HttpClient` uses the machine's own trust
/// store — which on a LAN install is exactly where the client installer put the cabinet's CA, and on a hosted one
/// is where Let's Encrypt already is. A callback that accepted anything would silently cover a
/// man-in-the-middle on the one transfer carrying the whole medical record.</para>
/// </summary>
public sealed class ArchiveCopyService
{
    /// <summary>The archive is built in full before a byte is sent, so this bounds an operation, not a transfer.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    /// <summary>Headroom over the previous copy's size before a write is begun. A cabinet grows.</summary>
    private const double FreeSpaceFactor = 1.25;

    private const string FilePrefix = "archive-";
    private const string FileSuffix = ".zip";
    private const string PartSuffix = ".part";

    private readonly ServerConfig _server;
    private readonly ArchiveCopySettings _settings;

    public ArchiveCopyService(ServerConfig server, ArchiveCopySettings settings)
    {
        _server = server;
        _settings = settings;
    }

    /// <summary>The newest copy already on disk, or null. Also what <see cref="ArchiveCopySettings.IsDue"/> reads.</summary>
    public static DateTime? NewestCopyUtc(string folder)
    {
        try
        {
            return ExistingCopies(folder).FirstOrDefault()?.LastWriteTimeUtc;
        }
        catch
        {
            // An unreadable folder is not « no copies »: reporting null would make one due immediately and every
            // launch would retry a folder that cannot be listed. The caller treats null-with-no-folder as off.
            return null;
        }
    }

    private static List<FileInfo> ExistingCopies(string folder) =>
        !Directory.Exists(folder)
            ? new List<FileInfo>()
            : new DirectoryInfo(folder)
                .GetFiles(FilePrefix + "*" + FileSuffix)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

    /// <summary>
    /// Pulls one copy. Never throws — every failure comes back as a French sentence the caller shows.
    ///
    /// <para>⚠️ <b>An existing good copy is never at risk.</b> The download goes to a <c>.part</c> file that is
    /// renamed only once the stream has completed, and pruning runs strictly after that rename (AC-5, AC-6, AC-9).
    /// A refusal, a disconnection or a full disk therefore leaves the folder exactly as it was.</para>
    /// </summary>
    public async Task<ArchiveCopyOutcome> CopyNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            return new ArchiveCopyOutcome(false, "La copie automatique n'est pas configurée sur ce poste.");
        }

        string partPath;
        try
        {
            Directory.CreateDirectory(_settings.Folder);
            HardenFolder(_settings.Folder);

            var refusal = CheckFreeSpace(_settings.Folder);
            if (refusal != null)
            {
                return new ArchiveCopyOutcome(false, refusal);
            }

            partPath = Path.Combine(
                _settings.Folder,
                $"{FilePrefix}{DateTime.Now:yyyy-MM-dd-HHmm}{FileSuffix}{PartSuffix}");
        }
        catch (Exception ex)
        {
            return new ArchiveCopyOutcome(false, $"Le dossier de destination n'est pas utilisable : {ex.Message}");
        }

        try
        {
            using var http = new HttpClient { Timeout = Timeout };

            var token = await ExchangeGrantAsync(http, cancellationToken);
            if (token == null)
            {
                return new ArchiveCopyOutcome(
                    false,
                    "Ce poste n'est plus autorisé. Autorisez-le à nouveau depuis « Paramètres » sur le serveur.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_server.BaseUrl}/api/backup/archive");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ArchiveCopyOutcome(
                    false, $"Le serveur a refusé la copie (code {(int)response.StatusCode}).");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(partPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var finalPath = partPath[..^PartSuffix.Length];
            File.Move(partPath, finalPath, overwrite: true);

            // Strictly after the rename: pruning first would trade a copy we have for one we might not get.
            Prune(_settings.Folder, _settings.KeepCopies);

            return new ArchiveCopyOutcome(true, $"Copie déposée dans {finalPath}", finalPath);
        }
        catch (Exception ex)
        {
            TryDelete(partPath);
            return new ArchiveCopyOutcome(false, $"La copie a échoué : {ex.Message}");
        }
    }

    /// <summary>Trades the grant for a short-lived token, or null for every refusal (AC-3's one wording).</summary>
    private async Task<string?> ExchangeGrantAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_server.BaseUrl}/api/backup/archive-grants/token");
        request.Headers.Add("X-Archive-Grant", _settings.GrantSecret);
        request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return body.RootElement.TryGetProperty("accessToken", out var token) ? token.GetString() : null;
    }

    /// <summary>
    /// Refuses before writing when the volume cannot plausibly hold another copy (AC-9's disk-full case), sized
    /// from the previous copy because nothing else knows how big this cabinet is.
    /// </summary>
    private static string? CheckFreeSpace(string folder)
    {
        var previous = ExistingCopies(folder).FirstOrDefault();
        if (previous == null)
        {
            return null;
        }

        var needed = (long)(previous.Length * FreeSpaceFactor);
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;

        return free >= needed
            ? null
            : $"Espace insuffisant : il faut environ {needed / (1024 * 1024)} Mo et il en reste "
              + $"{free / (1024 * 1024)} Mo sur ce disque.";
    }

    /// <summary>Keeps the newest <paramref name="keep"/> copies. Best-effort: a locked file is skipped, not fatal.</summary>
    private static void Prune(string folder, int keep)
    {
        foreach (var stale in ExistingCopies(folder).Skip(Math.Max(1, keep)))
        {
            TryDelete(stale.FullName);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Nothing to do and nothing worth failing a completed copy over.
        }
    }

    /// <summary>
    /// Breaks inheritance and leaves Administrators + this account (AC-7), the policy
    /// <c>DirectoryAclHardener</c> applies server-side. It is re-implemented rather than shared because the shell
    /// is a separate solution that references none of the API's assemblies.
    ///
    /// <para>⚠️ <b>Best-effort, and it must be.</b> A folder on a network share or an exFAT stick supports no
    /// ACLs at all; refusing to copy there would remove the capability over a hardening that volume could never
    /// have offered. The dialog states the risk instead.</para>
    /// </summary>
    private static void HardenFolder(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            var security = info.GetAccessControl();

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (var identity in new[]
                     {
                         new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                         WindowsIdentity.GetCurrent().User!,
                     })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            info.SetAccessControl(security);
        }
        catch
        {
            // See the note above: a volume with no ACL support is a supported destination.
        }
    }

    /// <summary>
    /// Whether the destination volume is BitLocker-protected (AC-8) — <b>stated, never enforced</b>.
    ///
    /// <para>⚠️ Returns null for « je ne sais pas », which is the common answer: <c>manage-bde</c> needs elevation.
    /// Reporting that honestly is the point — asserting « non chiffré » when we could not look would be the same
    /// class of confident wrong answer as reading a failed read as an empty list.</para>
    /// </summary>
    public static bool? IsDriveEncrypted(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder))?.TrimEnd('\\');
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            using var process = Process.Start(new ProcessStartInfo("manage-bde", $"-status {root}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process == null || !process.WaitForExit(5000) || process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (output.Contains("Protection On", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Protection activée", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return output.Contains("Protection Off", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("Protection désactivée", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        }
        catch
        {
            return null;
        }
    }
}
