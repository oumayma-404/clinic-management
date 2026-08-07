using ClinicManagement.Application.Features.CnamNomenclature;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// The batch reimbursement estimate (audit § 5.10, ACs P6.15–6.17).
///
/// <para><b>The finding was duplication, not a wrong number.</b> The BS1 editor carried its own
/// <c>CHILD_RATE = 0.7</c> / <c>ADULT_RATE = 0.6</c>, its own age-at-care-date arithmetic and its own
/// <c>coefficient × VLC × rate</c> — a second authority over a reimbursement figure, which drifts the first time
/// CNAM moves a rate or a band edge. This query is what the editor calls instead, and it shares
/// <see cref="CnamReimbursementCalculator"/> with the single-act query and the BS1 side, so the estimate and the
/// claim cannot disagree.</para>
///
/// <para>It exists as a batch because the editor shows an estimate per act row, live: the single-act endpoint per
/// row per keystroke is not a usable substitute. The VLC set is therefore read <b>once</b> per request.</para>
/// </summary>
public class ReimbursementEstimatesQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<ICnamCatalogRepository> _catalog = new();

    private GetReimbursementEstimatesQueryHandler Handler() =>
        new(_catalog.Object, NullLogger<GetReimbursementEstimatesQueryHandler>.Instance);

    private void WireLetterValues(params (string Cle, decimal Value)[] values) =>
        _catalog.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(values.Select(v => new CnamLetterValue(Guid.NewGuid(), ClinicId, v.Cle, v.Value)).ToList());

    [Fact]
    public async Task Estimates_Every_Act_In_One_Read_Of_The_Vlc_Set() // [AC-P6.15]
    {
        WireLetterValues(("D", 10m), ("C", 4m));

        var result = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                // Born 2010, cared for in 2026 → 16 years old → the 70 % child band.
                PatientDateOfBirth = new DateTime(2010, 3, 1),
                CareDate = new DateTime(2026, 7, 15),
                Items = new()
                {
                    new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 15m },
                    new ReimbursementEstimateItem { LettreCle = "C", Coefficient = 2m },
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(15m * 10m * 0.70m, result.Value![0].Estimate);
        Assert.Equal(2m * 4m * 0.70m, result.Value![1].Estimate);
        Assert.All(result.Value!, e => Assert.Equal(0.70m, e.RateApplied));

        // One read for the whole table — the per-act path would be one query per row.
        _catalog.Verify(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _catalog.Verify(r => r.GetLetterValueByCleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Results_Stay_Aligned_To_The_Requested_Order() // [AC-P6.15]
    {
        WireLetterValues(("D", 10m));

        var result = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                Items = new()
                {
                    new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 1m },
                    // No VLC value for « Z » — this row is not estimable, and it must not shift the ones after it.
                    new ReimbursementEstimateItem { LettreCle = "Z", Coefficient = 5m },
                    new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 3m },
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal(10m * 0.60m, result.Value![0].Estimate);
        Assert.Null(result.Value![1].Estimate);
        Assert.Equal(30m * 0.60m, result.Value![2].Estimate);
    }

    [Fact]
    public async Task An_Unknown_Lettre_Cle_Yields_Null_Not_Zero() // [AC-P6.15]
    {
        WireLetterValues(("D", 10m));

        var result = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                Items = new() { new ReimbursementEstimateItem { LettreCle = "X", Coefficient = 12m } },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // « — » and « 0,000 DT » are different claims: one says "we cannot estimate this", the other says
        // "the CNAM pays nothing for it".
        Assert.Null(result.Value!.Single().Estimate);
    }

    [Fact]
    public async Task A_Per_Act_Care_Date_Wins_Over_The_Bulletin_Date() // [AC-P6.15]
    {
        WireLetterValues(("D", 10m));

        var result = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                // The patient turns 19 on 1 June 2026 — the day they leave the 70 % band.
                PatientDateOfBirth = new DateTime(2007, 6, 1),
                CareDate = new DateTime(2026, 5, 1),
                Items = new()
                {
                    // Falls back to the bulletin date: still 18 → 70 %.
                    new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 1m },
                    // Carries its own, later care date: 19 → 60 %.
                    new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 1m, CareDate = new DateTime(2026, 7, 1) },
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The rate turns on the age at the CARE date, so a bulletin whose acts straddle a birthday genuinely has
        // two rates — which is why the item carries its own date rather than inheriting one for the batch.
        Assert.Equal(0.70m, result.Value![0].RateApplied);
        Assert.Equal(0.60m, result.Value![1].RateApplied);
    }

    [Fact]
    public async Task An_Empty_Request_Reads_Nothing()
    {
        var result = await Handler().Handle(new GetReimbursementEstimatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        _catalog.Verify(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Too_Many_Items_Is_Refused_In_French()
    {
        var result = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                Items = Enumerable.Range(0, 101)
                    .Select(_ => new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 1m })
                    .ToList(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Trop d'actes", result.Error);
        // The cap is a guard, so it must refuse before touching the database.
        _catalog.Verify(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Batch_Agrees_With_The_Single_Act_Query() // [AC-P6.15]
    {
        // Two endpoints over one calculator: if these ever disagree, the editor's live figure and the BS1's
        // computed one are two different numbers for the same act, which is the § 5.10 defect in a new place.
        WireLetterValues(("D", 12.500m));
        _catalog.Setup(r => r.GetLetterValueByCleAsync("D", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CnamLetterValue(Guid.NewGuid(), ClinicId, "D", 12.500m));

        var dob = new DateTime(2000, 1, 1);
        var careDate = new DateTime(2026, 7, 15);

        var batch = await Handler().Handle(
            new GetReimbursementEstimatesQuery
            {
                PatientDateOfBirth = dob,
                CareDate = careDate,
                Items = new() { new ReimbursementEstimateItem { LettreCle = "D", Coefficient = 7m } },
            },
            CancellationToken.None);

        var single = await new GetReimbursementEstimateQueryHandler(
                _catalog.Object, NullLogger<GetReimbursementEstimateQueryHandler>.Instance)
            .Handle(
                new GetReimbursementEstimateQuery
                {
                    LettreCle = "D",
                    Coefficient = 7m,
                    PatientDateOfBirth = dob,
                    CareDate = careDate,
                },
                CancellationToken.None);

        Assert.True(batch.IsSuccess);
        Assert.True(single.IsSuccess);
        Assert.Equal(single.Value!.Estimate, batch.Value!.Single().Estimate);
        Assert.Equal(single.Value!.RateApplied, batch.Value!.Single().RateApplied);
    }
}
