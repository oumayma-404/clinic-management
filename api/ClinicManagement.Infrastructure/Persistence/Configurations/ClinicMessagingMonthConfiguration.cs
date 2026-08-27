using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <b>No money annotation</b> — there is no <c>decimal</c> here at all, and if one is ever added it must carry no
/// <c>HasColumnType</c>/<c>HasPrecision</c> (see <c>ClinicSubscriptionConfiguration</c>'s note).
/// </summary>
public class ClinicMessagingMonthConfiguration : IEntityTypeConfiguration<ClinicMessagingMonth>
{
    public void Configure(EntityTypeBuilder<ClinicMessagingMonth> builder)
    {
        builder.ToTable("ClinicMessagingMonths");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.ClinicId)
            .IsRequired();

        builder.Property(m => m.MonthKey)
            .HasMaxLength(ClinicMessagingMonth.MonthKeyLength)
            .IsRequired();

        builder.Property(m => m.AllowanceMessages)
            .IsRequired();

        builder.Property(m => m.ConsumedMessages)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        // UNIQUE, and it is what makes the daily provisioning pass idempotent (D-2) — the pass re-runs over every
        // cabinet every night, so « one row per cabinet per month » has to be held by the database rather than by
        // whichever writer happens to check first.
        //
        // ⚠️ It is also why the dispatcher's ensure-create happens in its OWN save, before the send (§ 14a): a
        // unique violation raised by the daily pass racing it must not fail the commit that marks the reminder
        // Sent, which would leave one message paid for and uncounted and its duplicate counted twice (EC-15).
        builder.HasIndex(m => new { m.ClinicId, m.MonthKey })
            .IsUnique();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(m => m.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
