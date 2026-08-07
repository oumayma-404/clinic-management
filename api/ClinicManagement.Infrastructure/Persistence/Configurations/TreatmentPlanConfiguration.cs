using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> builder)
    {
        builder.ToTable("TreatmentPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.ClinicId)
            .IsRequired();

        builder.Property(p => p.PatientId)
            .IsRequired();

        builder.Property(p => p.Number)
            .HasMaxLength(20);

        // Devis number is unique per clinic (gapless per-clinic-per-year, separate from invoices). Filtered
        // to non-null so multiple drafts (Number == null) coexist; also the concurrency backstop on accept.
        builder.HasIndex(p => new { p.ClinicId, p.Number })
            .IsUnique()
            .HasFilter("\"Number\" IS NOT NULL");

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Notes)
            .HasMaxLength(2000);

        builder.Property(p => p.AcceptedDate);

        builder.Property(p => p.CancellationReason)
            .HasMaxLength(1000);

        builder.Property(p => p.TotalPlanned);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Post-acceptance amendment counter. Existing plans read 0 (never amended) and the devis prints no
        // revision mention at 0, so nothing already in the wild changes appearance.
        builder.Property(p => p.RevisionNumber)
            .IsRequired()
            .HasDefaultValue(0);

        // Aggregate children: cascade-deleted with the plan (a draft delete removes its items/installments).
        builder.HasMany(p => p.Items)
            .WithOne()
            .HasForeignKey(i => i.TreatmentPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Installments)
            .WithOne()
            .HasForeignKey(i => i.TreatmentPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Installments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.ClinicId, p.PatientId });

        // L9 attribution — who earned this. A real FK to `Doctors`, not a bare Guid column: before L9 the only FK
        // to that table in the entire model was `Appointment.DoctorId`, and `WaitingListEntry.PreferredDoctorId`
        // demonstrates the cost of the bare form — nothing stopped it holding an id from another clinic, or one
        // that no longer exists.
        //
        // ⚠️ `SetNull`, matching `Appointment.DoctorId`: deleting a practitioner must leave the money and the
        // clinical record intact and merely unattributed. `Cascade` here would delete invoices when a dentist
        // leaves the practice, and `Restrict` would make removing them impossible.
        builder.HasOne(x => x.Doctor)
            .WithMany()
            .HasForeignKey(x => x.DoctorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexed for the practitioner filter on /factures and on the dashboard's Argent section — the only two
        // readers, and both filter on it.
        builder.HasIndex(x => x.DoctorId);

    }
}
