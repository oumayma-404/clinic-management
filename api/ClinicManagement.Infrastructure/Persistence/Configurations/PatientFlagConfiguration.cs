using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientFlagConfiguration : IEntityTypeConfiguration<PatientFlag>
{
    public void Configure(EntityTypeBuilder<PatientFlag> builder)
    {
        builder.ToTable("PatientFlags");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .ValueGeneratedNever();

        builder.Property(f => f.PatientId)
            .IsRequired();

        builder.Property(f => f.FlagType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(f => f.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.Notes)
            .HasColumnType("text");

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasOne(f => f.Patient)
            .WithMany(p => p.Flags)
            .HasForeignKey(f => f.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}



