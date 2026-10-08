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
/// One line of « Modifications à vérifier » (<c>clinic-pc-copy</c> AC-5.6): what the record is, why it is listed, the
/// cloud's version and the cabinet's (JSON, as each side held it), and who changed it in the cloud.
/// </summary>
public sealed record RelayReviewItemDto(
    Guid Id,
    string Kind,
    string KindLabel,
    string Table,
    string TableLabel,
    string EntityKey,
    string? CloudEntityKey,
    string? CloudVersion,
    string? CabinetVersion,
    DateTime? CloudChangedAtUtc,
    string? CloudChangedBy,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc);

/// <summary>The French name of why a line is listed — server-side, like the journal's own labels.</summary>
public static class RelayReviewLabels
{
    public static string Kind(RelayReviewKind kind) => kind switch
    {
        RelayReviewKind.CloudOnly => "Modifié dans le cloud juste avant la coupure",
        RelayReviewKind.BothChanged => "Modifié des deux côtés — la version du cabinet est gardée",
        RelayReviewKind.ProbableDuplicate => "Enregistré deux fois — doublon probable",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>AC-5.6's bell row: the number of lines nobody has marked « Vu ».</summary>
    public static string BellMessage(int pending) =>
        pending == 1
            ? "1 modification faite dans le cloud juste avant la coupure est à vérifier."
            : $"{pending} modifications faites dans le cloud juste avant la coupure sont à vérifier.";

    public static RelayReviewItemDto ToDto(RelayReviewItem item) => new(
        item.Id, item.Kind.ToString(), Kind(item.Kind), item.Table, AuditLabels.Entity(item.Table), item.EntityKey,
        item.CloudEntityKey, item.CloudVersion, item.CabinetVersion, item.CloudChangedAtUtc, item.CloudChangedBy,
        item.CreatedAtUtc, item.ReviewedAtUtc);
}

/// <summary>« Modifications à vérifier », newest first — the ones already « Vu » only when asked for.</summary>
public sealed record GetRelayReviewItemsQuery(bool IncludeReviewed, int? Page, int? PageSize)
    : IRequest<Result<PagedResult<RelayReviewItemDto>>>;

public sealed class GetRelayReviewItemsQueryHandler
    : IRequestHandler<GetRelayReviewItemsQuery, Result<PagedResult<RelayReviewItemDto>>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IRelayReviewItemRepository _items;

    public GetRelayReviewItemsQueryHandler(IClinicContext clinicContext, IUserRepository users, IRelayReviewItemRepository items)
    {
        _clinicContext = clinicContext;
        _users = users;
        _items = items;
    }

    public async Task<Result<PagedResult<RelayReviewItemDto>>> Handle(
        GetRelayReviewItemsQuery request, CancellationToken cancellationToken)
    {
        var admin = await Commands.RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<PagedResult<RelayReviewItemDto>>.FailureFrom(admin);
        }

        var page = await _items.GetPageAsync(admin.Value!.ClinicId, request.IncludeReviewed,
            PageRequest.From(request.Page, request.PageSize) ?? PageRequest.Of(1, 50), cancellationToken);
        return Result<PagedResult<RelayReviewItemDto>>.Success(page.Map(RelayReviewLabels.ToDto));
    }
}
