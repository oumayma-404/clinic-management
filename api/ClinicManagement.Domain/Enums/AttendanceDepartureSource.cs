namespace ClinicManagement.Domain.Enums;

/// <summary>How a pointage line got its departure. Persisted as an int: append new members, never insert.</summary>
public enum AttendanceDepartureSource
{
    Borne = 0,
    DeclaredBySelf = 1,
    CorrectedByAdmin = 2,
}
