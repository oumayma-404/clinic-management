using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ClinicId).IsRequired();
        builder.Property(m => m.StockItemId).IsRequired();
        builder.Property(m => m.Type).IsRequired().HasConversion<int>();
        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.ResultingStock).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasIndex(m => new { m.StockItemId, m.CreatedAt });
    }
}
