using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .HasMaxLength(1000);

        builder.Property(s => s.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Unit)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.CurrentStock)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.MinimumStockLevel)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.MaximumStockLevel)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.UnitPrice)
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.Supplier)
            .HasMaxLength(200);

        builder.Property(s => s.ExpiryDate);

        builder.Property(s => s.BatchNumber)
            .HasMaxLength(100);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);
    }
}



