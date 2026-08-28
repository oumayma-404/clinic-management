using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Keeps the cabinet's patient files on this machine as loose, browsable, per-patient folders
/// (<c>patient-file-mirror</c>).
///
/// <para>⚠️ <b>Write-only, and never a sync engine.</b> Nothing here reads the folder back to the server, uploads
/// from it, or reconciles a difference. The server is authoritative; this is a copy that happens to be
/// convenient. The moment it gained a direction back, every question a two-way sync has to answer — which side
/// wins, what a local edit means, what a local delete means — would land on a folder nobody is watching.</para>
///
/// <para>⚠️ <b>It never deletes</b> (AC-6). A file removed on the server stays here. That makes the folder able to
/// outgrow the cabinet, and it is the right trade: this is the doctor's own copy, and « le serveur a supprimé
/// votre radio » is not a behaviour a backup may have.</para>
/// </summary>
public sealed class FileMirrorService
{
    /// <summary>Per-file, not per-run: a mirror of a large cabinet is many small transfers, not one long one.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>`PageRequest.MaxPageSize` server-side. Asking for more is clamped, not refused.</summary>
    private const int ManifestPageSize = 200;

    private const string PartSuffix = ".part";

    /// <summary>The HTTP status the manifest read failed with, so the message can name it.</summary>
    private int ManifestFailureCode { get; set; }

    private readonly ServerConfig _server;
    private readonly ArchiveCopySettings _settings;

    public FileMirrorService(ServerConfig server, ArchiveCopySettings settings)
    {
        _server = server;
        _settings = settings;
    }

    /// <summary>Where the mirror lives, beside — never inside — the archive copies.</summary>
    public static string RootFor(string folder) => Path.Combine(folder, MirrorPathPlanner.RootFolderName);

    /// <summary>
    /// Brings the folder up to date. Never throws; every outcome is a French sentence for the caller to show.
    ///
    /// <para><paramref name="progress"/> is reported as files land, because the first run of a real cabinet is
    /// minutes to hours and a window that says nothing for that long is one the user force-quits — which is
    /// exactly what happened to this feature's sibling before its status line moved.</para>
    /// </summary>
    /// <param name="reuseToken">
    /// A bearer already obtained in this window, if the caller has one. ⚠️ <b>The grant→token endpoint is on
    /// the ARCHIVE rate limiter — three requests in ten minutes</b> — so a « Copier maintenant » that exchanged
    /// once for the archive and again for the mirror spent the budget before this pass had read a single page.
    /// Passing the archive's own token costs nothing and leaves headroom for a retry.
    /// </param>
    public async Task<ArchiveCopyOutcome> MirrorNowAsync(
        IProgress<string>? progress = null,
        string? reuseToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured || !_settings.MirrorFiles)
        {
            return new ArchiveCopyOutcome(false, "La copie des fichiers n'est pas activée sur ce poste.");
        }

        string root;
        try
        {
            root = RootFor(_settings.Folder);
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            return new ArchiveCopyOutcome(false, $"Le dossier des fichiers n'est pas utilisable : {ex.Message}");
        }

        try
        {
            using var http = new HttpClient { Timeout = Timeout };

            string token;
            if (reuseToken != null)
            {
                token = reuseToken;
            }
            else
            {
                var exchange = await ArchiveGrant.ExchangeAsync(
                    http, _server, _settings.GrantSecret, cancellationToken);

                if (!exchange.Succeeded)
                {
                    return new ArchiveCopyOutcome(
                        false,
                        exchange.Throttled ? ArchiveGrant.ThrottledMessage : ArchiveGrant.RefusedMessage);
                }

                token = exchange.Token!;
            }

            progress?.Report("Lecture de la liste des fichiers…");

            var manifest = await ReadManifestAsync(http, token, cancellationToken);
            if (manifest == null)
            {
                return new ArchiveCopyOutcome(
                    false,
                    ManifestFailureCode == 429
                        ? ArchiveGrant.ThrottledMessage
                        : $"La liste des fichiers n'a pas pu être lue (code {ManifestFailureCode}).");
            }

            progress?.Report($"{manifest.Count} fichier(s) au cabinet. Vérification de ce qui manque…");

            var plan = MirrorPathPlanner.Plan(manifest);
            return await PullAsync(http, token, root, plan, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled run is not a failure to report: everything already renamed is kept, and the next tick
            // resumes from the disk.
            return new ArchiveCopyOutcome(false, "Copie des fichiers interrompue.");
        }
        catch (Exception ex)
        {
            return new ArchiveCopyOutcome(false, $"La copie des fichiers a échoué : {ex.Message}");
        }
    }

    /// <summary>
    /// Walks every page of the manifest.
    ///
    /// <para>⚠️ The whole manifest is held before a single file is fetched, because the planner's collision rules
    /// are a property of the entire set (see <see cref="MirrorPathPlanner"/>). It is metadata — a cabinet with
    /// fifty thousand files is a few megabytes here — and deciding a path from one page alone would give the same
    /// file different names on two machines that happened to page differently.</para>
    /// </summary>
    private async Task<List<MirrorEntry>?> ReadManifestAsync(
        HttpClient http, string token, CancellationToken cancellationToken)
    {
        var entries = new List<MirrorEntry>();

        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_server.BaseUrl}/api/backup/file-manifest?page={page}&pageSize={ManifestPageSize}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The code is carried out rather than swallowed: « la liste n'a pas pu être lue » with no reason
                // is what made a rate limit look like an empty cabinet, and left nothing on screen to act on.
                ManifestFailureCode = (int)response.StatusCode;
                return null;
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                entries.Add(new MirrorEntry(
                    item.GetProperty("fileId").GetGuid(),
                    item.GetProperty("patientId").GetGuid(),
                    item.GetProperty("patientName").GetString() ?? "",
                    item.GetProperty("fileName").GetString() ?? "",
                    item.GetProperty("fileSize").GetInt64(),
                    item.GetProperty("uploadedAt").GetDateTime()));
            }

            if (!root.TryGetProperty("hasNextPage", out var hasNext) || !hasNext.GetBoolean())
            {
                return entries;
            }
        }
    }

    private async Task<ArchiveCopyOutcome> PullAsync(
        HttpClient http,
        string token,
        string root,
        IReadOnlyList<MirrorPlanItem> plan,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var fetched = 0;
        var skipped = 0;
        var missing = 0;
        var examined = 0;

        // ⚠️ **Time-based, not every-Nth-file.** The report used to fire on `fetched % 10 == 0`, so a run that
        // copied ONE new file never reported at all and the window sat on « Lecture de la liste des fichiers… »
        // from start to finish — working perfectly, and indistinguishable from frozen. A clock does not care how
        // many files there turned out to be, which is the property that was missing.
        var lastReport = System.Diagnostics.Stopwatch.StartNew();
        var reportEvery = TimeSpan.FromMilliseconds(400);

        void ReportProgress(string message, bool force = false)
        {
            if (!force && lastReport.Elapsed < reportEvery)
            {
                return;
            }

            lastReport.Restart();
            progress?.Report(message);
        }

        foreach (var item in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            examined++;

            var destination = Path.Combine(root, item.RelativePath);

            // AC-4. The size is the whole check, deliberately: a hash would mean downloading the file to decide
            // whether to download it, and these rows are immutable — the product has no « replace this file »,
            // only upload and delete.
            var existing = new FileInfo(destination);
            if (existing.Exists && existing.Length == item.Entry.FileSize)
            {
                skipped++;

                // The scan itself is progress. Without this, a cabinet whose files are all already present shows
                // nothing at all between « Lecture… » and the final line.
                ReportProgress($"Vérification… {examined} / {plan.Count}");
                continue;
            }

            var refusal = CheckFreeSpace(root, item.Entry.FileSize);
            if (refusal != null)
            {
                // AC-8: stop, keep everything already written, say the figure. Retried at the next tick.
                return new ArchiveCopyOutcome(
                    false, $"{refusal} {fetched} fichier(s) copié(s) avant l'arrêt.");
            }

            var outcome = await FetchAsync(http, token, item, destination, cancellationToken);

            if (outcome == FetchOutcome.Unauthorized)
            {
                // AC-7. Thirty minutes is not enough for a first mirror, so a 401 mid-run is expected rather
                // than exceptional: ask the grant for another token and retry this same file once.
                var renewed = await ArchiveGrant.ExchangeAsync(
                    http, _server, _settings.GrantSecret, cancellationToken);

                if (!renewed.Succeeded)
                {
                    return new ArchiveCopyOutcome(
                        false,
                        renewed.Throttled ? ArchiveGrant.ThrottledMessage : ArchiveGrant.RefusedMessage);
                }

                token = renewed.Token!;
                outcome = await FetchAsync(http, token, item, destination, cancellationToken);
            }

            switch (outcome)
            {
                case FetchOutcome.Written:
                    fetched++;
                    ReportProgress(
                        $"Téléchargement… {fetched} fichier(s) copié(s) ({examined} / {plan.Count} vérifiés)");
                    break;

                // One blob that cannot be fetched — a pre-US-5 flat key, an object lost in the store — may not
                // stop a mirror of forty thousand. It is counted and named at the end.
                case FetchOutcome.Missing:
                    missing++;
                    break;

                default:
                    return new ArchiveCopyOutcome(
                        false,
                        $"La copie des fichiers s'est arrêtée après {fetched} fichier(s) : le serveur a refusé « "
                        + $"{item.Entry.FileName} ».");
            }
        }

        var message = $"Fichiers à jour : {fetched} copié(s), {skipped} déjà présent(s).";
        if (missing > 0)
        {
            message += $" {missing} introuvable(s) sur le serveur.";
        }

        return new ArchiveCopyOutcome(true, message, root);
    }

    private enum FetchOutcome
    {
        Written,
        Missing,
        Unauthorized,
        Refused,
    }

    /// <summary>
    /// One file, through a <c>.part</c> renamed only on a complete stream (AC-5).
    ///
    /// <para>⚠️ The rename is what makes an interrupted run safe to resume: a truncated file never occupies a real
    /// path, so the size check above cannot mistake half a radiograph for the whole one.</para>
    /// </summary>
    private async Task<FetchOutcome> FetchAsync(
        HttpClient http,
        string token,
        MirrorPlanItem item,
        string destination,
        CancellationToken cancellationToken)
    {
        var partPath = destination + PartSuffix;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_server.BaseUrl}/api/patients/{item.Entry.PatientId}/files/{item.Entry.FileId}/download");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return FetchOutcome.Unauthorized;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return FetchOutcome.Missing;
            }

            if (!response.IsSuccessStatusCode)
            {
                return FetchOutcome.Refused;
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partPath))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            File.Move(partPath, destination, overwrite: true);
            return FetchOutcome.Written;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(partPath);
            return FetchOutcome.Refused;
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    /// <summary>Refuses before writing rather than filling the volume the cabinet also works from (AC-8).</summary>
    private static string? CheckFreeSpace(string root, long needed)
    {
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
            return free >= needed + (64L * 1024 * 1024)
                ? null
                : $"Espace insuffisant : il reste {free / (1024 * 1024)} Mo sur ce disque.";
        }
        catch
        {
            // A volume that cannot report its free space is not a volume we refuse to write to; the write itself
            // will say so if it fails.
            return null;
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
            // Nothing worth failing a run over.
        }
    }
}
