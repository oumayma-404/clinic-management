using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DentalRecordActConfiguration : IEntityTypeConfiguration<DentalRecordAct>
{
    public void Configure(EntityTypeBuilder<DentalRecordAct> builder)
    {
        builder.ToTable("DentalRecordActs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.DentalRecordId)
            .IsRequired();

        builder.Property(a => a.ProcedureTypeId);

        builder.Property(a => a.ProcedureName)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(a => a.Cost)
            .HasColumnType("decimal(18,3)");

        // FDI tooth numbers as a JSON int array (mirrors TreatmentPlanItem.ToothNumbers).
        builder.Property(a => a.ToothNumbers)
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

        builder.Property(a => a.ResultingCondition)
            .HasConversion<int?>();

        builder.Property(a => a.Surfaces)
            .HasMaxLength(5);

        builder.Property(a => a.Note)
            .HasMaxLength(1000);

        builder.Property(a => a.CreatedAt)
            .IsRequired();
    }
}
