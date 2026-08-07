using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class ToothStateConfiguration : IEntityTypeConfiguration<ToothState>
{
    public void Configure(EntityTypeBuilder<ToothState> builder)
    {
        builder.ToTable("ToothStates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.PatientId)
            .IsRequired();

        // Denormalised from the patient so this clinical child carries a global query filter of its
        // own — see ApplicationDbContext.OnModelCreating. The two must agree; verify-schema's
        // clinical-child-clinic-matches-patient is what holds that.
        builder.Property(t => t.ClinicId)
            .IsRequired();

        builder.HasIndex(t => t.ClinicId);

        builder.Property(t => t.ToothNumber)
            .IsRequired();

        // Many treatments per tooth are allowed (across records/sessions) — a non-unique lookup index.
        builder.HasIndex(t => new { t.PatientId, t.ToothNumber });

        builder.Property(t => t.Condition)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(t => t.Source)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(t => t.Surfaces)
            .HasMaxLength(5);

        builder.Property(t => t.Note)
            .HasMaxLength(1000);

        builder.Property(t => t.DentalRecordId);

        builder.Property(t => t.TreatmentDate)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        // Child-of-patient: cascade-deleted with the patient (mirrors DentalRecord).
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(t => t.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sourced by a dental record — deleting that record removes the tooth entries it produced.
        builder.HasOne<DentalRecord>()
            .WithMany()
            .HasForeignKey(t => t.DentalRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.DentalRecordId);
    }
}
