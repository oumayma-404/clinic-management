using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .ValueGeneratedNever();

        builder.Property(l => l.InvoiceId)
            .IsRequired();

        builder.Property(l => l.Designation)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.Quantity)
            .IsRequired();

        builder.Property(l => l.UnitPriceHt)
            .HasColumnType("decimal(18,3)");

        builder.Property(l => l.LineTotalHt)
            .HasColumnType("decimal(18,3)");

        // Optional soft link to the billed dental record (no FK constraint — mirrors the header link) —
        // read by the "already invoiced" guard so a multi-record honoraires marks each record invoiced.
        builder.Property(l => l.DentalRecordId);

        builder.HasIndex(l => l.InvoiceId);
        builder.HasIndex(l => l.DentalRecordId);
    }
}
