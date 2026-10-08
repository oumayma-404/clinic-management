namespace ClinicManagement.Domain.Enums;

/// <summary>Why a line is in « Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6). Persisted as int: append, never insert.</summary>
public enum RelayReviewKind
{
    /// <summary>Changed in the cloud just before the cut; the PC never received it. Kept as the cloud has it.</summary>
    CloudOnly = 1,

    /// <summary>Changed on both sides: the cabinet's version was kept (EC-7), the cloud's is shown beside it.</summary>
    BothChanged = 2,

    /// <summary>One save recorded on both sides (D17); the cabinet's copy was kept because something already points to it.</summary>
    ProbableDuplicate = 3,
}
