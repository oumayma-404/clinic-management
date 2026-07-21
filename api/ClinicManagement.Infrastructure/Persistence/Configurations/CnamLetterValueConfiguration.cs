using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class CnamLetterValueConfiguration : IEntityTypeConfiguration<CnamLetterValue>
{
    public void Configure(EntityTypeBuilder<CnamLetterValue> builder)
    {
        builder.ToTable("CnamLetterValues");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .ValueGeneratedNever();

        builder.Property(v => v.LettreCle)
            .IsRequired()
            .HasMaxLength(10);

        // Global VLC set — one value per lettre clé.
        builder.HasIndex(v => v.LettreCle)
            .IsUnique();

        builder.Property(v => v.Value)
            .HasColumnType("decimal(18,3)");

        builder.Property(v => v.IsProvisional)
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        builder.Property(v => v.UpdatedAt);
    }
}
