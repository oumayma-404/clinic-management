using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> builder)
    {
        builder.ToTable("TreatmentPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.ClinicId)
            .IsRequired();

        builder.Property(p => p.PatientId)
            .IsRequired();

        builder.Property(p => p.Number)
            .HasMaxLength(20);

        // Devis number is unique per clinic (gapless per-clinic-per-year, separate from invoices). Filtered
        // to non-null so multiple drafts (Number == null) coexist; also the concurrency backstop on accept.
        builder.HasIndex(p => new { p.ClinicId, p.Number })
            .IsUnique()
            .HasFilter("\"Number\" IS NOT NULL");

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Notes)
            .HasMaxLength(2000);

        builder.Property(p => p.AcceptedDate);

        builder.Property(p => p.CancellationReason)
            .HasMaxLength(1000);

        builder.Property(p => p.TotalPlanned)
            .HasColumnType("decimal(18,3)");

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Aggregate children: cascade-deleted with the plan (a draft delete removes its items/installments).
        builder.HasMany(p => p.Items)
            .WithOne()
            .HasForeignKey(i => i.TreatmentPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Installments)
            .WithOne()
            .HasForeignKey(i => i.TreatmentPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Installments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.ClinicId, p.PatientId });
    }
}
