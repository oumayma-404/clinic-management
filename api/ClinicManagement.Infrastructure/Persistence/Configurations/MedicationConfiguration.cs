using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("Medications");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.BrandName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.Form)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Strength)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.IsActive)
            .IsRequired();

        builder.Property(m => m.IsProvisional)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.UpdatedAt);

        // Non-unique — brand + strength + form uniqueness is enforced by the handler (BrandExistsAsync),
        // this index just speeds the picker's brand-ordered reads.
        builder.HasIndex(m => m.BrandName);

        builder.HasMany(m => m.ActiveIngredients)
            .WithOne(i => i.Medication)
            .HasForeignKey(i => i.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
