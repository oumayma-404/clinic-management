using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IPdfGenerationService
{
    Task<byte[]> GeneratePdfFromDocumentDataAsync(MedicalDocumentPdfData documentData, CancellationToken cancellationToken = default);
}

