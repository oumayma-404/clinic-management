namespace ClinicManagement.Domain.Common;

/// <summary>
/// « Every change to this record belongs in the journal. »
///
/// <para><b>Why this exists, and what it fixes.</b> <c>AuditSaveChangesInterceptor.IsAuditable</c> decided what
/// to record by asking whether the entity derives from <see cref="AggregateRoot{TId}"/>. That is a reasonable
/// proxy right up to the moment it is wrong, and it was wrong about the clinical record itself:
/// <c>DentalRecord</c> (the fiche de soins), <c>MedicalDocument</c> (ordonnances, certificats, bulletins CNAM,
/// arrêts de travail), <c>PatientFile</c> (radiographies and uploads), <c>ToothState</c> (the odontogramme),
/// <c>PatientMedicalHistory</c>, <c>PatientFamilyHistory</c>, <c>PatientFlag</c> and <c>Payment</c> are all
/// plain <see cref="Entity{TId}"/>, so <b>they produced no audit row at all</b> — on create, on edit or on
/// delete. Editing a patient's name was recorded; editing their clinical notes was not, and deleting their
/// prescriptions and x-rays left no trace while the blob went too.</para>
///
/// <para>⚠️ <b>Why a marker interface rather than promoting them to aggregate roots.</b> Making
/// <c>Payment</c> an <see cref="AggregateRoot{TId}"/> to get it audited would be a lie about the model — a
/// payment lives inside the invoice's consistency boundary — and the next reader would take the claim at face
/// value. « Is this an aggregate root? » and « must this be recorded? » are two questions, they happened to
/// share an answer for most of the model, and the clinical record is exactly where they diverge. Two questions
/// get two markers.</para>
///
/// <para>An aggregate root stays auditable without implementing this: the interceptor accepts either, so
/// nothing that was recorded before stops being recorded.</para>
///
/// <para>What holds it: <c>ClinicalRecordAuditCoverageTests</c> derives the requirement from the model — every
/// entity carrying a <c>PatientId</c> must be auditable — rather than from a list somebody remembers to extend.
/// A sixteenth patient-owned entity written next month is covered on the day it is written.</para>
/// </summary>
public interface IAuditable
{
}
