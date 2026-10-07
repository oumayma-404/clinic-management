using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One PC de secours problem the vendor was told about, from its start to its end (<c>clinic-pc-copy</c> AC-9.2).
///
/// <para><b>Why a row and not a flag on <see cref="ClinicRelay"/>.</b> The relay row is rewritten by the PC's heartbeat
/// every ten seconds, so a watcher writing beside it would race that writer; and « already e-mailed? » needs a memory
/// that survives the minutely pass, or the vendor is e-mailed every minute. An episode row is that memory, and the
/// history of a cabinet's PC failures comes with it.</para>
///
/// <para>⚠️ <b>Not an aggregate root, deliberately</b>: the audit interceptor would otherwise write these into the
/// cabinet's « Journal d'activité », which is about what the practice's people did. Never copied to the PC and never
/// archived (<c>ClinicArchiveScope.Excluded</c>) — it is the vendor's monitoring, not the practice's record.</para>
/// </summary>
public class RelayIncident : Entity<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid RelayId { get; private set; }
    public RelayIncidentKind Kind { get; private set; }
    public DateTime StartedAtUtc { get; private set; }

    /// <summary>When the vendor was e-mailed; null when no e-mail could be sent (no SMTP, nobody to tell).</summary>
    public DateTime? EmailedAtUtc { get; private set; }

    /// <summary>When the problem ended; null while it lasts. At most one open row per (relay, kind).</summary>
    public DateTime? EndedAtUtc { get; private set; }

    private RelayIncident() { }

    public static RelayIncident Start(Guid clinicId, Guid relayId, RelayIncidentKind kind, DateTime nowUtc)
    {
        if (clinicId == Guid.Empty || relayId == Guid.Empty)
        {
            throw new ArgumentException("Un incident de PC de secours appartient à un cabinet et à un PC.");
        }

        return new RelayIncident
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            RelayId = relayId,
            Kind = kind,
            StartedAtUtc = nowUtc,
        };
    }

    public bool IsOpen => EndedAtUtc is null;

    /// <summary>Idempotent: the first e-mail is the one recorded.</summary>
    public void MarkEmailed(DateTime nowUtc) => EmailedAtUtc ??= nowUtc;

    /// <summary>Idempotent: the first end is the one recorded.</summary>
    public void End(DateTime nowUtc) => EndedAtUtc ??= nowUtc;
}
