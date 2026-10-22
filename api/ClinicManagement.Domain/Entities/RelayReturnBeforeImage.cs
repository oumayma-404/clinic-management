using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A cloud row exactly as it was just before a PC de secours's return replaced or deleted it (<c>clinic-pc-copy</c>):
/// the cloud's work is never destroyed by a PC, whatever the return does — at worst a row comes back from here.
/// Written by the return itself, in its own transaction, for every row it touches.
///
/// <para>⚠️ <b>Not an aggregate root</b> (no journal line per copy) and never copied to the PC nor archived
/// (<c>ClinicArchiveScope.Excluded</c>): it is the deployment's safety net, not the practice's record.</para>
/// </summary>
public class RelayReturnBeforeImage : Entity<Guid>
{
    public Guid ClinicId { get; private set; }

    /// <summary>The return (handback or restored-cloud gap) that replaced or deleted the row.</summary>
    public Guid ReturnId { get; private set; }

    public string Table { get; private set; } = string.Empty;
    public string EntityKey { get; private set; } = string.Empty;

    /// <summary>The cloud's whole row, as JSON, before the return.</summary>
    public string RowJson { get; private set; } = string.Empty;

    /// <summary>True when the return deleted the row; false when it replaced it.</summary>
    public bool Deleted { get; private set; }

    public DateTime SavedAtUtc { get; private set; }

    private RelayReturnBeforeImage() { }
}
