using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.PatientId);

        builder.Property(a => a.AppointmentDateTime)
            .IsRequired();

        builder.Property(a => a.Duration)
            .IsRequired()
            .HasConversion(
                v => v.Ticks,
                v => TimeSpan.FromTicks(v));

        builder.Property(a => a.DoctorName)
            .HasMaxLength(200);

        builder.Property(a => a.Notes)
            .HasColumnType("text");

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.RecurringAppointmentId);

        builder.Property(a => a.CancellationReason)
            .HasMaxLength(500);

        builder.Property(a => a.CancelledAt);

        builder.Property(a => a.GoogleCalendarEventId)
            .HasMaxLength(500);

        builder.Property(a => a.ProcedureTypeId);

        builder.Property(a => a.ProcedureDurationMinutes);

        builder.Property(a => a.ProcedureColorHex)
            .HasMaxLength(7);

        // Plain column, no FK: treatment-plan item ids are regenerated when a Draft plan's lines are
        // edited, so a referential constraint would be brittle; existence is validated at the handler.
        builder.Property(a => a.TreatmentPlanItemId);

        // « Rien à facturer » — the closure worklist's escape hatch. Three nullable columns, no default and no
        // backfill: a visit recorded before today genuinely has no such note, which is a different fact from
        // « somebody said there was nothing to bill » and is why none of them is defaulted.
        builder.Property(a => a.NothingToBillAtUtc);

        builder.Property(a => a.NothingToBillReason)
            .HasMaxLength(500);

        // The actor is `User.Id`, which is a string in this model (an Auth0 `sub` or `local|{guid}`) — hence text
        // rather than uuid, and no FK, for AuditEntry's reason: the row must survive the account being deleted.
        builder.Property(a => a.NothingToBillByUserId)
            .HasMaxLength(200);

        // Partial: only the marked rows are ever looked up by this, and they are a small minority of a clinic's
        // agenda — the same shape as the cheque due-date index, and for the same reason.
        builder.HasIndex(a => a.NothingToBillAtUtc)
            .HasFilter("\"NothingToBillAtUtc\" IS NOT NULL");

        builder.HasIndex(a => a.TreatmentPlanItemId);

        // Serves the minutely progress pass, whose elapse half scans 30 days of open statuses; `Status` had no
        // index at all, so that read was a seq scan of the clinic's whole agenda every minute.
        builder.HasIndex(a => new { a.Status, a.AppointmentDateTime });

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.UpdatedAt);

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.ProcedureType)
            .WithMany(pt => pt.Appointments)
            .HasForeignKey(a => a.ProcedureTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Per-practitioner scheduling: DoctorId is a proper FK to Doctor (null = unassigned). SetNull so
        // deleting a doctor leaves their appointments intact but unassigned. Indexed for the doctor filter.
        builder.HasOne(a => a.Doctor)
            .WithMany()
            .HasForeignKey(a => a.DoctorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => a.DoctorId);

        // A séance is several acts (« détartrage + deux obturations » is one visit). Cascade because the rows are
        // aggregate children with no meaning outside their appointment — nothing else points at them.
        builder.HasMany(a => a.Procedures)
            .WithOne()
            .HasForeignKey(p => p.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Procedures).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}



