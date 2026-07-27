using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.InvoiceId)
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,3)");

        builder.Property(p => p.Method)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.PaidOn)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // Void marker (data-and-money-integrity). The row is kept and flagged, never deleted, so a correction
        // leaves a trail.
        builder.Property(p => p.IsVoided)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.VoidedAt);

        builder.Property(p => p.VoidReason)
            .HasMaxLength(1000);

        // Soft link, no FK: User.Id is a string and users get deactivated — the trail must outlive them.
        builder.Property(p => p.VoidedByUserId)
            .HasMaxLength(200);

        builder.Property(p => p.VoidedByName)
            .HasMaxLength(200);

        // Soft link to the installment payment this was carried over from (devis→facture bridge).
        builder.Property(p => p.SourceInstallmentPaymentId);

        builder.HasIndex(p => p.InvoiceId);

        // The caisse sums payments by date and must skip voided rows; a partial index keeps that read cheap
        // and stops it degrading as voids accumulate.
        builder.HasIndex(p => new { p.InvoiceId, p.PaidOn })
            .HasFilter("NOT \"IsVoided\"");
    }
}
