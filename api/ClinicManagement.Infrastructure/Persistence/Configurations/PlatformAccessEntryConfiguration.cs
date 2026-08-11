using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the console's own access ledger.
///
/// <para>⚠️ <b>No foreign key to <c>Clinics</c> and none to <c>PlatformAccounts</c>, deliberately</b> — the
/// decision <c>AuditEntryConfiguration</c> makes, for the same reason and more sharply. Its two siblings
/// (<c>ClinicActivityDays</c>, <c>ClinicActivitySnapshots</c>) cascade from a cabinet <b>on purpose</b>: those are
/// measurements <i>of</i> a cabinet and are meaningless once it is gone. This is a record of what the <i>vendor</i>
/// did, and « who opened the file of the practice that has since been closed? » is exactly the row an audit of
/// this console would be looking for. A cascade would delete it; hence the denormalised
/// <see cref="PlatformAccessEntry.ClinicName"/> and <see cref="PlatformAccessEntry.AccountEmail"/>, which are what
/// let such a row still name both parties.</para>
///
/// <para>⚠️ Both indexes are on <c>(dimension, OccurredAt DESC)</c> rather than on the dimension alone: the
/// journal's only order is newest-first, and its two filters are « ce compte » and « ce cabinet », so a bare
/// equality index would leave PostgreSQL sorting the matched rows on every page.</para>
/// </summary>
public class PlatformAccessEntryConfiguration : IEntityTypeConfiguration<PlatformAccessEntry>
{
    public void Configure(EntityTypeBuilder<PlatformAccessEntry> builder)
    {
        builder.ToTable("PlatformAccessEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PlatformAccountId).IsRequired();
        builder.Property(e => e.AccountEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.ClinicId).IsRequired();
        builder.Property(e => e.ClinicName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Action).IsRequired().HasConversion<int>();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(PlatformAccessEntry.MaxIdempotencyKeyLength);

        // UNIQUE and **partial**: « one entry per submission » (AC-4.6) held by the database rather than by whichever
        // request happens to read first. Filtered on non-null because every read row legitimately has no key, and an
        // unfiltered unique index over a mostly-null column is both larger and — in PostgreSQL, where every NULL is
        // distinct — no stricter.
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("IX_PlatformAccessEntries_IdempotencyKey");

        builder.HasIndex(e => new { e.PlatformAccountId, e.OccurredAt })
            .HasDatabaseName("IX_PlatformAccessEntries_PlatformAccountId_OccurredAt");

        builder.HasIndex(e => new { e.ClinicId, e.OccurredAt })
            .HasDatabaseName("IX_PlatformAccessEntries_ClinicId_OccurredAt");
    }
}
