using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// « Répartition des actes » — what the clinic's work over the period was actually made of.
///
/// <para>The dashboard's only act-level figure, and the answer to a question no other screen asks: a dentist can
/// see how many visits they had and how much came in, but not that two fifths of their chair time went to
/// endodontics. That is a business decision input, which is why the client offers both measures — 62 détartrages
/// weigh fewer hours than 48 obturations, and « durée » is the half that says where the day went.</para>
///
/// <para>Two reads, not one per act: the grouped mix, then the catalogue once to overlay live names and colours.
/// A per-row lookup would be an N+1 over a figure that exists to be cheap.</para>
/// </summary>
public class DashboardProcedureMixReader : IDashboardProcedureMixReader
{
    /// <summary>
    /// How many act types the chart shows.
    ///
    /// <para>A catalogue runs to dozens of acts and a bar chart stops being readable long before that; the tail is
    /// individually tiny by construction, since the list is ordered by count. ⚠️ <b>The cap is stated to the reader
    /// by the client</b> rather than silently applied — « no silent caps » — so a clinic with more act types knows
    /// it is looking at the busiest ones.</para>
    /// </summary>
    public const int MaxPoints = 8;

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;

    public DashboardProcedureMixReader(
        IAppointmentRepository appointmentRepository,
        IProcedureTypeRepository procedureTypeRepository)
    {
        _appointmentRepository = appointmentRepository;
        _procedureTypeRepository = procedureTypeRepository;
    }

    public async Task<List<ProcedureMixPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, Guid? doctorId, CancellationToken cancellationToken)
    {
        var rows = await _appointmentRepository.GetProcedureMixBetweenAsync(
            clinicId, period.From, period.ToInclusive, doctorId, cancellationToken);

        if (rows.Count == 0)
        {
            return new List<ProcedureMixPointDto>();
        }

        // includeInactive: a retired act still did the work it did last month, and omitting it would quietly
        // shrink the period's totals rather than report them.
        var catalogue = await _procedureTypeRepository.GetFilteredAsync(
            clinicId, includeInactive: true, cancellationToken: cancellationToken);
        var live = catalogue.Items.ToDictionary(p => p.Id);

        return Cap(Merge(rows, live));
    }

    /// <summary>The « autres » point's name. One string, read by the client's own aggregate check too.</summary>
    public const string OthersName = "Autres actes";

    /// <summary>
    /// The busiest <see cref="MaxPoints"/> acts, plus everything else folded into one « Autres actes » point.
    ///
    /// <para>⚠️ <b>The tail used to be dropped, not folded.</b> A `Take(8)` over a catalogue of dozens meant the
    /// chart's figures did not add up to the clinic's work — « Répartition des actes » summed to less than the
    /// période's own act count, with nothing on screen accounting for the difference. The caption said the list was
    /// capped, which is honest about the <i>list</i> and says nothing about the arithmetic; a reader adding the bars
    /// up gets a wrong total either way.</para>
    ///
    /// <para>Folded rather than uncapped, because the cap is a real readability limit — a bar chart of forty acts is
    /// unreadable and the tail is individually tiny by construction, the list being ordered by count. One extra
    /// point makes the figures reconcile while keeping the chart legible, and it carries no colour: it is not an
    /// act, so a swatch would invite a click through to a catalogue entry that does not exist.</para>
    ///
    /// <para>Only ever added when something is actually folded — a clinic with eight act types or fewer sees
    /// exactly what it saw before.</para>
    /// </summary>
    public static List<ProcedureMixPointDto> Cap(List<ProcedureMixPointDto> ordered)
    {
        if (ordered.Count <= MaxPoints)
        {
            return ordered;
        }

        var shown = ordered.Take(MaxPoints).ToList();
        var tail = ordered.Skip(MaxPoints).ToList();

        shown.Add(new ProcedureMixPointDto
        {
            ProcedureTypeId = null,
            Name = OthersName,
            ColorHex = null,
            ActCount = tail.Sum(p => p.ActCount),
            Minutes = tail.Sum(p => p.Minutes),
        });

        return shown;
    }

    /// <summary>
    /// Collapse the grouped rows onto one point per act, and overlay the catalogue.
    ///
    /// <para>Merging on the <b>id</b> is what stops a renamed act appearing twice: the SQL grouping keys on the
    /// booking snapshot, so « Détartrage » renamed to « Détartrage complet » mid-month arrives as two rows for one
    /// act. Rows with no id keep their own name as the key, since that is all a hand-typed devis line has.</para>
    ///
    /// <para>Public rather than private so <c>DashboardProcedureMixReaderTests</c> can exercise it without a
    /// repository — it is the part with the decisions in it, and the test project has no
    /// <c>InternalsVisibleTo</c>. Same reasoning as <c>DirectoryAclHardener.ComposeGrantArguments</c>: a rule worth
    /// stating is worth asserting.</para>
    /// </summary>
    public static List<ProcedureMixPointDto> Merge(
        IReadOnlyList<ProcedureMixRow> rows,
        IReadOnlyDictionary<Guid, Domain.Entities.ProcedureType> live)
    {
        var merged = new Dictionary<string, ProcedureMixPointDto>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var key = row.ProcedureTypeId?.ToString() ?? $"name:{row.SnapshotName ?? string.Empty}";

            if (!merged.TryGetValue(key, out var point))
            {
                var resolved = row.ProcedureTypeId is { } id && live.TryGetValue(id, out var type) ? type : null;
                point = new ProcedureMixPointDto
                {
                    ProcedureTypeId = row.ProcedureTypeId,
                    // Live name wins, snapshot is the fallback that keeps a retired act rendering. « Acte » only
                    // where a link-only row carried no name at all — never an empty label.
                    Name = resolved?.Name ?? NonEmpty(row.SnapshotName) ?? "Acte",
                    ColorHex = resolved?.Color?.Value ?? NonEmpty(row.SnapshotColorHex),
                };
                merged[key] = point;
            }

            point.ActCount += row.ActCount;
            point.Minutes += row.Minutes;
        }

        return merged.Values
            .OrderByDescending(p => p.ActCount)
            .ThenByDescending(p => p.Minutes)
            .ThenBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
