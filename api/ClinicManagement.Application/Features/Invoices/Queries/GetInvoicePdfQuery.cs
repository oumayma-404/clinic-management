using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>Render the note-d'honoraires PDF for an issued invoice.</summary>
public class GetInvoicePdfQuery : IRequest<Result<InvoicePdfResult>>
{
    public Guid Id { get; set; }
}

public class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, Result<InvoicePdfResult>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly IQrCodeGenerator _qrCodeGenerator;
    private readonly ICnamBillingCalculator _cnamBillingCalculator;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoicePdfQueryHandler> _logger;

    public GetInvoicePdfQueryHandler(
        IInvoiceRepository invoiceRepository,
        IClinicRepository clinicRepository,
        IPatientRepository patientRepository,
        IPdfGenerationService pdfGenerationService,
        IQrCodeGenerator qrCodeGenerator,
        ICnamBillingCalculator cnamBillingCalculator,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoicePdfQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicRepository = clinicRepository;
        _patientRepository = patientRepository;
        _pdfGenerationService = pdfGenerationService;
        _qrCodeGenerator = qrCodeGenerator;
        _cnamBillingCalculator = cnamBillingCalculator;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<InvoicePdfResult>> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoicePdfResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<InvoicePdfResult>.Failure("Facture introuvable.");
            }

            if (invoice.Status == InvoiceStatus.Draft || invoice.Number == null)
            {
                return Result<InvoicePdfResult>.Failure("Émettez la facture avant de générer le PDF.");
            }

            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            var patient = await _patientRepository.GetByIdAsync(invoice.PatientId, cancellationToken);

            var data = BuildPdfData(invoice, clinic, patient?.GetFullName() ?? string.Empty, FormatAddress(patient));

            // Indicative CNAM split over the coded lines (reimbursable + out-of-pocket == TTC).
            var careDate = invoice.IssueDate ?? invoice.CreatedAt;
            var cnamLines = invoice.Lines
                .Select(l => new CnamBillingLine(l.DentalActCodeId, l.LineTotalHt))
                .ToList();
            var split = await _cnamBillingCalculator.ComputeAsync(
                cnamLines, invoice.TotalTtc, patient?.DateOfBirth, careDate, cancellationToken);
            data.CnamReimbursable = split.Reimbursable;
            data.PatientOutOfPocket = split.OutOfPocket;

            // FR-7: once validated, stamp the QR « cachet électronique visible » + TTN reference onto the PDF.
            // Degrade gracefully — a QR render failure must not block the (legally-important) invoice PDF, so
            // render without the cachet rather than failing the whole document.
            if (invoice.EInvoiceStatus == EInvoiceStatus.Valid && !string.IsNullOrWhiteSpace(invoice.QrPayload))
            {
                data.TtnIdentifier = invoice.TtnIdentifier;
                try
                {
                    data.QrCodePng = _qrCodeGenerator.GeneratePng(invoice.QrPayload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to render El Fatoora QR for invoice {InvoiceId}; PDF rendered without the cachet.", invoice.Id);
                }
            }

            var bytes = await _pdfGenerationService.GenerateInvoicePdfAsync(data, cancellationToken);

            return Result<InvoicePdfResult>.Success(new InvoicePdfResult
            {
                Content = bytes,
                FileName = $"note-honoraires-{invoice.Number}.pdf"
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error generating PDF for invoice {InvoiceId}", request.Id);
            return Result<InvoicePdfResult>.Failure("Erreur lors de la génération du PDF.");
        }
    }

    /// <summary>
    /// The patient's address as one line — « rue, code postal ville » — or null when they have none (J10).
    /// <para>
    /// Built from the parts that are actually written on a Tunisian envelope. <c>State</c> (the gouvernorat) is
    /// left out because it duplicates the city on the address of almost every patient a cabinet sees, and
    /// <c>Country</c> because a note issued in Tunisia to a Tunisian address does not name the country. Every
    /// part is guarded even though the value object requires the first four: a patient may have **no** address
    /// at all, and a document must render for them rather than throw (an empty result is the same as none).
    /// </para>
    /// </summary>
    private static string? FormatAddress(Patient? patient)
    {
        var address = patient?.Address;
        if (address == null)
        {
            return null;
        }

        var locality = string.Join(" ", new[] { address.ZipCode, address.City }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        var line = string.Join(", ", new[] { address.Street?.Trim(), locality }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private static InvoicePdfData BuildPdfData(
        Invoice invoice, Clinic? clinic, string patientName, string? patientAddress) => new()
    {
        ClinicName = clinic?.Name ?? string.Empty,
        ClinicAddress = clinic?.Address,
        ClinicPhone = clinic?.Phone,
        MatriculeFiscal = clinic?.MatriculeFiscal,
        PatientName = patientName,
        PatientAddress = patientAddress,
        Number = invoice.Number ?? string.Empty,
        IssueDate = invoice.IssueDate ?? invoice.CreatedAt,
        VatApplicable = invoice.VatApplicable,
        VatRate = invoice.VatRate,
        TotalHt = invoice.TotalHt,
        TotalVat = invoice.TotalVat,
        StampDutyAmount = invoice.StampDutyAmount,
        TotalTtc = invoice.TotalTtc,
        AmountCollected = invoice.AmountCollected,
        Outstanding = invoice.Outstanding,
        IsCancelled = invoice.Status == InvoiceStatus.Cancelled,
        Lines = invoice.Lines
            .Select(l => new InvoicePdfLine
            {
                Designation = l.Designation,
                Quantity = l.Quantity,
                UnitPriceHt = l.UnitPriceHt,
                LineTotalHt = l.LineTotalHt
            })
            .ToList()
    };
}
