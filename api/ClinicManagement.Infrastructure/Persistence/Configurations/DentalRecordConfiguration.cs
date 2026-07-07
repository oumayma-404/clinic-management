using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DentalRecordConfiguration : IEntityTypeConfiguration<DentalRecord>
{
    public void Configure(EntityTypeBuilder<DentalRecord> builder)
    {
        builder.ToTable("DentalRecords");

        builder.HasKey(dr => dr.Id);

        builder.Property(dr => dr.Id)
            .ValueGeneratedNever();

        builder.Property(dr => dr.PatientId)
            .IsRequired();

        builder.Property(dr => dr.InterventionDate)
            .IsRequired();

        builder.Property(dr => dr.ProcedureType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(dr => dr.Cost)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(dr => dr.AmountPaid)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(dr => dr.Notes)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null!),
                v => string.IsNullOrWhiteSpace(v) 
                    ? new List<string>() 
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null!) ?? new List<string>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                    c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
                    c => c != null ? c.ToList() : new List<string>()))
            .HasColumnType("text");

        builder.Property(dr => dr.ImportantNotes)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null!),
                v => string.IsNullOrWhiteSpace(v) 
                    ? new List<string>() 
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null!) ?? new List<string>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<string>>(
                    (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                    c => c != null ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())) : 0,
                    c => c != null ? c.ToList() : new List<string>()))
            .HasColumnType("text");

        builder.Property(dr => dr.IsAdultTeeth)
            .IsRequired();

        builder.Property(dr => dr.CreatedAt)
            .IsRequired();

        builder.Property(dr => dr.UpdatedAt);

        // Relationship with Patient
        builder.HasOne(dr => dr.Patient)
            .WithMany()
            .HasForeignKey(dr => dr.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Relationship with Teeth
        builder.HasMany(dr => dr.Teeth)
            .WithOne(t => t.DentalRecord)
            .HasForeignKey(t => t.DentalRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

