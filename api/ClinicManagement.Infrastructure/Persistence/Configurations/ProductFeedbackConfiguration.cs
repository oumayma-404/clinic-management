using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// « Retours » (guided-first-days E). Cascades with its cabinet; the author is a plain id, not a foreign key, so a
/// message outlives the account that wrote it (its name is read at query time and simply goes blank).
/// </summary>
public class ProductFeedbackConfiguration : IEntityTypeConfiguration<ProductFeedback>
{
    public void Configure(EntityTypeBuilder<ProductFeedback> builder)
    {
        builder.ToTable("ProductFeedback");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(f => f.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(f => f.UserId).IsRequired().HasMaxLength(255);
        builder.Property(f => f.AuthorRole).IsRequired().HasMaxLength(50);
        builder.Property(f => f.Kind).IsRequired().HasMaxLength(20);
        builder.Property(f => f.Source).IsRequired().HasMaxLength(20);
        builder.Property(f => f.LikedAreasCsv).HasMaxLength(400);
        builder.Property(f => f.HardAreasCsv).HasMaxLength(400);
        builder.Property(f => f.Message).HasMaxLength(ProductFeedback.MaxMessageLength);
        builder.Property(f => f.FromScreen).HasMaxLength(ProductFeedback.MaxScreenLength);
        builder.Property(f => f.CreatedAt).IsRequired();

        // The console reads newest first, optionally by kind.
        builder.HasIndex(f => f.CreatedAt);
        builder.HasIndex(f => new { f.ClinicId, f.CreatedAt });
    }
}
