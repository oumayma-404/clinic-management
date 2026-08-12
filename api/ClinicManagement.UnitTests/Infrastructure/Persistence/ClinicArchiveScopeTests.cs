using ClinicManagement.Domain.Entities;
using ClinicManagement.UnitTests.Common;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The archive's <b>plan</b> — which tables a cabinet's archive carries, in which order, and what is deliberately
/// left out (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>Why this is the highest-value class in the feature.</b> The entity set is derived from the EF model
/// rather than listed, and that is the decision everything else rests on: a table added next year is archived on
/// the day it is written. But a derived rule fails <i>silently</i> in both directions — a table that quietly stops
/// being reachable is a table the restore puts back nothing for, and a table wrongly admitted is a table selected
/// by the wrong predicate. Every case here is itself derived from <c>db.Model</c>, so it covers the model this
/// build actually has rather than the one somebody remembered.</para>
///
/// <para><b>No database is touched</b>, as everywhere in this project: Npgsql needs a syntactically valid
/// connection string to build the model and nothing more, and the model is all this reads
/// (<c>TenantScopeFilterTests</c>' technique and reason).</para>
/// </summary>
public class ClinicArchiveScopeTests
{
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, null);

    private static ClinicArchivePlan Plan(ApplicationDbContext db) => ClinicArchiveScope.Resolve(db.Model);

    /// <summary>Every non-owned table of the model, which is the population the plan is derived from.</summary>
    private static IReadOnlyList<IEntityType> Candidates(ApplicationDbContext db) =>
        db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.ClrType != typeof(object))
            .GroupBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    // [AC-1] Non-vacuity first, stated as data: a reflection-derived plan that found nothing would satisfy every
    // « nothing foreign is included » assertion below perfectly, and archive an empty file.
    [Fact]
    public void The_Plan_Covers_The_Cabinets_Real_Tables()
    {
        using var db = Context();
        var plan = Plan(db);

        Assert.True(plan.Tables.Count >= 25, $"Only {plan.Tables.Count} table(s) planned — the model scan is broken.");
        Assert.Contains(plan.Tables, t => t.Name == nameof(Patient));
        Assert.Contains(plan.Tables, t => t.Name == nameof(Invoice));
        Assert.Contains(plan.Tables, t => t.Name == nameof(InvoiceLine));
        Assert.Contains(plan.Tables, t => t.Name == nameof(DentalRecord));
    }

    // [AC-1] The cabinet's own record is FIRST and is its own scope: Clinic has no ClinicId — it IS the clinic —
    // so neither of the other two rules can place it, and the console path re-creates the practice from this row.
    [Fact]
    public void The_Clinic_Row_Is_Archived_First_And_Matched_On_Its_Own_Key()
    {
        using var db = Context();
        var first = Plan(db).Tables[0];

        Assert.Equal(nameof(Clinic), first.Name);
        Assert.Equal(ClinicArchiveTableScope.Self, first.Scope);
    }

    // [AC-1] The whole of « nothing belonging to another cabinet »: every planned table is reachable from ONE
    // clinic by one of exactly three predicates — its own key, its own ClinicId, or a parent already selected.
    // A table with no such path cannot be scoped and must never be admitted.
    [Fact]
    public void Every_Planned_Table_Is_Scoped_To_One_Cabinet()
    {
        using var db = Context();
        var plan = Plan(db);

        foreach (var table in plan.Tables)
        {
            switch (table.Scope)
            {
                case ClinicArchiveTableScope.Self:
                    Assert.Equal(typeof(Clinic), table.EntityType.ClrType);
                    break;

                case ClinicArchiveTableScope.Direct:
                    var clinicId = table.EntityType.FindProperty(ClinicArchiveScope.ClinicIdProperty);
                    Assert.True(clinicId is not null && clinicId.ClrType == typeof(Guid),
                        $"{table.Name} is scoped on ClinicId but has no such Guid column.");
                    break;

                default:
                    Assert.NotNull(table.ParentTable);
                    Assert.NotNull(table.ForeignKeyProperty);
                    break;
            }
        }
    }

    // [AC-1] A child hangs off a REQUIRED, single-column foreign key. An optional one is a reference and not
    // ownership — a TreatmentPlanItemId on an appointment does not make the appointment part of that plan — so
    // following one would scope a table by whichever parent it happened to mention.
    [Fact]
    public void A_Child_Is_Linked_By_A_Required_Single_Column_Foreign_Key()
    {
        using var db = Context();

        foreach (var table in Plan(db).Tables.Where(t => t.Scope == ClinicArchiveTableScope.Child))
        {
            var link = table.EntityType.GetForeignKeys().SingleOrDefault(fk =>
                fk.Properties.Count == 1
                && fk.Properties[0].Name == table.ForeignKeyProperty
                && fk.PrincipalEntityType.ClrType.Name == table.ParentTable);

            Assert.True(link is not null, $"{table.Name}'s declared link {table.ForeignKeyProperty} does not exist.");
            Assert.True(link!.IsRequired, $"{table.Name} is scoped through an OPTIONAL foreign key.");
        }
    }

    // [AC-3] Parents before children, which is the ordering a restore applies in: an invoice line reaching the
    // database ahead of its invoice is a foreign-key violation half way through putting a practice back.
    [Fact]
    public void Parents_Are_Planned_Before_Their_Children()
    {
        using var db = Context();
        var plan = Plan(db);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in plan.Tables)
        {
            if (table.Scope == ClinicArchiveTableScope.Child)
            {
                Assert.True(seen.Contains(table.ParentTable!),
                    $"{table.Name} is planned before its parent {table.ParentTable}.");
            }

            seen.Add(table.Name);
        }
    }

    // [AC-3] The assertion above is about the CHILD scope alone, and that is exactly where it was vacuous: the
    // directly-owned tables were appended in the model's own enumeration order with no regard for the foreign
    // keys between them, so on a full restore — the total-loss case the feature exists for — `DentalRecord`
    // reached the database before `Patient` and the operation died part way. The property is about every planned
    // table and every foreign key it holds, optional ones included: a nullable FK is not ownership, but the
    // database enforces it whenever the column is set.
    [Fact]
    public void Every_Foreign_Key_Points_At_A_Table_Already_Planned()
    {
        using var db = Context();
        var plan = Plan(db);

        var planned = plan.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var table in plan.Tables)
        {
            foreach (var fk in table.EntityType.GetForeignKeys())
            {
                var parent = fk.PrincipalEntityType.ClrType.Name;

                if (parent == table.Name || !planned.Contains(parent) || seen.Contains(parent))
                {
                    continue;
                }

                violations.Add($"{table.Name}.{fk.Properties[0].Name} → {parent}");
            }

            seen.Add(table.Name);
        }

        Assert.Empty(violations);
    }

    // [EC] The archive is keyed on the CLR *simple* name everywhere — `Excluded`, the manifest, `CanRestore`,
    // `Redacted`, `BlobProperties`. There is no collision in today's model, so this is latent; the failure modes
    // are the ones this feature cannot tolerate — one of two same-named types silently dropped from the plan, a
    // single `Excluded` entry excluding both, a manifest routing rows into the wrong entity — and all three are
    // invisible to the accounting test above, because the name IS accounted for.
    [Fact]
    public void No_Two_Archivable_Types_Share_A_Simple_Name()
    {
        using var db = Context();

        var duplicates = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.ClrType != typeof(object))
            .Select(e => e.ClrType)
            .Distinct()
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // [EC] Inclusion is derived and exclusion is hand-written, so the direction is inverted against the safe
    // default: a credential-bearing table added next year is archived into a deliberately UNENCRYPTED zip the
    // operator guidance tells the practice to keep on a laptop — with no compile error, no failing test and no
    // warning. This is the derived-vs-listed lesson `TenantScopeFilterTests` and `RealtimeResourceResolverTests`
    // already embody, applied in the one direction the feature had left open.
    [Fact]
    public void No_Planned_Table_Carries_A_Secret_That_Is_Not_Redacted()
    {
        using var db = Context();

        var carried = Plan(db).Tables
            .SelectMany(table => table.EntityType.GetProperties()
                .Where(p => !p.IsShadowProperty() && LooksLikeASecret(p.Name))
                .Where(p => !(ClinicArchiveScope.Redacted.GetValueOrDefault(table.Name)?.Contains(p.Name) ?? false))
                .Select(p => $"{table.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(carried);
    }

    // The other direction: an allowance nobody needs any more is a hole that has been pre-approved, so it fails
    // too — the same both-ways rule `PlatformReadShapeTests` holds its closed name set to.
    [Fact]
    public void Nothing_Is_Redacted_That_The_Archive_Does_Not_Carry()
    {
        using var db = Context();
        var plan = Plan(db);

        foreach (var (table, columns) in ClinicArchiveScope.Redacted)
        {
            var planned = plan.Tables.SingleOrDefault(t => t.Name == table);

            Assert.True(planned is not null, $"« {table} » is redacted but is not archived at all.");
            Assert.All(columns, column =>
                Assert.True(planned!.EntityType.FindProperty(column) is not null,
                    $"« {table}.{column} » is redacted but is not a column."));
        }
    }

    /// <summary>
    /// What a column name has to look like before it must be argued about — <see cref="SecretShapedNames"/>,
    /// shared with <c>SecretProtectionCoverageTests</c>. Two guards ask different questions of the <b>same</b>
    /// candidate set (« redacted from the archive? » and « encrypted at rest? »), and two copies of the rule
    /// would drift in the worst direction: a marker added to one leaves the other blind to precisely the columns
    /// somebody has just decided are sensitive.
    /// </summary>
    private static bool LooksLikeASecret(string name) => SecretShapedNames.Matches(name);

    // [EC] Nothing is dropped in silence: every table of the model is planned, excluded by name, or reported as
    // unreachable. Derived in both directions, so a table the walk stops reaching becomes a French warning rather
    // than a quietly smaller archive.
    [Fact]
    public void Every_Table_Is_Planned_Excluded_Or_Reported()
    {
        using var db = Context();
        var plan = Plan(db);

        var accountedFor = plan.Tables.Select(t => t.Name)
            .Concat(ClinicArchiveScope.Excluded)
            .ToHashSet(StringComparer.Ordinal);

        var unaccounted = Candidates(db)
            .Select(e => e.ClrType.Name)
            .Where(name => !accountedFor.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // What is left must be exactly what the plan warns about — same count and same names.
        Assert.Equal(unaccounted.Count, plan.Warnings.Count);
        foreach (var name in unaccounted)
        {
            Assert.Contains(plan.Warnings, w => w.Contains(name, StringComparison.Ordinal));
        }
    }

    /*
     * ── The exclusions ──────────────────────────────────────────────────────────────────────────────────────
     * Each is one of the spec's edge cases, and each fails differently: the entitlement would let a cabinet
     * restore its own cover from a file it controls, a due outbox row would SEND messages about visits that
     * happened months ago, and a credential would come back either undecryptable or in a file on a laptop.
     */

    [Theory]
    [InlineData(nameof(ClinicSubscription))]       // the VENDOR's money (clinic-subscription FR-2)
    [InlineData(nameof(SubscriptionPeriod))]
    [InlineData(nameof(Notification))]             // the three outboxes: re-sending is the loudest failure there is
    [InlineData(nameof(PushDelivery))]
    [InlineData(nameof(DocumentEmail))]
    [InlineData(nameof(StaffNotification))]
    [InlineData(nameof(DeviceRegistration))]
    [InlineData(nameof(BackupRun))]
    [InlineData(nameof(AuditEntry))]               // AC-9 has a restore APPEAR in the ledger, not rewrite it
    [InlineData(nameof(User))]                     // password hashes do not travel in a file on a laptop
    [InlineData(nameof(ClinicReminderSettings))]   // secrets whose key ring the archive does not carry
    public void What_An_Archive_Never_Carries_Is_Absent_From_The_Plan(string entity)
    {
        using var db = Context();

        Assert.Contains(entity, ClinicArchiveScope.Excluded);
        Assert.DoesNotContain(Plan(db).Tables, t => t.Name == entity);
    }

    // [EC] The vendor's own tables belong to no cabinet — they are measurements OF one — and ClinicSignup carries
    // no clinic at all, by construction.
    [Theory]
    [InlineData(nameof(PlatformAccount))]
    [InlineData(nameof(PlatformAccessEntry))]
    [InlineData(nameof(ClinicActivityDay))]
    [InlineData(nameof(ClinicActivitySnapshot))]
    [InlineData(nameof(ClinicSignup))]
    public void The_Vendors_Own_Tables_Are_Absent_From_The_Plan(string entity)
    {
        using var db = Context();

        Assert.DoesNotContain(Plan(db).Tables, t => t.Name == entity);
    }

    // [AC-5] The blob-key properties are DECLARED rather than derived, so a renamed column would stop the archive
    // carrying that kind of file at all — silently, since the rows would still be there. Checked against the model.
    [Fact]
    public void Every_Declared_Blob_Property_Exists_On_Its_Entity()
    {
        using var db = Context();
        var plan = Plan(db);

        Assert.NotEmpty(ClinicArchiveScope.BlobProperties);

        foreach (var (entity, property) in ClinicArchiveScope.BlobProperties)
        {
            var table = plan.Tables.SingleOrDefault(t => t.Name == entity);
            Assert.True(table is not null, $"{entity} declares a blob property but is not archived.");
            Assert.True(table!.EntityType.FindProperty(property) is not null,
                $"{entity}.{property} is declared as a storage key but is not a mapped column.");
        }
    }

    // [EC] The Google connection is a long-lived third-party CREDENTIAL, not a record, so it is nulled rather than
    // the row excluded — everything else on Clinic is exactly what a restored cabinet must come back with.
    [Fact]
    public void The_Clinics_Google_Credentials_Are_Redacted_Rather_Than_The_Row_Excluded()
    {
        using var db = Context();

        var redacted = ClinicArchiveScope.Redacted[nameof(Clinic)];

        Assert.Contains(nameof(Clinic.GoogleRefreshToken), redacted);
        Assert.Contains(nameof(Clinic.GoogleCalendarId), redacted);
        Assert.Contains(Plan(db).Tables, t => t.Name == nameof(Clinic));
    }
}
