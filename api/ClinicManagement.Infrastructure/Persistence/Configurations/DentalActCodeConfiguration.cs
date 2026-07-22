using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DentalActCodeConfiguration : IEntityTypeConfiguration<DentalActCode>
{
    public void Configure(EntityTypeBuilder<DentalActCode> builder)
    {
        builder.ToTable("DentalActCodes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.CodeActe)
            .IsRequired()
            .HasMaxLength(50);

        // Global catalog (no ClinicId) — the code acte is unique across the whole catalog.
        builder.HasIndex(e => e.CodeActe)
            .IsUnique();

        builder.Property(e => e.DesignationFr)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(e => e.LettreCle)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.Coefficient)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.DefaultFee)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.RequiresAccordPrealable)
            .IsRequired();

        builder.Property(e => e.IsActive)
            .IsRequired();

        builder.Property(e => e.IsProvisional)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);
    }
}
