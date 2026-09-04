using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// The walk from « this caller » to « this marker », in <b>one</b> place.
///
/// <para>⚠️ <b>Three rows have to agree before a marker may be touched</b>: the patient belongs to the caller's
/// clinic, the file belongs to that patient, and the marker belongs to that file. Rename and delete both need
/// exactly that chain, and the second copy of a four-step guard is where a link quietly goes missing — this
/// repo's dominant defect shape. `CreateFileAnnotationCommand` does the first two steps itself because it has
/// no third row yet and needs the file's own <c>ClinicId</c> in hand.</para>
///
/// <para>⚠️ <b>Every refusal is the same sentence.</b> « Repère introuvable » covers a marker that does not
/// exist, one on another patient's file and one in another clinic, deliberately: distinguishing them would let
/// a caller enumerate what exists elsewhere by reading which refusal came back.</para>
/// </summary>
internal static class FileAnnotationAccess
{
    public static async Task<Result<PatientFileAnnotation>> LocateAsync(
        ICurrentClinicResolver clinicResolver,
        IPatientRepository patients,
        IPatientFileRepository files,
        IPatientFileAnnotationRepository annotations,
        Guid patientId,
        Guid fileId,
        Guid annotationId,
        CancellationToken cancellationToken)
    {
        var clinicResult = await clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return Result<PatientFileAnnotation>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
        }

        var patient = await patients.GetByIdAsync(patientId, cancellationToken);
        if (patient == null || patient.ClinicId != clinicResult.Value)
        {
            return Result<PatientFileAnnotation>.Failure("Patient introuvable.");
        }

        var file = await files.GetByIdAsync(fileId, cancellationToken);
        if (file == null || file.PatientId != patientId)
        {
            return Result<PatientFileAnnotation>.Failure("Fichier introuvable.");
        }

        var annotation = await annotations.GetByIdAsync(annotationId, cancellationToken);
        if (annotation == null || annotation.PatientFileId != fileId)
        {
            return Result<PatientFileAnnotation>.Failure("Repère introuvable.");
        }

        return Result<PatientFileAnnotation>.Success(annotation);
    }
}
