using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Audit;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>
/// One line of « Modifications à vérifier » or « À reprendre » (<c>clinic-pc-copy</c> AC-5.6, AC-7.4): what the record
/// is, why it is listed, each side's version in one French line (and as JSON), who changed it on each side.
/// </summary>
public sealed record RelayReviewItemDto(
    Guid Id,
    string Kind,
    string KindLabel,
    string Table,
    string TableLabel,
    string EntityKey,
    string? CloudEntityKey,
    string? CloudSummary,
    string? CabinetSummary,
    string? Warning,
    string? CloudVersion,
    string? CabinetVersion,
    DateTime? CloudChangedAtUtc,
    string? CloudChangedBy,
    DateTime? CabinetChangedAtUtc,
    string? CabinetChangedBy,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc);

/// <summary>The French names of the two lists' lines — server-side, like the journal's own labels.</summary>
public static class RelayReviewLabels
{
    public static string Kind(RelayReviewKind kind) => kind switch
    {
        RelayReviewKind.CloudOnly => "Modifié dans le cloud juste avant la coupure",
        RelayReviewKind.BothChanged => "Modifié des deux côtés — la version du cabinet est gardée",
        RelayReviewKind.ProbableDuplicate => "Enregistré deux fois — doublon probable",
        RelayReviewKind.ToReEnter => "Enregistré sur le PC de secours, jamais arrivé dans le cloud",
        RelayReviewKind.KeptAfterRestore => "Perdu par la restauration du cloud puis modifié dans le cloud — la version du cloud est gardée",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>AC-5.6's bell row: the number of lines nobody has marked « Vu ».</summary>
    public static string BellMessage(int pending) =>
        pending == 1
            ? "1 modification faite dans le cloud juste avant la coupure est à vérifier."
            : $"{pending} modifications faites dans le cloud juste avant la coupure sont à vérifier.";

    /// <summary>AC-7.4's bell row: the number of records nobody has marked « Repris ».</summary>
    public static string ReEnterBellMessage(int pending) =>
        pending == 1
            ? "1 enregistrement fait sur le PC de secours pendant la coupure est à reprendre dans le cloud."
            : $"{pending} enregistrements faits sur le PC de secours pendant la coupure sont à reprendre dans le cloud.";

    public static RelayReviewItemDto ToDto(RelayReviewItem item, IReadOnlyDictionary<Guid, string> patientNames) => new(
        item.Id, item.Kind.ToString(), Kind(item.Kind), item.Table, AuditLabels.Entity(item.Table), item.EntityKey,
        item.CloudEntityKey,
        RelayRecordSummary.Describe(item.Table, item.CloudVersion, patientNames),
        RelayRecordSummary.Describe(item.Table, item.CabinetVersion, patientNames),
        RelayRecordSummary.Warning(item.Kind, item.Table, item.CabinetVersion),
        item.CloudVersion, item.CabinetVersion, item.CloudChangedAtUtc, item.CloudChangedBy,
        item.CabinetChangedAtUtc, item.CabinetChangedBy, item.CreatedAtUtc, item.ReviewedAtUtc);

    /// <summary>The names of the patients a set of lines names, in one read.</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> PatientNamesAsync(
        IPatientRepository patients, Guid clinicId, IEnumerable<RelayReviewItem> items, CancellationToken cancellationToken)
    {
        var ids = items
            .SelectMany(i => RelayRecordSummary.PatientIdsOf(i.CloudVersion).Concat(RelayRecordSummary.PatientIdsOf(i.CabinetVersion)))
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = await patients.GetByIdsAsync(clinicId, ids, cancellationToken);
        return found.ToDictionary(p => p.Key, p => p.Value.GetFullName());
    }
}

/// <summary>One of the two lists, newest first — the lines already marked only when asked for.</summary>
public sealed record GetRelayReviewItemsQuery(bool ReEnter, bool IncludeReviewed, int? Page, int? PageSize)
    : IRequest<Result<PagedResult<RelayReviewItemDto>>>;

public sealed class GetRelayReviewItemsQueryHandler
    : IRequestHandler<GetRelayReviewItemsQuery, Result<PagedResult<RelayReviewItemDto>>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IRelayReviewItemRepository _items;
    private readonly IPatientRepository _patients;

    public GetRelayReviewItemsQueryHandler(
        IClinicContext clinicContext, IUserRepository users, IRelayReviewItemRepository items, IPatientRepository patients)
    {
        _clinicContext = clinicContext;
        _users = users;
        _items = items;
        _patients = patients;
    }

    public async Task<Result<PagedResult<RelayReviewItemDto>>> Handle(
        GetRelayReviewItemsQuery request, CancellationToken cancellationToken)
    {
        var admin = await Commands.RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<PagedResult<RelayReviewItemDto>>.FailureFrom(admin);
        }

        var clinicId = admin.Value!.ClinicId;
        var page = await _items.GetPageAsync(clinicId, request.ReEnter, request.IncludeReviewed,
            PageRequest.From(request.Page, request.PageSize) ?? PageRequest.Of(1, 50), cancellationToken);
        var names = await RelayReviewLabels.PatientNamesAsync(_patients, clinicId, page.Items, cancellationToken);
        return Result<PagedResult<RelayReviewItemDto>>.Success(page.Map(i => RelayReviewLabels.ToDto(i, names)));
    }
}
