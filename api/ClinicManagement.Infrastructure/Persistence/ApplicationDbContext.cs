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

    // The clinic provider is optional so the design-time factory and any manual construction still work
    // (they pass no provider → the global query filter is inactive). At runtime AddDbContext injects it.
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentClinicProvider? clinicProvider = null) : base(options)
    {
        _clinicProvider = clinicProvider;
    }

    // Exposed for the global query filters below. Accessed through the context instance so EF Core
    // treats them as parameters re-evaluated per query (never baked into the cached model). When no
    // clinic is in scope (background jobs, CLI, anonymous flows) the filter is inactive → all rows.
    private bool IsClinicScoped => _clinicProvider?.ClinicId != null;
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
    // Persistent odontogram — child-of-patient (no ClinicId, no HasQueryFilter); tenant-scoped via the patient.
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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // The clinic-scoping query filters are applied only to the aggregate roots (Patient/Appointment/
        // ProcedureType), not their child entities — this is deliberate. EF then warns that the roots are
        // the required end of relationships whose dependents lack a matching filter; handlers guard the
        // filtered-out case explicitly (null owning-patient => "not found"), so this warning is expected noise.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Multi-tenant backstop (defense-in-depth): scope the directly-clinic-owned entities by the
        // caller's clinic. Inactive when no clinic is in scope (see IsClinicScoped). This is a backstop —
        // handlers still do the authoritative DB-resolved User.ClinicId check. Where a request-scoped path
        // must read across clinics, it calls IgnoreQueryFilters() explicitly. User/Clinic are deliberately
        // NOT filtered (auth/join flows resolve them cross-clinic before a clinic context exists).
        modelBuilder.Entity<Patient>().HasQueryFilter(p => !IsClinicScoped || p.ClinicId == ScopedClinicId);
        modelBuilder.Entity<Appointment>().HasQueryFilter(a => !IsClinicScoped || a.ClinicId == ScopedClinicId);
        modelBuilder.Entity<ProcedureType>().HasQueryFilter(pt => !IsClinicScoped || pt.ClinicId == ScopedClinicId);
        // StaffNotification is directly clinic-owned → filtered like the others. NotificationRead has no
        // ClinicId; it is always queried scoped by UserId and joined to its clinic-filtered notification
        // (a user belongs to one clinic), so it needs no filter of its own (plan R-5).
        modelBuilder.Entity<StaffNotification>().HasQueryFilter(n => !IsClinicScoped || n.ClinicId == ScopedClinicId);
        // Invoice is directly clinic-owned → filtered like the other aggregate roots. Its children
        // (InvoiceLine/Payment) are reached only through the invoice, so they need no filter of their own.
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => !IsClinicScoped || i.ClinicId == ScopedClinicId);
        // CreditNote (avoir) is directly clinic-owned → filtered like the other aggregate roots.
        modelBuilder.Entity<CreditNote>().HasQueryFilter(c => !IsClinicScoped || c.ClinicId == ScopedClinicId);
        // StockMovement is directly clinic-owned → filtered like the other aggregate roots.
        modelBuilder.Entity<StockMovement>().HasQueryFilter(m => !IsClinicScoped || m.ClinicId == ScopedClinicId);
        // TreatmentPlan is directly clinic-owned → filtered like the other aggregate roots. Its children
        // (TreatmentPlanItem/Installment) are reached only through the plan, so they need no filter of their own.
        modelBuilder.Entity<TreatmentPlan>().HasQueryFilter(p => !IsClinicScoped || p.ClinicId == ScopedClinicId);
        // ClinicReminderSettings is keyed by the clinic id (shared PK) → filter on Id. The reminder dispatcher
        // runs with no clinic in scope (filter inactive) so it can still resolve any clinic's settings by id.
        modelBuilder.Entity<ClinicReminderSettings>().HasQueryFilter(s => !IsClinicScoped || s.Id == ScopedClinicId);
        // Per-clinic reference catalogs (#5): scoped like the other aggregate roots. The per-clinic seeder runs
        // with no clinic in scope (filter inactive) so it can read/write any clinic's rows by explicit ClinicId.
        // MedicationActiveIngredient is reached only through Medication, so it needs no filter of its own.
        modelBuilder.Entity<CnamNomenclatureEntry>().HasQueryFilter(e => !IsClinicScoped || e.ClinicId == ScopedClinicId);
        modelBuilder.Entity<CnamLetterValue>().HasQueryFilter(v => !IsClinicScoped || v.ClinicId == ScopedClinicId);
        modelBuilder.Entity<Medication>().HasQueryFilter(m => !IsClinicScoped || m.ClinicId == ScopedClinicId);
        modelBuilder.Entity<DentalActCode>().HasQueryFilter(e => !IsClinicScoped || e.ClinicId == ScopedClinicId);
        // Clinical-workflow-depth aggregate roots — directly clinic-owned → filtered like the others. Their
        // Patient children are reached only through the aggregate, so they need no filter of their own.
        modelBuilder.Entity<Expense>().HasQueryFilter(e => !IsClinicScoped || e.ClinicId == ScopedClinicId);
        modelBuilder.Entity<WaitingListEntry>().HasQueryFilter(w => !IsClinicScoped || w.ClinicId == ScopedClinicId);
        modelBuilder.Entity<LabWorkOrder>().HasQueryFilter(l => !IsClinicScoped || l.ClinicId == ScopedClinicId);
        // RecurringAppointment gained a ClinicId (clinical-workflow-depth) → clinic-scoped like the others.
        modelBuilder.Entity<RecurringAppointment>().HasQueryFilter(r => !IsClinicScoped || r.ClinicId == ScopedClinicId);

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



