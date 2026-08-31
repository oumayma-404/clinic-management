using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.ProcedureTypes.Queries;

/// <summary>
/// The categories to offer when filing or filtering an act: the twelve suggested disciplines, plus every category
/// this clinic has invented for itself.
/// <para>
/// <b>The union is the point.</b> The category is open text, which is what makes it useful — a practice can file
/// work under « Occlusodontie » without waiting for a release — and also what makes it fragile: the second admin
/// to reach for that category will retype it, and « occlusodontie » is a different group from « Occlusodontie »
/// to everything that reads it. Serving the clinic's own labels back means the second admin picks instead of
/// types, so an open set converges on itself rather than shredding. (The typing case is still covered —
/// <see cref="ProcedureTypeCategories.Normalize"/> folds a variant back on write — but a list that already
/// contains the answer is the cheaper fix.)
/// </para>
/// <para>
/// It is also why this is a query rather than a constant shipped to the browser: the canonical twelve could have
/// been duplicated client-side, but the clinic's own categories are data and only the server knows them.
/// </para>
/// </summary>
public class GetProcedureTypeCategoriesQuery : IRequest<Result<List<string>>>
{
}

public class GetProcedureTypeCategoriesQueryHandler
    : IRequestHandler<GetProcedureTypeCategoriesQuery, Result<List<string>>>
{
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetProcedureTypeCategoriesQueryHandler> _logger;

    public GetProcedureTypeCategoriesQueryHandler(
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetProcedureTypeCategoriesQueryHandler> logger)
    {
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<string>>> Handle(
        GetProcedureTypeCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<string>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var inUse = await _procedureTypeRepository.GetCategoriesAsync(clinicResult.Value, cancellationToken);

            // The canonical twelve first, in clinical order — the order a course of treatment runs, which is how
            // the act pickers group and how a dentist scans. The clinic's own categories follow alphabetically:
            // they have no clinically meaningful position, and appending them keeps the familiar twelve where
            // they were rather than shuffling on the day someone adds a thirteenth.
            var categories = new List<string>(ProcedureTypeCategories.Canonical);
            categories.AddRange(inUse
                .Where(c => !ProcedureTypeCategories.IsCanonical(c))
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));

            return Result<List<string>>.Success(categories);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error retrieving procedure type categories");
            return Result<List<string>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
