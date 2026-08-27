using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <b>No <c>ClinicId</c> and therefore no query filter and no foreign key</b> — a signup exists precisely
/// because the clinic does not. <c>TenantScopeFilterTests</c> derives its clinic-owned set from the presence of
/// that column, so this table is outside it by construction rather than by exemption.
/// </summary>
public class ClinicSignupConfiguration : IEntityTypeConfiguration<ClinicSignup>
{
    public void Configure(EntityTypeBuilder<ClinicSignup> builder)
    {
        builder.ToTable("ClinicSignups");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.ClinicName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(s => s.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(s => s.Phone)
            .HasMaxLength(50);

        builder.Property(s => s.Address)
            .HasMaxLength(500);

        builder.Property(s => s.City)
            .HasMaxLength(100);

        builder.Property(s => s.DoctorInfoJson)
            .HasColumnType("text");

        // `text`, like DoctorInfoJson and Clinic.WorkingHoursJson: seven days of {enabled, from, to} is well under
        // any cap worth declaring, and a length limit on an opaque payload only turns a future extra field into a
        // truncation at the database rather than a refusal at the door.
        builder.Property(s => s.WorkingHoursJson)
            .HasColumnType("text");

        // 64 hex characters — SHA-256. Fixed by the algorithm, so the length is a statement rather than a guess.
        builder.Property(s => s.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.ExpiresAtUtc)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.Property(s => s.EmailSendAttempts)
            .IsRequired();

        // UNIQUE, and that is what makes « one row per address » (AC-6) an invariant the database holds rather
        // than a race the handler hopes to win: two simultaneous signups for one address would otherwise leave
        // two live tokens, and the loser's clinic name would be the one nobody chose.
        builder.HasIndex(s => s.Email)
            .IsUnique();

        // The verification lookup's only index. Unique too: a hash collision here is not a thing that happens,
        // and two rows sharing one hash would make « which clinic does this link create? » ambiguous.
        builder.HasIndex(s => s.TokenHash)
            .IsUnique();

        // The opportunistic purge scans on these two together.
        builder.HasIndex(s => new { s.ConsumedAtUtc, s.ExpiresAtUtc });
    }
}
