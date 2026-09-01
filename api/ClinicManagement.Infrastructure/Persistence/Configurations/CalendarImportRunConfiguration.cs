using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// The Google→App import ledger's schema.
/// </summary>
public class CalendarImportRunConfiguration : IEntityTypeConfiguration<CalendarImportRun>
{
    public void Configure(EntityTypeBuilder<CalendarImportRun> builder)
    {
        builder.ToTable("CalendarImportRuns");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // A real FK with a cascade, on BackupRunConfiguration's reasoning: what a pass imported into a clinic
        // that no longer exists is not evidence anyone can act on, and the rows it created went with the clinic.
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.StartedAtUtc).IsRequired();
        builder.Property(r => r.CompletedAtUtc);

        // Holds a user id (`local|{guid}` or a token subject) or `job|GoogleCalendarImportJob` — AuditEntry's own
        // actor convention, and sized to match it.
        builder.Property(r => r.TriggeredByUserId).IsRequired().HasMaxLength(200);

        builder.Property(r => r.WindowFromUtc).IsRequired();
        builder.Property(r => r.WindowToUtc).IsRequired();

        builder.Property(r => r.AppointmentsCreated).IsRequired();
        builder.Property(r => r.PatientsCreated).IsRequired();
        builder.Property(r => r.AppointmentsUpdated).IsRequired();
        builder.Property(r => r.AppointmentsLinked).IsRequired();

        builder.Property(r => r.RevertedAtUtc);
        builder.Property(r => r.RevertedByUserId).HasMaxLength(200);
        builder.Property(r => r.AppointmentsDeleted);
        builder.Property(r => r.PatientsDeleted);
        builder.Property(r => r.RowsKept);

        // The history read's shape: a clinic's runs, newest first. `GetLatestUndoableAsync` adds a
        // `RevertedAtUtc IS NULL` term and rides the same index.
        builder.HasIndex(r => new { r.ClinicId, r.StartedAtUtc });
    }
}
