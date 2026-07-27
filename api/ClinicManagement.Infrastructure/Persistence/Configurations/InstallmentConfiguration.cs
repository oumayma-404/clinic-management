using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InstallmentConfiguration : IEntityTypeConfiguration<Installment>
{
    public void Configure(EntityTypeBuilder<Installment> builder)
    {
        builder.ToTable("Installments");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.TreatmentPlanId)
            .IsRequired();

        builder.Property(i => i.DueDate)
            .IsRequired();

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.AmountPaid)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.LastMethod)
            .HasConversion<int>();

        builder.Property(i => i.LastPaidOn);

        // The payment ledger. Installment is now the first entity in this codebase that is both a child (of
        // TreatmentPlan) and a parent. The relationship is declared HERE ONLY — never also on the child side,
        // which is the mistake that silently overrode Patient→Appointment's SetNull with Cascade.
        builder.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => p.InstallmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // « Créances » pulls every open installment per clinic and there was no date index at all.
        builder.HasIndex(i => i.DueDate);
    }
}
