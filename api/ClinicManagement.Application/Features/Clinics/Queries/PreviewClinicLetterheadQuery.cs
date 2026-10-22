using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Clinics.Queries;

/// <summary>
/// A sample ordonnance printed on letterhead bands that are NOT saved yet — what the cabinet sees before it commits.
/// <para>
/// Real cabinet identity, the caller's own practitioner identity and cachet, a sample patient and sample lines, rendered
/// by the same renderer as every document. Persists nothing. A Query, so it broadcasts nothing either.
/// </para>
/// </summary>
public class PreviewClinicLetterheadQuery : IRequest<Result<byte[]>>
{
    public Stream Header { get; set; } = Stream.Null;
    public string? HeaderFileName { get; set; }
    public long HeaderLength { get; set; }
    public Stream? Footer { get; set; }
    public string? FooterFileName { get; set; }
    public long FooterLength { get; set; }
    public Stream? Body { get; set; }
    public string? BodyFileName { get; set; }
    public long BodyLength { get; set; }
}

public class PreviewClinicLetterheadQueryHandler : IRequestHandler<PreviewClinicLetterheadQuery, Result<byte[]>>
{
    private const string SamplePatient = "Patient Exemple";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IPdfGenerationService _pdf;
    private readonly ILogger<PreviewClinicLetterheadQueryHandler> _logger;

    public PreviewClinicLetterheadQueryHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IDoctorRepository doctorRepository,
        IPdfGenerationService pdf,
        ILogger<PreviewClinicLetterheadQueryHandler> logger)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _doctorRepository = doctorRepository;
        _pdf = pdf;
        _logger = logger;
    }

    public async Task<Result<byte[]>> Handle(PreviewClinicLetterheadQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            var user = string.IsNullOrEmpty(userId)
                ? null
                : await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<byte[]>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            if (!user.IsAdmin())
            {
                return Result<byte[]>.Failure(LetterheadRules.AdminOnly);
            }

            var header = await LetterheadBandReader.ReadAsync(
                LetterheadBandKind.Header, request.Header, request.HeaderFileName, request.HeaderLength, cancellationToken);
            if (header.IsFailure)
            {
                return Result<byte[]>.FailureFrom(header);
            }

            byte[]? footer = null;
            if (request.Footer != null)
            {
                var read = await LetterheadBandReader.ReadAsync(
                    LetterheadBandKind.Footer, request.Footer, request.FooterFileName, request.FooterLength, cancellationToken);
                if (read.IsFailure)
                {
                    return Result<byte[]>.FailureFrom(read);
                }

                footer = read.Value;
            }

            byte[]? body = null;
            if (request.Body != null)
            {
                var read = await LetterheadBandReader.ReadAsync(
                    LetterheadBandKind.Body, request.Body, request.BodyFileName, request.BodyLength, cancellationToken);
                if (read.IsFailure)
                {
                    return Result<byte[]>.FailureFrom(read);
                }

                body = read.Value;
            }

            var clinic = await _clinicRepository.GetByIdAsync(user.ClinicId, cancellationToken);
            var snapshot = await PractitionerRenderSnapshot.ResolveAsync(
                null, userId, user.ClinicId, _doctorRepository, _clinicRepository, cancellationToken);
            var doctor = await _doctorRepository.GetByUserIdAsync(userId!, cancellationToken);
            if (doctor != null && doctor.ClinicId != user.ClinicId)
            {
                doctor = null;
            }

            var data = new MedicalDocumentPdfData
            {
                DocumentType = DocumentTypes.Prescription,
                DocumentDate = ClinicClock.ClinicToday(),
                PatientName = SamplePatient,
                ClinicName = clinic?.Name ?? string.Empty,
                ClinicAddress = clinic?.Address ?? string.Empty,
                ClinicPhone = clinic?.Phone ?? string.Empty,
                ClinicEmail = snapshot.ClinicEmail,
                ClinicCity = snapshot.ClinicCity,
                DoctorName = doctor != null ? $"Dr. {doctor.FullName}" : string.Empty,
                DoctorSpecialty = DoctorSpecialtyLabels.Label(doctor?.Specialty),
                DoctorOrdreNumber = snapshot.DoctorOrdreNumber,
                DoctorCachetKey = snapshot.DoctorCachetKey,
                DoctorCachetContentType = snapshot.DoctorCachetContentType,
                Content = new Dictionary<string, string>
                {
                    ["medications"] = SampleMedicationsJson(ClinicClock.ClinicToday())
                }
            };

            var pdf = await _pdf.GeneratePdfWithLetterheadAsync(
                data, new LetterheadImages(header.Value!, footer, body), cancellationToken);
            return Result<byte[]>.Success(pdf);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error rendering the letterhead preview");
            return Result<byte[]>.Failure("Impossible de générer l'aperçu de l'en-tête.");
        }
    }

    // Built through the ordonnance's own wire shape, so the sample prints exactly like a real line.
    private static string SampleMedicationsJson(DateTime date)
    {
        var content = PrescriptionLines.BuildPrescriptionContentJson(
            new PrescriptionInput
            {
                Lines =
                {
                    new PrescriptionLineInput { Name = "Amoxicilline", Dosage = "1 g", Dose = "1 comprimé", TimesPerDay = "2", Duration = "7" },
                    new PrescriptionLineInput { Name = "Paracétamol", Dosage = "1 g", Dose = "1 comprimé", TimesPerDay = "3", Duration = "5" },
                    new PrescriptionLineInput { Name = "Chlorhexidine bain de bouche", Dosage = "0,12 %", Dose = "1 bain de bouche", TimesPerDay = "3", Duration = "7" }
                }
            },
            date);

        using var parsed = System.Text.Json.JsonDocument.Parse(content);
        return parsed.RootElement.GetProperty("medications").GetRawText();
    }
}
