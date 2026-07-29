using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>List the clinic's invoices for the Recettes view, filtered by period / patient / status.</summary>
public class GetInvoicesQuery : IRequest<Result<List<InvoiceDto>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? PatientId { get; set; }
    public string? Status { get; set; }
}

public class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, Result<List<InvoiceDto>>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoicesQueryHandler> _logger;

    public GetInvoicesQueryHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoicesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<InvoiceDto>>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            InvoiceStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<InvoiceStatus>(request.Status, ignoreCase: true, out var parsed))
                {
                    return Result<List<InvoiceDto>>.Failure("Statut de facture invalide.");
                }
                status = parsed;
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<InvoiceDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var invoices = await _invoiceRepository.GetFilteredAsync(
                clinicId, request.From, request.To, request.PatientId, status, cancellationToken);

            // One query for patient names, mapped by id (a user's clinic patient set is small).
            // includeArchived: this resolves NAMES, it is not a picker. An archived patient's invoices must
            // still show who they belong to.
            var patients = await _patientRepository.GetByClinicIdAsync(
                clinicId, includeArchived: true, cancellationToken: cancellationToken);
            var names = patients.ToDictionary(p => p.Id, p => p.GetFullName());

            // One grouped read for the credited totals — the row badges an avoir without an N+1. The avoirs
            // themselves are not loaded here; only the detail modal needs them.
            var credited = await _creditNoteRepository.GetTotalsForInvoicesAsync(
                invoices.Select(i => i.Id).ToList(), cancellationToken);

            var dtos = invoices
                .Select(i =>
                {
                    var dto = i.ToDto(names.TryGetValue(i.PatientId, out var name) ? name : null);
                    dto.CreditedTotal = credited.TryGetValue(i.Id, out var total) ? total : 0m;
                    return dto;
                })
                .ToList();

            return Result<List<InvoiceDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error listing invoices");
            return Result<List<InvoiceDto>>.Failure("Erreur lors de la récupération des factures.");
        }
    }
}
