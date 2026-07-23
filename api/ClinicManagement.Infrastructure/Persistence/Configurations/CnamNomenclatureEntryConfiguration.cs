using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class CnamNomenclatureEntryConfiguration : IEntityTypeConfiguration<CnamNomenclatureEntry>
{
    public void Configure(EntityTypeBuilder<CnamNomenclatureEntry> builder)
    {
        builder.ToTable("CnamNomenclatureEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ClinicId)
            .IsRequired();

        builder.Property(e => e.CodeActe)
            .IsRequired()
            .HasMaxLength(50);

        // Per-clinic catalog (#5): the code acte is unique WITHIN a clinic — each clinic owns its own copy.
        builder.HasIndex(e => new { e.ClinicId, e.CodeActe })
            .IsUnique();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.Property(e => e.IsActive)
            .IsRequired();

        builder.Property(e => e.IsProvisional)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);
    }
}
