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

        builder.Property(pt => pt.ClinicId)
            .IsRequired();

        builder.Property(pt => pt.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Names are unique per clinic (tenant-scoped) — not globally — so two clinics can each
        // define e.g. "Détartrage" independently.
        builder.HasIndex(pt => new { pt.ClinicId, pt.Name })
            .IsUnique();

        builder.Property(pt => pt.DefaultDurationMinutes)
            .IsRequired();

        // Millimes (3 decimals) — the catalog price seeds a dental act's unit cost, so it must carry the
        // same precision as the act it prefills.
        builder.Property(pt => pt.DefaultCost);

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

        builder.Property(pt => pt.ResultingCondition)
            .HasConversion<int?>();

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

        // The material list is a backing-field collection (AC-P4.9); the public surface is IReadOnlyCollection,
        // so EF has to be told to go through the field. Its FKs live in ProcedureTypeMaterialConfiguration.
        builder.Metadata
            .FindNavigation(nameof(ProcedureType.Materials))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}


