using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// « Répartition des actes » — the merge that turns the SQL grouping into one point per act.
///
/// <para>The query keys on the booking <b>snapshot</b> (guaranteed to translate), so everything interesting happens
/// here: collapsing the rows of a renamed act, overlaying the live catalogue, and keeping a link-only devis line
/// that has no catalogue act at all. Exercised through <c>Merge</c> rather than the reader so the decisions are
/// tested without a repository — this is the part with judgement in it.</para>
/// </summary>
public class DashboardProcedureMixReaderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Detartrage = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Obturation = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ProcedureType Act(Guid id, string name, string hex) =>
        new(id, ClinicId, name, 30, ColorHex.FromString(hex));

    private static Dictionary<Guid, ProcedureType> Catalogue(params ProcedureType[] acts) =>
        acts.ToDictionary(a => a.Id);

    // ── merging ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An act renamed mid-period arrives as two snapshot rows for one id. It must read as one act.
    ///
    /// <para>This is the whole reason the merge exists: without it, « Détartrage » renamed to « Détartrage
    /// complet » would occupy two bars, splitting its own history and understating both halves.</para>
    /// </summary>
    [Fact]
    public void RowsSharingAnActMergeIntoOnePoint()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(Detartrage, "Détartrage", "#2A9D8F", ActCount: 4, Minutes: 120),
            new(Detartrage, "Détartrage complet", "#2A9D8F", ActCount: 2, Minutes: 60),
        };

        var points = DashboardProcedureMixReader.Merge(rows, Catalogue(Act(Detartrage, "Détartrage", "#2A9D8F")));

        var point = Assert.Single(points);
        Assert.Equal(6, point.ActCount);
        Assert.Equal(180, point.Minutes);
    }

    /// <summary>The live catalogue name wins — the agenda shows the current name, and so must this.</summary>
    [Fact]
    public void LiveCatalogueNameAndColourWinOverTheSnapshot()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(Detartrage, "ancien nom", "#E76F51", ActCount: 1, Minutes: 30),
        };

        var points = DashboardProcedureMixReader.Merge(
            rows, Catalogue(Act(Detartrage, "Détartrage", "#2A9D8F")));

        var point = Assert.Single(points);
        Assert.Equal("Détartrage", point.Name);
        Assert.Equal("#2A9D8F", point.ColorHex);
    }

    /// <summary>
    /// A retired act keeps its snapshot rather than vanishing.
    ///
    /// <para>It still did the work it did last month; dropping it would quietly shrink the period's totals instead
    /// of reporting them, which is the same class of error as omitting a cancelled payment from a ledger.</para>
    /// </summary>
    [Fact]
    public void ARetiredActFallsBackToItsSnapshot()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(Obturation, "Obturation (retiré)", "#4F83CC", ActCount: 3, Minutes: 135),
        };

        // Deliberately not in the catalogue — the id no longer resolves.
        var points = DashboardProcedureMixReader.Merge(rows, Catalogue());

        var point = Assert.Single(points);
        Assert.Equal("Obturation (retiré)", point.Name);
        Assert.Equal("#4F83CC", point.ColorHex);
        Assert.Equal(3, point.ActCount);
    }

    /// <summary>
    /// A hand-typed devis line has no catalogue act, so it is keyed on its own name — and it is <b>kept</b>.
    ///
    /// <para>It is real work. Dropping it because it has no id would under-report exactly the practices that type
    /// their devis lines freehand.</para>
    /// </summary>
    [Fact]
    public void ALinkOnlyRowIsKeptAndKeyedOnItsName()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(null, "Pose de facette", null, ActCount: 2, Minutes: 0),
            new(null, "Autre acte libre", null, ActCount: 1, Minutes: 0),
        };

        var points = DashboardProcedureMixReader.Merge(rows, Catalogue());

        Assert.Equal(2, points.Count);
        Assert.Contains(points, p => p.Name == "Pose de facette" && p.ActCount == 2);
        // No colour to invent: the client renders a neutral swatch rather than picking one.
        Assert.All(points, p => Assert.Null(p.ColorHex));
        Assert.All(points, p => Assert.Null(p.ProcedureTypeId));
    }

    /// <summary>Two link-only rows with the same name are the same act and merge.</summary>
    [Fact]
    public void LinkOnlyRowsWithTheSameNameMerge()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(null, "Pose de facette", null, ActCount: 2, Minutes: 0),
            new(null, "Pose de facette", null, ActCount: 3, Minutes: 0),
        };

        var point = Assert.Single(DashboardProcedureMixReader.Merge(rows, Catalogue()));
        Assert.Equal(5, point.ActCount);
    }

    /// <summary>A row carrying no name at all is labelled, never rendered as an empty bar.</summary>
    [Fact]
    public void ARowWithNoNameGetsAReadableLabel()
    {
        var rows = new List<ProcedureMixRow> { new(null, null, null, ActCount: 1, Minutes: 0) };

        var point = Assert.Single(DashboardProcedureMixReader.Merge(rows, Catalogue()));
        Assert.Equal("Acte", point.Name);
    }

    /// <summary>An empty snapshot colour is null, not <c>""</c> — the client tests for absence.</summary>
    [Fact]
    public void ABlankSnapshotColourBecomesNull()
    {
        var rows = new List<ProcedureMixRow> { new(null, "Acte libre", "   ", ActCount: 1, Minutes: 0) };

        var point = Assert.Single(DashboardProcedureMixReader.Merge(rows, Catalogue()));
        Assert.Null(point.ColorHex);
    }

    // ── ordering ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Busiest first by count, then by minutes. The server's cap takes the head of this list, so the order is
    /// what decides which acts a clinic with a wide catalogue actually sees.
    /// </summary>
    [Fact]
    public void PointsAreOrderedByCountThenMinutes()
    {
        var rows = new List<ProcedureMixRow>
        {
            new(null, "Rare", null, ActCount: 1, Minutes: 600),
            new(null, "Fréquent", null, ActCount: 9, Minutes: 90),
            new(null, "Moyen long", null, ActCount: 4, Minutes: 240),
            new(null, "Moyen court", null, ActCount: 4, Minutes: 60),
        };

        var points = DashboardProcedureMixReader.Merge(rows, Catalogue());

        Assert.Equal(
            new[] { "Fréquent", "Moyen long", "Moyen court", "Rare" },
            points.Select(p => p.Name).ToArray());
    }

    /// <summary>Nothing in, nothing out — and no exception on the empty period a closed August produces.</summary>
    [Fact]
    public void NoRowsYieldsNoPoints()
    {
        Assert.Empty(DashboardProcedureMixReader.Merge(new List<ProcedureMixRow>(), Catalogue()));
    }

    /// <summary>
    /// Zero minutes is a real answer and survives the merge.
    ///
    /// <para>A séance of link-only rows contributes no duration, so « durée » must be able to show 0 beside a real
    /// count rather than the point being dropped as empty.</para>
    /// </summary>
    [Fact]
    public void ZeroMinutesIsKeptAlongsideARealCount()
    {
        var rows = new List<ProcedureMixRow> { new(null, "Acte libre", null, ActCount: 3, Minutes: 0) };

        var point = Assert.Single(DashboardProcedureMixReader.Merge(rows, Catalogue()));
        Assert.Equal(3, point.ActCount);
        Assert.Equal(0, point.Minutes);
    }
}
