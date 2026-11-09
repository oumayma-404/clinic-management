namespace ClinicManagement.Domain.Enums;

/// <summary>What a « Droit à congé » entry is. Stored as an int: append members, never insert.</summary>
public enum StaffLeaveAllowanceKind
{
    /// <summary>« 20 j par an » or « 2 j par mois » from a day on; a later rule supersedes it from its own day.</summary>
    Rule = 1,

    /// <summary>« Il lui reste 10 j au 06/11/2026 » — the balance on a day, which absorbs everything before it.</summary>
    Opening = 2,

    /// <summary>Days added or removed once, with a motif.</summary>
    Adjustment = 3,
}

/// <summary>How a rule credits its days: all at once on 1 January, or on the 1st of every month.</summary>
public enum StaffLeaveAccrual
{
    Year = 1,
    Month = 2,
}
