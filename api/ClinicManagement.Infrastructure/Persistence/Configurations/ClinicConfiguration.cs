using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.ToTable("Clinics");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Address)
            .HasMaxLength(500);

        builder.Property(c => c.City)
            .HasMaxLength(100);

        builder.Property(c => c.Phone)
            .HasMaxLength(50);

        builder.Property(c => c.Email)
            .HasMaxLength(200);

        builder.Property(c => c.Code)
            .HasMaxLength(20);

        // Billing / note-d'honoraires settings.
        builder.Property(c => c.MatriculeFiscal)
            .HasMaxLength(50);

        builder.Property(c => c.VatApplicable)
            .IsRequired()
            .HasDefaultValue(false);

        // AC-P4.38 — a RATE, not money: deliberately kept at (5,2) against the model-wide
        // HavePrecision(18,3) convention. A convention that silently widened a VAT rate would be worse than
        // the drift it fixes, so the explicit annotation is retained on purpose. verify-schema asserts this
        // column is NOT (18,3) — widening it is reported as drift in the other direction.
        builder.Property(c => c.VatRate)
            .HasColumnType("decimal(5,2)")
            .HasDefaultValue(7m);

        builder.Property(c => c.StampDutyEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.StampDutyAmount)
            .HasDefaultValue(1.000m);

        // TTN « El Fatoora » e-invoicing settings (non-secret).
        builder.Property(c => c.TtnEInvoicingEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.TtnEnvironment)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(Clinic.TtnEnvironmentSandbox);

        // The clinic's own El Fatoora identity (multi-tenant-cloud US-4). All nullable, no default: « this
        // clinic has not been issued a certificate » is a real state, not a value to invent.
        builder.Property(c => c.TtnUsername)
            .HasMaxLength(200);

        // A storage key, never the PFX itself — the bytes live in file storage like every other blob.
        builder.Property(c => c.TtnCertificateKey)
            .HasMaxLength(500);

        // Data-Protection ciphertext — opaque, variable length (matches the three reminder secret columns).
        builder.Property(c => c.TtnApiSecretEncrypted)
            .HasColumnType("text");

        builder.Property(c => c.TtnCertificatePasswordEncrypted)
            .HasColumnType("text");

        // Working hours JSON array (reliability-and-polish AC-7) — opaque, variable length.
        builder.Property(c => c.WorkingHoursJson)
            .HasColumnType("text");

        // Per-clinic Google Calendar connection (feature cloud-security-and-tenant-isolation, #4).
        builder.Property(c => c.GoogleRefreshToken)
            .HasColumnType("text");

        builder.Property(c => c.GoogleCalendarId)
            .HasMaxLength(256);

        // Patient-recall interval in months (clinical-workflow-depth), default 6.
        builder.Property(c => c.RecallIntervalMonths)
            .IsRequired()
            .HasDefaultValue(6);

        builder.HasIndex(c => c.Code)
            .IsUnique()
            .HasFilter("\"Code\" IS NOT NULL");

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt);

        builder.HasMany(c => c.Users)
            .WithOne(u => u.Clinic)
            .HasForeignKey(u => u.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Patients)
            .WithOne(p => p.Clinic)
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Appointments)
            .WithOne(a => a.Clinic)
            .HasForeignKey(a => a.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


