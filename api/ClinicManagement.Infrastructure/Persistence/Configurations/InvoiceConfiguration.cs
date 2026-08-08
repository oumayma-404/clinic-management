using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.ClinicId)
            .IsRequired();

        builder.Property(i => i.PatientId)
            .IsRequired();

        builder.Property(i => i.DentalRecordId);
        builder.Property(i => i.AppointmentId);
        // Soft link to the devis this note was generated from (no FK, like DentalRecordId/AppointmentId).
        builder.Property(i => i.TreatmentPlanId);

        builder.Property(i => i.Number)
            .HasMaxLength(20);

        // The fiscal number is unique per clinic (gapless per-clinic-per-year sequence). Filtered to
        // non-null so multiple drafts (Number == null) coexist; also the concurrency backstop for two
        // simultaneous issues computing the same sequence (unique violation → handler retries).
        builder.HasIndex(i => new { i.ClinicId, i.Number })
            .IsUnique()
            .HasFilter("\"Number\" IS NOT NULL");

        builder.Property(i => i.IssueDate);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(i => i.VatApplicable)
            .IsRequired();

        // AC-P4.38 — a RATE, not money: deliberately kept at (5,2) against the model-wide
        // HavePrecision(18,3) convention. A convention that silently widened a VAT rate would be worse than
        // the drift it fixes, so the explicit annotation is retained on purpose. verify-schema asserts this
        // column is NOT (18,3) — widening it is reported as drift in the other direction.
        builder.Property(i => i.VatRate)
            .HasColumnType("decimal(5,2)");

        builder.Property(i => i.StampDutyAmount);

        builder.Property(i => i.CancellationReason)
            .HasMaxLength(1000);

        builder.Property(i => i.TotalHt);
        builder.Property(i => i.TotalVat);
        builder.Property(i => i.TotalTtc);
        builder.Property(i => i.AmountCollected);

        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt);

        // Aggregate children: cascade-deleted with the invoice (a draft delete removes its lines).
        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Metadata.SetField-free backing fields: EF uses the private List<> backing fields by convention.
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => new { i.ClinicId, i.IssueDate });
        builder.HasIndex(i => i.PatientId);

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
