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

    /// <summary>
    /// This PC took over the cabinet's saves during a cut (D13) and accepts them now. False for the few seconds it hands
    /// the cut back (<see cref="IsHandingBack"/>): nothing saved then could reach the cloud.
    /// </summary>
    bool IsHolding { get; }

    /// <summary>D18: the cut's work is on its way back to the cloud; saves are refused with AC-5.2's sentence.</summary>
    bool IsHandingBack => false;
}
