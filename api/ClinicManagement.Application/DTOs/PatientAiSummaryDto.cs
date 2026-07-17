namespace ClinicManagement.Application.DTOs;

// Response for GET /api/patients/{patientId}/ai-summary — a live, AI-generated French overview.
// Not persisted (generated on demand per the feature's "auto on load, no persist" choice).
public class PatientAiSummaryDto
{
    public string Summary { get; set; } = string.Empty;
}
