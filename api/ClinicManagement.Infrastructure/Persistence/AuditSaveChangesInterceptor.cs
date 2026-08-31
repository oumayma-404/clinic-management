using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Writes the audit ledger (I6). One row per mutated <b>aggregate root</b>, carrying the actor, the clinic, the
/// entity, the action and — for updates and deletes — a compact summary of what moved.
///
/// <para><b>Why an interceptor.</b> Attribution wired into command handlers is attribution a new command can
/// forget, and the ledger's whole value is that it sees everything: a mutation missing from it is
/// indistinguishable from a mutation that never happened. Every write in the product funnels through
/// <c>SaveChangesAsync</c>, so this sees them all by construction. Nothing has to be remembered.</para>
///
/// <para><b>Aggregate roots only, deliberately.</b> Saving one invoice touches its lines and its payments; a row
/// per tracked entity would answer « qui a annulé cette facture ? » with eleven rows for one action. The
/// aggregate is the unit of change, so it is the unit of the ledger.</para>
///
/// <para><b>The two-phase shape, and the constraint that forces it.</b> The rows are <b>collected</b> in
/// <see cref="SavingChangesAsync"/> and <b>written</b> in <see cref="SavedChangesAsync"/> through a
/// <i>separate</i> <see cref="ApplicationDbContext"/>:</para>
/// <list type="bullet">
///   <item>Collection must happen before the save resolves, because a <c>Deleted</c> entry is <b>gone from the
///   change tracker</b> afterwards — its id and its identifying values only exist now. Same for the original
///   values an update's summary is diffed against.</item>
///   <item>Writing must happen outside the business save. An audit failure must never roll back a clinical or
///   money operation (the same contract as <c>INotificationGenerator</c>), and rows added to the caller's own
///   context would be part of the caller's transaction — a bad audit row would take the invoice with it. It is a
///   separate context and therefore <b>not a nested save</b>: this interceptor never re-enters the context it is
///   observing, which would recurse.</item>
/// </list>
///
/// <para>⚠️ <b>The known imprecision, stated rather than hidden.</b> <c>SavedChangesAsync</c> fires when
/// <c>SaveChanges</c> returns, which for the handful of handlers that open an explicit
/// <c>IUnitOfWork.BeginTransactionAsync</c> is <em>before</em> the commit. If such a transaction is rolled back,
/// its audit row survives. That is the deliberate direction of the error: over-recording an attempt is a reading
/// problem, while under-recording a real change is the failure the ledger exists to prevent — and the alternative
/// (enlisting in the caller's transaction) is exactly the coupling the contract above forbids.</para>
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Types excluded from the ledger, by name.
    ///
    /// <para><b>Two entries, and both are structural rather than a matter of taste.</b> <see cref="AuditEntry"/>
    /// itself, or the ledger would audit its own writes forever. And <c>Notification</c> — the outbound SMS/WhatsApp
    /// outbox — whose dispatcher rewrites every due row's status on a <b>minutely</b> schedule: auditing it would
    /// bury a clinic's real history under machine noise within a day, and it already has its own visible delivery
    /// log on « Rappels ». Anything else that mutates does so because a person decided something.</para>
    ///
    /// <para>⚠️ This is the one place a name could rot. It is a two-item exclusion list, not an allow-list, so
    /// forgetting to extend it costs noise rather than silence — the failure direction the rest of this class is
    /// built around.</para>
    /// </summary>
    private static readonly HashSet<string> ExcludedEntityTypes = new(StringComparer.Ordinal)
    {
        nameof(AuditEntry),
        nameof(Notification),
        // Written by the ANONYMOUS signup endpoint: no actor, no clinic to resolve, so no reading of GET /api/audit
        // can ever show them — and a purge row would record the abandoned visitor's name and address for ever.
        nameof(ClinicSignup),
        // The vendor's console identity (platform-console). « Journal d'activité » is a CLINIC's history, read by
        // that clinic's admin; a console sign-in, a lockout counter or a recovery code being spent belongs to no
        // cabinet, so every such row would be unattributable noise nobody can see. What the console does *to* a
        // cabinet is still audited — that write touches the cabinet's own aggregates and carries `console|{id}`.
        nameof(PlatformAccount),
        nameof(PlatformRecoveryCode),
        // The console's own access ledger (Part 3). It is already a ledger: auditing it would record « the vendor
        // recorded that it looked at a cabinet » in that cabinet's « Journal d'activité » — a row about the writing
        // of a row — and, because a mere READ produces one, opening a detail would appear in the practice's history
        // as a mutation of its own data, which it is not. What the console does *to* a cabinet still audits itself,
        // through the cabinet's own aggregates and under `console|{accountId}`.
        nameof(PlatformAccessEntry)
    };

    /// <summary>
    /// Properties whose change is <em>the</em> story of an update, and are therefore rendered as
    /// <c>old → new</c> rather than as a bare name. « Status » on its own does not answer « qui a annulé cette
    /// facture ? »; « Status: Issued → Cancelled » does.
    /// </summary>
    private static readonly HashSet<string> ValuedProperties = new(StringComparer.Ordinal)
    {
        "Status", "IsActive", "IsArchived", "IsVoided", "IsDraft", "Role", "IsFlagged"
    };

    /// <summary>
    /// How many changed property names a summary lists before eliding. Four names identify a change; twenty are a
    /// column dump nobody reads, and a fat aggregate saved after an ordinary edit touches many.
    /// </summary>
    private const int MaxListedProperties = 6;

    private readonly IAuditActorProvider _actorProvider;
    private readonly ICurrentClinicProvider _clinicProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    /// <summary>
    /// Rows collected in <c>SavingChangesAsync</c> and drained in <c>SavedChangesAsync</c>.
    ///
    /// <para>Interceptor instances are registered <b>scoped</b> alongside the context that owns them, so this is
    /// per-request state and not shared. It is keyed by the <c>DbContext</c> instance all the same: one scope can
    /// save more than once (a post-commit side effect saving again), and a second save must not re-write the first
    /// save's rows.</para>
    /// </summary>
    private readonly Dictionary<DbContext, List<AuditEntry>> _pending = new();

    public AuditSaveChangesInterceptor(
        IAuditActorProvider actorProvider,
        ICurrentClinicProvider clinicProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        _actorProvider = actorProvider;
        _clinicProvider = clinicProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        // The synchronous path exists only for completeness — the whole application saves asynchronously. Blocking
        // here is acceptable precisely because the work is a fire-and-forget insert nobody is waiting on.
        FlushAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    /// <summary>
    /// A failed save leaves rows collected for a save that never happened; drop them so a later save in the same
    /// scope does not attribute them to itself.
    /// </summary>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null)
        {
            _pending.Remove(eventData.Context);
        }

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            // Read once per save, not per row: an operation has one actor and one moment, and stamping each row
            // with its own `UtcNow` would make a single deletion look like several events a few ticks apart.
            var actor = _actorProvider.Current;
            var occurredAt = DateTime.UtcNow;
            var scopedClinicId = _clinicProvider.ClinicId;

            var rows = new List<AuditEntry>();

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (!IsAuditable(entry))
                {
                    continue;
                }

                var action = entry.State switch
                {
                    EntityState.Added => AuditAction.Insert,
                    EntityState.Modified => AuditAction.Update,
                    EntityState.Deleted => AuditAction.Delete,
                    _ => (AuditAction?)null
                };

                if (action is not { } auditAction)
                {
                    continue;
                }

                var entityId = ResolveEntityId(entry);
                if (entityId is null)
                {
                    // No key means nothing to point at afterwards, which makes the row unusable rather than
                    // partial. In practice unreachable: every aggregate root assigns its key in its constructor.
                    continue;
                }

                rows.Add(new AuditEntry(
                    ResolveClinicId(entry, scopedClinicId),
                    actor.UserId,
                    actor.Email,
                    entry.Entity.GetType().Name,
                    entityId,
                    auditAction,
                    Summarize(entry, auditAction),
                    occurredAt));
            }

            if (rows.Count > 0)
            {
                _pending[context] = rows;
            }
        }
        catch (Exception ex)
        {
            // Collection runs *inside* the caller's save, so a throw here would fail the operation being audited —
            // the one outcome the contract forbids. Logged at Error and swallowed, like every other best-effort
            // side effect in this codebase.
            _pending.Remove(context);
            _logger.LogError(ex, "Failed to collect audit entries; the operation itself is unaffected.");
        }
    }

    /// <summary>
    /// Aggregate roots only, and never the two excluded types. The check is on <see cref="AggregateRoot{TId}"/>
    /// rather than on a list of type names, so a new aggregate is audited the day it is written — the derived-check
    /// pattern this codebase uses for the realtime keys and the schema verifier, for the same reason.
    /// </summary>
    private static bool IsAuditable(EntityEntry entry)
    {
        var type = entry.Entity.GetType();

        if (ExcludedEntityTypes.Contains(type.Name))
        {
            return false;
        }

        // ⚠️ Either marker. `IAuditable` was added because this walk — « is it an aggregate root? » — was the
        // only thing deciding what reached the journal, and it answered NO for the entire clinical record:
        // DentalRecord, MedicalDocument, PatientFile, ToothState, PatientMedicalHistory, PatientFamilyHistory,
        // PatientFlag and Payment are all plain Entity<Guid>. Editing a patient's name was recorded; editing
        // their clinical notes was not. See IAuditable for why they were not simply promoted instead.
        if (typeof(IAuditable).IsAssignableFrom(type))
        {
            return true;
        }

        for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 0)
        {
            return null;
        }

        // Composite keys are joined so the value still points at exactly one row. No aggregate root has one today;
        // returning only the first property would silently produce an ambiguous reference if one ever did.
        var parts = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    /// <summary>
    /// The clinic, in order of how much it can be trusted: the aggregate's own <c>ClinicId</c>; for
    /// <c>Clinic</c> and <c>ClinicReminderSettings</c> the key itself (both are keyed <em>by</em> the clinic); then
    /// the request's scoped clinic. Null when none of those exist — see <see cref="AuditEntry.ClinicId"/> on why
    /// that is a null and not <c>Guid.Empty</c>.
    /// </summary>
    private static Guid? ResolveClinicId(EntityEntry entry, Guid? scopedClinicId)
    {
        var ownProperty = entry.Metadata.FindProperty(nameof(AuditEntry.ClinicId));
        if (ownProperty is not null)
        {
            var value = entry.Property(ownProperty.Name).CurrentValue;
            if (value is Guid clinicId && clinicId != Guid.Empty)
            {
                return clinicId;
            }
        }
        else if (entry.Entity is Clinic or ClinicReminderSettings
                 && entry.Metadata.FindPrimaryKey()?.Properties is [{ } keyProperty]
                 && entry.Property(keyProperty.Name).CurrentValue is Guid ownId
                 && ownId != Guid.Empty)
        {
            return ownId;
        }

        return scopedClinicId;
    }

    /// <summary>
    /// The compact summary. An insert gets none — the action and the entity already say everything. An update
    /// lists what moved, spelling out the values of the properties that <em>are</em> the change. A delete records
    /// the row's identifying values, because after it there is nothing left to look up: « Patient supprimé » with
    /// no name is a record of a deletion nobody can identify.
    /// </summary>
    private static string? Summarize(EntityEntry entry, AuditAction action)
    {
        if (action == AuditAction.Insert)
        {
            return null;
        }

        if (action == AuditAction.Delete)
        {
            var identifying = entry.Properties
                .Where(p => !p.Metadata.IsPrimaryKey() && IsIdentifying(p.Metadata.Name))
                .Select(p => $"{p.Metadata.Name}: {Render(p.OriginalValue)}")
                .Take(MaxListedProperties)
                .ToList();

            return identifying.Count == 0 ? null : string.Join("; ", identifying);
        }

        var changed = ChangedProperties(entry).ToList();

        if (changed.Count == 0)
        {
            return null;
        }

        var parts = changed
            .Take(MaxListedProperties)
            .Select(c => ValuedProperties.Contains(c.Name)
                ? $"{c.Name}: {Render(c.Property.OriginalValue)} → {Render(c.Property.CurrentValue)}"
                : c.Name)
            .ToList();

        if (changed.Count > MaxListedProperties)
        {
            parts.Add($"+{changed.Count - MaxListedProperties}");
        }

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Every modified scalar of an aggregate root, <b>including the ones inside its owned value objects</b>.
    ///
    /// <para>⚠️ This is the fix for a silent, load-bearing gap. <c>entry.Properties</c> covers only the root's own
    /// columns, and an <c>OwnsOne</c> is a separate <c>EntityEntry</c> — so on <c>Patient</c>, whose PhoneNumber,
    /// Email, Address, Insurance and Cnam are all owned, changing <i>only</i> the phone produced
    /// « Modification · — »: an audit row that records that something happened and refuses to say what. Worse, a
    /// mixed edit printed « LastName; Notes » and dropped the phone change from the same save, so the journal was
    /// not merely incomplete, it was misleading about which fields moved.</para>
    ///
    /// <para>Named by the owned navigation when the value object is a single scalar (<c>PhoneNumber</c>,
    /// <c>Email</c>) and qualified otherwise (<c>CnamInfo.IdentifiantUnique</c>): « PhoneNumber.Value » names
    /// nothing a reader is looking for, while a multi-field VO needs to say which of its fields moved.</para>
    ///
    /// <para>Owned <i>collections</i> are deliberately not walked: EF surfaces each element as its own entry, and
    /// a per-element diff on a fat aggregate is the column dump <see cref="MaxListedProperties"/> exists to
    /// prevent.</para>
    /// </summary>
    private static IEnumerable<(string Name, PropertyEntry Property)> ChangedProperties(EntityEntry entry)
    {
        foreach (var property in Modified(entry))
        {
            yield return (property.Metadata.Name, property);
        }

        foreach (var reference in entry.References)
        {
            var target = reference.TargetEntry;
            if (target is null || !target.Metadata.IsOwned())
            {
                continue;
            }

            var scalars = target.Properties.Where(p => !p.Metadata.IsPrimaryKey()).ToList();
            var singleValued = scalars.Count == 1;

            foreach (var property in Modified(target))
            {
                yield return (
                    singleValued
                        ? reference.Metadata.Name
                        : $"{reference.Metadata.Name}.{property.Metadata.Name}",
                    property);
            }
        }
    }

    /// <summary>
    /// The modified, non-key scalars of one entry. `Version` is EF's own concurrency token (PostgreSQL `xmin`) and
    /// `UpdatedAt` moves on every single save — listing either would put two words of noise at the front of every
    /// summary in the table.
    /// </summary>
    private static IEnumerable<PropertyEntry> Modified(EntityEntry entry) =>
        entry.Properties
            .Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
            .Where(p => p.Metadata.Name is not (nameof(Entity<Guid>.Version) or "UpdatedAt"));

    /// <summary>
    /// What names a deleted row. Matched on the property name so it works across every aggregate without a
    /// per-entity map — the alternative being 38 hand-written projections, of which the newest entity's would be
    /// missing.
    /// </summary>
    private static bool IsIdentifying(string propertyName) => propertyName is
        "Name" or "FirstName" or "LastName" or "FullName" or "Email" or
        "InvoiceNumber" or "DevisNumber" or "CreditNoteNumber" or "Title" or
        "Description" or "Category" or "Amount" or "TotalTtc" or "TotalPlanned" or
        "Status" or "ExpenseDate" or "PatientId";

    private static string Render(object? value) => value switch
    {
        null => "∅",
        bool b => b ? "oui" : "non",
        DateTime d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString("0.###"),
        _ => value.ToString() ?? "∅"
    };

    /// <summary>
    /// Writes the collected rows on their own context and their own transaction, then forgets them. Every failure
    /// path logs at Error and returns: the operation being audited has already committed, and nothing here may
    /// disturb it.
    /// </summary>
    private async Task FlushAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !_pending.Remove(context, out var rows) || rows.Count == 0)
        {
            return;
        }

        try
        {
            // A scope of its own, and therefore a context of its own: reusing the caller's would put the inserts
            // back inside the transaction this whole design exists to stay out of. The chain's advisory lock and
            // its transaction are opened by ApplicationDbContext.SaveChangesAsync — see the ⚠️ there on why the
            // transaction has to span the whole append.
            using var scope = _scopeFactory.CreateScope();
            var auditContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await auditContext.AuditEntries.AddRangeAsync(rows, cancellationToken);
            await auditContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Error, not Warning: a hole in the audit ledger is a real incident — it is the thing the ledger was
            // built to make impossible — and the message has to be loud enough that someone notices it is not
            // recording. It still does not fail the clinical or money operation that produced it.
            _logger.LogError(
                ex,
                "Failed to write {Count} audit entr{Plural}. The audited operation committed; the ledger did not.",
                rows.Count,
                rows.Count == 1 ? "y" : "ies");

            await TryDeclareGapAsync(rows, ex, cancellationToken);
        }
    }

    /// <summary>
    /// Records that entries are missing here (FR-4.1). One row per chain the lost rows belonged to.
    ///
    /// <para><b>Why this is worth a second attempt at the thing that just failed.</b> Without it a failed write
    /// leaves a chain that is perfectly intact and quietly shorter — indistinguishable from a period in which the
    /// practice did nothing. With it, a later walk can tell « a gap we know about » from « a break nobody
    /// declared », which is the whole distinction FR-4.1 asks for. The gap row is a fraction of the size of what
    /// failed, on a fresh connection, so the common causes (a transient timeout, one oversized row, a unique
    /// collision on a duplicate sequence) do not recur.</para>
    ///
    /// <para>⚠️ <b>If this fails too, nothing is recorded and the chain is genuinely broken</b> — which is the
    /// honest outcome, and is what the walk will say. It is still never allowed to throw: the operation being
    /// audited has already committed.</para>
    /// </summary>
    private async Task TryDeclareGapAsync(
        List<AuditEntry> rows, Exception cause, CancellationToken cancellationToken)
    {
        try
        {
            var occurredAt = DateTime.UtcNow;
            var gaps = rows
                .GroupBy(r => r.ClinicId)
                .Select(g => AuditEntry.DeclaredGap(
                    g.Key,
                    g.First().UserId,
                    g.First().UserEmail,
                    g.Count(),
                    // The type only — a message can carry a row's own values, and this row is readable by the
                    // clinic's admin on « Journal d'activité ».
                    cause.GetType().Name,
                    occurredAt))
                .ToList();

            using var scope = _scopeFactory.CreateScope();
            var auditContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await auditContext.AuditEntries.AddRangeAsync(gaps, cancellationToken);
            await auditContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to record a declared gap; the audit chain is now genuinely broken at this point.");
        }
    }
}
