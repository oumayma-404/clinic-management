using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mirrors <see cref="PaymentConfiguration"/>. Note what is <b>absent</b>: the relationship to
/// <see cref="Installment"/> is declared once, on the parent side in <c>InstallmentConfiguration</c> — never
/// from both, which is exactly the bug that made a patient delete cascade away their appointments.
///
/// There is deliberately no <c>DbSet</c> either: like every other aggregate child in this codebase
/// (<c>Payment</c>, <c>Installment</c>, <c>InvoiceLine</c>, <c>TreatmentPlanItem</c>), it is reached only
/// through its clinic-filtered root, which is what keeps it tenant-scoped.
/// </summary>
public class InstallmentPaymentConfiguration : IEntityTypeConfiguration<InstallmentPayment>
{
    public void Configure(EntityTypeBuilder<InstallmentPayment> builder)
    {
        builder.ToTable("InstallmentPayments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.InstallmentId)
            .IsRequired();

        builder.Property(p => p.Amount);

        builder.Property(p => p.Method)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.PaidOn)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.IsVoided)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.VoidedAt);

        builder.Property(p => p.VoidReason)
            .HasMaxLength(1000);

        builder.Property(p => p.VoidedByUserId)
            .HasMaxLength(200);

        builder.Property(p => p.VoidedByName)
            .HasMaxLength(200);

        builder.HasIndex(p => p.InstallmentId);

        // The index that makes the fixed monthly cash read cheap. Installments previously had no date index
        // at all, so the old (wrong) LastPaidOn query already scanned — this must not repeat that.
        builder.HasIndex(p => p.PaidOn)
            .HasFilter("NOT \"IsVoided\"");
    }
}
