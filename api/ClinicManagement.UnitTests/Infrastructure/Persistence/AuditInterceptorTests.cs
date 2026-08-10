using System.Reflection;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// [I6] What the audit interceptor records, and what it refuses to record.
///
/// <para><b>Why this can be a unit test at all.</b> The interceptor's decisions are made against the
/// <c>ChangeTracker</c> — which aggregate roots are dirty, what their original values were, which action each
/// entry is in — and <b>attaching entities to a context opens no connection</b>. So the collection phase, which
/// is the entire substance of the class, is testable without a database. Same posture as
/// <c>RecallQueryTranslationTests</c>: the Npgsql provider is configured because the model needs one, and the
/// connection string below is never dialled.</para>
///
/// <para>The <i>flush</i> phase does need a second context, so it is tested for the one property that matters
/// most and cannot be checked any other way: that a failure there is <b>swallowed and logged at Error</b>. An
/// audit write must never roll back the clinical or money operation that produced it.</para>
///
/// <para>⚠️ <c>Collect</c> and <c>FlushAsync</c> are private, and they are invoked here by reflection rather
/// than by standing up a real save. That is deliberate and narrow: driving them through
/// <c>SaveChangesAsync</c> would require a live database, which is exactly what this suite does not have — and
/// what the surrounding guide forbids adding. The alternative is no coverage of the ledger's core logic at all.</para>
/// </summary>
public class AuditInterceptorTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ---------------------------------------------------------------- harness

    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // Never connected to — see the class remarks.
            .UseNpgsql("Host=localhost;Database=audit_tracker_only;Username=none;Password=none")
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class Harness
    {
        public Mock<IAuditActorProvider> Actor { get; } = new();
        public Mock<ICurrentClinicProvider> Clinic { get; } = new();
        public Mock<IServiceScopeFactory> Scopes { get; } = new();
        public List<string> Errors { get; } = new();

        public AuditSaveChangesInterceptor Interceptor { get; }

        /// <summary>
        /// <paramref name="scopedClinic"/> is deliberately tri-state via <c>bool noScopedClinic</c> rather than a
        /// defaulted <c>Guid?</c>: a <c>static readonly Guid</c> cannot be a compile-time default, and defaulting
        /// it to <c>null</c> would make « no clinic in scope » the implicit case for every test.
        /// </summary>
        public Harness(AuditActor? actor = null, bool noScopedClinic = false, Guid? scopedClinic = null)
        {
            scopedClinic = noScopedClinic ? null : scopedClinic ?? ClinicId;

            Actor.SetupGet(a => a.Current).Returns(actor ?? new AuditActor("local|owner", "owner@clinic.tn"));
            Clinic.SetupGet(c => c.ClinicId).Returns(scopedClinic);

            // The flush path is not exercised by the collection tests; a throwing factory would be noticed
            // immediately if one of them started reaching it.
            Scopes.Setup(f => f.CreateScope()).Throws(new InvalidOperationException("flush not expected here"));

            Interceptor = new AuditSaveChangesInterceptor(
                Actor.Object, Clinic.Object, Scopes.Object, new CapturingLogger(Errors));
        }
    }

    /// <summary>Records Error-level messages so « swallowed but logged » is assertable, not just assumed.</summary>
    private sealed class CapturingLogger : ILogger<AuditSaveChangesInterceptor>
    {
        private readonly List<string> _errors;
        public CapturingLogger(List<string> errors) => _errors = errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                _errors.Add(formatter(state, exception));
            }
        }
    }

    /// <summary>Runs the private collection phase against a context whose tracker is already arranged.</summary>
    private static void Collect(AuditSaveChangesInterceptor interceptor, DbContext context) =>
        typeof(AuditSaveChangesInterceptor)
            .GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(interceptor, new object?[] { context });

    /// <summary>The rows collected but not yet written, keyed by the context that produced them.</summary>
    private static List<AuditEntry> Pending(AuditSaveChangesInterceptor interceptor, DbContext context)
    {
        var field = (Dictionary<DbContext, List<AuditEntry>>)typeof(AuditSaveChangesInterceptor)
            .GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(interceptor)!;

        return field.TryGetValue(context, out var rows) ? rows : new List<AuditEntry>();
    }

    private static Patient NewPatient(Guid? clinicId = null) =>
        new(Guid.NewGuid(), clinicId ?? ClinicId, "Bechir", "Ben Salah", new DateTime(1980, 4, 2), "Male");

    // ---------------------------------------------------------------- what gets a row

    [Fact]
    public void An_Inserted_Aggregate_Gets_One_Insert_Row()
    {
        var harness = new Harness();
        using var db = Context();
        db.Patients.Add(NewPatient());

        Collect(harness.Interceptor, db);

        var row = Assert.Single(Pending(harness.Interceptor, db));
        Assert.Equal(nameof(Patient), row.EntityType);
        Assert.Equal(AuditAction.Insert, row.Action);
        Assert.Equal(ClinicId, row.ClinicId);
        Assert.Equal("local|owner", row.UserId);
        Assert.Equal("owner@clinic.tn", row.UserEmail);
        // An insert needs no summary: the action and the entity already say everything one could.
        Assert.Null(row.ChangedFields);
    }

    [Fact]
    public void An_Updated_Aggregate_Gets_One_Update_Row_Naming_What_Moved()
    {
        var harness = new Harness();
        using var db = Context();
        var patient = NewPatient();
        db.Patients.Attach(patient);
        patient.UpdatePersonalInfo("Bechir", "Ben Salah", new DateTime(1980, 4, 2), "Male", null, null);
        db.Entry(patient).Property(nameof(Patient.LastName)).IsModified = true;

        Collect(harness.Interceptor, db);

        var row = Assert.Single(Pending(harness.Interceptor, db));
        Assert.Equal(AuditAction.Update, row.Action);
        Assert.NotNull(row.ChangedFields);
        Assert.Contains(nameof(Patient.LastName), row.ChangedFields!);
    }

    /// <summary>
    /// [I6] A delete keeps the entity id and the row's identifying values.
    ///
    /// <para>This is the case the two-phase design exists for: after the save the entry is gone from the change
    /// tracker, so « qui a supprimé ce patient ? » is answerable only if the id and the name were captured
    /// beforehand. « Patient supprimé » with no name is a record of a deletion nobody can identify.</para>
    /// </summary>
    [Fact]
    public void A_Deleted_Aggregate_Keeps_Its_Id_And_Its_Identifying_Values()
    {
        var harness = new Harness();
        using var db = Context();
        var patient = NewPatient();
        db.Patients.Attach(patient);
        db.Patients.Remove(patient);

        Collect(harness.Interceptor, db);

        var row = Assert.Single(Pending(harness.Interceptor, db));
        Assert.Equal(AuditAction.Delete, row.Action);
        Assert.Equal(patient.Id.ToString(), row.EntityId);
        Assert.NotNull(row.ChangedFields);
        Assert.Contains("Ben Salah", row.ChangedFields!);
    }

    // One save, one moment: every row from a single collection shares its timestamp, or a single deletion would
    // read as several events a few ticks apart.
    [Fact]
    public void Every_Row_From_One_Save_Shares_One_Timestamp()
    {
        var harness = new Harness();
        using var db = Context();
        db.Patients.Add(NewPatient());
        db.Patients.Add(NewPatient());
        db.Expenses.Add(new Expense(Guid.NewGuid(), ClinicId, new DateTime(2026, 8, 3), "Loyer", 500m,
            PaymentMethod.Cash, null));

        Collect(harness.Interceptor, db);

        var rows = Pending(harness.Interceptor, db);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows.Select(r => r.OccurredAt).Distinct());
    }

    // ---------------------------------------------------------------- what does NOT get a row

    /// <summary>
    /// [I6] Child entities produce no row of their own — the aggregate is the unit of change.
    ///
    /// <para>Saving one invoice touches its lines and its payments. A row per tracked entity would answer
    /// « qui a annulé cette facture ? » with a fistful of rows for one action, which is a worse answer than one.</para>
    /// </summary>
    [Fact]
    public void Child_Entities_Do_Not_Get_Their_Own_Rows()
    {
        var harness = new Harness();
        using var db = Context();
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, Guid.NewGuid());
        invoice.SetLines(new[] { ("Détartrage", 1, 80m) });
        db.Invoices.Add(invoice);

        Collect(harness.Interceptor, db);

        var row = Assert.Single(Pending(harness.Interceptor, db));
        Assert.Equal(nameof(Invoice), row.EntityType);
    }

    // The ledger must not audit its own writes, or every row would beget another forever.
    [Fact]
    public void The_Ledger_Does_Not_Audit_Itself()
    {
        var harness = new Harness();
        using var db = Context();
        db.AuditEntries.Add(new AuditEntry(ClinicId, "local|owner", null, nameof(Patient), "x",
            AuditAction.Insert, null, new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)));

        Collect(harness.Interceptor, db);

        Assert.Empty(Pending(harness.Interceptor, db));
    }

    /// <summary>
    /// [I6] The outbound reminder outbox is excluded. Its dispatcher rewrites every due row's status on a
    /// <b>minutely</b> schedule, so auditing it would bury a clinic's real history under machine noise within a
    /// day — and it already has its own visible delivery log on « Rappels ».
    /// </summary>
    [Fact]
    public void The_Reminder_Outbox_Is_Excluded()
    {
        var harness = new Harness();
        using var db = Context();
        db.Notifications.Add(new Notification(
            Guid.NewGuid(), NotificationType.SMS, "Rappel", "Votre rendez-vous",
            new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc), clinicId: ClinicId));

        Collect(harness.Interceptor, db);

        Assert.Empty(Pending(harness.Interceptor, db));
    }

    // An unchanged tracked entity is not an event.
    [Fact]
    public void An_Unchanged_Entity_Produces_Nothing()
    {
        var harness = new Harness();
        using var db = Context();
        db.Patients.Attach(NewPatient());

        Collect(harness.Interceptor, db);

        Assert.Empty(Pending(harness.Interceptor, db));
    }

    // ---------------------------------------------------------------- the clinic

    // The aggregate's own ClinicId wins over the request's — the row describes what changed, not who was asking.
    [Fact]
    public void The_Rows_Clinic_Comes_From_The_Entity_Not_The_Request()
    {
        var harness = new Harness(scopedClinic: ClinicId);
        using var db = Context();
        db.Patients.Add(NewPatient(OtherClinicId));

        Collect(harness.Interceptor, db);

        Assert.Equal(OtherClinicId, Assert.Single(Pending(harness.Interceptor, db)).ClinicId);
    }

    // `Clinic` is keyed BY the clinic, so its own id is the attribution.
    [Fact]
    public void A_Clinic_Row_Is_Attributed_To_Itself()
    {
        var harness = new Harness(noScopedClinic: true);
        using var db = Context();
        db.Clinics.Add(new Clinic(ClinicId, "Cabinet Test", null, null, null, "ABC123"));

        Collect(harness.Interceptor, db);

        Assert.Equal(ClinicId, Assert.Single(Pending(harness.Interceptor, db)).ClinicId);
    }

    /// <summary>
    /// [DEV-4] With no clinic derivable from the entity and none in scope, the row is written with a <b>null</b>
    /// clinic rather than <c>Guid.Empty</c> — and rather than being dropped. This is the case
    /// <c>verify-schema</c>'s <c>audit-ledger-clinic-nullable</c> check protects: if the column were ever
    /// tightened to NOT NULL, this insert would fail inside the interceptor's own swallow-and-log and the ledger
    /// would silently stop recording every job and console mutation.
    /// </summary>
    [Fact]
    public void An_Unattributable_Mutation_Is_Recorded_With_A_Null_Clinic()
    {
        var harness = new Harness(actor: AuditActor.Process("verify-schema"), noScopedClinic: true);
        using var db = Context();
        // `User` carries a ClinicId, so an unattributable row needs an aggregate with none set. A user minted
        // with Guid.Empty stands in for exactly that: nothing on the row, nothing in scope.
        db.Users.Add(new User("local|orphan", Guid.Empty, User.RoleAdmin, "orphan@clinic.tn", "Orphan"));

        Collect(harness.Interceptor, db);

        var row = Assert.Single(Pending(harness.Interceptor, db));
        Assert.Null(row.ClinicId);
        Assert.Equal($"{AuditActor.ProcessPrefix}verify-schema", row.UserId);
    }

    // A string-keyed aggregate (`User`, whose id is the Auth0 sub or `local|{guid}`) must still be pointed at.
    [Fact]
    public void A_String_Keyed_Aggregate_Records_Its_Own_Key()
    {
        var harness = new Harness();
        using var db = Context();
        db.Users.Add(new User("local|abc-123", ClinicId, User.RoleSecretary, "sec@clinic.tn", "Sam"));

        Collect(harness.Interceptor, db);

        Assert.Equal("local|abc-123", Assert.Single(Pending(harness.Interceptor, db)).EntityId);
    }

    // ---------------------------------------------------------------- failure containment

    /// <summary>
    /// [I6] A failure in the flush is swallowed **and logged at Error**. The operation being audited has already
    /// committed; nothing here may disturb it. Error rather than Warning because a hole in the ledger is the
    /// exact thing the ledger was built to make impossible.
    /// </summary>
    [Fact]
    public async Task A_Failing_Audit_Write_Is_Swallowed_But_Logged_At_Error()
    {
        var harness = new Harness();
        using var db = Context();
        db.Patients.Add(NewPatient());
        Collect(harness.Interceptor, db);
        Assert.NotEmpty(Pending(harness.Interceptor, db));

        // The harness's scope factory throws — standing in for any failure on the audit connection.
        var flush = (Task)typeof(AuditSaveChangesInterceptor)
            .GetMethod("FlushAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(harness.Interceptor, new object?[] { db, CancellationToken.None })!;

        await flush; // must NOT throw
        Assert.Single(harness.Errors);
        Assert.Empty(Pending(harness.Interceptor, db));
    }

    /// <summary>
    /// [I6] A throw during collection cannot fail the caller's save either — collection runs *inside* it. Driven
    /// by an actor provider that throws, which is the one dependency read before anything else.
    /// </summary>
    [Fact]
    public void A_Failure_While_Collecting_Cannot_Fail_The_Operation()
    {
        var actor = new Mock<IAuditActorProvider>();
        actor.SetupGet(a => a.Current).Throws(new InvalidOperationException("boom"));
        var errors = new List<string>();
        var interceptor = new AuditSaveChangesInterceptor(
            actor.Object, new Mock<ICurrentClinicProvider>().Object,
            new Mock<IServiceScopeFactory>().Object, new CapturingLogger(errors));

        using var db = Context();
        db.Patients.Add(NewPatient());

        Collect(interceptor, db); // must not throw

        Assert.Single(errors);
        Assert.Empty(Pending(interceptor, db));
    }

    // A save that FAILED must not leave its rows queued for the next save in the same scope to adopt.
    [Fact]
    public void A_Failed_Save_Discards_Its_Collected_Rows()
    {
        var harness = new Harness();
        using var db = Context();
        db.Patients.Add(NewPatient());
        Collect(harness.Interceptor, db);
        Assert.NotEmpty(Pending(harness.Interceptor, db));

        harness.Interceptor.SaveChangesFailed(
            new Microsoft.EntityFrameworkCore.Diagnostics.DbContextErrorEventData(
                default!, default!, db, new InvalidOperationException("save failed")));

        Assert.Empty(Pending(harness.Interceptor, db));
    }

    // ---------------------------------------------------------------- the derived rule

    /// <summary>
    /// [I6] The auditable set is <b>derived</b> from <see cref="AggregateRoot{TId}"/>, so the only thing anyone
    /// can quietly grow is the exclusion list — and this pins it to the three documented names.
    ///
    /// <para>Deliberately <b>not</b> a re-walk of the base chain over every model type: that would reimplement
    /// the production rule and then assert the reimplementation against itself, which passes whatever the
    /// interceptor does. The roots-get-a-row / children-do-not behaviour is asserted for real above
    /// (<see cref="An_Inserted_Aggregate_Gets_One_Insert_Row"/>,
    /// <see cref="Child_Entities_Do_Not_Get_Their_Own_Rows"/>) by running the real collection phase over a real
    /// change tracker. What is left to guard is the hand-maintained part, which is exactly this list.</para>
    /// </summary>
    [Fact]
    public void The_Exclusion_List_Is_Still_Only_The_Documented_Types()
    {
        var excluded = (HashSet<string>)typeof(AuditSaveChangesInterceptor)
            .GetField("ExcludedEntityTypes", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        // Each entry is structural, and each has its reason on the field itself:
        //   AuditEntry    — auditing the audit ledger recurses forever.
        //   Notification  — a minutely-rewritten outbox would bury a clinic's real history in machine noise.
        //   ClinicSignup  — written by the ANONYMOUS signup endpoint, so there is no actor and no clinic to
        //                   attribute it to, no reading of GET /api/audit could ever surface it, and a purge row
        //                   would preserve an abandoned visitor's name and address for ever.
        //   PlatformAccount / PlatformRecoveryCode — the VENDOR's console identity (platform-console). « Journal
        //                   d'activité » is a CLINIC's history, read by that clinic's admin; a console sign-in, a
        //                   lockout counter or a spent recovery code belongs to no cabinet, so every such row
        //                   would be unattributable noise nobody can see. ⚠️ This excludes the console's own
        //                   ACCOUNT rows only — what the console does *to* a cabinet is still audited, because
        //                   that write touches the cabinet's own aggregates and carries `console|{accountId}`.
        Assert.True(
            excluded.SetEquals(new[]
            {
                nameof(AuditEntry), nameof(Notification), nameof(ClinicSignup),
                nameof(PlatformAccount), nameof(PlatformRecoveryCode)
            }),
            "The audit exclusion list changed to [" + string.Join(", ", excluded.OrderBy(x => x))
            + "]. Every entry here is structural — self-audit recursion, a minutely-rewritten outbox, and a row "
            + "with no actor or clinic to attribute. Anything else excluded is a mutation an owner can no longer "
            + "see — justify it on the field and update this test.");
    }
}
