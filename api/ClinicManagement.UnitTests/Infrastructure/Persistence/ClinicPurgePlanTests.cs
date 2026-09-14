using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// The plan behind « supprimer définitivement ce cabinet » (<c>clinic-account-removal</c>) — the highest-stake
/// derived guard in this suite, because the failure it exists to catch is <b>silent and irreversible</b>: a table
/// the plan does not cover is a table whose patient rows survive a deletion the vendor was told had succeeded, and
/// nothing else in the build can see it. Nothing here touches a database; the plan is read off the EF model, which
/// is the same thing the runtime reads.
///
/// <para><b>⚠️ Every case is derived from the model, never from a list of table names.</b> A table added next month
/// is covered the day it is configured. The one hand-written list is
/// <see cref="ClinicPurge.SurvivesByDesign"/> — the rows that deliberately outlive a cabinet — and it is asserted
/// in both directions: a new table belonging to nobody fails, and so does an exemption for a table that has since
/// grown a clinic of its own.</para>
///
/// <para><b>⚠️ The single most valuable assertion is
/// <see cref="Every_Step_Names_The_Clinic_In_Its_Predicate"/>.</b> A step whose <c>WHERE</c> lost its parameter is
/// a <c>DELETE FROM "Patients"</c> for every cabinet on the deployment — it would pass a functional test of « the
/// cabinet is gone » perfectly.</para>
/// </summary>
public class ClinicPurgePlanTests
{
    private sealed class Scope : ICurrentClinicProvider
    {
        public bool IsSystemWide => true;
        public Guid? ClinicId => null;
    }

    /// <summary>Never connected to. Npgsql needs a syntactically valid string to configure itself, nothing more —
    /// `TenantScopeFilterTests`' helper, for its reason.</summary>
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=none;Password=none")
            .Options, new Scope());

    private static IReadOnlyList<ClinicPurge.PurgeStep> Plan()
    {
        using var db = Context();
        return ClinicPurge.BuildPlan(db.Model);
    }

    private static IReadOnlyList<string> Tables()
    {
        using var db = Context();
        return db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Where(e => e.GetTableName() is not null)
            .Select(e => e.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // ------------------------------------------------------------------ the classification, both directions

    [Fact]
    public void Every_Table_Is_Either_Purged_Or_Deliberately_Kept()
    {
        var planned = Plan().Select(s => s.Entity).ToHashSet(StringComparer.Ordinal);

        var unclassified = Tables()
            .Where(name => !planned.Contains(name))
            .Where(name => !ClinicPurge.SurvivesByDesign.ContainsKey(name))
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "These tables are neither deleted with a cabinet nor listed in ClinicPurge.SurvivesByDesign with a "
            + "reason. Decide which, and say why: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void No_Exemption_Names_A_Table_The_Plan_Already_Reaches()
    {
        var planned = Plan().Select(s => s.Entity).ToHashSet(StringComparer.Ordinal);

        // An exemption the plan contradicts is worse than no exemption: it reads as a promise that those rows
        // survive, while the deletion removes them.
        var contradicted = ClinicPurge.SurvivesByDesign.Keys.Where(planned.Contains).ToList();

        Assert.True(
            contradicted.Count == 0,
            "SurvivesByDesign claims these outlive a cabinet, but the plan deletes them: "
            + string.Join(", ", contradicted));
    }

    [Fact]
    public void No_Exemption_Names_A_Table_That_No_Longer_Exists()
    {
        var tables = Tables().ToHashSet(StringComparer.Ordinal);

        var stale = ClinicPurge.SurvivesByDesign.Keys.Where(k => !tables.Contains(k)).ToList();

        Assert.True(
            stale.Count == 0,
            "SurvivesByDesign exempts tables the model does not have. A stale exemption reads to the next author "
            + "as a live decision: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_Reason_Is_Stated()
    {
        Assert.All(
            ClinicPurge.SurvivesByDesign,
            entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), $"{entry.Key} is exempted with no reason"));
    }

    // ------------------------------------------------------------------ the properties a wrong plan would break

    [Fact]
    public void Every_Step_Names_What_It_Is_Keyed_On()
    {
        // The one that matters most: a predicate that lost its parameter is a DELETE for every cabinet on the
        // deployment, and a functional test of « this cabinet is gone » would still pass.
        Assert.All(
            Plan(),
            step => Assert.Contains(
                step.Key == ClinicPurge.PurgeKey.Clinic ? "@clinicId" : "@emails", step.Where));
    }

    [Fact]
    public void The_Two_Tables_No_Clinic_Id_Can_Reach_Are_Keyed_By_Address()
    {
        var byAddress = Plan().Where(s => s.Key == ClinicPurge.PurgeKey.Address).Select(s => s.Entity).ToList();

        // Derived from « clinic-less, not exempted, carries an Email column », so a third table of that shape is
        // swept the day it is configured. Named here because these two are the ones that exist today, and because
        // a plan that stopped finding them would otherwise look healthy.
        Assert.Contains(nameof(ClinicSignup), byAddress);
        Assert.Contains(nameof(PasswordResetRequest), byAddress);

        // The vendor's own account carries an address too, and it must not be swept with a cabinet's.
        Assert.DoesNotContain(nameof(PlatformAccount), byAddress);
    }

    [Fact]
    public void Every_Table_Is_Deleted_Once()
    {
        var duplicated = Plan()
            .GroupBy(s => s.Table, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicated.Count == 0, "Deleted twice: " + string.Join(", ", duplicated));
    }

    [Fact]
    public void The_Cabinets_Own_Row_Goes_Last()
    {
        var plan = Plan();

        Assert.Equal(nameof(Clinic), plan[^1].Entity);
        Assert.DoesNotContain(plan.Take(plan.Count - 1), s => s.Entity == nameof(Clinic));
    }

    [Fact]
    public void A_Dependent_Table_Is_Deleted_Before_The_Table_It_Points_At()
    {
        using var db = Context();
        var plan = ClinicPurge.BuildPlan(db.Model);
        var position = plan
            .Select((step, index) => (step.Entity, index))
            .ToDictionary(x => x.Entity, x => x.index, StringComparer.Ordinal);

        var wrongWayRound = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            if (!position.TryGetValue(entity.ClrType.Name, out var dependentAt))
            {
                continue;
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType.ClrType.Name;

                if (principal == entity.ClrType.Name || !position.TryGetValue(principal, out var principalAt))
                {
                    continue;
                }

                if (dependentAt > principalAt)
                {
                    wrongWayRound.Add($"{entity.ClrType.Name} after {principal}");
                }
            }
        }

        // Not a style preference: a row pointing at a deleted one is refused by PostgreSQL unless the key cascades,
        // so a mis-ordered plan aborts the whole deletion — which is safe, and reads to the vendor as a product
        // that cannot delete anything.
        Assert.True(
            wrongWayRound.Count == 0,
            "These tables are deleted after the tables they reference: " + string.Join(", ", wrongWayRound));
    }

    [Fact]
    public void The_Plan_Is_Not_Vacuous()
    {
        // A reflection guard fails OPEN: a renamed namespace or a model that stopped resolving would leave every
        // case above passing while checking nothing.
        var plan = Plan();

        Assert.True(plan.Count > 40, $"The plan covers only {plan.Count} tables — it has stopped reading the model.");
        Assert.Contains(plan, s => s.Entity == nameof(Patient));
        Assert.Contains(plan, s => s.Entity == nameof(DentalRecord));
        Assert.Contains(plan, s => s.Entity == nameof(Invoice));
        Assert.Contains(plan, s => s.Entity == nameof(User));
    }

    // ------------------------------------------------------------------ the figures the console states

    [Fact]
    public void Every_Headline_Figure_Names_A_Table_The_Plan_Deletes()
    {
        var planned = Plan().Select(s => s.Entity).ToHashSet(StringComparer.Ordinal);

        var orphaned = PlatformClinicFootprint.Headlines
            .SelectMany(h => h.Entities)
            .Where(e => !planned.Contains(e))
            .ToList();

        // A headline naming a table the plan does not delete reads 0 for ever — the console would understate the
        // deletion in exactly the figure somebody is deciding on.
        Assert.True(
            orphaned.Count == 0,
            "These headline figures name entities the plan does not cover: " + string.Join(", ", orphaned));
    }
}
