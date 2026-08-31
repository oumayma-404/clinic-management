using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Queries;

public class GetProcedureTypesQuery : IRequest<Result<PagedResult<ProcedureTypeDto>>>
{
    public bool IncludeInactive { get; set; } = false;

    /// <summary>1-based page and page size. Both null = every matching row.</summary>
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    /// <summary>Free-text filter, matched in SQL across the whole clinic — never only the requested page.</summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Narrow to one clinical discipline. Null / blank = every category, including unfiled acts.
    /// <para>
    /// Matched on the canonical spelling, and an <b>unknown</b> category is deliberately not an error: it simply
    /// matches nothing, the same way the lab-order stage filter ignores a value it does not recognise. A stale
    /// bookmark should show « aucun résultat », not a French failure.
    /// </para>
    /// </summary>
    public string? Category { get; set; }
}

public class GetProcedureTypesQueryHandler : IRequestHandler<GetProcedureTypesQuery, Result<PagedResult<ProcedureTypeDto>>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetProcedureTypesQueryHandler> _logger;

    public GetProcedureTypesQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetProcedureTypesQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PagedResult<ProcedureTypeDto>>> Handle(GetProcedureTypesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Scope explicitly to the caller's clinic so isolation does not hinge on the fail-open global
            // filter (defense-in-depth; the sibling Update/Delete/Create handlers scope the same way).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<ProcedureTypeDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }
            var clinicId = clinicResult.Value;

            // The clinic predicate is a repository argument now, not a Where applied after the read: filtering
            // an already-cut window in memory would have made pages arbitrarily short.
            var page = await _procedureTypeRepository.GetFilteredAsync(
                clinicId,
                request.IncludeInactive,
                request.SearchTerm,
                request.Category,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            return Result<PagedResult<ProcedureTypeDto>>.Success(page.Map(pt => pt.ToDto()));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving procedure types");
            return Result<PagedResult<ProcedureTypeDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}


