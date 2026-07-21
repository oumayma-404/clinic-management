using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class MedicationActiveIngredientConfiguration : IEntityTypeConfiguration<MedicationActiveIngredient>
{
    public void Configure(EntityTypeBuilder<MedicationActiveIngredient> builder)
    {
        builder.ToTable("MedicationActiveIngredients");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.MedicationId)
            .IsRequired();

        builder.Property(i => i.Dci)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.CreatedAt)
            .IsRequired();

        builder.HasOne(i => i.Medication)
            .WithMany(m => m.ActiveIngredients)
            .HasForeignKey(i => i.MedicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // A molecule appears at most once per medication.
        builder.HasIndex(i => new { i.MedicationId, i.Dci })
            .IsUnique();
    }
}
