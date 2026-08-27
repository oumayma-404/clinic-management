using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>List the clinic's invoices for the Recettes view, filtered by period / patient / status.</summary>
public class GetInvoicesQuery : IRequest<Result<PagedResult<InvoiceDto>>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? PatientId { get; set; }
    public string? Status { get; set; }
    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// L9 — only the notes attributed to this practitioner. Applied in SQL, like every other filter here: in the
    /// handler it would mean « hers among these 25 », hiding her invoices on every other page.
    /// </summary>
    public Guid? DoctorId { get; set; }

}

public class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, Result<PagedResult<InvoiceDto>>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoicesQueryHandler> _logger;

    public GetInvoicesQueryHandler(
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICreditNoteRepository creditNoteRepository,
        IDoctorRepository doctorRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoicesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _creditNoteRepository = creditNoteRepository;
        _doctorRepository = doctorRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<InvoiceDto>>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            InvoiceStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<InvoiceStatus>(request.Status, ignoreCase: true, out var parsed))
                {
                    return Result<PagedResult<InvoiceDto>>.Failure("Statut de facture invalide.");
                }
                status = parsed;
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<InvoiceDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var page = await _invoiceRepository.GetFilteredAsync(
                clinicId,
                request.From,
                request.To,
                request.PatientId,
                status,
                request.SearchTerm,
                PageRequest.From(request.Page, request.PageSize),
                request.DoctorId,
                cancellationToken: cancellationToken);
            var invoices = page.Items;

            // Patient names for the rows on this page only, via the batched read. This used to load EVERY
            // patient of the clinic — with their flags and both history collections — to build a name lookup,
            // which meant paginating the invoices bought nothing: the unbounded read simply moved next door.
            // `GetByIdsAsync` includes archived patients, which is what this needs: it resolves names, it is not
            // a picker, and an archived patient's invoices must still show who they belong to.
            var names = (await _patientRepository.GetByIdsAsync(
                    clinicId,
                    invoices.Select(i => i.PatientId).Distinct().ToList(),
                    cancellationToken))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetFullName());

            // One grouped read for the credited totals — the row badges an avoir without an N+1. The avoirs
            // themselves are not loaded here; only the detail modal needs them.
            var credited = await _creditNoteRepository.GetTotalsForInvoicesAsync(
                invoices.Select(i => i.Id).ToList(), cancellationToken);

            // L9 — practitioner names, once for the whole clinic rather than once per row. The roster of a dental
            // practice is a handful of rows, so this is cheaper than a batched-by-id read would be and it also
            // resolves a name for a row whose practitioner no longer appears on this page.
            var doctorNames = (await _doctorRepository.GetByClinicIdAsync(clinicId, cancellationToken))
                .ToDictionary(d => d.Id, d => d.FullName);

            var dtos = page.Map(i =>
            {
                var dto = i.ToDto(
                    names.TryGetValue(i.PatientId, out var name) ? name : null,
                    doctorName: i.DoctorId is { } docId && doctorNames.TryGetValue(docId, out var docName)
                        ? docName
                        : null);
                dto.CreditedTotal = credited.TryGetValue(i.Id, out var total) ? total : 0m;
                return dto;
            });

            return Result<PagedResult<InvoiceDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error listing invoices");
            return Result<PagedResult<InvoiceDto>>.Failure("Erreur lors de la récupération des factures.");
        }
    }
}
