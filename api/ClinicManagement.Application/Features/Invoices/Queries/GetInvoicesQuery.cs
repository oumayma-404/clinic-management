using MediatR;
using Microsoft.Extensions.Logging;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoicesQueryHandler> _logger;

    public GetInvoicesQueryHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoicesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
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
            var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken);
            var names = patients.ToDictionary(p => p.Id, p => p.GetFullName());

            var dtos = invoices
                .Select(i => i.ToDto(names.TryGetValue(i.PatientId, out var name) ? name : null))
                .ToList();

            return Result<List<InvoiceDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing invoices");
            return Result<List<InvoiceDto>>.Failure("Erreur lors de la récupération des factures.");
        }
    }
}
