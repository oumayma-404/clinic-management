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

        // Denormalised from the patient so this clinical child carries a global query filter of its
        // own — see ApplicationDbContext.OnModelCreating. The two must agree; verify-schema's
        // clinical-child-clinic-matches-patient is what holds that.
        builder.Property(f => f.ClinicId)
            .IsRequired();

        builder.HasIndex(f => f.ClinicId);

        builder.Property(f => f.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.StorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.FolderId);

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

        builder.HasOne(f => f.Folder)
            .WithMany(folder => folder.Files)
            .HasForeignKey(f => f.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}



