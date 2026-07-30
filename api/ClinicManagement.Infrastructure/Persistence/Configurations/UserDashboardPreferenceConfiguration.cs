using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class UserDashboardPreferenceConfiguration : IEntityTypeConfiguration<UserDashboardPreference>
{
    public void Configure(EntityTypeBuilder<UserDashboardPreference> builder)
    {
        builder.ToTable("UserDashboardPreferences");

        // Shared primary key with the user (1:1): the entity Id IS the user id, mapped to the UserId column and
        // never store-generated (assigned in the domain ctor). Same shape as ClinicReminderSettings ↔ Clinic.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("UserId")
            .HasMaxLength(255)
            .ValueGeneratedNever();

        // Cascade: a deleted account's layout choices have no meaning and nothing else references them.
        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserDashboardPreference>(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // A short canonical CSV. `text` rather than a bounded varchar because the domain already caps both the
        // key count and each key's length, so a column length would be a second, weaker copy of that rule —
        // and the one that produces a truncation error instead of a French refusal if the two ever disagree.
        builder.Property(p => p.HiddenKpisCsv)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt);

        // No index beyond the primary key, deliberately: every read of this table is by user id, which IS the
        // primary key. There is no clinic-wide or list query over dashboard preferences and no reason to expect
        // one — the row exists to be fetched for exactly one signed-in user.
    }
}
