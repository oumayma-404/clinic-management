using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DentalRecordToothConfiguration : IEntityTypeConfiguration<DentalRecordTooth>
{
    public void Configure(EntityTypeBuilder<DentalRecordTooth> builder)
    {
        builder.ToTable("DentalRecordTeeth");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.DentalRecordId)
            .IsRequired();

        builder.Property(t => t.ToothNumber)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        // Relationship with DentalRecord
        builder.HasOne(t => t.DentalRecord)
            .WithMany(dr => dr.Teeth)
            .HasForeignKey(t => t.DentalRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for faster lookups
        builder.HasIndex(t => new { t.DentalRecordId, t.ToothNumber })
            .IsUnique();
    }
}









