using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.ClinicId)
            .IsRequired();

        builder.Property(d => d.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Specialty)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Phone)
            .HasMaxLength(50);

        builder.Property(d => d.Email)
            .HasMaxLength(200);

        builder.Property(d => d.UserId)
            .HasMaxLength(200);

        builder.Property(d => d.CodeProfessionnelSante)
            .HasMaxLength(50);

        builder.Property(d => d.OrdreNumberCnomdt)
            .HasMaxLength(50);

        builder.Property(d => d.CachetStorageKey)
            .HasMaxLength(400);

        builder.Property(d => d.CachetContentType)
            .HasMaxLength(100);

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt);

        builder.HasOne(d => d.Clinic)
            .WithMany()
            .HasForeignKey(d => d.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.ClinicId);
        builder.HasIndex(d => d.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");
    }
}


