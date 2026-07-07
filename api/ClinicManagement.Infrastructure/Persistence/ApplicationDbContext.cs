using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Common;
using ClinicManagement.Infrastructure.Persistence.Configurations;

namespace ClinicManagement.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

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
    public DbSet<ProcedureType> ProcedureTypes { get; set; }
    public DbSet<PatientMedicalHistory> PatientMedicalHistories { get; set; }
    public DbSet<PatientFamilyHistory> PatientFamilyHistories { get; set; }
    public DbSet<DentalRecord> DentalRecords { get; set; }
    public DbSet<DentalRecordTooth> DentalRecordTeeth { get; set; }
    public DbSet<PatientFolder> PatientFolders { get; set; }
    public DbSet<MedicalDocument> MedicalDocuments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        
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



