namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Where a <see cref="Entities.ToothState"/> came from. <c>Treatment</c> entries are produced by the
/// dental-record flow (a completed act, record-owned). <c>Diagnosis</c> entries are charted directly on the
/// odontogram before treatment (existing pathology / "à traiter"), are patient-owned (no source record), and
/// are cleared when the tooth later receives a treatment on the same tooth.
/// </summary>
public enum ToothStateSource
{
    Treatment = 0,
    Diagnosis = 1
}
