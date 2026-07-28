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

        builder.Property(v => v.ClinicId)
            .IsRequired();

        builder.Property(v => v.LettreCle)
            .IsRequired()
            .HasMaxLength(10);

        // Per-clinic VLC set (#5): one value per lettre clé WITHIN a clinic.
        builder.HasIndex(v => new { v.ClinicId, v.LettreCle })
            .IsUnique();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(v => v.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(v => v.Value);

        builder.Property(v => v.IsProvisional)
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .IsRequired();

        builder.Property(v => v.UpdatedAt);
    }
}
