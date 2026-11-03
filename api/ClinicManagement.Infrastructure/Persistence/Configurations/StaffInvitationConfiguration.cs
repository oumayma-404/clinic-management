using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>StaffInvitations</c> (<c>guided-first-days</c> C). One row per invited account (unique <c>UserId</c>, re-armed in
/// place by « Renvoyer l'e-mail »), and a <b>required FK to <c>Users</c> with cascade</b> — deleting the account, or the
/// whole cabinet through the console's purge, takes the invitation with it by the model.
/// </summary>
public class StaffInvitationConfiguration : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(EntityTypeBuilder<StaffInvitation> builder)
    {
        builder.ToTable("StaffInvitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.UserId).IsRequired().HasMaxLength(255);
        builder.Property(i => i.InvitedByUserId).IsRequired().HasMaxLength(255);
        builder.Property(i => i.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(i => i.ExpiresAtUtc).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.UserId).IsUnique();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.ConsumedAtUtc, i.ExpiresAtUtc });
    }
}
