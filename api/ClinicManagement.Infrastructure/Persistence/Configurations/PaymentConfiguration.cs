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

        builder.Property(p => p.Amount);

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

        // Cheque identity (L8). Null for every method but Cheque — the invariant lives in `ChequeDetails.For`,
        // not here: a CHECK constraint could express it, but it would then be a second copy of a rule the domain
        // already enforces, and the one that fires would produce a 500 rather than the French refusal.
        builder.Property(p => p.ChequeNumber)
            .HasMaxLength(50);

        builder.Property(p => p.ChequeBankName)
            .HasMaxLength(200);

        builder.Property(p => p.ChequeDueDate);

        // Banked mark (Group B). Same three shapes and the same widths as the void trail above, because it is the
        // same kind of record: a moment, a soft actor link, and a name snapshot. Null for every held cheque and
        // for every payment that is not one — the invariant lives in `ChequeBankedStamp.For`, verified by
        // `verify-schema` rather than restated as a CHECK constraint, exactly like its sibling.
        builder.Property(p => p.ChequeBankedOn);

        builder.Property(p => p.ChequeBankedByUserId)
            .HasMaxLength(200);

        builder.Property(p => p.ChequeBankedByName)
            .HasMaxLength(200);

        builder.HasIndex(p => p.InvoiceId);

        // The caisse sums payments by date and must skip voided rows; a partial index keeps that read cheap
        // and stops it degrading as voids accumulate.
        builder.HasIndex(p => new { p.InvoiceId, p.PaidOn })
            .HasFilter("NOT \"IsVoided\"");

        // « Chèques à encaisser », ordered by the day they may be banked.
        //
        // ⚠️ The filter is `ChequeDueDate IS NOT NULL`, deliberately **not** `Method = 1`. By the domain
        // invariant only a cheque can carry a due date, so the two are equally selective — and the enum form
        // would bake `PaymentMethod.Cheque`'s ordinal into SQL, a magic number in the one place no compiler
        // checks it.
        builder.HasIndex(p => p.ChequeDueDate)
            .HasFilter("\"ChequeDueDate\" IS NOT NULL AND NOT \"IsVoided\"");
    }
}
