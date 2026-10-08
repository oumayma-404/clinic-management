using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One line of « Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6, EC-7, D17): what the return found the cloud
/// had too — a change made in the cloud just before the cut that the PC de secours never received, a record both sides
/// changed (the cabinet's version was kept), or a save recorded twice whose second copy something already points to.
/// Never decided by the return: a person reads both versions and marks the line « Vu ».
///
/// <para>Cloud only — excluded from the copy and the archive. Not an aggregate root: the return writes many at once and
/// each would be one more journal row for a list the journal already announces.</para>
/// </summary>
public class RelayReviewItem : Entity<Guid>
{
    public const int MaxTableLength = 128;
    public const int MaxKeyLength = 300;
    public const int MaxAuthorLength = 256;

    public Guid ClinicId { get; private set; }
    public Guid RelayId { get; private set; }

    /// <summary>The takeover moment of the cut (the PC's clock): one return of one cut lists a key once.</summary>
    public DateTime CutSinceUtc { get; private set; }

    public RelayReviewKind Kind { get; private set; }

    /// <summary>The relay scope's table name (the entity's CLR name).</summary>
    public string Table { get; private set; } = string.Empty;

    /// <summary>The cabinet's record — or, for a cloud-only change, the cloud's.</summary>
    public string EntityKey { get; private set; } = string.Empty;

    /// <summary>The cloud's record as it stood before the return, as JSON; null when the cloud had deleted it.</summary>
    public string? CloudVersion { get; private set; }

    /// <summary>The cabinet's record as it reached the cloud, as JSON; null for a cloud-only change, or a deletion.</summary>
    public string? CabinetVersion { get; private set; }

    /// <summary>For a probable duplicate: the cloud's own record made by the same save.</summary>
    public string? CloudEntityKey { get; private set; }

    public DateTime? CloudChangedAtUtc { get; private set; }

    /// <summary>Who changed it in the cloud, as the journal names them (an e-mail, else the account id); null when unknown.</summary>
    public string? CloudChangedBy { get; private set; }

    /// <summary>Who entered the cabinet's version on the PC, as its journal names them; null when unknown.</summary>
    public string? CabinetChangedBy { get; private set; }

    public DateTime? CabinetChangedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewedByUserId { get; private set; }

    private RelayReviewItem() { }

    public RelayReviewItem(
        Guid clinicId, Guid relayId, DateTime cutSinceUtc, RelayReviewKind kind, string table, string entityKey,
        string? cloudVersion, string? cabinetVersion, string? cloudEntityKey, DateTime? cloudChangedAtUtc,
        string? cloudChangedBy, DateTime nowUtc, string? cabinetChangedBy = null, DateTime? cabinetChangedAtUtc = null)
        : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(entityKey))
        {
            throw new ArgumentException("Une modification à vérifier nomme une table et un enregistrement.");
        }

        ClinicId = clinicId;
        RelayId = relayId;
        CutSinceUtc = cutSinceUtc;
        Kind = kind;
        Table = Cap(table, MaxTableLength)!;
        EntityKey = Cap(entityKey, MaxKeyLength)!;
        CloudVersion = cloudVersion;
        CabinetVersion = cabinetVersion;
        CloudEntityKey = Cap(cloudEntityKey, MaxKeyLength);
        CloudChangedAtUtc = cloudChangedAtUtc;
        CloudChangedBy = Cap(cloudChangedBy, MaxAuthorLength);
        CabinetChangedBy = Cap(cabinetChangedBy, MaxAuthorLength);
        CabinetChangedAtUtc = cabinetChangedAtUtc;
        CreatedAtUtc = nowUtc;
    }

    /// <summary>A person read both versions. Idempotent: the first reader is the one recorded.</summary>
    public void MarkReviewed(string byUserId, DateTime nowUtc)
    {
        if (ReviewedAtUtc is not null)
        {
            return;
        }

        ReviewedAtUtc = nowUtc;
        ReviewedByUserId = byUserId;
    }

    private static string? Cap(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length > max ? value[..max] : value;
}
