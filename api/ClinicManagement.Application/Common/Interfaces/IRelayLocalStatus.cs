namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What a PC de secours knows about itself (<c>clinic-pc-copy</c> AC-8.1): has the cloud retired it? Always « no » on
/// every install that is not a PC de secours.
///
/// <para>Read from the PC's own position file, which the copy loop writes when the cloud says the PC is released — the
/// PC's database cannot hold it, since the copy replaces that database's rows wholesale.</para>
/// </summary>
public interface IRelayLocalStatus
{
    bool IsRetired { get; }

    /// <summary>When this PC learned it was retired; null while it still follows its cabinet (or on older files).</summary>
    DateTime? RetiredAtUtc { get; }
}
