using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientAiSummaryQuery : IRequest<Result<PatientAiSummaryDto>>
{
    public Guid PatientId { get; set; }
}

public class GetPatientAiSummaryQueryHandler : IRequestHandler<GetPatientAiSummaryQuery, Result<PatientAiSummaryDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IHuggingFaceAIService _aiService;
    private readonly ILogger<GetPatientAiSummaryQueryHandler> _logger;

    public GetPatientAiSummaryQueryHandler(
        IPatientRepository patientRepository,
        IDentalRecordRepository dentalRecordRepository,
        ICurrentClinicResolver clinicResolver,
        IHuggingFaceAIService aiService,
        ILogger<GetPatientAiSummaryQueryHandler> logger)
    {
        _patientRepository = patientRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _clinicResolver = clinicResolver;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<Result<PatientAiSummaryDto>> Handle(GetPatientAiSummaryQuery request, CancellationToken cancellationToken)
    {
        // Tenant guard: resolve the caller's clinic and confirm the patient belongs to it. A missing or
        // cross-clinic patient throws NotFoundException → 404 (never another clinic's data, AC-8), keeping
        // it distinct from the AI-unavailable failure below (→ 400). GetByIdWithAppointmentsAsync eager-loads
        // the flags + history graphs the summary needs (GetByIdAsync does not include Flags).
        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return Result<PatientAiSummaryDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
        }

        var patient = await _patientRepository.GetByIdWithAppointmentsAsync(request.PatientId, cancellationToken);
        if (patient == null || patient.ClinicId != clinicResult.Value)
        {
            throw new NotFoundException("Patient not found");
        }

        var records = (await _dentalRecordRepository.GetByPatientIdAsync(request.PatientId, cancellationToken)).ToList();
        var prompt = BuildPrompt(patient, records);

        try
        {
            var messages = new List<HuggingFaceAIMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            var response = await _aiService.ChatAsync(messages, null, cancellationToken);
            if (response == null || string.IsNullOrWhiteSpace(response.Message))
            {
                // The AI backend answered but produced nothing usable — treat as unavailable (→ 400 → FR fallback).
                return Result<PatientAiSummaryDto>.Failure("Le résumé IA est momentanément indisponible.");
            }

            return Result<PatientAiSummaryDto>.Success(new PatientAiSummaryDto { Summary = response.Message.Trim() });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AI call failed (offline, backend error, timeout): log and surface a clean failure. The frontend
            // maps this to the French "résumé indisponible" fallback while the rest of the page still loads (AC-7).
            _logger.LogError(ex, "Failed to generate AI summary for patient {PatientId}", request.PatientId);
            return Result<PatientAiSummaryDto>.Failure("La génération du résumé IA a échoué.");
        }
    }

    // Builds a French instruction + a compact, factual patient snapshot. The AI is told to produce concise
    // bullet lines and to never invent data, so a data-poor patient yields a brief "peu d'informations" note
    // rather than a hallucinated history (spec edge case).
    private static string BuildPrompt(Patient patient, IReadOnlyList<DentalRecord> records)
    {
        var sb = new StringBuilder();
        // Framing matters: without an explicit authorization + output contract the model tends to refuse or
        // prepend a confidentiality disclaimer. State plainly that this is already-recorded data supplied by
        // the treating practitioner for authorized internal documentation, and demand the bullets ONLY.
        sb.AppendLine("Tu es un assistant de documentation clinique intégré au logiciel d'un cabinet dentaire. Les données ci-dessous sont déjà enregistrées dans le dossier du patient et te sont fournies par le praticien traitant à des fins de documentation interne autorisée. Tu ne divulgues rien : tu réorganises de manière concise des informations que le praticien connaît déjà.");
        sb.AppendLine("Réponds UNIQUEMENT par le résumé, directement en français. N'ajoute aucune introduction, aucun préambule, aucune clause de confidentialité ni avertissement ; ne refuse pas et ne reformule pas la demande.");
        sb.AppendLine("Objectif: en un coup d'œil, le praticien doit savoir ce qui a été fait au patient, ce qu'il faut surveiller, et ce qui reste à payer.");
        sb.AppendLine("Organise la réponse en sections, dans cet ordre. Chaque section = son titre sur sa propre ligne, puis des puces commençant par \"- \". N'écris rien avant la première section.");
        sb.AppendLine("⚠ Alertes — allergies, signalements actifs, antécédents médicaux à risque pour un soin dentaire. Écris une puce \"- Aucune alerte connue\" si rien.");
        sb.AppendLine("Traitements réalisés — une puce par fiche, de la plus récente à la plus ancienne : date, dent(s) traitée(s), procédure, puis la note clinique marquante (préfixe \"⚠\" pour une note importante). N'omets aucune fiche.");
        sb.AppendLine("À régler — le montant total restant à payer, en chiffres avec le symbole $. Écris \"- Aucun solde en attente\" si tout est payé. N'emploie jamais le mot « soldé ».");
        sb.AppendLine("À surveiller — points de suivi ou traitements à poursuivre, déduits des notes importantes ; omets entièrement cette section s'il n'y a rien à signaler.");
        sb.AppendLine("Style: français clinique télégraphique et concret. Condense les notes longues. N'invente aucune donnée ; si le dossier est pauvre, indique-le brièvement.");
        sb.AppendLine();
        sb.AppendLine("Données du patient:");

        var name = $"{patient.FirstName} {patient.LastName}".Trim();
        sb.AppendLine($"- Nom: {(string.IsNullOrWhiteSpace(name) ? "N/A" : name)}");

        var age = CalculateAge(patient.DateOfBirth);
        if (age.HasValue)
        {
            sb.AppendLine($"- Âge: {age.Value} ans");
        }

        if (!string.IsNullOrWhiteSpace(patient.Gender))
        {
            sb.AppendLine($"- Sexe: {patient.Gender}");
        }

        sb.AppendLine($"- Allergies: {(string.IsNullOrWhiteSpace(patient.Allergies) ? "aucune renseignée" : patient.Allergies)}");

        if (!string.IsNullOrWhiteSpace(patient.MedicalHistory))
        {
            sb.AppendLine($"- Antécédents médicaux (résumé): {patient.MedicalHistory}");
        }

        var activeFlags = patient.Flags.Where(f => f.IsActive).ToList();
        if (activeFlags.Count > 0)
        {
            var flagText = string.Join(", ", activeFlags.Select(f =>
                string.IsNullOrWhiteSpace(f.Description) ? f.FlagType.ToString() : $"{f.FlagType} ({f.Description})"));
            sb.AppendLine($"- Signalements actifs: {flagText}");
        }

        if (patient.MedicalHistoryEntries.Count > 0)
        {
            sb.AppendLine("- Historique médical:");
            foreach (var entry in patient.MedicalHistoryEntries)
            {
                sb.AppendLine($"  • {entry.Description}{(string.IsNullOrWhiteSpace(entry.Notes) ? string.Empty : $" — {entry.Notes}")}");
            }
        }

        if (patient.FamilyHistoryEntries.Count > 0)
        {
            sb.AppendLine("- Antécédents familiaux:");
            foreach (var entry in patient.FamilyHistoryEntries)
            {
                sb.AppendLine($"  • {entry.Relationship}: {entry.Condition}");
            }
        }

        if (records.Count > 0)
        {
            var totalOutstanding = records.Sum(r => Math.Max(0m, r.Cost - r.AmountPaid));
            sb.AppendLine($"- Reste à payer (total, toutes fiches confondues): {totalOutstanding.ToString("0.00", CultureInfo.InvariantCulture)} $");
            sb.AppendLine("- Fiches dentaires (de la plus récente à la plus ancienne):");
            foreach (var record in records.OrderByDescending(r => r.InterventionDate))
            {
                var remaining = Math.Max(0m, record.Cost - record.AmountPaid);
                var money = remaining > 0
                    ? $"coût {record.Cost.ToString("0.00", CultureInfo.InvariantCulture)} $, payé {record.AmountPaid.ToString("0.00", CultureInfo.InvariantCulture)} $, reste {remaining.ToString("0.00", CultureInfo.InvariantCulture)} $"
                    : $"coût {record.Cost.ToString("0.00", CultureInfo.InvariantCulture)} $, payé intégralement";
                var toothNumbers = record.Teeth.Select(t => t.ToothNumber).OrderBy(n => n).ToList();
                var dentition = record.IsAdultTeeth ? "dents adultes" : "dents de lait";
                var teethText = toothNumbers.Count > 0
                    ? $"{dentition} n° {string.Join(", ", toothNumbers)}"
                    : "aucune dent précisée";
                sb.AppendLine($"  • {record.InterventionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} — {record.ProcedureType} — {teethText} — {money}");
                // Feed the clinical notes the model previously never saw — the ImportantNotes are the
                // "⚠ Highlighted for doctors" ones and are the richest "what was done / what to watch" signal.
                foreach (var note in record.ImportantNotes)
                {
                    sb.AppendLine($"    ⚠ note importante: {note}");
                }
                foreach (var note in record.Notes)
                {
                    sb.AppendLine($"    note: {note}");
                }
            }
        }
        else
        {
            sb.AppendLine("- Fiches dentaires: aucune");
        }

        return sb.ToString();
    }

    private static int? CalculateAge(DateTime dateOfBirth)
    {
        if (dateOfBirth == default)
        {
            return null;
        }

        // The clinic's calendar day (AC-P6.4). On a birthday, `DateTime.UtcNow.Date` reports the patient a year
        // younger for the first hour of the day in Tunis — and the age is what the CNAM 70 %/60 % rate turns on.
        var today = ClinicClock.ClinicToday();
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? null : age;
    }
}
