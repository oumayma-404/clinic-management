using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.Maintenance;

/// <summary>
/// Moves the audit ledger's <b>existing</b> rows onto scheme v2 — the canonical form that covers
/// <c>ClinicId</c> and <c>UserEmail</c>.
///
///   ClinicManagement.API.exe rehash-audit-chain [--apply]
///
/// <para><b>Why this is needed.</b> <c>ClinicId</c> was not in the hashed set while every read of the journal
/// filters on it, so <c>UPDATE "AuditEntries" SET "ClinicId"=NULL</c> removed a row from a cabinet's journal
/// permanently and the chain still verified as intact. New rows are written under v2 and are covered; every row
/// written before that change is still on v1 and is still exposed. This is what closes them.</para>
///
/// <para><b>Why a console verb and not a data migration.</b> Three reasons, and the first is decisive: it needs
/// the <b>chain key</b>, which lives in configuration and deliberately not in the database — a migration has no
/// access to it, and giving it access would put the key on the restore path the key exists to stay independent
/// of. It is also re-runnable, and it is an operator's decision about a deployment's own history rather than a
/// schema change every deployment must take.</para>
///
/// <para>⚠️ <b>Dry run by default; <c>--apply</c> writes.</b> It rewrites the tamper-evidence of every historical
/// row — the one table the product treats as evidence — so it prints what it would do and changes nothing until
/// asked twice.</para>
///
/// <para>⚠️ <b>Every row is VERIFIED under v1 before it is rewritten under v2, and a chain that does not verify
/// is left completely alone.</b> Rehashing a tampered row would launder the tampering into a valid v2 hash and
/// destroy the only evidence that it happened. So a broken chain is reported and skipped, never repaired.</para>
///
/// Exit codes match <c>verify-schema</c> and <c>reconcile-money</c>, and the distinction matters to a script:
///   0 = ran; every chain is either already v2 or was rehashed cleanly
///   1 = could not run (bad config, unreachable database, no chain key)
///   2 = ran and found at least one chain it refused to touch
/// </summary>
public static class RehashAuditChainCommand
{
    public const string CommandName = "rehash-audit-chain";

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

            // The ledger spans every cabinet, and `AuditEntries` is one of the deliberately unfiltered tables —
            // but the scope is single-assignment and other reads here are filtered, so it is declared anyway.
            scope.ServiceProvider.GetRequiredService<ITenantScope>()
                .UseSystemWide($"{CommandName} rehashes the audit ledger of every clinic");

            var chainKey = scope.ServiceProvider.GetService<IAuditChainKeyProvider>();
            if (chainKey is null)
            {
                Console.Error.WriteLine(
                    "Aucune clé de chaînage n'est configurée (Audit:ChainKey), donc aucune empreinte ne peut "
                    + "être recalculée. Rien n'a été modifié.");
                return 1;
            }

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Console.WriteLine();
            Console.WriteLine("=== Ré-empreinte du journal d'activité ===");
            Console.WriteLine(apply
                ? "Mode      : APPLICATION — les lignes seront réécrites."
                : "Mode      : simulation — rien ne sera modifié. Ajoutez --apply pour écrire.");
            Console.WriteLine();

            var refused = 0;
            var rehashed = 0;
            var alreadyV2 = 0;

            await using var transaction = apply
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

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

                var walk = AuditChain.Walk(chain, rows.Select(r => r.ToChainEntry()), chainKey.Key);

                // ⚠️ A chain that does not verify is left ALONE. Rehashing it would mint valid v2 hashes over
                // whatever it currently says — laundering a tampered row into an intact-looking one and
                // destroying the only evidence there was.
                if (!walk.IsIntact)
                {
                    refused++;
                    Console.WriteLine(
                        $"  [REFUSÉ] {chain} : {AuditChain.Describe(walk.Break)} "
                        + $"(séquence {walk.FirstBrokenSequence}). Cette chaîne n'est PAS réécrite — "
                        + "la rupture doit être expliquée avant, pas effacée.");
                    continue;
                }

                var legacy = rows
                    .Where(r => r.EntryHash is not null
                                && !r.EntryHash.StartsWith(AuditChain.SchemeV2Prefix, StringComparison.Ordinal))
                    .ToList();

                if (legacy.Count == 0)
                {
                    alreadyV2++;
                    continue;
                }

                Console.WriteLine($"  [{(apply ? "RÉÉCRIT" : "à faire")}] {chain} : {legacy.Count} ligne(s) en v1.");
                rehashed += legacy.Count;

                if (!apply)
                {
                    continue;
                }

                // ⚠️ Written with parameterised SQL rather than through the entity, and that is deliberate.
                // `AuditEntry` has ONE mutator (`Chain`) and it throws on an already-chained row, because the
                // whole design of this table is that nothing in the product can correct it — « a ledger somebody
                // can correct is not evidence ». Adding a public `Rehash` to the entity to serve one console verb
                // would put that door in the domain for every future caller. Two columns, from one verb, under an
                // explicit transaction, is the narrower cost.
                //
                // Rewritten in sequence order, each row's PreviousHash taking the value the row before it has
                // just been given — the chain has to stay linked while its scheme changes underneath it.
                string? previous = null;
                foreach (var row in rows)
                {
                    if (row.EntryHash is null)
                    {
                        continue;
                    }

                    var entry = row.ToChainEntry() with { PreviousHash = previous };
                    var hash = AuditChain.Hash(previous, entry, chainKey.Key);

                    await db.Database.ExecuteSqlRawAsync(
                        """UPDATE "AuditEntries" SET "PreviousHash" = {0}, "EntryHash" = {1} WHERE "Id" = {2}""",
                        new object?[] { previous, hash, row.Id },
                        cancellationToken);

                    previous = hash;
                }
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            Console.WriteLine();
            Console.WriteLine($"Chaînes déjà en v2 : {alreadyV2}");
            Console.WriteLine($"Lignes {(apply ? "réécrites" : "à réécrire")} : {rehashed}");
            Console.WriteLine($"Chaînes refusées   : {refused}");
            Console.WriteLine();

            if (!apply && rehashed > 0)
            {
                Console.WriteLine("Relancez avec --apply pour écrire. Prenez une sauvegarde d'abord.");
                Console.WriteLine();
            }

            return refused > 0 ? RefusedChainExitCode : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"La ré-empreinte a échoué : {ex.Message}");
            return 1;
        }
    }
}
