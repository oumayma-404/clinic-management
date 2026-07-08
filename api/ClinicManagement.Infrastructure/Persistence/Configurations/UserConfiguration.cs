using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasMaxLength(200); // Auth0 sub can be long

        builder.Property(u => u.ClinicId)
            .IsRequired();

        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.Email)
            .HasMaxLength(200);

        builder.Property(u => u.FullName)
            .HasMaxLength(200);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt);

        // Local-mode credential fields (inert in Cloud mode).
        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(u => u.MustChangePassword)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.LastLoginAt);
        builder.Property(u => u.FailedLoginAttempts)
            .IsRequired()
            .HasDefaultValue(0);
        builder.Property(u => u.LockoutEnd);

        builder.HasIndex(u => u.ClinicId);
        builder.HasIndex(u => new { u.ClinicId, u.Role });

        // Local accounts (those with a password) must have a unique email per install.
        // Filtered so Cloud rows (null/duplicate emails, no password) are excluded and unaffected.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"PasswordHash\" IS NOT NULL");
    }
}





