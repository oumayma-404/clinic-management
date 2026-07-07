using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

public class PdfGenerationService : IPdfGenerationService
{
    private readonly ILogger<PdfGenerationService> _logger;

    public PdfGenerationService(ILogger<PdfGenerationService> logger)
    {
        _logger = logger;
        // Set QuestPDF license (free for non-commercial use, or use your license key)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GeneratePdfFromDocumentDataAsync(MedicalDocumentPdfData documentData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating PDF for document type: {DocumentType}", documentData.DocumentType);

            var pdfBytes = await Task.Run(() =>
            {
                return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        // Standard font: 11pt for body text, consistent across all documents
                        page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Helvetica"));

                        // Main content
                        page.Content()
                            .Column(column =>
                            {
                                column.Spacing(20);

                                // Header - Clinic Info
                                column.Item().Element(ComposeHeader(documentData));

                                // Recipient (for liaison)
                                if (documentData.DocumentType == "liaison" && !string.IsNullOrEmpty(documentData.RecipientDoctorName))
                                {
                                    column.Item().Element(ComposeRecipient(documentData));
                                }

                                // Date
                                column.Item().PaddingBottom(10).AlignRight().Text($"Paris, le {documentData.DocumentDate:dd MMMM yyyy}")
                                    .FontSize(11).FontFamily("Helvetica");

                                // Document Title
                                column.Item().Element(ComposeTitle(documentData.DocumentType));

                                // Patient Info
                                column.Item().Element(ComposePatientInfo(documentData));

                                // Document Content
                                column.Item().Element(ComposeContent(documentData));

                                // Spacer to push signature to bottom - fills remaining vertical space
                                column.Item().ExtendVertical();
                            });

                        // Footer - Signature always at bottom of page
                        page.Footer()
                            .PaddingTop(40)
                            .Element(ComposeSignature(documentData));
                    });
                }).GeneratePdf();
            }, cancellationToken);

            _logger.LogInformation("PDF generated successfully, size: {Size} bytes", pdfBytes.Length);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF from document data");
            throw;
        }
    }

    private Action<IContainer> ComposeHeader(MedicalDocumentPdfData data)
    {
        return container =>
        {
            container.PaddingBottom(15).Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(data.ClinicName).FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                column.Item().Text(data.ClinicAddress).FontSize(11).FontFamily("Helvetica");
                column.Item().Text($"Tél: {data.ClinicPhone}").FontSize(11).FontFamily("Helvetica");
                column.Item().Text($"{data.DoctorName} - {data.DoctorSpecialty}").FontSize(11).Bold().FontFamily("Helvetica");
            });
        };
    }

    private Action<IContainer> ComposeRecipient(MedicalDocumentPdfData data)
    {
        return container =>
        {
            container.Padding(12).PaddingBottom(15).Column(column =>
            {
                column.Spacing(4);
                column.Item().Text("À l'attention de:").FontSize(11).FontFamily("Helvetica");
                column.Item().Text(data.RecipientDoctorName ?? "").FontSize(12).Bold().FontFamily("Helvetica");
                if (!string.IsNullOrEmpty(data.RecipientDoctorSpecialty))
                {
                    column.Item().Text(data.RecipientDoctorSpecialty).FontSize(11).FontFamily("Helvetica");
                }
            });
        };
    }

    private Action<IContainer> ComposeTitle(string documentType)
    {
        var title = documentType.ToLowerInvariant() switch
        {
            "prescription" => "ORDONNANCE",
            "liaison" => "LETTRE DE LIAISON",
            "honoraires" => "NOTE D'HONORAIRES",
            "certificat" => "CERTIFICAT MÉDICAL",
            _ => "DOCUMENT MÉDICAL"
        };

        return container =>
        {
            container.PaddingVertical(10).AlignCenter().Text(title).FontSize(16).Bold().FontFamily("Helvetica");
        };
    }

    private Action<IContainer> ComposePatientInfo(MedicalDocumentPdfData data)
    {
        return container =>
        {
            container.Padding(12).PaddingBottom(15).Column(column =>
            {
                column.Spacing(6);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Patient").FontSize(9).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                        col.Item().Text(data.PatientName).FontSize(12).Bold().FontFamily("Helvetica");
                    });
                    if (!string.IsNullOrEmpty(data.PatientAge))
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Date de naissance").FontSize(9).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                            col.Item().Text(data.PatientAge).FontSize(12).FontFamily("Helvetica");
                        });
                    }
                });
            });
        };
    }

    private Action<IContainer> ComposeContent(MedicalDocumentPdfData data)
    {
        return container =>
        {
            container.Column(column =>
            {
                column.Spacing(15);

                switch (data.DocumentType.ToLowerInvariant())
                {
                    case "prescription":
                        if (data.Content.TryGetValue("medications", out var medications))
                        {
                            column.Item().PaddingBottom(8).Text("Prescription:").FontSize(12).Bold().FontFamily("Helvetica");
                            
                            // Try to parse as JSON array (new format)
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(medications))
                                {
                                    // Check if it's a JSON array (starts with '[')
                                    if (medications.TrimStart().StartsWith("["))
                                    {
                                        var options = new JsonSerializerOptions
                                        {
                                            PropertyNameCaseInsensitive = true
                                        };
                                        var medicationsArray = JsonSerializer.Deserialize<List<MedicationData>>(medications, options);
                                        if (medicationsArray != null && medicationsArray.Count > 0)
                                        {
                                            foreach (var med in medicationsArray)
                                            {
                                                var medText = med.Name ?? "Médicament";
                                                if (!string.IsNullOrWhiteSpace(med.Dosage))
                                                    medText += $" {med.Dosage}";
                                                if (!string.IsNullOrWhiteSpace(med.TimesPerDay))
                                                    medText += $", {med.TimesPerDay}x par jour";
                                                if (!string.IsNullOrWhiteSpace(med.Duration))
                                                {
                                                    var medDuration = med.Duration;
                                                    var isPlural = int.TryParse(medDuration, out var days) && days > 1;
                                                    medText += $" pendant {medDuration} jour{(isPlural ? "s" : "")}";
                                                }
                                                column.Item().PaddingBottom(4).Text(medText).FontSize(11).FontFamily("Helvetica");
                                            }
                                        }
                                        else
                                        {
                                            column.Item().PaddingBottom(4).Text("Aucune prescription").FontSize(11).FontFamily("Helvetica");
                                        }
                                    }
                                    else
                                    {
                                        // Old string format (backward compatibility)
                                        column.Item().PaddingBottom(4).Text(medications).FontSize(11).FontFamily("Helvetica");
                                    }
                                }
                                else
                                {
                                    column.Item().PaddingBottom(4).Text("Aucune prescription").FontSize(11).FontFamily("Helvetica");
                                }
                            }
                            catch (JsonException)
                            {
                                // Fallback to old string format (backward compatibility)
                                column.Item().PaddingBottom(4).Text(medications).FontSize(11).FontFamily("Helvetica");
                            }
                        }
                        break;

                    case "liaison":
                        // Display content directly without headers
                        if (data.Content.TryGetValue("content", out var content))
                        {
                            column.Item().PaddingBottom(4).Text(content).FontSize(11).FontFamily("Helvetica");
                        }
                        break;

                    case "honoraires":
                        column.Item().PaddingBottom(8).Text("Détail des services:").FontSize(12).Bold().FontFamily("Helvetica");
                        
                        // Handle both old string format and new array format
                        if (data.Content.TryGetValue("procedures", out var proceduresValue))
                        {
                            // proceduresValue is a string from Dictionary<string, string>
                            var proceduresStr = proceduresValue;
                            
                            if (!string.IsNullOrEmpty(proceduresStr))
                            {
                                // Try to parse as JSON array first
                                try
                                {
                                    var parsed = JsonSerializer.Deserialize<JsonElement>(proceduresStr);
                                    if (parsed.ValueKind == JsonValueKind.Array)
                                    {
                                        column.Item().PaddingLeft(5);
                                        foreach (var proc in parsed.EnumerateArray())
                                        {
                                            var procName = proc.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                                            var procCost = proc.TryGetProperty("cost", out var costEl) ? (costEl.ValueKind == JsonValueKind.Number ? costEl.GetDecimal() : 0) : 0;
                                            
                                            if (!string.IsNullOrEmpty(procName))
                                            {
                                                column.Item().PaddingBottom(6).Row(row =>
                                                {
                                                    row.RelativeItem().Text(procName).FontSize(11).FontFamily("Helvetica");
                                                    row.ConstantItem(90).AlignRight().Text(procCost.ToString("F2") + " €").FontSize(11).FontFamily("Helvetica");
                                                });
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Not an array, use as string
                                        column.Item().Text(proceduresStr).FontSize(11).FontFamily("Helvetica");
                                    }
                                }
                                catch
                                {
                                    // If parsing fails, use as plain string (old format)
                                    column.Item().Text(proceduresStr).FontSize(11).FontFamily("Helvetica");
                                }
                            }
                        }
                        
                        column.Item().PaddingTop(15).PaddingBottom(5).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Montant total:").FontSize(12).Bold().FontFamily("Helvetica");
                            row.RelativeItem().AlignRight().Text(data.Content.GetValueOrDefault("totalCost", "0,00 €")).FontSize(12).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                        });
                        break;

                    case "certificat":
                        var doctorOrderNumber = data.Content.GetValueOrDefault("doctorOrderNumber", "");
                        var startDate = data.Content.GetValueOrDefault("startDate", "");
                        var duration = data.Content.GetValueOrDefault("duration", "");
                        
                        // Parse start date if available
                        string startDateFormatted = "[date]";
                        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var startDateParsed))
                        {
                            startDateFormatted = startDateParsed.ToString("dd/MM/yyyy");
                        }
                        
                        // Parse patient date of birth
                        string patientDobFormatted = "[JJ/MM/AAAA]";
                        var patientDobStr = data.Content.GetValueOrDefault("patientDateOfBirth", "");
                        if (!string.IsNullOrEmpty(patientDobStr) && DateTime.TryParse(patientDobStr, out var patientDobParsed))
                        {
                            patientDobFormatted = patientDobParsed.ToString("dd/MM/yyyy");
                        }
                        
                        // Build cohesive paragraph text
                        var certificatText = $"Je soussigné(e), Docteur {data.DoctorName}, Docteur en médecine dentaire, Inscrit(e) à l'Ordre des Médecins sous le n° {(!string.IsNullOrEmpty(doctorOrderNumber) ? doctorOrderNumber : "[Numéro]")}, Exerçant à {data.ClinicAddress}, certifie avoir examiné ce jour : Patient(e) : Nom et prénom : {data.PatientName} né(e) le {patientDobFormatted} Et constate que son état de santé : ☐ nécessite un repos médical Pour une durée de : {(!string.IsNullOrEmpty(duration) ? duration : "[X]")} jour{(!string.IsNullOrEmpty(duration) && int.TryParse(duration, out var d) && d > 1 ? "s" : "")} À compter du : {startDateFormatted} Ce certificat est délivré à la demande de l'intéressé(e) pour servir et valoir ce que de droit.";
                        
                        column.Item().PaddingVertical(4).Text(certificatText).FontSize(11).FontFamily("Helvetica");
                        break;
                }
            });
        };
    }

    private Action<IContainer> ComposeSignature(MedicalDocumentPdfData data)
    {
        return container =>
        {
            container.PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("Date et signature du médecin").FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });
                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().Text(data.DoctorName).FontSize(12).Bold().FontFamily("Helvetica");
                    col.Item().Text(data.DoctorSpecialty).FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                });
            });
        };
    }

    private decimal ParseAmount(string amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
            return 0;

        // Remove currency symbols and spaces, replace comma with dot
        var cleaned = amountStr.Replace("€", "").Replace(" ", "").Replace(",", ".");
        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
            return result;

        return 0;
    }

    private class MedicationData
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("dosage")]
        public string Dosage { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("timesPerDay")]
        public string TimesPerDay { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("duration")]
        public string Duration { get; set; } = string.Empty;
    }
}
