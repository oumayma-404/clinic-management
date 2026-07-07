using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MedicalDocumentConfiguration : IEntityTypeConfiguration<MedicalDocument>
{
    public void Configure(EntityTypeBuilder<MedicalDocument> builder)
    {
        builder.ToTable("MedicalDocuments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.PatientId)
            .IsRequired();

        builder.Property(d => d.DocumentType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(d => d.DocumentDate)
            .IsRequired();

        builder.Property(d => d.PatientName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.PatientAge)
            .HasMaxLength(20);

        builder.Property(d => d.RecipientDoctorName)
            .HasMaxLength(200);

        builder.Property(d => d.RecipientDoctorSpecialty)
            .HasMaxLength(100);

        builder.Property(d => d.ContentJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(d => d.ClinicName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.ClinicAddress)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(d => d.ClinicPhone)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(d => d.DoctorName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.DoctorSpecialty)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt);

        // Relationships
        builder.HasOne(d => d.Patient)
            .WithMany()
            .HasForeignKey(d => d.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}








