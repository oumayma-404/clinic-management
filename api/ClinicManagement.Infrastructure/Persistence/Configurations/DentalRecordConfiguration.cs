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

        // Millimes (3 decimals), matching DentalRecordAct.Cost and every other money column — a 2-decimal
        // column silently rounded the derived total away from the sum of its acts.
        builder.Property(dr => dr.Cost)
            .IsRequired();

        builder.Property(dr => dr.AmountPaid)
            .IsRequired();

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

        // The appointment this fiche documents. Column + index have existed since AddDentalRecordAppointmentId
        // (2026-07-17); only the model never declared them, so nothing populated the column and the next
        // `migrations add` would have emitted DropColumn for it.
        //
        // Mapped as a bare property with a bare index and deliberately NO `HasOne`/`HasForeignKey`: PostgreSQL has no
        // FK constraint here (verified against pg_constraint), and declaring a relationship would make the model
        // claim one the catalog does not have — drift in the opposite direction, which `verify-schema` would flag.
        // A soft link is also the right semantics: deleting an appointment must not cascade into clinical records.
        builder.Property(dr => dr.AppointmentId);

        builder.HasIndex(dr => dr.AppointmentId);

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

        // Relationship with Acts (the line items; cascade-deleted with the record).
        builder.HasMany(dr => dr.Acts)
            .WithOne()
            .HasForeignKey(a => a.DentalRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(dr => dr.Teeth).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(dr => dr.Acts).UsePropertyAccessMode(PropertyAccessMode.Field);

        // L9 attribution — who earned this. A real FK to `Doctors`, not a bare Guid column: before L9 the only FK
        // to that table in the entire model was `Appointment.DoctorId`, and `WaitingListEntry.PreferredDoctorId`
        // demonstrates the cost of the bare form — nothing stopped it holding an id from another clinic, or one
        // that no longer exists.
        //
        // ⚠️ `SetNull`, matching `Appointment.DoctorId`: deleting a practitioner must leave the money and the
        // clinical record intact and merely unattributed. `Cascade` here would delete invoices when a dentist
        // leaves the practice, and `Restrict` would make removing them impossible.
        builder.HasOne(x => x.Doctor)
            .WithMany()
            .HasForeignKey(x => x.DoctorId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexed for the practitioner filter on /factures and on the dashboard's Argent section — the only two
        // readers, and both filter on it.
        builder.HasIndex(x => x.DoctorId);

    }
}

