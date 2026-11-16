using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Copies this machine's coffre into the archive folder, then tells the server it happened
/// (<c>clinic-file-vault</c>).
///
/// <para>⚠️ <b>Why the shell and not the server:</b> a coffre original was never uploaded, so nothing server-side
/// can see whether it is safe — and dental imaging carries a ten-to-twenty-year retention duty. Without this the
/// practice's own screens would show a healthy backup while a decade of studies sat on one undefended disk.</para>
///
/// <para>⚠️ <b>It copies rather than moves, and never deletes on either side.</b> The coffre is the app's live
/// store; the copy is a second place. It also never deletes from the destination — <c>FileMirrorService</c>'s
/// rule and for its reason: the folder can outgrow the cabinet, and that is the right trade for the doctor's own
/// copy of their own imaging.</para>
///
/// <para>⚠️ <b>Size is the whole freshness check</b>, exactly as the file mirror's is. These files are immutable
/// by construction — the product has add and delete, never replace — and hashing to decide whether to copy would
/// mean reading every gigabyte to avoid copying it.</para>
/// </summary>
public sealed class VaultCopyService
{
    /// <summary>The subfolder the copy occupies in the archive folder, beside the <c>archive-*.zip</c> files.</summary>
    public const string DestinationFolderName = "coffre";

    private static readonly TimeSpan ReportTimeout = TimeSpan.FromSeconds(30);

    private readonly ServerConfig _server;
    private readonly ArchiveCopySettings _settings;

    public VaultCopyService(ServerConfig server, ArchiveCopySettings settings)
    {
        _server = server;
        _settings = settings;
    }

    /// <summary>
    /// Copies whatever the coffre holds and is missing from the destination, then reports.
    ///
    /// <para>⚠️ <b>The report is sent when the copy is complete and only then</b>, because it is what silences the
    /// staleness alert: reporting a partial run would tell a practice its imaging is safe while some of it is not,
    /// on the one screen whose whole job is to answer that question. A run that copied nothing because there was
    /// nothing to copy <b>does</b> report — an empty coffre is fully copied.</para>
    /// </summary>
    public async Task<ArchiveCopyOutcome> CopyNowAsync(
        string? reuseToken = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            return new ArchiveCopyOutcome(false, "La copie automatique n'est pas configurée sur ce poste.");
        }

        var source = VaultFolder.Resolve(_settings);
        if (!Directory.Exists(source))
        {
            // No coffre folder at all is not a failure: this machine has simply never filed a large file.
            return new ArchiveCopyOutcome(true, "Aucun fichier volumineux n'est conservé sur ce poste.");
        }

        string destination;
        string? sameDisk;
        try
        {
            destination = Path.Combine(_settings.Folder, DestinationFolderName);

            // Before anything is created: with the archive disk unplugged, a camera's card can hold its letter.
            if (ArchiveDisk.Unplugged(_settings.Folder) is { } unplugged)
            {
                return new ArchiveCopyOutcome(false, unplugged);
            }

            // ⚠️ Said, never refused: this copy predates the rule, and a cabinet whose folder is another partition of
            // the PC's own disk would silently stop being copied — then told 30 days later to plug in a disk it has.
            sameDisk = AddOnlyFolderCopy.SameDiskRefusal(source, _settings.Folder);

            Directory.CreateDirectory(destination);
            ArchiveCopyService.HardenFolder(destination);
        }
        catch (Exception ex)
        {
            return new ArchiveCopyOutcome(false, $"Le dossier de destination n'est pas utilisable : {ex.Message}");
        }

        AddOnlyFolderCopy.Result copy;
        try
        {
            copy = await AddOnlyFolderCopy.CopyAsync(source, destination, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return new ArchiveCopyOutcome(false, $"La copie du coffre a échoué : {ex.Message}");
        }

        // ⚠️ What the destination now HOLDS, never what this run copied. Reporting the run's own bytes sent « 0 » on
        // every run after the first, the server answered « incomplete » to a complete copy, and the 30-day alert
        // fired over a coffre that was safely on a second disk.
        var reported = await ReportAsync(copy.Files, copy.Bytes, reuseToken, cancellationToken);

        var message = copy.Copied == 0
            ? $"Le coffre était déjà à jour ({copy.Files} fichier(s))."
            : $"{copy.Copied} fichier(s) copié(s) dans {destination}.";
        if (sameDisk != null)
        {
            message += " " + sameDisk;
        }

        // ⚠️ A report that did not land, or landed short, is stated rather than swallowed: the copy is on disk
        // either way, but the practice will keep being nagged, and « la copie a réussi » beside a nag that will not
        // clear is the confusing pair. « Incomplete » names a different cause from « not reported » on purpose —
        // one is a network problem to retry, the other is files this machine's coffre does not have.
        return reported switch
        {
            VaultReportOutcome.Covered => new ArchiveCopyOutcome(true, message, destination),
            VaultReportOutcome.Incomplete => new ArchiveCopyOutcome(
                true,
                message + " Le serveur indique que la copie ne couvre pas tous les fichiers du coffre ;"
                + " l'alerte reste active.",
                destination),
            _ => new ArchiveCopyOutcome(
                true, message + " Le serveur n'a pas pu être informé ; l'alerte peut persister.", destination),
        };
    }

    /// <summary>
    /// Tells the server a copy landed, and asks whether it covered everything.
    ///
    /// <para>⚠️ Three outcomes, not two. A failure to report is <see cref="VaultReportOutcome.NotReported"/> — the
    /// copy is real and on disk, and the only consequence is that the alert keeps standing, which is the safe
    /// direction. But the server also compares the figures with what it has on record, and answers
    /// <see cref="VaultReportOutcome.Incomplete"/> when the copy fell short: it never saw the originals, so this
    /// comparison is the only evidence that exists, and a copy covering three studies of four hundred used to
    /// clear the alert exactly as a complete one did.</para>
    /// </summary>
    private async Task<VaultReportOutcome> ReportAsync(
        int fileCount, long totalBytes, string? reuseToken, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = ReportTimeout };

            var token = reuseToken;
            if (string.IsNullOrEmpty(token))
            {
                var exchange = await ArchiveGrant.ExchangeAsync(
                    http, _server, _settings.GrantSecret, cancellationToken);
                if (!exchange.Succeeded)
                {
                    return VaultReportOutcome.NotReported;
                }

                token = exchange.Token;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_server.BaseUrl}/api/backup/vault-copy");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Both headers, for the archive download's reason: the bearer gets past the policy, the grant is what
            // the server re-checks to know this is an authorised machine and which cabinet it serves.
            request.Headers.Add(ArchiveGrant.Header, _settings.GrantSecret);
            request.Content = new StringContent(
                $"{{\"fileCount\":{fileCount},\"totalBytes\":{totalBytes}}}",
                Encoding.UTF8,
                "application/json");

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return VaultReportOutcome.NotReported;
            }

            // An older server answered 204 with no body; that is « reported » and nothing more can be said.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return VaultReportOutcome.Covered;
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("covered", out var covered)
                && covered.ValueKind == JsonValueKind.False
                    ? VaultReportOutcome.Incomplete
                    : VaultReportOutcome.Covered;
        }
        catch
        {
            return VaultReportOutcome.NotReported;
        }
    }
}

/// <summary>
/// What the server made of a coffre-copy report.
///
/// <para>⚠️ <see cref="Incomplete"/> is a success that is not good enough, and it is the reason this is not a
/// bool. The server never received a coffre original, so the file count and byte total it is handed are the only
/// evidence it will ever have; comparing them with its own records is what tells a complete copy from one that
/// covered a handful of studies, and only the first may clear the staleness alert.</para>
/// </summary>
internal enum VaultReportOutcome
{
    /// <summary>The report did not land — a network fault, a refused grant. The alert keeps standing.</summary>
    NotReported = 0,

    /// <summary>The copy accounts for everything on record. The alert clears.</summary>
    Covered = 1,

    /// <summary>The report landed and fell short of the record. The alert deliberately stays up.</summary>
    Incomplete = 2,
}
