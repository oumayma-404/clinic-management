using ClinicManagement.Application.Common;
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

    // Likewise the arrêt de travail, stamped onto the official CNAM P 061 form (L11). Two overlay types now, and
    // they stay two separate renderers: the forms share nothing but their pattern — different asset, different
    // page geometry, and every one of ~30 coordinates different.
    private const string ArretTravailType = DocumentTypes.ArretTravail;

    // Documents are Tunisian: French month names + TND formatting come from a fixed fr-FR culture, never
    // the ambient thread culture (which may be invariant/en in the background PDF job).
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// A stored instant as the calendar day the <b>clinic</b> was in — the one date authority every money
    /// document renders through (J3).
    ///
    /// <para>
    /// Every date on these documents is a UTC instant, and the renderer used to format it raw. Tunisia is
    /// UTC+1, so for the first hour of every clinic day the raw instant is still on the *previous* date: a note
    /// d'honoraires issued at 00h30 on 1 January was numbered <c>2027-0001</c> (the sequence takes its year from
    /// <see cref="ClinicClock.ClinicYear"/>) and printed « Le 31/12/2026 ». A 2027 number on a 2026-dated
    /// document is what an accountant rejects, and a document's number is legal identity — there is no
    /// correcting it afterwards. The same split existed on the avoir and the devis.
    /// </para>
    /// <para>
    /// ⚠️ The fix is deliberately here and <b>not</b> in what <c>Invoice.Issue</c> stores. The stored instant is
    /// already right: every money read buckets on it through <c>ClinicClock</c>'s local-day bounds, so a note
    /// issued at 00h30 Tunis on 1 January already books into January. Storing a clinic-local wall-clock value
    /// instead would make the print agree at the cost of shifting the instant by an hour — which
    /// <c>ApplicationDbContext</c> (Unspecified is written as UTC) would bake in, moving which month every past
    /// note books into. Rendering is the half that was wrong; reading was not.
    /// </para>
    /// <para>
    /// A date the user typed is unaffected: it arrives as midnight, and midnight UTC is 01:00 on the same
    /// clinic day.
    /// </para>
    /// </summary>
    private static string FrDay(DateTime instant) =>
        ClinicClock.ToClinicLocal(instant).ToString("dd/MM/yyyy", FrCulture);

    private readonly ILogger<PdfGenerationService> _logger;
    private readonly IFileStorage _fileStorage;
    private readonly CnamBs1BulletinRenderer _bs1Renderer;
    private readonly CnamArretTravailRenderer _arretTravailRenderer;

    public PdfGenerationService(ILogger<PdfGenerationService> logger, IFileStorage fileStorage)
    {
        _logger = logger;
        _fileStorage = fileStorage;
        // Pass the logger so the BS1 renderer can surface a Warning when it silently drops malformed acts.
        _bs1Renderer = new CnamBs1BulletinRenderer(logger);
        _arretTravailRenderer = new CnamArretTravailRenderer();
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

            // arret-travail is stamped onto the genuine CNAM P 061 form (same contract: fails fast if the asset is
            // missing, rather than falling through to the generic renderer — a free-text « certificat » in place of
            // the official form is precisely what the caisse refuses, and it would look like a success).
            if (string.Equals(documentData.DocumentType, ArretTravailType, StringComparison.OrdinalIgnoreCase))
            {
                var arretBytes = await Task.Run(() => _arretTravailRenderer.Render(documentData), cancellationToken);
                _logger.LogInformation("P61 arrêt de travail PDF generated successfully, size: {Size} bytes", arretBytes.Length);
                return arretBytes;
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

                                    // The patient's address, when they have one (J10). Conditional, not blank:
                                    // it is not legally required of a private patient, so an addressless patient
                                    // gets one line fewer rather than an empty « Adresse : » the reader has to
                                    // interpret.
                                    if (!string.IsNullOrWhiteSpace(data.PatientAddress))
                                    {
                                        col.Item().Text(data.PatientAddress).FontSize(10)
                                            .FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
                                    }
                                });
                                row.RelativeItem().AlignRight().Text($"Le {FrDay(data.IssueDate)}").FontSize(11).FontFamily("Helvetica");
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

                            column.Item().ExtendVertical();
                        });

                        page.Footer().PaddingTop(20).Column(footer =>
                        {
                            footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);

                            // Gated on the timbre actually applying (J10). The mention was unconditional while the
                            // timbre *line* above it is conditional, so a note issued by a clinic with the timbre
                            // switched off asserted a droit de timbre it had neither charged nor collected — the
                            // document contradicted its own totals. The currency half of the sentence is true
                            // either way, so only the timbre clause is conditional.
                            var mention = data.StampDutyAmount > 0m
                                ? "Note d'honoraires soumise au timbre fiscal — Montants exprimés en dinars tunisiens (DT)."
                                : "Montants exprimés en dinars tunisiens (DT).";
                            footer.Item().PaddingTop(6).Text(mention)
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
                                row.RelativeItem().AlignRight().Text($"Le {FrDay(data.Date)}").FontSize(11).FontFamily("Helvetica");
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
                                        table.Cell().Element(BodyCell).Text(FrDay(installment.DueDate));
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
            // FR-4.4 — the document it is FOR, not who it is for. `Reference` names the invoice or the
            // échéance precisely, which is what a reader chasing a failed render actually needs.
            _logger.LogInformation("Generating payment receipt PDF for {Reference}", data.Reference ?? "(none)");

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
                                    .Text($"REÇU ANNULÉ{(data.VoidedOn.HasValue ? $" LE {FrDay(data.VoidedOn.Value)}" : "")}")
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

                                Row("Date", FrDay(data.PaidOn));
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
            _logger.LogError(ex, "Error generating receipt PDF for {Reference}", data.Reference ?? "(none)");
            throw;
        }
    }

    public async Task<byte[]> GenerateAvoirPdfAsync(AvoirPdfData data, CancellationToken cancellationToken = default)
    {
        try
        {
            // The avoir's own number identifies it uniquely and per clinic — there was never anything the
            // patient's name added here that the document number does not.
            _logger.LogInformation("Generating avoir PDF {Number}", data.Number);

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

                                Row("Date d'établissement", FrDay(data.IssueDate));
                                Row("Date de remboursement", FrDay(data.RefundedOn));
                                Row("Patient", data.PatientName);

                                // Mandatory on an avoir: the document it corrects. Rendering it blank would
                                // make the piece unusable, so say so explicitly rather than leave a gap.
                                var invoiceRef = string.IsNullOrWhiteSpace(data.InvoiceNumber)
                                    ? "Facture non numérotée"
                                    : data.InvoiceIssueDate.HasValue
                                        ? $"N° {data.InvoiceNumber} du {FrDay(data.InvoiceIssueDate.Value)}"
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
                                // otherwise the single TTC figure is the honest presentation. The **timbre** is a
                                // separate line for the same reason it is a separate field (J6): it sits outside
                                // the VAT base, so folding it into HT would make the printed TVA disagree with
                                // the TVA the note actually charged.
                                if (data.VatApplicable && data.VatRate > 0m)
                                {
                                    totals.Item().Text($"Montant HT : {FormatDt(data.AmountHt)}").FontSize(11).FontFamily("Helvetica");
                                    totals.Item().Text($"TVA ({data.VatRate:0.##} %) : {FormatDt(data.AmountVat)}").FontSize(11).FontFamily("Helvetica");
                                }
                                if (data.AmountStamp > 0m)
                                {
                                    totals.Item().Text($"Timbre fiscal : {FormatDt(data.AmountStamp)}").FontSize(11).FontFamily("Helvetica");
                                }
                                totals.Item().PaddingTop(3).Text($"Montant remboursé : {FormatDt(data.AmountTtc)}")
                                    .FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                            });

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

                // Every prescriber/cabinet line comes from DocumentIdentity, so the ordonnance, the certificat,
                // the lettre de liaison and the bulletin all identify the practitioner the same way — including
                // the CNOMDT number an ordonnance is legally required to carry and used to omit entirely.
                foreach (var line in DocumentIdentity.PrescriberLines(data))
                {
                    column.Item().Text(line).FontSize(11).FontFamily("Helvetica");
                }
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

                // DocumentIdentity owns which patient lines a document must carry and in what order (nom, date
                // de naissance, sexe, poids, médecin traitant). Laid out two per row so the block stays compact
                // however many lines it yields; the patient's name is emphasised as the first one.
                var lines = DocumentIdentity.PatientLines(data);
                for (var index = 0; index < lines.Count; index += 2)
                {
                    var left = lines[index];
                    var right = index + 1 < lines.Count ? lines[index + 1] : null;

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Element(c => RenderIdentityLine(c, left, bold: index == 0));
                        if (right != null)
                        {
                            row.RelativeItem().Element(c => RenderIdentityLine(c, right, bold: false));
                        }
                        else
                        {
                            // Keep the grid honest: an odd final line must not stretch across both columns.
                            row.RelativeItem();
                        }
                    });
                }
            });
        };
    }

    /// <summary>Renders one <see cref="IdentityLine"/> as a small grey label above its value.</summary>
    private static void RenderIdentityLine(IContainer container, IdentityLine line, bool bold)
    {
        container.Column(col =>
        {
            col.Item().Text(line.Label).FontSize(9).FontColor(Colors.Grey.Darken2).FontFamily("Helvetica");
            var value = col.Item().Text(line.Value).FontSize(12).FontFamily("Helvetica");
            if (bold)
            {
                value.Bold();
            }
        });
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
                        // PrescriptionContent owns the whole body — the per-line norm formatting (DCI, posologie,
                        // voie, quantité, durée) and the per-ordonnance renewal mention. It also absorbs the
                        // legacy-string and malformed-JSON fallbacks that used to be three catch/else branches here.
                        var prescription = PrescriptionContent.Build(data.Content);
                        column.Item().PaddingBottom(8).Text("Prescription:").FontSize(12).Bold().FontFamily("Helvetica");

                        if (prescription.Lines.Count == 0)
                        {
                            column.Item().PaddingBottom(4).Text("Aucune prescription").FontSize(11).FontFamily("Helvetica");
                        }
                        else
                        {
                            foreach (var line in prescription.Lines)
                            {
                                column.Item().PaddingBottom(4).Text(line.Text).FontSize(11).FontFamily("Helvetica");
                            }
                        }

                        // Governs the document, so it renders once below the lines rather than against one of them.
                        if (prescription.RenewalMention != null)
                        {
                            column.Item().PaddingTop(6).Text(prescription.RenewalMention)
                                .FontSize(11).Italic().FontFamily("Helvetica");
                        }
                        break;

                    case DocumentTypes.Liaison:
                        // Render only the filled sections, in the norm reading order LiaisonContent declares.
                        // The free-text body is one of them (unlabelled) and no longer excludes the guided
                        // fields — a letter routinely carries prose AND structured norm sections.
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
                        // Free objet/motif body + optional repos clause. The practitioner's ordre number and the
                        // cabinet address are NOT passed here any more — DocumentIdentity renders them in the
                        // shared header for every document type, so the attestation formula names the registering
                        // body without restating the number.
                        var objetMotif = data.Content.GetValueOrDefault("objetMotif", "");
                        var startDate = data.Content.GetValueOrDefault("startDate", "");
                        var duration = data.Content.GetValueOrDefault("duration", "");

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
                            data.DoctorName, data.DoctorSpecialty,
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
