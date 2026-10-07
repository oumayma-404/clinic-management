using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>« Retirer ce PC » (AC-8.1): the copy stops, the clinic may set up another. The copy stays on that PC until erased there.</summary>
public sealed record RetireRelayCommand : IRequest<Result<RelayStatusDto>>;

public sealed class RetireRelayCommandHandler : IRequestHandler<RetireRelayCommand, Result<RelayStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRelayRowStore _rows;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _actor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RetireRelayCommandHandler> _logger;

    public RetireRelayCommandHandler(
        IClinicContext clinicContext,
        IUserRepository users,
        IClinicRelayRepository relays,
        IClinicRelayRowStore rows,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider actor,
        IUnitOfWork unitOfWork,
        ILogger<RetireRelayCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _rows = rows;
        _auditEntries = auditEntries;
        _actor = actor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayStatusDto>> Handle(RetireRelayCommand request, CancellationToken cancellationToken)
    {
        var admin = await RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayStatusDto>.FailureFrom(admin);
        }

        try
        {
            var relay = await _relays.GetCurrentForClinicAsync(admin.Value!.ClinicId, cancellationToken);
            if (relay is null)
            {
                return Result<RelayStatusDto>.Failure(RelayRefusals.NoRelay, RelayRefusals.NoRelayCode);
            }

            var now = DateTime.UtcNow;
            relay.Retire(ClinicRelayRetirement.Retired, admin.Value.Id, now);
            await RelayJournal.StageAsync(_auditEntries, _actor.Current, relay, AuditAction.Update, RelayJournal.Retire,
                now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _rows.DropCursorAsync(relay.ClinicId, cancellationToken);

            return Result<RelayStatusDto>.Success(Queries.GetRelayStatusQueryHandler.ToDto(relay, now));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Retiring the PC de secours of clinic {ClinicId} failed", admin.Value!.ClinicId);
            return Result<RelayStatusDto>.Failure("Le PC de secours n'a pas pu être retiré. Réessayez.");
        }
    }
}
