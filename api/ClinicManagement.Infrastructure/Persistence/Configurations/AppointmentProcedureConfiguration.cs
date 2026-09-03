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

        // Which step of that devis act this séance does. Plain column, no FK, for TreatmentPlanItemId's reason.
        // Indexed and filtered: only a séance of a multi-step act carries one, so the overwhelming majority of
        // rows are null, and the read that wants it asks « cette étape est-elle déjà planifiée ? ».
        builder.Property(p => p.TreatmentPlanItemStepId);

        builder.HasIndex(p => p.TreatmentPlanItemStepId)
            .HasFilter("\"TreatmentPlanItemStepId\" IS NOT NULL");

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
