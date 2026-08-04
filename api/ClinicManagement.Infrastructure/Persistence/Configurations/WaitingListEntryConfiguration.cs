using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class WaitingListEntryConfiguration : IEntityTypeConfiguration<WaitingListEntry>
{
    public void Configure(EntityTypeBuilder<WaitingListEntry> builder)
    {
        builder.ToTable("WaitingListEntries");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .ValueGeneratedNever();

        builder.Property(w => w.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(w => w.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(w => w.PatientId)
            .IsRequired();

        builder.HasOne(w => w.Patient)
            .WithMany()
            .HasForeignKey(w => w.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Clinic + status is the salle d'attente query shape (a clinic's active waiting entries).
        builder.HasIndex(w => new { w.ClinicId, w.Status });
        builder.HasIndex(w => w.PatientId);

        // L9 — a REAL FK, where this was a bare `Property` for the whole life of the product. The spec names it
        // as the illustration of what a bare Guid buys you: nothing prevented an id from another clinic, nothing
        // prevented one pointing at a deleted practitioner, and « le praticien souhaité » could therefore be a
        // value no screen could resolve to a name.
        //
        // ⚠️ There is no navigation property on the entity, so the FK is declared by shadow-type overload
        // (`HasOne<Doctor>()`). Adding one would put an EF-only reference on an aggregate that has no clinical need
        // to load a practitioner — the read resolves names in one batch, exactly as it did before.
        //
        // `SetNull` for the same reason as its three siblings: removing a dentist must not delete somebody's place
        // in the queue, it must merely forget their preference.
        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(w => w.PreferredDoctorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(w => w.PreferredDoctorId);

        builder.Property(w => w.Priority)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(w => w.DesiredTimeframe)
            .HasMaxLength(200);

        builder.Property(w => w.Note)
            .HasMaxLength(1000);

        builder.Property(w => w.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(w => w.ResultingAppointmentId);

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.UpdatedAt);
    }
}
