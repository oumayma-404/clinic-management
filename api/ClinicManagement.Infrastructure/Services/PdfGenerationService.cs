using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

public class PdfGenerationService : IPdfGenerationService
{
    // The bulletin-cnam type is overlaid onto the official CNAM BS1 form instead of the QuestPDF
    // renderer used by every other document type.
    private const string BulletinCnamType = DocumentTypes.BulletinCnam;

    // Documents are Tunisian: French month names + TND formatting come from a fixed fr-FR culture, never
    // the ambient thread culture (which may be invariant/en in the background PDF job).
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    private readonly ILogger<PdfGenerationService> _logger;
    private readonly IFileStorage _fileStorage;
    private readonly CnamBs1BulletinRenderer _bs1Renderer;

    public PdfGenerationService(ILogger<PdfGenerationService> logger, IFileStorage fileStorage)
    {
        _logger = logger;
        _fileStorage = fileStorage;
        // Pass the logger so the BS1 renderer can surface a Warning when it silently drops malformed acts.
        _bs1Renderer = new CnamBs1BulletinRenderer(logger);
        // Set QuestPDF license (free for non-commercial use, or use your license key)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GeneratePdfFromDocumentDataAsync(MedicalDocumentPdfData documentData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating PDF for document type: {DocumentType}", documentData.DocumentType);

            // FR-1.4: the "note d'honoraires" type is retired (compliant honoraires are issued as Invoices).
            // Reject it explicitly so a legacy/hand-crafted honoraires request fails loudly instead of
            // rendering a document titled "NOTE D'HONORAIRES" with an empty body.
            if (string.Equals(documentData.DocumentType, DocumentTypes.Honoraires, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Le type « note d'honoraires » n'est plus pris en charge. Créez une facture depuis le module Factures.");
            }

            // bulletin-cnam is stamped onto the genuine BS1 form (fails fast if the asset is missing).
            if (string.Equals(documentData.DocumentType, BulletinCnamType, StringComparison.OrdinalIgnoreCase))
            {
                var bulletinBytes = await Task.Run(() => _bs1Renderer.Render(documentData), cancellationToken);
                _logger.LogInformation("BS1 bulletin PDF generated successfully, size: {Size} bytes", bulletinBytes.Length);
                return bulletinBytes;
            }

            // FR-3.2: load the practitioner cachet blob (if snapshotted) before entering the sync render.
            // A missing/deleted blob or any storage error falls back to the plain signature line — never fails.
            var cachetImage = await LoadCachetImageAsync(documentData, cancellationToken);

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
                                if (documentData.DocumentType == DocumentTypes.Liaison && !string.IsNullOrEmpty(documentData.RecipientDoctorName))
                                {
                                    column.Item().Element(ComposeRecipient(documentData));
                                }

                                // Place + date (FR-6.1): the cabinet city (never a hardcoded "Paris"), with
                                // French month names forced via fr-FR. Falls back to "Le …" when no city is set.
                                var dateStr = documentData.DocumentDate.ToString("dd MMMM yyyy", FrCulture);
                                var placeLine = !string.IsNullOrWhiteSpace(documentData.ClinicCity)
                                    ? $"{documentData.ClinicCity}, le {dateStr}"
                                    : $"Le {dateStr}";
                                column.Item().PaddingBottom(10).AlignRight().Text(placeLine)
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
                            .Element(ComposeSignature(documentData, cachetImage));
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

                                // Payment state (montant réglé / reste à payer) — kept off a cancelled note.
                                if (!data.IsCancelled)
                                {
                                    totals.Item().PaddingTop(3).Text($"Montant réglé : {FormatDt(data.AmountCollected)}").FontSize(11).FontFamily("Helvetica");
                                    totals.Item().Text($"Reste à payer : {FormatDt(data.Outstanding)}").FontSize(11).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                                }

                                // Indicative CNAM split — shown only when there is a reimbursable portion.
                                if (data.CnamReimbursable > 0)
                                {
                                    totals.Item().PaddingTop(3).Text($"Dont estimation CNAM : {FormatDt(data.CnamReimbursable)}").FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                                    totals.Item().Text($"Reste à charge patient : {FormatDt(data.PatientOutOfPocket)}").FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                                }
                            });

                            // TTN « cachet électronique visible » — only present once the invoice is validated (FR-7).
                            if (data.QrCodePng != null && data.QrCodePng.Length > 0)
                            {
                                column.Item().PaddingTop(12).Row(qrRow =>
                                {
                                    qrRow.ConstantItem(110).Image(data.QrCodePng);
                                    qrRow.RelativeItem().PaddingLeft(12).AlignMiddle().Column(info =>
                                    {
                                        info.Spacing(2);
                                        info.Item().Text("Cachet électronique — TTN « El Fatoora »").FontSize(9).Bold().FontFamily("Helvetica");
                                        if (!string.IsNullOrWhiteSpace(data.TtnIdentifier))
                                            info.Item().Text($"Référence TTN : {data.TtnIdentifier}").FontSize(9).FontFamily("Helvetica");
                                        info.Item().Text("Facture électronique enregistrée auprès de TTN.").FontSize(8).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                                    });
                                });
                            }

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

    public async Task<byte[]> GenerateDevisPdfAsync(DevisPdfData data, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating devis PDF for plan {Number}", data.Number ?? "(brouillon)");

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

                            // Clinic identity header
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

                            column.Item().PaddingTop(4).AlignCenter().Text("DEVIS").FontSize(16).Bold().FontFamily("Helvetica");

                            // Number + date + patient + title
                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    if (!string.IsNullOrWhiteSpace(data.Number))
                                        col.Item().Text($"N° {data.Number}").FontSize(12).Bold().FontFamily("Helvetica");
                                    col.Item().Text($"Patient : {data.PatientName}").FontSize(11).FontFamily("Helvetica");
                                    if (!string.IsNullOrWhiteSpace(data.Title))
                                        col.Item().Text($"Plan : {data.Title}").FontSize(11).FontFamily("Helvetica");
                                });
                                row.RelativeItem().AlignRight().Text($"Le {data.Date:dd/MM/yyyy}").FontSize(11).FontFamily("Helvetica");
                            });

                            // Act lines table
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2);
                                    cols.RelativeColumn(5);
                                    cols.RelativeColumn(2);
                                    cols.RelativeColumn(2);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(HeaderCell).Text("Code");
                                    h.Cell().Element(HeaderCell).Text("Désignation");
                                    h.Cell().Element(HeaderCell).Text("Dents");
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Coût prévu");
                                });

                                foreach (var line in data.Lines)
                                {
                                    table.Cell().Element(BodyCell).Text(line.CodeActe ?? string.Empty);
                                    table.Cell().Element(BodyCell).Text(line.Designation);
                                    table.Cell().Element(BodyCell).Text(line.Teeth);
                                    table.Cell().Element(BodyCell).AlignRight().Text(FormatDt(line.PlannedCost));
                                }
                            });

                            column.Item().AlignRight().Column(totals =>
                            {
                                totals.Spacing(3);
                                totals.Item().Text($"Total : {FormatDt(data.TotalPlanned)}")
                                    .FontSize(13).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");

                                // Payment state — only once installment payments have been recorded.
                                if (data.AmountPaid > 0)
                                {
                                    totals.Item().Text($"Montant réglé : {FormatDt(data.AmountPaid)}").FontSize(11).FontFamily("Helvetica");
                                    totals.Item().Text($"Reste à payer : {FormatDt(data.Outstanding)}").FontSize(11).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                                }

                                // Indicative CNAM split — shown only when there is a reimbursable portion.
                                if (data.CnamReimbursable > 0)
                                {
                                    totals.Item().Text($"Dont estimation CNAM : {FormatDt(data.CnamReimbursable)}").FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                                    totals.Item().Text($"Reste à charge patient : {FormatDt(data.PatientOutOfPocket)}").FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                                }
                            });

                            // Échéancier
                            if (data.Installments.Count > 0)
                            {
                                column.Item().PaddingTop(8).Text("Échéancier").FontSize(12).Bold().FontFamily("Helvetica");
                                column.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.RelativeColumn(1);
                                        cols.RelativeColumn(3);
                                        cols.RelativeColumn(3);
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Element(HeaderCell).Text("N°");
                                        h.Cell().Element(HeaderCell).Text("Échéance");
                                        h.Cell().Element(HeaderCell).AlignRight().Text("Montant");
                                    });

                                    var index = 1;
                                    foreach (var installment in data.Installments)
                                    {
                                        table.Cell().Element(BodyCell).Text(index.ToString());
                                        table.Cell().Element(BodyCell).Text($"{installment.DueDate:dd/MM/yyyy}");
                                        table.Cell().Element(BodyCell).AlignRight().Text(FormatDt(installment.Amount));
                                        index++;
                                    }
                                });
                            }

                            column.Item().ExtendVertical();
                        });

                        page.Footer().PaddingTop(20).Column(footer =>
                        {
                            footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                            footer.Item().PaddingTop(6).Text("Devis — estimation non contractuelle. Montants exprimés en dinars tunisiens (DT).")
                                .FontSize(8).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                        });
                    });
                }).GeneratePdf();
            }, cancellationToken);

            _logger.LogInformation("Devis PDF generated, size: {Size} bytes", pdfBytes.Length);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating devis PDF for plan {Number}", data.Number ?? "(brouillon)");
            throw;
        }
    }

    public async Task<byte[]> GenerateReceiptPdfAsync(ReceiptPdfData data, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating payment receipt PDF for {Patient}", data.PatientName);

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

                            // Clinic identity header
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

                            column.Item().PaddingTop(4).AlignCenter().Text("REÇU DE PAIEMENT").FontSize(16).Bold().FontFamily("Helvetica");

                            // A voided payment's receipt still renders — the paper is already in the patient's
                            // hands and the clinic needs to reproduce what was handed over — but it is
                            // over-stamped so it can never be reprinted as a clean receipt. Mirrors the
                            // « FACTURE ANNULÉE » banner on the invoice.
                            if (data.IsVoided)
                            {
                                column.Item().PaddingTop(4).AlignCenter()
                                    .Text($"REÇU ANNULÉ{(data.VoidedOn.HasValue ? $" LE {data.VoidedOn.Value:dd/MM/yyyy}" : "")}")
                                    .FontSize(13).Bold().FontColor(Colors.Red.Darken2).FontFamily("Helvetica");

                                if (!string.IsNullOrWhiteSpace(data.VoidReason))
                                {
                                    column.Item().AlignCenter()
                                        .Text($"Motif : {data.VoidReason}")
                                        .FontSize(10).FontColor(Colors.Red.Darken2).FontFamily("Helvetica");
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(data.Reference))
                            {
                                column.Item().AlignCenter().Text($"Réf. {data.Reference}").FontSize(10).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                            }

                            // Fields
                            column.Item().PaddingTop(8).Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(3);
                                    cols.RelativeColumn(5);
                                });

                                void Row(string label, string value)
                                {
                                    table.Cell().Element(BodyCell).Text(label).FontFamily("Helvetica");
                                    table.Cell().Element(BodyCell).Text(value).FontFamily("Helvetica");
                                }

                                Row("Date", $"{data.PaidOn:dd/MM/yyyy}");
                                Row("Patient", data.PatientName);
                                Row("Objet", data.For);
                                Row("Mode de règlement", data.Method);
                            });

                            // Amount received (emphasised) + remaining balance
                            column.Item().PaddingTop(6).AlignRight().Column(totals =>
                            {
                                totals.Spacing(3);
                                totals.Item().Text($"Montant réglé : {FormatDt(data.Amount)}").FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                                totals.Item().Text($"Reste à payer : {FormatDt(data.RemainingBalance)}").FontSize(11).FontFamily("Helvetica");
                            });

                            column.Item().ExtendVertical();
                        });

                        page.Footer().PaddingTop(20).Column(footer =>
                        {
                            footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                            footer.Item().PaddingTop(6).Text("Reçu de paiement — Montants exprimés en dinars tunisiens (DT).")
                                .FontSize(8).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                        });
                    });
                }).GeneratePdf();
            }, cancellationToken);

            _logger.LogInformation("Receipt PDF generated, size: {Size} bytes", pdfBytes.Length);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating receipt PDF for {Patient}", data.PatientName);
            throw;
        }
    }

    public async Task<byte[]> GenerateAvoirPdfAsync(AvoirPdfData data, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating avoir PDF {Number} for {Patient}", data.Number, data.PatientName);

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

                            // Same identity header as the note d'honoraires — an avoir is the fiscal
                            // counterpart of the invoice it corrects, not a lesser note.
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

                            column.Item().PaddingTop(4).AlignCenter().Text("AVOIR").FontSize(16).Bold().FontFamily("Helvetica");
                            column.Item().AlignCenter().Text($"N° {data.Number}").FontSize(12).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");

                            column.Item().PaddingTop(8).Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(3);
                                    cols.RelativeColumn(5);
                                });

                                void Row(string label, string value)
                                {
                                    table.Cell().Element(BodyCell).Text(label).FontFamily("Helvetica");
                                    table.Cell().Element(BodyCell).Text(value).FontFamily("Helvetica");
                                }

                                Row("Date d'établissement", $"{data.IssueDate:dd/MM/yyyy}");
                                Row("Date de remboursement", $"{data.RefundedOn:dd/MM/yyyy}");
                                Row("Patient", data.PatientName);

                                // Mandatory on an avoir: the document it corrects. Rendering it blank would
                                // make the piece unusable, so say so explicitly rather than leave a gap.
                                var invoiceRef = string.IsNullOrWhiteSpace(data.InvoiceNumber)
                                    ? "Facture non numérotée"
                                    : data.InvoiceIssueDate.HasValue
                                        ? $"N° {data.InvoiceNumber} du {data.InvoiceIssueDate.Value:dd/MM/yyyy}"
                                        : $"N° {data.InvoiceNumber}";
                                Row("Facture corrigée", invoiceRef);

                                if (!string.IsNullOrWhiteSpace(data.Method))
                                {
                                    Row("Mode de remboursement", data.Method!);
                                }
                            });

                            column.Item().PaddingTop(6).Column(reason =>
                            {
                                reason.Spacing(3);
                                reason.Item().Text("Motif").FontSize(11).Bold().FontFamily("Helvetica");
                                reason.Item().Text(data.Reason).FontSize(11).FontFamily("Helvetica");
                            });

                            column.Item().PaddingTop(6).AlignRight().Column(totals =>
                            {
                                totals.Spacing(3);
                                // The split is only meaningful when the corrected invoice carried VAT;
                                // otherwise the single TTC figure is the honest presentation.
                                if (data.VatApplicable && data.VatRate > 0m)
                                {
                                    totals.Item().Text($"Montant HT : {FormatDt(data.AmountHt)}").FontSize(11).FontFamily("Helvetica");
                                    totals.Item().Text($"TVA ({data.VatRate:0.##} %) : {FormatDt(data.AmountVat)}").FontSize(11).FontFamily("Helvetica");
                                }
                                totals.Item().PaddingTop(3).Text($"Montant remboursé : {FormatDt(data.AmountTtc)}")
                                    .FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                            });

                            // The avoir is never transmitted to TTN — only the invoice is. Without this line
                            // a clinic would reasonably assume the declared figure had been corrected too.
                            if (data.CorrectedInvoiceIsTtnRegistered)
                            {
                                column.Item().PaddingTop(10).Text(
                                        "La facture corrigée est enregistrée auprès de TTN « El Fatoora ». Cet avoir n'est pas "
                                        + "télétransmis : la régularisation auprès de TTN reste à effectuer par le cabinet.")
                                    .FontSize(9).FontColor(Colors.Red.Darken2).FontFamily("Helvetica");
                            }

                            column.Item().ExtendVertical();
                        });

                        page.Footer().PaddingTop(20).Column(footer =>
                        {
                            footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                            footer.Item().PaddingTop(6).Text("Avoir — Montants exprimés en dinars tunisiens (DT).")
                                .FontSize(8).FontColor(Colors.Grey.Darken1).FontFamily("Helvetica");
                        });
                    });
                }).GeneratePdf();
            }, cancellationToken);

            _logger.LogInformation("Avoir PDF generated, size: {Size} bytes", pdfBytes.Length);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating avoir PDF {Number}", data.Number);
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
                // FR-4.1: the external confrère's free-text address (snapshotted in ContentJson), when present.
                var recipientAddress = data.Content.GetValueOrDefault("recipientAddress", "");
                if (!string.IsNullOrWhiteSpace(recipientAddress))
                {
                    column.Item().Text(recipientAddress).FontSize(11).FontFamily("Helvetica");
                }
            });
        };
    }

    private Action<IContainer> ComposeTitle(string documentType)
    {
        var title = documentType.ToLowerInvariant() switch
        {
            DocumentTypes.Prescription => "ORDONNANCE",
            DocumentTypes.Liaison => "LETTRE DE LIAISON",
            DocumentTypes.Certificat => "CERTIFICAT MÉDICAL",
            // "honoraires" is intentionally absent — the type is retired and rejected before rendering.
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
                    case DocumentTypes.Prescription:
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
                                                // Print the active ingredient(s) / DCI when captured (finding #12).
                                                if (med.Dci != null && med.Dci.Count > 0)
                                                {
                                                    var dciText = string.Join(", ", med.Dci.Where(d => !string.IsNullOrWhiteSpace(d)));
                                                    if (!string.IsNullOrWhiteSpace(dciText))
                                                        medText += $" (DCI : {dciText})";
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

                    case DocumentTypes.Liaison:
                        // FR-4.2: render only the filled guided sections (motif / examen clinique / examen
                        // radiologique / actes réalisés / prescriptions), each under its heading; empty fields
                        // are omitted. A legacy letter's free-text body renders as one unlabelled section.
                        foreach (var section in LiaisonContent.Build(data.Content))
                        {
                            column.Item().Column(sec =>
                            {
                                sec.Spacing(2);
                                if (section.Heading != null)
                                {
                                    sec.Item().Text(section.Heading).FontSize(12).Bold().FontFamily("Helvetica");
                                }
                                sec.Item().Text(section.Body).FontSize(11).FontFamily("Helvetica");
                            });
                        }
                        break;

                    // FR-1.4: the "honoraires" document type is retired (compliant honoraires are issued as
                    // Invoices). The old euro-denominated QuestPDF block is removed; no generic doc renders "€".

                    case DocumentTypes.Certificat:
                        // FR-2: light generalization — free objet/motif body + optional repos clause. The ordre
                        // comes from the authoritative profile snapshot (Part C, key doctorOrdreNumber); fall
                        // back to any legacy typed value for documents created before the snapshot existed.
                        var objetMotif = data.Content.GetValueOrDefault("objetMotif", "");
                        var startDate = data.Content.GetValueOrDefault("startDate", "");
                        var duration = data.Content.GetValueOrDefault("duration", "");
                        var ordreNumber = !string.IsNullOrWhiteSpace(data.DoctorOrdreNumber)
                            ? data.DoctorOrdreNumber
                            : data.Content.GetValueOrDefault("doctorOrderNumber", "");

                        string? startDateFormatted = null;
                        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var startDateParsed))
                        {
                            startDateFormatted = startDateParsed.ToString("dd/MM/yyyy", FrCulture);
                        }

                        string? patientDobFormatted = null;
                        var patientDobStr = data.Content.GetValueOrDefault("patientDateOfBirth", "");
                        if (!string.IsNullOrEmpty(patientDobStr) && DateTime.TryParse(patientDobStr, out var patientDobParsed))
                        {
                            patientDobFormatted = patientDobParsed.ToString("dd/MM/yyyy", FrCulture);
                        }

                        var certificat = CertificatTextBuilder.Build(
                            data.DoctorName, data.DoctorSpecialty, ordreNumber, data.ClinicAddress,
                            data.PatientName, patientDobFormatted, objetMotif, duration, startDateFormatted);

                        foreach (var paragraph in certificat.BodyParagraphs)
                        {
                            column.Item().PaddingVertical(2).Text(paragraph).FontSize(11).FontFamily("Helvetica");
                        }

                        // FR-2.3: the mandatory deontological mention, above the signature block (the footer).
                        column.Item().PaddingTop(12).Text(certificat.Mention).FontSize(11).Italic().FontFamily("Helvetica");
                        break;
                }
            });
        };
    }

    private Action<IContainer> ComposeSignature(MedicalDocumentPdfData data, byte[]? cachetImage)
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
                    // FR-3.2: render the practitioner cachet when present; otherwise the plain line above
                    // stands in as the empty signature area (no error).
                    if (cachetImage != null && cachetImage.Length > 0)
                    {
                        col.Item().AlignRight().Height(70).Image(cachetImage).FitHeight();
                    }
                    col.Item().Text(data.DoctorName).FontSize(12).Bold().FontFamily("Helvetica");
                    col.Item().Text(data.DoctorSpecialty).FontSize(10).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                });
            });
        };
    }

    // FR-3.2 / edge cases: fetch the snapshotted cachet blob. Returns null (→ plain signature line) when no
    // cachet key is snapshotted or the blob is missing/unreadable at render time — never throws.
    private async Task<byte[]?> LoadCachetImageAsync(MedicalDocumentPdfData data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(data.DoctorCachetKey))
        {
            return null;
        }

        try
        {
            await using var stream = await _fileStorage.DownloadAsync(data.DoctorCachetKey, cancellationToken);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            return memory.Length > 0 ? memory.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cachet blob {Key} could not be read; rendering the plain signature line", data.DoctorCachetKey);
            return null;
        }
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

        // Active ingredient(s) / DCI captured by the medication picker (finding #12) — now printed.
        [System.Text.Json.Serialization.JsonPropertyName("dci")]
        public List<string>? Dci { get; set; }
    }
}
