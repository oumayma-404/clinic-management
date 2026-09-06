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

        // Cheque identity (L8) — same three columns, same widths and same reasoning as `PaymentConfiguration`.
        builder.Property(p => p.ChequeNumber)
            .HasMaxLength(50);

        builder.Property(p => p.ChequeBankName)
            .HasMaxLength(200);

        builder.Property(p => p.ChequeDueDate);

        // Banked mark (Group B) — same three columns, same widths and same reasoning as `PaymentConfiguration`.
        builder.Property(p => p.ChequeBankedOn);

        builder.Property(p => p.ChequeBankedByUserId)
            .HasMaxLength(200);

        builder.Property(p => p.ChequeBankedByName)
            .HasMaxLength(200);

        /*
         * The fiche this money was handed over at — a SOFT reference, with no FK, deliberately.
         *
         * `TreatmentPlanItemStep.LinkedDentalRecordId` is the same shape for the same reason: a fiche and a
         * treatment are separate aggregates, and a cascade either way would be wrong in both directions — a
         * deleted fiche must not take a receipt with it, and a collected payment must not stop a clinical record
         * from being corrected. Nullable, and null means « not collected on a fiche » rather than « unknown »,
         * so no backfill: every row that predates the column really was taken at the till or on the échéancier.
         */
        builder.Property(p => p.DentalRecordId);

        builder.HasIndex(p => p.InstallmentId);

        // What `TreatmentPlan.CollectedOnRecord` reads on every re-save of a fiche carrying a treatment act, to
        // post the difference rather than the amount. Filtered to the rows that have one: chairside collection is
        // a minority of receipts, and an unfiltered index over mostly-NULL is the larger, less useful one.
        builder.HasIndex(p => p.DentalRecordId)
            .HasFilter("\"DentalRecordId\" IS NOT NULL");

        // The index that makes the fixed monthly cash read cheap. Installments previously had no date index
        // at all, so the old (wrong) LastPaidOn query already scanned — this must not repeat that.
        builder.HasIndex(p => p.PaidOn)
            .HasFilter("NOT \"IsVoided\"");

        // « Chèques à encaisser » must see BOTH ledgers, so the index exists on both. See `PaymentConfiguration`
        // for why the filter keys on the due date rather than on the method's ordinal.
        builder.HasIndex(p => p.ChequeDueDate)
            .HasFilter("\"ChequeDueDate\" IS NOT NULL AND NOT \"IsVoided\"");
    }
}
