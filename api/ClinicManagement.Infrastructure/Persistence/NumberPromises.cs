using System.Data.Common;
using System.Diagnostics;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>One gapless legal number a save is about to make final.</summary>
public sealed record NumberedDocument(Guid ClinicId, string Sequence, string Number);

/// <summary>
/// <c>clinic-pc-copy</c> D16: the documents whose <c>Number</c> is a gapless legal sequence, and what a save numbers.
/// Read off the save itself — every numbering path (a note issued, a fiche billed, a devis accepted, an avoir raised,
/// and any added later) is covered without being touched. <c>NumberedDocumentCoverageTests</c> fails on a numbered
/// entity missing here.
/// </summary>
public static class NumberedDocuments
{
    public static readonly IReadOnlyDictionary<Type, string> Sequences = new Dictionary<Type, string>
    {
        [typeof(Invoice)] = RelayNumberPromise.InvoiceSequence,
        [typeof(TreatmentPlan)] = RelayNumberPromise.DevisSequence,
        [typeof(CreditNote)] = RelayNumberPromise.CreditNoteSequence,
    };

    /// <summary>The numbers this save assigns: an added document with a number, or one whose number changed.</summary>
    public static List<NumberedDocument> Collect(DbContext context)
    {
        var numbered = new List<NumberedDocument>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (!Sequences.TryGetValue(entry.Metadata.ClrType, out var sequence) || IsNotAssigning(entry, out var number))
            {
                continue;
            }

            numbered.Add(new NumberedDocument((Guid)entry.Property("ClinicId").CurrentValue!, sequence, number!));
        }

        return numbered;
    }

    private static bool IsNotAssigning(EntityEntry entry, out string? number)
    {
        var property = entry.Property("Number");
        number = property.CurrentValue as string;
        if (string.IsNullOrWhiteSpace(number))
        {
            return true;
        }

        return entry.State switch
        {
            EntityState.Added => false,
            EntityState.Modified => !property.IsModified || Equals(property.OriginalValue, number),
            _ => true,
        };
    }
}

/// <summary>A number to promise to one PC.</summary>
public sealed record PlannedPromise(Guid RelayId, RelayNumberPromiseDto Promise);

/// <summary>
/// D16 on the cloud: before a save's transaction commits, every number it assigns for a cabinet whose PC could take over
/// is handed to that PC, which must keep it within <see cref="Wait"/> — else the save is undone and refused with
/// <c>relay_unconfirmed</c> and FR-3's sentence; the form stays open. Elsewhere (a PC, a LAN server) it never waits.
/// </summary>
public sealed class NumberPromiseCoordinator
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private readonly bool _promises;
    private readonly IRelayPromiseBroker _broker;
    private readonly IIdempotencyKeyAccessor? _keys;
    private readonly ILogger<NumberPromiseCoordinator> _logger;

    public NumberPromiseCoordinator(
        DeploymentProfile profile, IRelayPromiseBroker broker, IIdempotencyKeyAccessor? keys,
        ILogger<NumberPromiseCoordinator> logger)
    {
        _promises = profile.PublishesChangeFeed;
        _broker = broker;
        _keys = keys;
        _logger = logger;
    }

    /// <summary>Only the cloud promises: a PC numbers for itself during a cut, a LAN server has no PC.</summary>
    public bool Promises => _promises;

    /// <summary>Which of a save's numbers need a PC's word — read inside the save's own transaction.</summary>
    public async Task<IReadOnlyList<PlannedPromise>> PlanAsync(
        ApplicationDbContext context, IReadOnlyList<NumberedDocument> numbers, CancellationToken cancellationToken)
    {
        var planned = new List<PlannedPromise>();
        foreach (var clinic in numbers.GroupBy(n => n.ClinicId))
        {
            var relay = await ClinicRelayRepository
                .CurrentFor(context.ClinicRelays.IgnoreQueryFilters().AsNoTracking(), clinic.Key)
                .FirstOrDefaultAsync(cancellationToken);
            planned.AddRange(Plan(relay, clinic, _keys?.Current));
        }

        return planned;
    }

    /// <summary>The pure half of <see cref="PlanAsync"/>: one promise per number, for a PC that could take over with it.</summary>
    public static IEnumerable<PlannedPromise> Plan(ClinicRelay? relay, IEnumerable<NumberedDocument> numbers, string? key) =>
        ClinicWriteLease.PcMustConfirmNumbers(relay)
            ? numbers.Select(n => new PlannedPromise(relay!.Id,
                new RelayNumberPromiseDto(Guid.NewGuid(), n.ClinicId, n.Sequence, n.Number, key)))
            : Enumerable.Empty<PlannedPromise>();

    /// <summary>Waits for every PC concerned to keep its numbers; throws <see cref="ClinicFencedException"/> otherwise.</summary>
    public async Task ConfirmAsync(IReadOnlyList<PlannedPromise> planned, CancellationToken cancellationToken)
    {
        foreach (var relay in planned.GroupBy(p => p.RelayId))
        {
            var promises = relay.Select(p => p.Promise).ToList();
            var watch = Stopwatch.StartNew();
            var kept = await _broker.PromiseAsync(relay.Key, promises, Wait, cancellationToken);
            _logger.LogInformation(
                "D16: {Numbers} {Outcome} by PC de secours {RelayId} in {Ms} ms",
                string.Join(", ", promises.Select(p => $"{p.Sequence} {p.Number}")), kept ? "kept" : "NOT confirmed",
                relay.Key, watch.ElapsedMilliseconds);
            if (!kept)
            {
                throw new ClinicFencedException(RelayRefusals.Silent, RelayRefusals.UnconfirmedCode);
            }
        }
    }
}

/// <summary>
/// Runs <see cref="NumberPromiseCoordinator.ConfirmAsync"/> at the last moment a save can still be undone: just before
/// its transaction commits — whoever opened it (the save itself, or a handler around several saves).
/// </summary>
public sealed class NumberPromiseTransactionInterceptor : DbTransactionInterceptor
{
    private readonly NumberPromiseCoordinator _coordinator;

    public NumberPromiseTransactionInterceptor(NumberPromiseCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        (eventData.Context as ApplicationDbContext)?.TakePlannedPromises();
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ApplicationDbContext context)
        {
            var planned = context.TakePlannedPromises();
            if (planned.Count > 0)
            {
                await _coordinator.ConfirmAsync(planned, cancellationToken);
            }
        }

        return result;
    }

    /// <summary>The synchronous commit is never a numbering path's, but a promise it carried must not slip through unasked.</summary>
    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        if (eventData.Context is ApplicationDbContext context)
        {
            var planned = context.TakePlannedPromises();
            if (planned.Count > 0)
            {
                _coordinator.ConfirmAsync(planned, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        return result;
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        (eventData.Context as ApplicationDbContext)?.TakePlannedPromises();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The PC's half of D16: keeps each promised number (raw SQL — the table is the PC's own, never copied), so it numbers
/// after it during a cut. Kept 30 days: by then the number's own row has long reached the copy.
/// </summary>
public sealed class RelayNumberPromiseStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly ApplicationDbContext _db;

    public RelayNumberPromiseStore(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task KeepAsync(IEnumerable<RelayNumberPromiseDto> promises, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var promise in promises)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO ""RelayNumberPromises"" (""ClinicId"", ""Sequence"", ""Number"", ""IdempotencyKey"", ""PromisedAtUtc"")
                   VALUES ({promise.ClinicId}, {promise.Sequence}, {promise.Number}, {promise.IdempotencyKey}, {nowUtc})
                   ON CONFLICT (""ClinicId"", ""Sequence"", ""Number"")
                   DO UPDATE SET ""IdempotencyKey"" = EXCLUDED.""IdempotencyKey"", ""PromisedAtUtc"" = EXCLUDED.""PromisedAtUtc""",
                cancellationToken);
        }

        var expired = nowUtc - Retention;
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM ""RelayNumberPromises"" WHERE ""PromisedAtUtc"" < {expired}", cancellationToken);
    }

    /// <summary>The highest <c>NNNN</c> promised for this cabinet, sequence and year — 0 when none (always, on the cloud).</summary>
    public static async Task<int> MaxPromisedAsync(
        ApplicationDbContext db, Guid clinicId, string sequence, int year, CancellationToken cancellationToken)
    {
        var prefix = $"{year}-";
        var numbers = await db.RelayNumberPromises
            .Where(p => p.ClinicId == clinicId && p.Sequence == sequence && p.Number.StartsWith(prefix))
            .Select(p => p.Number)
            .ToListAsync(cancellationToken);
        return numbers.Count == 0 ? 0 : numbers.Max(n => RelayNumberPromise.SequenceOf(n, year));
    }
}
