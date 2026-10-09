using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Tells a person, every morning, when the host's off-site copies are not healthy (<c>server-loss-recovery</c>
/// Part 3).
///
/// <para><b>Why it exists.</b> The nightly backup aborted for 33 nights on the live server and its only record was
/// a <c>docker logs</c> line — no copy of any patient file left the server in that time, and nobody could have
/// known without logging in to look. A failure that waits to be looked for is a failure found by needing the
/// backup. This reads the same verdict as the console's status strip (<see cref="BackupHealthRules"/>) and e-mails
/// it while it is not <c>Ok</c>.</para>
///
/// <para>⚠️ <b>Stateless, and that is deliberate.</b> It sends every morning the copies are unhealthy and says
/// nothing on the mornings they are fine — no « last alerted » row, no migration. A daily repeat while something is
/// broken is the point: the alternative, one e-mail per incident, is how a single missed message becomes another
/// month of silence.</para>
///
/// <para>⚠️ <b>Recipients</b>: <c>Backup:AlertEmail</c> (comma-separated) when the operator named somebody, otherwise
/// <b>every active console account</b>. Falling back rather than staying quiet is the lesson of the bring-up step
/// nobody performed: an alert whose recipient is one more setting to remember is one more way to hear nothing.</para>
/// </summary>
public class BackupHealthJob
{
    /// <summary>Who receives the alert; unset ⇒ every active console account.</summary>
    public const string AlertEmailKey = "Backup:AlertEmail";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly IHostBackupStatusReader _reader;
    private readonly IPlatformAccountRepository _accounts;
    private readonly ITransactionalEmailSender _email;
    private readonly DeploymentProfile _profile;
    private readonly IConfiguration _configuration;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<BackupHealthJob> _logger;

    public BackupHealthJob(
        IHostBackupStatusReader reader,
        IPlatformAccountRepository accounts,
        ITransactionalEmailSender email,
        DeploymentProfile profile,
        IConfiguration configuration,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<BackupHealthJob> logger)
    {
        _reader = reader;
        _accounts = accounts;
        _email = email;
        _profile = profile;
        _configuration = configuration;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 2)]
    public Task CheckBackupHealth() => CheckBackupHealth(DateTime.UtcNow, CancellationToken.None);

    /// <summary>The pass at a given instant — the Hangfire entry point above supplies now; tests supply their own.</summary>
    public async Task CheckBackupHealth(DateTime nowUtc, CancellationToken cancellationToken)
    {
        // Registered only where the capability holds, and asked again: a reprofiled install can still hold the
        // recurring registration in Hangfire storage and fire it before Program.cs drops it.
        if (!_profile.MonitorsSidecarBackups)
        {
            return;
        }

        _auditActor.RunAs(nameof(BackupHealthJob));
        // Reads no cabinet (a status file, pg_stat_archiver, the console's own accounts), so the scope is declared
        // for the guard's sake, not to widen anything.
        _tenantScope.UseSystemWide("BackupHealthJob reads the host's backup status and the console accounts");

        var health = BackupHealthRules.Evaluate(await _reader.ReadAsync(cancellationToken), nowUtc);
        if (health.Verdict == BackupHealthRules.Ok)
        {
            _logger.LogInformation("Off-site backups healthy: nightly {Nightly}, WAL {Wal}",
                health.Nightly.Verdict, health.WalArchive.Verdict);
            return;
        }

        _logger.LogError("Off-site backups NOT healthy ({Verdict}): nightly {Nightly} (stage {Stage}), WAL {Wal}",
            health.Verdict, health.Nightly.Verdict, health.Nightly.FailedStage ?? "-", health.WalArchive.Verdict);

        if (!_email.IsConfigured)
        {
            _logger.LogError("Backup alert NOT e-mailed: no SMTP is configured on this deployment. "
                             + "The console's status strip is the only place this is shown.");
            return;
        }

        var recipients = await RecipientsAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            _logger.LogError("Backup alert NOT e-mailed: {Key} is empty and there is no active console account.",
                AlertEmailKey);
            return;
        }

        var content = Compose(health);
        foreach (var recipient in recipients)
        {
            var outcome = await _email.SendAsync(recipient, Subject(health), content, cancellationToken);
            if (outcome.Outcome != TransactionalEmailOutcome.Sent)
            {
                _logger.LogError("Backup alert to a console recipient failed: {Outcome} {Error}",
                    outcome.Outcome, outcome.Error);
            }
        }
    }

    private async Task<IReadOnlyList<string>> RecipientsAsync(CancellationToken cancellationToken)
    {
        var configured = (_configuration[AlertEmailKey] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (configured.Length > 0)
        {
            return configured;
        }

        return await _accounts.GetActiveEmailsAsync(cancellationToken);
    }

    /// <summary>The subject names the verdict, so an inbox list reads as the alarm without opening it.</summary>
    public static string Subject(PlatformBackupHealthDto health) =>
        health.Verdict == BackupHealthRules.Failing
            ? "Sauvegarde hors serveur EN ÉCHEC"
            : "Sauvegarde hors serveur EN RETARD";

    /// <summary>What the e-mail says — public so the wording is tested against the verdicts it describes.</summary>
    public static EmailContent Compose(PlatformBackupHealthDto health)
    {
        var details = new List<EmailDetail>
        {
            new("Copie nocturne (base, fichiers, clés)", NightlyLine(health.Nightly)),
            new("Copie continue de la base (WAL)", WalLine(health.WalArchive)),
            new("Vérifié le", At(health.CheckedAt)),
        };

        return new EmailContent
        {
            Title = Subject(health),
            Preheader = "Au moins une copie hors serveur des données des cabinets n'est pas à jour.",
            Intro =
            [
                "Au moins une des deux copies hors serveur n'est pas à jour. Si le serveur était perdu aujourd'hui, "
                + "ce qui n'a pas été copié serait perdu avec lui.",
                "Ce message est renvoyé chaque matin tant que la situation n'est pas réglée.",
            ],
            Details = details,
            Outro =
            [
                "Sur le serveur : « docker compose -f docker-compose.hosted.yml logs --tail 50 backup » dit pourquoi la "
                + "copie nocturne a échoué, et « exec backup cat /status/backup.status » à quelle étape.",
                "La console éditeur affiche le même état sur sa page d'accueil.",
            ],
        };
    }

    private static string NightlyLine(PlatformNightlyBackupDto nightly)
    {
        if (nightly.Outcome is null)
        {
            return $"{nightly.VerdictLabel} — aucune exécution enregistrée.";
        }

        var last = nightly.LastSuccessAt is { } success
            ? $"dernière réussite le {At(success)}"
            : "aucune réussite enregistrée";
        return nightly.FailedStage is { } stage
            ? $"{nightly.VerdictLabel} — échec à l'étape « {stage} », {last}."
            : $"{nightly.VerdictLabel} — {last}.";
    }

    private static string WalLine(PlatformWalArchiveDto wal) =>
        wal.LastArchivedAt is { } archived
            ? $"{wal.VerdictLabel} — dernier envoi le {At(archived)}"
              + (wal.LastFailedAt is { } failed && failed > archived ? $", échec depuis le {At(failed)}." : ".")
            : $"{wal.VerdictLabel} — aucun envoi enregistré.";

    private static string At(DateTime utc) =>
        ClinicClock.ToClinicLocal(utc).ToString("dd/MM/yyyy 'à' HH:mm", French);
}
