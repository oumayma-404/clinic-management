using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.DateOfBirth)
            .IsRequired();

        builder.Property(p => p.Gender)
            .IsRequired()
            .HasMaxLength(20);

        // Value Objects
        builder.OwnsOne(p => p.Email, email =>
        {
            email.Property(e => e.Value)
                .HasColumnName("Email")
                .IsRequired()
                .HasMaxLength(255);
        });

        builder.OwnsOne(p => p.PhoneNumber, phone =>
        {
            phone.Property(p => p.Value)
                .HasColumnName("PhoneNumber")
                .IsRequired()
                .HasMaxLength(20);
        });

        builder.OwnsOne(p => p.Address, address =>
        {
            address.Property(a => a.Street).HasColumnName("Street").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("City").HasMaxLength(100);
            address.Property(a => a.State).HasColumnName("State").HasMaxLength(100);
            address.Property(a => a.ZipCode).HasColumnName("ZipCode").HasMaxLength(20);
            address.Property(a => a.Country).HasColumnName("Country").HasMaxLength(100);
        });

        builder.OwnsOne(p => p.InsuranceInfo, insurance =>
        {
            insurance.Property(i => i.Provider).HasColumnName("InsuranceProvider").HasMaxLength(200);
            insurance.Property(i => i.PolicyNumber).HasColumnName("InsurancePolicyNumber").HasMaxLength(100);
            insurance.Property(i => i.GroupNumber).HasColumnName("InsuranceGroupNumber").HasMaxLength(100);
            insurance.Property(i => i.ExpiryDate).HasColumnName("InsuranceExpiryDate");
        });

        // Optional CNAM identity (spec AC-1) — owned, all columns nullable. An all-null owned instance
        // reads back as a null navigation, i.e. "no CNAM identity", which is exactly the desired behavior.
        builder.OwnsOne(p => p.CnamInfo, cnam =>
        {
            cnam.Property(c => c.IdentifiantUnique).HasColumnName("CnamIdentifiantUnique").HasMaxLength(50);
            cnam.Property(c => c.Regime).HasColumnName("CnamRegime").HasMaxLength(50);
            cnam.Property(c => c.AssureFirstName).HasColumnName("CnamAssureFirstName").HasMaxLength(100);
            cnam.Property(c => c.AssureLastName).HasColumnName("CnamAssureLastName").HasMaxLength(100);
            cnam.Property(c => c.AssureAddress).HasColumnName("CnamAssureAddress").HasMaxLength(300);
            cnam.Property(c => c.AssurePostalCode).HasColumnName("CnamAssurePostalCode").HasMaxLength(20);
            cnam.Property(c => c.MaladeLien).HasColumnName("CnamMaladeLien").HasMaxLength(50);
            cnam.Property(c => c.MaladeLienRang).HasColumnName("CnamMaladeLienRang").HasMaxLength(50);
        });

        builder.Property(p => p.MedicalHistory)
            .HasColumnType("text");

        builder.Property(p => p.Allergies)
            .HasColumnType("text");

        builder.Property(p => p.EmergencyContactName)
            .HasMaxLength(200);

        builder.OwnsOne(p => p.EmergencyContactPhone, phone =>
        {
            phone.Property(p => p.Value)
                .HasColumnName("EmergencyContactPhone")
                .HasMaxLength(20);
        });

        // Patient recall / relance (clinical-workflow-depth).
        builder.Property(p => p.RecallReason)
            .HasMaxLength(100);

        builder.Property(p => p.RecallSnoozedUntil);

        builder.Property(p => p.LastRecallContactedAt);

        // Archiving (data-and-money-integrity). HasDefaultValue(false) is what makes the migration emit
        // NOT NULL DEFAULT false, so the column lands on a populated table without a backfill.
        builder.Property(p => p.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.ArchivedAt);

        builder.Property(p => p.ArchiveReason)
            .HasMaxLength(500);

        // Every list, search and picker filters on (clinic, archived).
        builder.HasIndex(p => new { p.ClinicId, p.IsArchived });

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);

        // Relationships
        builder.HasMany(p => p.Flags)
            .WithOne(f => f.Patient)
            .HasForeignKey(f => f.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Files)
            .WithOne(f => f.Patient)
            .HasForeignKey(f => f.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // NOTE: Patient → Appointments is deliberately NOT configured here.
        //
        // It used to be, with OnDelete(Cascade), and because ApplyConfigurationsFromAssembly applies
        // configurations in alphabetical order by class name, "PatientConfiguration" ran AFTER
        // "AppointmentConfiguration" and silently overwrote its OnDelete(SetNull) — so deleting a patient
        // hard-deleted their entire appointment history instead of preserving the slots.
        //
        // The relationship is declared exactly once, on the side that documents the intent:
        // AppointmentConfiguration.HasOne(a => a.Patient) … OnDelete(DeleteBehavior.SetNull).
        // Do not re-add it here.
    }
}



