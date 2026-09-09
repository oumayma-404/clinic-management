using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents.Queries;

/// <summary>
/// Renders — and <b>never</b> persists — the ordonnance a fiche de soins is about to emit, so the practitioner
/// can read the actual sheet before saving.
///
/// <para>
/// ⚠️ <b>This exists because a browser-composed preview of a legal document is not safe here, and the product
/// already has the scar.</b> The document editor builds its own preview from form state and posts
/// <c>clinicName</c> / <c>doctorName</c> to <c>generate-pdf-download</c> with literal <c>"[Nom du cabinet]"</c>
/// and <c>"Dr. [Nom]"</c> fallbacks — so a failed clinic read there shows, and prints, a placeholder. The fiche
/// has none of those values and should never carry them: it sends what was typed, and the server composes the
/// sheet through <c>FicheOrdonnanceEmitter.ComposeAsync</c> — the same method the save uses, mapped through the
/// same <c>ToDto</c> → <c>ToPdfData</c> path the background PDF job uses. The preview is therefore the bytes
/// the save will produce, not a rendering that resembles them.
/// </para>
///
/// <para>
/// ⚠️ <b>Nothing is written and no id is needed.</b> There is no fiche yet on the first save — which is the
/// whole point, since « let me see the ordonnance before I hand it over » is asked at the chair, before
/// anything is recorded. The composed <c>MedicalDocument</c> is a value here: it is mapped, rendered and
/// dropped, and its <c>DentalRecordId</c> is null because it belongs to no fiche.
/// </para>
///
/// <para>
/// ⚠️ <b>The patient is tenant-checked, and the practitioner resolution is left to
/// <c>PractitionerRenderSnapshot</c></b> (which does its own), so a caller cannot render a sheet in another
/// cabinet's name or over another cabinet's patient.
/// </para>
/// </summary>
public class PreviewFicheOrdonnanceQuery : IRequest<Result<FicheOrdonnancePreviewDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>
    /// The practitioner the séance attributes the work to — <c>DentalRecord.DoctorId</c>. Resolved and
    /// tenant-checked server-side; the sheet is issued in this practitioner's name, never the caller's, so a
    /// secretary previewing a dentist's ordonnance sees the dentist's cachet.
    /// </summary>
    public Guid? DoctorId { get; set; }

    /// <summary>The séance's date, which is the document's date.</summary>
    public DateTime InterventionDate { get; set; }

    /// <summary>
    /// Which of the two sheets to render, in the <c>PrescriptionLineKinds</c> vocabulary the browser already
    /// holds (<c>medicament</c> | <c>examen</c>) rather than a raw document-type token. Anything unrecognised
    /// is a médicament, for <c>PrescriptionLineKinds.Normalize</c>'s reason.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Everything the section holds — both kinds. The server splits it, so the preview cannot sort a line differently from the save.</summary>
    public PrescriptionInput? Prescription { get; set; }
}

/// <summary>The rendered sheet. Bytes rather than a stored file, because nothing is stored.</summary>
public class FicheOrdonnancePreviewDto
{
    public byte[] Pdf { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}

public class PreviewFicheOrdonnanceQueryHandler
    : IRequestHandler<PreviewFicheOrdonnanceQuery, Result<FicheOrdonnancePreviewDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ILogger<PreviewFicheOrdonnanceQueryHandler> _logger;

    public PreviewFicheOrdonnanceQueryHandler(
        IUserRepository userRepository,
        IPatientRepository patientRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IClinicContext clinicContext,
        IPdfGenerationService pdfGenerationService,
        ILogger<PreviewFicheOrdonnanceQueryHandler> logger)
    {
        _userRepository = userRepository;
        _patientRepository = patientRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _clinicContext = clinicContext;
        _pdfGenerationService = pdfGenerationService;
        _logger = logger;
    }

    public async Task<Result<FicheOrdonnancePreviewDto>> Handle(
        PreviewFicheOrdonnanceQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<FicheOrdonnancePreviewDto>.Failure("Utilisateur non authentifié.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<FicheOrdonnancePreviewDto>.Failure("Utilisateur introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != user.ClinicId)
            {
                return Result<FicheOrdonnancePreviewDto>.Failure("Patient introuvable.");
            }

            var isExamen = PrescriptionLineKinds.IsExamen(request.Kind);
            var (medicaments, examens) = FicheOrdonnanceEmitter.Split(request.Prescription);
            var lines = isExamen ? examens : medicaments;

            if (lines.Count == 0)
            {
                // Not an error the user caused by anything they can see as broken — the section simply has
                // nothing of this kind in it — so it says what to do rather than that something failed.
                return Result<FicheOrdonnancePreviewDto>.Failure(isExamen
                    ? "Aucun examen à prévisualiser. Ajoutez un examen à la prescription."
                    : "Aucun médicament à prévisualiser. Ajoutez un médicament à la prescription.");
            }

            var documentType = isExamen ? DocumentTypes.Examens : DocumentTypes.Prescription;
            var composed = await FicheOrdonnanceEmitter.ComposeAsync(
                documentType,
                lines,
                // The renouvellement governs the médicament sheet only.
                isExamen ? null : request.Prescription?.Renewals,
                // No fiche and no appointment: nothing is saved, so the document belongs to nothing.
                new FicheOrdonnanceContext(
                    patient.Id, request.DoctorId, null, null, request.InterventionDate),
                patient,
                user.ClinicId,
                userId,
                _clinicRepository,
                _doctorRepository,
                _logger,
                cancellationToken);

            // Through the DTO, deliberately: that is the path PdfGenerationJob and the document-email command
            // both take, so a preview cannot be the one rendering that skips a mapping step.
            var pdfData = MedicalDocumentPdfMapping.ToPdfData(composed.Document.ToDto());
            var pdfBytes = await _pdfGenerationService.GeneratePdfFromDocumentDataAsync(pdfData, cancellationToken);

            var patientSlug = $"{patient.FirstName} {patient.LastName}".Trim().ToLowerInvariant().Replace(" ", "-");
            return Result<FicheOrdonnancePreviewDto>.Success(new FicheOrdonnancePreviewDto
            {
                Pdf = pdfBytes,
                FileName = $"{DocumentFileNaming.GetDocumentTypeName(documentType)}-{patientSlug}.pdf",
            });
        }
        catch (InvalidOperationException ex)
        {
            // ⚠️ A TYPED catch, and it has to be: the renderer's fail-fast messages (a missing or unreadable
            // form asset, no system font available for the overlay) are French text written for an operator,
            // and they are the whole reason `generate-pdf-download` surfaces this type verbatim. Folding this
            // into the catch-all below would replace a problem with a named remedy by « Une erreur est
            // survenue » — and folding the catch-all into THIS one would hand a Npgsql SQLSTATE to a browser,
            // which is what `ExceptionLeakCoverageTests` exists to refuse. Two catches, on purpose.
            _logger.LogError(ex, "Failed to render a fiche ordonnance preview ({Kind})", request.Kind);
            return Result<FicheOrdonnancePreviewDto>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render a fiche ordonnance preview ({Kind})", request.Kind);
            return Result<FicheOrdonnancePreviewDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
