using System.Reflection;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// What a PC de secours holds (<c>clinic-pc-copy</c> D4), derived from the EF model like the archive. A table the plan
/// silently stops covering is a table the PC does not have on the day it takes over; a table admitted with no path to
/// one clinic is another cabinet's rows on this one's PC. Every case reads <c>db.Model</c>; no database is touched.
/// </summary>
public class ClinicRelayScopeTests
{
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, null);

    private static ClinicRelayPlan Plan(ApplicationDbContext db) => ClinicRelayScope.Resolve(db.Model);

    private static IReadOnlyList<IEntityType> Candidates(ApplicationDbContext db) =>
        db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.ClrType != typeof(object) && e.GetTableName() is not null)
            .GroupBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    // Non-vacuity: a plan that found nothing would pass every « nothing foreign » assertion below.
    [Fact]
    public void The_Plan_Covers_The_Cabinets_Real_Tables_And_The_Added_Ones()
    {
        using var db = Context();
        var plan = Plan(db);

        Assert.True(plan.Tables.Count >= 30, $"Only {plan.Tables.Count} table(s) planned — the model scan is broken.");
        foreach (var name in new[] { nameof(Patient), nameof(Invoice), nameof(DentalRecord), nameof(Clinic) })
        {
            Assert.Contains(plan.Tables, t => t.Name == name);
        }

        foreach (var added in ClinicRelayScope.Added.Keys)
        {
            Assert.True(plan.Find(added) is not null, $"{added} is declared Added but is not in the relay plan.");
        }
    }

    // Every table of the model is either carried or deliberately left out — a third answer is a table nobody decided.
    [Fact]
    public void Every_Model_Table_Is_Carried_Or_Excluded_And_None_Is_Unplaced()
    {
        using var db = Context();
        var plan = Plan(db);

        Assert.Empty(plan.Unplaced);

        var undecided = Candidates(db)
            .Select(e => e.ClrType.Name)
            .Where(name => plan.Find(name) is null && !ClinicRelayScope.Excluded.Contains(name))
            .OrderBy(n => n)
            .ToList();
        Assert.True(undecided.Count == 0, "Table(s) neither carried nor excluded: " + string.Join(", ", undecided));
    }

    // The PC's own bookkeeping never travels — a PC holding the cloud's sessions or change log would be a second cloud.
    [Theory]
    [InlineData(nameof(ClinicRelay))]
    [InlineData(nameof(ClinicChange))]
    [InlineData(nameof(ClinicChangeCursor))]
    [InlineData(nameof(SessionFamily))]
    [InlineData(nameof(PasswordResetRequest))]
    [InlineData(nameof(AuditEntry))]
    [InlineData(nameof(PlatformAccount))]
    public void Deployment_State_Is_Excluded(string table)
    {
        using var db = Context();

        Assert.Contains(table, ClinicRelayScope.Excluded);
        Assert.Null(Plan(db).Find(table));
    }

    [Fact]
    public void Nothing_Is_Both_Added_And_Excluded()
    {
        Assert.Empty(ClinicRelayScope.Added.Keys.Intersect(ClinicRelayScope.Excluded));
    }

    // One clinic, by one of three predicates — and the clinic's own row first.
    [Fact]
    public void Every_Planned_Table_Is_Scoped_To_One_Cabinet()
    {
        using var db = Context();
        var plan = Plan(db);

        Assert.Equal(nameof(Clinic), plan.Tables[0].Name);
        foreach (var table in plan.Tables)
        {
            switch (table.Scope)
            {
                case ClinicRelayTableScope.Self:
                    Assert.Equal(nameof(Clinic), table.Name);
                    break;
                case ClinicRelayTableScope.Direct:
                    Assert.NotNull(table.ClinicColumn);
                    break;
                case ClinicRelayTableScope.Child:
                    Assert.NotNull(table.ParentTable);
                    Assert.True(plan.Find(table.ParentTable!) is not null, $"{table.Name}'s parent is not planned.");
                    break;
            }
        }
    }

    // A required FK out of a carried table must land in a carried table, or the PC refuses the row on apply.
    [Fact]
    public void Every_Required_Foreign_Key_Points_Into_The_Plan()
    {
        using var db = Context();
        var plan = Plan(db);

        var dangling = plan.Tables
            .SelectMany(t => t.EntityType.GetForeignKeys()
                .Where(fk => fk.IsRequired && !fk.PrincipalEntityType.IsOwned()
                             && plan.Find(fk.PrincipalEntityType.ClrType) is null)
                .Select(fk => $"{t.Name} → {fk.PrincipalEntityType.ClrType.Name}"))
            .ToList();

        Assert.True(dangling.Count == 0, "Required FK(s) leaving the relay plan: " + string.Join(", ", dangling));
    }

    // Apply order: a table is applied after every planned table it references, except the deferred back-edge columns.
    [Fact]
    public void Every_Foreign_Key_Points_At_A_Table_Applied_Earlier_Or_Is_Deferred()
    {
        using var db = Context();
        var plan = Plan(db);
        var position = plan.Tables.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i);

        var violations = new List<string>();
        foreach (var table in plan.Tables)
        {
            foreach (var fk in table.EntityType.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType.ClrType.Name;
                if (principal == table.Name || !position.TryGetValue(principal, out var theirs) || theirs < position[table.Name])
                {
                    continue;
                }

                var columns = fk.Properties.Select(p => p.GetColumnName() ?? p.Name);
                if (!columns.All(table.DeferredColumns.Contains))
                {
                    violations.Add($"{table.Name} → {principal}");
                }
            }
        }

        Assert.True(violations.Count == 0, "FK(s) applied before their principal with no deferral: " + string.Join(", ", violations));
    }

    // The secret-handling maps name real properties: a renamed column would travel in clear or be compared as drift.
    [Fact]
    public void Wrapped_Secrets_And_Per_Side_Columns_Name_Real_Properties()
    {
        using var db = Context();

        foreach (var (table, columns) in ClinicRelayScope.WrappedSecrets.Concat(ClinicRelayScope.PerSideColumns))
        {
            var entity = Candidates(db).Single(e => e.ClrType.Name == table);
            foreach (var column in columns)
            {
                Assert.True(entity.FindProperty(column) is not null, $"{table}.{column} does not exist.");
            }
        }
    }

    // A secret-shaped column on a carried table is sealed, redacted, or a hash — never sent under the cloud's key ring.
    [Fact]
    public void Every_Secret_Shaped_Column_Is_Sealed_Redacted_Or_A_Hash()
    {
        using var db = Context();
        var plan = Plan(db);

        var exposed = new List<string>();
        foreach (var table in plan.Tables)
        {
            // Only text or bytes can hold a secret; a flag named « MustChangePassword » holds none.
            foreach (var property in table.EntityType.GetProperties()
                         .Where(p => !p.IsShadowProperty() && (p.ClrType == typeof(string) || p.ClrType == typeof(byte[]))))
            {
                var name = property.Name;
                var secretShaped = name.Contains("Secret", StringComparison.Ordinal)
                                   || name.Contains("Token", StringComparison.Ordinal)
                                   || name.Contains("Password", StringComparison.Ordinal);
                if (!secretShaped || name.EndsWith("Hash", StringComparison.Ordinal) || name == "TokenVersion")
                {
                    continue;
                }

                var sealedOrRedacted =
                    (ClinicRelayScope.WrappedSecrets.TryGetValue(table.Name, out var wrapped) && wrapped.Contains(name))
                    || (ClinicRelayScope.Redacted.TryGetValue(table.Name, out var redacted) && redacted.Contains(name));
                if (!sealedOrRedacted)
                {
                    exposed.Add($"{table.Name}.{name}");
                }
            }
        }

        Assert.True(exposed.Count == 0, "Secret-shaped column(s) the PC would receive as stored: " + string.Join(", ", exposed));
    }

    // The TOTP secret is the one secret that must reach the PC — re-sealed for it, never in the cloud's ring.
    [Fact]
    public void The_Totp_Secret_Is_Sealed_For_The_Pc()
    {
        Assert.Contains(nameof(User.ProtectedTotpSecret), ClinicRelayScope.WrappedSecrets[nameof(User)]);
        Assert.Contains(nameof(User.ProtectedTotpSecret), ClinicRelayScope.PerSideColumns[nameof(User)]);
    }

    [Fact]
    public void The_Plan_Is_Resolved_Once_Per_Model()
    {
        using var db = Context();

        Assert.Same(ClinicRelayScope.For(db.Model), ClinicRelayScope.For(db.Model));
    }

    [Fact]
    public void A_Quoted_Identifier_Cannot_Close_Its_Quotes()
    {
        Assert.Equal("\"a\"\"b\"", ClinicRelaySql.Quote("a\"b"));
        Assert.Equal("\"s\".\"t\"", ClinicRelaySql.Qualify("s", "t"));
        Assert.Equal("\"t\"", ClinicRelaySql.Qualify(null, "t"));
    }
}
