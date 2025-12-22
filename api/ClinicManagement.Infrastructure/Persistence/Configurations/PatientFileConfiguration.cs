using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientFileConfiguration : IEntityTypeConfiguration<PatientFile>
{
    public void Configure(EntityTypeBuilder<PatientFile> builder)
    {
        builder.ToTable("PatientFiles");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .ValueGeneratedNever();

        builder.Property(f => f.PatientId)
            .IsRequired();

        builder.Property(f => f.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(f => f.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(f => f.FileSize)
            .IsRequired();

        builder.Property(f => f.FileType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(f => f.Description)
            .HasMaxLength(1000);

        builder.Property(f => f.UploadedBy)
            .HasMaxLength(200);

        builder.Property(f => f.UploadedAt)
            .IsRequired();

        builder.HasOne(f => f.Patient)
            .WithMany(p => p.Files)
            .HasForeignKey(f => f.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}



