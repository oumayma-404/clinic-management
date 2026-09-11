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

        builder.Property(a => a.Cost);

        // Pricing provenance: the unit price the total was built from, and whether it was multiplied by teeth.
        builder.Property(a => a.UnitCost);

        builder.Property(a => a.IsPerTooth)
            .IsRequired();

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

        // Which of those teeth are pontiques — same JSON int array, same comparer, for the same reason.
        // Additive and nullable-by-absence: every existing row deserialises to an empty list, which is exactly
        // « this act is not a bridge, or nobody said which tooth is which », so no backfill exists or is needed.
        builder.Property(a => a.PonticToothNumbers)
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

        // And which are piliers on an implant — the third role, third identical mapping. Same additive,
        // empty-by-absence contract as the pontique list above.
        builder.Property(a => a.ImplantPilierToothNumbers)
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

        // « Acte non terminé ». Additive with a false default, so every row written before this reads as
        // « finished » — which is what those séances meant, and is the one answer that claims nothing.
        builder.Property(a => a.IsUnfinished)
            .IsRequired()
            .HasDefaultValue(false);

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
