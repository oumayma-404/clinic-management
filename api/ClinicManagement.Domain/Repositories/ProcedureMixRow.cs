namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One act type's share of a window's work: how many were booked, and how long they take.
///
/// <para>Deliberately keyed on the <b>act</b> rather than the appointment. A séance routinely carries several
/// (« détartrage + deux obturations »), so the counts here normally sum to more than the number of visits — which
/// is why every surface rendering them says « actes » and never « rendez-vous ».</para>
///
/// <para><paramref name="ProcedureTypeId"/> is <b>null for a link-only row</b>: a hand-typed devis line has no
/// catalogue act behind it, takes its name from the plan step and contributes no duration. Such rows are real
/// work and are reported, grouped under their own name.</para>
///
/// <para>Name and colour are the <b>booking snapshots</b>. The reader overlays the live catalogue values where the
/// act still resolves, on <c>AppointmentProcedureMapping</c>'s rule — live wins, the snapshot is the fallback that
/// keeps a retired act rendering — but grouping happens on the id, so renaming an act merges its history rather
/// than splitting the chart in two.</para>
/// </summary>
public sealed record ProcedureMixRow(
    Guid? ProcedureTypeId,
    string? SnapshotName,
    string? SnapshotColorHex,
    int ActCount,
    int Minutes);
