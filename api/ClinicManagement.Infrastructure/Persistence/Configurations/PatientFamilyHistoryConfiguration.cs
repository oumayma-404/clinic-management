using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientFamilyHistoryConfiguration : IEntityTypeConfiguration<PatientFamilyHistory>
{
    public void Configure(EntityTypeBuilder<PatientFamilyHistory> builder)
    {
        builder.ToTable("PatientFamilyHistories");

        builder.HasKey(fh => fh.Id);

        builder.Property(fh => fh.PatientId)
            .IsRequired();

        // Denormalised from the patient so this clinical child carries a global query filter of its
        // own — see ApplicationDbContext.OnModelCreating. The two must agree; verify-schema's
        // clinical-child-clinic-matches-patient is what holds that.
        builder.Property(fh => fh.ClinicId)
            .IsRequired();

        builder.HasIndex(fh => fh.ClinicId);

        builder.Property(fh => fh.Relationship)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(fh => fh.Condition)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(fh => fh.Notes)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(fh => fh.CreatedAt)
            .IsRequired();

        builder.Property(fh => fh.UpdatedAt)
            .IsRequired(false);

        // Relationship with Patient
        builder.HasOne(fh => fh.Patient)
            .WithMany(p => p.FamilyHistoryEntries)
            .HasForeignKey(fh => fh.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}










