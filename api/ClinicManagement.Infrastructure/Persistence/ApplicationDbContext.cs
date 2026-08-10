using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence.Configurations;

namespace ClinicManagement.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    private readonly ICurrentClinicProvider? _clinicProvider;

    // The clinic provider is optional so the design-time factory and any manual construction still work (they
    // pass no provider → the filters return everything, as before). At runtime AddDbContext always injects it.
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentClinicProvider? clinicProvider = null) : base(options)
    {
        _clinicProvider = clinicProvider;
    }

    // Exposed for the global query filters below. Accessed through the context instance so EF Core treats them
    // as parameters re-evaluated per query (never baked into the cached model).
    //
    // ⚠️ The three states are ITenantScope's, and only one of them returns everything: a scope that declared
    // itself cross-clinic. A scope nobody set leaves ScopedClinicId at Guid.Empty, which no row carries — so the
    // filter refuses instead of switching off, and a path that forgot to establish a scope reads nothing rather
    // than every clinic. No provider *at all* (the design-time factory, a hand-constructed context) still reads
    // as system-wide; every DI'd path has one, including the console verbs (see the floor in AddInfrastructure).
    private bool IsSystemWide => _clinicProvider is null || _clinicProvider.IsSystemWide;
    private Guid ScopedClinicId => _clinicProvider?.ClinicId ?? Guid.Empty;

    public DbSet<Clinic> Clinics { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Doctor> Doctors { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PatientFile> PatientFiles { get; set; }
    public DbSet<PatientFlag> PatientFlags { get; set; }
    public DbSet<RecurringAppointment> RecurringAppointments { get; set; }
    public DbSet<StockItem> StockItems { get; set; }
    // Append-only stock-movement audit log (consume/restock history) — clinic-scoped aggregate root.
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<ProcedureType> ProcedureTypes { get; set; }
    // Clinical-workflow-depth: caisse expenses, salle-d'attente entries, and dental-lab work orders
    // (all clinic-scoped aggregate roots — added to the global clinic query filter below).
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<WaitingListEntry> WaitingListEntries { get; set; }
    public DbSet<LabWorkOrder> LabWorkOrders { get; set; }
    public DbSet<PatientMedicalHistory> PatientMedicalHistories { get; set; }
    public DbSet<PatientFamilyHistory> PatientFamilyHistories { get; set; }
    public DbSet<DentalRecord> DentalRecords { get; set; }
    public DbSet<DentalRecordTooth> DentalRecordTeeth { get; set; }
    public DbSet<DentalRecordAct> DentalRecordActs { get; set; }
    // Persistent odontogram — child-of-patient, and since the clinical-child filter work it carries its own
    // denormalised ClinicId and a HasQueryFilter like every other clinic-scoped table.
    public DbSet<ToothState> ToothStates { get; set; }
    public DbSet<PatientFolder> PatientFolders { get; set; }
    public DbSet<MedicalDocument> MedicalDocuments { get; set; }
    public DbSet<StaffNotification> StaffNotifications { get; set; }
    public DbSet<NotificationRead> NotificationReads { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    // Avoirs (credit notes) — clinic-scoped aggregate root offsetting a paid invoice's collected amount.
    public DbSet<CreditNote> CreditNotes { get; set; }
    // Treatment plans / devis (clinic-scoped aggregate root; children TreatmentPlanItem/Installment reached via it).
    public DbSet<TreatmentPlan> TreatmentPlans { get; set; }
    public DbSet<ClinicReminderSettings> ClinicReminderSettings { get; set; }
    // Outbound document-email outbox (clinic-scoped aggregate root — added to the global filter below). Its
    // dispatcher job runs with no clinic in scope, so the filter is inactive there and it can drain every
    // clinic's queue, exactly like the reminder outbox.
    public DbSet<DocumentEmail> DocumentEmails { get; set; }
    // One user's dashboard layout choices (1:1 with User, shared PK). Deliberately carries NO clinic query
    // filter: the row is keyed by the user id and a user belongs to exactly one clinic, so it is scoped by
    // UserId alone — the same reasoning as NotificationRead below.
    public DbSet<UserDashboardPreference> UserDashboardPreferences { get; set; }
    // Per-clinic CNAM reference data (feature cloud-security-and-tenant-isolation, #5): clinic-scoped via
    // HasQueryFilter below. Every clinic is seeded with the SAME default catalog + VLC values on creation,
    // then each clinic's admin edits stay private to it.
    public DbSet<CnamNomenclatureEntry> CnamNomenclatureEntries { get; set; }
    public DbSet<CnamLetterValue> CnamLetterValues { get; set; }
    // Per-clinic medication catalog (#5): clinic-scoped. Backs the ordonnance medication picker.
    public DbSet<Medication> Medications { get; set; }
    // Child of Medication (reached via its parent) — no ClinicId, no query filter of its own.
    public DbSet<MedicationActiveIngredient> MedicationActiveIngredients { get; set; }
    // Per-clinic dental act catalog (chapitre DCH, #5): clinic-scoped. Backs the treatment-plan act picker.
    public DbSet<DentalActCode> DentalActCodes { get; set; }
    // The audit ledger (I6) — written only by AuditSaveChangesInterceptor, read only by GET /api/audit.
    // ⚠️ Deliberately carries **no** global clinic query filter, unlike every other clinic-scoped table here.
    // Two reasons, and both point the same way: its ClinicId is nullable (a job or a console verb can mutate a
    // row with no clinic derivable from it), so a filter comparing it to the scoped id would silently hide
    // exactly the unattributed rows an owner most needs to see; and the interceptor writes on a context whose
    // clinic scope belongs to the request being audited, not to the row — filtering the write side is
    // meaningless. `GetAuditEntriesQuery` filters by the caller's DB-resolved clinic explicitly, which is the
    // authoritative check everywhere in this codebase anyway.
    public DbSet<AuditEntry> AuditEntries { get; set; }

    // The backup ledger (L4d). Clinic-scoped and **filtered** (see OnModelCreating), unlike AuditEntries: its
    // ClinicId is non-nullable, so the filter has nothing to hide. The daily job runs with no clinic in scope,
    // which leaves the filter inactive — the same arrangement the reminder dispatcher and the per-clinic seeder
    // already rely on.
    public DbSet<BackupRun> BackupRuns { get; set; }

    // OS push (mobile-native-shells Part 6). Both clinic-scoped and filtered; PushDispatchJob declares
    // UseSystemWide to drain every clinic's queue, exactly as the reminder dispatcher does.
    public DbSet<DeviceRegistration> DeviceRegistrations { get; set; }
    public DbSet<PushDelivery> PushDeliveries { get; set; }

    // Pending clinic self-signups (clinic-self-signup). Carries **no ClinicId at all** — there is no clinic yet —
    // so it is outside the tenant filter by construction rather than by exemption, and the guard that derives the
    // clinic-owned set from that column never sees it.
    public DbSet<ClinicSignup> ClinicSignups { get; set; }

    // The vendor's console identity population (platform-console FR-1). Like ClinicSignup above and for the same
    // structural reason, these carry **no ClinicId**: a console account belongs to no cabinet, which is the whole
    // point of it, so they sit outside the tenant filter by construction and need no named exemption in
    // TenantScopeFilterTests — that guard derives its clinic-owned set from the column neither of them has.
    public DbSet<PlatformAccount> PlatformAccounts { get; set; }
    public DbSet<PlatformRecoveryCode> PlatformRecoveryCodes { get; set; }

    // The console's activity counters (platform-console Part 2). These DO carry a ClinicId, so unlike the two
    // above they are named decisions in TenantScopeFilterTests rather than absent from it: they are the VENDOR's
    // measurements about a cabinet, written by the counter job and read only by the console — no clinic-facing
    // surface reads them, so a per-clinic filter would guard a door nobody uses while making the one legitimate
    // reader depend on lifting it.
    public DbSet<ClinicActivityDay> ClinicActivityDays { get; set; }
    public DbSet<ClinicActivitySnapshot> ClinicActivitySnapshots { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // The clinic-scoping query filters are applied to the directly-clinic-owned AGGREGATE ROOTS — 19 of
        // them, listed in OnModelCreating — never to their child entities, which are reached only through a
        // filtered parent. This is deliberate. EF then warns that the roots are the required end of
        // relationships whose dependents lack a matching filter; handlers guard the filtered-out case
        // explicitly (null owning-patient => "not found"), so this warning is expected noise.
        //
        // (AC-P4.29: this comment used to name only Patient/Appointment/ProcedureType. Fourteen more roots had
        // been filtered since it was written, so it was actively misleading about the size of the backstop.)
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        base.OnConfiguring(optionsBuilder);
    }

    /// <summary>
    /// Model-wide money precision (AC-P4.37). Every decimal in this model is Tunisian dinars stored in millimes,
    /// so <c>(18,3)</c> is the rule and the 26 explicit <c>HasColumnType("decimal(18,3)")</c> calls that used to
    /// state it 26 times are gone.
    ///
    /// <para><b>The convention alone would have done nothing</b>, which is why those deletions are part of the
    /// same change. <c>GetColumnType()</c> returns an explicit annotation verbatim and bypasses facet-derived
    /// store types entirely, so with the annotations still in place the differ would emit zero
    /// <c>AlterColumn</c>s and <c>StockItem.UnitPrice</c> would have stayed at 2 decimals — the exact bug this
    /// looks like it fixes. An earlier draft of the spec claimed otherwise; it was corrected during planning.</para>
    ///
    /// <para>The two VAT-rate columns keep their own precision through a retained explicit annotation
    /// (<c>ClinicConfiguration</c>, <c>InvoiceConfiguration</c>): they are rates, not money, and a convention
    /// that silently widened a VAT rate would be worse than the drift it fixes (AC-P4.38). <c>verify-schema</c>
    /// asserts both halves — every other decimal at <c>(18,3)</c>, those two <b>not</b>.</para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 3);
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // PostgreSQL's unaccent(), so the paginated lists' free-text search can fold accents in SQL. Before
        // paging, search ran in C# over every row of the clinic; a page of 25 makes that impossible — the term
        // has to be matched by the database or it only ever sees the page the user is already looking at.
        SqlSearch.MapUnaccent(modelBuilder);

        // Multi-tenant isolation layer: scope the directly-clinic-owned entities to the scope's clinic.
        //
        // ⚠️ These filters used to be **fail-open** — no clinic in scope meant no filter — which made them a
        // backstop that protected nothing on any path that failed to establish one. They now refuse
        // (ITenantScope.Unset ⇒ ScopedClinicId is Guid.Empty ⇒ no rows), so a job, verb or request that reads
        // across clinics has to say so through UseSystemWide and is answerable for it. Handlers still do the
        // authoritative DB-resolved User.ClinicId check: this is the second layer, not the first.
        //
        // User/Clinic stay deliberately unfiltered — the auth, setup and join flows resolve them before any
        // clinic exists to be in scope, and filtering them would break onboarding rather than protect it.
        //
        // ⚠️ `Notification` is unfiltered for the same structural reason as `AuditEntries` below, and it is recorded
        // here because that reason is NOT the one `TenantScopeFilterTests.UnfilteredByDesign` gives: its `ClinicId`
        // is **nullable**, so a filter comparing it to the scoped id would hide exactly the unattributed rows an
        // owner needs. « Drained cross-clinic by the dispatcher » is not the reason — `DocumentEmail` is filtered and
        // its dispatcher declares `UseSystemWide` too. All four reachable reads take a `clinicId` explicitly.
        modelBuilder.Entity<Patient>().HasQueryFilter(p => IsSystemWide || p.ClinicId == ScopedClinicId);
        // The seven clinical children of Patient. They carried no ClinicId for the product's whole life, so the
        // per-handler check was their ONLY layer — and it is the layer a new read forgets, silently, with the
        // symptom being another clinic's record on screen. The column is denormalised from the patient (the two
        // must agree; `verify-schema`'s `clinical-child-clinic-matches-patient` is what holds that, since no
        // model construct can) rather than filtered through the `Patient` navigation: filtering through it would
        // put a correlated subquery on the hottest reads in the product, and every other filtered entity here
        // states its clinic as a column. Same shape, same rule, one join fewer.
        modelBuilder.Entity<PatientFile>().HasQueryFilter(f => IsSystemWide || f.ClinicId == ScopedClinicId);
        modelBuilder.Entity<PatientFolder>().HasQueryFilter(f => IsSystemWide || f.ClinicId == ScopedClinicId);
        modelBuilder.Entity<MedicalDocument>().HasQueryFilter(d => IsSystemWide || d.ClinicId == ScopedClinicId);
        modelBuilder.Entity<DentalRecord>().HasQueryFilter(r => IsSystemWide || r.ClinicId == ScopedClinicId);
        modelBuilder.Entity<ToothState>().HasQueryFilter(t => IsSystemWide || t.ClinicId == ScopedClinicId);
        modelBuilder.Entity<PatientMedicalHistory>().HasQueryFilter(h => IsSystemWide || h.ClinicId == ScopedClinicId);
        modelBuilder.Entity<PatientFamilyHistory>().HasQueryFilter(h => IsSystemWide || h.ClinicId == ScopedClinicId);
        modelBuilder.Entity<Appointment>().HasQueryFilter(a => IsSystemWide || a.ClinicId == ScopedClinicId);
        modelBuilder.Entity<ProcedureType>().HasQueryFilter(pt => IsSystemWide || pt.ClinicId == ScopedClinicId);
        // StaffNotification is directly clinic-owned → filtered like the others. NotificationRead has no
        // ClinicId; it is always queried scoped by UserId and joined to its clinic-filtered notification
        // (a user belongs to one clinic), so it needs no filter of its own (plan R-5).
        modelBuilder.Entity<StaffNotification>().HasQueryFilter(n => IsSystemWide || n.ClinicId == ScopedClinicId);
        // Invoice is directly clinic-owned → filtered like the other aggregate roots. Its children
        // (InvoiceLine/Payment) are reached only through the invoice, so they need no filter of their own.
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => IsSystemWide || i.ClinicId == ScopedClinicId);
        // CreditNote (avoir) is directly clinic-owned → filtered like the other aggregate roots.
        modelBuilder.Entity<CreditNote>().HasQueryFilter(c => IsSystemWide || c.ClinicId == ScopedClinicId);
        // StockMovement is directly clinic-owned → filtered like the other aggregate roots.
        modelBuilder.Entity<StockMovement>().HasQueryFilter(m => IsSystemWide || m.ClinicId == ScopedClinicId);
        // TreatmentPlan is directly clinic-owned → filtered like the other aggregate roots. Its children
        // (TreatmentPlanItem/Installment) are reached only through the plan, so they need no filter of their own.
        modelBuilder.Entity<TreatmentPlan>().HasQueryFilter(p => IsSystemWide || p.ClinicId == ScopedClinicId);
        // ClinicReminderSettings is keyed by the clinic id (shared PK) → filter on Id. The reminder dispatcher
        // declares UseSystemWide, which is what lets it resolve any clinic's settings by id.
        modelBuilder.Entity<ClinicReminderSettings>().HasQueryFilter(s => IsSystemWide || s.Id == ScopedClinicId);
        // DocumentEmail is directly clinic-owned → filtered like the other aggregate roots. Its dispatcher
        // declares UseSystemWide, which is what lets one tick drain every clinic's queue.
        modelBuilder.Entity<DocumentEmail>().HasQueryFilter(e => IsSystemWide || e.ClinicId == ScopedClinicId);
        // Per-clinic reference catalogs (#5): scoped like the other aggregate roots. The per-clinic seeder reads
        // through IgnoreQueryFilters() throughout, so it is immune to the scope rather than dependent on it.
        // MedicationActiveIngredient is reached only through Medication, so it needs no filter of its own.
        modelBuilder.Entity<CnamNomenclatureEntry>().HasQueryFilter(e => IsSystemWide || e.ClinicId == ScopedClinicId);
        modelBuilder.Entity<CnamLetterValue>().HasQueryFilter(v => IsSystemWide || v.ClinicId == ScopedClinicId);
        modelBuilder.Entity<Medication>().HasQueryFilter(m => IsSystemWide || m.ClinicId == ScopedClinicId);
        modelBuilder.Entity<DentalActCode>().HasQueryFilter(e => IsSystemWide || e.ClinicId == ScopedClinicId);
        // Clinical-workflow-depth aggregate roots — directly clinic-owned → filtered like the others. Their
        // Patient children are reached only through the aggregate, so they need no filter of their own.
        modelBuilder.Entity<Expense>().HasQueryFilter(e => IsSystemWide || e.ClinicId == ScopedClinicId);
        modelBuilder.Entity<WaitingListEntry>().HasQueryFilter(w => IsSystemWide || w.ClinicId == ScopedClinicId);
        modelBuilder.Entity<LabWorkOrder>().HasQueryFilter(l => IsSystemWide || l.ClinicId == ScopedClinicId);
        // RecurringAppointment gained a ClinicId (clinical-workflow-depth) → clinic-scoped like the others.
        modelBuilder.Entity<RecurringAppointment>().HasQueryFilter(r => IsSystemWide || r.ClinicId == ScopedClinicId);
        // AC-P4.27 — Doctor and StockItem were the last two directly-clinic-owned roots left unfiltered, and
        // StockItem's own child StockMovement was filtered while its PARENT was not: the backstop protected the
        // ledger but not the item it belongs to.
        modelBuilder.Entity<Doctor>().HasQueryFilter(d => IsSystemWide || d.ClinicId == ScopedClinicId);
        modelBuilder.Entity<StockItem>().HasQueryFilter(s => IsSystemWide || s.ClinicId == ScopedClinicId);
        // StockBatch is a child of StockItem, reached only through its filtered parent → no filter of its own,
        // the same rule as InvoiceLine/Installment. ProcedureTypeMaterial likewise, under ProcedureType.
        // L4d — the backup ledger is clinic-owned with a NON-nullable ClinicId, so unlike AuditEntries it is
        // filtered like the rest. The hourly BackupJob declares UseSystemWide to iterate every clinic.
        modelBuilder.Entity<BackupRun>().HasQueryFilter(b => IsSystemWide || b.ClinicId == ScopedClinicId);
        // Part 6 — the push registry and its outbox, both clinic-owned with non-nullable ClinicIds (AC-53).
        // ⚠️ One read deliberately escapes this filter: IDeviceRegistrationRepository.GetByTokenAcrossClinicsAsync,
        // because the token is globally unique and a clinic-scoped lookup would miss another clinic's row and turn
        // the rebind into a unique-index violation. Its own doc comment carries why that is not a leak.
        modelBuilder.Entity<DeviceRegistration>().HasQueryFilter(d => IsSystemWide || d.ClinicId == ScopedClinicId);
        modelBuilder.Entity<PushDelivery>().HasQueryFilter(p => IsSystemWide || p.ClinicId == ScopedClinicId);

        // Optimistic concurrency for every entity, with no schema change: map Entity<T>.Version onto
        // PostgreSQL's xmin system column. EF then appends it to the WHERE of each UPDATE/DELETE, so a row a
        // peer changed since we read it matches nothing and throws DbUpdateConcurrencyException — which
        // UnitOfWork translates into a ConflictException → HTTP 409.
        //
        // ⚠️ `SkipsConcurrencyToken` (declared below) is the one opt-out, and it is about *semantics*, not about
        // avoiding an inconvenience. See its own comment.
        //
        // Three exclusions, each load-bearing:
        //  * ToList() first — modelBuilder.Entity(clrType) can ADD entity types, and mutating the collection
        //    being enumerated throws.
        //  * Owned types and shared-CLR-type entities are skipped. An owned type has no row of its own, and
        //    PhoneNumber is owned TWICE by Patient (PhoneNumber + EmergencyContactPhone), so it arrives as a
        //    shared-CLR-type entity that must not be configured by CLR type at all.
        //  * Anything not deriving from Entity<> is skipped — NotificationRead is a plain composite-key class
        //    and has no Version property to map.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (entityType.IsOwned() || entityType.HasSharedClrType)
            {
                continue;
            }

            if (!DerivesFromEntity(entityType.ClrType))
            {
                continue;
            }

            // Deliberately NOT Npgsql's UseXminAsConcurrencyToken(): it is obsolete in 8.0 and, worse, it adds
            // a SHADOW xmin property — leaving our CLR Version property to be mapped as an ordinary bigint
            // column called "Version". Mapping Version onto xmin explicitly is what actually binds the two.
            var version = modelBuilder.Entity(entityType.ClrType)
                .Property<uint>(nameof(Domain.Common.Entity<int>.Version))
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate();

            // The mapping above is unconditional — every entity's Version must resolve to the xmin system column,
            // or EF would look for an ordinary "Version" column that no table has. Only the *token* is opt-out.
            if (!SkipsConcurrencyToken.Contains(entityType.ClrType))
            {
                version.IsConcurrencyToken();
            }
        }

        // Apply a value converter for all DateTime and DateTime? properties to ensure UTC
        // This is required for PostgreSQL which only accepts UTC DateTime values
        // We apply this after configurations to ensure it works with all entities
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Only apply converter if one isn't already configured
                if (property.GetValueConverter() == null)
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                            v => ConvertToUtc(v),
                            v => v));
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
                            v => v.HasValue ? ConvertToUtc(v.Value) : null,
                            v => v));
                    }
                }
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Entities whose <c>Version</c> is still mapped onto <c>xmin</c> (so it reads back) but is <b>not</b> used as
    /// a concurrency token, so a losing write is not rejected.
    ///
    /// <para>
    /// This is a semantic opt-out, not a way around an inconvenient error. Optimistic concurrency exists here to
    /// stop a <i>lost update</i> to shared clinical or financial data: two people editing one patient, one invoice,
    /// one devis, where silently discarding the earlier write loses real information and « quelqu'un d'autre a
    /// modifié cet enregistrement » is exactly the right thing to say.
    /// </para>
    /// <para>
    /// <see cref="UserDashboardPreference"/> is none of that. It is one user's own view of their own dashboard,
    /// written only by them, and a full replace every time. Two writes racing — a double-click, two browser tabs,
    /// the desktop and the phone — is not a data-integrity event: last-write-wins is the *correct* semantics, and
    /// blocking the save is strictly worse than accepting the later one. Worse, the message the token produces is
    /// simply false for this row, because there is no « quelqu'un d'autre » who can write it.
    /// </para>
    /// <para>
    /// Adding a type here needs that argument to hold. If losing a write would lose information a user typed, it
    /// does not belong in this set.
    /// </para>
    /// </summary>
    private static readonly HashSet<Type> SkipsConcurrencyToken = new()
    {
        typeof(UserDashboardPreference),
    };

    /// <summary>
    /// Walks the base chain looking for the open generic <c>Entity&lt;&gt;</c>. Checking for the property by
    /// name would also match anything that merely happens to have one.
    /// </summary>
    private static bool DerivesFromEntity(Type? clrType)
    {
        for (var type = clrType; type != null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Domain.Common.Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTime ConvertToUtc(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            // Assume Unspecified dates are already in UTC (common when parsing from JSON)
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }
        else if (dateTime.Kind == DateTimeKind.Local)
        {
            // Convert Local to UTC
            return dateTime.ToUniversalTime();
        }
        else
        {
            // Already UTC
            return dateTime;
        }
    }

    /// <summary>
    /// Converts all DateTime properties to UTC before saving to PostgreSQL.
    /// PostgreSQL requires DateTime values to be UTC when using 'timestamp with time zone'.
    /// This method handles all entities automatically, including those with private setters.
    /// </summary>
    private void ConvertDateTimesToUtc()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            // Get all properties tracked by EF Core
            var properties = entry.Properties
                .Where(p => p.Metadata.ClrType == typeof(DateTime) || p.Metadata.ClrType == typeof(DateTime?));

            foreach (var property in properties)
            {
                try
                {
                    var currentValue = property.CurrentValue;
                    
                    if (currentValue == null && property.Metadata.ClrType == typeof(DateTime?))
                        continue;

                    if (currentValue is DateTime dateTime)
                    {
                        DateTime utcDateTime;
                        
                        if (dateTime.Kind == DateTimeKind.Unspecified)
                        {
                            // Assume Unspecified dates are already in UTC (common when parsing from JSON)
                            utcDateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                        }
                        else if (dateTime.Kind == DateTimeKind.Local)
                        {
                            // Convert Local to UTC
                            utcDateTime = dateTime.ToUniversalTime();
                        }
                        else
                        {
                            // Already UTC, no conversion needed
                            continue;
                        }

                        // Set the UTC value using EF Core's property accessor
                        property.CurrentValue = utcDateTime;
                    }
                }
                catch
                {
                    // Skip properties that can't be accessed
                    // This is safe to ignore as EF Core will handle them
                    continue;
                }
            }
        }
    }

    public override int SaveChanges()
    {
        ConvertDateTimesToUtc();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDateTimesToUtc();
        return await base.SaveChangesAsync(cancellationToken);
    }
}



