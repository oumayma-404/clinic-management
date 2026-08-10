using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One thing a console account did to one cabinet (<c>platform-console</c> FR-5, AC-7.3) — the vendor's own
/// append-only ledger.
///
/// <para><b>Why this is not <see cref="AuditEntry"/>.</b> That ledger answers « who at this cabinet changed this
/// record? » and is read by that cabinet's own admin at <c>GET /api/audit</c>; its actor is a clinic user and its
/// rows are the practice's history. This one answers the opposite question — « what did the <i>vendor</i> do, and
/// to whom? » — its actor belongs to no cabinet, and its most important row (a detail simply being <b>read</b>)
/// is not a mutation at all, so nothing in a save-interceptor could ever see it.</para>
///
/// <para>⚠️ <b>No foreign key to <c>Clinics</c>, and the cabinet's name is copied in.</b> The same decision
/// <see cref="AuditEntry"/> made and for a stronger reason here: a cascade would erase the record that somebody
/// looked at a cabinet that has since been closed, which is precisely the row anyone auditing this console would
/// want. <see cref="ClinicName"/> is denormalised so the journal can still name it (EC-13), and
/// <see cref="AccountEmail"/> likewise so a deactivated console account's rows stay readable.</para>
///
/// <para>⚠️ <b>Append-only by construction, not by convention.</b> There is no mutator on this type, no update or
/// delete on its repository, and no write endpoint — the same shape <c>AuditController</c> has. A ledger somebody
/// can correct is not evidence.</para>
/// </summary>
public class PlatformAccessEntry : AggregateRoot<Guid>
{
    /// <summary>The console account that acted. Recorded even after that account is deactivated.</summary>
    public Guid PlatformAccountId { get; private set; }

    /// <summary>The account's address at the time, so a row stays readable without joining a live account.</summary>
    public string AccountEmail { get; private set; } = string.Empty;

    /// <summary>The cabinet acted on. Deliberately not a foreign key — see the remarks above.</summary>
    public Guid ClinicId { get; private set; }

    /// <summary>The cabinet's name at the time, so a closed cabinet is still named rather than shown as a GUID.</summary>
    public string ClinicName { get; private set; } = string.Empty;

    public PlatformAccessAction Action { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private PlatformAccessEntry() { } // For EF Core

    public PlatformAccessEntry(
        Guid platformAccountId,
        string accountEmail,
        Guid clinicId,
        string clinicName,
        PlatformAccessAction action,
        DateTime occurredAt)
        : base(Guid.NewGuid())
    {
        if (platformAccountId == Guid.Empty)
            throw new ArgumentException("Un compte console est requis pour une entrée du journal.", nameof(platformAccountId));

        if (clinicId == Guid.Empty)
            throw new ArgumentException("Un cabinet est requis pour une entrée du journal.", nameof(clinicId));

        PlatformAccountId = platformAccountId;
        AccountEmail = accountEmail?.Trim() ?? string.Empty;
        ClinicId = clinicId;
        ClinicName = clinicName?.Trim() ?? string.Empty;
        Action = action;
        OccurredAt = occurredAt;
    }
}
