using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Records how long each audit chain is <b>right now</b>, so that a later deletion of its newest entries becomes
/// visible.
///
///   ClinicManagement.API.exe seal-audit-chain [--apply]
///
/// <para><b>Why this is needed.</b> Every check in <see cref="AuditChain.Walk"/> compares an entry against its
/// neighbour, so removing the newest <i>k</i> rows removes no neighbour: the shortened chain verifies perfectly,
/// and the next append re-links from whatever tip it finds. « Delete the last hour » was the cheapest attack on
/// this ledger and it left no trace at all. The tip has to be recorded somewhere the database cannot reach,
/// which is what this writes — beside the chain key, for the chain key's own reason.</para>
///
/// <para>⚠️ <b>A chain that does not currently verify is NOT sealed.</b> Sealing a broken chain would record its
/// tampered state as the reference and make every later check agree with the tampering — the same argument
/// <c>rehash-audit-chain</c> makes for refusing to rehash one. A refusal here is reported and the chain's
/// previous seal, if any, is kept.</para>
///
/// <para>⚠️ <b>And it is an operator action, never automatic.</b> A seal the application rewrote on every append
/// would be rewritten by the process an attacker already controls, one moment after the truncation. A seal means
/// « a person confirmed this chain was intact at this length », which is only true if a person did it.</para>
///
/// <para>⚠️ <b>Back the seal file up.</b> Losing it does not corrupt anything, but it silently restores the
/// blindness this closes — <c>verify-schema</c> then has nothing to compare against and reports the chain intact.
/// The file's path is printed on every run for exactly that reason.</para>
///
/// Exit codes match <c>verify-schema</c> and <c>rehash-audit-chain</c>:
///   0 = ran; every chain sealed (or already at its sealed tip)
///   1 = could not run (bad config, unreachable database, no chain key)
///   2 = ran and found at least one chain it refused to seal
/// </summary>
public static class SealAuditChainCommand
{
    public const string CommandName = "seal-audit-chain";

    /// <summary>Exit code when at least one chain was refused because it does not verify.</summary>
    public const int RefusedChainExitCode = 2;

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var apply = args.Any(a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));

        try
        {
            var configuration = InstallConfiguration.BuildForConsoleVerb();

            if (!MaintenanceDatabase.HasConnectionString(configuration, "This audit-chain utility"))
            {
                return 1;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(configuration);

            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} seals the audit ledger of every clinic");

            var chainKey = scope.ServiceProvider.GetService<IAuditChainKeyProvider>();
            if (chainKey is null)
            {
                Console.Error.WriteLine(
                    "Aucune clé de chaînage n'est configurée (Audit:ChainKey), donc aucune chaîne ne peut être "
                    + "vérifiée — et sceller sans vérifier n'a aucune valeur. Rien n'a été modifié.");
                return 1;
            }

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IAuditChainSealStore>();
            var existing = store.Read();

            Console.WriteLine();
            Console.WriteLine("=== Scellement du journal d'activité ===");
            Console.WriteLine(apply
                ? "Mode      : APPLICATION — le fichier de scellement sera réécrit."
                : "Mode      : simulation — rien ne sera écrit. Ajoutez --apply pour sceller.");
            Console.WriteLine($"Fichier   : {store.Location}");
            Console.WriteLine();

            var refused = 0;
            var sealed_ = new List<AuditChainSeal>();

            var chains = await db.AuditEntries
                .Select(e => e.ChainKey)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var chain in chains)
            {
                var rows = await db.AuditEntries
                    .Where(e => e.ChainKey == chain)
                    .OrderBy(e => e.Sequence)
                    .ToListAsync(cancellationToken);

                existing.TryGetValue(chain, out var previousSeal);

                // ⚠️ Walked WITH the existing seal, so a chain already truncated since the last sealing is
                // refused rather than quietly re-sealed at its new, shorter length. Re-sealing would erase the
                // only evidence — the exact failure this verb exists to make impossible.
                var walk = AuditChain.Walk(chain, rows.Select(r => r.ToChainEntry()), chainKey.Key, previousSeal);

                if (!walk.IsIntact)
                {
                    refused++;
                    Console.WriteLine(
                        $"  [REFUSÉ] {chain} : {AuditChain.Describe(walk.Break)} "
                        + $"(séquence {walk.FirstBrokenSequence}). Cette chaîne n'est PAS scellée — "
                        + "la rupture doit être expliquée avant, pas enregistrée comme référence.");

                    if (previousSeal is not null)
                    {
                        sealed_.Add(previousSeal);
                    }

                    continue;
                }

                var tip = rows.LastOrDefault(r => r.EntryHash is not null);
                if (tip is null)
                {
                    // A chain made entirely of pre-chain rows has no tip to record, and saying so is the honest
                    // answer — « scellée » over rows no key can cover would be a claim this verb cannot support.
                    Console.WriteLine($"  [ignorée] {chain} : aucune entrée chaînée (antérieure au chaînage).");
                    continue;
                }

                var seal = new AuditChainSeal(chain, tip.Sequence, tip.EntryHash!, DateTime.UtcNow);
                sealed_.Add(seal);

                var moved = previousSeal is null
                    ? "premier scellement"
                    : $"+{tip.Sequence - previousSeal.Sequence} entrée(s) depuis le dernier";
                Console.WriteLine($"  [{(apply ? "SCELLÉ" : "à sceller")}] {chain} : séquence {tip.Sequence} ({moved}).");
            }

            if (apply)
            {
                store.Write(sealed_);
            }

            Console.WriteLine();
            Console.WriteLine($"Chaînes {(apply ? "scellées" : "à sceller")} : {sealed_.Count}");
            Console.WriteLine($"Chaînes refusées  : {refused}");
            Console.WriteLine();
            Console.WriteLine(
                "⚠️ Sauvegardez ce fichier hors de la base. Le perdre ne corrompt rien, mais rétablit "
                + "silencieusement l'angle mort : sans référence, une troncature redevient invisible.");
            Console.WriteLine();

            if (!apply && sealed_.Count > 0)
            {
                Console.WriteLine("Relancez avec --apply pour écrire.");
                Console.WriteLine();
            }

            return refused > 0 ? RefusedChainExitCode : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Le scellement a échoué : {ex.Message}");
            return 1;
        }
    }
}
