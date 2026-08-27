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

        builder.Property(s => s.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.ClinicId);

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

        // UnitPrice carries NO explicit HasColumnType: it is governed by the model-wide
        // `HavePrecision(18,3)` convention (AC-P4.36/4.37). It was the one money column left at
        // `decimal(18,2)`, silently truncating the millime on every Tunisian price.
        builder.Property(s => s.UnitPrice);

        // The free-text `Supplier` string is gone: it named somebody nobody could call. `ON DELETE RESTRICT` is
        // the backstop only — AC-4 is enforced in DeleteSupplierCommand, which names the article count in French;
        // reaching the constraint would surface as a 500 instead.
        builder.Property(s => s.SupplierId);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.SupplierId);

        // ExpiryDate / BatchNumber are gone — they now live per lot on StockBatch (AC-P4.1), because AddStock
        // overwrote them and a second delivery destroyed the first lot's date.
        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        // The lots are a backing-field collection on the aggregate (AC-P4.1) — EF needs telling, since the
        // public surface is an IReadOnlyCollection.
        builder.Metadata
            .FindNavigation(nameof(StockItem.Batches))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}



