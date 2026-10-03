using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>« Reste à payer »'s plan candidate read compiles to SQL — an untranslatable LINQ shape fails only at runtime.</summary>
public class PlanOutstandingQueryTranslationTests
{
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=none;Password=none")
            .Options);

    [Fact]
    public void The_Total_Versus_Collected_Test_Is_Pushed_Down()
    {
        using var db = Context();

        var sql = TreatmentPlanRepository.PlanOutstandingPatientQuery(db, Guid.NewGuid(), Array.Empty<Guid>()).ToQueryString();

        Assert.Contains("TotalPlanned", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AmountPaid", sql, StringComparison.Ordinal);
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Billed_Plan_Exclusion_Reaches_The_Sql()
    {
        using var db = Context();

        var without = TreatmentPlanRepository.PlanOutstandingPatientQuery(db, Guid.NewGuid(), Array.Empty<Guid>()).ToQueryString();
        var with = TreatmentPlanRepository.PlanOutstandingPatientQuery(db, Guid.NewGuid(), new[] { Guid.NewGuid() }).ToQueryString();

        Assert.NotEqual(without, with);
        Assert.Contains("NOT", with, StringComparison.OrdinalIgnoreCase);
    }
}
