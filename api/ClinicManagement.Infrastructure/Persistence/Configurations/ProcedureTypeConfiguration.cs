using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class ProcedureTypeConfiguration : IEntityTypeConfiguration<ProcedureType>
{
    public void Configure(EntityTypeBuilder<ProcedureType> builder)
    {
        builder.ToTable("ProcedureTypes");

        builder.HasKey(pt => pt.Id);

        builder.Property(pt => pt.Id)
            .ValueGeneratedNever();

        builder.Property(pt => pt.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(pt => pt.Name)
            .IsUnique();

        builder.Property(pt => pt.DefaultDurationMinutes)
            .IsRequired();

        builder.Property(pt => pt.DefaultCost)
            .HasColumnType("decimal(18,2)");

        // Configure ColorHex as value object
        builder.OwnsOne(pt => pt.Color, colorBuilder =>
        {
            colorBuilder.Property(c => c.Value)
                .HasColumnName("ColorHex")
                .IsRequired()
                .HasMaxLength(7);
        });

        builder.Property(pt => pt.Description)
            .HasMaxLength(1000);

        builder.Property(pt => pt.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(pt => pt.CreatedAt)
            .IsRequired();

        builder.Property(pt => pt.UpdatedAt);

        // Configure relationship with Appointments
        builder.HasMany(pt => pt.Appointments)
            .WithOne(a => a.ProcedureType)
            .HasForeignKey(a => a.ProcedureTypeId)
            .OnDelete(DeleteBehavior.SetNull); // Set null if procedure type is deleted
    }
}


