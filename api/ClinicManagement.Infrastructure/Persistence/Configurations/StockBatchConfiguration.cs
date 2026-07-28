using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// One received lot of a stock item (AC-P4.1). A child of <see cref="StockItem"/>, so it carries no
/// <c>ClinicId</c> of its own and no query filter — it is only ever reached through its filtered parent, the
/// same shape as <c>InvoiceLine</c>/<c>Installment</c>.
/// </summary>
public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("StockBatches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.StockItemId).IsRequired();
        builder.Property(b => b.ReceivedQuantity).IsRequired();
        builder.Property(b => b.RemainingQuantity).IsRequired();
        builder.Property(b => b.ExpiryDate);
        builder.Property(b => b.BatchNumber).HasMaxLength(100);
        builder.Property(b => b.ReceivedAt).IsRequired();

        // Cascade: a lot has no meaning without its item, exactly like an invoice line without its invoice.
        builder.HasOne<StockItem>()
            .WithMany(s => s.Batches)
            .HasForeignKey(b => b.StockItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // FEFO reads the batches of one item ordered by expiry — that is this index, and it is also what the
        // expiry surfacing (earliest relevant expiry) and the approaching-expiry sweep both scan.
        builder.HasIndex(b => new { b.StockItemId, b.ExpiryDate });
    }
}
