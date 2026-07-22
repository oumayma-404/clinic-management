using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class TreatmentPlanItemConfiguration : IEntityTypeConfiguration<TreatmentPlanItem>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanItem> builder)
    {
        builder.ToTable("TreatmentPlanItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.TreatmentPlanId)
            .IsRequired();

        builder.Property(i => i.DentalActCodeId);

        builder.Property(i => i.CodeActe)
            .HasMaxLength(50);

        builder.Property(i => i.DesignationFr)
            .IsRequired()
            .HasMaxLength(300);

        // FDI tooth numbers stored as a JSON int array (mirrors DentalRecord.Notes JSON conversion).
        builder.Property(i => i.ToothNumbers)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null!),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<int>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null!) ?? new List<int>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<int>>(
                    (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                    c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
                    c => c != null ? c.ToList() : new List<int>()))
            .HasColumnType("text");

        builder.Property(i => i.PlannedCost)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(i => i.DoneDate);
        builder.Property(i => i.LinkedDentalRecordId);
    }
}
