using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentProcedureConfiguration : IEntityTypeConfiguration<AppointmentProcedure>
{
    public void Configure(EntityTypeBuilder<AppointmentProcedure> builder)
    {
        builder.ToTable("AppointmentProcedures");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.AppointmentId).IsRequired();

        builder.Property(p => p.ProcedureName).HasMaxLength(200);

        builder.Property(p => p.ColorHex).HasMaxLength(7);

        builder.Property(p => p.SequenceNumber).IsRequired();

        // Plain column, no FK — the same reasoning as the parent's TreatmentPlanItemId: a Draft plan's line ids are
        // regenerated when its acts are edited, so a referential constraint would be brittle. Existence is
        // validated in the handler, and it is indexed because the devis read-back queries by it.
        builder.Property(p => p.TreatmentPlanItemId);

        builder.HasIndex(p => p.TreatmentPlanItemId);

        // SetNull, matching Appointment.ProcedureTypeId: retiring a procedure must leave the booked visit intact.
        // ProcedureName is the snapshot that keeps the row readable once the link is gone.
        builder.HasOne(p => p.ProcedureType)
            .WithMany()
            .HasForeignKey(p => p.ProcedureTypeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.ProcedureTypeId);
    }
}
