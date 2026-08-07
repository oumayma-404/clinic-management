using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.ClinicId).IsRequired();
        // Soft link to the invoice (no FK navigation, matching the app's soft-link convention).
        builder.Property(c => c.InvoiceId).IsRequired();

        builder.Property(c => c.Number).IsRequired().HasMaxLength(20);
        // The avoir number is unique per clinic (its own gapless per-clinic-per-year sequence); also the
        // concurrency backstop for two simultaneous creations computing the same sequence.
        builder.HasIndex(c => new { c.ClinicId, c.Number }).IsUnique();

        builder.Property(c => c.IssueDate).IsRequired();
        builder.Property(c => c.Amount);
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.Method).HasConversion<int>();
        builder.Property(c => c.RefundedOn).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.InvoiceId);
        builder.HasIndex(c => new { c.ClinicId, c.RefundedOn });
    }
}
