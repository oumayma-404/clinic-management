using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The single door onto the console's access ledger (<c>platform-console</c> FR-5, AC-7.3).
///
/// <para>It exists as a helper rather than as four lines in the detail handler because Parts 4–6 add three more
/// callers — a grant, a cancellation and a suspension — and « who was acting » has to be resolved the same way in
/// all four. A copy per write site is the shape in which the fourth one forgets.</para>
///
/// <para>⚠️ <b>An unresolvable actor throws, and the caller does not swallow it.</b> This is not a post-commit
/// best-effort side effect like <c>INotificationGenerator</c>: AC-7.3 says every detail read <i>is</i> recorded,
/// so a read that could not be attributed must not succeed. Reaching here with no console account in scope is a
/// pipeline fault anyway — the policy pins the console's authentication scheme, so a request that got this far has
/// a console principal — which is exactly the argument <c>PlatformTenantScope.EnsureDeclared</c> makes for
/// throwing rather than repairing.</para>
/// </summary>
public static class PlatformAccessLedger
{
    /// <summary>
    /// Stages one ledger row for the acting console account. The caller commits it — so on the read path the row
    /// and nothing else is saved, and on Parts 4–6's write paths it rides the same transaction as the write it
    /// records.
    /// </summary>
    /// <param name="subscriptionPeriodId">
    /// The ledger entry a write produced or acted on, for the rows that have one (Parts 4–6). Null for a read.
    /// </param>
    /// <param name="idempotencyKey">
    /// The client's key for a keyed write, unique across the ledger — which is what makes AC-4.6's « one entry per
    /// submission » a property of the database. Null for a read and for an unkeyed write.
    /// </param>
    public static async Task RecordAsync(
        IPlatformAccessEntryRepository repository,
        IPlatformSessionContext session,
        Guid clinicId,
        string clinicName,
        PlatformAccessAction action,
        DateTime occurredAt,
        CancellationToken cancellationToken = default,
        Guid? subscriptionPeriodId = null,
        string? idempotencyKey = null)
    {
        // The address is what makes a row readable years later, and it is the account's own at the time — a
        // renamed or deleted account must not silently blank the rows it left behind.
        var entry = new PlatformAccessEntry(
            RequireAccountId(session),
            session.GetEmail() ?? string.Empty,
            clinicId,
            clinicName,
            action,
            occurredAt,
            subscriptionPeriodId,
            idempotencyKey);

        await repository.AddAsync(entry, cancellationToken);
    }

    /// <summary>
    /// The acting console account, or a throw. Public so a <b>write</b> can resolve it before it builds anything:
    /// a grant stamps the same account onto the append-only <c>SubscriptionPeriod</c> it creates, and « nous ne
    /// savons pas qui » has to stop that write rather than be discovered while recording it.
    /// </summary>
    public static Guid RequireAccountId(IPlatformSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.GetAccountId()
            ?? throw new InvalidOperationException(
                "Une action de la console éditeur s'exécute sans compte console identifiable : elle ne peut pas "
                + "être inscrite au journal des accès, et une action non attribuable ne doit pas aboutir. "
                + "Vérifiez que la stratégie PlatformConsole épingle bien le schéma d'authentification de la console.");
    }
}
