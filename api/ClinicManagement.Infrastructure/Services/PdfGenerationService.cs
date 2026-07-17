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

    public async Task<byte[]> GenerateInvoicePdfAsync(InvoicePdfData data, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating invoice PDF for {Number}", data.Number);

            var pdfBytes = await Task.Run(() =>
            {
                return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Helvetica"));

                        page.Content().Column(column =>
                        {
                            column.Spacing(16);

                            // Clinic identity header (incl. matricule fiscal)
                            column.Item().Column(header =>
                            {
                                header.Spacing(3);
                                header.Item().Text(data.ClinicName).FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                                if (!string.IsNullOrWhiteSpace(data.ClinicAddress))
                                    header.Item().Text(data.ClinicAddress).FontSize(10).FontFamily("Helvetica");
                                if (!string.IsNullOrWhiteSpace(data.ClinicPhone))
                                    header.Item().Text($"Tél : {data.ClinicPhone}").FontSize(10).FontFamily("Helvetica");
                                if (!string.IsNullOrWhiteSpace(data.MatriculeFiscal))
                                    header.Item().Text($"Matricule fiscal : {data.MatriculeFiscal}").FontSize(10).FontFamily("Helvetica");
                            });

                            // Title
                            column.Item().PaddingTop(4).AlignCenter().Text("NOTE D'HONORAIRES").FontSize(16).Bold().FontFamily("Helvetica");

                            if (data.IsCancelled)
                            {
                                column.Item().AlignCenter().Text("FACTURE ANNULÉE").FontSize(12).Bold().FontColor(Colors.Red.Darken2).FontFamily("Helvetica");
                            }

                            // Number + date + patient
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"N° {data.Number}").FontSize(12).Bold().FontFamily("Helvetica");
                                    col.Item().Text($"Patient : {data.PatientName}").FontSize(11).FontFamily("Helvetica");
                                });
                                row.RelativeItem().AlignRight().Text($"Le {data.IssueDate:dd/MM/yyyy}").FontSize(11).FontFamily("Helvetica");
                            });

                            // Lines table
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(5);
                                    cols.RelativeColumn(1);
                                    cols.RelativeColumn(2);
                                    cols.RelativeColumn(2);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(HeaderCell).Text("Désignation");
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Qté");
                                    h.Cell().Element(HeaderCell).AlignRight().Text("P.U. HT");
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Total HT");
                                });

                                foreach (var line in data.Lines)
                                {
                                    table.Cell().Element(BodyCell).Text(line.Designation);
                                    table.Cell().Element(BodyCell).AlignRight().Text(line.Quantity.ToString());
                                    table.Cell().Element(BodyCell).AlignRight().Text(FormatDt(line.UnitPriceHt));
                                    table.Cell().Element(BodyCell).AlignRight().Text(FormatDt(line.LineTotalHt));
                                }
                            });

                            // Totals
                            column.Item().AlignRight().Column(totals =>
                            {
                                totals.Spacing(3);
                                totals.Item().Text($"Total HT : {FormatDt(data.TotalHt)}").FontSize(11).FontFamily("Helvetica");
                                if (data.VatApplicable && data.TotalVat > 0)
                                {
                                    totals.Item().Text($"TVA ({data.VatRate:0.##} %) : {FormatDt(data.TotalVat)}").FontSize(11).FontFamily("Helvetica");
                                }
                                if (data.StampDutyAmount > 0)
                                {
                                    totals.Item().Text($"Timbre fiscal : {FormatDt(data.StampDutyAmount)}").FontSize(11).FontFamily("Helvetica");
                                }
                                totals.Item().PaddingTop(3).Text($"Total TTC : {FormatDt(data.TotalTtc)}").FontSize(13).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                            });

                            column.Item().ExtendVertical();
                        });

                        page.Footer().PaddingTop(20).Column(footer =>
                        {
                            footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                            footer.Item().PaddingTop(6).Text("Note d'honoraires soumise au timbre fiscal — Montants exprimés en dinars tunisiens (DT).")
                                .FontSize(8).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                        });
                    });
                }).GeneratePdf();
            }, cancellationToken);

            _logger.LogInformation("Invoice PDF generated, size: {Size} bytes", pdfBytes.Length);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invoice PDF for {Number}", data.Number);
            throw;
        }
    }

    // Format a TND amount to millimes (3 decimals) with the "DT" suffix, French grouping.
    private static string FormatDt(decimal amount) =>
        amount.ToString("#,##0.000", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " DT";

    private static IContainer HeaderCell(IContainer container) =>
        container.PaddingVertical(5).PaddingHorizontal(4).Background(Colors.Grey.Lighten3)
            .BorderBottom(1).BorderColor(Colors.Grey.Medium).DefaultTextStyle(x => x.Bold().FontSize(10).FontFamily("Helvetica"));

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).PaddingHorizontal(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .DefaultTextStyle(x => x.FontSize(10).FontFamily("Helvetica"));

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
            "bulletin-cnam" => "BULLETIN DE SOINS CNAM (BS1)",
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

                    case "bulletin-cnam":
                        var careType = data.Content.GetValueOrDefault("careType", "");
                        var apciCode = data.Content.GetValueOrDefault("apciCode", "");
                        var careLine = (careType == "APCI" && !string.IsNullOrEmpty(apciCode))
                            ? $"Type de prise en charge : {careType} — code APCI {apciCode}"
                            : $"Type de prise en charge : {careType}";
                        column.Item().PaddingBottom(6).Text(careLine).FontSize(11).Bold().FontFamily("Helvetica");

                        var idu = data.Content.GetValueOrDefault("identifiantUnique", "");
                        var regime = data.Content.GetValueOrDefault("regime", "");
                        var assureName = ($"{data.Content.GetValueOrDefault("assureFirstName", "")} {data.Content.GetValueOrDefault("assureLastName", "")}").Trim();
                        var maladeLien = data.Content.GetValueOrDefault("maladeLien", "");
                        column.Item().PaddingBottom(2).Text($"Identifiant unique : {(string.IsNullOrEmpty(idu) ? "________________" : idu)}").FontSize(10).FontFamily("Helvetica");
                        column.Item().PaddingBottom(2).Text($"Régime : {(string.IsNullOrEmpty(regime) ? "________________" : regime)}").FontSize(10).FontFamily("Helvetica");
                        column.Item().PaddingBottom(2).Text($"Assuré social : {(string.IsNullOrEmpty(assureName) ? "________________" : assureName)}").FontSize(10).FontFamily("Helvetica");
                        column.Item().PaddingBottom(8).Text($"Lien du malade à l'assuré : {(string.IsNullOrEmpty(maladeLien) ? "________________" : maladeLien)}").FontSize(10).FontFamily("Helvetica");

                        column.Item().PaddingBottom(4).Text("Actes et soins dentaires:").FontSize(12).Bold().FontFamily("Helvetica");
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(2);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("Date").FontSize(9).Bold();
                                header.Cell().Element(HeaderCell).Text("Dent(s)").FontSize(9).Bold();
                                header.Cell().Element(HeaderCell).Text("Code acte").FontSize(9).Bold();
                                header.Cell().Element(HeaderCell).Text("Cotation").FontSize(9).Bold();
                                header.Cell().Element(HeaderCell).AlignRight().Text("Honoraires").FontSize(9).Bold();
                            });

                            decimal totalHonoraires = 0m;
                            if (data.Content.TryGetValue("acts", out var actsStr) && !string.IsNullOrEmpty(actsStr))
                            {
                                try
                                {
                                    var acts = JsonSerializer.Deserialize<JsonElement>(actsStr);
                                    if (acts.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var act in acts.EnumerateArray())
                                        {
                                            string GetProp(string p) => act.TryGetProperty(p, out var el)
                                                ? (el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString())
                                                : "";
                                            var hon = GetProp("honoraires");
                                            if (decimal.TryParse(hon, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var honVal))
                                            {
                                                totalHonoraires += honVal;
                                            }
                                            table.Cell().Element(BodyCell).Text(GetProp("date")).FontSize(9);
                                            table.Cell().Element(BodyCell).Text(GetProp("teeth")).FontSize(9);
                                            table.Cell().Element(BodyCell).Text(GetProp("codeActe")).FontSize(9);
                                            table.Cell().Element(BodyCell).Text(GetProp("cotation")).FontSize(9);
                                            table.Cell().Element(BodyCell).AlignRight().Text(hon).FontSize(9);
                                        }
                                    }
                                }
                                catch
                                {
                                    // Malformed acts JSON — render an empty table rather than failing the PDF.
                                }
                            }
                            table.Cell().ColumnSpan(4).Element(BodyCell).AlignRight().Text("Total honoraires (TND):").FontSize(10).Bold();
                            table.Cell().Element(BodyCell).AlignRight().Text(totalHonoraires.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).FontSize(10).Bold();
                        });

                        column.Item().PaddingTop(10).Text("Bulletin de remboursement des frais de soins à déposer auprès de la CNAM dans un délai de 60 jours.").FontSize(8).Italic().FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
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
