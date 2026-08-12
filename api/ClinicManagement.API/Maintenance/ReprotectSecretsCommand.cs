using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// <c>reprotect-secrets [--rotate]</c> — re-encrypts every stored secret under the key ring's current
/// generation (<c>hosted-security-hardening</c> FR-3.1).
///
/// <para><b>Why it has to exist.</b> Configuring <c>ProtectKeysWithCertificate</c> protects keys the ring
/// <i>writes from then on</i>; it re-wraps nothing already on the volume. So without this verb the key that
/// encrypts every clinic's reminder credentials and every second factor stays in cleartext on that volume for
/// the rest of its life — FR-3.1 would read satisfied while a stolen disk still yields a readable master key.
/// Only once every ciphertext has moved to a protected key can the superseded plaintext key files be deleted,
/// and <c>verify-schema</c>'s <c>secrets-protected-under-current-ring</c> is the figure that says so.</para>
///
/// <para>⚠️ <b>The order is the whole safety argument, and it runs one way only.</b> Deleting a plaintext key
/// before its ciphertext has been re-protected is R-2's data loss arrived at from the other direction: every
/// second factor Part A enrolled, and every clinic's reminder credentials, become unreadable at once. This verb
/// never deletes a key and never re-mints the ring — it adds a generation and moves ciphertext onto it, and the
/// old keys stay as decryptors until an operator removes them by hand, after the check reads zero.</para>
///
/// <para>⚠️ <b>A row it cannot decrypt is NAMED and left alone</b>, never skipped in silence and never
/// overwritten. An undecryptable row means its key is already gone, so re-protecting is impossible and the
/// recovery is per family (re-issue the factor, re-enter the credential, re-connect the calendar). Such rows
/// make the run exit <b>2</b> — « ran, work remaining » — so a script cannot read « rien à faire » as « done ».</para>
///
/// <para><b>Idempotent.</b> Without <c>--rotate</c> a second run finds every row already under the current
/// generation and changes nothing. <c>--rotate</c> mints a fresh active key first, which is the deliberate
/// non-idempotent half — the step that makes « every subsequent write is protected » true.</para>
///
/// <para>Gated on a configured connection string, never on a capability (amendment M3): it runs no PostgreSQL
/// binary, and the deployment it exists for above all is the hosted one, which has no local DB tooling.</para>
/// </summary>
internal static class ReprotectSecretsCommand
{
    public const string CommandName = "reprotect-secrets";

    private const int Clean = 0;
    private const int CouldNotRun = 1;
    private const int WorkRemaining = 2;

    /// <summary>
    /// One family of ciphertext: what it is called, how to read a row's current value, and how to put a
    /// re-encrypted one back. A family per <i>column</i> rather than per table, because
    /// <c>ClinicReminderSettings</c> holds three independent secrets on one row and « 2 of 3 moved » has to be
    /// reportable.
    /// </summary>
    private sealed record Family(string Name, IReadOnlyList<Row> Rows);

    private sealed record Row(string Identity, string? Ciphertext, Action<string> Replace);

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var rotate = args.Any(a => string.Equals(a, "--rotate", StringComparison.OrdinalIgnoreCase));

        var configuration = InstallConfiguration.BuildForConsoleVerb();
        if (!MaintenanceDatabase.HasConnectionString(configuration, "re-protect the stored secrets"))
        {
            return CouldNotRun;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAuditActorProvider>().RunAs(CommandName);

        // Every family spans every clinic: this is a deployment-wide maintenance pass, and under an unset scope
        // the filtered tables would come back empty — indistinguishable from « nothing left to do », which is
        // the one wrong answer this verb can give.
        scope.ServiceProvider.GetRequiredService<ITenantScope>()
            .UseSystemWide($"{CommandName} re-encrypts stored secrets across every clinic");

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dataProtection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        if (rotate)
        {
            var minted = TryRotate(scope.ServiceProvider);
            if (minted is not null)
            {
                Console.Error.WriteLine(minted);
                return CouldNotRun;
            }

            Console.WriteLine("Une nouvelle clé active a été créée. Les écritures suivantes sont protégées par elle.");

            // FR-3.9's refresh rule: the marker is rewritten at startup AND here, the only place in the product
            // that rotates the ring on purpose — otherwise every dump taken before the next restart would carry
            // a generation the marker does not name, and its restore would be refused.
            var markerPath = KeyRingGenerationMarker.TryWrite(
                scope.ServiceProvider, configuration, out var markerProblem);
            Console.WriteLine(markerPath is not null
                ? $"Marqueur de génération réécrit : {markerPath}"
                : $"⚠️ Marqueur de génération non réécrit ({markerProblem ?? "aucun chemin configuré"}) — "
                  + "redémarrez l'API avant la prochaine sauvegarde.");
        }

        DataProtectionKeyGeneration.Generation generation;
        try
        {
            generation = DataProtectionKeyGeneration.Current(
                dataProtection.CreateProtector("ClinicManagement.KeyRingGeneration.Probe.v1"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Impossible de lire la génération active du trousseau : {ex.Message}");
            return CouldNotRun;
        }

        Console.WriteLine($"Génération active du trousseau : {generation.Id}");
        Console.WriteLine();

        var families = await BuildFamiliesAsync(context, scope.ServiceProvider, cancellationToken);

        var moved = 0;
        var alreadyCurrent = 0;
        var unreadable = new List<string>();

        foreach (var family in families)
        {
            var rows = family.Rows;
            var familyMoved = 0;
            var familyCurrent = 0;
            var familyUnreadable = 0;

            foreach (var row in rows)
            {
                if (generation.Covers(row.Ciphertext))
                {
                    familyCurrent++;
                    continue;
                }

                try
                {
                    row.Replace(row.Ciphertext!);
                    familyMoved++;
                }
                catch (Exception)
                {
                    // Named, not skipped: the row keeps its unreadable ciphertext and an operator is told which
                    // one it is, because the recovery differs per family and nobody can act on a count.
                    familyUnreadable++;
                    unreadable.Add($"{family.Name} — {row.Identity}");
                }
            }

            moved += familyMoved;
            alreadyCurrent += familyCurrent;

            Console.WriteLine(
                $"  {family.Name,-46} {rows.Count,5} ligne(s) · {familyMoved,4} rechiffrée(s) · "
                + $"{familyCurrent,4} déjà à jour · {familyUnreadable,4} illisible(s)");
        }

        if (moved > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        Console.WriteLine();
        Console.WriteLine($"Total : {moved} rechiffrée(s), {alreadyCurrent} déjà à jour, {unreadable.Count} illisible(s).");

        if (unreadable.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "⚠️ Les lignes suivantes n'ont pas pu être déchiffrées et ont été laissées telles quelles. "
                + "Leur clé d'origine n'est plus dans le trousseau : ne supprimez aucun fichier de clé tant "
                + "qu'elles sont listées ici (voir deploy/KEY-CUSTODY.md).");
            foreach (var row in unreadable)
            {
                Console.Error.WriteLine($"  - {row}");
            }

            return WorkRemaining;
        }

        Console.WriteLine(
            "Tous les secrets sont chiffrés par la génération active. Vérifiez avec « verify-schema » "
            + "(secrets-protected-under-current-ring) avant de retirer les anciens fichiers de clé.");
        return Clean;
    }

    /// <summary>
    /// Mints a fresh active key, which is what makes every subsequent write certificate-protected. Returns a
    /// French message on failure and <c>null</c> on success.
    /// </summary>
    private static string? TryRotate(IServiceProvider services)
    {
        var keyManager = services.GetService<IKeyManager>();
        if (keyManager is null)
        {
            return "Le gestionnaire de clés de protection des données est indisponible : « --rotate » ne peut "
                + "pas créer de nouvelle clé active.";
        }

        try
        {
            // Active immediately, so the very next Protect() in this process already uses it. The default
            // lifetime is the ring's own; nothing here shortens or extends it.
            var now = DateTimeOffset.UtcNow;
            keyManager.CreateNewKey(now, now.AddDays(90));
            return null;
        }
        catch (Exception ex)
        {
            return $"Impossible de créer une nouvelle clé active : {ex.Message}";
        }
    }

    /// <summary>
    /// The six families, in one list. Loading is eager per family so the console line can report a total
    /// alongside what moved — « 12 rechiffrées » says nothing without « of how many ».
    /// </summary>
    private static async Task<IReadOnlyList<Family>> BuildFamiliesAsync(
        ApplicationDbContext context, IServiceProvider services, CancellationToken cancellationToken)
    {
        var reminders = services.GetRequiredService<IReminderSecretProtector>();
        var userSecrets = services.GetRequiredService<IUserSecretProtector>();
        var platformSecrets = services.GetRequiredService<IPlatformSecretProtector>();
        var googleTokens = services.GetRequiredService<IGoogleTokenProtector>();

        var reminderSettings = await context.ClinicReminderSettings
            .Where(s => s.SmsApiKeyEncrypted != null
                        || s.WhatsAppAccessTokenEncrypted != null
                        || s.SmtpPasswordEncrypted != null)
            .ToListAsync(cancellationToken);

        var users = await context.Users
            .Where(u => u.ProtectedTotpSecret != null)
            .ToListAsync(cancellationToken);

        var accounts = await context.PlatformAccounts
            .Where(a => a.ProtectedTotpSecret != null)
            .ToListAsync(cancellationToken);

        var clinics = await context.Clinics
            .Where(c => c.GoogleRefreshTokenProtected != null)
            .ToListAsync(cancellationToken);

        return new List<Family>
        {
            Immediate("ClinicReminderSettings.SmsApiKeyEncrypted", reminderSettings
                .Where(s => s.SmsApiKeyEncrypted is not null)
                .Select(s => new Row(
                    $"cabinet {s.Id}",
                    s.SmsApiKeyEncrypted,
                    ciphertext => s.SetSmsApiKeyEncrypted(reminders.Protect(reminders.Unprotect(ciphertext)))))),

            Immediate("ClinicReminderSettings.WhatsAppAccessTokenEncrypted", reminderSettings
                .Where(s => s.WhatsAppAccessTokenEncrypted is not null)
                .Select(s => new Row(
                    $"cabinet {s.Id}",
                    s.WhatsAppAccessTokenEncrypted,
                    ciphertext => s.SetWhatsAppAccessTokenEncrypted(
                        reminders.Protect(reminders.Unprotect(ciphertext)))))),

            Immediate("ClinicReminderSettings.SmtpPasswordEncrypted", reminderSettings
                .Where(s => s.SmtpPasswordEncrypted is not null)
                .Select(s => new Row(
                    $"cabinet {s.Id}",
                    s.SmtpPasswordEncrypted,
                    ciphertext => s.SetSmtpPasswordEncrypted(reminders.Protect(reminders.Unprotect(ciphertext)))))),

            Immediate("User.ProtectedTotpSecret", users
                .Select(u => new Row(
                    $"compte {u.Email}",
                    u.ProtectedTotpSecret,
                    ciphertext => u.ReplaceProtectedTotpSecret(Reprotect(userSecrets, ciphertext))))),

            Immediate("PlatformAccount.ProtectedTotpSecret", accounts
                .Select(a => new Row(
                    $"compte console {a.Email}",
                    a.ProtectedTotpSecret,
                    ciphertext => a.ReplaceProtectedTotpSecret(Reprotect(platformSecrets, ciphertext))))),

            Immediate("Clinic.GoogleRefreshTokenProtected", clinics
                .Select(c => new Row(
                    $"cabinet {c.Name}",
                    c.GoogleRefreshTokenProtected,
                    ciphertext => c.SetProtectedGoogleRefreshToken(Reprotect(googleTokens, ciphertext))))),
        };
    }

    /// <summary>
    /// Decrypt-then-encrypt over the <c>bool</c>-returning seams. It <b>throws</b> where they return false,
    /// which is what routes the row into the « illisible » list instead of writing an empty secret over a real
    /// one — the single worst thing this verb could do.
    /// </summary>
    private static string Reprotect(IUserSecretProtector protector, string ciphertext) =>
        protector.TryUnprotect(ciphertext, out var plaintext)
            ? protector.Protect(plaintext)
            : throw new InvalidOperationException("ciphertext could not be decrypted");

    private static string Reprotect(IPlatformSecretProtector protector, string ciphertext) =>
        protector.TryUnprotect(ciphertext, out var plaintext)
            ? protector.Protect(plaintext)
            : throw new InvalidOperationException("ciphertext could not be decrypted");

    private static string Reprotect(IGoogleTokenProtector protector, string ciphertext) =>
        protector.TryUnprotect(ciphertext, out var plaintext)
            ? protector.Protect(plaintext)
            : throw new InvalidOperationException("ciphertext could not be decrypted");

    private static Family Immediate(string name, IEnumerable<Row> rows) => new(name, rows.ToList());
}
