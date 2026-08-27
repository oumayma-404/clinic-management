using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientMedicalHistoryConfiguration : IEntityTypeConfiguration<PatientMedicalHistory>
{
    public void Configure(EntityTypeBuilder<PatientMedicalHistory> builder)
    {
        builder.ToTable("PatientMedicalHistories");

        builder.HasKey(mh => mh.Id);

        builder.Property(mh => mh.PatientId)
            .IsRequired();

        // Denormalised from the patient so this clinical child carries a global query filter of its
        // own — see ApplicationDbContext.OnModelCreating. The two must agree; verify-schema's
        // clinical-child-clinic-matches-patient is what holds that.
        builder.Property(mh => mh.ClinicId)
            .IsRequired();

        builder.HasIndex(mh => mh.ClinicId);

        builder.Property(mh => mh.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(mh => mh.Date)
            .IsRequired(false);

        builder.Property(mh => mh.Notes)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(mh => mh.CreatedAt)
            .IsRequired();

        builder.Property(mh => mh.UpdatedAt)
            .IsRequired(false);

        // Relationship with Patient
        builder.HasOne(mh => mh.Patient)
            .WithMany(p => p.MedicalHistoryEntries)
            .HasForeignKey(mh => mh.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}










