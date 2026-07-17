using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.ClinicId)
            .IsRequired();

        builder.Property(i => i.PatientId)
            .IsRequired();

        builder.Property(i => i.DentalRecordId);
        builder.Property(i => i.AppointmentId);

        builder.Property(i => i.Number)
            .HasMaxLength(20);

        // The fiscal number is unique per clinic (gapless per-clinic-per-year sequence). Filtered to
        // non-null so multiple drafts (Number == null) coexist; also the concurrency backstop for two
        // simultaneous issues computing the same sequence (unique violation → handler retries).
        builder.HasIndex(i => new { i.ClinicId, i.Number })
            .IsUnique()
            .HasFilter("\"Number\" IS NOT NULL");

        builder.Property(i => i.IssueDate);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(i => i.VatApplicable)
            .IsRequired();

        builder.Property(i => i.VatRate)
            .HasColumnType("decimal(5,2)");

        builder.Property(i => i.StampDutyAmount)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.CancellationReason)
            .HasMaxLength(1000);

        builder.Property(i => i.TotalHt).HasColumnType("decimal(18,3)");
        builder.Property(i => i.TotalVat).HasColumnType("decimal(18,3)");
        builder.Property(i => i.TotalTtc).HasColumnType("decimal(18,3)");
        builder.Property(i => i.AmountCollected).HasColumnType("decimal(18,3)");

        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt);

        // Aggregate children: cascade-deleted with the invoice (a draft delete removes its lines).
        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Metadata.SetField-free backing fields: EF uses the private List<> backing fields by convention.
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => new { i.ClinicId, i.IssueDate });
        builder.HasIndex(i => i.PatientId);
    }
}
