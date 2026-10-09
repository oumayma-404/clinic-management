using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// « Vu » on a line of « Modifications à vérifier » (AC-5.6), or « Repris » on one of « À reprendre » (AC-7.4): an admin
/// read both versions, or entered the record again on the cloud. It changes no record — a
/// correction is made through the ordinary screens — and a second press keeps the first reader.
/// </summary>
public sealed record MarkRelayReviewItemSeenCommand(Guid Id) : IRequest<Result<RelayReviewItemDto>>;

public sealed class MarkRelayReviewItemSeenCommandHandler : IRequestHandler<MarkRelayReviewItemSeenCommand, Result<RelayReviewItemDto>>
{
    public const string NotFoundCode = "relay_review_not_found";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IRelayReviewItemRepository _items;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPatientRepository _patients;

    public MarkRelayReviewItemSeenCommandHandler(
        IClinicContext clinicContext, IUserRepository users, IRelayReviewItemRepository items, IUnitOfWork unitOfWork,
        IPatientRepository patients)
    {
        _clinicContext = clinicContext;
        _users = users;
        _items = items;
        _unitOfWork = unitOfWork;
        _patients = patients;
    }

    public async Task<Result<RelayReviewItemDto>> Handle(MarkRelayReviewItemSeenCommand request, CancellationToken cancellationToken)
    {
        var admin = await RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayReviewItemDto>.FailureFrom(admin);
        }

        var item = await _items.GetByIdAsync(admin.Value!.ClinicId, request.Id, cancellationToken);
        if (item is null)
        {
            return Result<RelayReviewItemDto>.Failure("Cette modification à vérifier n'existe pas.", NotFoundCode);
        }

        item.MarkReviewed(admin.Value.Id, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var names = await RelayReviewLabels.PatientNamesAsync(_patients, item.ClinicId, new[] { item }, cancellationToken);
        return Result<RelayReviewItemDto>.Success(RelayReviewLabels.ToDto(item, names));
    }
}
